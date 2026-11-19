using Economy.Core.Crafting;
using Economy.Core.Ledger;
using Economy.Sim;
using Economy.Sim.World;

namespace Economy.Core.Tests;

/// <summary>
/// Checks on the sim's own rules (parties, everyone fights, plots, hiring,
/// contracts), on small short runs so a future change can't quietly break them.
/// </summary>
public class SimRulesTests
{
    private static readonly string DataDir = DataLoader.FindDataDirectory(AppContext.BaseDirectory);
    private static readonly Catalog Catalog = DataLoader.LoadCatalog(DataDir);
    private static readonly WorldFile WorldFile = WorldFile.Load(DataDir);

    private static World Run(Action<SimConfig>? tweak = null, int days = 12, int agents = 400, ulong seed = 7)
    {
        var config = new SimConfig { Seed = seed, Days = days, AgentCount = agents };
        tweak?.Invoke(config);
        var w = new World(config, Catalog, WorldFile, ScenarioCatalog.Get("baseline"));
        w.Run();
        return w;
    }

    [Fact]
    public void SameSeedGivesTheSameWorld()
    {
        var a = Run();
        var b = Run();
        Assert.Equal(a.Ledger.TreasuryBalance, b.Ledger.TreasuryBalance);
        Assert.Equal(a.PartiesFormed, b.PartiesFormed);
        Assert.Equal(a.ActiveMoney(), b.ActiveMoney());
    }

    [Fact]
    public void TreasuryNeverGoesNegativeAndContractsPay()
    {
        var w = Run();
        Assert.True(w.Ledger.TreasuryBalance >= 0);
        Assert.True(w.Ledger.TotalFor(Flow.TownContract) > 0);
    }

    [Fact]
    public void PartiesFormAndMostHaveATankAndHealer()
    {
        var w = Run();
        Assert.True(w.PartiesFormed > 100, $"only {w.PartiesFormed} parties");
        Assert.True(w.PartiesFull > w.PartiesFormed / 3, $"{w.PartiesFull} of {w.PartiesFormed} parties full");
        Assert.True(w.PartyUnits > 0);
    }

    [Fact]
    public void NoPartiesWhenTurnedOff()
    {
        var w = Run(c => c.Parties = false);
        Assert.Equal(0, w.PartiesFormed);
    }

    [Fact]
    public void PartiesHireAnNpcTankWhenNoPlayerTanks()
    {
        var w = Run(c => { c.TankShare = 0; c.HealerShare = 0.25; });
        Assert.True(w.NpcTanksHired > 0);
        Assert.True(w.Ledger.TotalFor(Flow.NpcHireFee) > 0);
    }

    [Fact]
    public void NoHiringWhenTurnedOff()
    {
        var w = Run(c => { c.TankShare = 0; c.HireNpcs = false; });
        Assert.Equal(0, w.NpcTanksHired);
        Assert.Equal(0, w.Ledger.TotalFor(Flow.NpcHireFee));
    }

    /// <summary>Share of active crafters whose combat level has moved (fighting adds fractions to a whole-number start).</summary>
    private static double CraftersWhoFought(World w)
    {
        var crafters = w.Agents.Where(a => a.Active && Roles.IsCrafter(a.Role)).ToList();
        Assert.NotEmpty(crafters);
        return (double)crafters.Count(a => a.CombatSkill != Math.Floor(a.CombatSkill)) / crafters.Count;
    }

    [Fact]
    public void CraftersFightOnTheSide()
    {
        Assert.True(CraftersWhoFought(Run()) > 0.5);
    }

    [Fact]
    public void OnlyMainFightersFightWhenEveryoneFightsIsOff()
    {
        // A few crafters were fighters who switched trade, so allow a small share.
        Assert.True(CraftersWhoFought(Run(c => c.EveryoneFights = false)) < 0.1);
    }

    [Fact]
    public void FarmersStayNearThePlotLimit()
    {
        var w = Run(days: 40);
        int active = w.Agents.Count(a => a.Active && a.Role is not (Role.Speculator or Role.Bot));
        int farmers = w.Agents.Count(a => a.Active && a.Role == Role.Farmer);
        // The limit stops new farmers; allow a little slack for the starting mix.
        Assert.True(farmers <= active * (w.Config.FarmPlotShare + 0.03), $"{farmers} farmers of {active}");
    }

    [Fact]
    public void GearTiersNeedMoreAndDifferentMaterials()
    {
        var tiers = new SimConfig().GearTiers;
        for (int i = 1; i < tiers.Length; i++)
        {
            Assert.True(tiers[i].MinSkill > tiers[i - 1].MinSkill);
            Assert.True(tiers[i].InputMultiplier > tiers[i - 1].InputMultiplier);
        }
        Assert.Contains(tiers, t => t.Extra.Count > 0);
    }

    [Fact]
    public void FairPayIsRecordedForEveryFocus()
    {
        var w = Run(c => c.WarmupDays = 2, days: 16);
        var pay = w.AverageRoleIncome();
        Assert.Contains(Role.Fighter, pay.Keys);
        Assert.All(pay.Values, v => Assert.True(v > 0));
    }
}
