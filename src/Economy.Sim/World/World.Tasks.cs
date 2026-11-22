using Economy.Core.Items;
using Economy.Core.Ledger;

namespace Economy.Sim.World;

/// <summary>
/// Town delivery tasks (Ian, 2026-11-19). Each town keeps a stockpile of the
/// raw goods gathered near it and uses some up every day. While a stockpile is
/// below its target, a task pays full market price per unit; once it is full
/// the task closes. Solo players have a small daily cap, parties a bigger one
/// whose pay is split evenly. NPCs always buy raw goods at half price as a
/// fallback, and that stock is resold by NPC traders. Rare wilds materials are
/// for crafters only: no town or NPC takes them.
/// </summary>
public sealed partial class World
{
    private double _taskPoolToday;

    public long TaskUnitsDelivered { get; private set; }
    public long TaskCoinsPaid { get; private set; }

    /// <summary>Takes the delivery share of this hour's contract pool; the rest pays kill bounties.</summary>
    private long SplitTaskPool(long pool)
    {
        if (!Config.DeliveryTasks) return pool;
        long tasks = (long)(pool * (1 - Config.KillTaskShare));
        _taskPoolToday += tasks;
        return pool - tasks;
    }

    /// <summary>
    /// At day end: yesterday's delivery pool sets how much each town uses per
    /// day; the town uses that up from its stockpile and sets its target.
    /// </summary>
    private void OpenTaskBudgets()
    {
        double total = 0;
        foreach (var t in Towns) total += t.Def.Size;
        foreach (var t in Towns)
        {
            t.TaskBudget = _taskPoolToday * t.Def.Size / Math.Max(0.01, total);
            t.TaskBudgetStart = t.TaskBudget;
            if (!Config.TownStockpiles) continue;
            // Split the town's daily use across its goods by what is gathered nearby.
            double value = 0;
            for (int g = 0; g < Goods.Count; g++) if (IsTaskGood(t, g)) value += t.RegenPerHour[g] * t.Markets[g].Reference.Value;
            for (int g = 0; g < Goods.Count; g++)
            {
                if (!IsTaskGood(t, g) || value <= 0) { t.DailyUse[g] = 0; continue; }
                double coins = t.TaskBudget * t.RegenPerHour[g] * t.Markets[g].Reference.Value / value;
                t.DailyUse[g] = coins / Math.Max(1, t.Markets[g].Reference.Value);
                t.Stockpile[g] = Math.Max(0, t.Stockpile[g] - t.DailyUse[g]);
            }
        }
        _taskPoolToday = 0;
        foreach (var a in Agents) a.TaskUnitsToday = 0;
    }

    public bool IsRare(int g) => Goods.Defs[g].Tier == GoodTier.RelicCore || Config.RareGoods.Contains(Goods.Defs[g].Id);

    private bool IsTaskGood(Town t, int g) =>
        Goods.Defs[g].Tier == GoodTier.Raw && !IsRare(g) && t.RegenPerHour[g] > 0;

    /// <summary>Units the town still wants of a good today (stockpile mode), or no limit (budget mode).</summary>
    private int TaskUnitsWanted(Town t, int g) =>
        Config.TownStockpiles ? Math.Max(0, (int)(t.DailyUse[g] * Config.StockpileDays - t.Stockpile[g])) : int.MaxValue;

    /// <summary>The town's current price for one unit of a raw good.</summary>
    private double TaskPrice(Town t, int g)
    {
        if (Config.TownStockpiles)
            return TaskUnitsWanted(t, g) > 0 ? Config.StockpileTaskPriceShare * t.Markets[g].Reference.Value : 0;
        if (t.TaskBudget <= 0 || t.TaskBudgetStart <= 0) return 0;
        double spent = 1 - t.TaskBudget / t.TaskBudgetStart;
        return Config.TaskPriceShare * t.Markets[g].Reference.Value * (1 - Config.TaskPriceEase * spent);
    }

    /// <summary>
    /// Delivers up to <paramref name="maxUnits"/> of a good if the town pays at
    /// least what the deliverer expects; returns units delivered and coins paid.
    /// </summary>
    private (int units, long coins) Deliver(Town t, int g, int maxUnits, double minPrice)
    {
        int units = 0;
        long coins = 0;
        maxUnits = Math.Min(maxUnits, TaskUnitsWanted(t, g));
        while (units < maxUnits)
        {
            double p = TaskPrice(t, g);
            if (p <= 0 || p < minPrice || (!Config.TownStockpiles && p > t.TaskBudget)) break;
            long pay = Math.Max(1, (long)Math.Round(p));
            if (Ledger.TreasuryBalance - NpcCommitted - coins < pay) break;
            t.TaskBudget -= pay;
            coins += pay;
            units++;
        }
        if (Config.TownStockpiles) t.Stockpile[g] += units;
        return (units, coins);
    }

    private void PayTask(Agent a, long coins)
    {
        if (coins <= 0) return;
        if (!Ledger.TryTransfer(Ledger.Treasury, a.Account, coins, Flow.TownContract, out _)) return;
        a.TodayIncome += coins;
        Track(a, IncomeKind.Tasks, coins);
        TaskCoinsPaid += coins;
    }

    /// <summary>A solo player hands in surplus raw goods, up to the solo daily cap.</summary>
    private void SoloDeliver(Agent a)
    {
        if (!Config.DeliveryTasks) return;
        var t = Towns[a.Town];
        for (int g = 0; g < Goods.Count && a.TaskUnitsToday < Config.SoloTaskCapPerDay; g++)
        {
            if (!IsTaskGood(t, g)) continue;
            int surplus = a.Inventory[g] - KeepAmount(a, g);
            if (surplus <= 0) continue;
            int max = Math.Min(surplus, Config.SoloTaskCapPerDay - a.TaskUnitsToday);
            var (units, coins) = Deliver(t, g, max, a.Belief[g] * Config.TaskAcceptShare);
            if (units == 0) continue;
            a.Inventory.Take(g, units);
            a.TaskUnitsToday += units;
            TaskUnitsDelivered += units;
            TrackGood(g, GoodFlowKind.TaskDelivered, units);
            PayTask(a, coins);
        }
    }

    public long BuybackUnits { get; private set; }
    public long BuybackCoins { get; private set; }

    /// <summary>
    /// NPCs always buy raw goods at half price: a gatherer sitting on more than
    /// they can sell dumps the excess. The goods go to the NPC traders' stock
    /// and are resold, like scrapped items. Never rare materials.
    /// </summary>
    private void SellExcessToNpcs(Agent a)
    {
        if (Config.NpcBuybackShare <= 0) return;
        var t = Towns[a.Town];
        for (int g = 0; g < Goods.Count; g++)
        {
            if (Goods.Defs[g].Tier != GoodTier.Raw || IsRare(g)) continue;
            int excess = a.Inventory[g] - KeepAmount(a, g) - Math.Max(0, Config.NpcBuybackKeep - ListedUnits(a, g));
            if (excess <= 0) continue;
            var m = t.Markets[g];
            if (WarehouseFull(m)) continue;
            long price = Math.Max(1, (long)Math.Floor(m.Reference.Value * Config.NpcBuybackShare));
            long coins = price * excess;
            if (Ledger.TreasuryBalance - NpcCommitted < coins) return;
            if (!Ledger.TryTransfer(Ledger.Treasury, a.Account, coins, Flow.NpcScrapPurchase, out _)) return;
            a.Inventory.Take(g, excess);
            m.NpcStock += excess;
            a.TodayIncome += coins;
            Track(a, IncomeKind.SalesToNpcs, coins);
            TrackGood(g, GoodFlowKind.SoldToNpc, excess);
            BuybackUnits += excess;
            BuybackCoins += coins;
        }
    }

    /// <summary>
    /// A party hands in what it just brought back, up to its shared cap, and
    /// splits the pay evenly among its members.
    /// </summary>
    private void PartyDeliver(Town t, List<Agent> party, Dictionary<int, double[]> fresh)
    {
        if (!Config.DeliveryTasks) return;
        int cap = Config.PartyTaskCapPerMember * party.Count - party.Sum(a => a.TaskUnitsToday);
        long coins = 0;
        for (int g = 0; g < Goods.Count && cap > 0; g++)
        {
            if (!IsTaskGood(t, g)) continue;
            int have = 0;
            foreach (var a in party) have += (int)Math.Min(a.Inventory[g], fresh[a.Id][g]);
            if (have <= 0) continue;
            double expect = party.Average(a => a.Belief[g]) * Config.TaskAcceptShare;
            var (units, paid) = Deliver(t, g, Math.Min(have, cap), expect);
            if (units == 0) continue;
            int left = units;
            foreach (var a in party)
            {
                int take = Math.Min(left, (int)Math.Min(a.Inventory[g], fresh[a.Id][g]));
                a.Inventory.Take(g, take);
                left -= take;
                if (left == 0) break;
            }
            cap -= units;
            coins += paid;
            TaskUnitsDelivered += units;
            TrackGood(g, GoodFlowKind.TaskDelivered, units);
            int each = units / party.Count;
            foreach (var a in party) a.TaskUnitsToday += each;
        }
        if (coins <= 0) return;
        long share = coins / party.Count;
        long rest = coins - share * party.Count;
        foreach (var a in party) PayTask(a, share + (rest-- > 0 ? 1 : 0));
    }
}
