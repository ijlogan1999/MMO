using Economy.Core.Crafting;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Economy.Sim.Reporting;

/// <summary>
/// "Which numbers matter": runs the calm baseline with each key setting
/// nudged down and up (default 30%) and reports how far each outcome moves
/// from the unchanged run. Big movers are the numbers to watch in the game.
/// </summary>
public static class Sensitivity
{
    public static readonly string[] Settings =
    {
        nameof(SimConfig.PotionsPerFightHour), nameof(SimConfig.FoodPerHour), nameof(SimConfig.BuffPowerBonus),
        nameof(SimConfig.WeaponWearPerHour), nameof(SimConfig.ArmorWearPerHour), nameof(SimConfig.ContractShareOfSinks),
        nameof(SimConfig.GatherPerHour), nameof(SimConfig.FarmYieldPerHour), nameof(SimConfig.SoloGatherYield),
        nameof(SimConfig.PartyGatherPerHour), nameof(SimConfig.PartyCarryPerMember), nameof(SimConfig.PartyCarryPerHauler),
        nameof(SimConfig.DeepRegenShare), nameof(SimConfig.PartyTripChance), nameof(SimConfig.FocusFightShare),
        nameof(SimConfig.RelicCoreDropPerHour), nameof(SimConfig.StationFee), nameof(SimConfig.NpcTankFee),
    };

    public sealed record Outcome(double Basket, double Npc, double Treasury, double TokenMedian, double Money, double PayRange);

    public sealed record Row(string Setting, double Factor, Outcome Result);

    public static (Outcome baseline, List<Row> rows) Run(int seeds, int days, int agents, double nudge, Catalog catalog, World.WorldFile worldFile, Action<string>? log)
    {
        var jobs = new List<(string? setting, double factor, ulong seed)>();
        for (ulong s = 1; s <= (ulong)seeds; s++)
        {
            jobs.Add((null, 1, s));
            foreach (var name in Settings)
            {
                jobs.Add((name, 1 - nudge, s));
                jobs.Add((name, 1 + nudge, s));
            }
        }
        var results = new ConcurrentBag<(string? setting, double factor, RunSummary r)>();
        int done = 0;
        var scenario = ScenarioCatalog.Get("baseline");
        Parallel.ForEach(Partitioner.Create(jobs, EnumerablePartitionerOptions.NoBuffering), job =>
        {
            var config = new SimConfig { Seed = job.seed, Days = days, AgentCount = agents };
            if (job.setting != null) Scale(config, job.setting, job.factor);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var world = new World.World(config, catalog, worldFile, scenario);
            world.Run();
            results.Add((job.setting, job.factor, RunAnalysis.Summarize(world, scenario, sw.Elapsed.TotalSeconds)));
            int n = Interlocked.Increment(ref done);
            if (n % 20 == 0 || n == jobs.Count) log?.Invoke($"  {n}/{jobs.Count} runs");
        });

        Outcome Of(IEnumerable<RunSummary> rs)
        {
            var l = rs.ToList();
            return new Outcome(
                RunAnalysis.Median(l.Select(r => r.BasketTotalInflation)),
                RunAnalysis.Median(l.Select(r => r.NpcShare)),
                RunAnalysis.Median(l.Select(r => r.TreasuryDrift)),
                RunAnalysis.Median(l.Select(r => r.TokenHoursMedian)),
                RunAnalysis.Median(l.Select(r => r.MoneyPerPlayerEnd)),
                RunAnalysis.Median(l.Select(r => r.RoleIncome.Count > 0 ? r.RoleIncome.Values.Max() / Math.Max(0.01, r.RoleIncome.Values.Min()) : 0)));
        }

        var baseline = Of(results.Where(x => x.setting == null).Select(x => x.r));
        var rows = results.Where(x => x.setting != null)
            .GroupBy(x => (x.setting!, x.factor))
            .Select(g => new Row(g.Key.Item1, g.Key.factor, Of(g.Select(x => x.r))))
            .OrderBy(r => Array.IndexOf(Settings, r.Setting)).ThenBy(r => r.Factor).ToList();
        return (baseline, rows);
    }

    private static void Scale(SimConfig c, string name, double factor)
    {
        var p = typeof(SimConfig).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"No setting {name}");
        object v = p.GetValue(c)!;
        object scaled = v switch
        {
            double d => (object)(d * factor),
            long l => (object)(long)Math.Round(l * factor),
            int i => (object)(int)Math.Round(i * factor),
            _ => throw new ArgumentException($"{name} is not a number"),
        };
        p.SetValue(c, scaled);
    }

    /// <summary>How much a row moved the economy, as one score: the biggest normalized change.</summary>
    public static double Impact(Outcome b, Outcome o) => new[]
    {
        Math.Abs(o.Basket - b.Basket) / 0.15,
        Math.Abs(o.Npc - b.Npc) / 0.10,
        Math.Abs(o.Treasury - b.Treasury) / 0.25,
        Math.Abs(o.TokenMedian - b.TokenMedian) / Math.Max(1, b.TokenMedian),
        Math.Abs(o.Money - b.Money) / Math.Max(1, b.Money),
    }.Max();

    public static void Write(Outcome baseline, List<Row> rows, string dir)
    {
        Directory.CreateDirectory(dir);
        using var csv = new StreamWriter(Path.Combine(dir, "sensitivity.csv"));
        var ci = CultureInfo.InvariantCulture;
        csv.WriteLine("setting,factor,basket_change,npc_share,treasury_drift,token_median_h,money_per_player,pay_spread,impact");
        csv.WriteLine(string.Join(",", "baseline", "1", baseline.Basket.ToString("0.000", ci), baseline.Npc.ToString("0.000", ci), baseline.Treasury.ToString("0.000", ci),
            baseline.TokenMedian.ToString("0.0", ci), baseline.Money.ToString("0", ci), baseline.PayRange.ToString("0.0", ci), "0"));
        foreach (var r in rows)
            csv.WriteLine(string.Join(",", r.Setting, r.Factor.ToString("0.00", ci), r.Result.Basket.ToString("0.000", ci), r.Result.Npc.ToString("0.000", ci),
                r.Result.Treasury.ToString("0.000", ci), r.Result.TokenMedian.ToString("0.0", ci), r.Result.Money.ToString("0", ci),
                r.Result.PayRange.ToString("0.0", ci), Impact(baseline, r.Result).ToString("0.00", ci)));
    }
}
