using System;

namespace Mmo.Crafting.Skinning
{
    /// <summary>
    /// Every tunable number of the skinning minigame in one place.
    /// Skinning is a knowledge game: no timing or aiming. Time passes only with work done (each pull, cut, hone or
    /// gutting), never on a clock. Skill sharpens the senses (more feels, wounds and warnings noticed earlier) and
    /// raises the quality cap; the knife raises the cap and holds its edge longer. Neither adds yield.
    /// </summary>
    public static class SkinTuning
    {
        // The hide grid (laid out flat): 6 rows x 5 columns, legs at the corners, the belly line down column 2.
        public const int Rows = 6;
        public const int Columns = 5;

        // Work costs, in units of time.
        public const int PullCost = 1;
        public const int KnifeCost = 2;
        public const int HoneCost = 1;
        public const int GutCost = 3;

        // Cooling: warmth starts at 1 and falls by the species' rate x the season's factor per unit of work.
        public const double ClingBelow = 0.45; // below this warmth, every patch holds one step harder
        public const double SetBelow = 0.12;   // below this the carcass has set: two steps harder, and knife work drags meat with it

        public static double SeasonCooling(Season s) => s switch
        {
            Season.Summer => 0.6,
            Season.Winter => 1.6,
            _ => 1.0,
        };

        // Meat: an ungutted body sours after its species' time, shifted by season.
        public static int SeasonSpoilShift(Season s) => s switch
        {
            Season.Summer => -2,
            Season.Winter => 3,
            _ => 0,
        };

        public const double SpoilPerUnit = 0.07; // meat quality factor lost per unit of work past the spoil point
        public const double SpoilFloor = 0.35;

        // The knife edge: starts at 1, loses the knife's dull rate per stroke (x2 through fat), honed back to 1.
        public const double DullBelow = 0.35; // below this a stroke tears or scores instead of cutting clean
        public const double FatDull = 2.0;

        /// <summary>Edge at which the skinner notices the knife dragging: just as it goes dull at skill 1, well before at 100.</summary>
        public static double DragNoticeAt(int skill) => Lerp(DullBelow, 0.6, Skill01(skill));

        /// <summary>Units of work before the spoil point at which the skinner notices the belly swelling: 0 at skill 1, 3 at 100.</summary>
        public static int SwellNoticeLead(int skill) => (int)Math.Round(3 * Skill01(skill));

        /// <summary>Feels the skinner bothers with: 5 at skill 1, 11 at skill 100.</summary>
        public static int Feels(int skill) => 5 + (int)Math.Round(6 * Skill01(skill));

        /// <summary>Chance a wound shows through the fur at a glance: 45% at skill 1, 90% at skill 100.</summary>
        public static double WoundSeen(int skill) => Lerp(0.45, 0.9, Skill01(skill));

        /// <summary>Chance a patch differs from its species' usual hold by one step (individual animals vary).</summary>
        public const double HoldVariance = 0.2;

        // What each outcome leaves of a patch's worth.
        public static double PatchValue(PatchState s) => s switch
        {
            PatchState.Clean => 1.0,
            PatchState.Stretched => 0.8,
            PatchState.Scored => 0.6,
            PatchState.Holed => 0.65,
            PatchState.Torn => 0.15,
            _ => 0.0,
        };

        public static double LayerValue(Layer l) => l switch
        {
            Layer.Clean => 1.0,
            Layer.Membrane => 0.85, // a film left on: tans unevenly
            Layer.Fat => 0.6,       // grease: it rots and the tan won't take
            _ => 0.5,               // meat left on: it rots
        };

        public static double MarkValue(FleshMark m) => m switch
        {
            FleshMark.Thinned => 0.8,
            FleshMark.Scored => 0.7,
            FleshMark.Torn => 0.25,
            _ => 1.0,
        };

        /// <summary>Below this share of the hide taken off, there's no hide, only scraps.</summary>
        public const double MinHideShare = 0.5;

        /// <summary>Skinning style: the right way for the animal is 1.0.</summary>
        public static double StyleFactor(Species sp, SkinStyle style, bool tubeKept)
        {
            if (style == SkinStyle.Cased && !sp.CanCase) return 0.7; // too big to case: the tube tears over the shoulders
            if (sp.FurBearer) return tubeKept ? 1.0 : 0.88;          // a furrier pays for a whole tube
            return 1.0;
        }

        /// <summary>How each way of keeping the hide fares by season. Leather hides want salt; pelts want the stretcher.</summary>
        public static double PreserveFactor(Species sp, Preserve p, Season s)
        {
            switch (p)
            {
                case Preserve.Salt:
                    return sp.Pelt ? 0.9 : 1.0; // salt keeps anything, but stiffens fur
                case Preserve.Stretch:
                    if (s == Season.Summer) return sp.Pelt ? 0.85 : 0.7; // summer heat loosens the hair
                    return sp.Pelt ? 1.0 : 0.88;                          // a leather hide dries flint-hard
                case Preserve.Green:
                    return s switch
                    {
                        Season.Winter => 1.0,  // cold keeps it until the tanner gets it
                        Season.Summer => 0.6,  // it turns before the day is out
                        _ => 0.85,
                    };
                default:
                    return 0.6;
            }
        }

        /// <summary>Season nudges what the same animal gives (spec: about 10%, never a gate).</summary>
        public static double SeasonHide(Season s) => s == Season.Winter ? 1.1 : 1.0;
        public static double SeasonMeat(Season s) => s == Season.Autumn ? 1.1 : s == Season.Summer ? 0.9 : 1.0;

        /// <summary>Inventory name adjective, only when the season changed the material.</summary>
        public static string HideSeasonWord(Season s) => s == Season.Winter ? "Winter " : "";
        public static string MeatSeasonWord(Season s) => s == Season.Autumn ? "Autumn " : s == Season.Summer ? "Summer " : "";

        public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

        /// <summary>Skill 1..100 mapped to 0..1.</summary>
        public static double Skill01(int skill) => Clamp01((skill - 1) / 99.0);

        public static SkillRank RankFor(int skill)
        {
            if (skill >= 80) return SkillRank.Master;
            if (skill >= 60) return SkillRank.Expert;
            if (skill >= 40) return SkillRank.Journeyman;
            if (skill >= 20) return SkillRank.Apprentice;
            return SkillRank.Novice;
        }

        /// <summary>Best quality a skinner's skill can bring out: 31 at skill 1, 100 at skill 100 (same as mining).</summary>
        public static int SkillCap(int skill) => (int)Math.Round(30 + 0.70 * skill);

        /// <summary>Grade word from the spec's inventory naming: Torn 1-19, Rough 20-39, Common 40-59, Fine 60-79, Superb 80-100.</summary>
        public static string GradeWord(int quality)
        {
            if (quality >= 80) return "Superb";
            if (quality >= 60) return "Fine";
            if (quality >= 40) return "Common";
            if (quality >= 20) return "Rough";
            return "Torn";
        }

        public static string Describe(Hold h) => h switch
        {
            Hold.Loose => "slides under your fingers",
            Hold.Clinging => "clings",
            _ => "is held tight",
        };

        public static string Describe(Region r) => r switch
        {
            Region.Foreleg => "foreleg",
            Region.Hindleg => "hind leg",
            _ => r.ToString().ToLowerInvariant(),
        };
    }
}
