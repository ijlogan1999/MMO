using Economy.Core.Crafting;
using Economy.Core.Items;
using Economy.Core.Stabilizer;

namespace Economy.Sim;

/// <summary>
/// Every tunable number in one place. Defaults are the spec's starting
/// guesses; scenarios copy this and change what they shock.
/// </summary>
public sealed class SimConfig
{
    // Run shape
    public int Days { get; set; } = 90;
    public int WarmupDays { get; set; } = 30;
    public int AgentCount { get; set; } = 2000;
    public ulong Seed { get; set; } = 1;
    public bool StabilizerEnabled { get; set; } = true;

    /// <summary>Re-adds every account after each transfer. Slow; for debugging leaks.</summary>
    public bool VerifyEveryTransfer { get; set; }

    // Role mix (shares of the starting population; must sum to 1).
    public Dictionary<Role, double> RoleMix { get; set; } = new()
    {
        [Role.Miner] = 0.08,
        [Role.Logger] = 0.06,
        [Role.Hunter] = 0.06,
        [Role.Herbalist] = 0.05,
        [Role.Farmer] = 0.10,
        [Role.Smith] = 0.06,
        [Role.Cook] = 0.05,
        [Role.Tailor] = 0.03,
        [Role.Leatherworker] = 0.03,
        [Role.Alchemist] = 0.02,
        [Role.Carpenter] = 0.02,
        [Role.Repairer] = 0.0, // no repair profession: crafters repair (Ian, 2026-11-19)
        [Role.Hauler] = 0.05,
        [Role.Fighter] = 0.29,
        [Role.Casual] = 0.10,
    };

    // Money
    public long InitialPurse { get; set; } = 5000;
    public long StarterPurse { get; set; } = 1500;
    public long TreasurySeedPerAgent { get; set; } = 3000;
    public long TargetMoneyPerPlayer { get; set; } = 5000;

    // Sinks
    public double MarketTax { get; set; } = 0.03;
    public long StationFee { get; set; } = 10;
    public long RepairStationFee { get; set; } = 20;
    public long PlotRentPerWeek { get; set; } = 200;
    public int PlotsPerFarmer { get; set; } = 3;
    /// <summary>World farm plots, as the share of players who can hold a farm (0 = unlimited).</summary>
    public double FarmPlotShare { get; set; } = 0.10;
    public long ShopUpkeepPerWeek { get; set; } = 600;
    public double KeeperCut { get; set; } = 0.10;
    public double TokenListingFee { get; set; } = 0.02;
    public long SubclassSwitchFee { get; set; } = 800;
    public double SubclassSwitchChancePerDay { get; set; } = 0.02;
    public double NpcVendorMarkup { get; set; } = 1.30;
    public double NpcVendorDailyShare { get; set; } = 0.05;

    // Town contracts (the main faucet): paid to fighters from the treasury,
    // scaled so money per active player stays near the target.
    public double ContractShareOfSinks { get; set; } = 1.0;
    public double ContractMaxTreasurySharePerHour { get; set; } = 0.01;

    // Activity rates per online hour
    public double GatherPerHour { get; set; } = 3;

    /// <summary>Unsold units of one good a gatherer holds before gathering something else.</summary>
    public int StockpileLimit { get; set; } = 20;
    public double NoToolFactor { get; set; } = 0.25;
    public double NodeRegenPerRichness { get; set; } = 60;
    public double NodeStockHours { get; set; } = 48;
    public double OverharvestFraction { get; set; } = 0.10;
    public double OverharvestRegenFactor { get; set; } = 0.25;
    public int OverharvestDays { get; set; } = 7;
    public double FarmYieldPerHour { get; set; } = 4;
    public double HuntHidePerHour { get; set; } = 1.2;
    public double HuntMeatPerHour { get; set; } = 1.5;
    public double GrainYieldMultiplier { get; set; } = 1.0;

    /// <summary>Set by the obsolescence scenario: players stop buying bread.</summary>
    public bool BreadObsolete { get; set; }

    /// <summary>Quality points a perfect tool adds to gathered goods (a poor one subtracts the same).</summary>
    public double ToolQualityBonus { get; set; } = 10;
    /// <summary>
    /// Every player fights (Ian, 2026-11-19); their focus is what they do on
    /// the side. Share of online hours spent fighting, by focus.
    /// </summary>
    public bool EveryoneFights { get; set; } = true;
    public double MainFighterFightShare { get; set; } = 0.85;
    public double FocusFightShare { get; set; } = 0.35;
    // Party trips (Ian, 2026-11-19): tank, healer and 3 damage; gatherers
    // gather and haulers carry rich materials out of the dangerous wilds.
    public bool Parties { get; set; } = true;
    public double TankShare { get; set; } = 0.2;
    public double HealerShare { get; set; } = 0.2;
    /// <summary>Chance a gatherer, hauler, fighter or casual spends a fighting hour on a party trip.</summary>
    public double PartyTripChance { get; set; } = 0.5;
    /// <summary>Solo gathering near towns yields less than before.</summary>
    public double SoloGatherYield { get; set; } = 0.7;
    /// <summary>Regrowth of the dangerous wilds, as a share of the safe nodes' regrowth.</summary>
    public double DeepRegenShare { get; set; } = 0.15;
    /// <summary>Scales all wilds regrowth (patch scenarios).</summary>
    public double DeepRegenMultiplier { get; set; } = 1.0;
    /// <summary>Regrowth of rare wilds materials per unit of town wilds richness.</summary>
    public double RareRegenPerHour { get; set; } = 0.4;

    // Cross-craft uses (Ian, 2026-11-19): widen what each craft is good for.
    /// <summary>Fighters below this combat level heal with bandages (tailors) instead of potions.</summary>
    public double BandageMaxLevel { get; set; } = 40;
    /// <summary>Extra party carry for each member with a travel pack (leatherworkers); packs wear out per trip.</summary>
    public double PackCarry { get; set; } = 3;
    public double PackUsePerTrip { get; set; } = 0.1;
    /// <summary>Players with more coins than this furnish their homes (carpenters' furniture).</summary>
    public long FurnitureWealth { get; set; } = 6000;
    public double FurnitureBuysPerDay { get; set; } = 0.15;

    // Stronger mobs (Ian, 2026-11-19): deaths damage gear instead of extra wear.
    public double MobStrength { get; set; } = 1.0;

    /// <summary>Crafters stop making what is not selling: hold at most this many days of recent sales (at least CraftStockMin units).</summary>
    public bool CraftersWatchSales { get; set; } = true;

    /// <summary>Everyone can make bottom-tier goods at novice level (Ian, 2026-11-19).</summary>
    public bool NoviceCrafting { get; set; } = true;
    public string[] NoviceGoods { get; set; } = { "bread", "bandage", "flour", "firewood", "cloth" };
    /// <summary>Novice work: quality, and coins of time cost per craft step (it's slow).</summary>
    public double NoviceQuality { get; set; } = 25;
    public double NoviceTimeCost { get; set; } = 15;
    /// <summary>Make it yourself when the market asks at least this multiple of the novice cost.</summary>
    public double NoviceMakeThreshold { get; set; } = 1.3;

    /// <summary>Crafter starter quest: skill floor, and how many first items its materials cover (the NPC buys that many).</summary>
    public bool CrafterStarterQuest { get; set; } = true;
    public double CrafterQuestSkill { get; set; } = 20;
    public int CrafterQuestCrafts { get; set; } = 5;
    /// <summary>Rough coin value of the quest's materials (a fighter's sword and armor are worth about this).</summary>
    public double CrafterQuestValue { get; set; } = 3000;
    public double CraftStockDays { get; set; } = 4;
    public int CraftStockMin { get; set; } = 3;
    /// <summary>Weight of today in each player's running average of units sold per day.</summary>
    public double SalesAverageWeight { get; set; } = 0.15;
    public double DeathChancePerFightHour { get; set; } = 0.03;
    /// <summary>Durability lost on death, in hours of normal wear.</summary>
    public double DeathWearHours { get; set; } = 15;

    // Shops are limited; anyone can take up any craft (Ian, 2026-11-19).
    public int ShopsPerTownPerCraft { get; set; } = 2;
    /// <summary>Only shop owners keep listings up while offline; everyone else sells while online.</summary>
    public bool OfflineSellingNeedsShop { get; set; } = true;
    /// <summary>Job caps: the share a profession can grow to before newcomers stop picking it (crafts uncapped).</summary>
    public bool CapCrafts { get; set; } = false;

    // Town delivery tasks and fair parties (Ian, 2026-11-19).
    public bool DeliveryTasks { get; set; } = true;
    /// <summary>Share of the contract pool paid as kill bounties to fighters; the rest funds delivery tasks.</summary>
    public double KillTaskShare { get; set; } = 0.4;
    /// <summary>Delivery price as a share of the town's reference price, and how far it eases as the budget runs down.</summary>
    public double TaskPriceShare { get; set; } = 0.85;
    public double TaskPriceEase { get; set; } = 0.5;
    /// <summary>Players deliver when the task pays at least this share of what they expect to get from players.</summary>
    public double TaskAcceptShare { get; set; } = 0.9;
    public int SoloTaskCapPerDay { get; set; } = 20;
    public int PartyTaskCapPerMember { get; set; } = 30;

    /// <summary>
    /// Need-based tasks (Ian, 2026-11-19): towns keep a stockpile; a full-price
    /// task is open only while it is under StockpileDays of daily use.
    /// </summary>
    public bool TownStockpiles { get; set; } = true;
    public double StockpileDays { get; set; } = 2;
    public double StockpileTaskPriceShare { get; set; } = 1.0;
    /// <summary>NPCs always buy raw goods at this share of the market price (0 = off); players keep this many spare before dumping.</summary>
    public double NpcBuybackShare { get; set; } = 0.5;
    public int NpcBuybackKeep { get; set; } = 30;
    /// <summary>Rare wilds materials: crafters only, no town or NPC buys or sells them.</summary>
    public HashSet<string> RareGoods { get; set; } = new() { "ember_slag", "ice_crystal" };
    /// <summary>Personal loot: haulers carry members' overflow for this share of its value.</summary>
    public bool PersonalLoot { get; set; } = true;
    public double HaulFeeShare { get; set; } = 0.12;
    /// <summary>Hire board: a player tank or healer hired for one trip-hour, split across the party.</summary>
    public long PlayerHireFee { get; set; } = 150;
    public double PartyGatherPerHour { get; set; } = 8;
    /// <summary>Carry limits are in weight (ore 2 per unit, herbs 0.2).</summary>
    public double PartyCarryPerMember { get; set; } = 3;
    public double PartyCarryPerHauler { get; set; } = 15;
    public double PartyQualityBonus { get; set; } = 15;
    public double PartyWipeChanceFull { get; set; } = 0.02;
    public double PartyWipeChanceMissingRole { get; set; } = 0.12;
    /// <summary>
    /// Parties can hire an NPC tank, healer or porter for a trip-hour when no
    /// player fills the role (a coin sink). NPCs take no share of the loot.
    /// </summary>
    public bool HireNpcs { get; set; } = true;
    public long NpcTankFee { get; set; } = 250;
    public long NpcHealerFee { get; set; } = 250;
    public long NpcPorterFee { get; set; } = 120;
    /// <summary>NPC fighters are weaker than players: wipe chance multiplier per hired fighter.</summary>
    public double NpcWipeFactor { get; set; } = 1.5;
    public double FoodPerHour { get; set; } = 0.5;
    public double StewFoodValue { get; set; } = 2.0;
    /// <summary>Stew eaten as a combat buff per fight hour at skill 0 and at skill 100 (scales linearly).</summary>
    public double BuffFoodPerFightHourMin { get; set; } = 0.15;
    public double BuffFoodPerFightHourMax { get; set; } = 0.6;
    /// <summary>Combat power bonus while buffed (loot, dungeon cores, contract share).</summary>
    public double BuffPowerBonus { get; set; } = 0.25;
    public bool FoodBuffs { get; set; } = true;
    public int ToolWearPerHour { get; set; } = 16;
    public int WeaponWearPerHour { get; set; } = 20;
    public int ArmorWearPerHour { get; set; } = 14;
    public double PotionsPerFightHour { get; set; } = 0.4;
    public double FighterHidePerHour { get; set; } = 0.4;
    public double FighterMeatPerHour { get; set; } = 0.5;
    public double RelicCoreDropPerHour { get; set; } = 0.015;
    public double DungeonRunMultiplier { get; set; } = 1.0;
    public double CarefulShare { get; set; } = 0.5;

    // Skill
    public double SkillGainPerHour { get; set; } = 0.03;
    public int MasterSkill { get; set; } = 70;

    // Hauling
    public int WagonCapacity { get; set; } = 120;

    /// <summary>
    /// Courier jobs (Ian, 2026-11-19): haulers carry other people's goods for a
    /// fee paid on delivery instead of buying cargo. Fee = share of the goods'
    /// value plus coins per unit of weight per travel hour.
    /// </summary>
    public bool CourierJobs { get; set; } = true;
    public bool TownCourierJobs { get; set; } = true;
    public double CourierFeeShare { get; set; } = 0.06;
    public double CourierWeightFee { get; set; } = 0.5;
    /// <summary>A job is posted only if the owner nets at least this much more than selling at home.</summary>
    public double CourierMinGain { get; set; } = 0.1;
    public int CourierMaxUnits { get; set; } = 60;
    public double CourierCollateralShare { get; set; } = 0.5;
    public int CourierJobExpiryHours { get; set; } = 48;
    public long CourierRideEmptyMinFees { get; set; } = 150;
    public int TownCourierMinStock { get; set; } = 20;
    /// <summary>Weight a wagon carries (ore weighs 2, herbs 0.2).</summary>
    public double WagonWeight { get; set; } = 150;

    /// <summary>Weight per unit of each good; anything not listed weighs 1.</summary>
    public Dictionary<string, double> GoodWeights { get; set; } = new()
    {
        ["iron_ore"] = 2, ["coal"] = 1.5, ["timber"] = 2, ["hide"] = 1, ["meat"] = 1, ["grain"] = 0.5,
        ["fiber"] = 0.3, ["herbs"] = 0.2, ["ember_slag"] = 1.5, ["ice_crystal"] = 1, ["relic_core"] = 0.5,
        ["iron_ingot"] = 1, ["plank"] = 1, ["firewood"] = 1, ["flour"] = 0.5, ["cloth"] = 0.3, ["leather"] = 0.5,
        ["padding"] = 0.5, ["nails"] = 0.2, ["bread"] = 0.3, ["stew"] = 0.5, ["stock"] = 0.5,
        ["healing_potion"] = 0.3, ["bandage"] = 0.1, ["pickaxe"] = 3, ["woodaxe"] = 3, ["sickle"] = 1,
        ["skinning_knife"] = 0.5, ["sword"] = 3, ["blade_blank"] = 2, ["leather_armor"] = 5, ["plate_armor"] = 15,
        ["cloth_armor"] = 2, ["relic_sword"] = 3, ["travel_pack"] = 1, ["furniture"] = 20,
    };
    public double CargoLossChance { get; set; } = 0.03;
    public double HaulMinMargin { get; set; } = 0.12;

    // Population
    public double BaseQuitChancePerDay { get; set; } = 0.002;
    public double UnhappyQuitChancePerDay { get; set; } = 0.02;
    public double JoinerIncomeBias { get; set; } = 1.5;

    /// <summary>Daily chance that a player earning well below average switches to a better-paid role.</summary>
    public double RoleSwitchChancePerDay { get; set; } = 0.03;
    public double RoleSwitchIncomeRatio { get; set; } = 0.3;

    // Token
    public double TokenInitialPrice { get; set; } = 3000;
    public double TokenSellersPerAgentPerDay { get; set; } = 0.008;

    /// <summary>
    /// Gear tiers. A crafter makes gear at the highest tier their skill
    /// reaches; each tier needs more of the recipe's materials, and the top
    /// tiers also need different ones. Repairing gear uses repair material
    /// in proportion to the item's tier (set by its quality).
    /// </summary>
    public GearTier[] GearTiers { get; set; } =
    {
        new() { Name = "Common", MinSkill = 0, InputMultiplier = 1.0 },
        new() { Name = "Fine", MinSkill = 25, InputMultiplier = 1.5 },
        new() { Name = "Superior", MinSkill = 50, InputMultiplier = 2.0,
            Extra = new() { ["blacksmithing"] = new() { ["coal"] = 2 }, ["leatherworking"] = new() { ["herbs"] = 2 }, ["tailoring"] = new() { ["fiber"] = 2 } } },
        new() { Name = "Masterwork", MinSkill = 75, InputMultiplier = 3.0,
            Extra = new() { ["blacksmithing"] = new() { ["coal"] = 3, ["leather"] = 1, ["ember_slag"] = 1 }, ["leatherworking"] = new() { ["herbs"] = 3, ["cloth"] = 1, ["ice_crystal"] = 1 }, ["tailoring"] = new() { ["ice_crystal"] = 1 } } },
    };

    /// <summary>New players get their role's starter kit from a short quest instead of buying it.</summary>
    public bool StarterQuest { get; set; } = true;

    /// <summary>Hours of play the starter quest takes.</summary>
    public double StarterQuestHours { get; set; } = 1.0;

    /// <summary>Lowest price a player will sell at, as a share of the starting price.</summary>
    public double SellerFloorShare { get; set; } = 0.5;
    public double MemberShare { get; set; } = 0.5;
    public int FreeTokenAtDay { get; set; } = 30;

    // Shared rules
    public DurabilitySettings Durability { get; set; } = new();
    public QualitySettings Quality { get; set; } = new();
    public StabilizerSettings Stabilizer { get; set; } = new();

    /// <summary>Deep copy, so a scenario can change settings without touching the baseline.</summary>
    public SimConfig Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<SimConfig>(System.Text.Json.JsonSerializer.Serialize(this))!;
}

public enum Role
{
    Miner,
    Logger,
    Hunter,
    Herbalist,
    Farmer,
    Smith,
    Cook,
    Tailor,
    Leatherworker,
    Alchemist,
    Carpenter,
    Repairer,
    Hauler,
    Fighter,
    Casual,
    Speculator,
    Bot,
}

public static class Roles
{
    public static bool IsGatherer(Role r) => r is Role.Miner or Role.Logger or Role.Hunter or Role.Herbalist or Role.Farmer or Role.Bot;
    /// <summary>Crafts whose makers also repair gear (smiths, leatherworkers, tailors).</summary>
    public static bool Repairs(Role r) => r is Role.Smith or Role.Leatherworker or Role.Tailor or Role.Repairer;

    public static bool IsCrafter(Role r) => r is Role.Smith or Role.Cook or Role.Tailor or Role.Leatherworker or Role.Alchemist or Role.Carpenter;

    public static string? CraftSkill(Role r) => r switch
    {
        Role.Smith => "blacksmithing",
        Role.Cook => "cooking",
        Role.Tailor => "tailoring",
        Role.Leatherworker => "leatherworking",
        Role.Alchemist => "alchemy",
        Role.Carpenter => "carpentry",
        _ => null,
    };
}

public sealed class GearTier
{
    public string Name { get; set; } = "";
    public int MinSkill { get; set; }

    /// <summary>Quality is roughly skill, so an item's tier is also read from its quality.</summary>
    public double InputMultiplier { get; set; } = 1;

    /// <summary>Extra, different materials by craft skill (e.g. blacksmithing: coal, leather).</summary>
    public Dictionary<string, Dictionary<string, int>> Extra { get; set; } = new();
}
