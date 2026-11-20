namespace Economy.Sim.World;

/// <summary>Where each role's money comes from and goes, and where each good ends up (after warm-up).</summary>
public enum IncomeKind { SalesToPlayers, SalesToNpcs, InputCosts, Repairs, Bounties, Tasks, HireFees, HaulFeesIn, HaulFeesOut }

public enum SpendKind { Food, Healing, Gear, Materials, Token }

public enum GoodFlowKind { Gathered, Crafted, TaskDelivered, PlayerToPlayer, BoughtAsInput, SoldToNpc, BoughtFromNpc, UsedInCrafts }

public sealed partial class World
{
    private static readonly int RoleCount = Enum.GetValues<Role>().Length;
    private static readonly int KindCount = Enum.GetValues<IncomeKind>().Length;

    public long[,] IncomeParts { get; } = new long[RoleCount, KindCount];
    public double[] RoleHours { get; } = new double[RoleCount];
    public double[] RoleAgentDays { get; } = new double[RoleCount];
    private long[,]? _goodFlows;
    public long[,] GoodFlows => _goodFlows ??= new long[Goods.Count, Enum.GetValues<GoodFlowKind>().Length];

    /// <summary>Average reference price of each good across towns, per day.</summary>
    public List<double[]> DailyPrices { get; } = new();

    public bool Tracking => Day >= Config.WarmupDays;

    private void Track(Agent a, IncomeKind kind, long coins)
    {
        if (Tracking) IncomeParts[(int)a.Role, (int)kind] += coins;
    }

    /// <summary>What players spend on for themselves (not business inputs), by kind, per role.</summary>
    public long[,] SpendParts { get; } = new long[Enum.GetValues<Role>().Length, Enum.GetValues<SpendKind>().Length];

    private void TrackSpend(Agent a, SpendKind kind, long coins)
    {
        if (Tracking) SpendParts[(int)a.Role, (int)kind] += coins;
    }

    private SpendKind SpendOf(int good)
    {
        var d = Goods.Defs[good];
        if (good == Bread || good == Stew || d.Use == Economy.Core.Items.GoodUse.Food) return SpendKind.Food;
        if (good == Potion || good == Bandage || d.Use == Economy.Core.Items.GoodUse.Consumable) return SpendKind.Healing;
        if (d.HasDurability) return SpendKind.Gear;
        return SpendKind.Materials;
    }

    private void TrackGood(int good, GoodFlowKind kind, long units)
    {
        if (Tracking && good >= 0 && good < Goods.Count) GoodFlows[good, (int)kind] += units;
    }

    private void TrackDay()
    {
        var p = new double[Goods.Count];
        for (int g = 0; g < Goods.Count; g++) p[g] = Towns.Average(t => t.Markets[g].Reference.Value);
        DailyPrices.Add(p);
        if (!Tracking) return;
        foreach (var a in Agents)
        {
            if (!a.Active) continue;
            RoleHours[(int)a.Role] += a.SessionHours;
            RoleAgentDays[(int)a.Role] += 1;
        }
    }
}
