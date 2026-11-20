using System;
using System.Collections.Generic;

namespace Mmo.Crafting.Mining
{
    /// <summary>
    /// The mining "read and strike" minigame as a plain state machine (spec, Minigames: Mining).
    ///  1. Read the face: the rock shows streaks, flecks and cracks. Nothing says where the seam runs. The miner marks
    ///     where to start; the rock then splits along its grain from that spot, so a mark on the seam keeps every chunk
    ///     in the ore, and a mark beside it splits barren rock however cleanly it is worked.
    ///  2. Strike: one weak point at a time, a hairline crack opens briefly. A blow on it while open splits the chunk off
    ///     cleanly. A blow anywhere else fractures the rock, crushing the chunk in front of it (lower quality, the node
    ///     used up faster). A crack left alone closes again and that chunk stays in the rock.
    ///  3. Skill and pick quality keep the cracks open longer and give more reach.
    /// Yield = node size x yield score; quality = vein grade x (0.5 + 0.5 x break score), capped by pick and skill.
    /// Drive it with Update(dt) and the input methods. It never touches UnityEngine.
    /// </summary>
    public sealed class MineMinigame
    {
        public MineSetup Setup { get; }
        public OreNodeType Node => Setup.Node;
        public int Skill => Setup.SkillLevel;

        public MineStage Stage { get; private set; } = MineStage.Read;
        public Seam Seam { get; }
        public double MarkX { get; private set; }
        public double MarkY { get; private set; }

        public IReadOnlyList<WeakPoint> Points => _points;
        /// <summary>Index of the weak point the rock is on now (== Points.Count when finished).</summary>
        public int Current { get; private set; }
        public WeakPoint CurrentPoint => Current < _points.Count ? _points[Current] : null;
        /// <summary>Seconds the current weak point has been open (0 while the rock is settling).</summary>
        public double OpenFor { get; private set; }
        public double OpenSeconds { get; }
        public double HitRadius { get; }
        public int Fractures { get; private set; }
        /// <summary>Where each fracture landed, for drawing the crushed rock.</summary>
        public List<(double X, double Y)> FractureMarks { get; } = new List<(double, double)>();

        public MineResult Result { get; private set; }

        /// <summary>Things that happened since the last time the caller cleared it (for sounds and dust).</summary>
        public List<MineEvent> Events { get; } = new List<MineEvent>();

        private readonly Random _rng;
        private readonly List<WeakPoint> _points = new List<WeakPoint>();
        private double _wait;        // time left before the current point opens
        private double _recovery;    // time left before the pick can swing again

        public MineMinigame(MineSetup setup, int seed)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _rng = new Random(seed);
            OpenSeconds = MineTuning.OpenSeconds(Skill, setup.Pick, setup.Node);
            HitRadius = MineTuning.HitRadius(Skill, setup.Pick);

            double sx = 0.3 + _rng.NextDouble() * 0.4;
            double sy = MineTuning.FaceHeight * (0.25 + _rng.NextDouble() * 0.5);
            double angle = (_rng.NextDouble() * 2 - 1) * MineTuning.MaxSeamTilt;
            Seam = new Seam(sx, sy, angle, MineTuning.SeamHalfWidth(setup.Node, Skill));
        }

        public static bool InsideFace(double x, double y) =>
            x >= 0 && x <= 1 && y >= 0 && y <= MineTuning.FaceHeight;

        private static bool InsideMargin(double x, double y) =>
            x >= MineTuning.Margin && x <= 1 - MineTuning.Margin &&
            y >= MineTuning.Margin && y <= MineTuning.FaceHeight - MineTuning.Margin;

        // ------------------------------------------------------------------ update

        public void Update(double dt)
        {
            if (dt <= 0 || Stage != MineStage.Strike) return;
            if (_recovery > 0) _recovery -= dt;

            var p = CurrentPoint;
            if (p == null) { Complete(); return; }

            if (p.State == PointState.Waiting)
            {
                _wait -= dt;
                if (_wait <= 0)
                {
                    p.State = PointState.Showing;
                    OpenFor = 0;
                    Events.Add(new MineEvent(MineEventKind.Opened, "creak", p.X, p.Y));
                }
            }
            else if (p.State == PointState.Showing)
            {
                OpenFor += dt;
                if (OpenFor >= OpenSeconds)
                {
                    p.State = PointState.Missed;
                    Events.Add(new MineEvent(MineEventKind.Closed, "The crack closes", p.X, p.Y));
                    Advance();
                }
            }
        }

        private void Advance()
        {
            Current++;
            OpenFor = 0;
            _wait = MineTuning.PauseMin + _rng.NextDouble() * (MineTuning.PauseMax - MineTuning.PauseMin);
            if (Current >= _points.Count) Complete();
        }

        // ------------------------------------------------------------------ inputs

        /// <summary>Read stage: choose where to start working the face. The rock splits from here along its grain.</summary>
        public void Mark(double x, double y)
        {
            if (Stage != MineStage.Read || !InsideFace(x, y)) return;
            MarkX = Math.Max(MineTuning.Margin, Math.Min(1 - MineTuning.Margin, x));
            MarkY = Math.Max(MineTuning.Margin, Math.Min(MineTuning.FaceHeight - MineTuning.Margin, y));
            BuildWeakPoints();
            Current = 0;
            _wait = MineTuning.LeadIn;
            Stage = MineStage.Strike;
            Events.Add(new MineEvent(MineEventKind.Marked, "Marked", MarkX, MarkY));
            Events.Add(new MineEvent(MineEventKind.StageChanged, "Strike"));
        }

        /// <summary>
        /// Swing the pick at a spot on the face. On the open crack: a clean split. Anywhere else (or while the rock is
        /// still settling): a fracture that crushes the chunk in front of it.
        /// </summary>
        public void Swing(double x, double y)
        {
            if (Stage != MineStage.Strike || _recovery > 0 || !InsideFace(x, y)) return;
            var p = CurrentPoint;
            if (p == null) return;
            _recovery = MineTuning.SwingRecovery;

            double d = Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (p.State == PointState.Showing && d <= HitRadius)
            {
                p.State = PointState.Clean;
                Events.Add(new MineEvent(MineEventKind.Clean, "Clean split", p.X, p.Y));
            }
            else
            {
                Fractures++;
                FractureMarks.Add((x, y));
                p.State = PointState.Crushed;
                Events.Add(new MineEvent(MineEventKind.Fracture, "The rock fractures", x, y));
            }
            Advance();
        }

        /// <summary>Stop early. The weak points not yet reached stay in the node for the next miner.</summary>
        public void StepBack()
        {
            if (Stage != MineStage.Strike) return;
            for (int i = Current; i < _points.Count; i++) _points[i].State = PointState.Left;
            Current = _points.Count;
            Complete();
        }

        // ------------------------------------------------------------------ weak points

        /// <summary>
        /// Lays out one weak point per swing of the node, starting at the mark and spreading both ways along the
        /// grain (parallel to the seam), with a little wander. Points never leave the face.
        /// </summary>
        private void BuildWeakPoints()
        {
            _points.Clear();
            double gx = Seam.DirX, gy = Seam.DirY;   // grain direction
            double ax = -gy, ay = gx;                 // across the grain

            var ends = new[] { (X: MarkX, Y: MarkY), (X: MarkX, Y: MarkY) };
            bool[] blocked = new bool[2];
            int side = _rng.Next(2);
            Add(MarkX, MarkY);

            int guard = 0;
            while (_points.Count < Setup.NodeSize && guard++ < 500)
            {
                if (_rng.NextDouble() > MineTuning.KeepDirection) side = 1 - side;
                if (blocked[side]) side = 1 - side;

                double sign = side == 0 ? 1 : -1;
                double step = MineTuning.StepMin + _rng.NextDouble() * (MineTuning.StepMax - MineTuning.StepMin);
                double jitter = (_rng.NextDouble() * 2 - 1) * MineTuning.StepJitter;
                double dx = sign * (gx * Math.Cos(jitter) - gy * Math.Sin(jitter));
                double dy = sign * (gx * Math.Sin(jitter) + gy * Math.Cos(jitter));
                double drift = (_rng.NextDouble() * 2 - 1) * MineTuning.DriftAcross;
                double nx = ends[side].X + dx * step + ax * drift;
                double ny = ends[side].Y + dy * step + ay * drift;

                if (!InsideMargin(nx, ny))
                {
                    blocked[side] = true;
                    if (blocked[0] && blocked[1])
                    {
                        // Both ends reached the edge of the face: work a second row just across the grain.
                        var from = _points[_rng.Next(_points.Count)];
                        double off = (_rng.Next(2) == 0 ? 1 : -1) * (MineTuning.StepMin + _rng.NextDouble() * 0.03);
                        double rx = from.X + ax * off, ry = from.Y + ay * off;
                        if (InsideMargin(rx, ry) && !TooClose(rx, ry)) Add(rx, ry);
                    }
                    continue;
                }
                if (TooClose(nx, ny)) continue;
                ends[side] = (nx, ny);
                Add(nx, ny);
            }

            // Last resort for tiny faces: fill anywhere inside the margin.
            while (_points.Count < Setup.NodeSize)
            {
                double rx = MineTuning.Margin + _rng.NextDouble() * (1 - 2 * MineTuning.Margin);
                double ry = MineTuning.Margin + _rng.NextDouble() * (MineTuning.FaceHeight - 2 * MineTuning.Margin);
                Add(rx, ry);
            }
        }

        private bool TooClose(double x, double y)
        {
            foreach (var p in _points)
                if ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) < MineTuning.StepMin * MineTuning.StepMin * 0.5)
                    return true;
            return false;
        }

        private void Add(double x, double y) => _points.Add(new WeakPoint(x, y, Seam.OreContentAt(x, y)));

        // ------------------------------------------------------------------ scoring

        /// <summary>Quality cap before mining: the lower of the pick's cap and what the miner's skill can bring out.</summary>
        public static int ComputeCeiling(MineSetup setup, out string reason)
        {
            int skillCap = MineTuning.SkillCap(setup.SkillLevel);
            if (setup.Pick.QualityCap < skillCap) { reason = $"Tool ({setup.Pick.Name})"; return setup.Pick.QualityCap; }
            reason = $"Skill ({setup.SkillLevel})";
            return skillCap;
        }

        private void Complete()
        {
            if (Stage == MineStage.Done) return;
            var r = new MineResult
            {
                NodeSize = Setup.NodeSize,
                Fractures = Fractures,
                VeinGrade = Setup.VeinGrade,
                MarkOffset = Seam.DistanceTo(MarkX, MarkY) / Seam.HalfWidth,
            };

            double ore = 0;
            foreach (var p in _points)
            {
                switch (p.State)
                {
                    case PointState.Clean: r.Clean++; ore += p.OreContent; break;
                    case PointState.Missed: r.Missed++; break;
                    case PointState.Crushed: r.Crushed++; break;
                    case PointState.Left: r.Left++; break;
                }
            }

            r.Read = r.Clean > 0 ? ore / r.Clean : 0;
            r.YieldScore = Setup.NodeSize > 0 ? ore / Setup.NodeSize : 0;
            r.BreakScore = r.Clean > 0 ? r.Clean / (r.Clean + MineTuning.CrushPerFracture * Fractures) : 0;
            r.Score = r.YieldScore * r.BreakScore;
            r.Yield = (int)Math.Round(Setup.NodeSize * r.YieldScore);

            r.Ceiling = ComputeCeiling(Setup, out r.CeilingReason);
            r.RawQuality = (int)Math.Round(Setup.VeinGrade * (0.5 + 0.5 * r.BreakScore));
            if (r.Yield > 0)
            {
                r.Quality = Math.Max(1, Math.Min(r.Ceiling, r.RawQuality));
                r.Grade = MineTuning.GradeWord(r.Quality);
                r.ItemName = $"{r.Grade} {Node.OreName}";
            }
            else
            {
                r.Quality = 0;
                r.Grade = "";
                r.ItemName = "Only rubble";
            }

            // Skill only grows from nodes that still challenge you.
            if (r.Yield > 0 && Skill < 100 && Skill < Node.RequiredSkill + 30)
                r.SkillGain = r.Score >= 0.6 ? 2 : 1;

            Result = r;
            Stage = MineStage.Done;
            Events.Add(new MineEvent(MineEventKind.Done, r.ItemName));
        }
    }
}
