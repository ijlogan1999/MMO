using System;
using System.Collections.Generic;
using System.Linq;

namespace Mmo.Crafting.Logging
{
    /// <summary>
    /// The logging minigame as a turn-based knowledge game (no timing, no aiming).
    ///
    ///  1. Survey: walk the stand. Read each tree's lean and heavy side from two sides, look for signs of rot
    ///     (and things that only look like them), knock the trunk, sight its height against the clearing.
    ///  2. Aim: choose which way it should fall. The stem must land clear of other crowns, the forest edge and rocks,
    ///     not straight down a steep slope, and the tree's own pull (lean, heavy crown, wind) has to be reckoned with.
    ///  3. Notch: chop the face notch on the side it should fall to, a quarter to a third of the way through.
    ///  4. Back cut: pick its height against the notch floor, then saw in from the far side. Stop with a hinge left
    ///     (about a tenth of the trunk) and let the pull, or wedges, take it over. A tree leaning back sits on the saw
    ///     unless a wedge is set early.
    ///  5. Buck: cut the stem into lengths. Read how it lies: where it bridges a dip saw from beneath, where an end
    ///     hangs free or it lies on the ground saw from the top. Rot runs up from the butt; crooks and cracks spoil a length.
    ///  6. Yard: stack timber (peel it for bark if you have the strength), split the rest for firewood, or leave it.
    /// </summary>
    public sealed class LandingCheck
    {
        public bool Caught;  // a neighbour's crown is in the way
        public bool Edge;    // the top would reach the forest edge
        public bool HungUp => Caught || Edge;
        public readonly List<int> RockMetres = new List<int>();
        public bool Downhill;
        public bool Clear => !HungUp && RockMetres.Count == 0 && !Downhill;
    }

    public sealed class LogMinigame
    {
        public LogSetup Setup { get; }
        public StandType Stand => Setup.Stand;
        public int Skill => Setup.SkillLevel;

        public LogStage Stage { get; private set; } = LogStage.Survey;
        public IReadOnlyList<Tree> Trees => _trees;
        public IReadOnlyList<Rock> Rocks => _rocks;
        public int WindDir { get; }
        /// <summary>0 calm, 1 breeze, 2 strong.</summary>
        public int WindStrength { get; }
        public int DownhillDir { get; }
        public bool Steep { get; }

        public double Strength { get; private set; } = LogTuning.Strength;
        public int TapsLeft { get; private set; }

        /// <summary>The tree being looked at (Survey) or worked (after ChooseTree).</summary>
        public int Selected { get; private set; }
        public Tree Tree => _trees[Selected];

        public int Aim { get; private set; }
        public int NotchDepth { get; private set; }
        public int BackDepth { get; private set; }
        public BackCutHeight BackHeight { get; private set; } = BackCutHeight.Level;
        public int Wedges { get; private set; }
        public bool Pinched { get; private set; }
        public int Hinge => LogTuning.CutSteps - NotchDepth - BackDepth;

        public int FellDir { get; private set; } = -1;
        public bool HungUp { get; private set; }
        public IReadOnlyList<StemUnit> Stem => _stem;
        public IReadOnlyCollection<int> Cuts => _cuts;
        public IReadOnlyList<LogPiece> Pieces => _pieces;

        public LogResult Result { get; private set; }
        public List<LogEvent> Events { get; } = new List<LogEvent>();

        private readonly Random _rng;
        private readonly List<Tree> _trees = new List<Tree>();
        private readonly List<Rock> _rocks = new List<Rock>();
        private readonly List<StemUnit> _stem = new List<StemUnit>();
        private readonly SortedSet<int> _cuts = new SortedSet<int>();
        private readonly List<LogPiece> _pieces = new List<LogPiece>();
        private readonly List<string> _fallNotes = new List<string>();
        private readonly List<string> _buckNotes = new List<string>();
        private bool _satBack, _uncontrolled;
        private int _danger, _tapsUsed, _buckPinches, _dirtHits, _lastCreakDepth = -1;

        public LogMinigame(LogSetup setup, int seed)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _rng = new Random(seed);
            TapsLeft = LogTuning.Taps(Skill);
            WindDir = _rng.Next(8);
            double w = _rng.NextDouble();
            WindStrength = w < 0.3 ? 0 : w < 0.75 ? 1 : 2;
            DownhillDir = _rng.Next(8);
            Steep = _rng.NextDouble() < 0.5;
            BuildStand();
        }

        // ------------------------------------------------------------------ the stand

        private void BuildStand()
        {
            var st = Stand;
            for (int tries = 0; _trees.Count < LogTuning.Trees && tries < 2000; tries++)
            {
                double x = LogTuning.TreeMargin + _rng.NextDouble() * (LogTuning.ClearingW - 2 * LogTuning.TreeMargin);
                double y = LogTuning.TreeMargin + _rng.NextDouble() * (LogTuning.ClearingH - 2 * LogTuning.TreeMargin);
                if (_trees.Any(t => Dist(t.X, t.Y, x, y) < LogTuning.TreeSpacing)) continue;
                _trees.Add(MakeTree(_trees.Count, x, y, st));
            }

            for (int tries = 0; _rocks.Count < LogTuning.Rocks && tries < 2000; tries++)
            {
                double x = 1.5 + _rng.NextDouble() * (LogTuning.ClearingW - 3);
                double y = 1.5 + _rng.NextDouble() * (LogTuning.ClearingH - 3);
                double r = 0.6 + _rng.NextDouble() * 0.8;
                if (_trees.Any(t => Dist(t.X, t.Y, x, y) < r + 2.5)) continue;
                if (_rocks.Any(o => Dist(o.X, o.Y, x, y) < r + o.R + 2)) continue;
                _rocks.Add(new Rock(x, y, r));
            }
        }

        private Tree MakeTree(int id, double x, double y, StandType st)
        {
            double size = _rng.NextDouble(); // big trees are taller, wider, older (more rot, better wood)
            double height = Math.Round(st.MinHeight + (st.MaxHeight - st.MinHeight) * (0.6 * size + 0.4 * _rng.NextDouble()), 1);
            int diameter = (int)Math.Round(st.MinDiameter + (st.MaxDiameter - st.MinDiameter) * (0.7 * size + 0.3 * _rng.NextDouble()));
            double crown = st.MinCrown + (st.MaxCrown - st.MinCrown) * _rng.NextDouble();

            double l = _rng.NextDouble();
            int leanStrength = l < 0.35 ? 0 : l < 0.8 ? 1 : 2;
            int leanDir = _rng.Next(8);
            int crownStrength = _rng.NextDouble() < 0.5 ? 1 : 0;
            int crownDir = _rng.NextDouble() < 0.5 ? LogTuning.Wrap(leanDir + _rng.Next(3) - 1) : _rng.Next(8);

            var rot = RotKind.None;
            if (_rng.NextDouble() < st.RotChance * (0.6 + 0.8 * size))
                rot = _rng.NextDouble() < 0.35 ? RotKind.Hollow : RotKind.HeartRot;

            int stem = Math.Max(LogTuning.TimberLength * 2, (int)Math.Round(height * LogTuning.StemShare));
            int rotLength = rot == RotKind.None ? 0 : Math.Min(stem - 1, (rot == RotKind.Hollow ? 3 : 2) + _rng.Next(4));
            int crookAt = _rng.NextDouble() < st.CrookChance ? 2 + _rng.Next(stem - 3) : -1;
            bool lowBranches = _rng.NextDouble() < 0.3;

            var knots = new List<int>();
            for (int i = 0; i < stem; i++)
            {
                double f = i / (double)stem;
                int k;
                if (f < 0.45) k = lowBranches ? 1 + _rng.Next(2) : (_rng.NextDouble() < 0.15 ? 1 : 0);
                else if (f < 0.75) k = _rng.Next(2) + (lowBranches ? 1 : 0);
                else k = 1 + _rng.Next(3);
                knots.Add(k);
            }

            var signs = new List<TreeSign>();
            double seen = LogTuning.SignSeen(Skill);
            if (rot != RotKind.None)
            {
                if (_rng.NextDouble() < seen * 0.85) signs.Add(TreeSign.Conks);
                if (_rng.NextDouble() < seen * (rot == RotKind.Hollow ? 0.7 : 0.35)) signs.Add(TreeSign.WoodpeckerHoles);
                if (_rng.NextDouble() < seen * (rot == RotKind.Hollow ? 0.45 : 0.2)) signs.Add(TreeSign.DeadTop);
            }
            else
            {
                if (_rng.NextDouble() < 0.04) signs.Add(TreeSign.Conks);
                if (_rng.NextDouble() < 0.06) signs.Add(TreeSign.WoodpeckerHoles);
                if (_rng.NextDouble() < 0.04) signs.Add(TreeSign.DeadTop);
            }
            // Things that look like trouble and aren't, on any tree.
            if (_rng.NextDouble() < 0.35) signs.Add(TreeSign.Moss);
            if (_rng.NextDouble() < 0.2) signs.Add(TreeSign.Ivy);
            if (_rng.NextDouble() < 0.12) signs.Add(TreeSign.Burl);

            int grade = (int)Math.Round(Setup.StandGrade + (size - 0.5) * 12 + (_rng.NextDouble() - 0.5) * 16);
            grade = Math.Max(1, Math.Min(100, grade));

            // Box-Muller: a fixed error in this logger's eye for this tree, so sighting twice says the same.
            double u1 = 1 - _rng.NextDouble(), u2 = _rng.NextDouble();
            double gauss = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            double sight = Math.Max(-0.4, Math.Min(0.4, gauss * LogTuning.SightSpread(Skill)));

            return new Tree(id, x, y, height, diameter, crown, leanDir, leanStrength, crownDir, crownStrength,
                rot, rotLength, crookAt, lowBranches, knots, signs, grade, sight);
        }

        private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        /// <summary>What pulls a tree over: its lean, the heavy side of its crown, and the wind.</summary>
        public (double X, double Y) Pull(Tree t)
        {
            var lean = LogTuning.Vec(t.LeanDir);
            var crown = LogTuning.Vec(t.CrownDir);
            var wind = LogTuning.Vec(WindDir);
            double cw = LogTuning.CrownWeight * t.CrownStrength, ww = LogTuning.WindWeight * WindStrength;
            return (lean.X * t.LeanStrength + crown.X * cw + wind.X * ww,
                    lean.Y * t.LeanStrength + crown.Y * cw + wind.Y * ww);
        }

        public double PullStrength(Tree t)
        {
            var p = Pull(t);
            return Math.Sqrt(p.X * p.X + p.Y * p.Y);
        }

        public int PullDir(Tree t)
        {
            var p = Pull(t);
            return LogTuning.DirOf(p.X, p.Y);
        }

        /// <summary>How hard the tree pulls toward a direction (negative: it pulls back against it).</summary>
        public double Forward(Tree t, int dir)
        {
            var p = Pull(t);
            var v = LogTuning.Vec(dir);
            return p.X * v.X + p.Y * v.Y;
        }

        private bool Spend(double cost)
        {
            if (Strength + 1e-9 < cost)
            {
                Events.Add(new LogEvent(LogEventKind.TooTired, "You haven't the strength left for that"));
                return false;
            }
            Strength = Math.Max(0, Strength - cost);
            return true;
        }

        // ------------------------------------------------------------------ survey

        public void Select(int treeId)
        {
            if (Stage != LogStage.Survey || treeId < 0 || treeId >= _trees.Count) return;
            Selected = treeId;
            Events.Add(new LogEvent(LogEventKind.Selected, "", treeId));
        }

        /// <summary>Knock the trunk with the back of the axe: sound wood rings, rotten wood sounds dull, a hollow booms.</summary>
        public TrunkSound Tap(int treeId)
        {
            if (Stage != LogStage.Survey || treeId < 0 || treeId >= _trees.Count) return TrunkSound.None;
            var t = _trees[treeId];
            if (t.Tapped != TrunkSound.None) return t.Tapped;
            if (TapsLeft <= 0) return TrunkSound.None;
            TapsLeft--;
            _tapsUsed++;
            t.Tapped = Sound(t.Rot);
            Events.Add(new LogEvent(LogEventKind.Tapped, t.Tapped.ToString().ToLowerInvariant(), treeId));
            return t.Tapped;
        }

        private static TrunkSound Sound(RotKind rot) => rot switch
        {
            RotKind.Hollow => TrunkSound.Boom,
            RotKind.HeartRot => TrunkSound.Dull,
            _ => TrunkSound.Ring,
        };

        /// <summary>
        /// Sight the tree's height with the axe handle at arm's length: returns how far out the logger reckons its top
        /// will reach. The eye errs (less with skill), the same way each time for the same tree.
        /// </summary>
        public double SightReach(int treeId)
        {
            if (treeId < 0 || treeId >= _trees.Count) return 0;
            var t = _trees[treeId];
            Events.Add(new LogEvent(LogEventKind.Sighted, "", treeId));
            return t.Height * (1 + t.SightError);
        }

        /// <summary>Walk up to the selected tree to fell it.</summary>
        public void ChooseTree()
        {
            if (Stage != LogStage.Survey) return;
            Stage = LogStage.Aim;
            Events.Add(new LogEvent(LogEventKind.StageChanged, "Aim"));
        }

        // ------------------------------------------------------------------ aim

        public void SetAim(int dir)
        {
            if (Stage != LogStage.Aim && !(Stage == LogStage.Notch && NotchDepth == 0)) return;
            Aim = LogTuning.Wrap(dir);
            Events.Add(new LogEvent(LogEventKind.Aimed, LogTuning.DirNames[Aim]));
        }

        public void BackToSurvey()
        {
            if (Stage != LogStage.Aim) return;
            Stage = LogStage.Survey;
            Events.Add(new LogEvent(LogEventKind.StageChanged, "Survey"));
        }

        public void ConfirmAim()
        {
            if (Stage != LogStage.Aim) return;
            Stage = LogStage.Notch;
            Events.Add(new LogEvent(LogEventKind.StageChanged, "Notch"));
        }

        // ------------------------------------------------------------------ notch

        /// <summary>One bite deeper into the face notch.</summary>
        public void Chop()
        {
            if (Stage != LogStage.Notch || NotchDepth >= LogTuning.MaxNotch) return;
            if (!Spend(LogTuning.StepCost(Tree.Diameter))) return;
            NotchDepth++;
            Events.Add(new LogEvent(LogEventKind.Chopped, "", NotchDepth));
        }

        public void BackToAim()
        {
            if (Stage != LogStage.Notch || NotchDepth > 0) return;
            Stage = LogStage.Aim;
            Events.Add(new LogEvent(LogEventKind.StageChanged, "Aim"));
        }

        public void FinishNotch()
        {
            if (Stage != LogStage.Notch || NotchDepth == 0) return;
            Stage = LogStage.BackCut;
            Events.Add(new LogEvent(LogEventKind.StageChanged, "BackCut"));
        }

        // ------------------------------------------------------------------ back cut

        /// <summary>Where the back cut starts. Only before the saw is in.</summary>
        public void SetBackHeight(BackCutHeight h)
        {
            if (Stage != LogStage.BackCut || BackDepth > 0) return;
            BackHeight = h;
        }

        /// <summary>One stroke deeper into the back cut. A pinched saw takes twice the effort until a wedge frees it.</summary>
        public void Saw()
        {
            if (Stage != LogStage.BackCut || Hinge <= 0) return;
            if (!Spend(LogTuning.StepCost(Tree.Diameter) * (Pinched ? 2 : 1))) return;
            BackDepth++;
            Events.Add(new LogEvent(LogEventKind.Sawed, "", BackDepth));

            if (!_satBack && Wedges == 0 && BackDepth >= LogTuning.SitBackDepth && Forward(Tree, Aim) < 0)
            {
                _satBack = true;
                Pinched = true;
                Events.Add(new LogEvent(LogEventKind.Pinched, "The tree settles back onto the saw and pinches it"));
            }
            CheckFall();
        }

        /// <summary>Drive a wedge into the back cut: lifts the tree toward the notch and frees a pinched saw.</summary>
        public void DriveWedge()
        {
            if (Stage != LogStage.BackCut) return;
            if (BackDepth < LogTuning.WedgeRoom)
            {
                Events.Add(new LogEvent(LogEventKind.Refused, "No room for a wedge yet: saw in further first"));
                return;
            }
            if (Wedges >= LogTuning.MaxWedges)
            {
                Events.Add(new LogEvent(LogEventKind.Refused, "The wedges are driven home"));
                return;
            }
            if (!Spend(LogTuning.WedgeCost)) return;
            Wedges++;
            if (Pinched)
            {
                Pinched = false;
                Events.Add(new LogEvent(LogEventKind.Wedged, "The wedge lifts it off the saw"));
            }
            else Events.Add(new LogEvent(LogEventKind.Wedged, "The wedge bites"));
            CheckFall();
        }

        private void CheckFall()
        {
            if (Hinge <= 0)
            {
                Fall(true);
                return;
            }
            if (BackDepth == 0) return;
            double push = Forward(Tree, Aim) + Wedges * LogTuning.WedgePush;
            double hold = LogTuning.HingeHold(Hinge);
            if (push >= hold) Fall(false);
            else if (push >= hold - 0.8 && _lastCreakDepth != BackDepth + Wedges * 100)
            {
                _lastCreakDepth = BackDepth + Wedges * 100;
                Events.Add(new LogEvent(LogEventKind.Creak, "The hinge creaks"));
            }
        }

        /// <summary>Leave without felling anything (or with the tree left standing).</summary>
        public void WalkAway()
        {
            if (Stage == LogStage.Buck || Stage == LogStage.Yard || Stage == LogStage.Done) return;
            Complete();
        }

        // ------------------------------------------------------------------ the fall

        private void Fall(bool uncontrolled)
        {
            var t = Tree;
            _uncontrolled = uncontrolled;
            double ps = PullStrength(t);
            int pdir = PullDir(t);
            double forward = Forward(t, Aim);
            int dir = Aim;

            if (uncontrolled)
            {
                _danger++;
                dir = ps >= 0.5 ? pdir : LogTuning.Wrap(Aim + (_rng.NextDouble() < 0.5 ? 1 : -1));
                _fallNotes.Add($"You sawed clean through the hinge. With nothing left to steer it, it went its own way ({LogTuning.DirNames[dir]}).");
            }
            else
            {
                var weak = new List<string>();
                if (Hinge == 1) weak.Add("the hinge was too thin to steer it");
                if (NotchDepth < LogTuning.NotchMin) weak.Add("the notch was too shallow");
                if (NotchDepth > LogTuning.NotchMax) weak.Add("the notch was too deep, leaving the hinge too short");
                if (t.Rot != RotKind.None) weak.Add("the hinge was in rotten wood");
                if (weak.Count > 0 && ps >= LogTuning.DriftPull && pdir != Aim)
                {
                    dir = LogTuning.StepToward(Aim, pdir);
                    _fallNotes.Add($"It swung off your line toward its pull ({LogTuning.DirNames[dir]}): {string.Join(", ", weak)}.");
                }
                else if (weak.Count > 0)
                    _fallNotes.Add($"It held your line, but only because nothing pulled it aside: {string.Join(", ", weak)}.");
            }
            FellDir = dir;

            // The stem, butt first.
            for (int i = 0; i < t.StemLength; i++)
                _stem.Add(new StemUnit(i, i < t.RotLength ? t.Rot : RotKind.None, i == t.CrookAt, t.Knots[i]));
            int n = _stem.Count;

            // At the stump.
            if (BackHeight == BackCutHeight.Below)
            {
                _danger++;
                _stem[0].Cracked = true;
                _fallNotes.Add("The back cut was below the notch floor: the butt kicked back off the stump and split.");
                Events.Add(new LogEvent(LogEventKind.Kickback, "The butt kicks back off the stump!"));
            }
            else if (BackHeight == BackCutHeight.Level && !uncontrolled && _rng.NextDouble() < LogTuning.LevelSlideChance)
            {
                _danger++;
                _stem[0].SplitEnd = true;
                _fallNotes.Add("The back cut was level with the notch: with no step to catch it, the butt slid back off the stump and tore.");
            }
            if (BackHeight == BackCutHeight.High)
            {
                _stem[0].FibrePull = true;
                _fallNotes.Add("The back cut was well above the notch: the tall hinge tore fibre out of the butt.");
            }
            if (!uncontrolled && Hinge >= LogTuning.ThickHinge)
            {
                _stem[0].FibrePull = true;
                _fallNotes.Add("The hinge was thick: it tore fibre out of the butt as it went over.");
            }
            if (NotchDepth < LogTuning.NotchMin && forward >= LogTuning.BarberChairPull)
            {
                _danger++;
                for (int i = 0; i < 3 && i < n; i++) _stem[i].Cracked = true;
                _fallNotes.Add("A shallow notch on a tree pulling hard forward: the trunk split up from the stump (a barber chair).");
                Events.Add(new LogEvent(LogEventKind.BarberChair, "The trunk splits up from the stump!"));
            }

            // Where it lands.
            var line = CheckLine(t, dir);
            if (line.HungUp)
            {
                HungUp = true;
                _danger++;
                Strength = Math.Max(0, Strength - LogTuning.FreeHangUpCost);
                for (int i = Math.Max(0, n - LogTuning.HangUpBreaks); i < n; i++) _stem[i].Cracked = true;
                _fallNotes.Add(line.Edge
                    ? "It went into the trees at the edge of the clearing and hung up there. Pulling it down cost you a lot of strength and broke the top of the stem."
                    : "It hung up in a neighbour's crown. Pulling it down cost you a lot of strength and broke the top of the stem.");
                Events.Add(new LogEvent(LogEventKind.HungUp, line.Edge ? "It hangs up in the forest edge" : "It hangs up in the next tree"));
            }

            var supports = new SortedSet<int> { 0 };
            foreach (int u in line.RockMetres)
            {
                _stem[u].Cracked = true;
                _stem[u].OnRock = true;
                supports.Add(u);
                _fallNotes.Add($"It came down across a rock about {u + 1} m up the stem and cracked there.");
                Events.Add(new LogEvent(LogEventKind.Cracked, "It cracks across a rock", u));
            }

            if (line.Downhill)
            {
                int u = n / 3 + _rng.Next(Math.Max(1, n / 3));
                _stem[u].Cracked = true;
                _fallNotes.Add($"It fell down the slope, hit hard and cracked about {u + 1} m up the stem.");
                Events.Add(new LogEvent(LogEventKind.Cracked, "It hits hard down the slope", u));
            }

            // How it lies: the butt by the stump, a hump somewhere along it, and any rocks it came down on.
            supports.Add(n / 3 + _rng.Next(Math.Max(1, n / 3)));
            int prev = -1;
            foreach (int s in supports)
            {
                if (prev >= 0 && s - prev >= 3 && _rng.NextDouble() < 0.6)
                    for (int i = prev + 1; i < s; i++) _stem[i].Resting = false;
                prev = s;
            }
            if (prev < n - 2 && n - 1 - prev <= 5 && _rng.NextDouble() < 0.55)
                for (int i = prev + 1; i < n; i++) _stem[i].Resting = false;

            t.Felled = true;
            Stage = LogStage.Buck;
            string how = uncontrolled ? "It goes, but not where you meant!" : dir == Aim ? "Timber! It lays down right on your line." : "Timber! It swings off your line as it goes.";
            Events.Add(new LogEvent(LogEventKind.Fell, how, dir));
            Events.Add(new LogEvent(LogEventKind.StageChanged, "Buck"));
        }

        /// <summary>What a tree falling in a direction would land on. For tests and NPC loggers; the player gets no such readout.</summary>
        public LandingCheck CheckLine(Tree t, int dir)
        {
            var c = new LandingCheck();
            var v = LogTuning.Vec(dir);
            double reach = t.Height * LogTuning.EdgeReach;
            double endX = t.X + v.X * reach, endY = t.Y + v.Y * reach;
            c.Edge = endX < 0 || endX > LogTuning.ClearingW || endY < 0 || endY > LogTuning.ClearingH;
            foreach (var o in _trees)
            {
                if (o == t || o.Felled) continue;
                double along = (o.X - t.X) * v.X + (o.Y - t.Y) * v.Y;
                if (along < 2 || along > t.Height) continue;
                double perp = Math.Abs((o.X - t.X) * v.Y - (o.Y - t.Y) * v.X);
                if (perp <= o.CrownRadius * LogTuning.HangUpReach) c.Caught = true;
            }
            foreach (var r in _rocks)
            {
                double along = (r.X - t.X) * v.X + (r.Y - t.Y) * v.Y;
                if (along <= 0 || along >= t.StemLength) continue;
                double perp = Math.Abs((r.X - t.X) * v.Y - (r.Y - t.Y) * v.X);
                if (perp <= r.R + 0.3) c.RockMetres.Add((int)along);
            }
            c.Downhill = Steep && LogTuning.DirDiff(dir, DownhillDir) <= 1;
            return c;
        }

        // ------------------------------------------------------------------ buck

        /// <summary>
        /// The side a cut at boundary k (between metre k-1 and k) should be sawn from. Lying on the ground: the top
        /// (you can't saw through dirt). Bridging a dip, held both sides: it sags, the top closes, so from beneath.
        /// An end hanging free past its support: the bottom closes, so from the top.
        /// </summary>
        public BuckSide RightSide(int k)
        {
            if (_stem[k - 1].Resting && _stem[k].Resting) return BuckSide.Top;
            bool left = false, right = false;
            for (int j = 0; j < k; j++) if (_stem[j].Resting) { left = true; break; }
            for (int j = k; j < _stem.Count; j++) if (_stem[j].Resting) { right = true; break; }
            return left && right ? BuckSide.Under : BuckSide.Top;
        }

        /// <summary>Knock a metre of the felled stem: sound, dull (rot) or booming (hollow).</summary>
        public TrunkSound TapStem(int unit)
        {
            if (Stage != LogStage.Buck || unit < 0 || unit >= _stem.Count) return TrunkSound.None;
            var u = _stem[unit];
            if (u.Tapped != TrunkSound.None) return u.Tapped;
            if (TapsLeft <= 0) return TrunkSound.None;
            TapsLeft--;
            _tapsUsed++;
            u.Tapped = Sound(u.Rot);
            Events.Add(new LogEvent(LogEventKind.Tapped, u.Tapped.ToString().ToLowerInvariant(), unit));
            return u.Tapped;
        }

        /// <summary>Saw through the stem at boundary k (1..length-1) from the top or from beneath.</summary>
        public void Buck(int k, BuckSide side)
        {
            if (Stage != LogStage.Buck) return;
            if (k < 1 || k >= _stem.Count || _cuts.Contains(k))
            {
                Events.Add(new LogEvent(LogEventKind.Refused, "Already cut there", k));
                return;
            }
            double cost = LogTuning.BuckCost(Tree.Diameter);
            if (side == BuckSide.Under && _stem[k - 1].Resting && _stem[k].Resting)
            {
                if (!Spend(cost * 0.5)) return;
                _dirtHits++;
                Events.Add(new LogEvent(LogEventKind.HitDirt, "The saw grinds into the dirt: it lies on the ground here", k));
                return;
            }
            var right = RightSide(k);
            if (!Spend(cost)) return;
            _cuts.Add(k);
            if (side == right)
            {
                Events.Add(new LogEvent(LogEventKind.Bucked, "A clean cut", k));
                return;
            }

            _buckPinches++;
            Strength = Math.Max(0, Strength - LogTuning.BuckPinchCost);
            // The side that drops tears.
            bool leftHeld = false;
            for (int j = 0; j < k; j++) if (_stem[j].Resting) { leftHeld = true; break; }
            var torn = right == BuckSide.Under ? _stem[k] : leftHeld ? _stem[k] : _stem[k - 1];
            torn.SplitEnd = true;
            _buckNotes.Add(right == BuckSide.Under
                ? $"The cut at {k} m was sawn from the top where the log sagged over a dip: it closed on the saw and the end tore."
                : $"The cut at {k} m was sawn from beneath where the end hung free: it closed on the saw and the end tore as it dropped.");
            Events.Add(new LogEvent(LogEventKind.BuckPinched, "The cut closes on the saw; you wrench it free and the end tears", k));
        }

        public void FinishBucking()
        {
            if (Stage != LogStage.Buck) return;
            int start = 0;
            foreach (int k in _cuts)
            {
                _pieces.Add(new LogPiece(start, k));
                start = k;
            }
            _pieces.Add(new LogPiece(start, _stem.Count));
            _pieces.Add(new LogPiece(_stem.Count, _stem.Count, true));
            Stage = LogStage.Yard;
            Events.Add(new LogEvent(LogEventKind.StageChanged, "Yard"));
        }

        // ------------------------------------------------------------------ yard

        public int FirewoodBundles(LogPiece p)
        {
            if (p.IsCrown) return LogTuning.CrownFirewood;
            double wood = 0;
            for (int i = p.Start; i < p.End; i++)
                wood += _stem[i].Rot == RotKind.None ? 1 : _stem[i].Rot == RotKind.HeartRot ? 0.5 : 0;
            return (int)Math.Ceiling(wood / 2 - 1e-9);
        }

        public double UseCost(LogPiece p, PieceUse use) => use switch
        {
            PieceUse.Timber => LogTuning.StackCost,
            PieceUse.PeeledTimber => LogTuning.StackCost + LogTuning.PeelCost,
            PieceUse.Firewood => LogTuning.SplitCost * FirewoodBundles(p),
            _ => 0,
        };

        public bool CanUse(LogPiece p, PieceUse use) =>
            !((use == PieceUse.Timber || use == PieceUse.PeeledTimber) && (p.IsCrown || p.Length < LogTuning.TimberLength));

        public void SetUse(int index, PieceUse use)
        {
            if (Stage != LogStage.Yard || index < 0 || index >= _pieces.Count) return;
            var p = _pieces[index];
            if (!CanUse(p, use))
            {
                Events.Add(new LogEvent(LogEventKind.Refused, p.IsCrown ? "The crown is all limbs: firewood or nothing" : "Too short for timber", index));
                return;
            }
            double delta = UseCost(p, use) - UseCost(p, p.Use);
            if (delta > Strength + 1e-9)
            {
                Events.Add(new LogEvent(LogEventKind.TooTired, "You haven't the strength left for that", index));
                return;
            }
            Strength = Math.Max(0, Strength - delta);
            p.Use = use;
        }

        /// <summary>Leave, timber, peeled timber, firewood, and round again (skipping what can't be done).</summary>
        public void CycleUse(int index)
        {
            if (Stage != LogStage.Yard || index < 0 || index >= _pieces.Count) return;
            var p = _pieces[index];
            var order = new[] { PieceUse.Leave, PieceUse.Timber, PieceUse.PeeledTimber, PieceUse.Firewood };
            int at = Array.IndexOf(order, p.Use);
            for (int step = 1; step <= order.Length; step++)
            {
                var next = order[(at + step) % order.Length];
                if (!CanUse(p, next)) continue;
                if (UseCost(p, next) - UseCost(p, p.Use) > Strength + 1e-9) continue;
                SetUse(index, next);
                return;
            }
        }

        /// <summary>Does this length hold rot, a crook or a crack (a buyer will refuse it as timber)?</summary>
        public bool HasDefect(LogPiece p)
        {
            for (int i = p.Start; i < p.End; i++) if (_stem[i].Defect) return true;
            return false;
        }

        public int PieceQuality(LogPiece p)
        {
            double knots = 0;
            bool split = false, pull = false;
            for (int i = p.Start; i < p.End; i++)
            {
                knots += _stem[i].Knots;
                split |= _stem[i].SplitEnd;
                pull |= _stem[i].FibrePull;
            }
            double q = Tree.Grade * Math.Max(0.4, 1 - LogTuning.KnotPenalty * knots / Math.Max(1, p.Length));
            if (split) q *= LogTuning.SplitEndFactor;
            if (pull) q *= LogTuning.FibrePullFactor;
            return (int)Math.Round(q);
        }

        public void FinishYard()
        {
            if (Stage != LogStage.Yard) return;
            Complete();
        }

        // ------------------------------------------------------------------ scoring

        /// <summary>Quality cap: the lower of the axe's cap and what the logger's skill can bring out.</summary>
        public static int ComputeCeiling(LogSetup setup, out string reason)
        {
            int skillCap = LogTuning.SkillCap(setup.SkillLevel);
            if (setup.Axe.QualityCap < skillCap) { reason = $"Tool ({setup.Axe.Name})"; return setup.Axe.QualityCap; }
            reason = $"Skill ({setup.SkillLevel})";
            return skillCap;
        }

        /// <summary>Most clean timber lengths the tree could have given, with its rot and crook (not counting damage done felling it).</summary>
        public static int BestTimber(Tree t)
        {
            int best = 0, run = 0;
            for (int i = 0; i < t.StemLength; i++)
            {
                bool bad = i < t.RotLength || i == t.CrookAt;
                if (bad) { best += run / LogTuning.TimberLength; run = 0; }
                else run++;
            }
            return best + run / LogTuning.TimberLength;
        }

        private void Complete()
        {
            var t = Tree;
            var r = new LogResult
            {
                Felled = t.Felled,
                TreeId = t.Id,
                Height = t.Height,
                Diameter = t.Diameter,
                Rot = t.Rot,
                RotLength = t.RotLength,
                Crooked = t.CrookAt >= 0,
                LowBranches = t.LowBranches,
                TreeGrade = t.Grade,
                Aim = Aim,
                FellDir = FellDir,
                PullDir = PullDir(t),
                PullStrength = PullStrength(t),
                NotchDepth = NotchDepth,
                Hinge = t.Felled && !_uncontrolled ? Hinge : 0,
                BackHeight = BackHeight,
                Wedges = Wedges,
                CutThrough = _uncontrolled,
                HungUp = HungUp,
                CloseCalls = _danger,
                TapsUsed = _tapsUsed,
                BuckPinches = _buckPinches,
                DirtHits = _dirtHits,
                Cuts = _cuts.Count,
                StrengthLeft = Strength,
            };
            r.FallNotes.AddRange(_fallNotes);
            r.BuckNotes.AddRange(_buckNotes);
            r.BestTimber = BestTimber(t);
            foreach (var u in _stem) if (u.Cracked) r.CrackedMetres++;

            double qSum = 0;
            foreach (var p in _pieces)
            {
                switch (p.Use)
                {
                    case PieceUse.Timber:
                    case PieceUse.PeeledTimber:
                        if (HasDefect(p))
                        {
                            r.TimberCulled++;
                            r.CullNotes.Add($"The {p.Length} m length from {p.Start} m: " + DefectWords(p) + ".");
                        }
                        else
                        {
                            r.Timber++;
                            qSum += PieceQuality(p);
                        }
                        if (p.Use == PieceUse.PeeledTimber) r.Bark += Stand.BarkPerLog;
                        break;
                    case PieceUse.Firewood:
                        r.Firewood += FirewoodBundles(p);
                        break;
                    default:
                        if (!p.IsCrown) r.MetresLeft += p.Length;
                        break;
                }
            }

            int stacked = r.Timber + r.TimberCulled;
            r.Purity = stacked > 0 ? r.Timber / (double)stacked : 0;
            r.AvgLengthQuality = r.Timber > 0 ? qSum / r.Timber : 0;
            r.YieldScore = r.BestTimber > 0 ? Math.Min(1, r.Timber / (double)r.BestTimber) : 0;
            r.OnLine = t.Felled && FellDir == Aim;
            r.Score = r.YieldScore * r.Purity * (r.OnLine ? 1 : 0.8);

            r.Ceiling = ComputeCeiling(Setup, out r.CeilingReason);
            r.RawQuality = (int)Math.Round(r.AvgLengthQuality * (0.6 + 0.4 * r.Purity));
            if (r.Timber > 0)
            {
                r.Quality = Math.Max(1, Math.Min(r.Ceiling, r.RawQuality));
                r.Grade = LogTuning.GradeWord(r.Quality);
                r.ItemName = $"{r.Grade} {Stand.TimberName}";
            }
            else
            {
                r.Quality = 0;
                r.Grade = "";
                r.ItemName = t.Felled ? "No timber" : "Nothing felled";
            }
            if (r.Bark > 0)
            {
                r.BarkQuality = Math.Max(1, Math.Min(r.Ceiling, t.Grade));
                r.BarkName = $"{LogTuning.GradeWord(r.BarkQuality)} {Stand.BarkName}";
            }

            if ((r.Timber > 0 || r.Firewood > 0) && Skill < 100 && Skill < Stand.RequiredSkill + 30)
                r.SkillGain = r.Score >= 0.5 ? 2 : 1;

            Result = r;
            Stage = LogStage.Done;
            Events.Add(new LogEvent(LogEventKind.Done, r.ItemName));
        }

        private string DefectWords(LogPiece p)
        {
            var words = new List<string>();
            bool rot = false, hollow = false, crook = false, crack = false;
            for (int i = p.Start; i < p.End; i++)
            {
                rot |= _stem[i].Rot == RotKind.HeartRot;
                hollow |= _stem[i].Rot == RotKind.Hollow;
                crook |= _stem[i].Crook;
                crack |= _stem[i].Cracked;
            }
            if (hollow) words.Add("hollow");
            if (rot) words.Add("rotten at the heart");
            if (crook) words.Add("crooked");
            if (crack) words.Add("cracked");
            return "refused as timber, " + string.Join(", ", words);
        }
    }
}
