using System;

namespace Mmo.Crafting.Mining
{
    /// <summary>
    /// Every tunable number of the mining minigame in one place.
    /// Skill and pick widen the windows; they never change the game.
    /// </summary>
    public static class MineTuning
    {
        // The rock face, in face units (1 across).
        public const double FaceHeight = 0.6;
        public const double Margin = 0.06;            // weak points stay this far inside the edge

        // Seam
        public const double SeamFade = 2.5;           // ore fades to a trace this many half-widths outside the band
        public const double StrayOre = 0.10;          // even barren rock holds a trace
        public const double MaxSeamTilt = 0.6;        // radians (~35 degrees) either way from level

        // Weak points: a walk from the miner's mark, following the grain of the rock (parallel to the seam).
        public const double StepMin = 0.075, StepMax = 0.11;
        public const double StepJitter = 0.35;        // radians off the grain per step
        public const double DriftAcross = 0.012;      // small sideways wander per step
        public const double KeepDirection = 0.75;     // chance the walk keeps going the same way along the grain

        // Timing
        public const double LeadIn = 1.0;             // pause after marking before the first crack opens
        public const double PauseMin = 0.55, PauseMax = 1.15; // rock settles between weak points
        public const double SwingRecovery = 0.22;     // a pick can't swing faster than this

        // Quality
        public const double CrushPerFracture = 1.0;   // fractures count fully against how cleanly the rock split

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

        /// <summary>Skill 1..100 mapped to 0..1.</summary>
        public static double Skill01(int skill) => Clamp01((skill - 1) / 99.0);

        /// <summary>How long a weak point stays open: 1.8 s at skill 1, 3.2 s at 100, times pick and rock.
        /// (The spec's 0.6-1.5 s felt far too fast in play, Ian 2026-11-20.)</summary>
        public static double OpenSeconds(int skill, Pick pick, OreNodeType node) =>
            Lerp(1.8, 3.2, Skill01(skill)) * pick.WindowMultiplier * node.Softness;

        /// <summary>How close to a weak point a blow must land to split it (face units).</summary>
        public static double HitRadius(int skill, Pick pick) => Lerp(0.032, 0.055, Skill01(skill)) * pick.ReachMultiplier;

        /// <summary>A practised eye follows a seam a little further: the band counts as slightly wider.</summary>
        public static double SeamHalfWidth(OreNodeType node, int skill) => node.BaseSeamHalfWidth * Lerp(1.0, 1.4, Skill01(skill));

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
    }
}
