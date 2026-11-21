using Economy.Core.Crafting;
using Economy.Core.Items;
using Economy.Core.Ledger;
using Economy.Core.Random;
using Economy.Sim;

// Phase 1 economy simulation entry point.
// For now this checks the shared economy core end to end: it loads the game
// data, crafts a few items, wears and repairs them, and moves coins through
// the ledger, then verifies the books. Markets, agents, the stabilizer and
// scenarios arrive in later steps (see docs/economy-sim.md).

ulong seed = args.Length > 0 && ulong.TryParse(args[0], out var s) ? s : 1UL;
var rng = new SimRandom(seed);

var catalog = DataLoader.LoadCatalog(DataLoader.FindDataDirectory());
Console.WriteLine($"Loaded {catalog.Goods.Count()} goods and {catalog.Recipes.Count()} recipes (seed {seed}).");

var quality = new QualityRules();
var durability = new DurabilityRules();
var ledger = new CoinLedger(verifyEveryTransfer: true);

// Treasury starts with a seed; two new players get starter purses.
ledger.Transfer(ledger.Mint, ledger.Treasury, 100_000, Flow.TreasurySeed);
var smith = ledger.OpenPlayer("smith");
var miner = ledger.OpenPlayer("miner");
ledger.Transfer(ledger.Mint, smith, 500, Flow.StarterPurse);
ledger.Transfer(ledger.Mint, miner, 500, Flow.StarterPurse);

// The smith crafts a pickaxe and sells it to the miner, paying tax and a station fee.
var recipe = catalog.GetRecipe("forge_pickaxe");
var inputs = new[] { new InputQuality(60, 2), new InputQuality(55, 1) };
int skill = 45;
var roll = quality.Roll(recipe, skill, inputs, quality.SimulatedPerformance(skill, rng), rng);
var pickaxe = durability.Create(catalog.GetGood("pickaxe"), roll.Quality);
ledger.Transfer(smith, ledger.Treasury, 5, Flow.StationFee);
ledger.Transfer(miner, smith, 120, Flow.PlayerTrade);
ledger.Transfer(smith, ledger.Treasury, 4, Flow.MarketTax);
Console.WriteLine($"Crafted {pickaxe} (range {roll.Floor}-{roll.Ceiling}{(roll.Botched ? ", botched" : "")}).");

// The miner wears it to breaking, then pays for a repair.
while (durability.Wear(pickaxe, 25) == WearOutcome.Worn) { }
durability.RepairBroken(pickaxe, repairerSkill: 70);
ledger.Transfer(miner, ledger.Treasury, 10, Flow.RepairFee);
Console.WriteLine($"After one break and a repair: {pickaxe}.");

ledger.Verify();
Console.WriteLine($"Ledger balanced: {ledger.Circulating} coins circulating = faucets {ledger.FaucetTotal} - sinks {ledger.SinkTotal}; treasury {ledger.TreasuryBalance}.");
