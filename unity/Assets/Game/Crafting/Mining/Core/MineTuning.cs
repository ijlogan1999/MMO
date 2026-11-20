using System;

namespace Mmo.Crafting.Mining
{
    /// <summary>
    /// Every tunable number of the mining minigame in one place.
    /// Mining is a knowledge game: no timing or aiming. Skill sharpens the miner's senses (more signs seen, more
    /// taps, earlier roof warnings) and raises the quality cap; it never adds swings.
    /// </summary>
    public static class MineTuning
    {
        // The face
        public const int Rows = 5;
        public const int Columns = 12;
        public const int LargeWidth = 3;         // blocks this wide or wider are "large" (a wedge job)
        public const double SeamOreChance = 0.8; // blocks in the seam bed that are ore (the rest are pinch-outs)
        public const double SpillOreChance = 0.15; // ore in the beds either side of the seam
        public const double LooseChance = 0.25;  // broken blocks outside the seam

        // Work
        public const int Swings = 10;            // pick, wedge, sledge and props each cost one
        public const int Props = 2;              // timber props in the kit

        // Roof
        public const double RemoveStress = 1;    // every block taken out widens the opening
        public const double TopRowStress = 1;    // extra for blocks right under the roof
        public const double SledgeStress = 1;    // extra for the sledge's shock
        public const double PropRelief = 4;
        public const double SafeStress = 8;      // above this, every swing risks a fall
        public const double CollapsePerStress = 0.08;
        public const double CollapseLoss = 0.4;  // share of the cart buried when the roof comes in

        // Pieces
        public const int SmallPieces = 3, LargePieces = 5;

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

        /// <summary>Skill 1..100 mapped to 0..1.</summary>
        public static double Skill01(int skill) => Clamp01((skill - 1) / 99.0);

        /// <summary>Taps the miner bothers with before the face is "read": 4 at skill 1, 8 at skill 100.</summary>
        public static int Taps(int skill) => 4 + (int)Math.Round(4 * Skill01(skill));

        /// <summary>Chance an ore block's true sign is noticed at all: 55% at skill 1, 95% at skill 100.</summary>
        public static double SignSeen(int skill) => Lerp(0.55, 0.95, Skill01(skill));

        public const double DecoyOnOre = 0.15;    // ore blocks can carry a misleading sign too
        public const double DecoyOnRock = 0.20;   // barren blocks with some other sign
        public const double TrueSignOnRock = 0.06; // and, rarely, the true sign where there's no ore

        /// <summary>Roof stress at which a miner first notices dust and creaks: 7 at skill 1, 4 at skill 100.</summary>
        public static double NoticeAt(int skill) => Lerp(7, 4, Skill01(skill));

        public static RoofSound RoofSoundFor(double stress) =>
            stress < 5 ? RoofSound.Solid : stress < SafeStress ? RoofSound.Drummy : RoofSound.VeryDrummy;

        /// <summary>Share of an ore block's pieces that are really ore, from the vein grade.</summary>
        public static double OreShare(int veinGrade) => 0.6 + 0.35 * Clamp01(veinGrade / 100.0);

        public static SkillRank RankFor(int skill)
        {
            if (skill >= 80) return SkillRank.Master;
            if (skill >= 60) return SkillRank.Expert;
            if (skill >= 40) return SkillRank.Journeyman;
            if (skill >= 20) return SkillRank.Apprentice;
            return SkillRank.Novice;
        }

        /// <summary>Best ore quality a miner's skill can bring out: 31 at skill 1, 100 at skill 100.</summary>
        public static int SkillCap(int skill) => (int)Math.Round(30 + 0.70 * skill);

        /// <summary>Inventory grade word for a quality number (accepted naming: grade word + quality number).</summary>
        public static string GradeWord(int quality)
        {
            if (quality >= 81) return "Superb";
            if (quality >= 61) return "Fine";
            if (quality >= 41) return "Common";
            if (quality >= 21) return "Rough";
            return "Torn";
        }

        public static string Describe(PieceColour c) => c switch
        {
            PieceColour.Rust => "rust-brown",
            PieceColour.DarkGrey => "dark grey",
            PieceColour.Black => "black",
            PieceColour.GreenBlue => "green-blue",
            PieceColour.Brass => "brassy yellow",
            PieceColour.Grey => "grey",
            PieceColour.Brown => "brown",
            _ => "white",
        };

        public static string Describe(Lustre l) => l switch
        {
            Lustre.Metallic => "metallic",
            Lustre.Glassy => "glassy",
            _ => "dull",
        };

        public static string Describe(Heft h) => h switch
        {
            Heft.Heavy => "heavy for its size",
            Heft.Light => "light for its size",
            _ => "about what you'd expect",
        };
    }
}
