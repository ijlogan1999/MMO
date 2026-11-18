using System.Globalization;
using System.Text;
using Economy.Core.Ledger;
using Economy.Core.Stabilizer;
using Economy.Sim.World;

namespace Economy.Sim.Reporting;

public sealed class HourRow
{
    public int Hour;
    public int Active;
    public long ActiveMoney;
    public long Treasury;
    public long Circulating;
    public long PlayerValue;
    public long NpcValue;

    /// <summary>Average best-ask minus best-bid over reference, across markets players trade in.</summary>
    public double Spread;
}

public sealed class DayRow
{
    public int Day;
    public int Active;
    public double MoneyPerPlayer;
    public long Treasury;
    public long Circulating;
    public long Faucets;
    public long Sinks;
    public double Basket;
    public double StarterKitHours;
    public double TokenPrice;
    public double TokenHoursMaster;
    public double TokenHoursMedian;
    public int Quits;
    public int Joins;
    public long PlayerValue;
    public long NpcValue;
    public long ItemsInjected;
    public Dictionary<Flow, long> Flows = new();
}

public sealed class MarketDayRow
{
    public int Day;
    public int Town;
    public int Good;
    public double Price;
    public double Reference;
    public int PlayerUnits;
    public int NpcUnits;
    public long PlayerValue;
    public long NpcValue;
    public double Shortage;
    public int Caravan, Guild, Hand, Maker;
    public int NpcStock;
}

public sealed class EventRow
{
    public int Hour;
    public int Town;
    public int Good;
    public NpcRole Role;
    public StabilizerEventKind Kind;
    public int Strength;
}

/// <summary>Records hourly and daily metrics plus every stabilizer event.</summary>
public sealed class MetricsRecorder
{
    private readonly World.World _w;
    private long _hourPlayerValue, _hourNpcValue;
    private long _dayFaucetStart, _daySinkStart;
    private readonly Dictionary<Flow, long> _flowStart = new();

    public MetricsRecorder(World.World world) { _w = world; }

    public List<HourRow> Hours { get; } = new();
    public List<DayRow> Days { get; } = new();
    public List<MarketDayRow> MarketDays { get; } = new();
    public List<EventRow> Events { get; } = new();

    public void RecordEvent(int town, int good, StabilizerEvent e) =>
        Events.Add(new EventRow { Hour = _w.Hour, Town = town, Good = good, Role = e.Role, Kind = e.Kind, Strength = e.Strength });

    public void RecordHour()
    {
        long pv = 0, nv = 0;
        // Hour stats were reset in EndHourMarkets; use the running day totals' change instead.
        foreach (var t in _w.Towns)
            foreach (var m in t.Markets) { pv += m.DayPlayerValue; nv += m.DayNpcValue; }
        double spreadSum = 0; int spreadN = 0;
        foreach (var t in _w.Towns)
            foreach (var m in t.Markets)
            {
                if (m.PlayerVolumeEma < 1) continue;
                var bid = m.Book.BestBid; var ask = m.Book.BestAsk;
                if (!bid.HasValue || !ask.HasValue) continue;
                spreadSum += Math.Min(2, Math.Max(0, ask.Value - bid.Value) / m.Reference.Value);
                spreadN++;
            }
        var row = new HourRow
        {
            Spread = spreadN > 0 ? spreadSum / spreadN : double.NaN,
            Hour = _w.Hour,
            ActiveMoney = _w.ActiveMoney(),
            Active = _w.ActiveAgentCount,
            Treasury = _w.Ledger.TreasuryBalance,
            Circulating = _w.Ledger.Circulating,
            PlayerValue = pv - _hourPlayerValue,
            NpcValue = nv - _hourNpcValue,
        };
        _hourPlayerValue = pv;
        _hourNpcValue = nv;
        if (_w.HourOfDay == 23) { _hourPlayerValue = 0; _hourNpcValue = 0; }
        Hours.Add(row);
    }

    public void RecordDay()
    {
        var w = _w;
        long active = w.ActiveMoney();
        int n = w.ActiveAgentCount;
        long dayPlayer = 0, dayNpc = 0;
        foreach (var t in w.Towns)
        {
            for (int g = 0; g < w.Goods.Count; g++)
            {
                var m = t.Markets[g];
                dayPlayer += m.DayPlayerValue;
                dayNpc += m.DayNpcValue;
                if (m.DayPlayerUnits == 0 && m.DayNpcUnits == 0 && !m.Stabilizer.AnyActive && m.Book.Sells.Count == 0 && m.Book.Buys.Count == 0) continue;
                MarketDays.Add(new MarketDayRow
                {
                    Day = w.Day,
                    Town = t.Index,
                    Good = g,
                    Price = PriceOf(m),
                    Reference = m.Reference.Value,
                    PlayerUnits = m.DayPlayerUnits,
                    NpcUnits = m.DayNpcUnits,
                    PlayerValue = m.DayPlayerValue,
                    NpcValue = m.DayNpcValue,
                    Shortage = m.DayShortageHours > 0 ? m.DayShortageSum / m.DayShortageHours : 0,
                    Caravan = m.Stabilizer.Strength(NpcRole.TraderCaravan),
                    Guild = m.Stabilizer.Strength(NpcRole.BuyerGuild),
                    Hand = m.Stabilizer.Strength(NpcRole.HiredHand),
                    Maker = m.Stabilizer.Strength(NpcRole.MarketMaker),
                    NpcStock = m.NpcStock,
                });
            }
        }

        var (master, median) = TokenHours();
        var row = new DayRow
        {
            Day = w.Day,
            Active = n,
            MoneyPerPlayer = n > 0 ? (double)active / n : 0,
            Treasury = w.Ledger.TreasuryBalance,
            Circulating = w.Ledger.Circulating,
            Faucets = w.Ledger.FaucetTotal - _dayFaucetStart,
            Sinks = w.Ledger.SinkTotal - _daySinkStart,
            Basket = Basket(),
            StarterKitHours = StarterKitHours(),
            TokenPrice = w.Tokens.LastPrice,
            TokenHoursMaster = master,
            TokenHoursMedian = median,
            Quits = w.QuitsToday,
            Joins = w.JoinsToday,
            PlayerValue = dayPlayer,
            NpcValue = dayNpc,
            ItemsInjected = w.ItemsInjected,
        };
        foreach (Flow f in Enum.GetValues(typeof(Flow)))
        {
            long total = w.Ledger.TotalFor(f);
            row.Flows[f] = total - _flowStart.GetValueOrDefault(f);
            _flowStart[f] = total;
        }
        _dayFaucetStart = w.Ledger.FaucetTotal;
        _daySinkStart = w.Ledger.SinkTotal;
        Days.Add(row);
    }

    public void Finish() { }

    public static double PriceOf(GoodMarket m) =>
        m.DayPlayerUnits > 0 ? (double)m.DayPlayerValue / m.DayPlayerUnits : m.LastPrice;

    /// <summary>Average across towns of bread + pickaxe + leather armor.</summary>
    public double Basket()
    {
        double sum = 0;
        foreach (var id in new[] { "bread", "pickaxe", "leather_armor" })
        {
            int g = _w.Goods[id];
            sum += _w.Towns.Average(t => PriceOf(t.Markets[g]));
        }
        return sum;
    }

    /// <summary>Hours of beginner mining needed to buy a pickaxe and three bread.</summary>
    public double StarterKitHours()
    {
        // In each town: a pickaxe and three loaves, paid for by gathering
        // whichever local raw material sells best, as a beginner (skill 15,
        // quality-35 tool) who pays market tax. Averaged over towns.
        int pick = _w.Goods["pickaxe"], bread = _w.Goods["bread"];
        double beginnerRate = _w.Config.GatherPerHour * (0.75 + 0.5 * 0.35) * (0.6 + 0.8 * 0.15) * (1 - _w.TaxRate);
        var hours = new List<double>();
        foreach (var t in _w.Towns)
        {
            double kit = PriceOf(t.Markets[pick]) + 3 * PriceOf(t.Markets[bread]);
            double best = 0;
            for (int g = 0; g < _w.Goods.Count; g++)
                if (t.Richness[g] > 0) best = Math.Max(best, PriceOf(t.Markets[g]));
            hours.Add(best > 0 ? kit / (beginnerRate * best) : double.PositiveInfinity);
        }
        return hours.Average();
    }

    /// <summary>Hours of play a top-10% earner and a median earner need to buy one token.</summary>
    private (double master, double median) TokenHours()
    {
        var rates = new List<double>();
        foreach (var a in _w.Agents)
        {
            if (!a.Active || a.Role is Role.Speculator or Role.Bot) continue;
            if (_w.Hour - a.JoinedHour < 24 * 7) continue;
            double hours = Math.Max(1, a.SessionHours * 7);
            rates.Add(a.DailyIncome.Sum() / hours);
        }
        if (rates.Count == 0) return (double.NaN, double.NaN);
        rates.Sort();
        double top = rates[(int)(rates.Count * 0.9)];
        double med = rates[rates.Count / 2];
        double price = _w.Tokens.LastPrice;
        return (top > 0 ? price / top : double.PositiveInfinity, med > 0 ? price / med : double.PositiveInfinity);
    }

    // ------------------------------------------------------------ CSV output

    public void WriteCsv(string directory)
    {
        Directory.CreateDirectory(directory);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine("day,active,money_per_player,treasury,circulating,faucets,sinks,basket,starter_kit_hours,token_price,token_hours_master,token_hours_median,quits,joins,player_value,npc_value,items_injected");
        foreach (var d in Days)
            sb.AppendLine(string.Join(",", d.Day, d.Active, F(d.MoneyPerPlayer), d.Treasury, d.Circulating, d.Faucets, d.Sinks, F(d.Basket), F(d.StarterKitHours),
                F(d.TokenPrice), F(d.TokenHoursMaster), F(d.TokenHoursMedian), d.Quits, d.Joins, d.PlayerValue, d.NpcValue, d.ItemsInjected));
        File.WriteAllText(Path.Combine(directory, "world_daily.csv"), sb.ToString());

        sb.Clear();
        sb.AppendLine("day," + string.Join(",", Enum.GetNames(typeof(Flow))));
        foreach (var d in Days)
            sb.AppendLine(d.Day + "," + string.Join(",", Enum.GetValues(typeof(Flow)).Cast<Flow>().Select(f => d.Flows.GetValueOrDefault(f))));
        File.WriteAllText(Path.Combine(directory, "flows_daily.csv"), sb.ToString());

        sb.Clear();
        sb.AppendLine("day,town,good,price,reference,player_units,npc_units,player_value,npc_value,shortage,caravan,buyer_guild,hired_hand,market_maker,npc_stock");
        foreach (var m in MarketDays)
            sb.AppendLine(string.Join(",", m.Day, _w.Towns[m.Town].Def.Id, _w.Goods.Defs[m.Good].Id, F(m.Price), F(m.Reference), m.PlayerUnits, m.NpcUnits,
                m.PlayerValue, m.NpcValue, F(m.Shortage), m.Caravan, m.Guild, m.Hand, m.Maker, m.NpcStock));
        File.WriteAllText(Path.Combine(directory, "markets_daily.csv"), sb.ToString());

        sb.Clear();
        sb.AppendLine("hour,active,active_money,treasury,circulating,player_value,npc_value");
        foreach (var h in Hours)
            sb.AppendLine(string.Join(",", h.Hour, h.Active, h.ActiveMoney, h.Treasury, h.Circulating, h.PlayerValue, h.NpcValue));
        File.WriteAllText(Path.Combine(directory, "world_hourly.csv"), sb.ToString());

        sb.Clear();
        sb.AppendLine("hour,town,good,role,event,strength");
        foreach (var e in Events)
            sb.AppendLine(string.Join(",", e.Hour, _w.Towns[e.Town].Def.Id, _w.Goods.Defs[e.Good].Id, e.Role, e.Kind, e.Strength));
        File.WriteAllText(Path.Combine(directory, "stabilizer_events.csv"), sb.ToString());

        string F(double v) => double.IsFinite(v) ? v.ToString("0.###", inv) : "";
    }
}
