// Pure C# (no UnityEngine) so the same rules can run on the game server and in tests.
using System.Collections.Generic;

namespace Mmo.Crafting.Mining
{
    /// <summary>Dig: tap, cut and prop at the face. Sort: keep or toss what's in the cart. Done: results.</summary>
    public enum MineStage { Dig, Sort, Done }

    /// <summary>Skill ranks from the crafting spec (skill 1-100).</summary>
    public enum SkillRank { Novice, Apprentice, Journeyman, Expert, Master }

    public enum OreKind { Iron, Coal, Copper }

    /// <summary>What the miner works the face with. Tapping costs no swing; the others each cost one.</summary>
    public enum Tool { Tap, Pick, Wedge, Sledge }

    /// <summary>What a block of the face really is. Ore lies in the seam; loose rock is already broken; solid rock is sound.</summary>
    public enum BlockKind { Ore, Solid, Loose }

    public enum BlockState
    {
        InPlace,
        Removed,  // barren rock taken out (to the spoil heap)
        Clean,    // ore taken out whole: into the cart
        Crushed,  // ore shattered: into the cart as crushed grit (worse quality)
    }

    /// <summary>What a tap with the pick's back sounds like.</summary>
    public enum TapSound { None, Ring, Thud, Hollow }

    public enum RoofSound { Solid, Drummy, VeryDrummy }

    /// <summary>
    /// Surface signs a block can show. Which sign means which ore is rolled per world (spec: "the signs are rolled
    /// per world"), so players learn them by digging and can trade what they know.
    /// </summary>
    public enum Sign { RustStreaks, GreenStain, BlackBands, QuartzVeins, YellowLichen, GreyLichen, CubeCrystals, NeedleCrystals }

    public enum PieceColour { Rust, DarkGrey, Black, GreenBlue, Brass, Grey, Brown, White }

    public enum Lustre { Dull, Metallic, Glassy }

    /// <summary>How a piece feels in the hand for its size.</summary>
    public enum Heft { Light, Ordinary, Heavy }

    public enum MineEventKind
    {
        Tapped,
        RoofTapped,
        Pried,      // barren rock taken out cleanly
        Freed,      // ore taken out whole
        Shattered,  // ore crushed
        Bounced,    // a wasted swing: the rock didn't move
        Smashed,    // the sledge broke rock out
        Propped,
        Creak,      // the roof is working
        Collapse,
        StageChanged,
        Done,
    }

    public struct MineEvent
    {
        public MineEventKind Kind;
        public string Message;
        public int BlockId;

        public MineEvent(MineEventKind kind, string message, int blockId = -1)
        {
            Kind = kind;
            Message = message;
            BlockId = blockId;
        }

        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>
    /// One block of the face, bounded by visible joints. The face is Rows x Columns units; a block spans
    /// Width columns of one row (a bed of rock between two bedding planes).
    /// </summary>
    public sealed class Block
    {
        public int Id { get; }
        public int Row { get; }
        public int Col { get; }
        public int Width { get; }
        public BlockKind Kind { get; }
        /// <summary>Signs visible on its surface. True signs only show if the miner's eye is good enough to see them.</summary>
        public IReadOnlyList<Sign> Signs { get; }
        /// <summary>The pieces it breaks into, decided when the face forms (so results are fair to compare).</summary>
        public IReadOnlyList<Piece> Pieces { get; }

        public BlockState State { get; internal set; } = BlockState.InPlace;
        public TapSound Tapped { get; internal set; } = TapSound.None;

        public bool IsLarge => Width >= MineTuning.LargeWidth;
        public bool InPlace => State == BlockState.InPlace;
        public int End => Col + Width; // first column after this block

        public Block(int id, int row, int col, int width, BlockKind kind, IReadOnlyList<Sign> signs, IReadOnlyList<Piece> pieces)
        {
            Id = id;
            Row = row;
            Col = col;
            Width = width;
            Kind = kind;
            Signs = signs;
            Pieces = pieces;
        }
    }

    /// <summary>One lump in the cart. The miner judges it by colour, lustre and heft and keeps it or tosses it.</summary>
    public sealed class Piece
    {
        public bool IsOre { get; }
        public PieceColour Colour { get; }
        public Lustre Lustre { get; }
        public Heft Heft { get; }
        public bool Crushed { get; internal set; }
        public bool Keep { get; internal set; } = true;
        /// <summary>Small shape variation for drawing (0..1).</summary>
        public double Shape { get; }

        public Piece(bool isOre, PieceColour colour, Lustre lustre, Heft heft, double shape)
        {
            IsOre = isOre;
            Colour = colour;
            Lustre = lustre;
            Heft = heft;
            Shape = shape;
        }
    }

    /// <summary>A kind of node.</summary>
    public sealed class OreNodeType
    {
        public string Id { get; }
        public string Name { get; }
        public OreKind Ore { get; }
        public string OreName { get; }
        public int RequiredSkill { get; }
        public string Description { get; }

        public OreNodeType(string id, string name, OreKind ore, string oreName, int requiredSkill, string description)
        {
            Id = id;
            Name = name;
            Ore = ore;
            OreName = oreName;
            RequiredSkill = requiredSkill;
            Description = description;
        }

        // Metal path: iron for level 1-20 gear, coal fuels every forge, copper joins at level 21+.
        public static readonly OreNodeType IronSeam = new OreNodeType(
            "iron_seam", "Iron seam", OreKind.Iron, "Iron ore", 1, "Mountain rock with a bed of iron ore.");
        public static readonly OreNodeType CoalSeam = new OreNodeType(
            "coal_seam", "Coal seam", OreKind.Coal, "Coal", 1, "A hillside coal bed.");
        public static readonly OreNodeType CopperSeam = new OreNodeType(
            "copper_seam", "Copper seam", OreKind.Copper, "Copper ore", 20, "A shallow hill seam of copper ore.");

        public static readonly OreNodeType[] All = { IronSeam, CoalSeam, CopperSeam };
    }

    /// <summary>The miner's pick. Better picks raise the quality cap.</summary>
    public sealed class Pick
    {
        public string Name { get; }
        public int QualityCap { get; }
        public string Description { get; }

        public Pick(string name, int qualityCap, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            Description = description;
        }

        public static readonly Pick WornPick = new Pick("Worn pick", 60, "Starter kit. Caps ore quality at 60.");
        public static readonly Pick IronPick = new Pick("Iron pick", 85, "Smith-made. Caps ore quality at 85.");
        public static readonly Pick SteelPick = new Pick("Steel pick", 100, "Master-made. No cap.");

        public static readonly Pick[] All = { WornPick, IronPick, SteelPick };
    }

    /// <summary>What the miner brings to the node, and the node itself.</summary>
    public sealed class MineSetup
    {
        public OreNodeType Node = OreNodeType.IronSeam;
        public int SkillLevel = 1;
        public Pick Pick = Pick.IronPick;
        /// <summary>Hidden vein grade 1..100: how rich the seam is, and the base of ore quality.</summary>
        public int VeinGrade = 60;
        /// <summary>Which world the node is in. Sign meanings are rolled per world.</summary>
        public int WorldSeed = 1;

        public MineSetup() { }

        public MineSetup(OreNodeType node) { Node = node; }
    }
}
