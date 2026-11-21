using System;

namespace Economy.Core.Items
{
    /// <summary>Tunable durability numbers. Starting guesses from the design doc.</summary>
    public sealed class DurabilitySettings
    {
        /// <summary>
        /// Share of the item's current ceiling a repair after a full break keeps,
        /// by repairer skill 0..100 (linear between novice and expert).
        /// Design doc: novice 50%, expert 90%.
        /// </summary>
        public double NoviceRepairFactor { get; set; } = 0.50;
        public double ExpertRepairFactor { get; set; } = 0.90;

        /// <summary>
        /// Routine upkeep before a break costs this share of the ceiling per
        /// fully-worn item. Topping up at half wear costs half of this.
        /// Much cheaper than a break, so players who maintain gear get more life.
        /// </summary>
        public double UpkeepCeilingLoss { get; set; } = 0.05;

        /// <summary>Below this share of its original ceiling an item is worn out for good.</summary>
        public double WornOutFraction { get; set; } = 0.20;

        /// <summary>Share of the item's recipe materials returned when a worn-out item is salvaged.</summary>
        public double SalvageReturn { get; set; } = 0.25;

        /// <summary>A fresh item's ceiling scales with quality: quality 1 gets this share, quality 100 gets 100%.</summary>
        public double MinQualityDurabilityFactor { get; set; } = 0.50;

        public void Validate()
        {
            if (NoviceRepairFactor <= 0 || NoviceRepairFactor > 1) throw new InvalidOperationException("NoviceRepairFactor must be in (0, 1].");
            if (ExpertRepairFactor < NoviceRepairFactor || ExpertRepairFactor > 1) throw new InvalidOperationException("ExpertRepairFactor must be in [novice, 1].");
            if (UpkeepCeilingLoss < 0 || UpkeepCeilingLoss >= 1) throw new InvalidOperationException("UpkeepCeilingLoss must be in [0, 1).");
            if (WornOutFraction <= 0 || WornOutFraction >= 1) throw new InvalidOperationException("WornOutFraction must be in (0, 1).");
            if (MinQualityDurabilityFactor <= 0 || MinQualityDurabilityFactor > 1) throw new InvalidOperationException("MinQualityDurabilityFactor must be in (0, 1].");
        }
    }

    /// <summary>What happened when an item took wear.</summary>
    public enum WearOutcome
    {
        /// <summary>Durability went down; the item still works.</summary>
        Worn,
        /// <summary>Durability hit zero; the item stops working until repaired.</summary>
        Broke,
        /// <summary>Durability hit zero and the item's relic core shattered.</summary>
        BrokeAndCoreShattered,
        /// <summary>The item was already broken or unusable, so nothing changed.</summary>
        NotUsable,
    }

    /// <summary>
    /// Wear, breaking, repair, upkeep and relic cores. Every rule here is shared
    /// by the simulation and the game server.
    /// </summary>
    public sealed class DurabilityRules
    {
        private long _nextId = 1;

        public DurabilityRules(DurabilitySettings? settings = null)
        {
            Settings = settings ?? new DurabilitySettings();
            Settings.Validate();
        }

        public DurabilitySettings Settings { get; }

        /// <summary>
        /// Makes a new piece of equipment. Magical equipment needs a relic core
        /// at creation (the crafter sets one), so <paramref name="coreQuality"/>
        /// is required for magical goods and forbidden otherwise.
        /// </summary>
        public Equipment Create(GoodDefinition good, int quality, int? coreQuality = null)
        {
            if (good == null) throw new ArgumentNullException(nameof(good));
            if (!good.HasDurability) throw new ArgumentException($"'{good.Id}' is not equipment.", nameof(good));
            if (good.IsMagical && !coreQuality.HasValue)
                throw new ArgumentException($"Magical item '{good.Id}' needs a relic core to be made.", nameof(coreQuality));
            if (!good.IsMagical && coreQuality.HasValue)
                throw new ArgumentException($"'{good.Id}' is not magical and cannot hold a core.", nameof(coreQuality));

            int q = Quality.Clamp(quality);
            double factor = Settings.MinQualityDurabilityFactor +
                            (1.0 - Settings.MinQualityDurabilityFactor) * (q - Quality.Min) / (Quality.Max - Quality.Min);
            int max = Math.Max(1, (int)Math.Round(good.BaseDurability * factor));
            int? core = coreQuality.HasValue ? Quality.Clamp(coreQuality.Value) : (int?)null;
            return new Equipment(_nextId++, good, q, max, core);
        }

        /// <summary>
        /// Applies wear from use. At zero the item breaks; a magical item's core
        /// shatters on the break, whatever its condition.
        /// </summary>
        public WearOutcome Wear(Equipment item, int amount)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!item.IsUsable) return WearOutcome.NotUsable;

            item.Durability = Math.Max(0, item.Durability - amount);
            if (item.Durability > 0) return WearOutcome.Worn;

            item.BreakCount++;
            if (item.Good.IsMagical)
            {
                item.CoreQuality = null;
                return WearOutcome.BrokeAndCoreShattered;
            }
            return WearOutcome.Broke;
        }

        /// <summary>Repair factor for a repairer's skill, 0 (novice) to 100 (expert).</summary>
        public double RepairFactor(int repairerSkill)
        {
            double t = Math.Max(0, Math.Min(100, repairerSkill)) / 100.0;
            return Settings.NoviceRepairFactor + (Settings.ExpertRepairFactor - Settings.NoviceRepairFactor) * t;
        }

        /// <summary>
        /// Repairs a broken item. The new ceiling is the old ceiling times the
        /// repairer's factor, so each break lowers it and a repair can never
        /// raise it. A magical item needs a new relic core to be repaired.
        /// Returns false (and changes nothing) if the item is worn out for good.
        /// </summary>
        public bool RepairBroken(Equipment item, int repairerSkill, int? newCoreQuality = null)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!item.IsBroken) throw new InvalidOperationException($"{item} is not broken; use Upkeep instead.");
            if (item.Good.IsMagical && !newCoreQuality.HasValue)
                throw new InvalidOperationException($"{item} lost its core when it broke; repairing it needs a new relic core.");
            if (!item.Good.IsMagical && newCoreQuality.HasValue)
                throw new InvalidOperationException($"{item} is not magical and cannot hold a core.");

            int newMax = (int)Math.Floor(item.MaxDurability * RepairFactor(repairerSkill));
            if (IsBelowWornOut(item, newMax)) return false;

            item.MaxDurability = newMax;
            item.Durability = newMax;
            if (newCoreQuality.HasValue) item.CoreQuality = Quality.Clamp(newCoreQuality.Value);
            return true;
        }

        /// <summary>
        /// Routine upkeep on an item that has not broken: restores it to full
        /// at a small cost to the ceiling, scaled by how worn it was.
        /// </summary>
        public void Upkeep(Equipment item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.IsBroken) throw new InvalidOperationException($"{item} is broken; use RepairBroken instead.");
            if (item.Durability == item.MaxDurability) return;

            double wornShare = 1.0 - (double)item.Durability / item.MaxDurability;
            int loss = (int)Math.Ceiling(item.MaxDurability * Settings.UpkeepCeilingLoss * wornShare);
            item.MaxDurability = Math.Max(1, item.MaxDurability - loss);
            item.Durability = item.MaxDurability;
        }

        /// <summary>True once the item's ceiling has fallen too low to be worth repairing.</summary>
        public bool IsWornOut(Equipment item) => IsBelowWornOut(item, item.MaxDurability);

        private bool IsBelowWornOut(Equipment item, int max) =>
            max < item.OriginalMaxDurability * Settings.WornOutFraction;
    }
}
