using Economy.Core.Items;
using Economy.Core.Ledger;

namespace Economy.Sim.World;

/// <summary>
/// Town delivery tasks (Ian, 2026-11-19): each town buys raw materials at a
/// per-unit price, a little under the player price, out of a daily budget.
/// The price eases as the day's budget is spent and resets the next day.
/// Bring 5 or 150; solo players have a small daily cap, parties a bigger one
/// whose pay is split evenly. Delivered goods are used up by the town.
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

    /// <summary>At day end, yesterday's delivery pool becomes today's town budgets.</summary>
    private void OpenTaskBudgets()
    {
        double total = 0;
        foreach (var t in Towns) total += t.Def.Size;
        foreach (var t in Towns)
        {
            t.TaskBudget = _taskPoolToday * t.Def.Size / Math.Max(0.01, total);
            t.TaskBudgetStart = t.TaskBudget;
        }
        _taskPoolToday = 0;
        foreach (var a in Agents) a.TaskUnitsToday = 0;
    }

    private bool IsTaskGood(Town t, int g) =>
        Goods.Defs[g].Tier == GoodTier.Raw && (t.RegenPerHour[g] > 0 || t.DeepRegenPerHour[g] > 0);

    /// <summary>The town's current price for one unit of a raw good.</summary>
    private double TaskPrice(Town t, int g)
    {
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
        while (units < maxUnits)
        {
            double p = TaskPrice(t, g);
            if (p <= 0 || p < minPrice || p > t.TaskBudget) break;
            long pay = Math.Max(1, (long)Math.Round(p));
            if (Ledger.TreasuryBalance - NpcCommitted - coins < pay) break;
            t.TaskBudget -= pay;
            coins += pay;
            units++;
        }
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
