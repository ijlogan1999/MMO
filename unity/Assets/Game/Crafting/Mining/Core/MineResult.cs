namespace Mmo.Crafting.Mining
{
    /// <summary>What came out of the node, with every number that went into it so the player can see why.</summary>
    public sealed class MineResult
    {
        public int NodeSize;
        public int Clean;
        public int Missed;
        public int Crushed;
        public int Left;
        public int Fractures;

        /// <summary>Distance from the miner's mark to the seam's centre line, in seam half-widths (0 = dead on).</summary>
        public double MarkOffset;
        /// <summary>Average ore share of the chunks the miner split cleanly (how well the face was read).</summary>
        public double Read;
        /// <summary>Ore actually split out, as a share of the node (yield score).</summary>
        public double YieldScore;
        /// <summary>Clean splits / (clean splits + fractures): how cleanly the rock broke (quality score).</summary>
        public double BreakScore;
        /// <summary>Overall score for skill gain: yield score x break score.</summary>
        public double Score;

        // Yield = node size x yield score.  Quality = vein grade x (0.5 + 0.5 x break score), capped.
        public int Yield;
        public int VeinGrade;
        public int RawQuality;
        public int Ceiling;
        public string CeilingReason;
        public int Quality;
        public string Grade;
        public string ItemName;

        public int SkillGain;
    }
}
