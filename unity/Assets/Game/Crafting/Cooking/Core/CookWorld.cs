using System;

namespace Mmo.Crafting.Cooking
{
    /// <summary>
    /// The cooking secrets each world rolls once (spec, "Rolled per world"): where each dish's done point sits
    /// (±10%), how long a roast should rest (1 to 5 counts), and which wild herb suits which food.
    /// Players learn these by cooking and sharing notes. Fixed facts never change: salt and resting always help.
    /// </summary>
    public sealed class CookWorld
    {
        public int Seed { get; }

        public CookWorld(int seed)
        {
            Seed = seed;
        }

        private Random Rng(string key) => new Random(unchecked(Seed * 31 + StableHash(key)));

        /// <summary>Doneness this world counts as done for a recipe: 0.9 to 1.1 of the base.</summary>
        public double DoneTarget(CookRecipe recipe) => 0.9 + Rng("done:" + recipe.Id).NextDouble() * 0.2;

        /// <summary>Counts (seconds) a roast should rest before carving: 1 to 5.</summary>
        public double RestCounts => 1 + Rng("rest").NextDouble() * 4;

        /// <summary>The herb that suits this food on this world, and the one that clashes with it.</summary>
        public void HerbPairing(FlavorBase flavor, out Herb good, out Herb clash)
        {
            var rng = Rng("herb:" + flavor);
            var herbs = new[] { Herb.Thyme, Herb.WildGarlic, Herb.Juniper, Herb.Sage };
            int g = rng.Next(herbs.Length);
            int c = (g + 1 + rng.Next(herbs.Length - 1)) % herbs.Length;
            good = herbs[g];
            clash = herbs[c];
        }

        /// <summary>FNV-1a. string.GetHashCode changes between runs, which would reroll the world.</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s) { h ^= ch; h *= 16777619; }
                return (int)h;
            }
        }
    }
}
