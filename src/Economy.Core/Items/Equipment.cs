using System;

namespace Economy.Core.Items
{
    /// <summary>
    /// One piece of equipment in the world: a tool, weapon or armor piece with
    /// its own quality and durability. Stackable goods (ore, food) are counted
    /// in inventories instead and do not use this type.
    /// </summary>
    public sealed class Equipment
    {
        internal Equipment(long id, GoodDefinition good, int quality, int originalMax, int? coreQuality)
        {
            Id = id;
            Good = good;
            Quality = quality;
            OriginalMaxDurability = originalMax;
            MaxDurability = originalMax;
            Durability = originalMax;
            CoreQuality = coreQuality;
        }

        public long Id { get; }
        public GoodDefinition Good { get; }
        public int Quality { get; }

        /// <summary>Durability when the item was made.</summary>
        public int OriginalMaxDurability { get; }

        /// <summary>Current ceiling. Only ever goes down: breaks and upkeep lower it.</summary>
        public int MaxDurability { get; internal set; }

        public int Durability { get; internal set; }

        /// <summary>Quality of the relic core set in this item, or null when it has none.</summary>
        public int? CoreQuality { get; internal set; }

        public int BreakCount { get; internal set; }

        public bool IsBroken => Durability <= 0;

        public bool HasCore => CoreQuality.HasValue;

        /// <summary>A broken item, or a magical item with no core, does nothing.</summary>
        public bool IsUsable => !IsBroken && (!Good.IsMagical || HasCore);

        public override string ToString() =>
            $"{Good.Id}#{Id} q{Quality} {Durability}/{MaxDurability} (orig {OriginalMaxDurability})" +
            (Good.IsMagical ? (HasCore ? $" core q{CoreQuality}" : " no core") : string.Empty);
    }
}
