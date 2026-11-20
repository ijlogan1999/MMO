// Pure C# (no UnityEngine) so the same rules can run on the game server and in tests.
using System;

namespace Mmo.Crafting.Mining
{
    public enum MineStage { Read, Strike, Done }

    /// <summary>Skill ranks from the crafting spec (skill 1-100).</summary>
    public enum SkillRank { Novice, Apprentice, Journeyman, Expert, Master }

    public enum OreKind { Iron, Coal, Copper }

    /// <summary>What happened to one weak point in the rock.</summary>
    public enum PointState
    {
        Waiting,   // not reached yet
        Showing,   // its crack is open right now: strike it
        Clean,     // struck while open: the chunk split off cleanly
        Missed,    // the crack closed before anyone struck it: that chunk stays in the rock
        Crushed,   // a blow off the weak point fractured the rock here and crushed the ore
        Left,      // the miner walked away before reaching it (the node keeps it)
    }

    public enum MineEventKind
    {
        StageChanged,
        Marked,     // the miner chose where to start
        Opened,     // a weak point's crack opened (a creak and a trickle of dust)
        Closed,     // it closed again unstruck
        Clean,      // clean split
        Fracture,   // blow off a weak point
        Done,
    }

    public struct MineEvent
    {
        public MineEventKind Kind;
        public string Message;
        public double X, Y;

        public MineEvent(MineEventKind kind, string message, double x = 0, double y = 0)
        {
            Kind = kind;
            Message = message;
            X = x;
            Y = y;
        }

        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>
    /// The seam: a straight band of ore through the rock face. Face coordinates run 0..1 across and
    /// 0..MineTuning.FaceHeight down, origin top-left.
    /// </summary>
    public sealed class Seam
    {
        public double X { get; }
        public double Y { get; }
        /// <summary>Direction of the band in radians (0 = level).</summary>
        public double Angle { get; }
        /// <summary>Half the band's thickness, in face units.</summary>
        public double HalfWidth { get; }

        public Seam(double x, double y, double angle, double halfWidth)
        {
            X = x;
            Y = y;
            Angle = angle;
            HalfWidth = halfWidth;
        }

        public double DirX => Math.Cos(Angle);
        public double DirY => Math.Sin(Angle);

        /// <summary>Distance from a point to the seam's centre line.</summary>
        public double DistanceTo(double x, double y) => Math.Abs(-(x - X) * DirY + (y - Y) * DirX);

        /// <summary>How much of a chunk at this distance is ore: all of it inside the band, falling to a trace well outside.</summary>
        public double OreContentAt(double x, double y)
        {
            double d = DistanceTo(x, y);
            if (d <= HalfWidth) return 1.0;
            double fade = 1.0 - (d - HalfWidth) / (HalfWidth * MineTuning.SeamFade);
            return Math.Max(MineTuning.StrayOre, fade);
        }
    }

    /// <summary>One place where the rock wants to split. The miner meets them one at a time, in order.</summary>
    public sealed class WeakPoint
    {
        public double X { get; }
        public double Y { get; }
        /// <summary>Share of this chunk that is ore (0..1), from how close it sits to the seam.</summary>
        public double OreContent { get; }
        public PointState State { get; internal set; } = PointState.Waiting;

        public WeakPoint(double x, double y, double oreContent)
        {
            X = x;
            Y = y;
            OreContent = oreContent;
        }
    }

    /// <summary>A kind of node. Harder rock gives less time to strike; thin seams are harder to find.</summary>
    public sealed class OreNodeType
    {
        public string Id { get; }
        public string Name { get; }
        public OreKind Ore { get; }
        public string OreName { get; }
        public int RequiredSkill { get; }
        public double BaseSeamHalfWidth { get; }
        /// <summary>Multiplies how long a weak point stays open (soft rock &gt; 1).</summary>
        public double Softness { get; }
        public string Description { get; }

        public OreNodeType(string id, string name, OreKind ore, string oreName, int requiredSkill,
            double baseSeamHalfWidth, double softness, string description)
        {
            Id = id;
            Name = name;
            Ore = ore;
            OreName = oreName;
            RequiredSkill = requiredSkill;
            BaseSeamHalfWidth = baseSeamHalfWidth;
            Softness = softness;
            Description = description;
        }

        // Metal path: iron for level 1-20 gear, coal fuels every forge, copper joins at level 21+.
        public static readonly OreNodeType IronSeam = new OreNodeType(
            "iron_seam", "Iron seam", OreKind.Iron, "Iron ore", 1, 0.050, 0.90,
            "Mountain rock. Hard, so cracks close fast. Rust-red streaks.");
        public static readonly OreNodeType CoalSeam = new OreNodeType(
            "coal_seam", "Coal seam", OreKind.Coal, "Coal", 1, 0.070, 1.15,
            "Hillside seam. Soft and wide, a good first node.");
        public static readonly OreNodeType CopperSeam = new OreNodeType(
            "copper_seam", "Copper seam", OreKind.Copper, "Copper ore", 20, 0.038, 1.00,
            "Shallow hill seam, thin and easy to lose. Green-blue staining.");

        public static readonly OreNodeType[] All = { IronSeam, CoalSeam, CopperSeam };
    }

    /// <summary>The miner's pick. Better picks raise the quality cap and give more time and reach at each weak point.</summary>
    public sealed class Pick
    {
        public string Name { get; }
        public int QualityCap { get; }
        public double WindowMultiplier { get; }
        public double ReachMultiplier { get; }
        public string Description { get; }

        public Pick(string name, int qualityCap, double windowMultiplier, double reachMultiplier, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            WindowMultiplier = windowMultiplier;
            ReachMultiplier = reachMultiplier;
            Description = description;
        }

        public static readonly Pick WornPick = new Pick("Worn pick", 60, 0.90, 0.90, "Starter kit. Caps ore quality at 60.");
        public static readonly Pick IronPick = new Pick("Iron pick", 85, 1.00, 1.00, "Smith-made. Caps ore quality at 85.");
        public static readonly Pick SteelPick = new Pick("Steel pick", 100, 1.15, 1.15, "Master-made. No cap, a little more time and reach.");

        public static readonly Pick[] All = { WornPick, IronPick, SteelPick };
    }

    /// <summary>What the miner brings to the node, and the node itself.</summary>
    public sealed class MineSetup
    {
        public OreNodeType Node = OreNodeType.IronSeam;
        public int SkillLevel = 1;
        public Pick Pick = Pick.IronPick;
        /// <summary>Hidden vein grade 1..100: caps how good this node's ore can be.</summary>
        public int VeinGrade = 60;
        /// <summary>Swings of ore the node holds (spec: 5 to 15). One weak point per swing.</summary>
        public int NodeSize = 10;

        public MineSetup() { }

        public MineSetup(OreNodeType node) { Node = node; }
    }
}
