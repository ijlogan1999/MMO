using Economy.Core.Ledger;
using Economy.Sim.World;

namespace Economy.Sim;

/// <summary>A scripted shock applied to a run.</summary>
public interface IScenario
{
    int Number { get; }
    string Id { get; }
    string Name { get; }
    string Shock { get; }

    /// <summary>Day the shock starts and the day it stops being applied.</summary>
    int ShockStartDay { get; }
    int ShockEndDay { get; }

    /// <summary>Goods (by id) whose prices the recovery check watches. Empty means all traded goods.</summary>
    IReadOnlyList<string> WatchedGoods { get; }

    /// <summary>True if the run is scored on speculators losing money.</summary>
    bool ScoresSpeculators { get; }

    void Configure(SimConfig config);
    void BeforeSetup(World.World w);
    void AfterSetup(World.World w);
    void OnHour(World.World w);
    bool ForceQuit(World.World w, Agent a);
}

public abstract class ScenarioBase : IScenario
{
    public abstract int Number { get; }
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Shock { get; }
    public virtual int ShockStartDay => 30;
    public virtual int ShockEndDay => 30;
    public virtual IReadOnlyList<string> WatchedGoods => Array.Empty<string>();
    public virtual bool ScoresSpeculators => false;
    public virtual void Configure(SimConfig config) { }
    public virtual void BeforeSetup(World.World w) { }
    public virtual void AfterSetup(World.World w) { }
    public virtual void OnHour(World.World w) { }
    public virtual bool ForceQuit(World.World w, Agent a) => false;

    protected static bool AtShockStart(World.World w, int day) => w.Day == day && w.HourOfDay == 0;
}

public static class ScenarioCatalog
{
    public static IReadOnlyList<IScenario> All { get; } = new IScenario[]
    {
        new Baseline(),
        new MinersQuit(),
        new KeyCrafterQuits(),
        new LaunchFlood(),
        new YieldPatch(),
        new HoardingCorner(),
        new GoldFlood(),
        new GoldDrought(),
        new BotFarm(),
        new NightTrough(),
        new RegionCutOff(),
        new SlowObsolescence(),
        new ExploitHunt(),
        new TreasurySqueeze(),
        new CoreSwing(),
        new HealerShortage(),
        new HaulersQuit(),
        new WildsPatch(),
        new EveryoneSolo(),
    };

    public static IScenario Get(string id) =>
        All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || s.Number.ToString() == id)
        ?? throw new ArgumentException($"Unknown scenario '{id}'. Known: {string.Join(", ", All.Select(s => s.Id))}.");
}

public sealed class Baseline : ScenarioBase
{
    public override int Number => 1;
    public override string Id => "baseline";
    public override string Name => "Calm baseline";
    public override string Shock => "None";
}

public sealed class MinersQuit : ScenarioBase
{
    public override int Number => 2;
    public override string Id => "miners-quit";
    public override string Name => "Miners quit";
    public override string Shock => "Half of all miners leave on day 31";
    public override IReadOnlyList<string> WatchedGoods => new[] { "iron_ore", "coal", "iron_ingot", "pickaxe" };
    public override bool ForceQuit(World.World w, Agent a) =>
        w.Day == ShockStartDay && a.Role == Role.Miner && (a.Id % 2 == 0);
}

public sealed class KeyCrafterQuits : ScenarioBase
{
    public override int Number => 3;
    public override string Id => "smiths-quit";
    public override string Name => "Key crafters quit";
    public override string Shock => "Every smith in the North region quits on day 31";
    public override IReadOnlyList<string> WatchedGoods => new[] { "pickaxe", "woodaxe", "sword", "iron_ingot" };
    public override bool ForceQuit(World.World w, Agent a) =>
        w.Day == ShockStartDay && a.Role == Role.Smith && w.Regions[w.Towns[a.Town].Region] == "North";
}

public sealed class LaunchFlood : ScenarioBase
{
    public override int Number => 4;
    public override string Id => "launch-flood";
    public override string Name => "Launch flood";
    public override string Shock => "Three times the population joins over 7 days";
    public override int ShockEndDay => 37;
    public override IReadOnlyList<string> WatchedGoods => new[] { "pickaxe", "bread", "sickle", "woodaxe" };
    public override void OnHour(World.World w)
    {
        if (w.HourOfDay == 0 && w.Day >= ShockStartDay && w.Day < ShockEndDay)
            w.ExtraJoinersToday += w.Config.AgentCount * 3 / 7;
    }
}

public sealed class YieldPatch : ScenarioBase
{
    public override int Number => 5;
    public override string Id => "yield-patch";
    public override string Name => "Yield patch";
    public override string Shock => "A patch doubles grain yield from day 31";
    public override int ShockEndDay => 180;
    public override IReadOnlyList<string> WatchedGoods => new[] { "grain", "flour", "bread" };
    public override void OnHour(World.World w)
    {
        if (AtShockStart(w, ShockStartDay)) w.Config.GrainYieldMultiplier = 2.0;
    }
}

/// <summary>Speculators with deep pockets try to corner a good in one town.</summary>
public abstract class SpeculatorScenario : ScenarioBase
{
    protected abstract string TargetGood { get; }
    protected abstract string TargetTown { get; }
    protected virtual int Speculators => 20;
    protected virtual long Purse => 250_000;
    public override bool ScoresSpeculators => true;
    public override int ShockEndDay => 60;
    public override IReadOnlyList<string> WatchedGoods => new[] { TargetGood };

    public override void AfterSetup(World.World w)
    {
        int town = w.Towns.First(t => t.Def.Id == TargetTown).Index;
        for (int i = 0; i < Speculators; i++)
        {
            var a = w.CreateAgent(Role.Speculator, town, Purse, w.ScenarioRng, veteran: true);
            a.TargetGood = w.Goods[TargetGood];
            a.SpecPhase = 0;
            a.SessionStart = w.ScenarioRng.NextInt(0, 24);
            a.SpecStartWealth = w.Coins(a);
        }
    }

    public override void OnHour(World.World w)
    {
        if (!AtShockStart(w, ShockStartDay)) return;
        foreach (var a in w.Agents)
            if (a.Role == Role.Speculator) { a.SpecPhase = 1; a.SpecPhaseHour = w.Hour; }
    }
}

public sealed class HoardingCorner : SpeculatorScenario
{
    public override int Number => 6;
    public override string Id => "hoarding";
    public override string Name => "Hoarding corner";
    public override string Shock => "20 speculators with 25,000 coins each buy up Ironhold's iron ore";
    protected override string TargetGood => "iron_ore";
    protected override string TargetTown => "ironhold";
}

public sealed class GoldFlood : ScenarioBase
{
    public override int Number => 7;
    public override string Id => "gold-flood";
    public override string Name => "Gold flood";
    public override string Shock => "A dupe bug gives 5% of players ten times the target money each";
    public override void OnHour(World.World w)
    {
        if (!AtShockStart(w, ShockStartDay)) return;
        long amount = w.Config.TargetMoneyPerPlayer * 10;
        foreach (var a in w.Agents)
            if (a.Active && w.ScenarioRng.Chance(0.05))
                w.Ledger.Transfer(w.Ledger.Mint, a.Account, amount, Flow.ExploitDupe);
    }
}

public sealed class GoldDrought : ScenarioBase
{
    public override int Number => 8;
    public override string Id => "gold-drought";
    public override string Name => "Gold drought";
    public override string Shock => "Market tax raised to 8% for 30 days";
    public override int ShockEndDay => 60;
    public override void OnHour(World.World w)
    {
        if (AtShockStart(w, ShockStartDay)) w.TaxRate = 0.08;
        if (AtShockStart(w, ShockEndDay)) w.TaxRate = w.Config.MarketTax;
    }
}

public sealed class BotFarm : ScenarioBase
{
    public override int Number => 9;
    public override string Id => "bot-farm";
    public override string Name => "Bot farm";
    public override string Shock => "200 bots mine iron ore in Ironhold 20 hours a day";
    public override int ShockEndDay => 180;
    public override IReadOnlyList<string> WatchedGoods => new[] { "iron_ore" };
    public override void OnHour(World.World w)
    {
        if (!AtShockStart(w, ShockStartDay)) return;
        int town = w.Towns.First(t => t.Def.Id == "ironhold").Index;
        int bots = 200 * w.Config.AgentCount / 2000;
        for (int i = 0; i < bots; i++)
        {
            var a = w.CreateAgent(Role.Bot, town, 0, w.ScenarioRng, veteran: false);
            a.TargetGood = w.Goods["iron_ore"];
        }
    }
}

public sealed class NightTrough : ScenarioBase
{
    public override int Number => 10;
    public override string Id => "night-trough";
    public override string Name => "Night trough";
    public override string Shock => "None; spreads and market makers are compared between 3 to 6 AM and evenings";
}

public sealed class RegionCutOff : ScenarioBase
{
    public override int Number => 11;
    public override string Id => "region-cut-off";
    public override string Name => "Region cut off";
    public override string Shock => "Travel to and from the North is blocked for 14 days";
    public override int ShockEndDay => 44;
    public override void OnHour(World.World w)
    {
        int north = Array.IndexOf(w.Regions, "North");
        if (AtShockStart(w, ShockStartDay)) w.TravelBlocked[north] = true;
        if (AtShockStart(w, ShockEndDay)) w.TravelBlocked[north] = false;
    }
}

public sealed class SlowObsolescence : ScenarioBase
{
    public override int Number => 12;
    public override string Id => "obsolescence";
    public override string Name => "Slow obsolescence";
    public override string Shock => "From day 31 players stop buying bread (a better food replaced it)";
    public override int ShockEndDay => 180;
    public override IReadOnlyList<string> WatchedGoods => new[] { "bread" };
    public override void OnHour(World.World w)
    {
        if (AtShockStart(w, ShockStartDay)) w.Config.BreadObsolete = true;
    }
}

public sealed class ExploitHunt : SpeculatorScenario
{
    public override int Number => 13;
    public override string Id => "exploit-hunt";
    public override string Name => "Exploit hunt";
    public override string Shock => "Speculators spike iron ingot prices in Port Town to draw caravans, then try to sell high";
    protected override string TargetGood => "iron_ingot";
    protected override string TargetTown => "porttown";
    protected override int Speculators => 10;
    protected override long Purse => 150_000;
}

public sealed class TreasurySqueeze : ScenarioBase
{
    public override int Number => 14;
    public override string Id => "treasury-squeeze";
    public override string Name => "Treasury squeeze";
    public override string Shock => "Treasury starts at 10% of normal";
    public override void Configure(SimConfig config) => config.TreasurySeedPerAgent = config.TreasurySeedPerAgent / 10;
}

public sealed class CoreSwing : ScenarioBase
{
    public override int Number => 16;
    public override string Id => "core-swing";
    public override string Name => "Core swing";
    public override string Shock => "Dungeon runs halve for 15 days, then double for 15 days";
    public override int ShockEndDay => 60;
    public override IReadOnlyList<string> WatchedGoods => new[] { "relic_core", "relic_sword" };
    public override void OnHour(World.World w)
    {
        if (AtShockStart(w, ShockStartDay)) w.Config.DungeonRunMultiplier = 0.5;
        if (AtShockStart(w, 45)) w.Config.DungeonRunMultiplier = 2.0;
        if (AtShockStart(w, ShockEndDay)) w.Config.DungeonRunMultiplier = 1.0;
    }
}

/// <summary>Healers get scarce, so few parties have a full lineup.</summary>
public sealed class HealerShortage : ScenarioBase
{
    public override int Number => 17;
    public override string Id => "healer-shortage";
    public override string Name => "Healer shortage";
    public override string Shock => "Four in five healers switch to damage for 20 days";
    public override int ShockEndDay => 50;
    public override IReadOnlyList<string> WatchedGoods => new[] { "iron_ore", "timber", "herbs", "hide" };
    private readonly HashSet<int> _switched = new();
    public override void OnHour(World.World w)
    {
        if (AtShockStart(w, ShockStartDay))
            foreach (var a in w.Agents)
                if (a.Active && a.CombatRole == CombatRole.Healer && a.Id % 5 != 0) { a.CombatRole = CombatRole.Damage; _switched.Add(a.Id); }
        if (AtShockStart(w, ShockEndDay))
            foreach (int id in _switched) w.Agents[id].CombatRole = CombatRole.Healer;
    }
}

/// <summary>Every hauler leaves, so parties can carry little out of the wilds.</summary>
public sealed class HaulersQuit : ScenarioBase
{
    public override int Number => 18;
    public override string Id => "haulers-quit";
    public override string Name => "Haulers quit";
    public override string Shock => "Every hauler quits on day 31";
    public override IReadOnlyList<string> WatchedGoods => new[] { "iron_ore", "timber", "herbs", "hide" };
    public override bool ForceQuit(World.World w, Agent a) => w.Day == ShockStartDay && a.Role == Role.Hauler;
}

/// <summary>A patch nerfs, then buffs, the dangerous wilds.</summary>
public sealed class WildsPatch : ScenarioBase
{
    public override int Number => 19;
    public override string Id => "wilds-patch";
    public override string Name => "Wilds patch";
    public override string Shock => "Dangerous-zone yields halve for 15 days, then rise 50% for 15 days";
    public override int ShockEndDay => 60;
    public override IReadOnlyList<string> WatchedGoods => new[] { "iron_ore", "timber", "herbs", "hide" };
    private double _base;
    public override void OnHour(World.World w)
    {
        if (AtShockStart(w, ShockStartDay)) { _base = w.Config.DeepRegenShare; w.Config.DeepRegenShare = _base * 0.5; }
        if (AtShockStart(w, 45)) w.Config.DeepRegenShare = _base * 1.5;
        if (AtShockStart(w, ShockEndDay)) w.Config.DeepRegenShare = _base;
    }
}

/// <summary>Players stop grouping for 20 days and everyone gathers alone.</summary>
public sealed class EveryoneSolo : ScenarioBase
{
    public override int Number => 20;
    public override string Id => "everyone-solo";
    public override string Name => "Everyone solo";
    public override string Shock => "No party trips for 20 days";
    public override int ShockEndDay => 50;
    public override IReadOnlyList<string> WatchedGoods => new[] { "iron_ore", "timber", "herbs", "hide" };
    private double _base;
    public override void OnHour(World.World w)
    {
        if (AtShockStart(w, ShockStartDay)) { _base = w.Config.PartyTripChance; w.Config.PartyTripChance = 0; }
        if (AtShockStart(w, ShockEndDay)) w.Config.PartyTripChance = _base;
    }
}
