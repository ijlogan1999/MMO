using System;
using System.Collections.Generic;

namespace Mmo.Crafting.Mining
{
    /// <summary>
    /// The mining minigame as a turn-based knowledge game (no timing, no aiming).
    ///
    /// The face is a wall of blocks split by visible joints and bedding planes. The seam is one bed of it; nothing
    /// marks it except surface signs (which sign means which ore is rolled per world) and how a block rings when tapped.
    ///
    ///  1. Survey: look at the signs, tap blocks (ring = ore, thud = solid rock, hollow = loose) and the roof.
    ///  2. Plan the cut: a block held on both sides and from above is locked. Prying locked ore shatters it, so open
    ///     the face first on barren or loose rock, then take the ore out once it has a free side or a free top.
    ///  3. Tool per blow: pick for small blocks, wedge for large ones, sledge to smash rock (never ore).
    ///  4. Roof: every block out widens the opening; dust and creaks warn; a prop eases it; past the limit it can fall.
    ///  5. Sort: keep or toss each piece in the cart by colour, lustre and heft.
    /// </summary>
    public sealed class MineMinigame
    {
        public MineSetup Setup { get; }
        public OreNodeType Node => Setup.Node;
        public int Skill => Setup.SkillLevel;

        public MineStage Stage { get; private set; } = MineStage.Dig;
        public IReadOnlyList<Block> Blocks => _blocks;
        public int SeamRow { get; }
        public Sign TrueSign { get; }

        public int SwingsLeft { get; private set; } = MineTuning.Swings;
        public int TapsLeft { get; private set; }
        public int PropsLeft { get; private set; } = MineTuning.Props;
        public int PropsSet => MineTuning.Props - PropsLeft;
        /// <summary>Hidden roof stress. The view only shows it through dust and creaks (see DustLevel).</summary>
        public double RoofStress { get; private set; }
        public bool Collapsed { get; private set; }
        /// <summary>What the miner last heard from the roof, if they tapped it.</summary>
        public RoofSound? LastRoofTap { get; private set; }

        /// <summary>0 until the miner would notice anything, rising to 1 as the roof nears its limit and beyond.</summary>
        public double DustLevel
        {
            get
            {
                double notice = MineTuning.NoticeAt(Skill);
                if (RoofStress < notice) return 0;
                return MineTuning.Clamp01(0.25 + (RoofStress - notice) / (MineTuning.SafeStress + 3 - notice));
            }
        }

        /// <summary>Pieces in the cart (Sort stage works on these).</summary>
        public IReadOnlyList<Piece> Cart => _cart;

        public MineResult Result { get; private set; }

        public List<MineEvent> Events { get; } = new List<MineEvent>();

        private readonly Random _rng;
        private readonly List<Block> _blocks = new List<Block>();
        private readonly List<Piece> _cart = new List<Piece>();
        private int _wasted, _swingsUsed, _tapsUsed, _buried;

        public MineMinigame(MineSetup setup, int seed)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _rng = new Random(seed);
            TapsLeft = MineTuning.Taps(Skill);
            TrueSign = SignTable.TrueSign(setup.WorldSeed, setup.Node.Ore);
            SeamRow = 1 + _rng.Next(MineTuning.Rows - 2); // never the top or bottom bed
            BuildFace();
        }

        // ------------------------------------------------------------------ the face

        private void BuildFace()
        {
            int id = 0;
            for (int row = 0; row < MineTuning.Rows; row++)
            {
                int col = 0;
                while (col < MineTuning.Columns)
                {
                    int left = MineTuning.Columns - col;
                    // Two clear sizes: small (a pick job) and large (a wedge job).
                    int w = left < MineTuning.LargeBlock || _rng.NextDouble() < 0.55 ? MineTuning.SmallBlock : MineTuning.LargeBlock;
                    _blocks.Add(MakeBlock(id++, row, col, w));
                    col += w;
                }
            }

            // Every face holds at least three ore blocks.
            int ore = 0;
            foreach (var b in _blocks) if (b.Kind == BlockKind.Ore) ore++;
            for (int i = 0; i < _blocks.Count && ore < 3; i++)
            {
                var b = _blocks[i];
                if (b.Row != SeamRow || b.Kind == BlockKind.Ore) continue;
                _blocks[i] = MakeBlock(b.Id, b.Row, b.Col, b.Width, BlockKind.Ore);
                ore++;
            }
        }

        private Block MakeBlock(int id, int row, int col, int width, BlockKind? force = null)
        {
            BlockKind kind;
            if (force.HasValue) kind = force.Value;
            else if (row == SeamRow) kind = _rng.NextDouble() < MineTuning.SeamOreChance ? BlockKind.Ore : BlockKind.Solid;
            else if (Math.Abs(row - SeamRow) == 1 && _rng.NextDouble() < MineTuning.SpillOreChance) kind = BlockKind.Ore;
            else kind = _rng.NextDouble() < MineTuning.LooseChance ? BlockKind.Loose : BlockKind.Solid;

            var signs = new List<Sign>();
            if (kind == BlockKind.Ore)
            {
                if (_rng.NextDouble() < MineTuning.SignSeen(Skill)) signs.Add(TrueSign);
                if (_rng.NextDouble() < MineTuning.DecoyOnOre) signs.Add(Decoy());
            }
            else
            {
                if (_rng.NextDouble() < MineTuning.TrueSignOnRock) signs.Add(TrueSign);
                if (_rng.NextDouble() < MineTuning.DecoyOnRock) signs.Add(Decoy());
            }

            var pieces = new List<Piece>();
            if (kind == BlockKind.Ore)
            {
                int n = width >= MineTuning.LargeWidth ? MineTuning.LargePieces : MineTuning.SmallPieces;
                double share = MineTuning.OreShare(Setup.VeinGrade);
                for (int i = 0; i < n; i++) pieces.Add(_rng.NextDouble() < share ? OrePiece() : WastePiece());
            }
            return new Block(id, row, col, width, kind, signs, pieces);
        }

        private Sign Decoy()
        {
            Sign s;
            do s = SignTable.AllSigns[_rng.Next(SignTable.AllSigns.Length)]; while (s == TrueSign);
            return s;
        }

        // Real-world tells: iron and copper ores are heavy for their size, coal is light; waste rock is ordinary.
        private Piece OrePiece()
        {
            double shape = _rng.NextDouble();
            switch (Node.Ore)
            {
                case OreKind.Iron:
                    return new Piece(true, _rng.NextDouble() < 0.6 ? PieceColour.Rust : PieceColour.DarkGrey,
                        _rng.NextDouble() < 0.5 ? Lustre.Metallic : Lustre.Dull, Heft.Heavy, shape);
                case OreKind.Coal:
                    return new Piece(true, PieceColour.Black,
                        _rng.NextDouble() < 0.6 ? Lustre.Glassy : Lustre.Dull, Heft.Light, shape);
                default:
                    return new Piece(true, _rng.NextDouble() < 0.6 ? PieceColour.GreenBlue : PieceColour.Brass,
                        _rng.NextDouble() < 0.5 ? Lustre.Metallic : Lustre.Dull, Heft.Heavy, shape);
            }
        }

        private Piece WastePiece()
        {
            double shape = _rng.NextDouble(), r = _rng.NextDouble();
            if (r < 0.4) // plain country rock
                return new Piece(false, _rng.NextDouble() < 0.5 ? PieceColour.Grey : PieceColour.Brown, Lustre.Dull, Heft.Ordinary, shape);
            if (r < 0.75) // rock stained the ore's colour: the classic fool
            {
                var c = Node.Ore == OreKind.Iron ? PieceColour.Rust : Node.Ore == OreKind.Coal ? PieceColour.Black : PieceColour.GreenBlue;
                return new Piece(false, c, Lustre.Dull, Heft.Ordinary, shape);
            }
            return new Piece(false, PieceColour.White, Lustre.Glassy, Heft.Ordinary, shape); // quartz
        }

        public Block BlockAt(int row, int col)
        {
            foreach (var b in _blocks)
                if (b.Row == row && col >= b.Col && col < b.End) return b;
            return null;
        }

        public Block BlockById(int id) => id >= 0 && id < _blocks.Count ? _blocks[id] : null;

        /// <summary>
        /// Held on the left, the right and from above. The side walls and the roof count as holding.
        /// A locked block can't be pried out; ore pried while locked shatters.
        /// </summary>
        public bool IsLocked(Block b)
        {
            bool leftHeld = b.Col == 0 || (BlockAt(b.Row, b.Col - 1)?.InPlace ?? false);
            bool rightHeld = b.End >= MineTuning.Columns || (BlockAt(b.Row, b.End)?.InPlace ?? false);
            bool topHeld = b.Row == 0;
            if (!topHeld)
            {
                bool open = false;
                for (int c = b.Col; c < b.End; c++)
                    if (!(BlockAt(b.Row - 1, c)?.InPlace ?? true)) { open = true; break; }
                topHeld = !open;
            }
            return leftHeld && rightHeld && topHeld;
        }

        // ------------------------------------------------------------------ inputs

        /// <summary>Tap a block with the back of the pick. Free of swings, but the miner only has so many taps in them.</summary>
        public TapSound Tap(int blockId)
        {
            var b = BlockById(blockId);
            if (Stage != MineStage.Dig || b == null || !b.InPlace || TapsLeft <= 0) return TapSound.None;
            if (b.Tapped != TapSound.None) return b.Tapped;
            TapsLeft--;
            _tapsUsed++;
            b.Tapped = b.Kind switch
            {
                BlockKind.Ore => TapSound.Ring,
                BlockKind.Loose => TapSound.Hollow,
                _ => TapSound.Thud,
            };
            Events.Add(new MineEvent(MineEventKind.Tapped, b.Tapped.ToString().ToLowerInvariant(), b.Id));
            return b.Tapped;
        }

        /// <summary>Tap the roof: solid, drummy or very drummy.</summary>
        public RoofSound? TapRoof()
        {
            if (Stage != MineStage.Dig || TapsLeft <= 0) return null;
            TapsLeft--;
            _tapsUsed++;
            LastRoofTap = MineTuning.RoofSoundFor(RoofStress);
            Events.Add(new MineEvent(MineEventKind.RoofTapped, LastRoofTap.ToString()));
            return LastRoofTap;
        }

        /// <summary>Work a block with the pick, wedge or sledge. Costs one swing whatever happens.</summary>
        public void Work(Tool tool, int blockId)
        {
            if (tool == Tool.Tap) { Tap(blockId); return; }
            var b = BlockById(blockId);
            if (Stage != MineStage.Dig || b == null || !b.InPlace || SwingsLeft <= 0) return;
            SwingsLeft--;
            _swingsUsed++;

            bool locked = IsLocked(b);
            bool ore = b.Kind == BlockKind.Ore;

            if (tool == Tool.Sledge)
            {
                RoofStress += MineTuning.SledgeStress;
                if (ore) Take(b, BlockState.Crushed, MineEventKind.Shattered, "The sledge shatters the ore");
                else Take(b, BlockState.Removed, MineEventKind.Smashed, "The rock breaks out");
            }
            else if (b.Kind == BlockKind.Loose)
            {
                Take(b, BlockState.Removed, MineEventKind.Pried, "The loose rock comes away");
            }
            else if (locked)
            {
                if (ore) Take(b, BlockState.Crushed, MineEventKind.Shattered, "Held tight, the ore shatters");
                else Waste(b, "The rock is held fast");
            }
            else
            {
                bool rightTool = tool == Tool.Wedge ? b.IsLarge : !b.IsLarge;
                if (ore)
                {
                    if (rightTool) Take(b, BlockState.Clean, MineEventKind.Freed, "The ore comes out whole");
                    else Take(b, BlockState.Crushed, MineEventKind.Shattered,
                        tool == Tool.Wedge ? "The wedge splits it to pieces" : "Too big to pry: you hack it apart");
                }
                else if (rightTool) Take(b, BlockState.Removed, MineEventKind.Pried, "The rock comes out");
                else Waste(b, tool == Tool.Wedge ? "Too small for the wedge to bite" : "Too big to pry");
            }

            AfterSwing();
        }

        /// <summary>Set a timber prop. Costs a swing and eases the roof.</summary>
        public void Prop()
        {
            if (Stage != MineStage.Dig || PropsLeft <= 0 || SwingsLeft <= 0) return;
            PropsLeft--;
            SwingsLeft--;
            _swingsUsed++;
            RoofStress = Math.Max(0, RoofStress - MineTuning.PropRelief);
            Events.Add(new MineEvent(MineEventKind.Propped, "Prop set"));
            AfterSwing();
        }

        /// <summary>Stop digging and sort the cart.</summary>
        public void FinishDigging()
        {
            if (Stage != MineStage.Dig) return;
            Stage = MineStage.Sort;
            Events.Add(new MineEvent(MineEventKind.StageChanged, "Sort"));
        }

        /// <summary>Sort stage: flip a piece between keep and toss.</summary>
        public void ToggleKeep(int index)
        {
            if (Stage != MineStage.Sort || index < 0 || index >= _cart.Count) return;
            _cart[index].Keep = !_cart[index].Keep;
        }

        public void SetKeep(int index, bool keep)
        {
            if (Stage != MineStage.Sort || index < 0 || index >= _cart.Count) return;
            _cart[index].Keep = keep;
        }

        public void FinishSorting()
        {
            if (Stage != MineStage.Sort) return;
            Complete();
        }

        // ------------------------------------------------------------------ internals

        private void Take(Block b, BlockState state, MineEventKind kind, string message)
        {
            b.State = state;
            RoofStress += MineTuning.RemoveStress + (b.Row == 0 ? MineTuning.TopRowStress : 0);
            if (state == BlockState.Clean || state == BlockState.Crushed)
            {
                foreach (var p in b.Pieces)
                {
                    if (state == BlockState.Crushed) p.Crushed = true;
                    _cart.Add(p);
                }
            }
            Events.Add(new MineEvent(kind, message, b.Id));
        }

        private void Waste(Block b, string message)
        {
            _wasted++;
            Events.Add(new MineEvent(MineEventKind.Bounced, message, b.Id));
        }

        private double _lastCreak;

        private void AfterSwing()
        {
            if (DustLevel > 0 && RoofStress > _lastCreak)
            {
                _lastCreak = RoofStress;
                Events.Add(new MineEvent(MineEventKind.Creak, "The roof creaks"));
            }

            if (RoofStress > MineTuning.SafeStress &&
                _rng.NextDouble() < (RoofStress - MineTuning.SafeStress) * MineTuning.CollapsePerStress)
            {
                Collapsed = true;
                int bury = (int)Math.Round(_cart.Count * MineTuning.CollapseLoss);
                for (int i = 0; i < bury && _cart.Count > 0; i++) _cart.RemoveAt(_rng.Next(_cart.Count));
                _buried = bury;
                Events.Add(new MineEvent(MineEventKind.Collapse, "The roof comes in! You scramble clear with what's in the cart."));
                Stage = MineStage.Sort;
                Events.Add(new MineEvent(MineEventKind.StageChanged, "Sort"));
                return;
            }

            if (SwingsLeft <= 0) FinishDigging();
        }

        // ------------------------------------------------------------------ scoring

        /// <summary>Quality cap: the lower of the pick's cap and what the miner's skill can bring out.</summary>
        public static int ComputeCeiling(MineSetup setup, out string reason)
        {
            int skillCap = MineTuning.SkillCap(setup.SkillLevel);
            if (setup.Pick.QualityCap < skillCap) { reason = $"Tool ({setup.Pick.Name})"; return setup.Pick.QualityCap; }
            reason = $"Skill ({setup.SkillLevel})";
            return skillCap;
        }

        private void Complete()
        {
            var r = new MineResult
            {
                VeinGrade = Setup.VeinGrade,
                WastedSwings = _wasted,
                SwingsUsed = _swingsUsed,
                TapsUsed = _tapsUsed,
                PropsUsed = PropsSet,
                Collapsed = Collapsed,
                PiecesBuried = _buried,
            };

            int cleanOre = 0, allOre = 0;
            foreach (var b in _blocks)
            {
                if (b.Kind == BlockKind.Ore)
                {
                    r.OreBlocks++;
                    foreach (var p in b.Pieces) if (p.IsOre) r.NodeOre++;
                    if (b.State == BlockState.Clean) r.OreFreed++;
                    if (b.State == BlockState.Crushed) r.OreShattered++;
                    if (b.State != BlockState.InPlace)
                        foreach (var p in b.Pieces) if (p.IsOre) { allOre++; if (b.State == BlockState.Clean) cleanOre++; }
                }
                else if (b.State == BlockState.Removed) r.RockRemoved++;
            }

            int right = 0;
            foreach (var p in _cart)
            {
                if (p.IsOre && p.Keep) { if (p.Crushed) r.CrushedKept++; else r.OreKept++; }
                else if (p.IsOre) r.OreTossed++;
                else if (p.Keep) r.WasteKept++;
                else r.WasteTossed++;
                if (p.IsOre == p.Keep) right++;
            }
            r.SortScore = _cart.Count > 0 ? right / (double)_cart.Count : 0;

            r.OreTaken = r.OreKept + 0.5 * r.CrushedKept;
            r.Yield = (int)Math.Round(r.OreTaken);
            r.YieldScore = r.NodeOre > 0 ? r.OreTaken / r.NodeOre : 0;
            r.BreakScore = allOre > 0 ? cleanOre / (double)allOre : 0;
            int kept = r.OreKept + r.CrushedKept + r.WasteKept;
            r.Purity = kept > 0 ? (r.OreKept + r.CrushedKept) / (double)kept : 0;
            r.Score = r.YieldScore * r.BreakScore * r.Purity;

            r.Ceiling = ComputeCeiling(Setup, out r.CeilingReason);
            r.RawQuality = (int)Math.Round(Setup.VeinGrade * (0.5 + 0.5 * r.BreakScore) * (0.6 + 0.4 * r.Purity));
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
                r.ItemName = "Nothing worth carrying";
            }

            // What the face taught: each sign seen, and how often it sat on ore here.
            var tally = new Dictionary<Sign, (int Ore, int Rock)>();
            foreach (var b in _blocks)
                foreach (var s in b.Signs)
                {
                    tally.TryGetValue(s, out var v);
                    tally[s] = b.Kind == BlockKind.Ore ? (v.Ore + 1, v.Rock) : (v.Ore, v.Rock + 1);
                }
            foreach (var kv in tally)
                r.Learned.Add($"{SignTable.Describe(kv.Key)}: on ore {kv.Value.Ore}, on barren rock {kv.Value.Rock}");

            if (r.Yield > 0 && Skill < 100 && Skill < Node.RequiredSkill + 30)
                r.SkillGain = r.Score >= 0.5 ? 2 : 1;

            Result = r;
            Stage = MineStage.Done;
            Events.Add(new MineEvent(MineEventKind.Done, r.ItemName));
        }
    }
}
