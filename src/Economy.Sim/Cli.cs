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
            double calibrateCap = double.Parse(Arg(args, "--cost-cap") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            double thinCap = double.Parse(Arg(args, "--thin-cap") ?? "1.3", System.Globalization.CultureInfo.InvariantCulture);
            double thinUnits = double.Parse(Arg(args, "--thin-units") ?? "1000", System.Globalization.CultureInfo.InvariantCulture);
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
            // Gear's anchor is what its materials cost at today's market prices
            // plus the crafter's time, not the design price list, which runs far
            // above where raw materials actually trade.
            var traded = value.Where(kv => kv.Value.u >= 5).ToDictionary(kv => kv.Key, kv => kv.Value.v / kv.Value.u);
            double MarketCost(string id, int depth = 0)
            {
                int g = design[id];
                var r = design.RecipesByOutput[g].FirstOrDefault();
                if (r == null || depth > 6) return traded.TryGetValue(id, out double t) ? t : design.BasePrice[g];
                double inputs = r.Inputs.Sum(i => i.Quantity * (!design.Defs[design[i.GoodId]].HasDurability && traded.TryGetValue(i.GoodId, out double p) ? p : MarketCost(i.GoodId, depth + 1)));
                return (inputs * 1.25 + r.CraftMinutes * 5) / r.OutputQuantity;
            }
            // Crafted materials and consumables: a market price far above what it
            // costs to make (inputs at their own capped prices plus time) is a
            // leftover from thin early trading, so cap it at a few times cost.
            var capped = new Dictionary<string, double>();
            double CappedPrice(string id, int depth = 0)
            {
                if (capped.TryGetValue(id, out double c)) return c;
                int g = design[id];
                double t = traded.TryGetValue(id, out double tp) ? tp : design.BasePrice[g];
                var r = design.RecipesByOutput[g].FirstOrDefault();
                // Rarely sold goods (Ian, 2026-11-20): a few sales at whatever the one
                // seller asked say nothing about worth; price them near making cost.
                bool thin = !value.TryGetValue(id, out var vu) || vu.u < thinUnits;
                double cap = thin ? thinCap : calibrateCap;
                if (r != null && depth < 8 && !design.Defs[g].HasDurability && cap > 0)
                {
                    double cost = (r.Inputs.Sum(i => i.Quantity * (design.Defs[design[i.GoodId]].HasDurability ? MarketCost(i.GoodId) : CappedPrice(i.GoodId, depth + 1))) * 1.25 + r.CraftMinutes * 5) / r.OutputQuantity;
                    t = Math.Min(t, cost * cap);
                }
                return capped[id] = t;
            }
            double Clamp(string id, double tradedPrice)
            {
                double d = design.BasePrice[design[id]];
                if (!design.Defs[design[id]].HasDurability) return Math.Max(Math.Min(tradedPrice, CappedPrice(id)), d * 0.25);
                double m = MarketCost(id);
                return Math.Clamp(tradedPrice, m * 0.8, m * 1.5);
            }
            var prices = value.Where(kv => kv.Value.u >= 5).OrderBy(kv => kv.Key)
                .ToDictionary(kv => kv.Key, kv => Math.Round(Clamp(kv.Key, kv.Value.v / kv.Value.u), 1));
            // Higher-band gear barely trades in a calm run; price it from its
            // band-1 piece's market price, scaled by how much more its recipe costs.
            foreach (var baseId in LevelBands.BandedGear)
                foreach (var bid in design.Defs.Select(d => d.Id).Where(x => x != baseId && (x.StartsWith(baseId + "_b") || x.StartsWith(baseId + "_s"))))
                {
                    if (prices.ContainsKey(bid) || !prices.TryGetValue(baseId, out double basePrice)) continue;
                    prices[bid] = Math.Round(basePrice * MarketCost(bid) / MarketCost(baseId), 1);
                }
            prices = prices.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
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
                ApplySets(args, cfg);
                var scen = ScenarioCatalog.Get("baseline");
                var w = new World.World(cfg, catalog, worldFile, scen);
                var sw2 = Stopwatch.StartNew();
                w.Run();
                var sum = Reporting.RunAnalysis.Summarize(w, scen, sw2.Elapsed.TotalSeconds);
                var roles = Enum.GetValues<Role>();
                var kinds = Enum.GetValues<IncomeKind>();
                var flows = Enum.GetValues<GoodFlowKind>();
                results.Add(new
                {
                    seed = s,
                    npcShare = sum.NpcShare, treasuryDrift = sum.TreasuryDrift, basket = sum.BasketTotalInflation, tokenMedian = sum.TokenHoursMedian, moneyEnd = sum.MoneyPerPlayerEnd,
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
                    armedShare = w.Agents.Count(a => a.Active && a.Role is not (Role.Bot or Role.Speculator) && a.Weapon != null) / (double)Math.Max(1, w.Agents.Count(a => a.Active && a.Role is not (Role.Bot or Role.Speculator))),
                    upgradeGapEarly = w.UpgradeGapsEarly.Count == 0 ? -1 : w.UpgradeGapsEarly.OrderBy(v => v).ElementAt(w.UpgradeGapsEarly.Count / 2),
                    upgradesEarly = w.UpgradeGapsEarly.Count,
                    medianCoins = w.Agents.Where(a => a.Active && a.Role is not (Role.Bot or Role.Speculator)).Select(a => w.Coins(a)).OrderBy(c => c).ElementAt(w.Agents.Count(a => a.Active && a.Role is not (Role.Bot or Role.Speculator)) / 2),
                    buybackUnits = w.BuybackUnits, buybackCoins = w.BuybackCoins, courierPosted = w.CourierJobsPosted, courierDelivered = w.CourierJobsDelivered, courierFees = w.CourierFeesPaid,
                });
                Console.WriteLine($"  seed {s} done");
            });
            var file = Path.Combine(dir, Arg(args, "--name") is string nm ? nm + ".json" : noTasks ? "diagnose-notasks.json" : "diagnose.json");
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
        ApplySets(args, config);

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
            if (args.Contains("--basket")) Console.WriteLine("  basket parts: " + string.Join(" ", Reporting.MetricsRecorder.BasketItems.Select((b, i) => $"{b.id} {d.BasketParts[i]:0}")));
            Console.WriteLine($"day {d.Day + 1,3}: active {d.Active,5}  money/player {d.MoneyPerPlayer,7:0}  treasury {d.Treasury,9}  basket {d.Basket,7:0.0}  kit {d.StarterKitHours,5:0.0}h  token {d.TokenPrice,6:0}  npc {(d.PlayerValue + d.NpcValue > 0 ? 100.0 * d.NpcValue / (d.PlayerValue + d.NpcValue) : 0),5:0.0}%");
        });
        if (args.Contains("--roles"))
        {
            if (Arg(args, "--trades") is string tg)
                foreach (var id in tg.Split(','))
                {
                    int gi = world.Goods[id];
                    var rows = world.TradeByRole.Where(kv => kv.Key.good == gi).OrderByDescending(kv => kv.Value).Take(8)
                        .Select(kv => $"{(Role)kv.Key.buyer}<-{(kv.Key.seller < 0 ? "NPC" : ((Role)kv.Key.seller).ToString())} {kv.Value}");
                    Console.WriteLine($"  trades {id}: {string.Join(", ", rows)}");
                }
            var inc = world.RoleIncome();
            foreach (var g in world.Agents.Where(a => a.Active).GroupBy(a => a.Role).OrderBy(g => g.Key))
                Console.WriteLine($"  {g.Key,-14} {g.Count(),5}  income x{inc.GetValueOrDefault(g.Key):0.00}  coins {Economy.Core.Currency.Money.Format(g.Average(a => world.Coins(a))),12}");
            Console.WriteLine($"  role switches {world.RoleSwitches} (blocked by the fee {world.RoleSwitchesBlocked}), items injected {world.ItemsInjected}");
            Console.WriteLine($"  parties {world.PartiesFormed} (full {world.PartiesFull}), wipes {world.PartyWipes}, units {world.PartyUnits}, hired tanks {world.NpcTanksHired} healers {world.NpcHealersHired} porters {world.NpcPortersHired}, deaths {world.Deaths}, furniture placed {world.FurniturePlaced}, shops {world.Agents.Count(a => a.HasShop)}");
            {
                var hl = world.Agents.Where(a => a.Active && a.Role == Role.Hauler).ToList();
                Console.WriteLine($"  horses: bought {world.HorsesBought}, haulers {hl.Count} (level 21+ {hl.Count(a => World.World.LevelOf(a) >= config.HorseMinLevel)}, with horse {hl.Count(a => a.HasHorse)}, saddled {hl.Count(a => a.HasHorse && a.Saddle != null && a.Saddle.IsUsable)}), mounted trips {world.MountedTrips}");
            }
            {
                var mdays = world.Metrics.Days;
                int d0 = Math.Min(mdays.Count - 1, config.WarmupDays);
                Console.WriteLine("  basket paid price (warm-up end -> last day): " + string.Join(", ", Reporting.MetricsRecorder.BasketItems.Select((b, i) =>
                    $"{b.id} {mdays[d0].BasketParts[i]:0} -> {mdays[^1].BasketParts[i]:0}")));
            }
            Console.WriteLine($"  tasks: {world.TaskUnitsDelivered} units for {world.TaskCoinsPaid} coins; haul fees {world.HaulFeesPaid}; players hired {world.PlayersHired}");
            var lv = world.Agents.Where(a => a.Active && a.Role is not (Role.Bot or Role.Speculator)).Select(World.World.LevelOf).ToList();
            Console.WriteLine($"  levels: band1 {lv.Count(l => l <= 20)}, band2 {lv.Count(l => l is > 20 and <= 40)}, band3 {lv.Count(l => l is > 40 and <= 60)}, band4 {lv.Count(l => l > 60)}; median {lv.OrderBy(l => l).ElementAt(lv.Count / 2)}");
            foreach (var id in new[] { "iron_ore", "copper_ore", "iron_ingot", "copper_ingot", "alloy_ingot", "sword", "sword_s2", "sword_s5", "sword_b2", "sword_b3", "sword_b4", "leather_armor", "leather_armor_b2", "leather_armor_b3", "leather_armor_b4", "saddle", "travel_pack" })
            {
                int g = world.Goods[id];
                Console.Write($"  {id} ref {Economy.Core.Currency.Money.Format(world.Towns[0].Markets[g].Reference.Value)} wearing {world.Agents.Count(a => a.Active && (a.Weapon?.Good.Id == id || a.Armor?.Good.Id == id))};");
            }
            Console.WriteLine();
            var players = world.Agents.Where(a => a.Active && a.Role is not (Role.Bot or Role.Speculator)).ToList();
            int armed = players.Count(a => a.Weapon != null), armored = players.Count(a => a.Armor != null);
            int upToDate = players.Count(a => a.Weapon != null && LevelBands.RankOf(a.Weapon.Good.Id) >= LevelBands.WearableRank("sword", World.World.LevelOf(a)));
            var behind = players.Where(a => a.Weapon != null && LevelBands.RankOf(a.Weapon.Good.Id) < LevelBands.WearableRank("sword", World.World.LevelOf(a))).Select(a => world.Coins(a)).OrderBy(c => c).ToList();
            {
                static double Med(List<double> x) => x.Count == 0 ? double.NaN : x.OrderBy(v => v).ElementAt(x.Count / 2);
                var ranks = players.Where(a => a.Weapon != null && a.Weapon.Good.Id.StartsWith("sword")).GroupBy(a => LevelBands.BandOf(a.Weapon!.Good.Id)).OrderBy(g => g.Key);
                Console.WriteLine($"  weapon upgrades: {world.UpgradeGapsEarly.Count} at level 1-20 (median {Med(world.UpgradeGapsEarly):0.0} fight hours apart), {world.UpgradeGapsLate.Count} above (median {Med(world.UpgradeGapsLate):0.0}h); swords worn by band " + string.Join(", ", ranks.Select(g => $"b{g.Key + 1} {g.Count()}")));
            }
            Console.WriteLine($"  gear: {players.Count} players, {armed} with a weapon, {armored} with armor, {upToDate} weapon up to level; behind-on-weapon median coins {(behind.Count > 0 ? behind[behind.Count / 2] : 0)}");
            {
                int swd = world.Goods["sword"];
                double swRef = world.Towns[0].Markets[swd].Reference.Value;
                var unarmed = players.Where(a => a.Weapon == null).ToList();
                var coinsU = unarmed.Select(a => world.Coins(a)).OrderBy(c => c).ToList();
                Console.WriteLine($"  unarmed {unarmed.Count}: can afford a sword {coinsU.Count(c => c >= swRef)}, median coins {(coinsU.Count > 0 ? coinsU[coinsU.Count / 2] : 0)}, with sword in bag {unarmed.Count(a => a.Inventory[swd] > 0)}; swords held by smiths {world.Agents.Where(a => a.Role == Role.Smith).Sum(a => a.Inventory[swd])}; smiths {world.Agents.Count(a => a.Role == Role.Smith && a.Active)} (skill 35+ for level-21 swords: {world.Agents.Count(a => a.Role == Role.Smith && a.Active && a.Skill >= 35)}, 50+: {world.Agents.Count(a => a.Role == Role.Smith && a.Active && a.Skill >= 50)})");
            }
            {
                // Income vs what players spend on themselves, per online hour, all roles.
                int roles = Enum.GetValues<Role>().Length;
                double hours = Enumerable.Range(0, roles).Where(r => (Role)r is not (Role.Bot or Role.Speculator)).Sum(r => (double)world.RoleHours[r]);
                double income = 0;
                for (int r = 0; r < roles; r++) if ((Role)r is not (Role.Bot or Role.Speculator))
                    foreach (var k in Enum.GetValues<World.IncomeKind>()) income += world.IncomeParts[r, (int)k];
                var spend = Enum.GetValues<World.SpendKind>().ToDictionary(k => k, k => Enumerable.Range(0, roles).Where(r => (Role)r is not (Role.Bot or Role.Speculator)).Sum(r => (double)world.SpendParts[r, (int)k]));
                Console.WriteLine($"  per online hour: net income {income / hours:0}c; spend " + string.Join(", ", spend.Select(kv => $"{kv.Key} {kv.Value / hours:0}c ({kv.Value / Math.Max(1, income):P0})")));
            }
            Console.WriteLine("  novice-made: " + string.Join(", ", world.NoviceByGood.OrderByDescending(kv => kv.Value).Select(kv => $"{world.Goods.Defs[kv.Key].Id} {kv.Value}")));
            Console.WriteLine($"  fight hours with a healer {world.HealedFightHours}; novice crafts by good: " + string.Join(", ", new[] { "sword", "sword_s2", "sword_s3", "sword_s4", "sword_s5" }.Select(id => $"{id} {world.GoodFlows[world.Goods[id], (int)World.GoodFlowKind.Crafted]}")));
            Console.WriteLine($"  work orders posted {world.WorkOrdersPosted}, done {world.WorkOrdersDone}, expired {world.WorkOrdersExpired}; novice crafts {world.NoviceCrafts}; crafter quests {world.CrafterQuestsGiven}");
            Console.WriteLine("  order misses: " + string.Join(", ", world.WorkOrderMisses.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.good}/{kv.Key.why} {kv.Value}")));
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

    /// <summary>--set Name=Value overrides any setting, for quick what-ifs.</summary>
    private static void ApplySets(string[] args, SimConfig config)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--set") continue;
            var parts = args[i + 1].Split('=', 2);
            var prop = typeof(SimConfig).GetProperty(parts[0]) ?? throw new ArgumentException($"No setting {parts[0]}");
            prop.SetValue(config, prop.PropertyType == typeof(string[]) ? parts[1].Split(',') : Convert.ChangeType(parts[1], prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
