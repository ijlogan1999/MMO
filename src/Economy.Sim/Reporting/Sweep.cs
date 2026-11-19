using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Economy.Core.Crafting;

namespace Economy.Sim.Reporting;

/// <summary>One pass/fail check, judged across every run it applies to.</summary>
public sealed class CriterionResult
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Scope { get; set; } = "";
    public int Runs { get; set; }
    public int Passed { get; set; }
    public double PassRate => Runs > 0 ? (double)Passed / Runs : 0;

    /// <summary>pass (at least 90% of runs), partial (at least half), fail.</summary>
    public string Status => Runs == 0 ? "n/a" : PassRate >= 0.9 ? "pass" : PassRate >= 0.5 ? "partial" : "fail";
    public string Detail { get; set; } = "";
}

public sealed class ScenarioResult
{
    public int Number { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Shock { get; set; } = "";
    public int ShockStartDay { get; set; }
    public int ShockEndDay { get; set; }
    public List<string> WatchedGoods { get; set; } = new();
    public int Seeds { get; set; }
    public int Recovered { get; set; }
    public double? RecoveryMedian { get; set; }
    public double? RecoveryMedianOff { get; set; }
    public int RecoveredOff { get; set; }
    public double PeakDeviationMedian { get; set; }
    public double NpcShareMedian { get; set; }
    public double TreasuryDriftMedian { get; set; }
    public double MoneyPerPlayerMedian { get; set; }
    public double KitHoursMedian { get; set; }
    public double? SpeculatorProfitMedian { get; set; }
    public int MaxSpawnsPerMarketWeek { get; set; }
    public double NightSpreadMedian { get; set; }
    public double EveningSpreadMedian { get; set; }
}

public sealed class SweepReport
{
    public string Generated { get; set; } = "";
    public int Seeds { get; set; }
    public int Days { get; set; }
    public int Agents { get; set; }
    public int Runs { get; set; }
    public double WallSeconds { get; set; }
    public List<CriterionResult> Criteria { get; set; } = new();
    public List<ScenarioResult> Scenarios { get; set; } = new();
    public List<RunSummary> RunSummaries { get; set; } = new();
    public List<RunSeries> Series { get; set; } = new();
}

/// <summary>
/// Runs every scenario over many seeds (and scenarios 2-11 again with the
/// stabilizer off, which is scenario 15), then judges the pass criteria.
/// </summary>
public static class Sweep
{
    /// <summary>Scenarios whose shock is reran with the stabilizer off (scenario 15).</summary>
    public static readonly int[] OffComparison = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

    public static SweepReport Run(int seeds, int days, int agents, Catalog catalog, World.WorldFile worldFile, Action<string>? log = null)
    {
        var jobs = new List<(IScenario s, ulong seed, bool on)>();
        foreach (var s in ScenarioCatalog.All)
            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                jobs.Add((s, seed, true));
                if (OffComparison.Contains(s.Number)) jobs.Add((s, seed, false));
            }

        var summaries = new ConcurrentBag<RunSummary>();
        var series = new ConcurrentBag<RunSeries>();
        int done = 0;
        var wall = Stopwatch.StartNew();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
        {
            var config = new SimConfig { Seed = job.seed, Days = days, AgentCount = agents, StabilizerEnabled = job.on };
            job.s.Configure(config);
            var sw = Stopwatch.StartNew();
            var world = new World.World(config, catalog, worldFile, job.s);
            world.Run();
            sw.Stop();
            summaries.Add(RunAnalysis.Summarize(world, job.s, sw.Elapsed.TotalSeconds));
            if (job.seed == 1) series.Add(RunAnalysis.Series(world, job.s));
            int n = Interlocked.Increment(ref done);
            if (n % 20 == 0 || n == jobs.Count) log?.Invoke($"  {n}/{jobs.Count} runs, {wall.Elapsed.TotalMinutes:0.0} min");
        });

        var runs = summaries.OrderBy(r => r.Scenario).ThenBy(r => r.Seed).ThenByDescending(r => r.Stabilizer).ToList();
        var report = new SweepReport
        {
            Generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"),
            Seeds = seeds,
            Days = days,
            Agents = agents,
            Runs = runs.Count,
            WallSeconds = Math.Round(wall.Elapsed.TotalSeconds, 1),
            RunSummaries = runs,
            Series = series.OrderBy(x => x.Scenario).ThenByDescending(x => x.Stabilizer).ToList(),
        };
        report.Scenarios = ScenarioCatalog.All.Select(s => ScenarioSummary(s, runs)).ToList();
        report.Criteria = Judge(runs);
        return report;
    }

    private static ScenarioResult ScenarioSummary(IScenario s, List<RunSummary> all)
    {
        var on = all.Where(r => r.Scenario == s.Number && r.Stabilizer).ToList();
        var off = all.Where(r => r.Scenario == s.Number && !r.Stabilizer).ToList();
        double? MedianOrNull(IEnumerable<double> xs) { var l = xs.ToList(); return l.Count > 0 ? RunAnalysis.Median(l) : null; }
        return new ScenarioResult
        {
            Number = s.Number,
            Id = s.Id,
            Name = s.Name,
            Shock = s.Shock,
            ShockStartDay = s.ShockStartDay,
            ShockEndDay = s.ShockEndDay,
            WatchedGoods = s.WatchedGoods.ToList(),
            Seeds = on.Count,
            Recovered = on.Count(r => r.RecoveryDays.HasValue),
            RecoveryMedian = MedianOrNull(on.Where(r => r.RecoveryDays.HasValue).Select(r => (double)r.RecoveryDays!.Value)),
            RecoveredOff = off.Count(r => r.RecoveryDays.HasValue),
            RecoveryMedianOff = MedianOrNull(off.Where(r => r.RecoveryDays.HasValue).Select(r => (double)r.RecoveryDays!.Value)),
            PeakDeviationMedian = RunAnalysis.Median(on.Select(r => r.PeakDeviation)),
            NpcShareMedian = RunAnalysis.Median(on.Select(r => r.NpcShare)),
            TreasuryDriftMedian = RunAnalysis.Median(on.Select(r => r.TreasuryDrift)),
            MoneyPerPlayerMedian = RunAnalysis.Median(on.Select(r => r.MoneyPerPlayerEnd)),
            KitHoursMedian = RunAnalysis.Median(on.Select(r => r.KitHoursMedian)),
            SpeculatorProfitMedian = s.ScoresSpeculators ? MedianOrNull(on.Select(r => (double)(r.SpeculatorProfit ?? 0))) : null,
            MaxSpawnsPerMarketWeek = on.Count > 0 ? on.Max(r => r.MaxSpawnsPerMarketWeek) : 0,
            NightSpreadMedian = RunAnalysis.Median(on.Select(r => r.NightSpread)),
            EveningSpreadMedian = RunAnalysis.Median(on.Select(r => r.EveningSpread)),
        };
    }

    private static List<CriterionResult> Judge(List<RunSummary> runs)
    {
        var on = runs.Where(r => r.Stabilizer).ToList();
        var baseline = on.Where(r => r.Scenario == 1).ToList();
        var shocks = on.Where(r => r.Scenario != 1 && r.Scenario != 14).ToList();
        var list = new List<CriterionResult>();

        CriterionResult C(int n, string name, string target, string scope, IEnumerable<RunSummary> rs, Func<RunSummary, bool> ok, string detail)
        {
            var l = rs.ToList();
            var c = new CriterionResult { Number = n, Name = name, Target = target, Scope = scope, Runs = l.Count, Passed = l.Count(ok), Detail = detail };
            list.Add(c);
            return c;
        }

        C(1, "Coins balance", "Every coin accounted for, every hour", "all runs", runs, r => r.LedgerBalanced,
            $"{runs.Count(r => r.LedgerBalanced)} of {runs.Count} runs balanced to the coin.");

        C(2, "Shocks recover", "Watched prices back in band within 30 days", "scenarios 2-13, 16", shocks,
            r => r.RecoveryDays is int d && d <= 30,
            $"Median {Fmt(RunAnalysis.Median(shocks.Where(r => r.RecoveryDays.HasValue).Select(r => (double)r.RecoveryDays!.Value)))} days; {shocks.Count(r => !r.RecoveryDays.HasValue)} runs never settled.");

        C(3, "NPCs stay small", "Under 10% of trade value in calm", "baseline", baseline, r => r.NpcShare < 0.10,
            $"Median NPC share {RunAnalysis.Median(baseline.Select(r => r.NpcShare)):P1}.");

        C(4, "No flicker", "At most 2 spawns per market, role and week", "all stabilizer-on runs", on, r => r.MaxSpawnsPerMarketWeek <= 2,
            $"Worst case {on.Max(r => r.MaxSpawnsPerMarketWeek)} spawns in a week; median share of market-weeks over 2: {RunAnalysis.Median(on.Select(r => r.FlickerShare)):P2}.");

        C(5, "Treasury holds", "Never negative; drifts under 25% in calm", "baseline (never negative: all)", baseline,
            r => r.TreasuryMin >= 0 && Math.Abs(r.TreasuryDrift) < 0.25,
            $"Median baseline drift {RunAnalysis.Median(baseline.Select(r => r.TreasuryDrift)):+0%;-0%}; lowest balance in any run {runs.Min(r => r.TreasuryMin):N0}.");

        C(6, "Prices steady", "Basket inflation under 2% a week and 15% over the run", "baseline", baseline,
            r => Math.Abs(r.BasketWeeklyInflation) < 0.02 && Math.Abs(r.BasketTotalInflation) < 0.15,
            $"Median basket change {RunAnalysis.Median(baseline.Select(r => r.BasketTotalInflation)):+0%;-0%} over the run.");

        C(7, "Cheap start", "Starter kit in under 2 hours of play", "baseline", baseline, r => r.KitHoursMedian < 2,
            $"Median {RunAnalysis.Median(baseline.Select(r => r.KitHoursMedian)):0.0} hours (starter quest); buying one at market would take {RunAnalysis.Median(baseline.Select(r => r.KitMarketHoursMedian)):0.0}.");

        var spec = on.Where(r => r.SpeculatorProfit.HasValue).ToList();
        C(8, "Cornering fails", "Speculators lose money", "scenarios 6 and 13", spec, r => r.SpeculatorProfit < 0,
            $"Median speculator group result {RunAnalysis.Median(spec.Select(r => (double)r.SpeculatorProfit!.Value)):N0} coins.");

        // Stabilizer on vs off, seed by seed. A run that never settles counts as the full run length.
        var pairs = new List<(RunSummary on, RunSummary off)>();
        foreach (var r in on.Where(r => OffComparison.Contains(r.Scenario)))
        {
            var o = runs.FirstOrDefault(x => !x.Stabilizer && x.Scenario == r.Scenario && x.Seed == r.Seed);
            if (o != null) pairs.Add((r, o));
        }
        var pairRuns = pairs.Select(p => p.on).ToList();
        bool Faster(RunSummary r)
        {
            var (a, b) = pairs.First(p => p.on == r);
            double ra = a.RecoveryDays ?? 1000, rb = b.RecoveryDays ?? 1000;
            if (ra <= 3 && rb <= 3) return true; // neither needed help
            return ra * 2 <= rb;
        }
        C(9, "Stabilizer earns its keep", "Recovers at least 2x faster than with it off", "scenarios 2-11 vs 15", pairRuns, Faster,
            $"{pairs.Count(p => (p.on.RecoveryDays ?? 1000) < (p.off.RecoveryDays ?? 1000))} of {pairs.Count} pairs recovered faster with it on.");

        C(10, "Token reachable", "Top earners under 20h, median under 60h per token", "baseline", baseline,
            r => r.TokenHoursMaster < 20 && r.TokenHoursMedian < 60,
            $"Median {RunAnalysis.Median(baseline.Select(r => r.TokenHoursMaster)):0}h for top earners, {RunAnalysis.Median(baseline.Select(r => r.TokenHoursMedian)):0}h for the median player.");

        C(11, "Fast to run", "Each run under 5 minutes", "all runs", runs, r => r.RuntimeSeconds < 300,
            $"Slowest run {runs.Max(r => r.RuntimeSeconds):0.0}s.");

        // The never-negative half of criterion 5 applies to every run.
        var negative = runs.Count(r => r.TreasuryMin < 0);
        if (negative > 0) list[4].Detail += $" {negative} runs went negative.";
        return list;
    }

    private static string Fmt(double x) => double.IsFinite(x) ? x.ToString("0") : "n/a";

    public static void Write(SweepReport report, string dir)
    {
        Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        File.WriteAllText(Path.Combine(dir, "sweep.json"), JsonSerializer.Serialize(report, opts));
        using var csv = new StreamWriter(Path.Combine(dir, "runs.csv"));
        csv.WriteLine("scenario,id,seed,stabilizer,runtime_s,ledger_ok,recovery_days,peak_dev,npc_share,max_spawns_week,treasury_min,treasury_drift,basket_total,kit_hours,token_master_h,token_median_h,spec_profit,money_per_player,items_injected");
        foreach (var r in report.RunSummaries)
            csv.WriteLine(string.Join(",", r.Scenario, r.ScenarioId, r.Seed, r.Stabilizer ? 1 : 0, r.RuntimeSeconds.ToString("0.0"), r.LedgerBalanced ? 1 : 0,
                r.RecoveryDays?.ToString() ?? "", r.PeakDeviation.ToString("0.000"), r.NpcShare.ToString("0.0000"), r.MaxSpawnsPerMarketWeek,
                r.TreasuryMin, r.TreasuryDrift.ToString("0.000"), r.BasketTotalInflation.ToString("0.000"), r.KitHoursMedian.ToString("0.00"),
                r.TokenHoursMaster.ToString("0.0"), r.TokenHoursMedian.ToString("0.0"), r.SpeculatorProfit?.ToString() ?? "", r.MoneyPerPlayerEnd.ToString("0"), r.ItemsInjected));
    }
}
