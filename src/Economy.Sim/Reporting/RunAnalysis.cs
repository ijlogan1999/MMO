using Economy.Core.Stabilizer;
using Economy.Sim.World;

namespace Economy.Sim.Reporting;

/// <summary>The numbers one finished run is judged on.</summary>
public sealed class RunSummary
{
    public int Scenario { get; set; }
    public string ScenarioId { get; set; } = "";
    public ulong Seed { get; set; }
    public bool Stabilizer { get; set; }
    public double RuntimeSeconds { get; set; }

    public bool LedgerBalanced { get; set; }

    /// <summary>Days from the end of the shock until watched prices settled; null if they never did.</summary>
    public int? RecoveryDays { get; set; }

    /// <summary>Largest move of the watched price index away from its pre-shock level.</summary>
    public double PeakDeviation { get; set; }

    public double NpcShare { get; set; }
    public int MaxSpawnsPerMarketWeek { get; set; }
    public double FlickerShare { get; set; }

    public long TreasuryMin { get; set; }
    public double TreasuryDrift { get; set; }

    public double BasketWeeklyInflation { get; set; }
    public double BasketTotalInflation { get; set; }

    public double KitHoursMedian { get; set; }
    public double KitMarketHoursMedian { get; set; }
    public double TokenHoursMaster { get; set; }
    public double TokenHoursMedian { get; set; }

    /// <summary>Total speculator profit (coins plus held goods at reference minus purse); null when no speculators.</summary>
    public long? SpeculatorProfit { get; set; }

    public double MoneyPerPlayerEnd { get; set; }

    /// <summary>Average spread 3-6 AM and 6-10 PM after warm-up (night-trough check).</summary>
    public double NightSpread { get; set; }
    public double EveningSpread { get; set; }
    public long ItemsInjected { get; set; }
}

/// <summary>Daily series kept for the dashboard (one run per scenario).</summary>
public sealed class RunSeries
{
    public int Scenario { get; set; }
    public bool Stabilizer { get; set; }
    public List<double> MoneyPerPlayer { get; set; } = new();
    public List<long> Treasury { get; set; } = new();
    public List<double> Basket { get; set; } = new();
    public List<double> KitHours { get; set; } = new();
    public List<double> TokenPrice { get; set; } = new();
    public List<double> NpcShare { get; set; } = new();
    public List<int> Active { get; set; } = new();

    /// <summary>Watched price index (1.0 = pre-shock level), smoothed over 7 days.</summary>
    public List<double> PriceIndex { get; set; } = new();

    /// <summary>Markets with each NPC role active at the end of each day: caravan, guild, hand, maker.</summary>
    public List<int[]> NpcRoles { get; set; } = new();

    /// <summary>Per watched good: average price across towns per day.</summary>
    public Dictionary<string, List<double>> WatchedPrices { get; set; } = new();
}

public static class RunAnalysis
{
    public const double Band = 0.25;

    public static RunSummary Summarize(World.World w, IScenario s, double seconds)
    {
        var m = w.Metrics;
        int warm = w.Config.WarmupDays;
        var days = m.Days.Where(d => d.Day >= warm).ToList();

        var summary = new RunSummary
        {
            Scenario = s.Number,
            ScenarioId = s.Id,
            Seed = w.Config.Seed,
            Stabilizer = w.Config.StabilizerEnabled,
            RuntimeSeconds = seconds,
            LedgerBalanced = LedgerBalances(w),
            ItemsInjected = w.ItemsInjected,
            MoneyPerPlayerEnd = m.Days[^1].MoneyPerPlayer,
        };

        var late = m.Hours.Where(h => h.Hour >= warm * 24 && double.IsFinite(h.Spread)).ToList();
        summary.NightSpread = late.Where(h => h.Hour % 24 is >= 3 and <= 6).Select(h => h.Spread).DefaultIfEmpty(0).Average();
        summary.EveningSpread = late.Where(h => h.Hour % 24 is >= 18 and <= 22).Select(h => h.Spread).DefaultIfEmpty(0).Average();

        var (index, _) = WatchedIndex(w, s);
        (summary.RecoveryDays, summary.PeakDeviation) = Recovery(index, s, w.Config.Days);

        long pv = days.Sum(d => d.PlayerValue), nv = days.Sum(d => d.NpcValue);
        summary.NpcShare = pv + nv > 0 ? (double)nv / (pv + nv) : 0;

        (summary.MaxSpawnsPerMarketWeek, summary.FlickerShare) = Flicker(w, warm);

        var treasuryHours = m.Hours.Where(h => h.Hour >= warm * 24).Select(h => h.Treasury).ToList();
        summary.TreasuryMin = treasuryHours.Count > 0 ? treasuryHours.Min() : m.Hours.Min(h => h.Treasury);
        long t0 = m.Days.First(d => d.Day == warm - 1).Treasury;
        summary.TreasuryDrift = t0 > 0 ? (double)(m.Days[^1].Treasury - t0) / t0 : 0;

        double b0 = Avg(m.Days, warm - 7, warm - 1, d => d.Basket);
        double b1 = Avg(m.Days, w.Config.Days - 7, w.Config.Days - 1, d => d.Basket);
        double weeks = (w.Config.Days - warm) / 7.0;
        summary.BasketTotalInflation = b0 > 0 ? b1 / b0 - 1 : 0;
        summary.BasketWeeklyInflation = b0 > 0 && b1 > 0 ? Math.Pow(b1 / b0, 1 / weeks) - 1 : 0;

        summary.KitHoursMedian = Median(days.Select(d => d.StarterKitHours));
        summary.KitMarketHoursMedian = Median(days.Select(d => d.StarterKitMarketHours));
        summary.TokenHoursMaster = Median(days.Select(d => d.TokenHoursMaster));
        summary.TokenHoursMedian = Median(days.Select(d => d.TokenHoursMedian));

        if (s.ScoresSpeculators)
        {
            long profit = 0;
            foreach (var a in w.Agents)
            {
                if (a.Role != Role.Speculator) continue;
                double held = a.TargetGood >= 0 ? a.Inventory[a.TargetGood] * w.Towns[a.Town].Markets[a.TargetGood].Reference.Value : 0;
                foreach (var oo in a.Orders)
                    if (oo.Good == a.TargetGood) held += oo.Order.Remaining * w.Towns[a.Town].Markets[a.TargetGood].Reference.Value;
                profit += w.Coins(a) + (long)held - a.SpecStartWealth;
            }
            summary.SpeculatorProfit = profit;
        }
        return summary;
    }

    public static RunSeries Series(World.World w, IScenario s)
    {
        var m = w.Metrics;
        var (index, perGood) = WatchedIndex(w, s);
        var series = new RunSeries { Scenario = s.Number, Stabilizer = w.Config.StabilizerEnabled };
        foreach (var d in m.Days)
        {
            series.MoneyPerPlayer.Add(Math.Round(d.MoneyPerPlayer, 1));
            series.Treasury.Add(d.Treasury);
            series.Basket.Add(Math.Round(d.Basket, 1));
            series.KitHours.Add(Clean(d.StarterKitMarketHours));
            series.TokenPrice.Add(Math.Round(d.TokenPrice));
            series.NpcShare.Add(d.PlayerValue + d.NpcValue > 0 ? Math.Round((double)d.NpcValue / (d.PlayerValue + d.NpcValue), 4) : 0);
            series.Active.Add(d.Active);
        }
        series.PriceIndex = Smooth(index).Select(x => Math.Round(x, 3)).ToList();
        foreach (var kv in perGood) series.WatchedPrices[kv.Key] = kv.Value.Select(x => Math.Round(x, 2)).ToList();

        var roles = new int[w.Config.Days][];
        for (int d = 0; d < roles.Length; d++) roles[d] = new int[4];
        foreach (var r in m.MarketDays)
        {
            if (r.Day >= roles.Length) continue;
            if (r.Caravan > 0) roles[r.Day][0]++;
            if (r.Guild > 0) roles[r.Day][1]++;
            if (r.Hand > 0) roles[r.Day][2]++;
            if (r.Maker > 0) roles[r.Day][3]++;
        }
        series.NpcRoles = roles.ToList();
        return series;
    }

    private static double Clean(double x) => double.IsFinite(x) ? Math.Round(x, 2) : -1;

    /// <summary>The ledger must hold every coin the mint issued, to the coin.</summary>
    private static bool LedgerBalances(World.World w)
    {
        try { w.Ledger.Verify(); return true; }
        catch (Economy.Core.Ledger.LedgerException) { return false; }
    }

    /// <summary>
    /// Daily price index of the watched goods, 1.0 = average over the week
    /// before the shock, weighted by each good's pre-shock trade value.
    /// With no watched goods, the six most traded goods are used.
    /// </summary>
    public static (List<double> index, Dictionary<string, List<double>> perGood) WatchedIndex(World.World w, IScenario s)
    {
        int days = w.Config.Days;
        int preFrom = Math.Max(0, s.ShockStartDay - 7), preTo = s.ShockStartDay - 1;
        var byGood = new Dictionary<int, (double[] value, double[] units, double[] priceSum, int[] priceN)>();

        var goods = s.WatchedGoods.Select(id => w.Goods[id]).ToList();
        if (goods.Count == 0)
        {
            goods = w.Metrics.MarketDays.Where(r => r.Day >= preFrom && r.Day <= preTo)
                .GroupBy(r => r.Good)
                .OrderByDescending(g => g.Sum(r => r.PlayerValue + r.NpcValue))
                .Take(6).Select(g => g.Key).ToList();
        }
        foreach (int g in goods) byGood[g] = (new double[days], new double[days], new double[days], new int[days]);
        foreach (var r in w.Metrics.MarketDays)
        {
            if (r.Day >= days || !byGood.TryGetValue(r.Good, out var acc)) continue;
            acc.value[r.Day] += r.PlayerValue + r.NpcValue;
            acc.units[r.Day] += r.PlayerUnits + r.NpcUnits;
            if (r.Price > 0) { acc.priceSum[r.Day] += r.Price; acc.priceN[r.Day]++; }
        }

        var prices = new Dictionary<int, double[]>();
        foreach (var (g, acc) in byGood)
        {
            var p = new double[days];
            double last = double.NaN;
            for (int d = 0; d < days; d++)
            {
                double v = acc.units[d] > 0 ? acc.value[d] / acc.units[d] : acc.priceN[d] > 0 ? acc.priceSum[d] / acc.priceN[d] : last;
                p[d] = v;
                if (!double.IsNaN(v)) last = v;
            }
            prices[g] = p;
        }

        var weights = new Dictionary<int, double>();
        var pre = new Dictionary<int, double>();
        foreach (int g in goods)
        {
            var vals = Enumerable.Range(preFrom, preTo - preFrom + 1).Select(d => prices[g][d]).Where(x => !double.IsNaN(x) && x > 0).ToList();
            pre[g] = vals.Count > 0 ? vals.Average() : double.NaN;
            weights[g] = Enumerable.Range(preFrom, preTo - preFrom + 1).Sum(d => byGood[g].value[d]);
        }
        double wsum = goods.Where(g => !double.IsNaN(pre[g])).Sum(g => weights[g]);
        if (wsum <= 0) foreach (int g in goods) weights[g] = 1;
        wsum = goods.Where(g => !double.IsNaN(pre[g])).Sum(g => weights[g]);

        var index = new List<double>();
        for (int d = 0; d < days; d++)
        {
            double sum = 0, wt = 0;
            foreach (int g in goods)
            {
                if (double.IsNaN(pre[g]) || double.IsNaN(prices[g][d])) continue;
                sum += weights[g] * prices[g][d] / pre[g];
                wt += weights[g];
            }
            index.Add(wt > 0 ? sum / wt : 1.0);
        }
        var perGood = goods.ToDictionary(g => w.Goods.Defs[g].Id, g => prices[g].Select(x => double.IsNaN(x) ? 0 : x).ToList());
        return (index, perGood);
    }

    /// <summary>
    /// Days until the watched index settles. For a shock that ends, settling
    /// means back within the band of the pre-shock level for 7 straight days.
    /// For a lasting change (a patch, bots that stay), it means holding a new
    /// level: 14 days in a row within 15% of where it stands.
    /// </summary>
    public static (int? days, double peak) Recovery(List<double> raw, IScenario s, int totalDays)
    {
        var idx = Smooth(raw);
        int start = s.ShockStartDay;
        double peak = 0;
        for (int d = start; d < idx.Count; d++) peak = Math.Max(peak, Math.Abs(idx[d] - 1));
        bool lasting = s.ShockEndDay >= totalDays;

        if (lasting)
        {
            for (int d = start; d + 14 < idx.Count; d++)
            {
                bool steady = true;
                for (int k = 1; k <= 14 && steady; k++) steady = Math.Abs(idx[d + k] / idx[d] - 1) <= 0.15;
                if (steady) return (d - start, peak);
            }
            return (null, peak);
        }

        int from = s.ShockEndDay;
        bool leftBand = false;
        for (int d = start; d < idx.Count; d++) if (Math.Abs(idx[d] - 1) > Band) { leftBand = true; break; }
        if (!leftBand) return (0, peak);
        for (int d = from; d + 7 <= idx.Count; d++)
        {
            bool ok = true;
            for (int k = 0; k < 7 && ok; k++) ok = Math.Abs(idx[d + k] - 1) <= Band;
            if (ok) return (d - from, peak);
        }
        return (null, peak);
    }

    /// <summary>Spawns per market, role and week after warm-up: the worst case, and the share of weeks over 2.</summary>
    private static (int max, double share) Flicker(World.World w, int warm)
    {
        var counts = new Dictionary<(int, int, NpcRole, int), int>();
        foreach (var e in w.Metrics.Events)
        {
            if (e.Kind != StabilizerEventKind.Spawn || e.Hour < warm * 24) continue;
            var key = (e.Town, e.Good, e.Role, e.Hour / (24 * 7));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        if (counts.Count == 0) return (0, 0);
        int over = counts.Values.Count(c => c > 2);
        return (counts.Values.Max(), (double)over / counts.Count);
    }

    public static List<double> Smooth(List<double> x, int window = 7)
    {
        var r = new List<double>(x.Count);
        for (int i = 0; i < x.Count; i++)
        {
            int from = Math.Max(0, i - window + 1);
            double sum = 0; int n = 0;
            for (int j = from; j <= i; j++) if (double.IsFinite(x[j])) { sum += x[j]; n++; }
            r.Add(n > 0 ? sum / n : 1.0);
        }
        return r;
    }

    private static double Avg(List<DayRow> days, int from, int to, Func<DayRow, double> f)
    {
        var v = days.Where(d => d.Day >= from && d.Day <= to).Select(f).Where(double.IsFinite).ToList();
        return v.Count > 0 ? v.Average() : 0;
    }

    public static double Median(IEnumerable<double> xs)
    {
        var v = xs.Where(double.IsFinite).OrderBy(x => x).ToList();
        if (v.Count == 0) return double.NaN;
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }
}
