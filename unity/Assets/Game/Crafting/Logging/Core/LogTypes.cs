// Pure C# (no UnityEngine) so the same rules can run on the game server and in tests.
using System.Collections.Generic;

namespace Mmo.Crafting.Logging
{
    /// <summary>
    /// Survey: look the stand over and pick a tree. Aim: choose where it should fall. Notch: chop the face notch.
    /// BackCut: saw from the far side (and drive wedges) until it goes. Buck: cut the fallen stem into lengths.
    /// Yard: decide what each length is (timber, peeled timber, firewood, or leave it). Done: results.
    /// </summary>
    public enum LogStage { Survey, Aim, Notch, BackCut, Buck, Yard, Done }

    /// <summary>Skill ranks from the crafting spec (skill 1-100).</summary>
    public enum SkillRank { Novice, Apprentice, Journeyman, Expert, Master }

    public enum Species { Pine, Birch, Oak }

    /// <summary>Rot inside the trunk. It starts at the butt and runs up the stem.</summary>
    public enum RotKind { None, HeartRot, Hollow }

    /// <summary>What knocking the trunk with the back of the axe sounds like.</summary>
    public enum TrunkSound { None, Ring, Dull, Boom }

    /// <summary>What can be seen on a standing tree. Some mean rot, some mean nothing.</summary>
    public enum TreeSign { Conks, WoodpeckerHoles, DeadTop, Moss, Ivy, Burl }

    /// <summary>Where the back cut goes, relative to the floor of the notch on the other side.</summary>
    public enum BackCutHeight { Below = -1, Level = 0, Step = 1, High = 2 }

    /// <summary>Which side of the log a bucking cut is sawn from.</summary>
    public enum BuckSide { Top, Under }

    /// <summary>What becomes of a cut length in the yard.</summary>
    public enum PieceUse { Leave, Timber, PeeledTimber, Firewood }

    public enum LogEventKind
    {
        Selected,
        Tapped,
        Sighted,
        Aimed,
        Chopped,      // one bite deeper into the notch
        Sawed,        // one stroke deeper into the back cut
        Wedged,
        Pinched,      // the tree sat back on the saw
        Creak,        // the hinge is working: it's about to go
        Fell,         // it's down (message says how)
        HungUp,
        Cracked,
        Kickback,
        BarberChair,
        Bucked,
        BuckPinched,
        HitDirt,
        TooTired,
        Refused,      // something that can't be done (too short for timber, already cut here...)
        StageChanged,
        Done,
    }

    public struct LogEvent
    {
        public LogEventKind Kind;
        public string Message;
        public int Index;

        public LogEvent(LogEventKind kind, string message, int index = -1)
        {
            Kind = kind;
            Message = message;
            Index = index;
        }

        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>A rock in the clearing (metres). A falling stem that lands across one cracks there.</summary>
    public sealed class Rock
    {
        public double X { get; }
        public double Y { get; }
        public double R { get; }

        public Rock(double x, double y, double r)
        {
            X = x;
            Y = y;
            R = r;
        }
    }

    /// <summary>
    /// One standing tree. Positions are metres in the clearing (x east, y south). Directions are 0..7 clockwise
    /// from north. Everything the player can't see (rot, grade) is still here, so the results screen can show it.
    /// </summary>
    public sealed class Tree
    {
        public int Id { get; }
        public double X { get; }
        public double Y { get; }
        public double Height { get; }      // metres
        public int Diameter { get; }       // centimetres at chest height
        public double CrownRadius { get; } // metres
        public int LeanDir { get; }
        public int LeanStrength { get; }   // 0 straight, 1 slight, 2 heavy
        public int CrownDir { get; }
        public int CrownStrength { get; }  // 0 even crown, 1 heavy on one side
        public RotKind Rot { get; }
        public int RotLength { get; }      // metres of stem the rot runs up from the butt
        public int CrookAt { get; }        // metre of stem with a bend in it, -1 for none
        public bool LowBranches { get; }   // grew in the open: branch stubs (knots) low on the stem
        public IReadOnlyList<int> Knots { get; } // knots per metre of usable stem
        public IReadOnlyList<TreeSign> Signs { get; }
        public int Grade { get; }          // hidden wood quality 1..100
        /// <summary>How far off this faller's eye is when sighting this tree's height (fraction, fixed per tree).</summary>
        public double SightError { get; }

        public TrunkSound Tapped { get; internal set; } = TrunkSound.None;
        public bool Felled { get; internal set; }

        /// <summary>Usable stem in whole metres: the rest is crown.</summary>
        public int StemLength => Knots.Count;

        public Tree(int id, double x, double y, double height, int diameter, double crownRadius,
            int leanDir, int leanStrength, int crownDir, int crownStrength, RotKind rot, int rotLength,
            int crookAt, bool lowBranches, IReadOnlyList<int> knots, IReadOnlyList<TreeSign> signs, int grade, double sightError)
        {
            Id = id;
            X = x;
            Y = y;
            Height = height;
            Diameter = diameter;
            CrownRadius = crownRadius;
            LeanDir = leanDir;
            LeanStrength = leanStrength;
            CrownDir = crownDir;
            CrownStrength = crownStrength;
            Rot = rot;
            RotLength = rotLength;
            CrookAt = crookAt;
            LowBranches = lowBranches;
            Knots = knots;
            Signs = signs;
            Grade = grade;
            SightError = sightError;
        }
    }

    /// <summary>One metre of the felled stem, butt first.</summary>
    public sealed class StemUnit
    {
        public int Index { get; }
        public RotKind Rot { get; }
        public bool Crook { get; }
        public int Knots { get; }

        public bool Cracked { get; internal set; }   // broke on a rock, downhill, hang-up or barber chair
        public bool SplitEnd { get; internal set; }  // a pinched cut tore the end
        public bool FibrePull { get; internal set; } // the hinge tore fibre out of the butt
        /// <summary>Lying on the ground (or a rock). False where the stem bridges a dip or hangs out past its support.</summary>
        public bool Resting { get; internal set; } = true;
        public bool OnRock { get; internal set; }
        public TrunkSound Tapped { get; internal set; } = TrunkSound.None;

        public bool Defect => Rot != RotKind.None || Crook || Cracked;

        public StemUnit(int index, RotKind rot, bool crook, int knots)
        {
            Index = index;
            Rot = rot;
            Crook = crook;
            Knots = knots;
        }
    }

    /// <summary>A length of stem between two cuts (or the crown).</summary>
    public sealed class LogPiece
    {
        public int Start { get; }  // first metre (inclusive)
        public int End { get; }    // last metre (exclusive)
        public bool IsCrown { get; }
        public PieceUse Use { get; internal set; } = PieceUse.Leave;

        public int Length => End - Start;

        public LogPiece(int start, int end, bool isCrown = false)
        {
            Start = start;
            End = end;
            IsCrown = isCrown;
        }
    }

    /// <summary>A kind of stand.</summary>
    public sealed class StandType
    {
        public string Id { get; }
        public string Name { get; }
        public Species Species { get; }
        public string TimberName { get; }
        public string BarkName { get; }
        public int RequiredSkill { get; }
        public string Description { get; }
        public double MinHeight { get; }
        public double MaxHeight { get; }
        public int MinDiameter { get; }
        public int MaxDiameter { get; }
        public double MinCrown { get; }
        public double MaxCrown { get; }
        public double RotChance { get; }
        public double CrookChance { get; }
        /// <summary>Bark pieces from peeling one timber length.</summary>
        public int BarkPerLog { get; }

        public StandType(string id, string name, Species species, string timberName, string barkName, int requiredSkill, string description,
            double minHeight, double maxHeight, int minDiameter, int maxDiameter, double minCrown, double maxCrown,
            double rotChance, double crookChance, int barkPerLog)
        {
            Id = id;
            Name = name;
            Species = species;
            TimberName = timberName;
            BarkName = barkName;
            RequiredSkill = requiredSkill;
            Description = description;
            MinHeight = minHeight;
            MaxHeight = maxHeight;
            MinDiameter = minDiameter;
            MaxDiameter = maxDiameter;
            MinCrown = minCrown;
            MaxCrown = maxCrown;
            RotChance = rotChance;
            CrookChance = crookChance;
            BarkPerLog = barkPerLog;
        }

        // Timber for carpenters and builders, bark for tanners (about 1.5 bark per timber across stands), firewood for cooks and alchemists.
        public static readonly StandType PineStand = new StandType(
            "pine_stand", "Pine stand", Species.Pine, "Pine timber", "Pine bark", 1,
            "Tall, straight conifers. Light work, modest bark.",
            16, 25, 30, 52, 2.4, 3.4, 0.2, 0.12, 1);
        public static readonly StandType BirchCopse = new StandType(
            "birch_copse", "Birch copse", Species.Birch, "Birch timber", "Birch bark", 1,
            "Slim birches. Easy to fell, often crooked, quick to rot, rich in bark.",
            14, 20, 24, 38, 2.0, 3.0, 0.32, 0.3, 2);
        public static readonly StandType OakGrove = new StandType(
            "oak_grove", "Oak grove", Species.Oak, "Oak timber", "Oak bark", 20,
            "Old broad oaks. Heavy work and wide crowns, the best timber and tanning bark.",
            16, 23, 44, 70, 3.4, 4.8, 0.3, 0.25, 2);

        public static readonly StandType[] All = { PineStand, BirchCopse, OakGrove };
    }

    /// <summary>The felling axe. Better axes raise the quality cap (never the yield).</summary>
    public sealed class Axe
    {
        public string Name { get; }
        public int QualityCap { get; }
        public string Description { get; }

        public Axe(string name, int qualityCap, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            Description = description;
        }

        public static readonly Axe WornAxe = new Axe("Worn axe", 60, "Starter kit. Caps timber quality at 60.");
        public static readonly Axe IronAxe = new Axe("Iron axe", 85, "Smith-made. Caps timber quality at 85.");
        public static readonly Axe SteelAxe = new Axe("Steel axe", 100, "Master-made. No cap.");

        public static readonly Axe[] All = { WornAxe, IronAxe, SteelAxe };
    }

    /// <summary>What the logger brings to the stand, and the stand itself.</summary>
    public sealed class LogSetup
    {
        public StandType Stand = StandType.PineStand;
        public int SkillLevel = 1;
        public Axe Axe = Axe.IronAxe;
        /// <summary>Hidden stand grade 1..100: the base of timber quality (each tree varies around it).</summary>
        public int StandGrade = 60;

        public LogSetup() { }

        public LogSetup(StandType stand) { Stand = stand; }
    }
}
