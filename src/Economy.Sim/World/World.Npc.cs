using Economy.Core.Ledger;
using Economy.Core.Items;
using Economy.Core.Markets;
using Economy.Core.Stabilizer;

namespace Economy.Sim.World;

public sealed partial class World
{
    private long _contractPoolThisHour;
    private readonly long[] _sinkHistory = new long[24];
    private long _lastSinkTotal;
    private readonly List<StabilizerEvent> _events = new();

    /// <summary>Open NPC orders per (town, good): (order, role tag).</summary>
    private readonly Dictionary<(int town, int good), List<Order>> _npcOrders = new();

    public const int VendorTag = 9;

    private static readonly double FastAlpha = 1 - Math.Pow(0.5, 1.0 / 24);

    /// <summary>
    /// The first half of the warm-up is calibration: prices find their level
    /// with the stabilizer off, then each reference jumps to the recent
    /// market price and the stabilizer goes live.
    /// </summary>
    public int CalibrationDays => Config.WarmupDays / 2;

    public bool StabilizerLive => Config.StabilizerEnabled && Day >= CalibrationDays;
    public const int NpcGoodsQuality = 45;
    public const int VendorQuality = 50;

    /// <summary>
    /// Town contracts are the main faucet. Each hour the treasury pays out
    /// roughly what sinks brought in over the last day, scaled up when money
    /// per active player is below target and down when above.
    /// </summary>
    private void StartContractHour()
    {
        // Net treasury income from everything except contracts: sinks minus
        // what NPC buyers paid out. Contracts hand back that much (scaled), so
        // the treasury neither drains nor hoards while money sits at target.
        long sinksNow = Ledger.SinkTotal
            - Ledger.TotalFor(Flow.BuyerGuildPurchase) - Ledger.TotalFor(Flow.MarketMakerPurchase)
            - Ledger.TotalFor(Flow.HiredHandWage) - Ledger.TotalFor(Flow.NpcScrapPurchase);
        _sinkHistory[HourOfDay] = sinksNow - _lastSinkTotal;
        _lastSinkTotal = sinksNow;
        double sinkPerHour = _sinkHistory.Average();

        int active = ActiveAgentCount;
        double moneyPer = active > 0 ? (double)ActiveMoney() / active : Config.TargetMoneyPerPlayer;
        double ratio = Config.TargetMoneyPerPlayer / Math.Max(1.0, moneyPer);
        double scale = Math.Clamp(ratio * ratio, 0.25, 3.0);
        // Coins that quitting players take with them are gone for good; the
        // treasury tops players up only while it is above its starting level,
        // so it does not drain itself refilling them.
        double seed = (double)Config.TreasurySeedPerAgent * Config.AgentCount;
        if (scale > 1 && Ledger.TreasuryBalance < seed)
            scale = Math.Max(Math.Min(1.0, scale), scale * Math.Pow(Ledger.TreasuryBalance / seed, 4));
        double pool = Math.Max(0, sinkPerHour) * Config.ContractShareOfSinks * scale;
        double cap = Math.Max(0, Ledger.TreasuryBalance - NpcCommitted) * Config.ContractMaxTreasurySharePerHour;
        _contractPoolThisHour = (long)Math.Min(pool, cap);
    }

    public int ActiveAgentCount { get; private set; }

    /// <summary>Coins held by active players plus everything in escrow.</summary>
    public long ActiveMoney()
    {
        long sum = Ledger.Balance(Escrow);
        int n = 0;
        foreach (var a in Agents)
        {
            if (!a.Active || a.Role == Role.Speculator) continue;
            sum += Ledger.Balance(a.Account);
            n++;
        }
        ActiveAgentCount = n;
        return sum;
    }

    private void RunStabilizer()
    {
        foreach (var town in Towns)
        {
            for (int g = 0; g < Goods.Count; g++)
            {
                var m = town.Markets[g];
                var signals = Signals(town, g, m);
                // The guild only buys what it can resell later: once its warehouse
                // holds a couple of weeks of normal volume it stops buying.
                bool canAfford = Ledger.TreasuryBalance - NpcCommitted > Ledger.TreasuryBalance * 0.05
                    && !WarehouseFull(m);
                _events.Clear();
                if (StabilizerLive) m.Stabilizer.Evaluate(signals, canAfford, _npcRng, _events);
                foreach (var e in _events) Metrics.RecordEvent(town.Index, g, e);
                PlaceNpcOrders(town, g, m, signals);
            }
        }
    }

    private MarketSignals Signals(Town town, int g, GoodMarket m)
    {
        double price = m.HourPlayerUnits > 0 ? (double)m.HourPlayerValue / m.HourPlayerUnits : m.LastPrice;
        var bid = m.Book.BestBid;
        var ask = m.Book.BestAsk;
        double reference = m.Reference.Value;
        double spread = bid.HasValue && ask.HasValue ? Math.Max(0, ask.Value - bid.Value) / reference : double.PositiveInfinity;
        return new MarketSignals
        {
            Price = price,
            Reference = reference,
            ShortageRate = m.ShortageRate,
            Liquidity = ask.HasValue ? m.Book.AskDepthAtOrBelow((long)Math.Round(ask.Value * 1.1)) : 0,
            DailyVolume = Math.Max(1, m.PlayerVolumeEma),
            Spread = spread,
            PlayerProducersActive = RegionProducedHour[town.Region, g] >= Hour - 48 && Hour >= 48,
            HasDemand = m.PlayerVolumeEma >= 1 || m.HadDemandToday,
        };
    }

    private void PlaceNpcOrders(Town town, int g, GoodMarket m, MarketSignals s)
    {
        var key = (town.Index, g);
        if (_npcOrders.TryGetValue(key, out var open))
        {
            foreach (var o in open)
            {
                int left = CancelNpcOrder(town.Index, g, o);
                if (o.Side == Side.Sell && o.Tag != VendorTag) m.NpcStock += left;
                else if (o.Tag == VendorTag) m.VendorLeftToday += left;
            }
            open.Clear();
        }
        else
        {
            open = new List<Order>();
            _npcOrders[key] = open;
        }

        // NPC vendors sell raw materials at normal quality in small daily amounts.
        if (Goods.Defs[g].Tier == GoodTier.Raw && m.VendorLeftToday > 0)
        {
            long price = (long)Math.Ceiling(m.Reference.Value * Config.NpcVendorMarkup);
            int qty = m.VendorLeftToday;
            m.VendorLeftToday = 0;
            var o = PlaceNpcOrder(town.Index, g, Side.Sell, qty, Math.Max(1, price), VendorQuality, VendorTag, VendorOwner);
            int sold = qty - (o?.Remaining ?? 0);
            if (o != null) open.Add(o);
            _ = sold;
        }

        if (!StabilizerLive) return;
        double band = Config.Stabilizer.PriceBand;
        double reference = m.Reference.Value;
        double perHour(NpcRole role) => m.Stabilizer.VolumeShare(role) * s.DailyVolume / 24.0;

        // Relic cores only drop from dungeons: NPCs never sell them.
        bool npcMaySell = Goods.Defs[g].Tier != GoodTier.RelicCore;

        foreach (var role in new[] { NpcRole.TraderCaravan, NpcRole.HiredHand })
        {
            if (!npcMaySell || m.Stabilizer.Strength(role) == 0) continue;
            int qty = RandomRound(perHour(role), _npcRng);
            if (qty <= 0) continue;
            int fromStock = Math.Min(qty, m.NpcStock);
            m.NpcStock -= fromStock;
            ItemsInjected += qty - fromStock;
            long price = Math.Max(1, (long)Math.Ceiling(reference * (1 + band)));
            var o = PlaceNpcOrder(town.Index, g, Side.Sell, qty, price, NpcGoodsQuality, (int)role);
            if (o != null) open.Add(o);
        }

        if (m.Stabilizer.Strength(NpcRole.BuyerGuild) > 0 && !WarehouseFull(m))
        {
            int qty = RandomRound(perHour(NpcRole.BuyerGuild), _npcRng);
            long price = Math.Max(1, (long)Math.Floor(reference * (1 - band)));
            var o = PlaceNpcOrder(town.Index, g, Side.Buy, qty, price, 1, (int)NpcRole.BuyerGuild);
            if (o != null) open.Add(o);
        }

        // Market makers only work markets players already trade in.
        if (m.Stabilizer.Strength(NpcRole.MarketMaker) > 0 && m.PlayerVolumeEma >= 1)
        {
            int qty = RandomRound(perHour(NpcRole.MarketMaker), _npcRng);
            // Just outside the best player prices, so players always trade first.
            long bidPrice = (long)Math.Floor(reference * (1 - band * 0.6));
            if (m.Book.BestBid is long pb) bidPrice = Math.Min(bidPrice, pb - 1);
            long askPrice = (long)Math.Ceiling(reference * (1 + band * 0.6));
            if (m.Book.BestAsk is long pa) askPrice = Math.Max(askPrice, pa + 1);
            if (!WarehouseFull(m))
            {
                var b = PlaceNpcOrder(town.Index, g, Side.Buy, qty, Math.Max(1, bidPrice), 1, (int)NpcRole.MarketMaker);
                if (b != null) open.Add(b);
            }
            if (npcMaySell && m.NpcStock > 0)
            {
                int sq = Math.Min(qty, m.NpcStock);
                m.NpcStock -= sq;
                var sOrder = PlaceNpcOrder(town.Index, g, Side.Sell, sq, askPrice, NpcGoodsQuality, (int)NpcRole.MarketMaker);
                if (sOrder != null) open.Add(sOrder);
            }
        }
    }

    /// <summary>End-of-hour market bookkeeping: shortage, reference price.</summary>
    private void EndHourMarkets()
    {
        foreach (var town in Towns)
        {
            for (int g = 0; g < Goods.Count; g++)
            {
                var m = town.Markets[g];
                int bids = 0, asks = 0;
                foreach (var o in m.Book.Buys) if (o.OwnerId >= 0) bids += o.Remaining;
                foreach (var o in m.Book.Sells) asks += o.Remaining;
                double shortage = bids > 0 ? Math.Max(0, bids - asks) / (double)bids : 0;
                m.ShortageWindow[m.ShortageCursor] = shortage;
                m.ShortageCursor = (m.ShortageCursor + 1) % m.ShortageWindow.Length;
                m.DayShortageSum += shortage;
                m.DayShortageHours++;

                if (m.HourPlayerUnits > 0)
                {
                    double p = (double)m.HourPlayerValue / m.HourPlayerUnits;
                    m.FastPrice = m.FastPrice <= 0 ? p : m.FastPrice + FastAlpha * (p - m.FastPrice);
                    if (Day >= CalibrationDays) m.Reference.Observe(p);
                }
                if (Day == CalibrationDays && HourOfDay == 0 && m.FastPrice > 0) m.Reference.Reset(CalibratedReference(g, m.FastPrice));
                m.ResetHour();
            }
        }
    }

    /// <summary>NPC stock above this many days of player volume: stop buying.</summary>
    public const double WarehouseDays = 14;

    public static bool WarehouseFull(GoodMarket m) => m.NpcStock >= Math.Max(10, m.PlayerVolumeEma * WarehouseDays);

    /// <summary>
    /// When calibration ends, references jump to the market price, but never
    /// further than 3x from the starting price: a market still swinging wildly
    /// at that point should not set the band for months.
    /// </summary>
    private double CalibratedReference(int g, double fastPrice)
    {
        double b = DesignPrice[g];
        return Math.Clamp(fastPrice, b / 3, b * 3);
    }

    private void RefreshVendors()
    {
        foreach (var town in Towns)
        {
            for (int g = 0; g < Goods.Count; g++)
            {
                if (Goods.Defs[g].Tier != GoodTier.Raw) continue;
                var m = town.Markets[g];
                m.VendorLeftToday = Math.Max(3, (int)Math.Round(m.DailyVolumeEma * Config.NpcVendorDailyShare));
            }
        }
    }
}
