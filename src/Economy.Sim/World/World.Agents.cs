using Economy.Core.Crafting;
using Economy.Core.Items;
using Economy.Core.Ledger;

namespace Economy.Sim.World;

public sealed partial class World
{
    private readonly List<InputQuality> _inputs = new();
    private int _onlineFightersThisHour;
    private readonly List<Agent> _fightersThisHour = new();

    /// <summary>Whether this player can fight at all (everyone but bots and trading groups).</summary>
    private bool Fights(Agent a) =>
        a.Role == Role.Fighter || (Config.EveryoneFights && a.Role is not (Role.Bot or Role.Speculator));

    private bool FightsThisHour(Agent a)
    {
        if (!Config.EveryoneFights) return false;
        if (!Fights(a)) return false;
        double share = a.Role == Role.Fighter ? Config.MainFighterFightShare : Config.FocusFightShare;
        return _agentRng.Chance(share);
    }

    private void AgentTurn(Agent a)
    {
        // Reprice: take back last hour's unfilled orders and learn from them.
        CancelAgentOrders(a);

        double productivity = Eat(a);
        a.OnlineHoursWeek += 1;

        if (FightsThisHour(a))
        {
            Fight(a, productivity);
            if (Config.Parties && PartyEligible(a.Role) && _agentRng.Chance(Config.PartyTripChance))
            {
                JoinPartyPool(a);
                if (a.Inventory[Pack] == 0 && Coins(a) > 1500)
                    PlaceBuy(a, Pack, 1, Math.Max(1, (long)Math.Round(a.Belief[Pack])));
            }
            else if (Config.Parties && !PartyEligible(a.Role) && a.CombatRole != CombatRole.Damage)
                JoinHirePool(a);
        }
        else switch (a.Role)
        {
            case Role.Miner:
            case Role.Logger:
            case Role.Herbalist:
            case Role.Hunter:
            case Role.Farmer:
            case Role.Bot:
                Gather(a, productivity);
                break;
            case Role.Smith:
            case Role.Cook:
            case Role.Tailor:
            case Role.Leatherworker:
            case Role.Alchemist:
            case Role.Carpenter:
                Craft(a, productivity);
                if (Roles.Repairs(a.Role)) AdjustRepairPrice(a);
                break;
            case Role.Repairer:
                RepairerTurn(a);
                break;
            case Role.Hauler:
                if (Config.CourierJobs) CourierTurn(a, productivity);
                else HaulerTurn(a);
                break;
            case Role.Fighter:
                if (Config.EveryoneFights) Gather(a, productivity * 0.6); // side gathering
                else Fight(a, productivity);
                break;
            case Role.Casual:
                Gather(a, productivity * 0.6);
                break;
            case Role.Speculator:
                SpeculatorTurn(a);
                break;
        }

        if (a.Role is not (Role.Speculator or Role.Bot)) SoloDeliver(a);
        if (a.Role != Role.Speculator && (a.Role != Role.Hauler || Config.CourierJobs)) { SellSurplus(a); SellExcessToNpcs(a); SellRemoteStock(a); }
        BuyFood(a);
        // Well-off players furnish their homes (carpenters' furniture).
        if (a.Role is not (Role.Speculator or Role.Bot) && Coins(a) > Config.FurnitureWealth
            && _agentRng.Chance(Config.FurnitureBuysPerDay / Math.Max(1, a.SessionHours)))
            PlaceBuy(a, Furniture, 1, Math.Max(1, (long)Math.Round(a.Belief[Furniture])));
    }

    // ---------------------------------------------------------------- needs

    /// <returns>Productivity factor: 1 when fed, 0.5 when hungry.</returns>
    private double Eat(Agent a)
    {
        a.FoodHours -= 1;
        if (a.FoodHours > 0) return 1;
        foreach (int food in Food)
        {
            if (a.Inventory.Take(food, 1) == 1)
            {
                a.FoodHours += (food == Stew ? Config.StewFoodValue : 1.0) / Config.FoodPerHour;
                return 1;
            }
        }
        a.FoodHours = 0;
        return 0.5;
    }

    private void BuyFood(Agent a)
    {
        int held = a.Inventory[Bread] + a.Inventory[Stew] * 2;
        if (held >= 3) return;
        // Richer players prefer stew; everyone falls back to bread.
        long coins = Coins(a);
        int want = 6 - held;
        bool stew = Config.BreadObsolete || (coins > 15000 && a.Belief[Stew] < a.Belief[Bread] * 3);
        int good = stew ? Stew : Bread;
        int qty = stew ? (want + 1) / 2 : want;
        if (!stew) { qty -= TryNoviceMake(a, good, qty); if (qty <= 0) return; }
        long price = Math.Max(1, (long)Math.Round(a.Belief[good]));
        if (price * qty > coins / 3) qty = (int)Math.Max(1, coins / 3 / price);
        PlaceBuy(a, good, qty, price);
    }

    /// <summary>
    /// Keeps a piece of equipment working: repairs it when broken (or tops it
    /// up if the owner is careful), salvages it when worn out, and buys a new
    /// one when there is nothing to repair. Returns the usable item, or null.
    /// </summary>
    private Equipment? Maintain(Agent a, Equipment? item, int goodIfNone, Action<Equipment?> set)
    {
        if (item != null)
        {
            if (item.IsBroken || (a.Careful && item.Durability < item.MaxDurability * 0.3))
            {
                if (Durability.IsWornOut(item) || !TryRepair(a, item))
                {
                    if (item.IsBroken)
                    {
                        Salvage(a, item);
                        item = null;
                        set(null);
                    }
                }
            }
            if (item != null && item.IsUsable) return item;
        }

        // Need a new one: use one already bought, else buy.
        int good = item?.Good != null ? Goods[item.Good.Id] : goodIfNone;
        if (a.Inventory[good] > 0)
        {
            int q = a.Inventory.QualityOf(good);
            a.Inventory.Take(good, 1);
            int? core = null;
            if (Goods.Defs[good].IsMagical) core = q;
            var fresh = Durability.Create(Goods.Defs[good], q, core);
            set(fresh);
            return fresh;
        }
        if (!PostWorkOrder(a, good))
        {
            long price = (long)Math.Round(AskOrBelief(a, good));
            PlaceBuy(a, good, 1, Math.Max(1, price));
        }
        return item != null && item.IsUsable ? item : null;
    }

    private void Salvage(Agent a, Equipment item)
    {
        int g = Goods[item.Good.Id];
        int material = Goods.RepairMaterial[g];
        if (material < 0) return;
        var recipe = Goods.RecipesByOutput[g].FirstOrDefault();
        int qty = recipe?.Inputs.Where(i => Goods[i.GoodId] == material).Sum(i => i.Quantity) ?? 1;
        int back = RandomRound(qty * Config.Durability.SalvageReturn, _agentRng);
        a.Inventory.Add(material, back, item.Quality * 0.5);
    }

    /// <summary>Finds a repairer in town, pays them and the station fee, and repairs or tops up the item.</summary>
    private bool TryRepair(Agent owner, Equipment item)
    {
        int g = Goods[item.Good.Id];
        int material = Goods.RepairMaterial[g];
        bool magical = item.Good.IsMagical && item.IsBroken;
        if (magical && owner.Inventory[RelicCore] == 0)
        {
            // Repairing a broken magical item needs a new core: buy one first.
            PlaceBuy(owner, RelicCore, 1, (long)Math.Round(AskOrBelief(owner, RelicCore)));
            if (owner.Inventory[RelicCore] == 0) return false;
        }

        Agent? best = null;
        // Better gear takes more material to repair.
        int units = (int)Math.Ceiling(Config.GearTiers[TierFor(item.Quality)].InputMultiplier);
        // Repair is a lesser form of making: the craft that makes the item fixes it.
        string? craft = Goods.RecipesByOutput[g].FirstOrDefault()?.Skill;
        foreach (int id in Towns[owner.Town].Repairers)
        {
            var r = Agents[id];
            if (!r.Active || r.Id == owner.Id) continue;
            if (r.Role != Role.Repairer && Roles.CraftSkill(r.Role) != craft) continue;
            if (material >= 0 && r.Inventory[material] < units) continue;
            if (best == null || r.RepairPrice < best.RepairPrice) best = r;
        }
        if (best == null) return false;

        long cost = best.RepairPrice + Config.RepairStationFee;
        if (Coins(owner) < cost) return false;
        Ledger.Transfer(owner.Account, best.Account, best.RepairPrice, Flow.PlayerTrade);
        best.TodayIncome += best.RepairPrice;
        Track(best, IncomeKind.Repairs, best.RepairPrice);
        Ledger.Transfer(owner.Account, Ledger.Treasury, Config.RepairStationFee, Flow.RepairFee);
        if (material >= 0) best.Inventory.Take(material, units);
        best.RepairsToday++;
        if (best.Role == Role.Repairer) best.RepairSkill = Math.Min(100, best.RepairSkill + Config.SkillGainPerHour * 2);

        if (item.IsBroken)
        {
            int? core = null;
            if (item.Good.IsMagical)
            {
                core = owner.Inventory.QualityOf(RelicCore);
                owner.Inventory.Take(RelicCore, 1);
            }
            return Durability.RepairBroken(item, (int)(best.Role == Role.Repairer ? best.RepairSkill : best.Skill), core);
        }
        Durability.Upkeep(item, (int)(best.Role == Role.Repairer ? best.RepairSkill : best.Skill));
        return true;
    }

    // ---------------------------------------------------------------- gathering

    private static readonly string[] MinerGoods = { "iron_ore", "coal" };
    private static readonly string[] LoggerGoods = { "timber" };
    private static readonly string[] HerbalistGoods = { "herbs" };
    private static readonly string[] FarmerGoods = { "grain", "fiber", "herbs" };

    private void Gather(Agent a, double productivity)
    {
        string toolGood = a.Role switch
        {
            Role.Logger => "woodaxe",
            Role.Hunter => "skinning_knife",
            Role.Herbalist or Role.Farmer => "sickle",
            _ => "pickaxe",
        };
        var tool = Maintain(a, a.Tool, Goods[toolGood], t => a.Tool = t);
        // Like skill, a better tool raises the quality of what is gathered,
        // not the amount (Ian, 2026-11-19). Having no tool still slows you down.
        double toolFactor = tool != null ? 1.0 : Config.NoToolFactor;
        _gatherToolBonus = tool != null ? Config.ToolQualityBonus * (tool.Quality - 50) / 50.0 : 0;
        // Skill raises the quality of what is gathered, not the amount, so a
        // server full of veterans does not flood the market as everyone levels.
        const double skillFactor = 1.0;
        double rate = productivity * toolFactor * skillFactor;
        var town = Towns[a.Town];

        // Nobody keeps gathering what they cannot sell: with a full stockpile
        // of something, a player gathers something else or does other things.
        bool room(int g) => a.Inventory[g] < Config.StockpileLimit;

        if (a.Role == Role.Hunter)
        {
            if (!room(Goods["hide"]) && !room(Goods["meat"])) return;
            GatherFromNode(a, town, Goods["hide"], Config.HuntHidePerHour * rate);
            GatherFromNode(a, town, Goods["meat"], Config.HuntMeatPerHour * rate);
        }
        else if (a.Role == Role.Farmer)
        {
            int best = BestByBelief(a, FarmerGoods, room);
            if (best < 0) return;
            double yield = Config.FarmYieldPerHour * rate * (Goods.Defs[best].Id == "grain" ? Config.GrainYieldMultiplier : 1.0);
            if (Goods.Defs[best].Id == "herbs") yield *= 0.5;
            AddProduce(a, best, yield);
        }
        else
        {
            var options = a.Role switch
            {
                Role.Miner or Role.Bot => MinerGoods,
                Role.Logger => LoggerGoods,
                Role.Herbalist => HerbalistGoods,
                _ => Config.SideGatherGoods,
            };
            if (a.Role == Role.Bot && a.TargetGood >= 0) GatherFromNode(a, town, a.TargetGood, Config.GatherPerHour * rate);
            else
            {
                int best = BestByBelief(a, options, g => town.Stock[g] >= 1 && room(g));
                if (best >= 0) GatherFromNode(a, town, best, Config.GatherPerHour * rate);
            }
        }

        if (tool != null) Durability.Wear(tool, Config.ToolWearPerHour);
        a.Skill = Math.Min(100, a.Skill + Config.SkillGainPerHour);
    }

    private int BestByBelief(Agent a, string[] options, Func<int, bool> available)
    {
        int best = -1;
        double bestValue = double.MinValue;
        foreach (var id in options)
        {
            int g = Goods[id];
            if (!available(g)) continue;
            // A little noise so agents do not all pile onto the same good.
            double v = a.Belief[g] * (0.9 + 0.2 * _agentRng.NextDouble());
            if (v > bestValue) { bestValue = v; best = g; }
        }
        return best;
    }

    private double _gatherToolBonus;

    private void GatherFromNode(Agent a, Town town, int good, double amount)
    {
        if (Config.Parties) amount *= Config.SoloGatherYield;
        amount = Math.Min(amount, town.Stock[good]);
        if (amount <= 0) return;
        town.Stock[good] -= amount;
        double cap = town.RegenPerHour[good] * Config.NodeStockHours;
        if (town.Stock[good] < cap * Config.OverharvestFraction && town.DepletedUntilHour[good] <= Hour)
            town.DepletedUntilHour[good] = Hour + Config.OverharvestDays * 24;
        AddProduce(a, good, amount);
    }

    private void AddProduce(Agent a, int good, double amount)
    {
        int n = RandomRound(amount, _agentRng);
        if (n <= 0) return;
        double q = Math.Clamp(_agentRng.NextGaussian(30 + 0.6 * a.Skill + _gatherToolBonus, 8), 1, 100);
        a.Inventory.Add(good, n, q);
        TrackGood(good, GoodFlowKind.Gathered, n);
        RegionProducedHour[Towns[a.Town].Region, good] = Hour;
    }

    // ---------------------------------------------------------------- crafting

    private void Craft(Agent a, double productivity)
    {
        string skill = Roles.CraftSkill(a.Role)!;
        Recipe? best = null;
        double bestProfit = 0;
        // A work order comes first: hand it over if made, else work on it.
        var order = TakeWorkOrder(a);
        if (order != null)
        {
            DeliverWorkOrder(a, order);
            if (_orders.Contains(order)) best = Goods.RecipesByOutput[order.Good].First();
        }
        // Orders are for the common version: a skilled crafter makes it well, but with common materials.
        double tierSkill = order != null && _orders.Contains(order) ? Math.Min(a.Skill, OrderTierSkill) : a.Skill;
        if (best == null)
        foreach (var r in Goods.Catalog.Recipes)
        {
            if (r.Skill != skill || r.MinSkill > a.Skill) continue;
            int output = Goods[r.OutputGoodId];
            // A real crafter stops making (and buying materials for) what is not selling.
            if (Config.CraftersWatchSales && a.Inventory[output] >= UnsoldLimit(a, output)) continue;
            double value = a.Belief[output] * r.OutputQuantity;
            double cost = Config.StationFee;
            foreach (var (g, q) in TieredInputs(r, a.Skill)) cost += InputCost(a, g) * q;
            double perHour = (value - cost) * (60.0 / r.CraftMinutes);
            // Do not flood a market: count what we already hold unsold.
            perHour *= 1.0 / (1 + a.Inventory[output] / 20.0);
            if (perHour > bestProfit) { bestProfit = perHour; best = r; }
        }
        if (best == null) return;

        // Missing a component we could make ourselves (a blade blank, flour)?
        // Make that first when the market is not offering it cheaply.
        var recipe = best;
        foreach (var (g, q) in TieredInputs(best, tierSkill))
        {
            if (a.Inventory[g] >= q) continue;
            var ask = Towns[a.Town].Markets[g].Book.BestAsk;
            if (ask.HasValue && ask.Value <= a.Belief[g] * 1.2) continue;
            var sub = Goods.RecipesByOutput[g].FirstOrDefault(r => r.Skill == skill && r.MinSkill <= a.Skill);
            if (sub != null) { recipe = sub; break; }
        }

        int crafts = Math.Max(1, (int)Math.Round(60.0 / recipe.CraftMinutes * productivity));
        int possible = crafts;
        var tiered = TieredInputs(recipe, tierSkill);
        foreach (var (g, q) in tiered) possible = Math.Min(possible, a.Inventory[g] / q);

        int output2 = Goods[recipe.OutputGoodId];
        for (int i = 0; i < possible; i++)
        {
            if (!Ledger.TryTransfer(a.Account, Ledger.Treasury, Config.StationFee, Flow.StationFee, out _)) break;
            _inputs.Clear();
            int coreQuality = 0;
            foreach (var (g, need) in tiered)
            {
                int q = a.Inventory.QualityOf(g);
                a.Inventory.Take(g, need);
                TrackGood(g, GoodFlowKind.UsedInCrafts, need);
                a.SoldToday[g] += need; // using stock up counts as moving it, like a sale
                _inputs.Add(new InputQuality(q, need));
                if (g == RelicCore) coreQuality = q;
            }
            var roll = QualityRules.Roll(recipe, (int)a.Skill, _inputs, QualityRules.SimulatedPerformance((int)a.Skill, _agentRng), _agentRng);
            int quality = Goods.Defs[output2].IsMagical && coreQuality > 0 ? Math.Min(roll.Quality, coreQuality + 20) : roll.Quality;
            a.Inventory.Add(output2, recipe.OutputQuantity, quality);
            TrackGood(output2, GoodFlowKind.Crafted, recipe.OutputQuantity);
            if (a.CommissionLeft > 0) PayCommission(a, output2, recipe.OutputQuantity);
            RegionProducedHour[Towns[a.Town].Region, output2] = Hour;
            a.Skill = Math.Min(100, a.Skill + Config.SkillGainPerHour / Math.Max(1, crafts));
        }

        // Order inputs for the next hour's crafting: take a fair ask now, else bid.
        long coins = Coins(a);
        foreach (var (g, q) in tiered)
        {
            int need = q * crafts - a.Inventory[g];
            if (need <= 0) continue;
            var ask = Towns[a.Town].Markets[g].Book.BestAsk;
            long price = ask.HasValue && ask.Value <= a.Belief[g] * 1.25
                ? ask.Value
                : Math.Max(1, (long)Math.Round(a.Belief[g]));
            if (price * need > coins / 2) need = (int)(coins / 2 / price);
            if (need > 0) PlaceBuy(a, g, need, price);
        }
    }

    /// <summary>How much unsold stock of a good a crafter will hold before making more: a few days of recent sales and own use.</summary>
    private double UnsoldLimit(Agent a, int good) =>
        Math.Max(Config.CraftStockMin, a.SalesPerDay[good] * Config.CraftStockDays);

    private readonly Dictionary<(Recipe, int), List<(int good, int qty)>> _tieredCache = new();

    /// <summary>Gear tier for a skill level (or an item's quality).</summary>
    public int TierFor(double skillOrQuality)
    {
        int t = 0;
        for (int i = 0; i < Config.GearTiers.Length; i++) if (skillOrQuality >= Config.GearTiers[i].MinSkill) t = i;
        return t;
    }

    /// <summary>
    /// Inputs for one craft at the crafter's tier. Only gear is tiered:
    /// food, potions and materials use the base recipe.
    /// </summary>
    private List<(int good, int qty)> TieredInputs(Recipe r, double skill)
    {
        int output = Goods[r.OutputGoodId];
        int tier = Goods.Defs[output].Use == GoodUse.Equipment ? TierFor(skill) : 0;
        if (_tieredCache.TryGetValue((r, tier), out var list)) return list;
        var t = Config.GearTiers[tier];
        list = r.Inputs.Select(i => (Goods[i.GoodId], i.GoodId == "relic_core" ? i.Quantity : (int)Math.Ceiling(i.Quantity * t.InputMultiplier))).ToList();
        if (t.Extra.TryGetValue(r.Skill, out var extra))
            foreach (var kv in extra)
            {
                int g = Goods[kv.Key];
                int idx = list.FindIndex(x => x.Item1 == g);
                if (idx >= 0) list[idx] = (g, list[idx].Item2 + kv.Value);
                else list.Add((g, kv.Value));
            }
        _tieredCache[(r, tier)] = list;
        return list;
    }

    /// <summary>What an input would cost right now: a fair ask, else the agent's belief.</summary>
    private double InputCost(Agent a, int g)
    {
        var ask = Towns[a.Town].Markets[g].Book.BestAsk;
        return ask.HasValue && ask.Value <= a.Belief[g] * 1.25 ? ask.Value : a.Belief[g];
    }

    // ---------------------------------------------------------------- selling

    /// <summary>Lists what the agent produced or no longer needs.</summary>
    private void SellSurplus(Agent a)
    {
        for (int g = 0; g < Goods.Count; g++)
        {
            int have = a.Inventory[g];
            if (have <= 0) continue;
            int keep = KeepAmount(a, g);
            int sell = have - keep;
            if (sell > 0 && Config.CourierJobs && !Goods.Defs[g].HasDurability && a.Role is not (Role.Bot or Role.Hauler))
                sell -= MaybePostCourierJob(a, g, sell / 2);
            // With NPCs buying the excess, list only a sensible amount on the market.
            if (DumpsExcess(g)) sell = Math.Min(sell, Config.NpcBuybackKeep - ListedUnits(a, g));
            if (sell <= 0) continue;
            long price = Math.Max(1, (long)Math.Round(a.Belief[g] * (0.97 + 0.06 * _agentRng.NextDouble())));
            if (a.Role == Role.Bot) price = Math.Max(1, (long)(a.Belief[g] * 0.9));
            PlaceSell(a, g, sell, price);
        }
    }

    private bool DumpsExcess(int g) => Config.NpcBuybackShare > 0 && Goods.Defs[g].Tier == GoodTier.Raw && !IsRare(g);

    private static int ListedUnits(Agent a, int g)
    {
        int n = 0;
        foreach (var o in a.Orders) if (o.Good == g && o.Order.Side == Economy.Core.Markets.Side.Sell) n += o.Order.Remaining;
        return n;
    }

    private int KeepAmount(Agent a, int g)
    {
        if (g == Bread) return 6;
        if (g == Stew) return Fights(a) ? 6 : 3;
        if (g == Potion && Fights(a)) return 6;
        if (g == Bandage && Fights(a)) return 6;
        if (g == Pack && Fights(a)) return 1;
        if (g == Furniture) return int.MaxValue / 2;
        if (g == RelicCore && Fights(a) && a.Weapon?.Good.IsMagical == true) return 1;
        if (Goods.Defs[g].HasDurability && !Roles.IsCrafter(a.Role) && a.Role != Role.Hauler) return 1;
        if (Roles.IsCrafter(a.Role) && g == RepairStockFor(a.Role)) return 4;
        if (a.Role == Role.Repairer && g == Goods.RepairMaterial[Goods["pickaxe"]]) return 8;
        if (a.Role == Role.Repairer && g == Goods.RepairMaterial[Goods["leather_armor"]]) return 8;
        if (Roles.IsCrafter(a.Role) && IsInputFor(a, g)) return 30;
        if (a.NoviceKeep.TryGetValue(g, out int nk)) return nk;
        return 0;
    }

    private bool IsInputFor(Agent a, int g)
    {
        string skill = Roles.CraftSkill(a.Role)!;
        string id = Goods.Defs[g].Id;
        foreach (var r in Goods.Catalog.Recipes)
            if (r.Skill == skill && r.Inputs.Any(i => i.GoodId == id)) return true;
        return false;
    }

    // ---------------------------------------------------------------- repairers

    /// <summary>The material a repairing crafter keeps on hand for repairs.</summary>
    private int RepairStockFor(Role r) => r switch
    {
        Role.Smith => Goods["iron_ingot"],
        Role.Leatherworker => Goods["leather"],
        Role.Tailor => Goods["cloth"],
        _ => -1,
    };

    private void AdjustRepairPrice(Agent a)
    {
        int m = RepairStockFor(a.Role);
        if (m >= 0 && a.Inventory[m] < 4) PlaceBuy(a, m, 4 - a.Inventory[m], Math.Max(1, (long)Math.Round(a.Belief[m])));
        if (a.RepairsToday > 3) a.RepairPrice = Math.Min(2000, a.RepairPrice + 10);
        else if (a.RepairsToday == 0 && a.RepairPrice > 40 && a.RepairsToday == 0) a.RepairPrice = Math.Max(40, a.RepairPrice - 2);
    }

    private void RepairerTurn(Agent a)
    {
        // Keep repair materials on hand; adjust the asking price to demand.
        foreach (var material in new[] { Goods.RepairMaterial[Goods["pickaxe"]], Goods.RepairMaterial[Goods["leather_armor"]] })
        {
            if (material < 0) continue;
            int need = 8 - a.Inventory[material];
            if (need > 0) PlaceBuy(a, material, need, Math.Max(1, (long)Math.Round(a.Belief[material])));
        }
        if (a.RepairsToday > 3) a.RepairPrice = Math.Min(2000, a.RepairPrice + 10);
        else if (a.RepairsToday == 0 && HourOfDay % 6 == 0) a.RepairPrice = Math.Max(40, a.RepairPrice - 10);
    }

    // ---------------------------------------------------------------- fighters

    private void Fight(Agent a, double productivity)
    {
        long coins = Coins(a);
        bool wantRelic = coins > 15000;
        int weaponGood = Goods[wantRelic ? "relic_sword" : "sword"];
        // Healers and casters wear cloth (tailors); others leather, then plate.
        int armorGood = Goods[a.CombatRole == CombatRole.Healer ? "cloth_armor" : coins > 10000 ? "plate_armor" : "leather_armor"];

        var weapon = Maintain(a, a.Weapon, weaponGood, w => a.Weapon = w);
        var armor = Maintain(a, a.Armor, armorGood, x => a.Armor = x);

        double power = productivity * (weapon != null ? 1.0 : 0.3) * (armor != null ? 1.0 : 0.7);
        // Low-level fighters heal with bandages (tailors), later with potions (alchemists).
        int heal = a.CombatSkill < Config.BandageMaxLevel ? Bandage : Potion;
        a.PotionCredit += Config.PotionsPerFightHour;
        if (a.PotionCredit >= 1)
        {
            a.PotionCredit -= 1;
            if (!TakeHealing(a, heal)) power *= 0.7;
        }
        if (a.Inventory[heal] < 3)
        {
            int want = 4 - TryNoviceMake(a, heal, 4);
            if (want > 0) PlaceBuy(a, heal, want, Math.Max(1, (long)Math.Round(a.Belief[heal])));
        }
        if (Config.FoodBuffs)
        {
            double t = a.CombatSkill / 100.0;
            a.BuffCredit += Config.BuffFoodPerFightHourMin + (Config.BuffFoodPerFightHourMax - Config.BuffFoodPerFightHourMin) * t;
            if (a.BuffCredit >= 1)
            {
                a.BuffCredit -= 1;
                if (a.Inventory.Take(Stew, 1) == 1) a.BuffHours = 1.0 / Math.Max(0.01, Config.BuffFoodPerFightHourMin + (Config.BuffFoodPerFightHourMax - Config.BuffFoodPerFightHourMin) * t);
            }
            if (a.BuffHours > 0) { a.BuffHours -= 1; power *= 1 + Config.BuffPowerBonus; }
            // Keep a few buff meals on hand; stronger fighters keep more.
            int keep = 2 + (int)Math.Round(3 * t);
            if (a.Inventory[Stew] < keep)
            {
                long coins2 = Coins(a);
                long price = Math.Max(1, (long)Math.Round(a.Belief[Stew]));
                int qty = keep + 1 - a.Inventory[Stew];
                if (price * qty > coins2 / 4) qty = (int)Math.Max(0, coins2 / 4 / price);
                if (qty > 0) PlaceBuy(a, Stew, qty, price);
            }
        }
        a.LastPower = power;

        if (weapon != null) Durability.Wear(weapon, Config.WeaponWearPerHour);
        if (armor != null) Durability.Wear(armor, Config.ArmorWearPerHour);

        // Stronger mobs kill more often; a death costs gear durability, a
        // heal to get back on your feet, and the hour's loot.
        double deathChance = Config.DeathChancePerFightHour * Config.MobStrength / Math.Max(0.3, power);
        if (_agentRng.Chance(Math.Min(0.5, deathChance)))
        {
            Deaths++;
            if (weapon != null) Durability.Wear(weapon, (int)Math.Round(Config.WeaponWearPerHour * Config.DeathWearHours));
            if (armor != null) Durability.Wear(armor, (int)Math.Round(Config.ArmorWearPerHour * Config.DeathWearHours));
            TakeHealing(a, heal);
            a.CombatSkill = Math.Min(100, a.CombatSkill + Config.SkillGainPerHour * 0.5);
            _fightersThisHour.Add(a);
            _onlineFightersThisHour++;
            return;
        }

        // Loot: materials from kills, and now and then a relic core from a dungeon.
        AddLoot(a, Goods["hide"], Config.FighterHidePerHour * power);
        AddLoot(a, Goods["meat"], Config.FighterMeatPerHour * power);
        if (_agentRng.Chance(Config.RelicCoreDropPerHour * Config.DungeonRunMultiplier * power))
            a.Inventory.Add(RelicCore, 1, Math.Clamp(_agentRng.NextGaussian(55, 15), 1, 100));

        a.CombatSkill = Math.Min(100, a.CombatSkill + Config.SkillGainPerHour);
        _fightersThisHour.Add(a);
        _onlineFightersThisHour++;
    }

    public long Deaths { get; private set; }

    /// <summary>Uses one heal, falling back to the other kind if out.</summary>
    private bool TakeHealing(Agent a, int preferred)
    {
        if (a.Inventory.Take(preferred, 1) == 1) return true;
        int other = preferred == Bandage ? Potion : Bandage;
        return a.Inventory.Take(other, 1) == 1;
    }

    private void AddLoot(Agent a, int good, double amount)
    {
        int n = RandomRound(amount, _agentRng);
        if (n > 0) a.Inventory.Add(good, n, Math.Clamp(_agentRng.NextGaussian(45, 10), 1, 100));
    }

    // ---------------------------------------------------------------- haulers

    private void HaulerTurn(Agent a)
    {
        // Sell cargo wherever we are; start a new trip once the wagon is empty.
        bool hasCargo = false;
        for (int g = 0; g < Goods.Count; g++)
        {
            int have = a.Inventory[g] - KeepAmount(a, g);
            if (have <= 0) continue;
            hasCargo = true;
            long price = Math.Max(1, (long)Math.Round(a.Belief[g]));
            PlaceSell(a, g, have, price);
        }
        if (hasCargo) return;

        var here = Towns[a.Town];
        long coins = Coins(a);
        var bestHere = BestHaul(a, here, coins);
        // Nothing worth carrying from here? Ride empty to the town with the best run.
        double bestElsewhere = 0;
        int bestSource = -1;
        foreach (var t in Towns)
        {
            if (t.Index == a.Town || !CanTravel(here, t)) continue;
            var h = BestHaul(a, t, coins);
            double perHour = h.profit * h.hours / (h.hours + _worldFile.TravelHoursBetween(here.Def.Region, t.Def.Region));
            if (perHour > bestElsewhere) { bestElsewhere = perHour; bestSource = t.Index; }
        }
        if (bestHere.good < 0 || bestElsewhere > bestHere.profit * 1.5)
        {
            if (bestSource < 0) return;
            a.TravelTo = bestSource;
            a.TravelHoursLeft = _worldFile.TravelHoursBetween(here.Def.Region, Towns[bestSource].Def.Region);
            return;
        }
        int bought = PlaceBuy(a, bestHere.good, bestHere.qty, bestHere.ask, rest: false);
        if (bought <= 0) return;
        a.TravelTo = bestHere.dest;
        a.TravelHoursLeft = bestHere.hours;
    }

    /// <summary>Best load to buy in <paramref name="from"/> and sell elsewhere, as profit per travel hour.</summary>
    private (int good, int dest, int qty, long ask, double profit, int hours) BestHaul(Agent a, Town from, long coins)
    {
        (int good, int dest, int qty, long ask, double profit, int hours) best = (-1, -1, 0, 0, 0, 1);
        for (int g = 0; g < Goods.Count; g++)
        {
            var book = from.Markets[g].Book;
            var ask = book.BestAsk;
            if (!ask.HasValue) continue;
            foreach (var t in Towns)
            {
                if (t.Index == from.Index || !CanTravel(from, t)) continue;
                var m = t.Markets[g];
                double sell = m.LastTradeHour >= Hour - 72 ? m.LastPrice : m.Reference.Value;
                double margin = sell * (1 - TaxRate) / ask.Value - 1;
                if (margin < Config.HaulMinMargin) continue;
                int qty = (int)Math.Min(Math.Min(book.AskDepthAtOrBelow(ask.Value), Config.WagonCapacity), coins / ask.Value);
                qty = Math.Min(qty, (int)Math.Max(10, m.DailyVolumeEma * 3));
                int hours = _worldFile.TravelHoursBetween(from.Def.Region, t.Def.Region);
                double profit = margin * ask.Value * qty / hours;
                if (qty > 0 && profit > best.profit) best = (g, t.Index, qty, ask.Value, profit, hours);
            }
        }
        return best;
    }

    private bool CanTravel(Town from, Town to) => !TravelBlocked[from.Region] && !TravelBlocked[to.Region];

    private void TickTravel(Agent a)
    {
        a.TravelHoursLeft--;
        if (a.TravelHoursLeft > 0) return;
        if (_agentRng.Chance(Config.CargoLossChance))
        {
            if (a.Cargo.Count > 0) LoseCargo(a);
            else if (!Config.CourierJobs) a.Inventory.Clear();
        }
        a.Town = a.TravelTo;
        a.TravelTo = -1;
        var m = Towns[a.Town].Markets;
        for (int g = 0; g < Goods.Count; g++)
            a.Belief[g] = m[g].LastTradeHour >= Hour - 72 ? m[g].LastPrice : m[g].Reference.Value;
    }

    // ---------------------------------------------------------------- speculators

    /// <summary>
    /// Buys up a target good, holds it, then sells it off. Profit is measured
    /// at the end to check that cornering a market does not pay.
    /// </summary>
    private void SpeculatorTurn(Agent a)
    {
        if (a.TargetGood < 0) return;
        int g = a.TargetGood;
        var m = Towns[a.Town].Markets[g];
        double reference = m.Reference.Value;
        int hoursInPhase = Hour - a.SpecPhaseHour;

        if (a.SpecPhase == 1)
        {
            // Buying: sweep the book up to 1.6x reference for a few days.
            long limit = (long)Math.Round(reference * 1.6);
            var ask = m.Book.BestAsk;
            if (ask.HasValue && ask.Value <= limit) PlaceBuy(a, g, 500, limit, rest: false);
            if (hoursInPhase >= 24 * 4) { a.SpecPhase = 2; a.SpecPhaseHour = Hour; }
        }
        else if (a.SpecPhase == 2)
        {
            // Holding out for a high price, cutting the ask a little each day.
            double days = hoursInPhase / 24.0;
            long ask = Math.Max(1, (long)Math.Round(reference * Math.Max(0.6, 2.0 - 0.1 * days)));
            PlaceSell(a, g, a.Inventory[g], ask);
        }
    }

    /// <summary>Pays this hour's town contracts to the fighters who fought.</summary>
    private void PayContracts()
    {
        if (_fightersThisHour.Count > 0 && _contractPoolThisHour > 0)
        {
            // Stronger (fed, equipped, buffed) fighters complete more of the contract.
            double total = 0;
            foreach (var f in _fightersThisHour) total += Math.Max(0.01, f.LastPower);
            foreach (var f in _fightersThisHour)
            {
                long each = (long)(_contractPoolThisHour * Math.Max(0.01, f.LastPower) / total);
                if (each <= 0) continue;
                if (!Ledger.TryTransfer(Ledger.Treasury, f.Account, each, Flow.TownContract, out _)) break;
                f.TodayIncome += each;
                Track(f, IncomeKind.Bounties, each);
            }
        }
        _fightersThisHour.Clear();
        _onlineFightersThisHour = 0;
    }
}
