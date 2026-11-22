using System.Collections.Generic;

namespace Mmo.Crafting.Logging
{
    /// <summary>What came out of the tree, with every number that went into it so the player can see why.</summary>
    public sealed class LogResult
    {
        // The tree as it really was
        public bool Felled;
        public int TreeId;
        public double Height;
        public int Diameter;
        public RotKind Rot;
        public int RotLength;
        public bool Crooked;
        public bool LowBranches;
        public int TreeGrade;

        // The fall
        public int Aim;
        public int FellDir;
        public int PullDir;
        public double PullStrength;
        public int NotchDepth;     // twentieths of the trunk
        public int Hinge;          // twentieths of the trunk left when it went (0 if cut through)
        public BackCutHeight BackHeight;
        public int Wedges;
        public bool CutThrough;
        public bool HungUp;
        public bool OnLine;
        public int CrackedMetres;
        public int CloseCalls;
        public readonly List<string> FallNotes = new List<string>();

        // Bucking and the yard
        public int TapsUsed;
        public int Cuts;
        public int BuckPinches;
        public int DirtHits;
        public readonly List<string> BuckNotes = new List<string>();
        public int Timber;         // clean lengths stacked (the yield)
        public int TimberCulled;   // lengths stacked as timber that a buyer will refuse
        public readonly List<string> CullNotes = new List<string>();
        public int Bark;
        public int BarkQuality;
        public string BarkName = "";
        public int Firewood;       // bundles
        public int MetresLeft;     // stem left lying in the clearing
        public double StrengthLeft;

        /// <summary>Clean timber lengths the tree could have given, given its rot and crook.</summary>
        public int BestTimber;
        public double YieldScore;  // Timber / BestTimber
        public double Purity;      // clean lengths / everything stacked as timber
        public double Score;       // yield x purity x (1 on line, 0.8 off it), for skill gain

        // Quality = average length quality x (0.6 + 0.4 x purity), capped.
        // Length quality = tree grade x knots factor x torn end x fibre pull.
        public double AvgLengthQuality;
        public int RawQuality;
        public int Ceiling;
        public string CeilingReason;
        public int Quality;
        public string Grade;
        public string ItemName;

        public int SkillGain;
    }
}
