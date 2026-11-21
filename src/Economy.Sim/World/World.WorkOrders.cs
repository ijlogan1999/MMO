using Economy.Core.Crafting;
using Economy.Core.Ledger;

namespace Economy.Sim.World;

/// <summary>A player's order for one piece of gear, paid into escrow up front.</summary>
public sealed class WorkOrder
{
    public int Buyer { get; init; }
    public int Town { get; init; }
    public int Good { get; init; }
    public long Price { get; init; }
    public int PostedHour { get; init; }
    public int Crafter { get; set; } = -1;
    public int AcceptedHour { get; set; }
    /// <summary>Coins released to the crafter for materials when the order was taken.</summary>
    public long MaterialAdvance { get; set; }
}

/// <summary>
/// Work orders (Ian, 2026-11-19: try them if crafters still can't afford to
/// make gear). A player who needs gear posts an order and pays into escrow.
/// A crafter who can make it takes the order; the game releases part of the
/// escrow to buy the materials (in the game: materials bought through the
/// order, so a crafter can't pocket the coins), and the rest when the item is
/// handed over. Orders nobody takes, or that aren't finished in time, go back
/// to the buyer, who can still buy from the market.
/// </summary>
public sealed partial class World
{
    private readonly List<WorkOrder> _orders = new();
    private AccountId _orderEscrow;

    /// <summary>Highest skill that still uses common-tier materials.</summary>
    private double OrderTierSkill => Config.GearTiers.Length > 1 ? Config.GearTiers[1].MinSkill - 1 : 100;

    public long WorkOrdersPosted { get; private set; }
    public long WorkOrdersDone { get; private set; }
    public long WorkOrdersExpired { get; private set; }
    public Dictionary<(string good, string why), int> WorkOrderMisses { get; } = new();

    /// <summary>Posts an order for a piece of gear if a local crafter could make it. Returns true if the player is now waiting on one.</summary>
    private bool PostWorkOrder(Agent a, int good)
    {
        if (!Config.WorkOrders) return false;
        if (a.WorkOrderGoods.Contains(good)) return true;
        if (a.WorkOrderGaveUp.Contains(good)) return false;
        var recipe = Goods.RecipesByOutput[good].FirstOrDefault();
        if (recipe == null) return false;
        bool someone = false;
        foreach (int id in Towns[a.Town].Repairers)
        {
            var c = Agents[id];
            if (c.Active && c.Id != a.Id && Roles.CraftSkill(c.Role) == recipe.Skill && c.Skill >= recipe.MinSkill) { someone = true; break; }
        }
        if (!someone) return false;
        // Offer about what the market (or an NPC shop) would charge.
        long price = (long)Math.Round(Math.Min(AskOrBelief(a, good), Towns[a.Town].Markets[good].Reference.Value * (1 + Config.Stabilizer.PriceBand)));
        if (price <= 0 || Coins(a) < price) return false;
        Ledger.Transfer(a.Account, _orderEscrow, price, Flow.EscrowRelease);
        _orders.Add(new WorkOrder { Buyer = a.Id, Town = a.Town, Good = good, Price = price, PostedHour = Hour });
        a.WorkOrderGoods.Add(good);
        WorkOrdersPosted++;
        return true;
    }

    /// <summary>Materials cost of one item at the crafter's tier, at today's prices (components made from their own inputs).</summary>
    private double MaterialCost(Agent c, Recipe r, int depth = 0)
    {
        double cost = Config.StationFee;
        foreach (var (g, q) in TieredInputs(r, Math.Min(c.Skill, OrderTierSkill)))
        {
            double unit = InputCost(c, g);
            var sub = depth < 2 ? Goods.RecipesByOutput[g].FirstOrDefault(x => x.Skill == r.Skill && x.MinSkill <= c.Skill) : null;
            if (sub != null) unit = Math.Min(unit, MaterialCost(c, sub, depth + 1) / sub.OutputQuantity);
            cost += unit * q;
        }
        return cost;
    }

    /// <summary>A crafter with no order takes the best-paying one it can make. Returns the order it is working on.</summary>
    private WorkOrder? TakeWorkOrder(Agent c)
    {
        if (!Config.WorkOrders) return null;
        var mine = _orders.FirstOrDefault(o => o.Crafter == c.Id);
        if (mine != null) return mine;
        string skill = Roles.CraftSkill(c.Role)!;
        WorkOrder? best = null;
        double bestMargin = 0, bestCost = 0;
        foreach (var o in _orders)
        {
            if (o.Crafter >= 0 || o.Town != c.Town || o.Buyer == c.Id) continue;
            var r = Goods.RecipesByOutput[o.Good].FirstOrDefault();
            if (r == null || r.Skill != skill || r.MinSkill > c.Skill) continue;
            double cost = MaterialCost(c, r);
            double margin = o.Price - cost * (1 + Config.WorkOrderMinMargin);
            if (margin > bestMargin) { bestMargin = margin; best = o; bestCost = cost; }
        }
        if (best == null) return null;
        best.Crafter = c.Id;
        best.AcceptedHour = Hour;
        best.MaterialAdvance = Math.Min(best.Price, (long)Math.Ceiling(bestCost * 1.15));
        Ledger.Transfer(_orderEscrow, c.Account, best.MaterialAdvance, Flow.EscrowRelease);
        c.TodayIncome += best.MaterialAdvance;
        Track(c, IncomeKind.SalesToPlayers, best.MaterialAdvance);
        return best;
    }

    /// <summary>Hands over a finished item and releases the rest of the payment.</summary>
    private void DeliverWorkOrder(Agent c, WorkOrder o)
    {
        if (c.Inventory[o.Good] <= 0) return;
        int q = c.Inventory.QualityOf(o.Good);
        c.Inventory.Take(o.Good, 1);
        var buyer = Agents[o.Buyer];
        buyer.Inventory.Add(o.Good, 1, q);
        buyer.WorkOrderGoods.Remove(o.Good);
        long rest = o.Price - o.MaterialAdvance;
        if (rest > 0)
        {
            Ledger.Transfer(_orderEscrow, c.Account, rest, Flow.EscrowRelease);
            c.TodayIncome += rest;
            Track(c, IncomeKind.SalesToPlayers, rest);
        }
        c.SoldToday[o.Good]++;
        _orders.Remove(o);
        WorkOrdersDone++;
        TrackGood(o.Good, GoodFlowKind.PlayerToPlayer, 1);
    }

    /// <summary>Daily: orders nobody took, or that weren't finished in time, are refunded.</summary>
    private void ExpireWorkOrders()
    {
        for (int i = _orders.Count - 1; i >= 0; i--)
        {
            var o = _orders[i];
            var buyer = Agents[o.Buyer];
            bool untaken = o.Crafter < 0 && Hour - o.PostedHour >= Config.WorkOrderWaitHours;
            bool late = o.Crafter >= 0 && Hour - o.AcceptedHour >= Config.WorkOrderBuildHours;
            bool gone = !buyer.Active || (o.Crafter >= 0 && !Agents[o.Crafter].Active);
            if (!untaken && !late && !gone) continue;
            _orders.RemoveAt(i);
            long refund = o.Price - o.MaterialAdvance;
            if (o.Crafter >= 0 && o.MaterialAdvance > 0)
            {
                // Late crafter: the materials money comes back out of their purse (in the game the order holds the materials).
                var c = Agents[o.Crafter];
                long back = Math.Min(o.MaterialAdvance, Coins(c));
                if (back > 0) { Ledger.Transfer(c.Account, buyer.Account, back, Flow.PlayerTrade); c.TodayIncome -= back; }
            }
            if (refund > 0) Ledger.Transfer(_orderEscrow, buyer.Account, refund, Flow.EscrowRelease);
            buyer.WorkOrderGoods.Remove(o.Good);
            if (untaken) buyer.WorkOrderGaveUp.Add(o.Good); // buy from the market next time
            WorkOrdersExpired++;
            var k = (Goods.Defs[o.Good].Id, untaken ? "untaken" : late ? "late" : "gone");
            WorkOrderMisses[k] = WorkOrderMisses.GetValueOrDefault(k) + 1;
        }
        // Players who gave up retry orders after a week.
        if (Day % 7 == 0) foreach (var a in Agents) a.WorkOrderGaveUp.Clear();
    }
}
