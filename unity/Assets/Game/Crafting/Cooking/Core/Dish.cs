namespace Mmo.Crafting.Cooking
{
    /// <summary>
    /// One dish on one station while it cooks. Everything here is hidden from the player except through how the food
    /// looks and sounds: colour, smoke, bubbles, scum and sheen. No bars or numbers.
    /// </summary>
    public sealed class Dish
    {
        public int Slot { get; }
        public CookRecipe Recipe { get; }
        /// <summary>Doneness this world counts as done for this recipe.</summary>
        public double Target { get; }
        public Herb Herb { get; internal set; }
        public Heat Heat { get; internal set; } = Heat.Medium;
        public DishState State { get; internal set; } = DishState.Cooking;
        public bool Ruined { get; internal set; }

        /// <summary>Spit and griddle: doneness of side 0 and side 1.</summary>
        public double[] Sides { get; } = new double[2];
        /// <summary>Which side faces the fire (spit) or the stone (griddle).</summary>
        public int Facing { get; internal set; }
        public int Turns { get; internal set; }

        /// <summary>Pot: doneness of each part (liquid first). Parts not yet added stay at 0.</summary>
        public double[] Parts { get; }
        public int PartsAdded { get; internal set; }

        public double Scorch { get; internal set; }
        public double Scum { get; internal set; }
        public double SinceStir { get; internal set; }
        public double Moisture { get; internal set; } = 1.0;
        public int BastesLeft { get; internal set; }
        public double RestTime { get; internal set; }
        public double CookTime { get; internal set; }
        /// <summary>How far the hearth's heat has wandered from the set heat right now (-wobble..+wobble).</summary>
        public double Wobble { get; internal set; }

        internal bool CatchingSaid, CharringSaid;

        public bool IsPot => Recipe.Station == StationKind.Pot;
        public bool TwoSided => !IsPot;
        public bool AllPartsIn => !IsPot || PartsAdded >= Recipe.Parts.Count;
        public string NextPartName => IsPot && !AllPartsIn ? Recipe.Parts[PartsAdded].Name : null;

        public double EffectiveHeat => CookTuning.HeatMultiplier(Heat) * (1 + Wobble);

        /// <summary>Overall doneness relative to done (1 = done on this world). For drawing only.</summary>
        public double Look => (IsPot ? Parts[0] : (Sides[0] + Sides[1]) / 2) / Target;

        public Dish(int slot, CookRecipe recipe, double target)
        {
            Slot = slot;
            Recipe = recipe;
            Target = target;
            Parts = new double[recipe.Parts.Count];
            PartsAdded = recipe.Parts.Count > 0 ? 1 : 0;
            BastesLeft = recipe.CanBaste ? CookTuning.Bastes : 0;
        }
    }
}
