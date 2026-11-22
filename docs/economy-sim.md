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

## What the sim has

- **Markets** (`Economy.Core/Markets/OrderBook`): one limit order book per good per town, price-time priority, fills at the resting price, buyers can ask for a minimum quality. Buy orders hold their coins in escrow. A 3% market tax is the main trade sink.
- **Agents** (`Economy.Sim/World`): 2,000 players across 15 roles (gatherers, crafters, repairers, haulers, fighters, casuals). Each keeps a belief price per good that moves with every fill and every unfilled hour. Gatherers stop at a full stockpile, crafters pick the most profitable recipe and make missing parts themselves, haulers buy where goods are cheap and ride (empty if need be) to where they are dear. Every player fights (design rule): a player's role is their side focus, so main fighters spend about 85% of their hours fighting and everyone else about 35%, with a combat level separate from their trade skill. Fighting players eat stew as a combat buff, wear gear, drink potions and earn contract pay weighted by combat power. Fighting hours can become party trips: players in a town group up (ideally tank, healer and three damage, haulers placed first), gather the richer, separate stock of the dangerous wilds at +15 quality, carry what their haulers allow, split it evenly, and risk a wipe that drops the cargo (2% with a tank and healer, 12% without). Solo gathering near towns yields 30% less. Farm plots are limited to 10% of players, and better tools raise gathered quality, not yield. Players quit when they lose money or go hungry; new players lean toward roles that pay.
- **Hidden stabilizer** (`Economy.Core/Stabilizer`): per town market, four NPC roles (trader caravan, buyer guild, hired hand, market maker) spawn after 6 hours out of band, grow to a cap, retire after 24 hours back in band, then wait 72 hours before they may spawn again. They only place ordinary orders, paid from the finite treasury. The guild stops buying when its warehouse holds two weeks of volume. References are a 14-day moving average of player prices, set from the market at the end of calibration (day 15).
- **Faucets and the treasury**: town contracts pay fighters back what sinks and NPC sales brought in, scaled up when coins per player are below the 5,000 target and down when above, so the treasury neither drains nor hoards. Coins that quitters take with them are not refilled once the treasury is below its starting level.
- **Token**: real-money tokens listed for coins, member renewals, a free token after 30 days, 2% listing fee.
- **Scenarios** (`Economy.Sim/Scenarios`): the 16 shock tests from the spec (scenario 15 is tests 2-11 rerun with the stabilizer off).
- **Analysis** (`Economy.Sim/Reporting`): per-run metrics, the 11 pass/fail checks, and the seed sweep.

Coins are on a x10 scale from the first design numbers (target 5,000 per player, iron ore about 40 at design price) so cheap goods are not stuck at 1 coin.

## Starting prices

`data/start_prices.json` holds the prices the calm baseline settles at, so runs start near equilibrium instead of drifting for months. Regenerate it after changing rules or rates:

```
dotnet run -c Release --project src/Economy.Sim -- --calibrate --seeds 4
```

Prices pinned at the sellers' floor are clamped to 25% of the design price, since a floor price means too much supply, not the right price.

## Running

```
dotnet test                                                        # all tests
dotnet run -c Release --project src/Economy.Sim -- --days 180 --roles --out out/base   # one world, CSVs out
dotnet run -c Release --project src/Economy.Sim -- --scenario miners-quit --seed 3      # one shock test
dotnet run -c Release --project src/Economy.Sim -- --scenario miners-quit --no-stabilizer
dotnet run -c Release --project src/Economy.Sim -- --sweep --seeds 20 --out results/phase1  # the full gate
```

Flags: `--scenario <id or number>`, `--seed`, `--days`, `--agents`, `--out <dir>` (CSV per day, market and hour, plus stabilizer events), `--no-stabilizer`, `--roles` (role counts and incomes at the end), `--inspect <Role>` (a few agents' inventories and orders), `--verbose` (print every day).

The sweep writes `sweep.json` (criteria, per-scenario medians, every run, and daily series for seed 1) and `runs.csv`. On 4 cores the 500-run sweep takes about 11 minutes.
