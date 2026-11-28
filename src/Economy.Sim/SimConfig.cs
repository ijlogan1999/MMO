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
    /// <summary>Day the price-steadiness check measures from (the week before it): after the launch shortages.</summary>
    public int PriceBaselineDay { get; set; } = 60;
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
    public long TargetMoneyPerPlayer { get; set; } = 8000;

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
    public double GrainYieldMultiplier { get; set; } = 1.3;

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
    /// <summary>Tailors' gauze (not novice-made) heals as much as GauzeHeals bandages and works up to this combat skill.</summary>
    public double GauzeMaxLevel { get; set; } = 40;
    /// <summary>Iron ore and coal gathered per swing, relative to other raw goods.</summary>
    public double OreYieldMultiplier { get; set; } = 2.2;
    /// <summary>A profession stops drawing new players at this multiple of its normal share.</summary>
    public double RoleShareCap { get; set; } = 2.5;
    public int GauzeHeals { get; set; } = 2;
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
    /// <summary>Count only stock beyond what a crafter keeps for their own use as unsold (fix: alchemists who fight kept 6 potions and never brewed).</summary>
    public bool OwnUseNotUnsold { get; set; } = true;
    /// <summary>Buyers weigh quality against price: a bid is for standard quality (50); quality 100 is worth 1 + slope, quality 0 worth 1 - slope.</summary>
    public bool QualityPricing { get; set; } = true;
    public double QualityPriceSlope { get; set; } = 0.25;
    /// <summary>
    /// New and switching players notice a craft missing locally (Ian, 2026-11-22):
    /// a craft under half its normal share in a region weighs this much more for
    /// players there, and they set up shop in that region.
    /// </summary>
    public double LocalShortageBoost { get; set; } = 3.0;
    /// <summary>Crafters value what they make at the best buy order on the board when it beats their own price belief.</summary>
    public bool CraftersSeeBids { get; set; } = false;
    /// <summary>A player who can't afford gear for their level buys the best lower piece on sale instead of going without.</summary>
    public bool BuyBestAffordable { get; set; } = true;
    /// <summary>Listings can't be priced above this many times the town's recent average (0 = no cap).</summary>
    public double ListCapMultiple { get; set; } = 2;
    /// <summary>Units traded in 7 days needed to use the 7-day average; otherwise the 30-day one.</summary>
    public int ListCapMinUnits { get; set; } = 10;
    /// <summary>When the crafter is out of repair materials, the player buys them and brings them along.</summary>
    public bool RepairBringMaterials { get; set; } = false;
    /// <summary>Players can get repairs from a crafter anywhere in their region, not only their own town.</summary>
    public bool RepairInRegion { get; set; } = false;
    /// <summary>Broken gear can be sent to a crafter in any town (courier fee) instead of being scrapped when none is local.</summary>
    public bool RepairAnyTown { get; set; } = false;
    public long RemoteRepairFee { get; set; } = 50;

    /// <summary>Everyone can make bottom-tier goods at novice level (Ian, 2026-11-19).</summary>
    public bool NoviceCrafting { get; set; } = true;
    public string[] NoviceGoods { get; set; } = { "bread", "roast_meat", "bandage", "flour", "firewood", "cloth",
        // Rough early swords (Ian, 2026-11-19), and the bottom-tier parts they need.
        "iron_ingot", "blade_blank", "leather", "plank", "sword", "sword_s2", "sword_s3", "sword_s4", "sword_s5" };
    /// <summary>Gear is made at novice level whenever buying costs more than this times making it.</summary>
    public double NoviceGearThreshold { get; set; } = 1.0;
    /// <summary>Novice work: quality, and coins of time cost per craft step (it's slow).</summary>
    public double NoviceQuality { get; set; } = 25;
    public double NoviceTimeCost { get; set; } = 40;
    /// <summary>Make it yourself when the market asks at least this multiple of the novice cost.</summary>
    public double NoviceMakeThreshold { get; set; } = 2;

    /// <summary>Crafter starter quest: skill floor, and how many first items its materials cover (the NPC buys that many).</summary>
    public bool CrafterStarterQuest { get; set; } = true;

    /// <summary>
    /// Levels 1-80 in four bands with gear per band (Ian, 2026-11-19). Levels
    /// per fight hour at level 0 (slows as you level). Each band of gear you are
    /// behind costs this power factor; each band up pays this much more per kill.
    /// </summary>
    public bool LevelBands { get; set; } = true;
    public double LevelsPerFightHour { get; set; } = 0.8;
    public double GearGapPenalty { get; set; } = 0.75;
    public double ZonePayPerBand { get; set; } = 0.5;
    /// <summary>Harder zones pay more (Ian, 2026-11-22): extra minted bounty coin per fight hour,
    /// as a multiple of BountyMintPerFightHour, for each level band above the first.
    /// 2026-11-28: harder zones pay more in materials and less in new coin (was 1), to
    /// bring average player coin back down; see ZonePayInCoin to restore the old pay.</summary>
    public double ZoneMintPerBand { get; set; } = 0.6;
    /// <summary>Level 21+ mobs drop copper ore (per fight hour at power 1, per band above the first).</summary>
    public double ZoneCopperDropPerHour { get; set; } = 1;
    /// <summary>Level 41+ mobs drop ember slag / ice crystal (per fight hour at power 1, per band
    /// above the second). 2026-11-28: raised from 0.5 as part of paying high zones in materials.</summary>
    public double ZoneRareDropPerHour { get; set; } = 1.5;
    /// <summary>
    /// Restores the 2026-11-22 high-zone pay (mostly minted coin): ZoneMintPerBand 1,
    /// ZoneRareDropPerHour 0.5. Use `--set ZonePayInCoin=true` (put any later
    /// --set of those two after it).
    /// </summary>
    public bool ZonePayInCoin
    {
        get => _zonePayInCoin;
        set
        {
            _zonePayInCoin = value;
            if (value) { ZoneMintPerBand = 1; ZoneRareDropPerHour = 0.5; }
        }
    }
    private bool _zonePayInCoin;

    /// <summary>What fighters and casual players gather on the side (sharing nodes with the trades).</summary>
    public string[] SideGatherGoods { get; set; } = { "iron_ore", "timber", "herbs" };

    /// <summary>Work orders: gear ordered and paid into escrow, made by a local crafter.</summary>
    public bool WorkOrders { get; set; } = true;
    public double WorkOrderMinMargin { get; set; } = 0.1;
    public int WorkOrderWaitHours { get; set; } = 48;
    public int WorkOrderBuildHours { get; set; } = 72;
    public double CrafterQuestSkill { get; set; } = 20;
    public int CrafterQuestCrafts { get; set; } = 5;
    /// <summary>Rough coin value of the quest's materials (a fighter's sword and armor are worth about this).</summary>
    public double CrafterQuestValue { get; set; } = 3000;
    public double CraftStockDays { get; set; } = 4;
    public int CraftStockMin { get; set; } = 3;

    /// <summary>Crafters sell the parts they make themselves beyond a few for their own use (tailors' padding goes to leatherworkers).</summary>
    public bool SellOwnParts { get; set; } = true;
    public int OwnPartKeep { get; set; } = 6;
    /// <summary>Crafters never list their own work below today's material cost, so prices follow inputs.</summary>
    public bool CrafterCostFloor { get; set; } = false;
    /// <summary>Bark gathered per unit of timber felled (used for tanning when the data has bark).</summary>
    public double BarkPerTimber { get; set; } = 1.5;

    /// <summary>Horses (Ian, 2026-11-20): haulers of HorseMinLevel+ buy a horse from the stable (a coin sink); riding needs a leather saddle, which wears while travelling.</summary>
    public bool Horses { get; set; } = true;
    /// <summary>Joiners pick professions by the median player's pay, not the average (a few rich shop owners skew it).</summary>
    public bool JoinByTypicalPay { get; set; } = true;
    /// <summary>Tailors' quivers (ranged fighters) and reagent bags (herbalists, alchemists, cooks): work at NoBagProductivity without one.</summary>
    public bool SpecialBags { get; set; } = true;
    /// <summary>Cloth for healers and ranged fighters, leather for melee (Ian, 2026-11-20).</summary>
    public bool RangedWearCloth { get; set; } = true;
    public double NoBagProductivity { get; set; } = 0.85;
    public int BagWearPerHour { get; set; } = 3;
    /// <summary>Hours of fighting wear a dangerous-zone party trip puts on gear, on top of wipes (Ian, 2026-11-20: gear wears with use, not just death).</summary>
    public double PartyTripWearHours { get; set; } = 0;
    public int HorseMinLevel { get; set; } = 21;
    public long HorsePrice { get; set; } = 5000;
    public double HorseExtraWeight { get; set; } = 100;
    public double HorseTravelFactor { get; set; } = 0.75;
    public int SaddleWearPerHour { get; set; } = 2;
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
    public double StockpileTaskPriceShare { get; set; } = 0.8;
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
    public double PartyGatherPerHour { get; set; } = 11;
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
    public int WeaponWearPerHour { get; set; } = 10;
    public int ArmorWearPerHour { get; set; } = 10;
    public double PotionsPerFightHour { get; set; } = 0.2;
    /// <summary>
    /// Food heals over time (Ian, 2026-11-22, WoW style): this share of the
    /// heals a fighter needs comes from eating between fights (bread or roast,
    /// FoodHealUnits each). Potions stay the instant heal for mid-fight and deaths.
    /// </summary>
    public double FoodHealShare { get; set; } = 0;
    public double FoodHealUnits { get; set; } = 1;
    /// <summary>Fighters restock healing food (bread or roast) when under this many, up to twice it.</summary>
    public int FoodHealStock { get; set; } = 2;
    /// <summary>
    /// Meals give a health regen buff (Ian, 2026-11-22): the share of potion use
    /// each meal saves while it lasts. Bread is the poor meal: little regen, fills less.
    /// </summary>
    public bool MealRegen { get; set; } = true;
    public double BreadRegen { get; set; } = 0.1;
    public double RoastRegen { get; set; } = 0.35;
    public double StewRegen { get; set; } = 0.5;
    public double BreadFoodValue { get; set; } = 0.6;
    /// <summary>Every town's NPC smith repairs when no player crafter can: dearer, and repairs like a novice.</summary>
    public bool NpcRepair { get; set; } = true;
    public long NpcRepairPrice { get; set; } = 300;
    public int NpcRepairSkill { get; set; } = 10;
    /// <summary>Harder zones (Ian, 2026-11-20): heals and buff meals used per fight hour rise by this share per level band above the first.</summary>
    public double ZoneSupplyPerBand { get; set; } = 0;
    public double FighterHidePerHour { get; set; } = 0.4;
    public double FighterMeatPerHour { get; set; } = 0.5;
    public double RelicCoreDropPerHour { get; set; } = 0.015;
    public double DungeonRunMultiplier { get; set; } = 1.0;
    /// <summary>
    /// Players weigh a relic core's price against farming one (Ian, 2026-11-22):
    /// they pay at most the coins they'd earn in the hours it takes to farm it,
    /// otherwise they run dungeons themselves (drops x CoreHuntFactor until they have one).
    /// </summary>
    public bool CoreFarmChoice { get; set; } = true;
    public double CoreHuntFactor { get; set; } = 2.0;
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
    public double CourierFeeShare { get; set; } = 0.1;
    public double CourierWeightFee { get; set; } = 1;
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
        ["iron_ore"] = 2, ["copper_ore"] = 2, ["coal"] = 1.5, ["timber"] = 2, ["hide"] = 1, ["meat"] = 1, ["grain"] = 0.5,
        ["fiber"] = 0.3, ["herbs"] = 0.2, ["bark"] = 0.3, ["ember_slag"] = 1.5, ["ice_crystal"] = 1, ["relic_core"] = 0.5,
        ["iron_ingot"] = 1, ["plank"] = 1, ["firewood"] = 1, ["flour"] = 0.5, ["cloth"] = 0.3, ["leather"] = 0.5,
        ["padding"] = 0.5, ["nails"] = 0.2, ["bread"] = 0.3, ["roast_meat"] = 0.3, ["stew"] = 0.5, ["stock"] = 0.5,
        ["healing_potion"] = 0.3, ["bandage"] = 0.1, ["pickaxe"] = 3, ["woodaxe"] = 3, ["sickle"] = 1,
        ["skinning_knife"] = 0.5, ["sword"] = 3, ["blade_blank"] = 2, ["leather_armor"] = 5, ["plate_armor"] = 15,
        ["cloth_armor"] = 2, ["relic_sword"] = 3, ["travel_pack"] = 1, ["saddle"] = 8, ["quiver"] = 1, ["gauze"] = 0.1, ["reagent_bag"] = 1, ["furniture"] = 20,
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
    /// <summary>Copper paid to retrain into a new profession (50 silver); 0 makes switching free.</summary>
    public long RoleSwitchFee { get; set; } = 5000;

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
            Extra = new() { ["blacksmithing"] = new() { ["coal"] = 3, ["leather"] = 1, ["ember_slag"] = 1 }, ["leatherworking"] = new() { ["bark"] = 3, ["cloth"] = 1, ["ice_crystal"] = 1 }, ["tailoring"] = new() { ["ice_crystal"] = 1 } } },
    };

    /// <summary>New players get their role's starter kit from a short quest instead of buying it.</summary>
    public bool StarterQuest { get; set; } = true;
    /// <summary>Players set coins aside for missing or outleveled gear before buying extras.</summary>
    public bool SaveForGear { get; set; } = true;
    /// <summary>Everyone's starter kit has a weapon and armor (off: only main fighters).</summary>
    public bool StarterGearForAll { get; set; } = true;
    /// <summary>Roast meat (meat + firewood) is an everyday food beside bread.</summary>
    public bool RoastMeat { get; set; } = true;
    /// <summary>Herbs gathered or grown per hour, as a share of the base rate (herbs were flooding the market).</summary>
    public double HerbYieldMultiplier { get; set; } = 0.7;
    /// <summary>Hidden yield balancer: raw-material yields drift down while a good trades below its usual price, up while above.</summary>
    public bool YieldBalancer { get; set; } = true;
    public double YieldBalancerBand { get; set; } = 0.15;
    public double YieldBalancerStep { get; set; } = 0.02;
    public double YieldBalancerMin { get; set; } = 0.6;
    public double YieldBalancerMax { get; set; } = 1.4;
    /// <summary>Fresh coins per fight hour that towns add to bounties (a faucet); 0 = bounties only recycle sinks.</summary>
    public double BountyMintPerFightHour { get; set; } = 30;
    /// <summary>Copper sits in harder zones; miners below this level can't reach it.</summary>
    public int CopperMinLevel { get; set; } = 21;
    /// <summary>Healers heal with abilities: fighting with one uses this share of the potions and buff meals.</summary>
    public bool HealersReduceConsumables { get; set; } = true;
    public double HealedConsumableFactor { get; set; } = 0.3;
    /// <summary>Chance a fighter groups up when enough healers are out.</summary>
    public double GroupFightChance { get; set; } = 0.6;

    /// <summary>Hours of play the starter quest takes.</summary>
    public double StarterQuestHours { get; set; } = 1.0;

    /// <summary>Lowest price a player will sell at, as a share of the starting price.</summary>
    public double SellerFloorShare { get; set; } = 0.5;
    public double MemberShare { get; set; } = 0.5;
    public int FreeTokenAtDay { get; set; } = 30;

    // Shared rules
    public DurabilitySettings Durability { get; set; } = new() { CoreSurvivesBreak = true };
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
