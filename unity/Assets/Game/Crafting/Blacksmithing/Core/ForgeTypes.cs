// Pure C# (no UnityEngine) so the same rules can run on the game server and in tests.
namespace Mmo.Crafting.Blacksmithing
{
    /// <summary>Where on the blade a strike lands. Each lane moves one part of the shape.</summary>
    public enum Lane { Tip = 0, Body = 1, Edge = 2 }

    public enum QuenchMedium { Water, Oil, Brine }

    public enum ForgeStage { Heat, Strike, QuenchChoose, QuenchHold, Finish, Done }

    /// <summary>Colour of the hot metal, from cold to burning.</summary>
    public enum HeatBand { Black, DullRed, Cherry, Orange, Yellow, White, Burning }

    /// <summary>Skill ranks from the crafting spec (skill 1-100).</summary>
    public enum SkillRank { Novice, Apprentice, Journeyman, Expert, Master }

    public enum Judgement { Perfect, Good, Miss, OffBeat, Cold }

    public enum ForgeEventKind
    {
        StageChanged,
        Pulled,
        Cracked,
        Burned,
        Beat,
        Strike,      // a note was hit (see Judgement: Perfect or Good)
        Miss,
        OffBeat,
        ColdStrike,
        QuenchStart,
        QuenchEnd,
        QuenchCrack,
        FinishStroke,
        Done,
    }

    public struct ForgeEvent
    {
        public ForgeEventKind Kind;
        public Judgement Judgement;
        public Lane Lane;
        public string Message;

        public ForgeEvent(ForgeEventKind kind, string message, Judgement judgement = Judgement.Perfect, Lane lane = Lane.Body)
        {
            Kind = kind;
            Message = message;
            Judgement = judgement;
            Lane = lane;
        }

        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>A forge station. Better stations raise the quality ceiling.</summary>
    public sealed class ForgeStation
    {
        public string Name { get; }
        public int QualityCap { get; }
        public string Description { get; }

        public ForgeStation(string name, int qualityCap, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            Description = description;
        }

        public static readonly ForgeStation CampForge = new ForgeStation("Camp forge", 50, "Portable. Uneven heat, caps quality at 50.");
        public static readonly ForgeStation TownForge = new ForgeStation("Town forge", 75, "Public station in town. Caps quality at 75.");
        public static readonly ForgeStation ShopForge = new ForgeStation("Shop forge", 100, "Owned smithy. No cap.");

        public static readonly ForgeStation[] All = { CampForge, TownForge, ShopForge };
    }

    /// <summary>The smith's hammer. Better tools raise the quality ceiling and give a little more timing room.</summary>
    public sealed class Hammer
    {
        public string Name { get; }
        public int QualityCap { get; }
        public double WindowMultiplier { get; }
        public string Description { get; }

        public Hammer(string name, int qualityCap, double windowMultiplier, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            WindowMultiplier = windowMultiplier;
            Description = description;
        }

        public static readonly Hammer WornHammer = new Hammer("Worn hammer", 60, 0.90, "Starter kit. Caps quality at 60, tighter timing.");
        public static readonly Hammer IronHammer = new Hammer("Iron hammer", 85, 1.00, "Smith-made. Caps quality at 85.");
        public static readonly Hammer SteelHammer = new Hammer("Steel hammer", 100, 1.15, "Master-made. No cap, 15% more timing room.");

        public static readonly Hammer[] All = { WornHammer, IronHammer, SteelHammer };
    }
}
