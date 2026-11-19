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
        foreach (var list in _hirePool.Values) list.Clear();
    }

    private readonly List<Agent> _party = new(5);

    public long NpcTanksHired { get; private set; }
    public long NpcHealersHired { get; private set; }
    public long NpcPortersHired { get; private set; }
    public long PlayersHired { get; private set; }
    public long HaulFeesPaid { get; private set; }

    /// <summary>Players fighting this hour who are not on a gathering trip but would join one as a paid tank or healer.</summary>
    private readonly Dictionary<int, List<Agent>> _hirePool = new();

    private void JoinHirePool(Agent a)
    {
        if (!_hirePool.TryGetValue(a.Town, out var list)) _hirePool[a.Town] = list = new List<Agent>();
        list.Add(a);
    }

    /// <summary>Hire board: a player tank or healer joins for a fee split across the party.</summary>
    private bool HirePlayer(Town town, List<Agent> party, CombatRole role)
    {
        if (!_hirePool.TryGetValue(town.Index, out var list)) return false;
        int i = list.FindIndex(x => x.CombatRole == role);
        if (i < 0) return false;
        long share = (Config.PlayerHireFee + party.Count - 1) / party.Count;
        foreach (var a in party) if (Coins(a) < share * 5) return false;
        var hired = list[i];
        list.RemoveAt(i);
        foreach (var a in party)
        {
            Ledger.Transfer(a.Account, hired.Account, share, Flow.PlayerTrade);
            hired.TodayIncome += share;
            Track(hired, IncomeKind.HireFees, share);
        }
        PlayersHired++;
        return true;
    }

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
            // Hire board first: players are cheaper and stronger than NPCs.
            if (!tank && HirePlayer(town, _party, CombatRole.Tank)) tank = true;
            if (!healer && HirePlayer(town, _party, CombatRole.Healer)) healer = true;
            bool playerTank = tank && !_party.Any(a => a.CombatRole == CombatRole.Tank);
            bool playerHealer = healer && !_party.Any(a => a.CombatRole == CombatRole.Healer);
            if (Config.HireNpcs)
            {
                hireTank = !tank && Hire(_party, Config.NpcTankFee);
                hireHealer = !healer && Hire(_party, Config.NpcHealerFee);
                hirePorter = !hauler && Hire(_party, Config.NpcPorterFee);
                if (hireTank) NpcTanksHired++;
                if (hireHealer) NpcHealersHired++;
                if (hirePorter) NpcPortersHired++;
            }
            RunTrip(town, _party, hireTank || playerTank, hireHealer || playerHealer, hirePorter, npcTank: hireTank, npcHealer: hireHealer);
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

    private void RunTrip(Town town, List<Agent> party, bool extraTank, bool extraHealer, bool npcPorter, bool npcTank, bool npcHealer)
    {
        PartiesFormed++;
        bool tank = extraTank || party.Any(a => a.CombatRole == CombatRole.Tank);
        bool healer = extraHealer || party.Any(a => a.CombatRole == CombatRole.Healer);
        bool full = tank && healer && party.Count + (extraTank ? 1 : 0) + (extraHealer ? 1 : 0) == 5;
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

        double stock = 0, stockWeight = 0;
        for (int g = 0; g < Goods.Count; g++) { stock += town.DeepStock[g]; stockWeight += town.DeepStock[g] * WeightOf(g); }
        if (stock <= 0) return;
        // Carrying limits are by weight: heavy ore fills a bag far sooner than herbs.
        double perUnit = Math.Max(0.05, stockWeight / stock);

        // Personal loot: each member keeps what they gather, as far as they can
        // carry it. Haulers (and a hired porter) carry the overflow for a fee.
        int n = party.Count;
        var gathered = new double[n];
        var own = new double[n];
        double haulCap = npcPorter ? Config.PartyCarryPerHauler / perUnit : 0, overflow = 0;
        for (int i = 0; i < n; i++)
        {
            var a = party[i];
            gathered[i] = (IsGatherFocus(a.Role) ? 1.0 : 0.4) * Config.PartyGatherPerHour;
            double carry = Config.PartyCarryPerMember / perUnit;
            if (a.Role == Role.Hauler) haulCap += Config.PartyCarryPerHauler / perUnit;
            // Travel packs (leatherworkers) add carrying space and wear out.
            if (a.Inventory[Pack] > 0)
            {
                carry += Config.PackCarry / perUnit;
                a.PackCredit += Config.PackUsePerTrip;
                if (a.PackCredit >= 1) { a.PackCredit -= 1; a.Inventory.Take(Pack, 1); }
            }
            own[i] = Math.Min(gathered[i], carry);
            overflow += gathered[i] - own[i];
        }
        double hauled = Math.Min(overflow, haulCap);
        double haulRatio = overflow > 0 ? hauled / overflow : 0;
        var carried = new double[n];
        double total = 0;
        for (int i = 0; i < n; i++)
        {
            carried[i] = (gathered[i] - own[i]) * haulRatio;
            total += own[i] + carried[i];
        }
        if (!Config.PersonalLoot)
        {
            // Old rule: everything split evenly.
            for (int i = 0; i < n; i++) { own[i] = total / n; carried[i] = 0; }
        }

        var fresh = new Dictionary<int, double[]>(n);
        foreach (var a in party) fresh[a.Id] = new double[Goods.Count];
        var haulers = party.Where(a => a.Role == Role.Hauler).ToList();
        double saved = _gatherToolBonus;
        _gatherToolBonus = Config.PartyQualityBonus;
        var fees = new double[n];
        for (int g = 0; g < Goods.Count; g++)
        {
            if (town.DeepStock[g] <= 0) continue;
            double take = Math.Min(town.DeepStock[g], total * town.DeepStock[g] / stock);
            town.DeepStock[g] -= take;
            PartyUnits += (long)take;
            double value = town.Markets[g].Reference.Value;
            for (int i = 0; i < n; i++)
            {
                double share = total > 0 ? (own[i] + carried[i]) / total : 0;
                double units = take * share;
                AddProduce(party[i], g, units);
                fresh[party[i].Id][g] += units + 1;
                if (total > 0) fees[i] += take * carried[i] / total * value * Config.HaulFeeShare;
            }
        }
        _gatherToolBonus = saved;

        // Pay the haulers for what they carried (a hired NPC porter was paid up front).
        if (haulers.Count > 0)
        {
            double playerHaul = haulCap - (npcPorter ? Config.PartyCarryPerHauler / perUnit : 0);
            double playerShare = haulCap > 0 ? playerHaul / haulCap : 0;
            for (int i = 0; i < n; i++)
            {
                long fee = (long)Math.Round(fees[i] * playerShare);
                if (fee <= 0 || party[i].Role == Role.Hauler) continue;
                fee = Math.Min(fee, Coins(party[i]));
                long each = fee / haulers.Count;
                if (each <= 0) continue;
                foreach (var h in haulers)
                {
                    Ledger.Transfer(party[i].Account, h.Account, each, Flow.PlayerTrade);
                    h.TodayIncome += each;
                    party[i].TodayIncome -= each;
                    Track(h, IncomeKind.HaulFeesIn, each);
                    Track(party[i], IncomeKind.HaulFeesOut, -each);
                    HaulFeesPaid += each;
                }
            }
        }

        PartyDeliver(town, party, fresh);
    }
}
