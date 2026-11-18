using System;

namespace Economy.Core.Items
{
    /// <summary>Where a good sits in the production chain.</summary>
    public enum GoodTier
    {
        /// <summary>Gathered from nodes or farmed: ore, timber, grain.</summary>
        Raw,
        /// <summary>Processed raw goods: ingots, planks, flour.</summary>
        Intermediate,
        /// <summary>Tradeable sub-crafts: blade blanks, sauce base, padding.</summary>
        Component,
        /// <summary>Goods players use: food, tools, armor, potions.</summary>
        Finished,
        /// <summary>Dungeon drops set into magical gear. Never crafted, never sold by NPCs.</summary>
        RelicCore,
    }

    /// <summary>How a good leaves the world once made.</summary>
    public enum GoodUse
    {
        /// <summary>Consumed as a recipe input.</summary>
        Material,
        /// <summary>Eaten. Each agent eats on a schedule.</summary>
        Food,
        /// <summary>Drunk or applied; used up in combat.</summary>
        Consumable,
        /// <summary>Has durability, wears with use, breaks and is repaired.</summary>
        Equipment,
    }

    /// <summary>
    /// Static definition of a good, loaded from data. One per good id; every
    /// item instance and stack in the world points at one of these.
    /// </summary>
    public sealed class GoodDefinition
    {
        public GoodDefinition(
            string id,
            string name,
            GoodTier tier,
            GoodUse use,
            int baseDurability = 0,
            bool isMagical = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Good id is required.", nameof(id));
            if (use == GoodUse.Equipment && baseDurability <= 0)
                throw new ArgumentException($"Equipment '{id}' needs a positive base durability.", nameof(baseDurability));
            if (use != GoodUse.Equipment && baseDurability != 0)
                throw new ArgumentException($"Only equipment has durability ('{id}').", nameof(baseDurability));
            if (isMagical && use != GoodUse.Equipment)
                throw new ArgumentException($"Only equipment can be magical ('{id}').", nameof(isMagical));

            Id = id;
            Name = name;
            Tier = tier;
            Use = use;
            BaseDurability = baseDurability;
            IsMagical = isMagical;
        }

        public string Id { get; }
        public string Name { get; }
        public GoodTier Tier { get; }
        public GoodUse Use { get; }

        /// <summary>Durability of a fresh item. Zero for anything that is not equipment.</summary>
        public int BaseDurability { get; }

        /// <summary>Magical equipment needs a relic core set in it to work.</summary>
        public bool IsMagical { get; }

        public bool HasDurability => Use == GoodUse.Equipment;

        public override string ToString() => Id;
    }
}
