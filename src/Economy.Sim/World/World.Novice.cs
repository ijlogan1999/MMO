using Economy.Core.Crafting;
using Economy.Core.Ledger;

namespace Economy.Sim.World;

/// <summary>
/// Ian, 2026-11-19. Everyone knows novice level of every craft, for the very
/// bottom tier only (bread, bandages and the firewood, flour and cloth they
/// need), so nobody has to pay whatever cooks or tailors ask for basics.
/// Novice work is slow and low quality, so the trades keep the better market.
/// New crafters get a starter quest: it teaches the craft, hands over
/// materials for their first few items, and an NPC buys those first items at a
/// fair price so they have coins to restock.
/// </summary>
public sealed partial class World
{
    public long NoviceCrafts { get; private set; }
    public long CrafterQuestsGiven { get; private set; }
    public long CommissionCoins { get; private set; }

    private Dictionary<int, Recipe>? _noviceRecipes;

    /// <summary>The one recipe a novice uses for each bottom-tier good.</summary>
    private Dictionary<int, Recipe> NoviceRecipes => _noviceRecipes ??= Config.NoviceGoods
        .Where(id => Goods.Index.ContainsKey(id) && Goods.RecipesByOutput[Goods[id]].Count > 0)
        .ToDictionary(id => Goods[id], id => Goods.RecipesByOutput[Goods[id]][0]);

    /// <summary>Raw materials (good, units) to make one unit of a novice good from scratch.</summary>
    private void NoviceRaw(int good, double mult, Dictionary<int, double> raw)
    {
        if (!NoviceRecipes.TryGetValue(good, out var r)) { raw[good] = raw.GetValueOrDefault(good) + mult; return; }
        foreach (var inp in r.Inputs) NoviceRaw(Goods[inp.GoodId], mult * inp.Quantity / r.OutputQuantity, raw);
    }

    private readonly Dictionary<int, double> _rawScratch = new();

    /// <summary>
    /// When the market wants clearly more for a basic good than its raw
    /// materials cost, a player makes it at novice level instead of buying.
    /// Returns units made (0 if it was cheaper to buy, or materials are short;
    /// the materials are ordered so a later turn can make it).
    /// </summary>
    private int TryNoviceMake(Agent a, int good, int qty)
    {
        if (!Config.NoviceCrafting || qty <= 0 || !NoviceRecipes.ContainsKey(good)) return 0;
        if (Roles.CraftSkill(a.Role) == NoviceRecipes[good].Skill) return 0; // the trade's own crafters craft properly
        _rawScratch.Clear();
        NoviceRaw(good, 1, _rawScratch);
        var m = Towns[a.Town].Markets;
        double rawCost = 0;
        foreach (var (g, n) in _rawScratch) rawCost += n * Math.Max(1, m[g].Reference.Value);
        double steps = CountSteps(good);
        double noviceCost = rawCost + steps * Config.StationFee + Config.NoviceTimeCost * steps;
        double buy = m[good].Book.BestAsk is long ask ? ask : a.Belief[good];
        double threshold = Goods.Defs[good].HasDurability ? Config.NoviceGearThreshold : Config.NoviceMakeThreshold;
        if (buy < noviceCost * threshold) return 0;

        // Have the raw materials? Make as many as we can; otherwise order them.
        int can = qty;
        foreach (var (g, n) in _rawScratch) can = Math.Min(can, (int)(a.Inventory[g] / n));
        if (can <= 0)
        {
            foreach (var (g, n) in _rawScratch)
            {
                int need = (int)Math.Ceiling(n * qty) - a.Inventory[g];
                // Hold on to these until they're used (don't resell them next turn).
                a.NoviceKeep[g] = Math.Max(a.NoviceKeep.GetValueOrDefault(g), (int)Math.Ceiling(n * qty));
                if (need > 0) PlaceBuy(a, g, need, Math.Max(1, (long)Math.Round(Math.Max(a.Belief[g], m[g].Reference.Value))));
            }
            return 0;
        }
        long fees = (long)Math.Ceiling(steps * Config.StationFee * can);
        if (!Ledger.TryTransfer(a.Account, Ledger.Treasury, fees, Flow.StationFee, out _)) return 0;
        foreach (var (g, n) in _rawScratch)
        {
            int take = (int)Math.Ceiling(n * can - 1e-9);
            a.Inventory.Take(g, take);
            TrackGood(g, GoodFlowKind.UsedInCrafts, take);
        }
        a.Inventory.Add(good, can, Config.NoviceQuality);
        foreach (var g in _rawScratch.Keys) a.NoviceKeep.Remove(g);
        TrackGood(good, GoodFlowKind.Crafted, can);
        NoviceCrafts += can;
        return can;
    }

    private double CountSteps(int good) =>
        NoviceRecipes.TryGetValue(good, out var r) ? 1 + r.Inputs.Sum(i => CountSteps(Goods[i.GoodId]) * i.Quantity / (double)r.OutputQuantity) : 0;

    /// <summary>A player taking up a craft for the first time gets the starter quest.</summary>
    private void GiveCrafterQuest(Agent a)
    {
        if (!Config.CrafterStarterQuest || !Roles.IsCrafter(a.Role) || a.CraftQuestDone) return;
        a.CraftQuestDone = true;
        string skill = Roles.CraftSkill(a.Role)!;
        // The quest teaches the basics: at least enough skill for the trade's starter items.
        a.Skill = Math.Max(a.Skill, Config.CrafterQuestSkill);
        // Materials for the first few items of the best starter recipe the player can now make.
        var recipe = Goods.Catalog.Recipes
            .Where(r => r.Skill == skill && r.MinSkill <= a.Skill && Goods.Defs[Goods[r.OutputGoodId]].Tier != Economy.Core.Items.GoodTier.Raw)
            .OrderByDescending(r => DesignPrice[Goods[r.OutputGoodId]])
            .FirstOrDefault();
        if (recipe == null) return;
        // Worth about as much as a fighter's starter kit, for at most a handful of items.
        var inputs = TieredInputs(recipe, a.Skill);
        double cost = inputs.Sum(x => DesignPrice[x.good] * x.qty);
        int crafts = (int)Math.Clamp(Math.Floor(Config.CrafterQuestValue / Math.Max(1, cost)), 1, Config.CrafterQuestCrafts);
        foreach (var (g, q) in inputs) a.Inventory.Add(g, q * crafts, 40);
        a.CommissionLeft = crafts;
        CrafterQuestsGiven++;
    }

    /// <summary>The quest's NPC buys a new crafter's first items at the fair (recipe) price.</summary>
    private void PayCommission(Agent a, int good, int qty)
    {
        int n = Math.Min(qty, Math.Min(a.CommissionLeft, a.Inventory[good]));
        if (n <= 0) return;
        long pay = (long)Math.Round(DesignPrice[good] * n);
        if (!Ledger.TryTransfer(Ledger.Treasury, a.Account, pay, Flow.TownContract, out _)) return;
        a.Inventory.Take(good, n);
        Towns[a.Town].Markets[good].NpcStock += n;
        a.CommissionLeft -= n;
        a.TodayIncome += pay;
        Track(a, IncomeKind.Tasks, pay);
        CommissionCoins += pay;
    }
}
