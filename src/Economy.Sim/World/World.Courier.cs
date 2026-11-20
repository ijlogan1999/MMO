using Economy.Core.Items;
using Economy.Core.Ledger;

namespace Economy.Sim.World;

/// <summary>A paid delivery: carry goods from one town to another, paid on arrival.</summary>
public sealed class CourierJob
{
    /// <summary>The player who posted it, or -1 for a town moving NPC stock.</summary>
    public int Owner { get; init; }
    public int From { get; init; }
    public int To { get; init; }
    public int Good { get; init; }
    public int Qty { get; init; }
    public int Quality { get; init; }
    public long Fee { get; init; }
    /// <summary>Deposit the courier puts up, paid to the owner if the cargo is lost.</summary>
    public long Collateral { get; init; }
    public int PostedHour { get; init; }
}

/// <summary>
/// Courier jobs (Ian, 2026-11-19). Haulers no longer buy cargo. A player whose
/// goods fetch clearly more in another town posts a job: the hauler carries the
/// goods there and is paid a fee on delivery, and the goods go into the owner's
/// storage in that town to sell. Towns post jobs too, moving NPC stock from
/// where it piles up to where it is short. Goods have weight, and wagons and
/// fees go by weight, so heavy goods cost more to move.
/// </summary>
public sealed partial class World
{
    private readonly List<CourierJob>[] _jobs;
    private readonly AccountId _courierEscrow;
    private long[] _jobFees = Array.Empty<long>();

    public long CourierJobsPosted { get; private set; }
    public long CourierJobsDelivered { get; private set; }
    public long CourierFeesPaid { get; private set; }
    public long CourierCargoLost { get; private set; }

    public double WeightOf(int g) => Config.GoodWeights.TryGetValue(Goods.Defs[g].Id, out var w) ? w : 1.0;

    private double RecentPrice(Town t, int g)
    {
        var m = t.Markets[g];
        return m.LastTradeHour >= Hour - 72 ? m.LastPrice : m.Reference.Value;
    }

    private long CourierFee(int g, int qty, double destPrice, int hours) =>
        (long)Math.Ceiling(qty * (Config.CourierFeeShare * destPrice + Config.CourierWeightFee * WeightOf(g) * hours));

    /// <summary>
    /// A player with spare goods checks whether another town pays enough more
    /// to be worth a courier, and posts a job for part of them if so.
    /// Returns units handed to the courier.
    /// </summary>
    private int MaybePostCourierJob(Agent a, int g, int spare)
    {
        if (!Config.CourierJobs || spare <= 0) return 0;
        var here = Towns[a.Town];
        if (a.CourierPending.Contains(g)) return 0;
        double local = RecentPrice(here, g);
        int bestTo = -1, bestQty = 0;
        double bestGain = 0;
        long bestFee = 0;
        foreach (var t in Towns)
        {
            if (t.Index == here.Index || !CanTravel(here, t)) continue;
            double dest = RecentPrice(t, g);
            int hours = _worldFile.TravelHoursBetween(here.Def.Region, t.Def.Region);
            // Don't ship more than that town buys in a couple of days.
            int qty = Math.Min(spare, Math.Min(Config.CourierMaxUnits, (int)Math.Max(3, t.Markets[g].DailyVolumeEma * 2)));
            if (qty <= 0) continue;
            long fee = CourierFee(g, qty, dest, hours);
            double net = qty * dest * (1 - TaxRate) - fee;
            double gain = net - qty * local * (1 + Config.CourierMinGain);
            if (gain > bestGain) { bestGain = gain; bestTo = t.Index; bestQty = qty; bestFee = fee; }
        }
        if (bestTo < 0 || Coins(a) < bestFee * 2) return 0;
        // The fee is held until delivery.
        Ledger.Transfer(a.Account, _courierEscrow, bestFee, Flow.EscrowRelease);
        int quality = a.Inventory.QualityOf(g);
        a.Inventory.Take(g, bestQty);
        a.CourierPending.Add(g);
        _jobFees[here.Index] += bestFee;
        double value = bestQty * RecentPrice(Towns[bestTo], g);
        _jobs[here.Index].Add(new CourierJob
        {
            Owner = a.Id, From = here.Index, To = bestTo, Good = g, Qty = bestQty, Quality = quality,
            Fee = bestFee, Collateral = (long)(value * Config.CourierCollateralShare), PostedHour = Hour,
        });
        CourierJobsPosted++;
        return bestQty;
    }

    /// <summary>Once a day, towns move NPC stock from where it piles up to where it is short.</summary>
    private void PostTownCourierJobs()
    {
        if (!Config.CourierJobs || !Config.TownCourierJobs) return;
        for (int g = 0; g < Goods.Count; g++)
        {
            if (IsRare(g) || Goods.Defs[g].HasDurability) continue;
            Town? from = null, to = null;
            foreach (var t in Towns)
            {
                if (from == null || t.Markets[g].NpcStock > from.Markets[g].NpcStock) from = t;
            }
            if (from == null || from.Markets[g].NpcStock < Config.TownCourierMinStock) continue;
            double pFrom = RecentPrice(from, g);
            foreach (var t in Towns)
            {
                if (t == from || !CanTravel(from, t) || t.Markets[g].NpcStock > 0) continue;
                if (RecentPrice(t, g) < pFrom * (1 + Config.CourierMinGain)) continue;
                if (to == null || RecentPrice(t, g) > RecentPrice(to, g)) to = t;
            }
            if (to == null) continue;
            int qty = Math.Min(from.Markets[g].NpcStock / 2, Config.CourierMaxUnits);
            if (qty <= 0) continue;
            from.Markets[g].NpcStock -= qty;
            int hours = _worldFile.TravelHoursBetween(from.Def.Region, to.Def.Region);
            double dest = RecentPrice(to, g);
            var job = new CourierJob
            {
                Owner = -1, From = from.Index, To = to.Index, Good = g, Qty = qty, Quality = NpcGoodsQuality,
                Fee = CourierFee(g, qty, dest, hours), Collateral = (long)(qty * dest * Config.CourierCollateralShare), PostedHour = Hour,
            };
            _jobs[from.Index].Add(job);
            _jobFees[from.Index] += job.Fee;
            CourierJobsPosted++;
        }
    }

    /// <summary>Jobs nobody took within two days go back to whoever posted them.</summary>
    private void ExpireCourierJobs()
    {
        foreach (var list in _jobs)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var j = list[i];
                if (Hour - j.PostedHour < Config.CourierJobExpiryHours) continue;
                list.RemoveAt(i);
                _jobFees[j.From] -= j.Fee;
                ReturnJob(j);
            }
        }
    }

    private void ReturnJob(CourierJob j)
    {
        if (j.Owner < 0) { Towns[j.From].Markets[j.Good].NpcStock += j.Qty; return; }
        var owner = Agents[j.Owner];
        Ledger.Transfer(_courierEscrow, owner.Account, j.Fee, Flow.EscrowRelease);
        owner.Inventory.Add(j.Good, j.Qty, j.Quality);
        owner.CourierPending.Remove(j.Good);
    }

    private void CourierTurn(Agent a, double productivity)
    {
        // Arrived: hand over the cargo and collect the fees.
        if (a.Cargo.Count > 0)
        {
            for (int i = a.Cargo.Count - 1; i >= 0; i--)
            {
                var j = a.Cargo[i];
                if (j.To != a.Town) continue;
                a.Cargo.RemoveAt(i);
                Deliver(a, j);
            }
            if (a.Cargo.Count > 0)
            {
                // Still carrying something for elsewhere: keep going.
                var next = a.Cargo[0].To;
                a.TravelTo = next;
                a.TravelHoursLeft = RideHours(a, a.Town, next);
                return;
            }
        }

        var here = Towns[a.Town];
        var jobs = _jobs[here.Index];
        HorseUpkeep(a);
        bool mounted = Mounted(a);
        double capacity = Config.WagonWeight + (mounted ? Config.HorseExtraWeight : 0);
        if (jobs.Count > 0)
        {
            // Best destination by fees per travel hour, loaded up to the wagon's weight.
            int bestTo = -1;
            double bestRate = 0;
            foreach (var grp in jobs.Where(j => CanTravel(here, Towns[j.To])).GroupBy(j => j.To))
            {
                double fees = 0, weight = 0;
                foreach (var j in grp.OrderByDescending(j => j.Fee / Math.Max(0.1, j.Qty * WeightOf(j.Good))))
                {
                    double w = j.Qty * WeightOf(j.Good);
                    if (weight + w > capacity) continue;
                    weight += w;
                    fees += j.Fee;
                }
                double rate = fees / RideHours(a, here.Index, grp.Key);
                if (rate > bestRate) { bestRate = rate; bestTo = grp.Key; }
            }
            if (bestTo >= 0)
            {
                double load = 0;
                long coins = Coins(a), held = 0;
                foreach (var j in jobs.Where(j => j.To == bestTo).OrderByDescending(j => j.Fee / Math.Max(0.1, j.Qty * WeightOf(j.Good))).ToList())
                {
                    double w = j.Qty * WeightOf(j.Good);
                    // The courier must be able to cover the deposit on everything carried.
                    if (load + w > capacity || held + j.Collateral > coins) continue;
                    load += w;
                    held += j.Collateral;
                    jobs.Remove(j);
                    _jobFees[here.Index] -= j.Fee;
                    a.Cargo.Add(j);
                }
                if (a.Cargo.Count > 0)
                {
                    a.TravelTo = bestTo;
                    a.TravelHoursLeft = RideHours(a, here.Index, bestTo);
                    if (mounted) MountedTrips++;
                    return;
                }
            }
        }

        // No work here: ride to the town with the most waiting fees, or gather meanwhile.
        int target = -1;
        long most = Config.CourierRideEmptyMinFees;
        foreach (var t in Towns)
        {
            if (t.Index == here.Index || !CanTravel(here, t)) continue;
            long fees = _jobFees[t.Index];
            if (fees > most) { most = fees; target = t.Index; }
        }
        if (target >= 0 && _agentRng.Chance(0.5))
        {
            a.TravelTo = target;
            a.TravelHoursLeft = RideHours(a, here.Index, target);
            return;
        }
        Gather(a, productivity * 0.6);
    }

    private bool Mounted(Agent a) => Config.Horses && a.HasHorse && a.Saddle != null && a.Saddle.IsUsable;

    /// <summary>Travel hours between towns; a rider is faster.</summary>
    private int RideHours(Agent a, int from, int to)
    {
        int h = _worldFile.TravelHoursBetween(Towns[from].Def.Region, Towns[to].Def.Region);
        return Mounted(a) ? Math.Max(1, (int)Math.Round(h * Config.HorseTravelFactor)) : h;
    }

    /// <summary>A hauler of high enough level buys a horse once they can spare the coins, then keeps a saddle on it.</summary>
    private void HorseUpkeep(Agent a)
    {
        if (!Config.Horses || SaddleGood < 0 || LevelOf(a) < Config.HorseMinLevel) return;
        if (!a.HasHorse)
        {
            if (Coins(a) < Config.HorsePrice * 2) return;
            Ledger.Transfer(a.Account, Ledger.Treasury, Config.HorsePrice, Flow.HorsePurchase);
            a.HasHorse = true;
            HorsesBought++;
        }
        a.Saddle = Maintain(a, a.Saddle, SaddleGood, s => a.Saddle = s);
    }

    private void Deliver(Agent courier, CourierJob j)
    {
        if (j.Owner < 0)
        {
            Towns[j.To].Markets[j.Good].NpcStock += j.Qty;
            if (!Ledger.TryTransfer(Ledger.Treasury, courier.Account, j.Fee, Flow.TownContract, out _)) return;
        }
        else
        {
            var owner = Agents[j.Owner];
            owner.CourierPending.Remove(j.Good);
            owner.AddRemote(j.To, j.Good, j.Qty, j.Quality);
            Ledger.Transfer(_courierEscrow, courier.Account, j.Fee, Flow.EscrowRelease);
            owner.TodayIncome -= j.Fee;
            Track(owner, IncomeKind.HaulFeesOut, -j.Fee);
        }
        courier.TodayIncome += j.Fee;
        Track(courier, IncomeKind.HaulFeesIn, j.Fee);
        CourierJobsDelivered++;
        CourierFeesPaid += j.Fee;
    }

    /// <summary>Cargo lost on the road: the courier's deposit goes to the owner, and the fee is refunded.</summary>
    private void LoseCargo(Agent courier)
    {
        foreach (var j in courier.Cargo)
        {
            CourierCargoLost += j.Qty;
            if (j.Owner < 0) continue;
            var owner = Agents[j.Owner];
            owner.CourierPending.Remove(j.Good);
            long pay = Math.Min(j.Collateral, Coins(courier));
            if (pay > 0) Ledger.Transfer(courier.Account, owner.Account, pay, Flow.PlayerTrade);
            courier.TodayIncome -= pay;
            Ledger.Transfer(_courierEscrow, owner.Account, j.Fee, Flow.EscrowRelease);
        }
        courier.Cargo.Clear();
    }

    /// <summary>Lists goods a player keeps in other towns' storage on those towns' markets.</summary>
    private void SellRemoteStock(Agent a)
    {
        if (a.Remote.Count == 0) return;
        foreach (var key in a.Remote.Keys.ToList())
        {
            var (qty, quality) = a.Remote[key];
            a.Remote.Remove(key);
            int town = key / 10000, g = key % 10000;
            long price = Math.Max(1, (long)Math.Round(RecentPrice(Towns[town], g) * (0.97 + 0.06 * _agentRng.NextDouble())));
            PlaceSellAt(a, town, g, qty, price, quality);
        }
    }
}
