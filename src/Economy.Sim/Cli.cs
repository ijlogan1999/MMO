using System.Diagnostics;
using Economy.Sim.World;

namespace Economy.Sim;

public static class Cli
{
    public static int Run(string[] args)
    {
        string scenarioId = Arg(args, "--scenario") ?? "baseline";
        ulong seed = ulong.Parse(Arg(args, "--seed") ?? "1");
        int days = int.Parse(Arg(args, "--days") ?? "90");
        int agents = int.Parse(Arg(args, "--agents") ?? "2000");
        string? outDir = Arg(args, "--out");

        var dataDir = DataLoader.FindDataDirectory();
        var catalog = DataLoader.LoadCatalog(dataDir);
        var worldFile = WorldFile.Load(dataDir);

        if (args.Contains("--calibrate"))
        {
            // Runs the calm baseline over a few seeds and writes the prices the
            // market settled at, so later runs start close to equilibrium
            // instead of spending months drifting there.
            int seeds = int.Parse(Arg(args, "--seeds") ?? "4");
            var value = new System.Collections.Concurrent.ConcurrentDictionary<string, (double v, double u)>();
            Parallel.For(1, seeds + 1, seed =>
            {
                var cfg = new SimConfig { Seed = (ulong)seed, Days = days, AgentCount = agents };
                var w = new World.World(cfg, catalog, worldFile, ScenarioCatalog.Get("baseline"));
                w.Run();
                foreach (var r in w.Metrics.MarketDays.Where(r => r.Day >= days - 60 && r.PlayerUnits > 0))
                    value.AddOrUpdate(w.Goods.Defs[r.Good].Id, (r.PlayerValue, r.PlayerUnits), (_, old) => (old.v + r.PlayerValue, old.u + r.PlayerUnits));
            });
            // Prices pinned at the sellers' floor say "too much supply", not
            // "this is the right price", so never start below 25% of the design price.
            worldFile.StartPrices.Clear();
            var design = new World.GoodsTable(catalog, worldFile.RawPrices());
            var prices = value.Where(kv => kv.Value.u >= 5).OrderBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key, kv => Math.Round(Math.Max(kv.Value.v / kv.Value.u, design.BasePrice[design[kv.Key]] * 0.25), 1));
            var path = Path.Combine(dataDir, WorldFile.StartPricesFile);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(prices, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
            foreach (var kv in prices) Console.WriteLine($"  {kv.Key,-16} {kv.Value,9:0.0}");
            Console.WriteLine($"Wrote {path} ({prices.Count} goods).");
            return 0;
        }

        if (args.Contains("--sensitivity"))
        {
            int seeds = int.Parse(Arg(args, "--seeds") ?? "4");
            double nudge = double.Parse(Arg(args, "--nudge") ?? "0.3", System.Globalization.CultureInfo.InvariantCulture);
            string dir = outDir ?? "results";
            Console.WriteLine($"Sensitivity: {Reporting.Sensitivity.Settings.Length} settings x2 at ±{nudge:P0}, {seeds} seeds, {days} days.");
            var (b, rows) = Reporting.Sensitivity.Run(seeds, days, agents, nudge, catalog, worldFile, Console.WriteLine);
            Reporting.Sensitivity.Write(b, rows, dir);
            Console.WriteLine($"  baseline: basket {b.Basket:+0%;-0%} npc {b.Npc:P1} treasury {b.Treasury:+0%;-0%} token {b.TokenMedian:0}h money {b.Money:0} pay spread x{b.PayRange:0.0}");
            foreach (var g in rows.GroupBy(r => r.Setting).OrderByDescending(g => g.Max(r => Reporting.Sensitivity.Impact(b, r.Result))))
                foreach (var r in g)
                    Console.WriteLine($"  {r.Setting,-22} x{r.Factor:0.00}  impact {Reporting.Sensitivity.Impact(b, r.Result),5:0.00}  basket {r.Result.Basket:+0%;-0%} npc {r.Result.Npc:P1} treasury {r.Result.Treasury:+0%;-0%} token {r.Result.TokenMedian:0}h money {r.Result.Money:0} pay spread x{r.Result.PayRange:0.0}");
            Console.WriteLine($"Wrote {dir}/sensitivity.csv.");
            return 0;
        }

        if (args.Contains("--sweep"))
        {
            int seeds = int.Parse(Arg(args, "--seeds") ?? "20");
            string dir = outDir ?? "results";
            Console.WriteLine($"Sweep: {ScenarioCatalog.All.Count} scenarios x {seeds} seeds, {days} days, {agents} players, {Environment.ProcessorCount} cores.");
            var report = Reporting.Sweep.Run(seeds, days, agents, catalog, worldFile, Console.WriteLine);
            Reporting.Sweep.Write(report, dir);
            foreach (var c in report.Criteria)
                Console.WriteLine($"{c.Number,2}. {c.Name,-26} {c.Status,-8} {c.Passed,3}/{c.Runs,-3}  {c.Detail}");
            Console.WriteLine();
            foreach (var s in report.Scenarios)
                Console.WriteLine($"{s.Number,2} {s.Id,-18} recovered {s.Recovered,2}/{s.Seeds}  median {(s.RecoveryMedian?.ToString("0") ?? "-"),3}d  off: {s.RecoveredOff,2} median {(s.RecoveryMedianOff?.ToString("0") ?? "-"),3}d  peak {s.PeakDeviationMedian:P0}  npc {s.NpcShareMedian:P1}  drift {s.TreasuryDriftMedian:+0%;-0%}  money {s.MoneyPerPlayerMedian:0}");
            Console.WriteLine($"Wrote {dir}/sweep.json and runs.csv in {report.WallSeconds / 60:0.0} min.");
            return report.Criteria.Any(c => c.Number == 1 && c.Status != "pass") ? 1 : 0;
        }

        var scenario = ScenarioCatalog.Get(scenarioId);
        var config = new SimConfig { Seed = seed, Days = days, AgentCount = agents, StabilizerEnabled = !args.Contains("--no-stabilizer") };
        scenario.Configure(config);

        var sw = Stopwatch.StartNew();
        var world = new World.World(config, catalog, worldFile, scenario);
        world.Run(w =>
        {
            if (w.Day % 10 != 9 && !args.Contains("--verbose")) return;
            var d = w.Metrics.Days[^1];
            Console.WriteLine($"day {d.Day + 1,3}: active {d.Active,5}  money/player {d.MoneyPerPlayer,7:0}  treasury {d.Treasury,9}  basket {d.Basket,7:0.0}  kit {d.StarterKitHours,5:0.0}h  token {d.TokenPrice,6:0}  npc {(d.PlayerValue + d.NpcValue > 0 ? 100.0 * d.NpcValue / (d.PlayerValue + d.NpcValue) : 0),5:0.0}%");
        });
        if (args.Contains("--roles"))
        {
            var inc = world.RoleIncome();
            foreach (var g in world.Agents.Where(a => a.Active).GroupBy(a => a.Role).OrderBy(g => g.Key))
                Console.WriteLine($"  {g.Key,-14} {g.Count(),5}  income x{inc.GetValueOrDefault(g.Key):0.00}  coins {g.Average(a => world.Coins(a)),7:0}");
            Console.WriteLine($"  role switches {world.RoleSwitches}, items injected {world.ItemsInjected}");
            Console.WriteLine($"  parties {world.PartiesFormed} (full {world.PartiesFull}), wipes {world.PartyWipes}, units {world.PartyUnits}, hired tanks {world.NpcTanksHired} healers {world.NpcHealersHired} porters {world.NpcPortersHired}, deaths {world.Deaths}, furniture placed {world.FurniturePlaced}, shops {world.Agents.Count(a => a.HasShop)}");
            if (Arg(args, "--inspect") is string inspect && Enum.TryParse<Role>(inspect, true, out var ir))
                foreach (var a in world.Agents.Where(a => a.Active && a.Role == ir).Take(8))
                    Console.WriteLine($"    #{a.Id} {world.Towns[a.Town].Def.Id} skill {a.Skill:0} coins {world.Coins(a)} income {string.Join('/', a.DailyIncome)} inv " +
                        string.Join(", ", Enumerable.Range(0, world.Goods.Count).Where(g => a.Inventory[g] > 0).Select(g => $"{world.Goods.Defs[g].Id}:{a.Inventory[g]}")) +
                        " orders " + string.Join(", ", a.Orders.Select(o => $"{(o.Order.Side == Economy.Core.Markets.Side.Buy ? "B" : "S")} {(o.Good < world.Goods.Count ? world.Goods.Defs[o.Good].Id : "token")} {o.Order.Remaining}@{o.Order.Price}")));
        }
        Console.WriteLine($"Finished {scenario.Name}, seed {seed}, {days} days, in {sw.Elapsed.TotalSeconds:0.0}s.");
        if (outDir != null) world.Metrics.WriteCsv(outDir);
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
