using Economy.Core.Crafting;
using Economy.Core.Items;
using Economy.Core.Ledger;
using Economy.Core.Random;
using Economy.Sim.Reporting;

namespace Economy.Sim.World;

/// <summary>
/// One simulated world. Time moves in one-hour steps; in each hour every
/// online agent takes one turn, then the hidden stabilizer looks at every
/// market, then the books are checked. Split across partial files:
/// World.Market (orders and settlement), World.Agents (agent turns),
/// World.Npc (stabilizer, vendors, contracts), World.Daily (upkeep, churn,
/// token), World.Metrics.
/// </summary>
public sealed partial class World
{
    public const int NpcOwner = -1;
    public const int VendorOwner = -2;

    private readonly SimRandom _agentRng;
    private readonly SimRandom _npcRng;
    private readonly SimRandom _setupRng;
    private readonly WorldFile _worldFile;
    private readonly List<int> _turnOrder = new();

    public World(SimConfig config, Catalog catalog, WorldFile worldFile, IScenario? scenario = null)
    {
        Config = config;
        Scenario = scenario;
        _worldFile = worldFile;
        var root = new SimRandom(config.Seed);
        _setupRng = root.Fork(1);
        _agentRng = root.Fork(2);
        _npcRng = root.Fork(3);
        ScenarioRng = root.Fork(4);

        Goods = new GoodsTable(catalog, worldFile.RawPrices());
        DesignPrice = new GoodsTable(catalog, worldFile.DesignRawPrices()).BasePrice;
        Durability = new DurabilityRules(config.Durability);
        QualityRules = new QualityRules(config.Quality);
        Ledger = new CoinLedger(config.VerifyEveryTransfer);
        Escrow = Ledger.OpenEscrow("order escrow");
        _courierEscrow = Ledger.OpenEscrow("courier fees");
        _orderEscrow = Ledger.OpenEscrow("work orders");
        Metrics = new MetricsRecorder(this);

        Regions = worldFile.Regions.ToArray();
        Towns = worldFile.Towns.Select((t, i) => new Town(i, t, Array.IndexOf(Regions, t.Region), Goods.Count)).ToArray();
        foreach (var t in Towns) t.InitStockpile(Goods.Count);
        _jobs = Towns.Select(_ => new List<CourierJob>()).ToArray();
        _jobFees = new long[Towns.Length];
        RegionProducedHour = new int[Regions.Length, Goods.Count];
        for (int r = 0; r < Regions.Length; r++)
            for (int g = 0; g < Goods.Count; g++) RegionProducedHour[r, g] = 0;
        TravelBlocked = new bool[Regions.Length];

        Food = new[] { Goods["bread"], Goods["stew"] };
        Bread = Goods["bread"];
        Stew = Goods["stew"];
        Potion = Goods["healing_potion"];
        Bandage = Goods["bandage"];
        CopperOre = Goods.Index.TryGetValue("copper_ore", out int cu) ? cu : -1;
        Pack = Goods["travel_pack"];
        Furniture = Goods["furniture"];
        RelicCore = Goods["relic_core"];
        TokenIndex = Goods.Count;

        Setup();
    }

    public SimConfig Config { get; }
    public IScenario? Scenario { get; }
    public SimRandom ScenarioRng { get; }
    public GoodsTable Goods { get; }

    /// <summary>
    /// Prices from the design data alone. Sellers' floors and ceilings hang
    /// off these, so recalibrating start prices can never ratchet them down.
    /// </summary>
    public double[] DesignPrice { get; }
    public DurabilityRules Durability { get; }
    public QualityRules QualityRules { get; }
    public CoinLedger Ledger { get; }
    public AccountId Escrow { get; }
    public MetricsRecorder Metrics { get; }
    public string[] Regions { get; }
    public Town[] Towns { get; }
    public List<Agent> Agents { get; } = new();

    /// <summary>Last hour a player in each region produced each good.</summary>
    public int[,] RegionProducedHour { get; }

    /// <summary>Regions cut off from travel (scenario 11).</summary>
    public bool[] TravelBlocked { get; }

    public int[] Food { get; }
    public int Bread { get; }
    public int Stew { get; }
    public int Potion { get; }
    public int Bandage { get; }
    public int CopperOre { get; }
    public int Pack { get; }
    public int Furniture { get; }
    public int RelicCore { get; }

    /// <summary>Belief slot used for the game-time token.</summary>
    public int TokenIndex { get; }

    public int Hour { get; private set; }
    public int Day => Hour / 24;
    public int HourOfDay => Hour % 24;
    public int TotalHours => Config.Days * 24;

    /// <summary>Set by the gold-drought scenario.</summary>
    public double TaxRate { get; set; }

    /// <summary>Units the NPCs brought in from outside the world.</summary>
    public long ItemsInjected { get; set; }

    /// <summary>Starter kits the starter quest handed to new players.</summary>
    public int StarterKitsGiven { get; private set; }

    private void Setup()
    {
        TaxRate = Config.MarketTax;
        Tokens = new TokenMarket(Config.TokenInitialPrice);
        double scale = Config.AgentCount / 2000.0;
        foreach (var town in Towns)
        {
            foreach (var kv in town.Def.Nodes)
            {
                int g = Goods[kv.Key];
                town.Richness[g] = kv.Value;
                town.RegenPerHour[g] = kv.Value * Config.NodeRegenPerRichness * scale * town.Def.Size * 5;
                town.Stock[g] = town.RegenPerHour[g] * Config.NodeStockHours;
                town.DeepRegenPerHour[g] = town.RegenPerHour[g] * Config.DeepRegenShare;
            }
            // Rare materials (ember slag, ice crystals) only come from the wilds.
            foreach (var kv in town.Def.Wilds)
                town.DeepRegenPerHour[Goods[kv.Key]] += kv.Value * Config.RareRegenPerHour * scale * town.Def.Size * 5;
            for (int g = 0; g < Goods.Count; g++)
                town.DeepStock[g] = town.DeepRegenPerHour[g] * Config.NodeStockHours;
            for (int g = 0; g < Goods.Count; g++)
                town.Markets[g] = new GoodMarket(Goods.BasePrice[g], Config.Stabilizer.ReferenceHalfLifeHours, Config.Stabilizer);
        }

        Ledger.Transfer(Ledger.Mint, Ledger.Treasury, Config.TreasurySeedPerAgent * Config.AgentCount, Flow.TreasurySeed);
        Scenario?.BeforeSetup(this);

        var roles = Config.RoleMix.ToList();
        for (int i = 0; i < Config.AgentCount; i++)
        {
            var role = PickWeighted(roles.Select(r => (r.Key, r.Value)).ToList(), _setupRng);
            var agent = CreateAgent(role, PickTown(role, _setupRng), Config.InitialPurse, _setupRng, veteran: true);
            agent.JoinedHour = -_setupRng.NextInt(0, 24 * 60);
        }

        Scenario?.AfterSetup(this);
    }

    /// <summary>Runs the whole simulation.</summary>
    public void Run(Action<World>? onDay = null)
    {
        for (Hour = 0; Hour < TotalHours; Hour++)
        {
            Step();
            if (HourOfDay == 23) onDay?.Invoke(this);
        }
        Ledger.Verify();
        Metrics.Finish();
    }

    private void Step()
    {
        Scenario?.OnHour(this);
        RegenNodes();
        StartContractHour();

        // Agents take turns in a random order each hour so no one is always first.
        _turnOrder.Clear();
        for (int i = 0; i < Agents.Count; i++)
        {
            var a = Agents[i];
            if (!a.Active) continue;
            if (a.TravelHoursLeft > 0) { TickTravel(a); continue; }
            if (a.OnlineAt(HourOfDay)) _turnOrder.Add(i);
            // Without a shop, a player's goods sell only while they are online
            // to trade them; listings come down when they log off.
            else if (Config.OfflineSellingNeedsShop && !a.HasShop && a.Orders.Count > 0
                     && a.Role is not (Role.Bot or Role.Speculator or Role.Hauler))
                CancelAgentOrders(a, learn: false);
        }
        Shuffle(_turnOrder, _agentRng);
        foreach (int i in _turnOrder) AgentTurn(Agents[i]);
        ResolveParties();

        PayContracts();
        RunStabilizer();
        EndHourMarkets();

        Ledger.Verify();
        Metrics.RecordHour();

        if (HourOfDay == 23) EndDay();
    }

    private void RegenNodes()
    {
        foreach (var town in Towns)
            for (int g = 0; g < Goods.Count; g++)
            {
                double deep = town.DeepRegenPerHour[g] * Config.DeepRegenMultiplier;
                if (deep > 0) town.DeepStock[g] = Math.Min(town.DeepRegenPerHour[g] * Config.NodeStockHours, town.DeepStock[g] + deep);
            }
        foreach (var town in Towns)
        {
            for (int g = 0; g < Goods.Count; g++)
            {
                double regen = town.RegenPerHour[g];
                if (regen <= 0) continue;
                if (town.DepletedUntilHour[g] > Hour) regen *= Config.OverharvestRegenFactor;
                double cap = town.RegenPerHour[g] * Config.NodeStockHours;
                town.Stock[g] = Math.Min(cap, town.Stock[g] + regen);
            }
        }
    }

    public Agent CreateAgent(Role role, int town, long purse, SimRandom rng, bool veteran)
    {
        var account = Ledger.OpenPlayer($"{role}-{Agents.Count}");
        var agent = new Agent(Agents.Count, role, town, account, Goods.Count + 1)
        {
            JoinedHour = Hour,
            SessionStart = (int)Math.Round(Math.Clamp(rng.NextGaussian(19, 3), 0, 23)),
            SessionHours = role == Role.Casual ? 1 : rng.NextInt(2, 6),
            Skill = veteran ? rng.NextInt(15, 80) : rng.NextInt(5, 25),
            CombatSkill = veteran ? rng.NextInt(15, 80) : Config.LevelBands ? rng.NextInt(0, 2) : rng.NextInt(5, 25),
            RepairSkill = role == Role.Repairer ? (veteran ? rng.NextInt(30, 95) : rng.NextInt(10, 40)) : 0,
            Careful = rng.Chance(Config.CarefulShare),
            Member = rng.Chance(Config.MemberShare),
            RenewalDay = rng.NextInt(0, 30),
            FoodHours = 6,
        };
        double cr = rng.NextDouble();
        agent.CombatRole = cr < Config.TankShare ? CombatRole.Tank : cr < Config.TankShare + Config.HealerShare ? CombatRole.Healer : CombatRole.Damage;
        if (role == Role.Bot) { agent.SessionStart = 0; agent.SessionHours = 20; agent.Skill = 40; }
        if (role == Role.Speculator) { agent.SessionHours = 4; }

        Agents.Add(agent);
        if (purse > 0) Ledger.Transfer(Ledger.Mint, account, purse, Flow.StarterPurse);
        agent.LastBalance = Ledger.Balance(account);

        for (int g = 0; g < Goods.Count; g++) agent.Belief[g] = Towns[town].Markets[g].LastPrice;
        agent.Belief[TokenIndex] = Tokens.LastPrice;

        // Starting kit: food, plus the tool or gear the role works with.
        // Veterans already own theirs; new players get it from the starter
        // quest (with the quest off, they start empty-handed and must buy).
        if (!veteran && !Config.StarterQuest) return agent;
        if (!veteran) StarterKitsGiven++;
        agent.Inventory.Add(Bread, 3, 40);
        int q = veteran ? rng.NextInt(30, 70) : 35;
        switch (role)
        {
            case Role.Miner:
            case Role.Bot:
            case Role.Casual:
                agent.Tool = Durability.Create(Goods.Defs[Goods["pickaxe"]], q); break;
            case Role.Logger:
                agent.Tool = Durability.Create(Goods.Defs[Goods["woodaxe"]], q); break;
            case Role.Hunter:
                agent.Tool = Durability.Create(Goods.Defs[Goods["skinning_knife"]], q); break;
            case Role.Herbalist:
            case Role.Farmer:
                agent.Tool = Durability.Create(Goods.Defs[Goods["sickle"]], q); break;
        }
        // Everyone fights (Ian, 2026-11-19), so everyone's kit has a weapon and
        // armor for their level; main fighters also get a few potions.
        if (Fights(agent))
        {
            int lvl = LevelOf(agent);
            string armorBase = agent.CombatRole == CombatRole.Healer ? "cloth_armor" : "leather_armor";
            agent.Weapon = Durability.Create(Goods.Defs[Goods[LevelBands.Id("sword", Config.LevelBands ? LevelBands.WearableBand("sword", lvl) : 0)]], q);
            agent.Armor = Durability.Create(Goods.Defs[Goods[LevelBands.Id(armorBase, Config.LevelBands ? LevelBands.WearableBand(armorBase, lvl) : 0)]], q);
            if (role == Role.Fighter) agent.Inventory.Add(Potion, 3, 40);
        }
        if (Roles.Repairs(role))
        {
            Towns[town].Repairers.Add(agent.Id);
            agent.RepairPrice = 150;
        }
        if (!veteran) GiveCrafterQuest(agent);
        return agent;
    }

    /// <summary>
    /// Picks a home town. Gatherers settle where their resources are, so a
    /// miner lives near ore; everyone else follows town size.
    /// </summary>
    public int PickTown(Role role, SimRandom rng)
    {
        var goods = GatherGoodsFor(role);
        if (goods.Length == 0) goods = SettleNearFor(role);
        if (goods.Length == 0) return PickWeighted(Towns.Select(t => (t.Index, t.Def.Size)).ToList(), rng);
        return PickWeighted(Towns.Select(t => (t.Index, t.Def.Size * goods.Sum(id => t.Richness[Goods[id]]))).ToList(), rng);
    }

    public static string[] GatherGoodsFor(Role role) => role switch
    {
        Role.Miner or Role.Bot => new[] { "iron_ore", "coal" },
        Role.Logger => new[] { "timber" },
        Role.Hunter => new[] { "hide", "meat" },
        Role.Herbalist => new[] { "herbs" },
        _ => Array.Empty<string>(),
    };

    /// <summary>Crafters settle near their main raw input, as players do once they learn the map.</summary>
    public static string[] SettleNearFor(Role role) => role switch
    {
        Role.Smith => new[] { "iron_ore", "coal" },
        Role.Cook => new[] { "grain" },
        Role.Tailor => new[] { "fiber" },
        Role.Leatherworker => new[] { "hide" },
        Role.Alchemist => new[] { "herbs" },
        Role.Carpenter => new[] { "timber" },
        _ => Array.Empty<string>(),
    };

    public static T PickWeighted<T>(IReadOnlyList<(T item, double weight)> items, SimRandom rng)
    {
        double total = items.Sum(i => Math.Max(0, i.weight));
        double x = rng.NextDouble() * total;
        foreach (var (item, weight) in items)
        {
            x -= Math.Max(0, weight);
            if (x <= 0) return item;
        }
        return items[^1].item;
    }

    private static void Shuffle<T>(List<T> list, SimRandom rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Rounds a fractional amount up or down at random so averages come out right.</summary>
    public static int RandomRound(double x, SimRandom rng)
    {
        if (x <= 0) return 0;
        int whole = (int)Math.Floor(x);
        return whole + (rng.Chance(x - whole) ? 1 : 0);
    }

    public long Coins(Agent a) => Ledger.Balance(a.Account);
}
