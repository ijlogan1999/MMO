using System.Collections.Generic;

namespace Mmo.Crafting.Mining
{
    /// <summary>What came out of the node, with every number that went into it so the player can see why.</summary>
    public sealed class MineResult
    {
        // The dig
        public int OreBlocks;
        public int OreFreed;      // ore blocks taken out whole
        public int OreShattered;  // ore blocks crushed
        public int RockRemoved;
        public int WastedSwings;
        public int SwingsUsed;
        public int TapsUsed;
        public int PropsUsed;
        public bool Collapsed;
        public int PiecesBuried;

        // The sort
        public int OreKept;
        public int CrushedKept;
        public int WasteKept;
        public int OreTossed;
        public int WasteTossed;

        /// <summary>Ore pieces the whole node held.</summary>
        public int NodeOre;
        /// <summary>Kept ore, crushed ore counting half.</summary>
        public double OreTaken;
        public double YieldScore;  // OreTaken / NodeOre
        public double BreakScore;  // clean ore / all ore taken out
        public double Purity;      // kept ore / everything kept
        public double SortScore;   // right keep/toss calls / pieces sorted
        public double Score;       // yield x break x purity (for skill gain)

        // Yield = ore taken.  Quality = vein grade x (0.5 + 0.5 x break) x (0.6 + 0.4 x purity), capped.
        public int Yield;
        public int VeinGrade;
        public int RawQuality;
        public int Ceiling;
        public string CeilingReason;
        public int Quality;
        public string Grade;
        public string ItemName;

        public int SkillGain;
        /// <summary>What the dig taught the journal: one line per sign seen on this face.</summary>
        public readonly List<string> Learned = new List<string>();
    }
}
