using System;

namespace Mmo.Crafting.Cooking
{
    /// <summary>
    /// Every tunable number of the hearth in one place. Doneness is measured so that 1.0 is "done" before the world's
    /// own roll (see CookWorld). Skill widens the done band and adds stations; it never changes the game.
    /// </summary>
    public static class CookTuning
    {
        // Heat: how fast food cooks at each height over the coals.
        public static double HeatMultiplier(Heat h) => h switch
        {
            Heat.Low => 0.5,
            Heat.High => 1.7,
            _ => 1.0,
        };

        /// <summary>How fast the hearth's heat wanders (random walk speed per second).</summary>
        public const double WobbleSpeed = 0.35;

        // Two-sided food: the side facing the fire cooks fully, the side away from it barely.
        public const double SpitBackFactor = 0.20;     // heat reaching the top of a spit
        public const double GriddleBackFactor = 0.06;  // a griddle only cooks the bottom

        // Doneness
        public const double CharredAt = 1.45;      // a side past this starts to blacken (smoke)
        public const double CinderAt = 2.2;        // past this the food is ruined and comes off by itself
        public const double AccuracyFalloff = 0.45; // score drops to 0 this far outside the done band

        // Pots
        public const double ScorchGraceMedium = 7.0;  // seconds without a stir before the bottom catches
        public const double ScorchGraceHigh = 3.5;
        public const double ScorchRateMedium = 0.05;  // per second once catching
        public const double ScorchRateHigh = 0.12;
        public const double ScumRate = 0.035;         // scum thrown up per second at medium, early in the simmer
        public const double ScumFade = 25.0;          // seconds: scum comes mostly at the start
        public const double SkimAmount = 0.30;
        public const double ScumPenalty = 0.4;        // unskimmed scum (0..1) costs up to this much clarity

        // Roast
        public const double DryRateMedium = 1.0 / 80.0;  // moisture lost per second at medium heat
        public const int Bastes = 3;                     // one cut of fat basts three times
        public const double BasteAmount = 0.5;

        // Chop
        public const int Chops = 12;

        // Seasoning (spec: "salt and resting always help"; herb pairings are rolled per world)
        public const double HerbGood = 1.0;
        public const double HerbNeutral = 0.9;
        public const double HerbClash = 0.72;

        // Quality
        public const double BotchBelow = 0.15;
        public const double RollRange = 0.10;

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

        /// <summary>Skill 1..100 mapped to 0..1.</summary>
        public static double Skill01(int skill) => Clamp01((skill - 1) / 99.0);

        /// <summary>Half-width of the done band as a fraction of done: ±6% at skill 1, ±12% at skill 100.</summary>
        public static double DoneTolerance(int skill) => Lerp(0.06, 0.12, Skill01(skill));

        /// <summary>How uneven a chopping rhythm can be (spread of the gaps between chops) and still count as even.</summary>
        public static double ChopTolerance(int skill) => Lerp(0.08, 0.15, Skill01(skill));

        /// <summary>Spec: 1 to 2 stations at the start, up to 5 for a master.</summary>
        public static int StationsFor(int skill)
        {
            if (skill >= 80) return 5;
            if (skill >= 60) return 4;
            if (skill >= 20) return 3;
            return 2;
        }

        public static SkillRank RankFor(int skill)
        {
            if (skill >= 80) return SkillRank.Master;
            if (skill >= 60) return SkillRank.Expert;
            if (skill >= 40) return SkillRank.Journeyman;
            if (skill >= 20) return SkillRank.Apprentice;
            return SkillRank.Novice;
        }

        /// <summary>Quality floor from skill: 5 at skill 1, 45 at skill 100 (same as the forge).</summary>
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

        /// <summary>1 inside the done band, falling off on either side. Raw is bad, burnt is bad.</summary>
        public static double DonenessScore(double doneness, double target, int skill)
        {
            double err = Math.Abs(doneness / target - 1.0);
            double tol = DoneTolerance(skill);
            if (err <= tol) return 1.0;
            return Clamp01(1.0 - (err - tol) / AccuracyFalloff);
        }

        /// <summary>A word for how cooked something is, relative to this world's done point.</summary>
        public static string DonenessWord(double doneness, double target, int skill)
        {
            double r = doneness / target;
            double tol = DoneTolerance(skill);
            if (r < 0.55) return "raw";
            if (r < 1 - tol - 0.15) return "underdone";
            if (r < 1 - tol) return "a touch underdone";
            if (r <= 1 + tol) return "just right";
            if (r <= 1 + tol + 0.15) return "a touch overdone";
            if (r < CharredAt) return "overdone";
            return "charred";
        }

        /// <summary>Chop evenness from the gaps between chops (coefficient of variation).</summary>
        public static double ChopScore(double spread, int skill)
        {
            double tol = ChopTolerance(skill);
            if (spread <= tol) return 1.0;
            return Clamp01(1.0 - (spread - tol) * 2.5);
        }

        /// <summary>Resting a roast: too short and the juices run, a little long is fine, far too long and it goes cold.</summary>
        public static double RestScore(double rested, double target)
        {
            if (rested < target) return 0.72 + 0.28 * Clamp01(rested / target);
            if (rested <= target + 4) return 1.0;
            return Math.Max(0.85, 1.0 - (rested - target - 4) * 0.02);
        }

        public static double HerbScore(Herb herb, Herb good, Herb clash)
        {
            if (herb == Herb.None) return HerbNeutral;
            if (herb == good) return HerbGood;
            if (herb == clash) return HerbClash;
            return HerbNeutral;
        }

        public static string HerbName(Herb h) => h switch
        {
            Herb.WildGarlic => "Wild garlic",
            Herb.None => "No herb",
            _ => h.ToString(),
        };

        public static string IngredientName(Ingredient i) => i switch
        {
            Ingredient.LargeCut => "Large cut",
            _ => i.ToString(),
        };
    }
}
