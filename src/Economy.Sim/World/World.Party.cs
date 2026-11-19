using Economy.Core.Ledger;
using Economy.Core.Items;

namespace Economy.Sim.World;

/// <summary>
/// Party trips into the dangerous wilds. Players who fight this hour may
/// group up in their town: ideally one tank, one healer and three damage,
/// with gatherers to gather and haulers to carry. A full party rarely
/// wipes; a party missing a tank or healer wipes more often and drops its
/// cargo. What comes out is split evenly and is better quality than solo
/// gathering near town.
/// </summary>
public sealed partial class World
{
    private readonly Dictionary<int, List<Agent>> _partyPool = new();

    public long PartiesFormed { get; private set; }
    public long PartiesFull { get; private set; }
    public long PartyWipes { get; private set; }
    public long PartyUnits { get; private set; }

    private static bool PartyEligible(Role r) =>
        r is Role.Miner or Role.Logger or Role.Herbalist or Role.Hunter or Role.Hauler or Role.Fighter or Role.Casual;

    private static bool IsGatherFocus(Role r) =>
        r is Role.Miner or Role.Logger or Role.Herbalist or Role.Hunter or Role.Casual;

    private void JoinPartyPool(Agent a)
    {
        if (!_partyPool.TryGetValue(a.Town, out var list)) _partyPool[a.Town] = list = new List<Agent>();
        list.Add(a);
    }

    private void ResolveParties()
    {
        foreach (var (town, pool) in _partyPool)
        {
            if (pool.Count >= 3) FormParties(Towns[town], pool);
            pool.Clear();
        }
    }

    private readonly List<Agent> _party = new(5);

    public long NpcTanksHired { get; private set; }
    public long NpcHealersHired { get; private set; }
    public long NpcPortersHired { get; private set; }

    private void FormParties(Town town, List<Agent> pool)
    {
        Shuffle(pool, _agentRng);
        var tanks = new Queue<Agent>(pool.Where(a => a.CombatRole == CombatRole.Tank));
        var healers = new Queue<Agent>(pool.Where(a => a.CombatRole == CombatRole.Healer));
        // Damage dealers, haulers first so each party gets its carriers.
        var rest = new Queue<Agent>(pool.Where(a => a.CombatRole == CombatRole.Damage)
            .OrderBy(a => a.Role == Role.Hauler ? 0 : IsGatherFocus(a.Role) ? 1 : 2));

        while (tanks.Count + healers.Count + rest.Count >= 3)
        {
            _party.Clear();
            bool hasTank = tanks.Count > 0, hasHealer = healers.Count > 0;
            if (hasTank) _party.Add(tanks.Dequeue());
            if (hasHealer) _party.Add(healers.Dequeue());
            // Leave a slot for each NPC the party will try to hire.
            int slots = 5 - (Config.HireNpcs && !hasTank ? 1 : 0) - (Config.HireNpcs && !hasHealer ? 1 : 0);
            while (_party.Count < slots && rest.Count > 0) _party.Add(rest.Dequeue());
            while (_party.Count < slots && tanks.Count > 0) _party.Add(tanks.Dequeue());
            while (_party.Count < slots && healers.Count > 0) _party.Add(healers.Dequeue());
            if (_party.Count < 3) break;

            bool tank = _party.Any(a => a.CombatRole == CombatRole.Tank);
            bool healer = _party.Any(a => a.CombatRole == CombatRole.Healer);
            bool hauler = _party.Any(a => a.Role == Role.Hauler);
            bool hireTank = false, hireHealer = false, hirePorter = false;
            if (Config.HireNpcs)
            {
                hireTank = !tank && Hire(_party, Config.NpcTankFee);
                hireHealer = !healer && Hire(_party, Config.NpcHealerFee);
                hirePorter = !hauler && Hire(_party, Config.NpcPorterFee);
                if (hireTank) NpcTanksHired++;
                if (hireHealer) NpcHealersHired++;
                if (hirePorter) NpcPortersHired++;
            }
            RunTrip(town, _party, hireTank, hireHealer, hirePorter);
        }
    }

    /// <summary>Splits an NPC's fee across the party; hires only if everyone can easily afford their share.</summary>
    private bool Hire(List<Agent> party, long fee)
    {
        long share = (fee + party.Count - 1) / party.Count;
        foreach (var a in party) if (Coins(a) < share * 5) return false;
        foreach (var a in party) Ledger.Transfer(a.Account, Ledger.Treasury, share, Flow.NpcHireFee);
        return true;
    }

    private void RunTrip(Town town, List<Agent> party, bool npcTank, bool npcHealer, bool npcPorter)
    {
        PartiesFormed++;
        bool tank = npcTank || party.Any(a => a.CombatRole == CombatRole.Tank);
        bool healer = npcHealer || party.Any(a => a.CombatRole == CombatRole.Healer);
        bool full = tank && healer && party.Count + (npcTank ? 1 : 0) + (npcHealer ? 1 : 0) == 5;
        if (full) PartiesFull++;

        double wipe = tank && healer ? Config.PartyWipeChanceFull : Config.PartyWipeChanceMissingRole;
        if (npcTank) wipe *= Config.NpcWipeFactor;
        if (npcHealer) wipe *= Config.NpcWipeFactor;
        if (_agentRng.Chance(wipe))
        {
            // Cargo drops on death; gear takes a beating.
            PartyWipes++;
            foreach (var a in party)
            {
                if (a.Weapon != null) Durability.Wear(a.Weapon, Config.WeaponWearPerHour * 3);
                if (a.Armor != null) Durability.Wear(a.Armor, Config.ArmorWearPerHour * 3);
            }
            return;
        }

        double gather = 0, carry = npcPorter ? Config.PartyCarryPerHauler : 0;
        foreach (var a in party)
        {
            gather += (IsGatherFocus(a.Role) ? 1.0 : 0.4) * Config.PartyGatherPerHour;
            carry += a.Role == Role.Hauler ? Config.PartyCarryPerHauler : Config.PartyCarryPerMember;
            // Travel packs (leatherworkers) add carrying space and wear out.
            if (a.Inventory[Pack] > 0)
            {
                carry += Config.PackCarry;
                a.PackCredit += Config.PackUsePerTrip;
                if (a.PackCredit >= 1) { a.PackCredit -= 1; a.Inventory.Take(Pack, 1); }
            }
        }
        double total = Math.Min(gather, carry);

        double stock = 0;
        for (int g = 0; g < Goods.Count; g++) stock += town.DeepStock[g];
        if (stock <= 0) return;

        double saved = _gatherToolBonus;
        _gatherToolBonus = Config.PartyQualityBonus;
        for (int g = 0; g < Goods.Count; g++)
        {
            if (town.DeepStock[g] <= 0) continue;
            double take = Math.Min(town.DeepStock[g], total * town.DeepStock[g] / stock);
            town.DeepStock[g] -= take;
            PartyUnits += (long)take;
            double each = take / party.Count;
            foreach (var a in party) AddProduce(a, g, each);
        }
        _gatherToolBonus = saved;
    }
}
