using System;

namespace Mmo.Crafting.Blacksmithing
{
    /// <summary>
    /// Every tunable number of the forge in one place.
    /// Skill widens the windows; it never changes the game (spec: "a master plays the same forge as a novice but with more room").
    /// </summary>
    public static class ForgeTuning
    {
        // Heat (temperature is 0..1, above 1 is burning)
        public const double StartHeat = 0.05;
        public const double HeatRatePerSecond = 0.15;
        public const double CoolRatePerSecond = 0.035;
        public const double BurnLimit = 1.12;       // bar is pulled automatically (burned) here
        public const double WorkMin = 0.25;         // below cherry, strikes stop counting
        public const double CrackBelow = 0.25;      // pulled this cold, the bar cracks
        public const int FreeReheats = 2;           // each reheat after the second costs quality
        public const double ReheatPenalty = 0.95;   // per reheat beyond the free ones

        // Strike
        public const double LeadInBeats = 2;
        public const double GoodWindowFactor = 2.2;
        public const double PerfectProgress = 1.0;
        public const double GoodProgress = 0.85;
        public const double GoodAccuracy = 0.6;
        public const double OffBeatDent = 0.02;     // shape lost per off-beat strike
        public const double StrikeEndDelay = 0.6;

        // Finish
        public const int FinishStrokes = 3;
        public const double FinishCursorSpeed = 1.4; // sweeps per second across the bar

        // Quality
        public const double BotchBelow = 0.15;
        public const double RollRange = 0.10;

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

        /// <summary>Skill 1..100 mapped to 0..1.</summary>
        public static double Skill01(int skill) => Clamp01((skill - 1) / 99.0);

        /// <summary>Perfect strike window in seconds either side of the beat: ±60 ms at skill 1, ±140 ms at 100.</summary>
        public static double PerfectWindow(int skill, Hammer hammer) =>
            Lerp(0.060, 0.140, Skill01(skill)) * hammer.WindowMultiplier;

        public static double GoodWindow(int skill, Hammer hammer) => PerfectWindow(skill, hammer) * GoodWindowFactor;

        /// <summary>How far from the target heat a pull still counts as perfect.</summary>
        public static double HeatTolerance(int skill) => Lerp(0.05, 0.12, Skill01(skill));

        public static HeatBand BandFor(double t)
        {
            if (t > 1.0) return HeatBand.Burning;
            if (t >= 0.85) return HeatBand.White;
            if (t >= 0.65) return HeatBand.Yellow;
            if (t >= 0.45) return HeatBand.Orange;
            if (t >= 0.25) return HeatBand.Cherry;
            if (t >= 0.10) return HeatBand.DullRed;
            return HeatBand.Black;
        }

        /// <summary>Score for pulling the bar at temperature t when the recipe wants target.</summary>
        public static double HeatPullScore(double t, double target, int skill, out string flaw)
        {
            flaw = null;
            if (t < CrackBelow) { flaw = "Cracked (pulled too cold)"; return 0.3; }
            if (t > 1.0) { flaw = "Burned (pulled too hot)"; return 0.35; }
            double err = Math.Abs(t - target);
            double tol = HeatTolerance(skill);
            if (err <= tol) return 1.0;
            return Math.Max(0.5, 1.0 - (err - tol) * 2.5);
        }

        public static SkillRank RankFor(int skill)
        {
            if (skill >= 80) return SkillRank.Master;
            if (skill >= 60) return SkillRank.Expert;
            if (skill >= 40) return SkillRank.Journeyman;
            if (skill >= 20) return SkillRank.Apprentice;
            return SkillRank.Novice;
        }

        /// <summary>Quality floor from skill (the method's own floor is folded in: 5 at skill 1, 45 at skill 100).</summary>
        public static int SkillFloor(int skill) => (int)Math.Round(5 + 0.40 * skill);

        /// <summary>Inventory grade word for a quality number (accepted naming: grade word + quality number).</summary>
        public static string GradeWord(int quality)
        {
            if (quality >= 81) return "Superb";
            if (quality >= 61) return "Fine";
            if (quality >= 41) return "Common";
            if (quality >= 21) return "Rough";
            return "Torn";
        }

        // Quench: water is harsh and fast, oil is gentle and slow, brine sits between.
        public static double QuenchTargetSeconds(QuenchMedium m) => m switch
        {
            QuenchMedium.Water => 1.5,
            QuenchMedium.Brine => 2.0,
            _ => 3.0,
        };

        public static double QuenchTolerance(QuenchMedium m, int skill)
        {
            double baseTol = m switch
            {
                QuenchMedium.Water => 0.15,
                QuenchMedium.Brine => 0.25,
                _ => 0.40,
            };
            return baseTol * Lerp(1.0, 1.8, Skill01(skill));
        }

        /// <summary>Best quench score each medium can give (harsher media harden more).</summary>
        public static double QuenchMax(QuenchMedium m) => m switch
        {
            QuenchMedium.Water => 1.0,
            QuenchMedium.Brine => 0.95,
            _ => 0.88,
        };

        /// <summary>Chance the blade cracks. Always some risk with water; holding too long makes it much worse.</summary>
        public static double QuenchCrackChance(QuenchMedium m, int skill, bool heldTooLong)
        {
            double s = Skill01(skill);
            double baseChance = m switch
            {
                QuenchMedium.Water => Lerp(0.20, 0.04, s),
                QuenchMedium.Brine => Lerp(0.08, 0.01, s),
                _ => 0.0,
            };
            double extra = !heldTooLong ? 0 : m switch
            {
                QuenchMedium.Water => 0.30,
                QuenchMedium.Brine => 0.12,
                _ => 0.03,
            };
            return baseChance + extra;
        }

        public static string QuenchDescription(QuenchMedium m) => m switch
        {
            QuenchMedium.Water => "Water: hardest blade, short count (1.5 s), real crack risk.",
            QuenchMedium.Brine => "Brine: middle ground (2.0 s), small crack risk.",
            _ => "Oil: gentle and safe (3.0 s), a little less hard.",
        };

        public static double FinishSweetWidth(int skill) => Lerp(0.08, 0.18, Skill01(skill));

        /// <summary>Chance per trait roll; the finish score and skill both raise it.</summary>
        public static double TraitChance(double finishScore, int skill) => finishScore * Lerp(0.15, 0.60, Skill01(skill));

        public static int MaxTraits(int skill) => skill >= 60 ? 2 : 1;

        public static readonly string[] Traits =
        {
            "+3% Sharpness",
            "+3% Durability",
            "+2% Balance",
            "+3% Edge retention",
        };
    }
}
