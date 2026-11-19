using Economy.Core.Items;
using Economy.Core.Ledger;
using Economy.Core.Markets;
using Economy.Core.Stabilizer;

namespace Economy.Sim.World;

/// <summary>Stackable goods an agent holds, with the average quality of each stack.</summary>
public sealed class Inventory
{
    private readonly int[] _qty;
    private readonly double[] _quality;

    public Inventory(int goods)
    {
        _qty = new int[goods];
        _quality = new double[goods];
    }

    public int this[int good] => _qty[good];
    public int QualityOf(int good) => _qty[good] > 0 ? Math.Max(1, (int)Math.Round(_quality[good])) : 0;

    public void Add(int good, int qty, double quality)
    {
        if (qty <= 0) return;
        int before = _qty[good];
        _quality[good] = (before * _quality[good] + qty * quality) / (before + qty);
        _qty[good] = before + qty;
    }

    /// <summary>Takes up to <paramref name="qty"/> units; returns how many were taken.</summary>
    public int Take(int good, int qty)
    {
        int n = Math.Min(qty, _qty[good]);
        _qty[good] -= n;
        return n;
    }

    public int TotalUnits() => _qty.Sum();

    public void Clear()
    {
        Array.Clear(_qty);
        Array.Clear(_quality);
    }
}

/// <summary>A resting order and what it holds back (coins for a buy, goods for a sell).</summary>
public sealed class OpenOrder
{
    public OpenOrder(Order order, int town, int good)
    {
        Order = order;
        Town = town;
        Good = good;
    }

    public Order Order { get; }
    public int Town { get; }
    public int Good { get; }
    public int PlacedHour { get; set; }
}

public sealed class Agent
{
    public Agent(int id, Role role, int town, AccountId account, int goods)
    {
        Id = id;
        Role = role;
        Town = town;
        Account = account;
        Inventory = new Inventory(goods);
        Belief = new double[goods];
        SoldToday = new int[goods];
        SalesPerDay = new double[goods];
    }

    public int Id { get; }
    public Role Role { get; set; }
    public int Town { get; set; }
    public AccountId Account { get; }
    public Inventory Inventory { get; }

    /// <summary>What this agent thinks each good is worth, adjusted by how its orders fill.</summary>
    public double[] Belief { get; }

    /// <summary>Units of each good sold today, and a running average of units sold per day.</summary>
    public int[] SoldToday { get; }
    public double[] SalesPerDay { get; }

    public List<OpenOrder> Orders { get; } = new();

    public bool Active { get; set; } = true;
    public int JoinedHour { get; set; }

    /// <summary>Hour of day the session starts, and how many hours it lasts.</summary>
    public int SessionStart { get; set; }
    public int SessionHours { get; set; }

    public double Skill { get; set; }
    public double RepairSkill { get; set; }
    public bool Careful { get; set; }
    public bool Member { get; set; }
    public int RenewalDay { get; set; }
    public bool UsedFreeToken { get; set; }
    public int Tokens { get; set; }

    public Equipment? Tool { get; set; }
    public Equipment? Weapon { get; set; }
    public Equipment? Armor { get; set; }

    public long RepairPrice { get; set; }
    public int RepairsToday { get; set; }
    public bool RenewalPending { get; set; }
    public int RenewalPendingSince { get; set; }

    public double FoodHours { get; set; }
    public double PotionCredit { get; set; }
    public double BuffCredit { get; set; }
    public double PackCredit { get; set; }
    public int TaskUnitsToday { get; set; }
    /// <summary>Crafter starter quest done, and how many first items its NPC will still buy.</summary>
    public bool CraftQuestDone { get; set; }
    /// <summary>Gear this player has on order, and gear it stopped ordering (no crafter took it).</summary>
    public HashSet<int> WorkOrderGoods { get; } = new();
    public HashSet<int> WorkOrderGaveUp { get; } = new();
    /// <summary>Raw materials held back for novice crafting.</summary>
    public Dictionary<int, int> NoviceKeep { get; } = new();
    public int CommissionLeft { get; set; }
    /// <summary>Owns one of the town's limited shops (only shop owners sell while offline).</summary>
    public bool HasShop { get; set; }
    /// <summary>Combat level progress, 0-100. Separate from the profession skill.</summary>
    public double CombatSkill { get; set; }
    /// <summary>Party role: tank, healer or damage.</summary>
    public CombatRole CombatRole { get; set; } = CombatRole.Damage;
    public double BuffHours { get; set; }
    /// <summary>Combat power this hour, used to weight contract pay.</summary>
    public double LastPower { get; set; }

    /// <summary>Goods this player has waiting with a courier (one job per good at a time).</summary>
    public HashSet<int> CourierPending { get; } = new();

    /// <summary>Courier jobs a hauler is carrying.</summary>
    public List<CourierJob> Cargo { get; } = new();

    /// <summary>Goods kept in other towns' storage (key town*10000+good): units and quality.</summary>
    public Dictionary<int, (int qty, int quality)> Remote { get; } = new();

    public void AddRemote(int town, int good, int qty, int quality)
    {
        int key = town * 10000 + good;
        var cur = Remote.GetValueOrDefault(key);
        int q = cur.qty + qty > 0 ? (cur.qty * cur.quality + qty * quality) / (cur.qty + qty) : quality;
        Remote[key] = (cur.qty + qty, q);
    }

    /// <summary>Hauler state: hours left on the road, and where to.</summary>
    public int TravelHoursLeft { get; set; }
    public int TravelTo { get; set; } = -1;

    /// <summary>
    /// Daily business income, last 7 days: sales, contract pay and repair
    /// fees received, minus what was spent on inputs for the agent's trade.
    /// Food and the agent's own gear are consumption and do not count.
    /// </summary>
    public long[] DailyIncome { get; } = new long[7];
    public long TodayIncome { get; set; }
    public long LastBalance { get; set; }
    public double OnlineHoursWeek { get; set; }

    // Speculator / bot settings
    public int TargetGood { get; set; } = -1;
    public int SpecPhase { get; set; }
    public long SpecStartWealth { get; set; }
    public int SpecPhaseHour { get; set; }

    public bool IsMaster(int masterSkill) => Skill >= masterSkill && Roles.IsCrafter(Role);

    public bool OnlineAt(int hourOfDay)
    {
        int d = (hourOfDay - SessionStart + 24) % 24;
        return d < SessionHours;
    }
}

/// <summary>Per good, per town market state the stabilizer and metrics read.</summary>
public sealed class GoodMarket
{
    public GoodMarket(double basePrice, double refHalfLife, StabilizerSettings settings)
    {
        Book = new OrderBook();
        Reference = new ReferencePrice(basePrice, refHalfLife);
        LastPrice = basePrice;
        DailyVolumeEma = 0;
        Stabilizer = new StabilizerController(settings);
    }

    public OrderBook Book { get; }
    public ReferencePrice Reference { get; }
    public StabilizerController Stabilizer { get; }
    public double LastPrice { get; set; }
    public int LastTradeHour { get; set; } = -1;

    // This hour
    public long HourPlayerValue;
    public int HourPlayerUnits;
    public long HourNpcValue;
    public int HourNpcUnits;

    // Today
    public long DayPlayerValue;
    public int DayPlayerUnits;
    public long DayNpcValue;
    public int DayNpcUnits;
    public double DayShortageSum;
    public int DayShortageHours;

    public double DailyVolumeEma;

    /// <summary>Player-only daily volume; NPC volumes are capped against this so NPCs cannot feed on themselves.</summary>
    public double PlayerVolumeEma;

    /// <summary>Fast (one-day half-life) average of player trade prices, used to calibrate the reference after warm-up.</summary>
    public double FastPrice;
    public readonly double[] ShortageWindow = new double[24];
    public int ShortageCursor;
    public bool HadDemandToday;

    /// <summary>NPC-held stock of this good (bought by the buyer guild), sold first by caravans.</summary>
    public int NpcStock;

    /// <summary>NPC vendor units left to sell today (raw goods only).</summary>
    public int VendorLeftToday;

    public double ShortageRate
    {
        get
        {
            double s = 0;
            foreach (var v in ShortageWindow) s += v;
            return s / ShortageWindow.Length;
        }
    }

    public void ResetHour()
    {
        HourPlayerValue = 0;
        HourPlayerUnits = 0;
        HourNpcValue = 0;
        HourNpcUnits = 0;
    }

    public void ResetDay()
    {
        DayPlayerValue = 0;
        DayPlayerUnits = 0;
        DayNpcValue = 0;
        DayNpcUnits = 0;
        DayShortageSum = 0;
        DayShortageHours = 0;
        HadDemandToday = false;
    }
}

public sealed class Town
{
    public Town(int index, TownDef def, int region, int goods)
    {
        Index = index;
        Def = def;
        Region = region;
        Richness = new double[goods];
        Stock = new double[goods];
        RegenPerHour = new double[goods];
        DeepStock = new double[goods];
        DeepRegenPerHour = new double[goods];
        DepletedUntilHour = new int[goods];
        Markets = new GoodMarket[goods];
    }

    public int Index { get; }
    public TownDef Def { get; }
    public int Region { get; }
    public string Name => Def.Name;
    public double[] Richness { get; }
    public double[] Stock { get; }
    public double[] RegenPerHour { get; }
    /// <summary>Rich materials in the dangerous wilds around the town, reachable only by parties.</summary>
    public double[] DeepStock { get; }
    public double[] DeepRegenPerHour { get; }
    /// <summary>Coins left in today's delivery-task budget, and what it started at.</summary>
    /// <summary>Raw goods the town holds, and how much of each it uses up per day.</summary>
    public double[] Stockpile { get; private set; } = Array.Empty<double>();
    public double[] DailyUse { get; private set; } = Array.Empty<double>();
    public void InitStockpile(int goods) { Stockpile = new double[goods]; DailyUse = new double[goods]; }
    public double TaskBudget { get; set; }
    public double TaskBudgetStart { get; set; }
    public int[] DepletedUntilHour { get; }
    public GoodMarket[] Markets { get; }
    public List<int> Repairers { get; } = new();
}

public enum CombatRole { Tank, Healer, Damage }
