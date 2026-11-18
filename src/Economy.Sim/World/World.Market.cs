using Economy.Core.Ledger;
using Economy.Core.Markets;

namespace Economy.Sim.World;

/// <summary>The game-time token: one world-wide order book.</summary>
public sealed class TokenMarket
{
    public TokenMarket(double initialPrice) { LastPrice = initialPrice; }
    public OrderBook Book { get; } = new();
    public double LastPrice { get; set; }
    public long DayValue { get; set; }
    public int DayUnits { get; set; }
    public int RealMoneyTokens { get; set; }
    public int FreeTokensUsed { get; set; }
    public int TokensConsumed { get; set; }
}

public sealed partial class World
{
    private long _orderIds;
    private readonly List<Fill> _fills = new();

    /// <summary>Coins the treasury has promised to open NPC buy orders.</summary>
    public long NpcCommitted { get; private set; }

    public TokenMarket Tokens { get; private set; } = new(15000);

    private OrderBook BookFor(int town, int good) => good == TokenIndex ? Tokens.Book : Towns[town].Markets[good].Book;

    /// <summary>
    /// Places a buy order for an agent, holding its coins in escrow. Returns
    /// the units bought right away. With <paramref name="rest"/> false, any
    /// unfilled part is cancelled at once (a market order).
    /// </summary>
    public int PlaceBuy(Agent a, int good, int qty, long price, int minQuality = 1, bool rest = true)
    {
        if (qty <= 0 || price <= 0) return 0;
        long coins = Coins(a);
        if (coins < price) return 0;
        qty = (int)Math.Min(qty, coins / price);
        if (qty <= 0) return 0;

        Ledger.Transfer(a.Account, Escrow, price * qty, Flow.EscrowHold);
        int town = good == TokenIndex ? -1 : a.Town;
        var book = BookFor(town, good);
        var order = new Order(++_orderIds, a.Id, Side.Buy, price, qty, minQuality, book.NextSequence());
        _fills.Clear();
        book.Submit(order, _fills, rest);
        int filled = qty - order.Remaining;
        SettleAll(town, good);

        if (order.Remaining > 0)
        {
            if (rest) a.Orders.Add(new OpenOrder(order, town, good) { PlacedHour = Hour });
            else Ledger.Transfer(Escrow, a.Account, order.Price * order.Remaining, Flow.EscrowRelease);
        }
        if (good != TokenIndex) Towns[town].Markets[good].HadDemandToday = true;
        return filled;
    }

    /// <summary>Lists goods from an agent's inventory. Returns units sold right away.</summary>
    public int PlaceSell(Agent a, int good, int qty, long price, bool rest = true)
    {
        if (price <= 0) price = 1;
        int quality;
        if (good == TokenIndex)
        {
            qty = Math.Min(qty, a.Tokens);
            if (qty <= 0) return 0;
            long fee = (long)Math.Ceiling(price * qty * Config.TokenListingFee);
            if (fee > 0 && !Ledger.TryTransfer(a.Account, Ledger.Treasury, fee, Flow.TokenListingFee, out _)) return 0;
            a.Tokens -= qty;
            quality = 100;
        }
        else
        {
            quality = a.Inventory.QualityOf(good);
            qty = a.Inventory.Take(good, qty);
            if (qty <= 0) return 0;
        }

        int town = good == TokenIndex ? -1 : a.Town;
        var book = BookFor(town, good);
        var order = new Order(++_orderIds, a.Id, Side.Sell, price, qty, quality, book.NextSequence());
        _fills.Clear();
        book.Submit(order, _fills, rest);
        int filled = qty - order.Remaining;
        SettleAll(town, good);

        if (order.Remaining > 0)
        {
            if (rest) a.Orders.Add(new OpenOrder(order, town, good) { PlacedHour = Hour });
            else ReturnGoods(a, good, order.Remaining, quality);
        }
        return filled;
    }

    /// <summary>An NPC order. Buys are paid by the treasury, which must be able to cover them.</summary>
    public Order? PlaceNpcOrder(int town, int good, Side side, int qty, long price, int quality, int tag, int owner = NpcOwner)
    {
        if (qty <= 0 || price <= 0) return null;
        if (side == Side.Buy)
        {
            long free = Ledger.TreasuryBalance - NpcCommitted;
            qty = (int)Math.Min(qty, Math.Max(0, free) / price);
            if (qty <= 0) return null;
            NpcCommitted += price * qty;
        }
        var book = BookFor(town, good);
        var order = new Order(++_orderIds, owner, side, price, qty, quality, book.NextSequence()) { Tag = tag };
        _fills.Clear();
        book.Submit(order, _fills);
        SettleAll(town, good);
        if (order.Remaining == 0) return null;
        return order;
    }

    /// <summary>Removes a resting NPC order; returns unsold units (for sells).</summary>
    public int CancelNpcOrder(int town, int good, Order order)
    {
        int left = BookFor(town, good).Cancel(order);
        if (order.Side == Side.Buy)
        {
            NpcCommitted -= order.Price * left;
            return 0;
        }
        return left;
    }

    /// <summary>
    /// Cancels an agent's resting orders, returning held coins and goods, and
    /// nudges its beliefs: unsold goods were priced too high, unfilled bids too low.
    /// </summary>
    public void CancelAgentOrders(Agent a, bool learn = true)
    {
        foreach (var oo in a.Orders)
        {
            var o = oo.Order;
            int left = BookFor(oo.Town, oo.Good).Cancel(o);
            if (left <= 0) continue;
            if (o.Side == Side.Buy)
            {
                Ledger.Transfer(Escrow, a.Account, o.Price * left, Flow.EscrowRelease);
                if (learn) a.Belief[oo.Good] = Math.Min(a.Belief[oo.Good] * 1.05, CeilingFor(oo.Good));
            }
            else
            {
                ReturnGoods(a, oo.Good, left, o.Quality);
                if (learn) a.Belief[oo.Good] = Math.Max(a.Belief[oo.Good] * 0.96, FloorFor(oo.Good));
            }
        }
        a.Orders.Clear();
    }

    /// <summary>Nobody sells for less than 15% of the starting price; they would rather stop.</summary>
    private double FloorFor(int good) => good == TokenIndex ? 1000 : Math.Max(1, Goods.BasePrice[good] * 0.15);

    private double CeilingFor(int good) => good == TokenIndex ? 1_000_000 : Goods.BasePrice[good] * 20;

    private void ReturnGoods(Agent a, int good, int qty, int quality)
    {
        if (good == TokenIndex) a.Tokens += qty;
        else a.Inventory.Add(good, qty, quality);
    }

    private void SettleAll(int town, int good)
    {
        // Settling can place no new orders, so the list is stable while we walk it.
        for (int i = 0; i < _fills.Count; i++) Settle(town, good, _fills[i]);
        _fills.Clear();
    }

    private void Settle(int town, int good, in Fill f)
    {
        int buyerId = f.Buy.OwnerId, sellerId = f.Sell.OwnerId;
        long total = f.Total;
        bool playerTrade = buyerId >= 0 && sellerId >= 0;

        // Coins
        if (buyerId >= 0)
        {
            var buyer = Agents[buyerId];
            long refund = (f.Buy.Price - f.Price) * f.Quantity;
            if (refund > 0) Ledger.Transfer(Escrow, buyer.Account, refund, Flow.EscrowRelease);
            if (sellerId >= 0) Ledger.Transfer(Escrow, Agents[sellerId].Account, total, Flow.EscrowRelease);
            else
            {
                // Bought from an NPC: the money leaves circulation into the treasury.
                Ledger.Transfer(Escrow, Ledger.Treasury, total, Flow.NpcSale);
            }
            ReceiveGoods(buyer, good, f.Quantity, f.Sell.Quality);
            if (good != TokenIndex && IsBusinessInput(buyer, good)) buyer.TodayIncome -= total;
            buyer.Belief[good] = Math.Max(1, buyer.Belief[good] * 0.99);
        }
        else
        {
            NpcCommitted -= f.Buy.Price * f.Quantity;
            if (sellerId >= 0)
            {
                var flow = f.Buy.Tag == (int)Core.Stabilizer.NpcRole.BuyerGuild ? Flow.BuyerGuildPurchase : Flow.MarketMakerPurchase;
                Ledger.Transfer(Ledger.Treasury, Agents[sellerId].Account, total, flow);
            }
            if (good != TokenIndex) Towns[town].Markets[good].NpcStock += f.Quantity;
        }

        if (sellerId >= 0)
        {
            var seller = Agents[sellerId];
            long tax = (long)Math.Round(total * TaxRate);
            if (tax > 0) Ledger.Transfer(seller.Account, Ledger.Treasury, tax, Flow.MarketTax);
            if (good != TokenIndex) seller.TodayIncome += total - tax;
            if (seller.IsMaster(Config.MasterSkill) && !seller.OnlineAt(HourOfDay))
            {
                long cut = (long)Math.Round(total * Config.KeeperCut);
                if (cut > 0) Ledger.Transfer(seller.Account, Ledger.Treasury, cut, Flow.KeeperCut);
            }
            seller.Belief[good] = Math.Min(seller.Belief[good] * 1.02, CeilingFor(good));
        }

        // Statistics
        if (good == TokenIndex)
        {
            Tokens.LastPrice = f.Price;
            Tokens.DayValue += total;
            Tokens.DayUnits += f.Quantity;
            return;
        }
        var m = Towns[town].Markets[good];
        if (playerTrade)
        {
            m.HourPlayerValue += total;
            m.HourPlayerUnits += f.Quantity;
            m.DayPlayerValue += total;
            m.DayPlayerUnits += f.Quantity;
            m.LastPrice = f.Price;
            m.LastTradeHour = Hour;
        }
        else
        {
            m.HourNpcValue += total;
            m.HourNpcUnits += f.Quantity;
            m.DayNpcValue += total;
            m.DayNpcUnits += f.Quantity;
        }
    }

    /// <summary>True when a purchase is a cost of doing business rather than consumption.</summary>
    private bool IsBusinessInput(Agent a, int good) => a.Role switch
    {
        Role.Hauler or Role.Speculator => true,
        Role.Repairer => good == Goods.RepairMaterial[Goods["pickaxe"]] || good == Goods.RepairMaterial[Goods["leather_armor"]],
        _ => Roles.IsCrafter(a.Role) && IsInputFor(a, good),
    };

    private void ReceiveGoods(Agent buyer, int good, int qty, int quality)
    {
        if (good == TokenIndex)
        {
            buyer.Tokens += qty;
            if (buyer.RenewalPending && buyer.Tokens > 0)
            {
                buyer.Tokens--;
                buyer.RenewalPending = false;
                Tokens.TokensConsumed++;
            }
            return;
        }
        buyer.Inventory.Add(good, qty, quality);
    }

    /// <summary>Best price an agent can buy at right now in its town (ask), or its belief if there is none.</summary>
    public double AskOrBelief(Agent a, int good)
    {
        var ask = Towns[a.Town].Markets[good].Book.BestAsk;
        return ask.HasValue ? Math.Min(ask.Value, a.Belief[good] * 1.5) : a.Belief[good];
    }
}
