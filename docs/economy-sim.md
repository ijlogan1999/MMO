# Economy simulation (phase 1)

Phase 1 is a headless program that runs one simulated world for months of game time in minutes.
Its gate: prices stay in band through every shock scenario, across many random seeds, with no
settings changed mid-run. The full spec is the project's "Economy Simulation Spec (Phase 1)" doc.

## Layout

| Path | What it is |
| --- | --- |
| `src/Economy.Core` | Shared economy rules. netstandard2.1 and C# 9, no engine, server or serializer dependencies, so the Unity client, the game server and the sim all reference it. |
| `src/Economy.Sim` | The headless simulation (.NET 8 console app). Loads `data/` and runs scenarios. |
| `tests/Economy.Core.Tests` | xUnit tests for the core rules and the game data. |
| `data/` | Goods and recipes as JSON. Tune these without recompiling; phase 2 reads the same files. |

## What the core has so far

- **Deterministic random** (`Random/SimRandom`): xoshiro256**, so a seed replays the same run on any platform. `Fork` gives each subsystem its own stream.
- **Goods** (`Items/Goods`): tiers raw, intermediate, component, finished and relic core; uses material, food, consumable, equipment.
- **Quality** (`Items/Quality`, `Crafting/QualityRules`): 1 to 100, five market bands. Skill and method set the range, inputs cap it (weakest input by default; weighted average is a setting while the crafting spec decides), performance and a random nudge pick where it lands. Botches make a terrible but usable item. Quick craft averages recent manual crafts.
- **Durability and repair** (`Items/DurabilityRules`): wear, breaks, repair keeps 50% (novice) to 90% (expert) of the ceiling, upkeep before a break costs far less, items eventually wear out for good.
- **Relic cores**: magical gear needs a core to be made, the core shatters when the item breaks, and repairing it needs a new core. Cores cannot be crafted.
- **Coin ledger** (`Ledger/CoinLedger`): double-entry, every movement tagged with a named faucet, sink or transfer. Sinks can only pay the treasury; only the starter purse creates coins; the treasury can never go negative. `Verify()` checks the books to the coin.

## Coming next

Markets (order books, NPC vendors, hauling), agents, the hidden stabilizer, and the 16 shock scenarios with their reports.

## Running

```
dotnet test                         # all tests
dotnet run --project src/Economy.Sim -- 1   # smoke run with seed 1
```
