using System.Collections.Generic;

namespace Mmo.Crafting.Cooking
{
    /// <summary>One input of a recipe. Weight is how much its quality counts toward the ceiling.</summary>
    public sealed class CookInput
    {
        public Ingredient Ingredient { get; }
        public int Count { get; }
        public double Weight { get; }

        public CookInput(Ingredient ingredient, int count, double weight)
        {
            Ingredient = ingredient;
            Count = count;
            Weight = weight;
        }
    }

    /// <summary>
    /// Something that goes into a pot. The first part is in from the start (the liquid); the rest are added by the cook,
    /// in order, whenever they judge the moment right. Each part cooks from the moment it goes in.
    /// </summary>
    public sealed class PotPart
    {
        public string Name { get; }
        /// <summary>Seconds at medium heat for this part to be done.</summary>
        public double Seconds { get; }

        public PotPart(string name, double seconds)
        {
            Name = name;
            Seconds = seconds;
        }
    }

    /// <summary>
    /// A cooking recipe from the spec's cooking table. Novice to Journeyman are in; pie and banquet need an oven (later).
    /// All timings are seconds at medium heat. The player is never shown them.
    /// </summary>
    public sealed class CookRecipe
    {
        public string Id { get; }
        public string Name { get; }
        public SkillRank Rank { get; }
        public int RequiredSkill { get; }
        public int ItemLevel { get; }
        public int MethodCap { get; }
        public StationKind Station { get; }
        public FlavorBase Flavor { get; }
        /// <summary>Spit and griddle: seconds at medium heat for both sides to be done if turned evenly.</summary>
        public double CookSeconds { get; }
        /// <summary>Pot: the liquid first, then what the cook adds.</summary>
        public IReadOnlyList<PotPart> Parts { get; }
        /// <summary>Uses chopped vegetables (the chopping board comes first).</summary>
        public bool NeedsChop { get; }
        /// <summary>Pot dishes throw up scum that should be skimmed.</summary>
        public bool HasScum { get; }
        /// <summary>The roast dries out unless basted with its fat.</summary>
        public bool CanBaste { get; }
        /// <summary>The roast should rest before it is carved.</summary>
        public bool Rests { get; }
        public IReadOnlyList<CookInput> Inputs { get; }
        public string Effect { get; }
        public string Description { get; }

        public bool NeedsPot => Station == StationKind.Pot;

        public CookRecipe(string id, string name, SkillRank rank, int requiredSkill, int itemLevel, int methodCap,
            StationKind station, FlavorBase flavor, double cookSeconds, PotPart[] parts, bool needsChop, bool hasScum,
            bool canBaste, bool rests, CookInput[] inputs, string effect, string description)
        {
            Id = id;
            Name = name;
            Rank = rank;
            RequiredSkill = requiredSkill;
            ItemLevel = itemLevel;
            MethodCap = methodCap;
            Station = station;
            Flavor = flavor;
            CookSeconds = cookSeconds;
            Parts = parts ?? new PotPart[0];
            NeedsChop = needsChop;
            HasScum = hasScum;
            CanBaste = canBaste;
            Rests = rests;
            Inputs = inputs;
            Effect = effect;
            Description = description;
        }

        public static readonly CookRecipe MeatOnAStick = new CookRecipe(
            "meat_on_a_stick", "Meat on a Stick", SkillRank.Novice, 1, 1, 55,
            StationKind.Spit, FlavorBase.GameMeat, cookSeconds: 18, parts: null,
            needsChop: false, hasScum: false, canBaste: false, rests: false,
            inputs: new[] { new CookInput(Ingredient.Meat, 1, 1.0) },
            effect: "Small stamina",
            description: "Hold it over the fire and turn it.");

        public static readonly CookRecipe Flatbread = new CookRecipe(
            "flatbread", "Hearth Flatbread", SkillRank.Novice, 1, 1, 60,
            StationKind.Griddle, FlavorBase.Bread, cookSeconds: 14, parts: null,
            needsChop: false, hasScum: false, canBaste: false, rests: false,
            inputs: new[]
            {
                new CookInput(Ingredient.Flour, 2, 0.75),
                new CookInput(Ingredient.Fat, 1, 0.15),
                new CookInput(Ingredient.Salt, 1, 0.10),
            },
            effect: "Small stamina (everyday bread)",
            description: "Dough on a hot stone. Only the bottom cooks; flip it.");

        public static readonly CookRecipe SpitRoast = new CookRecipe(
            "spit_roast", "Spit Roast", SkillRank.Apprentice, 20, 8, 80,
            StationKind.Spit, FlavorBase.GameMeat, cookSeconds: 42, parts: null,
            needsChop: false, hasScum: false, canBaste: true, rests: true,
            inputs: new[]
            {
                new CookInput(Ingredient.LargeCut, 1, 0.85),
                new CookInput(Ingredient.Fat, 1, 0.15),
            },
            effect: "Medium stamina",
            description: "A large cut turned on a slow spit, basted with its fat, rested, then carved.");

        public static readonly CookRecipe BoneBroth = new CookRecipe(
            "bone_broth", "Bone Broth", SkillRank.Journeyman, 40, 14, 90,
            StationKind.Pot, FlavorBase.Bones, cookSeconds: 0,
            parts: new[] { new PotPart("bones", 48), new PotPart("vegetables", 20) },
            needsChop: true, hasScum: true, canBaste: false, rests: false,
            inputs: new[]
            {
                new CookInput(Ingredient.Bones, 2, 0.60),
                new CookInput(Ingredient.Vegetables, 1, 0.30),
                new CookInput(Ingredient.Salt, 1, 0.10),
            },
            effect: "Component (stew, gravy), small regen",
            description: "Simmer bones, skim, add the vegetables. Needs a pot.");

        public static readonly CookRecipe HuntersStew = new CookRecipe(
            "hunters_stew", "Hunter's Stew", SkillRank.Journeyman, 40, 16, 100,
            StationKind.Pot, FlavorBase.GameMeat, cookSeconds: 0,
            parts: new[] { new PotPart("broth", 55), new PotPart("meat", 38), new PotPart("vegetables", 18) },
            needsChop: true, hasScum: true, canBaste: false, rests: false,
            inputs: new[]
            {
                new CookInput(Ingredient.Broth, 1, 0.40),
                new CookInput(Ingredient.Meat, 2, 0.40),
                new CookInput(Ingredient.Vegetables, 2, 0.20),
            },
            effect: "Medium stamina + warmth (buff meal)",
            description: "Broth, then meat, then vegetables, each in its own time. Needs a pot.");

        public static readonly CookRecipe[] All = { MeatOnAStick, Flatbread, SpitRoast, BoneBroth, HuntersStew };
    }
}
