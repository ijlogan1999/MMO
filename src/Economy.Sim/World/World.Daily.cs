using Economy.Core.Ledger;

namespace Economy.Sim.World;

public sealed partial class World
{
    public int QuitsToday { get; private set; }
    public int JoinsToday { get; private set; }

    /// <summary>Extra joiners a scenario wants today (launch flood).</summary>
    public int ExtraJoinersToday { get; set; }

    private void EndDay()
    {
        int dayIndex = Day % 7;
        bool weekEnd = dayIndex == 6;

        if (weekEnd && Day >= Config.WarmupDays)
        {
            foreach (var (role, ratio) in RoleIncome())
                RoleIncomeWeeks[role] = (RoleIncomeWeeks.GetValueOrDefault(role).sum + ratio, RoleIncomeWeeks.GetValueOrDefault(role).weeks + 1);
        }

        if (weekEnd || Day == 0) AssignShops();
        OpenTaskBudgets();
        TrackDay();
        ExpireCourierJobs();
        PostTownCourierJobs();

        foreach (var a in Agents)
        {
            if (!a.Active) continue;
            // Furniture bought goes into the player's home for good.
            FurniturePlaced += a.Inventory.Take(Furniture, a.Inventory[Furniture]);
            a.DailyIncome[dayIndex] = a.TodayIncome;
            a.TodayIncome = 0;
            a.RepairsToday = 0;
            for (int g = 0; g < a.SoldToday.Length; g++)
            {
                if (a.SoldToday[g] == 0 && a.SalesPerDay[g] == 0) continue;
                a.SalesPerDay[g] += Config.SalesAverageWeight * (a.SoldToday[g] - a.SalesPerDay[g]);
                a.SoldToday[g] = 0;
            }
        }

        if (weekEnd) WeeklyFees();
        SubclassSwitches();
        TokenDay();
        Churn();

        Metrics.RecordDay();

        foreach (var town in Towns)
        {
            for (int g = 0; g < Goods.Count; g++)
            {
                var m = town.Markets[g];
                double alpha = Math.Max(1.0 / (Day + 1), 1 - Math.Pow(0.5, 1.0 / 14));
                m.DailyVolumeEma += alpha * (m.DayPlayerUnits + m.DayNpcUnits - m.DailyVolumeEma);
                m.PlayerVolumeEma += alpha * (m.DayPlayerUnits - m.PlayerVolumeEma);
                // NPC stock beyond a month of player volume is shipped out of the world.
                int maxStock = (int)Math.Max(20, m.PlayerVolumeEma * 30);
                if (m.NpcStock > maxStock) m.NpcStock = maxStock;
                m.ResetDay();
            }
        }
        Tokens.DayValue = 0;
        Tokens.DayUnits = 0;
        RefreshVendors();

        if (weekEnd) foreach (var a in Agents) a.OnlineHoursWeek = 0;
    }

    private void WeeklyFees()
    {
        foreach (var a in Agents)
        {
            if (!a.Active) continue;
            if (a.Role == Role.Farmer)
            {
                long rent = Config.PlotRentPerWeek * Config.PlotsPerFarmer;
                Ledger.TryTransfer(a.Account, Ledger.Treasury, Math.Min(rent, Coins(a)), Flow.PlotRent, out _);
            }
            if (a.HasShop)
                Ledger.TryTransfer(a.Account, Ledger.Treasury, Math.Min(Config.ShopUpkeepPerWeek, Coins(a)), Flow.ShopUpkeep, out _);
        }
    }

    private void SubclassSwitches()
    {
        foreach (var a in Agents)
        {
            if (!a.Active || !Fights(a)) continue;
            if (!_agentRng.Chance(Config.SubclassSwitchChancePerDay)) continue;
            if (Coins(a) < Config.SubclassSwitchFee * 3) continue;
            Ledger.Transfer(a.Account, Ledger.Treasury, Config.SubclassSwitchFee, Flow.SubclassSwitchFee);
        }
    }

    /// <summary>
    /// Token supply comes from players buying tokens with real money and
    /// listing them for coins. Members renew every 30 days with a token if
    /// they can afford one (else they pay cash, which the sim ignores).
    /// </summary>
    private void TokenDay()
    {
        foreach (var a in Agents)
        {
            if (!a.Active || a.Role is Role.Speculator or Role.Bot) continue;

            // Last renewal's buy order expired: pay cash instead.
            if (a.RenewalPending && Day - a.RenewalPendingSince >= 3)
            {
                a.RenewalPending = false;
                CancelTokenOrders(a);
            }

            if (_agentRng.Chance(Config.TokenSellersPerAgentPerDay))
            {
                a.Tokens++;
                Tokens.RealMoneyTokens++;
                long ask = Math.Max(1, (long)Math.Round(a.Belief[TokenIndex] * (0.98 + 0.04 * _agentRng.NextDouble())));
                PlaceSell(a, TokenIndex, 1, ask);
            }

            if (!a.Member || (Day % 30) != a.RenewalDay) continue;
            int lifeDays = (Hour - a.JoinedHour) / 24;
            if (!a.UsedFreeToken && lifeDays >= Config.FreeTokenAtDay)
            {
                a.UsedFreeToken = true;
                Tokens.FreeTokensUsed++;
                continue;
            }
            if (a.Tokens > 0) { a.Tokens--; Tokens.TokensConsumed++; continue; }
            long price = Math.Max(1, (long)Math.Round(a.Belief[TokenIndex]));
            if (Coins(a) < price * 2) continue;
            a.RenewalPending = true;
            a.RenewalPendingSince = Day;
            PlaceBuy(a, TokenIndex, 1, price);
        }

        // Unfilled token orders reprice daily.
        foreach (var a in Agents)
        {
            if (!a.Active) continue;
            bool any = false;
            foreach (var oo in a.Orders) if (oo.Good == TokenIndex && oo.Order.Remaining > 0) { any = true; break; }
            if (!any) continue;
            bool wasBuying = a.RenewalPending;
            CancelTokenOrders(a);
            if (a.Tokens > 0 && !wasBuying) PlaceSell(a, TokenIndex, a.Tokens, Math.Max(1, (long)Math.Round(a.Belief[TokenIndex])));
            else if (wasBuying && Coins(a) >= a.Belief[TokenIndex]) PlaceBuy(a, TokenIndex, 1, Math.Max(1, (long)Math.Round(a.Belief[TokenIndex])));
        }
    }

    private void CancelTokenOrders(Agent a)
    {
        var keep = new List<OpenOrder>();
        var token = new List<OpenOrder>();
        foreach (var oo in a.Orders) (oo.Good == TokenIndex ? token : keep).Add(oo);
        if (token.Count == 0) return;
        a.Orders.Clear();
        a.Orders.AddRange(token);
        CancelAgentOrders(a);
        a.Orders.AddRange(keep);
    }

    /// <summary>
    /// Players quit at a small base rate, more when they are losing money or
    /// hungry. New players replace them, drawn toward the roles that earn most.
    /// </summary>
    private void Churn()
    {
        QuitsToday = 0;
        JoinsToday = 0;
        foreach (var a in Agents)
        {
            if (!a.Active || a.Role is Role.Speculator or Role.Bot) continue;
            if (Hour - a.JoinedHour < 24 * 7) continue;
            long week = a.DailyIncome.Sum();
            bool unhappy = (week < 0 && Coins(a) < 1000) || a.FoodHours <= 0;
            double p = Config.BaseQuitChancePerDay + (unhappy ? Config.UnhappyQuitChancePerDay : 0);
            if (Scenario?.ForceQuit(this, a) == true) p = 1;
            if (_agentRng.Chance(p)) Quit(a);
        }

        SwitchRoles();

        int joiners = QuitsToday + ExtraJoinersToday;
        ExtraJoinersToday = 0;
        if (joiners <= 0) return;

        var mix = JoinMix();
        for (int i = 0; i < joiners; i++)
        {
            var role = PickWeighted(mix, _agentRng);
            CreateAgent(role, PickTown(role, _agentRng), Config.StarterPurse, _agentRng, veteran: false);
            JoinsToday++;
        }
    }

    /// <summary>
    /// Players earning far below average sometimes move to a better-paid
    /// profession. This is how high margins pull in new miners after a shock.
    /// </summary>
    private void SwitchRoles()
    {
        var income = RoleIncome();
        var mix = JoinMix();
        var share = RoleShares();
        foreach (var a in Agents)
        {
            if (!a.Active || a.Role is Role.Speculator or Role.Bot or Role.Casual) continue;
            if (Hour - a.JoinedHour < 24 * 7) continue;
            if (income.GetValueOrDefault(a.Role, 1.0) >= Config.RoleSwitchIncomeRatio) continue;
            // A profession never empties out completely: its last half stays.
            if (share.GetValueOrDefault(a.Role) < Config.RoleMix.GetValueOrDefault(a.Role) * 0.5) continue;
            if (!_agentRng.Chance(Config.RoleSwitchChancePerDay)) continue;
            var role = PickWeighted(mix, _agentRng);
            if (role == a.Role) continue;
            share[a.Role] = share.GetValueOrDefault(a.Role) - 1.0 / Math.Max(1, ActiveAgentCount);
            CancelAgentOrders(a, learn: false);
            if (Roles.Repairs(a.Role)) Towns[a.Town].Repairers.Remove(a.Id);
            a.Role = role;
            a.Skill = 15;
            if (GatherGoodsFor(role).Length > 0 || SettleNearFor(role).Length > 0) a.Town = PickTown(role, _agentRng);
            if (Roles.Repairs(role)) { Towns[a.Town].Repairers.Add(a.Id); a.RepairPrice = 150; }
            GiveCrafterQuest(a);
            a.Tool = null;
            RoleSwitches++;
        }
    }

    public int RoleSwitches { get; private set; }

    /// <summary>Share of active players in each role.</summary>
    private Dictionary<Role, double> RoleShares()
    {
        var counts = new Dictionary<Role, double>();
        int n = 0;
        foreach (var a in Agents)
        {
            if (!a.Active || a.Role is Role.Speculator or Role.Bot) continue;
            counts[a.Role] = counts.GetValueOrDefault(a.Role) + 1;
            n++;
        }
        foreach (var k in counts.Keys.ToList()) counts[k] /= Math.Max(1, n);
        return counts;
    }

    /// <summary>
    /// Where new and switching players go: the base role mix, pulled toward
    /// roles that earn more, but no role grows past 1.5 times its normal share
    /// (in a real game, crowded professions stop looking attractive).
    /// </summary>
    private List<(Role, double)> JoinMix()
    {
        var income = RoleIncome();
        var share = RoleShares();
        return Config.RoleMix
            .Select(kv => (kv.Key, (share.GetValueOrDefault(kv.Key) >= kv.Value * 1.5 && (Config.CapCrafts || !Roles.IsCrafter(kv.Key))) || PlotsFull(kv.Key, share) ? 0.0
                : kv.Value * Math.Pow(Math.Clamp(income.GetValueOrDefault(kv.Key, 1.0), 0.3, 3.0), Config.JoinerIncomeBias)))
            .ToList();
    }

    /// <summary>
    /// Each world has a fixed number of farm plots (Ian's rule), so once they
    /// are all rented nobody new can take up farming.
    /// </summary>
    private bool PlotsFull(Role role, Dictionary<Role, double> share) =>
        role == Role.Farmer && Config.FarmPlotShare > 0 && share.GetValueOrDefault(Role.Farmer) >= Config.FarmPlotShare;

    public long FurniturePlaced { get; private set; }

    /// <summary>
    /// Shops are limited: each town has a few per craft, held by its most
    /// skilled masters. Everyone else sells to other players while online.
    /// </summary>
    private void AssignShops()
    {
        foreach (var a in Agents) a.HasShop = false;
        foreach (var g in Agents.Where(a => a.Active && a.IsMaster(Config.MasterSkill)).GroupBy(a => (a.Town, a.Role)))
            foreach (var a in g.OrderByDescending(a => a.Skill).Take(Config.ShopsPerTownPerCraft))
                a.HasShop = true;
    }

    public void Quit(Agent a)
    {
        CancelAgentOrders(a, learn: false);
        a.Active = false;
        if (Roles.Repairs(a.Role)) Towns[a.Town].Repairers.Remove(a.Id);
        QuitsToday++;
    }

    /// <summary>Each role's average weekly income per online hour, relative to the overall average.</summary>
    /// <summary>Weekly pay-per-hour ratios (role vs everyone) summed after warm-up, for the fair pay check.</summary>
    public Dictionary<Role, (double sum, int weeks)> RoleIncomeWeeks { get; } = new();

    public Dictionary<Role, double> AverageRoleIncome() =>
        RoleIncomeWeeks.ToDictionary(kv => kv.Key, kv => kv.Value.weeks > 0 ? kv.Value.sum / kv.Value.weeks : 1.0);

    public Dictionary<Role, double> RoleIncome()
    {
        var sums = new Dictionary<Role, (double income, double hours)>();
        double totalIncome = 0, totalHours = 0;
        foreach (var a in Agents)
        {
            if (!a.Active || a.Role is Role.Speculator or Role.Bot) continue;
            double inc = a.DailyIncome.Sum();
            double hrs = Math.Max(1, a.SessionHours * 7);
            var cur = sums.GetValueOrDefault(a.Role);
            sums[a.Role] = (cur.income + inc, cur.hours + hrs);
            totalIncome += inc;
            totalHours += hrs;
        }
        double overall = totalHours > 0 ? totalIncome / totalHours : 0;
        var result = new Dictionary<Role, double>();
        foreach (var kv in sums)
        {
            double perHour = kv.Value.hours > 0 ? kv.Value.income / kv.Value.hours : 0;
            result[kv.Key] = overall > 0 ? Math.Max(0.01, perHour / overall) : 1.0;
        }
        return result;
    }
}
