using System;
using System.Collections.Generic;
using Economy.Core.Random;

namespace Economy.Core.Stabilizer
{
    /// <summary>The four hidden NPC roles from the design doc.</summary>
    public enum NpcRole
    {
        /// <summary>Sells imported goods at the top of the band during a shortage or price spike.</summary>
        TraderCaravan = 0,
        /// <summary>Buys surplus at the bottom of the band during a glut or price slump.</summary>
        BuyerGuild = 1,
        /// <summary>Produces slowly when a whole production step is missing from a region.</summary>
        HiredHand = 2,
        /// <summary>Keeps small orders on both sides when the market is thin.</summary>
        MarketMaker = 3,
    }

    public sealed class StabilizerSettings
    {
        /// <summary>Healthy price band: within this share of the reference price.</summary>
        public double PriceBand { get; set; } = 0.25;

        /// <summary>Share of buy demand left unfilled over a day that counts as a shortage.</summary>
        public double ShortageThreshold { get; set; } = 0.10;

        /// <summary>Spread (as a share of reference) above which the market counts as thin.</summary>
        public double SpreadThreshold { get; set; } = 0.20;

        /// <summary>Liquidity below this share of a day's demand counts as thin.</summary>
        public double LiquidityThreshold { get; set; } = 0.25;

        /// <summary>Hours a metric must stay out of band before a role spawns.</summary>
        public int SpawnAfterHours { get; set; } = 6;

        /// <summary>Hours back in band before a role starts to shrink.</summary>
        public int RetireAfterHours { get; set; } = 24;

        /// <summary>Random extra wait, 0..N hours, added to spawn and retire waits so players cannot time them.</summary>
        public int JitterHours { get; set; } = 3;

        /// <summary>Hours after a role retires before it may spawn again in the same market (stops flicker).</summary>
        public int RespawnCooldownHours { get; set; } = 72;

        /// <summary>Strength steps; strength 1 is the small first spawn, MaxStrength is the cap.</summary>
        public int MaxStrength { get; set; } = 6;

        /// <summary>Volume cap per role at full strength, as a share of a day's normal volume.</summary>
        public double CaravanCap { get; set; } = 0.30;
        public double BuyerGuildCap { get; set; } = 0.30;
        public double HiredHandCap { get; set; } = 0.10;
        public double MarketMakerCap { get; set; } = 0.05;

        /// <summary>Reference price half-life in hours (14 days).</summary>
        public double ReferenceHalfLifeHours { get; set; } = 14 * 24;

        public double CapFor(NpcRole role) => role switch
        {
            NpcRole.TraderCaravan => CaravanCap,
            NpcRole.BuyerGuild => BuyerGuildCap,
            NpcRole.HiredHand => HiredHandCap,
            NpcRole.MarketMaker => MarketMakerCap,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    /// <summary>
    /// A slow-moving reference price: an exponential average of observed
    /// player trade prices with a long half-life, so NPCs smooth shocks but
    /// do not fight lasting change that players chose.
    /// </summary>
    public sealed class ReferencePrice
    {
        private readonly double _alpha;

        public ReferencePrice(double initial, double halfLifeHours)
        {
            if (initial <= 0) throw new ArgumentOutOfRangeException(nameof(initial));
            if (halfLifeHours <= 0) throw new ArgumentOutOfRangeException(nameof(halfLifeHours));
            Value = initial;
            _alpha = 1.0 - Math.Pow(0.5, 1.0 / halfLifeHours);
        }

        public double Value { get; private set; }

        /// <summary>Jumps straight to a value (used once, when the warm-up calibration ends).</summary>
        public void Reset(double value)
        {
            if (value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)) Value = value;
        }

        /// <summary>Moves one hour toward an observed price.</summary>
        public void Observe(double price)
        {
            if (price <= 0 || double.IsNaN(price) || double.IsInfinity(price)) return;
            Value += _alpha * (price - Value);
        }
    }

    /// <summary>What the stabilizer can see for one good in one town, once an hour.</summary>
    public struct MarketSignals
    {
        /// <summary>Current price estimate (recent player trades, else mid of the book).</summary>
        public double Price;
        public double Reference;

        /// <summary>Share of buy demand left unfilled over the last day.</summary>
        public double ShortageRate;

        /// <summary>Units offered within 10% of the best ask.</summary>
        public double Liquidity;

        /// <summary>A normal day's volume for this good in this town.</summary>
        public double DailyVolume;

        /// <summary>Best ask minus best bid over reference; infinity if a side is empty.</summary>
        public double Spread;

        /// <summary>True if any player in the region produced this good recently.</summary>
        public bool PlayerProducersActive;

        /// <summary>True if anyone has wanted to buy recently (no demand means nothing to fix).</summary>
        public bool HasDemand;
    }

    public enum StabilizerEventKind
    {
        Spawn,
        Grow,
        Shrink,
        Retire,
    }

    public readonly struct StabilizerEvent
    {
        public StabilizerEvent(NpcRole role, StabilizerEventKind kind, int strength)
        {
            Role = role;
            Kind = kind;
            Strength = strength;
        }

        public NpcRole Role { get; }
        public StabilizerEventKind Kind { get; }
        public int Strength { get; }
    }

    /// <summary>
    /// The hidden stabilizer for one good in one town. Once an hour it reads
    /// <see cref="MarketSignals"/>, and for each role decides whether to spawn,
    /// grow, shrink or retire. It never sets prices or moves coins: the caller
    /// turns strengths into ordinary orders, paid from a finite treasury.
    /// </summary>
    public sealed class StabilizerController
    {
        private readonly StabilizerSettings _settings;
        private readonly RoleState[] _roles;

        public StabilizerController(StabilizerSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _roles = new RoleState[4];
            for (int i = 0; i < _roles.Length; i++) _roles[i] = new RoleState();
        }

        public int Strength(NpcRole role) => _roles[(int)role].Strength;

        /// <summary>Share of a day's normal volume this role should trade per day right now.</summary>
        public double VolumeShare(NpcRole role)
        {
            int s = _roles[(int)role].Strength;
            return s == 0 ? 0 : _settings.CapFor(role) * s / _settings.MaxStrength;
        }

        public bool AnyActive
        {
            get
            {
                foreach (var r in _roles) if (r.Strength > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// Runs one hourly decision. <paramref name="canAfford"/> is false when
        /// the treasury cannot pay for more NPC buying; roles that spend
        /// treasury coins then cannot grow.
        /// </summary>
        public void Evaluate(in MarketSignals s, bool canAfford, SimRandom rng, List<StabilizerEvent> events)
        {
            double band = _settings.PriceBand;
            bool priceHigh = s.Reference > 0 && s.Price > s.Reference * (1 + band);
            bool priceLow = s.Reference > 0 && s.Price > 0 && s.Price < s.Reference * (1 - band);
            bool shortage = s.HasDemand && s.ShortageRate > _settings.ShortageThreshold;
            bool thin = s.HasDemand && (s.Spread > _settings.SpreadThreshold || s.Liquidity < s.DailyVolume * _settings.LiquidityThreshold);

            Step(NpcRole.TraderCaravan, priceHigh || (shortage && !priceLow), true, rng, events);
            Step(NpcRole.BuyerGuild, priceLow && !shortage, canAfford, rng, events);
            Step(NpcRole.HiredHand, shortage && !s.PlayerProducersActive, true, rng, events);
            Step(NpcRole.MarketMaker, thin && !priceHigh && !priceLow, canAfford, rng, events);
        }

        private void Step(NpcRole role, bool outOfBand, bool mayGrow, SimRandom rng, List<StabilizerEvent> events)
        {
            var r = _roles[(int)role];
            if (outOfBand)
            {
                r.InBandHours = 0;
                r.OutOfBandHours++;
                if (r.Strength == 0)
                {
                    if (r.CooldownHours > 0) { r.CooldownHours--; return; }
                    if (r.SpawnWait < 0) r.SpawnWait = _settings.SpawnAfterHours + rng.NextInt(0, _settings.JitterHours + 1);
                    if (r.OutOfBandHours >= r.SpawnWait && mayGrow)
                    {
                        r.Strength = 1;
                        r.SpawnWait = -1;
                        events.Add(new StabilizerEvent(role, StabilizerEventKind.Spawn, r.Strength));
                    }
                }
                else if (r.Strength < _settings.MaxStrength && mayGrow)
                {
                    r.Strength++;
                    events.Add(new StabilizerEvent(role, StabilizerEventKind.Grow, r.Strength));
                }
            }
            else
            {
                r.OutOfBandHours = 0;
                r.SpawnWait = -1;
                if (r.Strength == 0)
                {
                    if (r.CooldownHours > 0) r.CooldownHours--;
                    return;
                }
                r.InBandHours++;
                if (r.RetireWait < 0) r.RetireWait = _settings.RetireAfterHours + rng.NextInt(0, _settings.JitterHours + 1);
                if (r.InBandHours >= r.RetireWait)
                {
                    r.Strength--;
                    events.Add(new StabilizerEvent(role,
                        r.Strength == 0 ? StabilizerEventKind.Retire : StabilizerEventKind.Shrink, r.Strength));
                    if (r.Strength == 0) { r.RetireWait = -1; r.InBandHours = 0; r.CooldownHours = _settings.RespawnCooldownHours; }
                }
            }
        }

        private sealed class RoleState
        {
            public int Strength;
            public int OutOfBandHours;
            public int InBandHours;
            public int SpawnWait = -1;
            public int RetireWait = -1;
            public int CooldownHours;
        }
    }
}
