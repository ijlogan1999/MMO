using System.Collections.Generic;

namespace Mmo.Crafting.Skinning
{
    /// <summary>What came off the carcass, with every number that went into it so the player can see why.</summary>
    public sealed class SkinResult
    {
        // Skinning
        public SkinStyle Style;
        public bool TubeKept;
        public int Patches;
        public int Clean, Stretched, Scored, Holed, Torn, Left;
        public int WoundsFound;   // wounds the skinner saw or felt before working the patch
        public int WoundsAbsorbed; // wounds the opening cuts ran through
        public int Hones;
        public int TimeUsed;      // units of work
        public int GutAt;         // when the body was gutted (units of work in)
        public int SpoilPoint;    // when the belly would sour
        public bool GuttedLate => GutAt > SpoilPoint;

        // Fleshing
        public int Scrapes, Trims, Skids;
        public int LayerClean, LayerMembrane, LayerFat, LayerMeat;
        public int Thinned, ScoredOnBeam, TornOnBeam;

        // Keeping
        public Preserve Preserve;

        public double HideShare;  // share of the hide taken off
        public double SkinScore;  // mean patch value over the whole hide
        public double FleshScore; // mean layer x mark value over the patches taken off
        public double HideScore;  // skin x flesh
        public double StyleFactor;
        public double PreserveFactor;
        public double SeasonHide;
        public double MeatFactor;
        public double SeasonMeat;

        // Quality = grade x (0.4 + 0.6 x hide score) x style x keeping x season, capped by knife and skill.
        public int AnimalGrade;
        public int HideRaw;
        public int Ceiling;
        public string CeilingReason;
        public int HideQuality;   // 0 = scraps
        public string HideGrade;
        public string HideItem;
        public double HideUnits;

        public int MeatYield;
        public int MeatRaw;
        public int MeatQuality;
        public string MeatGrade;
        public string MeatItem;   // "" when the animal gives no meat

        public string Extras;
        public int SkillGain;

        /// <summary>Lessons, one line each: what went wrong where, in plain words.</summary>
        public readonly List<string> Notes = new List<string>();
    }
}
