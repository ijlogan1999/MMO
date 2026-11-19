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
            // Gear trades rarely, so a handful of sales to rich players can set an
            // absurd price nobody else can pay. Keep gear within reach of its recipe cost.
            double Clamp(string id, double traded)
            {
                double d = design.BasePrice[design[id]];
                return design.Defs[design[id]].HasDurability ? Math.Clamp(traded, d * 0.5, d * 1.5) : Math.Max(traded, d * 0.25);
            }
            var prices = value.Where(kv => kv.Value.u >= 5).OrderBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key, kv => Math.Round(Clamp(kv.Key, kv.Value.v / kv.Value.u), 1));
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

        if (args.Contains("--diagnose"))
        {
            // Pay breakdown by role and where each good ends up, for digging into fair pay.
            int seeds = int.Parse(Arg(args, "--seeds") ?? "6");
            bool noTasks = args.Contains("--no-tasks");
            string dir = outDir ?? "results/diagnose";
            Directory.CreateDirectory(dir);
            var results = new System.Collections.Concurrent.ConcurrentBag<object>();
            Parallel.For(1, seeds + 1, s =>
            {
                var cfg = new SimConfig { Seed = (ulong)s, Days = days, AgentCount = agents };
                if (noTasks) cfg.DeliveryTasks = false;
                var w = new World.World(cfg, catalog, worldFile, ScenarioCatalog.Get("baseline"));
                w.Run();
                var roles = Enum.GetValues<Role>();
                var kinds = Enum.GetValues<IncomeKind>();
                var flows = Enum.GetValues<GoodFlowKind>();
                results.Add(new
                {
                    seed = s,
                    roleIncome = w.AverageRoleIncome().ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                    roles = roles.ToDictionary(r => r.ToString(), r => new
                    {
                        hours = w.RoleHours[(int)r],
                        agentDays = w.RoleAgentDays[(int)r],
                        active = w.Agents.Count(a => a.Active && a.Role == r),
                        parts = kinds.ToDictionary(k => k.ToString(), k => w.IncomeParts[(int)r, (int)k]),
                    }),
                    goods = Enumerable.Range(0, w.Goods.Count).ToDictionary(g => w.Goods.Defs[g].Id, g => flows.ToDictionary(k => k.ToString(), k => w.GoodFlows[g, (int)k])),
                    prices = Enumerable.Range(0, w.Goods.Count).ToDictionary(g => w.Goods.Defs[g].Id, g => w.DailyPrices.Select(d => Math.Round(d[g], 1)).ToArray()),
                    taskUnits = w.TaskUnitsDelivered, taskCoins = w.TaskCoinsPaid, haulFees = w.HaulFeesPaid,
                    buybackUnits = w.BuybackUnits, buybackCoins = w.BuybackCoins, courierPosted = w.CourierJobsPosted, courierDelivered = w.CourierJobsDelivered, courierFees = w.CourierFeesPaid,
                });
                Console.WriteLine($"  seed {s} done");
            });
            var file = Path.Combine(dir, noTasks ? "diagnose-notasks.json" : "diagnose.json");
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(results.ToArray()));
            Console.WriteLine($"Wrote {file}.");
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
            if (Arg(args, "--watch-role") is string wr && Enum.TryParse<Role>(wr, true, out var watch) && w.Day % 10 == 9)
            {
                var g = w.Agents.Where(a => a.Active && a.Role == watch).ToList();
                if (g.Count > 0)
                    Console.WriteLine($"  {watch} day {w.Day + 1}: n {g.Count} coins avg {g.Average(a => w.Coins(a)):0} median {g.Select(a => w.Coins(a)).OrderBy(c => c).ElementAt(g.Count / 2)} skill avg {g.Average(a => a.Skill):0} max {g.Max(a => a.Skill):0} >=60: {g.Count(a => a.Skill >= 60)} new(<10d) {g.Count(a => w.Hour - a.JoinedHour < 240)}");
            }
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
            Console.WriteLine($"  tasks: {world.TaskUnitsDelivered} units for {world.TaskCoinsPaid} coins; haul fees {world.HaulFeesPaid}; players hired {world.PlayersHired}");
            Console.WriteLine($"  NPC buyback {world.BuybackUnits} units for {world.BuybackCoins}; courier jobs posted {world.CourierJobsPosted}, delivered {world.CourierJobsDelivered}, fees {world.CourierFeesPaid}, cargo lost {world.CourierCargoLost}");
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
