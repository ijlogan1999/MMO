using System.Collections.Generic;

namespace Mmo.Crafting.Cooking
{
    /// <summary>What the cook brings to the hearth: which dish goes on each station, and with what.</summary>
    public sealed class CookSetup
    {
        public int SkillLevel = 1;
        public Hearth Hearth = Hearth.TownKitchen;
        public Cookware Cookware = Cookware.IronCookware;
        public CookWorld World = new CookWorld(1);
        /// <summary>One entry per station; null = empty. Only the first StationsFor(skill) are used.</summary>
        public CookRecipe[] Dishes = new CookRecipe[5];
        /// <summary>Quality 1..100 of each raw material.</summary>
        public readonly Dictionary<Ingredient, int> Quality = new Dictionary<Ingredient, int>();

        public CookSetup()
        {
            foreach (Ingredient i in System.Enum.GetValues(typeof(Ingredient))) Quality[i] = 50;
        }

        public int QualityOf(Ingredient i) => Quality.TryGetValue(i, out int q) ? q : 50;

        /// <summary>Why a dish can't go on the fire with this kit, or null if it can.</summary>
        public string Blocker(CookRecipe recipe)
        {
            if (recipe == null) return null;
            if (recipe.NeedsPot && !Cookware.HasPot) return "needs a pot (buy cookware from a smith)";
            return null;
        }
    }

    /// <summary>One finished dish, with every number that went into it so the player can see why.</summary>
    public sealed class DishResult
    {
        public int Slot;
        public CookRecipe Recipe;

        // Score S = Doneness x Handling x Prep
        public double Doneness;
        public double Handling = 1.0;
        public double Prep = 1.0;
        public double Score;

        // Parts of the score, for the explanation
        public double HerbScore = 1.0;
        public double ChopScore = 1.0;
        public double Scorch;          // 0..1, pots
        public double ScumLeft;        // 0..1, pots
        public double Moisture = 1.0;  // roasts
        public double Rested;          // seconds, roasts
        public double RestScore = 1.0;
        public Herb Herb;

        // Quality Q = floor + (ceiling - floor) x clamp(S + r, 0, 1)
        public int Floor;
        public int Ceiling;
        public string CeilingReason;
        public int InputMeanQuality;
        public double Roll;
        public bool Botch;
        public bool Ruined;
        public int Quality;
        public string Grade;
        public string ItemName;

        /// <summary>Plain-language notes on what happened (shown on the results screen).</summary>
        public readonly List<string> Notes = new List<string>();
        public readonly List<string> Flaws = new List<string>();
    }

    /// <summary>The whole session at the hearth.</summary>
    public sealed class CookResult
    {
        public readonly List<DishResult> Dishes = new List<DishResult>();
        public double ChopScore = 1.0;
        public bool Chopped;
        public double AverageScore;
        public int SkillGain;
    }
}
