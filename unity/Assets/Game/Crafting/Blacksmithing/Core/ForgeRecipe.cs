using System.Collections.Generic;

namespace Mmo.Crafting.Blacksmithing
{
    /// <summary>One input of a recipe. Weight is how much its quality counts toward the ceiling.</summary>
    public sealed class RecipeInput
    {
        public string Name { get; }
        public int Count { get; }
        public double Weight { get; }

        public RecipeInput(string name, int count, double weight)
        {
            Name = name;
            Count = count;
            Weight = weight;
        }
    }

    /// <summary>
    /// A blacksmithing recipe. Higher tiers add steps (quench, finish), more strikes and a faster beat,
    /// following the spec: "each tier adds one more step or one more sub-craft".
    /// </summary>
    public sealed class ForgeRecipe
    {
        public string Id { get; }
        public string Name { get; }
        public SkillRank Rank { get; }
        public int RequiredSkill { get; }
        public int ItemLevel { get; }
        /// <summary>The method's quality cap (one of the three ceiling caps).</summary>
        public int MethodCap { get; }
        /// <summary>Heat (0..1) the bar should be pulled at. 0.55 = orange, 0.75 = yellow.</summary>
        public double TargetHeat { get; }
        /// <summary>Strikes the ghost outline needs on Tip, Body and Edge.</summary>
        public int[] StrikesPerLane { get; }
        public double Bpm { get; }
        /// <summary>Chance a note comes half a beat after the previous one instead of a full beat.</summary>
        public double HalfBeatChance { get; }
        public bool HasQuench { get; }
        public bool HasFinish { get; }
        public IReadOnlyList<RecipeInput> Inputs { get; }
        public string Description { get; }

        public int TotalStrikes => StrikesPerLane[0] + StrikesPerLane[1] + StrikesPerLane[2];

        public ForgeRecipe(string id, string name, SkillRank rank, int requiredSkill, int itemLevel, int methodCap,
            double targetHeat, int[] strikesPerLane, double bpm, double halfBeatChance, bool hasQuench, bool hasFinish,
            RecipeInput[] inputs, string description)
        {
            Id = id;
            Name = name;
            Rank = rank;
            RequiredSkill = requiredSkill;
            ItemLevel = itemLevel;
            MethodCap = methodCap;
            TargetHeat = targetHeat;
            StrikesPerLane = strikesPerLane;
            Bpm = bpm;
            HalfBeatChance = halfBeatChance;
            HasQuench = hasQuench;
            HasFinish = hasFinish;
            Inputs = inputs;
            Description = description;
        }

        // Iron path, level band 1-20: every sword in the line keeps using iron.
        public static readonly ForgeRecipe PlainIronSword = new ForgeRecipe(
            "plain_iron_sword", "Plain Iron Sword", SkillRank.Novice, 1, 1, 60,
            targetHeat: 0.55, strikesPerLane: new[] { 3, 6, 3 }, bpm: 90, halfBeatChance: 0.0,
            hasQuench: false, hasFinish: false,
            inputs: new[]
            {
                new RecipeInput("Iron ingot", 2, 0.85),
                new RecipeInput("Hide strip (grip)", 1, 0.15),
            },
            description: "Novice stopgap blade. Heat to orange, then strike.");

        public static readonly ForgeRecipe IronShortsword = new ForgeRecipe(
            "iron_shortsword", "Iron Shortsword", SkillRank.Apprentice, 20, 8, 80,
            targetHeat: 0.70, strikesPerLane: new[] { 5, 8, 7 }, bpm: 100, halfBeatChance: 0.10,
            hasQuench: false, hasFinish: true,
            inputs: new[]
            {
                new RecipeInput("Iron ingot", 3, 0.85),
                new RecipeInput("Leather grip", 1, 0.15),
            },
            description: "Heat to yellow, strike, then sharpen. Finish can add a bonus trait.");

        public static readonly ForgeRecipe IronLongsword = new ForgeRecipe(
            "iron_longsword", "Iron Longsword", SkillRank.Journeyman, 40, 16, 100,
            targetHeat: 0.75, strikesPerLane: new[] { 6, 14, 12 }, bpm: 110, halfBeatChance: 0.20,
            hasQuench: true, hasFinish: true,
            inputs: new[]
            {
                new RecipeInput("Iron blade blank", 1, 0.60),
                new RecipeInput("Iron guard + pommel", 1, 0.25),
                new RecipeInput("Leather grip", 1, 0.15),
            },
            description: "Full process: heat, strike, quench, finish.");

        public static readonly ForgeRecipe[] All = { PlainIronSword, IronShortsword, IronLongsword };
    }
}
