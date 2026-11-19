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
    public int Days { get; set; } = 180;
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
        [Role.Repairer] = 0.05,
        [Role.Hauler] = 0.05,
        [Role.Fighter] = 0.24,
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

    public double FoodPerHour { get; set; } = 0.5;
    public double StewFoodValue { get; set; } = 2.0;
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
            Extra = new() { ["blacksmithing"] = new() { ["coal"] = 2 }, ["leatherworking"] = new() { ["herbs"] = 2 } } },
        new() { Name = "Masterwork", MinSkill = 75, InputMultiplier = 3.0,
            Extra = new() { ["blacksmithing"] = new() { ["coal"] = 3, ["leather"] = 1 }, ["leatherworking"] = new() { ["herbs"] = 3, ["cloth"] = 1 } } },
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
