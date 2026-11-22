using System;

namespace Mmo.Crafting.Logging
{
    /// <summary>
    /// Every tunable number of the logging minigame in one place.
    /// Logging is a knowledge game: no timing or aiming. Skill sharpens the logger's senses (more rot signs noticed,
    /// more knocks, a truer eye for height) and raises the quality cap; it never adds strength.
    /// </summary>
    public static class LogTuning
    {
        // The clearing (metres)
        public const double ClearingW = 54, ClearingH = 38;
        public const int Trees = 5;
        public const double TreeSpacing = 7, TreeMargin = 4;
        public const int Rocks = 4;

        // Strength: every bite, saw stroke, wedge blow, cut, peel and split spends some.
        public const double Strength = 100;
        /// <summary>The cut through the trunk is measured in twentieths of its width.</summary>
        public const int CutSteps = 20;
        public static double StepCost(int diameterCm) => diameterCm / 25.0;
        public static double BuckCost(int diameterCm) => diameterCm / 12.0;
        public const double WedgeCost = 2;
        public const double PeelCost = 3;
        public const double SplitCost = 2;   // per bundle of firewood
        public const double StackCost = 1;   // rolling a length onto the timber stack
        public const double FreeHangUpCost = 15;
        public const double BuckPinchCost = 5;

        // The notch and the hinge (in steps of 1/20 of the trunk)
        public const int NotchMin = 5, NotchMax = 7; // a quarter to a third of the way through
        public const int MaxNotch = 12;
        public const int WedgeRoom = 3;              // back cut this deep before a wedge fits in the kerf
        public const int MaxWedges = 5;
        public const double WedgePush = 1.0;
        /// <summary>The hinge holds the tree up until the forward pull (lean, crown, wind, wedges) beats this.</summary>
        public static double HingeHold(int hingeSteps) => 0.8 * hingeSteps;
        public const int ThickHinge = 4;             // falling on a hinge this thick or thicker tears fibre out of the butt
        public const int SitBackDepth = 5;           // a tree that leans back sits on the saw once the back cut is this deep
        public const double BarberChairPull = 2;     // forward pull at which a shallow notch lets the trunk split up
        public const double LevelSlideChance = 0.3;  // back cut level with the notch: no step to stop the butt sliding back

        // Pull on the tree
        public const double CrownWeight = 0.5, WindWeight = 0.5;
        public const double DriftPull = 1.0;         // pull this strong drags a badly hinged tree off its line

        // The fall
        public const double HangUpReach = 0.7;       // share of a neighbour's crown radius a falling stem catches on
        public const int HangUpBreaks = 3;           // metres of the top that break when it's pulled down
        public const double StemShare = 0.7;         // usable stem as a share of height
        public const double EdgeReach = 0.85;        // share of the height that catches in the forest edge (the very top just brushes it)

        // Timber
        public const int TimberLength = 4;           // the shortest length a buyer takes as timber
        public const int CrownFirewood = 2;          // bundles from splitting the crown and limbs
        public const double KnotPenalty = 0.08;      // per knot per metre, averaged over the length
        public const double SplitEndFactor = 0.8;
        public const double FibrePullFactor = 0.8;

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

        /// <summary>Skill 1..100 mapped to 0..1.</summary>
        public static double Skill01(int skill) => Clamp01((skill - 1) / 99.0);

        /// <summary>Knocks with the back of the axe: 4 at skill 1, 8 at skill 100.</summary>
        public static int Taps(int skill) => 4 + (int)Math.Round(4 * Skill01(skill));

        /// <summary>Chance a true sign of rot is noticed: 50% at skill 1, 95% at skill 100.</summary>
        public static double SignSeen(int skill) => Lerp(0.5, 0.95, Skill01(skill));

        /// <summary>Spread of the eye when sighting a tree's height: ±15% at skill 1, ±3% at skill 100 (one sigma).</summary>
        public static double SightSpread(int skill) => Lerp(0.15, 0.03, Skill01(skill));

        /// <summary>Best timber quality a logger's skill can bring out: 31 at skill 1, 100 at skill 100.</summary>
        public static int SkillCap(int skill) => (int)Math.Round(30 + 0.70 * skill);

        public static SkillRank RankFor(int skill)
        {
            if (skill >= 80) return SkillRank.Master;
            if (skill >= 60) return SkillRank.Expert;
            if (skill >= 40) return SkillRank.Journeyman;
            if (skill >= 20) return SkillRank.Apprentice;
            return SkillRank.Novice;
        }

        /// <summary>Inventory grade word for a quality number (accepted naming: grade word + quality number).</summary>
        public static string GradeWord(int quality)
        {
            if (quality >= 81) return "Superb";
            if (quality >= 61) return "Fine";
            if (quality >= 41) return "Common";
            if (quality >= 21) return "Rough";
            return "Torn";
        }

        // ------------------------------------------------------------------ directions (0 = north, clockwise)

        public static readonly string[] DirNames = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        /// <summary>Unit vector for a direction, x east and y south (screen space).</summary>
        public static (double X, double Y) Vec(int dir)
        {
            double a = Wrap(dir) * Math.PI / 4;
            return (Math.Sin(a), -Math.Cos(a));
        }

        public static int Wrap(int dir) => ((dir % 8) + 8) % 8;

        /// <summary>Steps between two directions, 0..4.</summary>
        public static int DirDiff(int a, int b)
        {
            int d = Math.Abs(Wrap(a) - Wrap(b));
            return Math.Min(d, 8 - d);
        }

        /// <summary>The nearest of the eight directions to a vector.</summary>
        public static int DirOf(double x, double y)
        {
            double a = Math.Atan2(x, -y); // 0 at north, clockwise
            return Wrap((int)Math.Round(a / (Math.PI / 4)));
        }

        /// <summary>One step from <paramref name="from"/> toward <paramref name="to"/>.</summary>
        public static int StepToward(int from, int to)
        {
            int d = Wrap(to - from);
            if (d == 0) return Wrap(from);
            return Wrap(from + (d <= 4 ? 1 : -1));
        }

        public static string Describe(TreeSign s) => s switch
        {
            TreeSign.Conks => "bracket fungus (conks) on the trunk",
            TreeSign.WoodpeckerHoles => "woodpecker holes",
            TreeSign.DeadTop => "a dead, bare top",
            TreeSign.Moss => "moss on the bark",
            TreeSign.Ivy => "ivy up the trunk",
            _ => "a burl (a big knobbly swelling)",
        };

        public static bool MeansRot(TreeSign s) => s == TreeSign.Conks || s == TreeSign.WoodpeckerHoles || s == TreeSign.DeadTop;
    }
}
