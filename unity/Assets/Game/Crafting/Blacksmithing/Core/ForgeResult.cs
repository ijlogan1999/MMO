using System.Collections.Generic;

namespace Mmo.Crafting.Blacksmithing
{
    /// <summary>What the player brings to the anvil.</summary>
    public sealed class ForgeSetup
    {
        public ForgeRecipe Recipe;
        public int SkillLevel = 1;
        public ForgeStation Station = ForgeStation.TownForge;
        public Hammer Hammer = Hammer.IronHammer;
        /// <summary>Quality (1..100) of each recipe input, same order as Recipe.Inputs.</summary>
        public int[] InputQualities;

        public ForgeSetup(ForgeRecipe recipe)
        {
            Recipe = recipe;
            InputQualities = new int[recipe.Inputs.Count];
            for (int i = 0; i < InputQualities.Length; i++) InputQualities[i] = 50;
        }
    }

    /// <summary>The finished craft, with every number that went into it so the player can see why.</summary>
    public sealed class ForgeResult
    {
        // Minigame score S = Shape x Accuracy x HeatControl
        public double Shape;
        public double Accuracy;
        public double HeatControl;
        public double HeatPullAverage;
        public double QuenchScore = 1.0;
        public double FinishScore;
        public double Score;

        // Quality Q = floor + (ceiling - floor) x clamp(S + r, 0, 1)
        public int Floor;
        public int Ceiling;
        public string CeilingReason;
        public int InputMeanQuality;
        public double Roll;
        public bool Botch;
        public int Quality;
        public string Grade;
        public string ItemName;
        public int ItemLevel;

        public int Perfect;
        public int Good;
        public int BeatsUnused;
        public int OffBeat;
        public int Cold;
        public int Reheats;

        public int SkillGain;
        public readonly List<string> Traits = new List<string>();
        public readonly List<string> Flaws = new List<string>();
    }
}
