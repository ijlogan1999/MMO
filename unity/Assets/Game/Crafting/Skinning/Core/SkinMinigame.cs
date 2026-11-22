using System;
using System.Collections.Generic;

namespace Mmo.Crafting.Skinning
{
    /// <summary>
    /// Skinning and processing a kill, as a turn-based knowledge game (no timing, no aiming). Hunting itself is
    /// ordinary combat; this starts with the animal dead at your feet.
    ///
    ///  1. Open: slit the belly and legs (open, the hide comes off flat) or only across the hind legs (cased, the pelt
    ///     peels off whole like a sleeve). Fur-bearers are worth more cased; anything big tears if you try.
    ///  2. Skin: work the hide off patch by patch from an open edge. Feel how a patch holds; pull loose hide off by
    ///     hand, cut clinging, tight or fatty hide free with the knife. Pulling the wrong patch stretches or tears it;
    ///     cutting loose hide scores it. Knife around wounds. Every bit of work lets the body cool, and a cooling body
    ///     holds its hide harder. Gut it in time or the meat sours; the knife dulls and needs honing.
    ///  3. Flesh: on the beam, scrape off film and fat, trim off meat, and leave clean hide alone (thin skin tears).
    ///  4. Keep: salt it, stretch and dry it, or roll it green for the tanner, by what it is and the season.
    /// </summary>
    public sealed class SkinMinigame
    {
        public SkinSetup Setup { get; }
        public Species Species => Setup.Species;
        public int Skill => Setup.SkillLevel;

        public SkinStage Stage { get; private set; } = SkinStage.Open;
        public SkinStyle Style { get; private set; } = SkinStyle.Open;
        /// <summary>Cased and not since slit open to gut it.</summary>
        public bool TubeKept { get; private set; }
        public IReadOnlyList<Patch> Patches => _patches;

        public int FeelsLeft { get; private set; }
        /// <summary>Units of work done. Hidden from the player: they see steam fade and the belly swell instead.</summary>
        public int Time { get; private set; }
        public bool Gutted { get; private set; }
        public int GutAt { get; private set; } = -1;
        /// <summary>Hidden knife edge 1..0. The player only learns of it when they notice it drag, or when it fails.</summary>
        public double Edge { get; private set; } = 1.0;
        public int Hones { get; private set; }
        public Preserve Preserve { get; private set; } = Preserve.None;

        public SkinResult Result { get; private set; }
        public List<SkinEvent> Events { get; } = new List<SkinEvent>();

        private readonly Random _rng;
        private readonly List<Patch> _patches = new List<Patch>();
        private readonly HashSet<int> _opened = new HashSet<int>();
        private bool _dragNoticed, _swellNoticed;
        private int _scrapes, _trims, _skids, _woundsFound, _woundsAbsorbed;

        public SkinMinigame(SkinSetup setup, int seed)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _rng = new Random(seed);
            FeelsLeft = SkinTuning.Feels(Skill);
            BuildHide();
        }

        // ------------------------------------------------------------------ the carcass

        /// <summary>Which region each grid cell is (null = no hide there).</summary>
        public static Region? RegionAt(int row, int col)
        {
            switch (row)
            {
                case 0: return col >= 1 && col <= 3 ? Region.Neck : (Region?)null;
                case 1: return col == 0 || col == 4 ? Region.Foreleg : col == 2 ? Region.Chest : Region.Shoulder;
                case 2:
                case 3: return col >= 1 && col <= 3 ? (col == 2 ? Region.Belly : Region.Ribs) : (Region?)null;
                case 4: return col == 0 || col == 4 ? Region.Hindleg : col == 2 ? Region.Belly : Region.Haunch;
                case 5: return col == 2 ? Region.Tail : (Region?)null;
                default: return null;
            }
        }

        private void BuildHide()
        {
            int id = 0;
            for (int row = 0; row < SkinTuning.Rows; row++)
                for (int col = 0; col < SkinTuning.Columns; col++)
                {
                    var region = RegionAt(row, col);
                    if (region == null) continue;
                    var hold = Species.HoldOf(region.Value);
                    if (_rng.NextDouble() < SkinTuning.HoldVariance)
                        hold = (Hold)Math.Max(0, Math.Min(2, (int)hold + (_rng.NextDouble() < 0.5 ? -1 : 1)));
                    _patches.Add(new Patch(id++, row, col, region.Value, hold,
                        Species.FatOn(region.Value), Species.ThinOn(region.Value), false, false));
                }

            // Wounds from the fight, never two on one patch. Some show through the fur; the rest have to be found.
            int wounds = Math.Max(0, Math.Min(Setup.Wounds, _patches.Count));
            var pool = new List<int>();
            for (int i = 0; i < _patches.Count; i++) pool.Add(i);
            for (int w = 0; w < wounds; w++)
            {
                int k = _rng.Next(pool.Count);
                int i = pool[k];
                pool.RemoveAt(k);
                var p = _patches[i];
                _patches[i] = new Patch(p.Id, p.Row, p.Col, p.Region, p.BaseHold, p.Fat, p.Thin, true,
                    _rng.NextDouble() < SkinTuning.WoundSeen(Skill));
            }
        }

        public Patch PatchAt(int row, int col)
        {
            foreach (var p in _patches) if (p.Row == row && p.Col == col) return p;
            return null;
        }

        public Patch PatchById(int id) => id >= 0 && id < _patches.Count ? _patches[id] : null;

        /// <summary>An opening cut runs through this patch.</summary>
        public bool IsOpened(Patch p) => _opened.Contains(p.Id);

        /// <summary>The hide can be worked here: a cut runs through it, or a neighbour is already off.</summary>
        public bool IsWorkable(Patch p)
        {
            if (p.State != PatchState.OnBody) return false;
            if (_opened.Contains(p.Id)) return true;
            return (PatchAt(p.Row - 1, p.Col)?.IsOff ?? false) || (PatchAt(p.Row + 1, p.Col)?.IsOff ?? false) ||
                   (PatchAt(p.Row, p.Col - 1)?.IsOff ?? false) || (PatchAt(p.Row, p.Col + 1)?.IsOff ?? false);
        }

        /// <summary>1 when freshly killed, falling with work done. Shown only as steam rising off the body.</summary>
        public double Warmth =>
            SkinTuning.Clamp01(1 - Time * Species.CoolRate * SkinTuning.SeasonCooling(Setup.Season));

        /// <summary>The carcass has set: knife work drags meat off with the hide.</summary>
        public bool IsSet => Warmth < SkinTuning.SetBelow;

        /// <summary>How the patch holds right now: its warm hold, one step harder once cooling, two once set.</summary>
        public Hold HoldNow(Patch p)
        {
            int steps = (int)p.BaseHold + (Warmth < SkinTuning.ClingBelow ? 1 : 0) + (IsSet ? 1 : 0);
            return (Hold)Math.Min(2, steps);
        }

        public int SpoilPoint => Math.Max(1, Species.SpoilAfter + SkinTuning.SeasonSpoilShift(Setup.Season));

        /// <summary>0 until the skinner would notice the belly swelling, rising to 1 as it sours. Gutting ends it.</summary>
        public double Swelling
        {
            get
            {
                if (Gutted || Species.Meat == 0) return 0;
                int notice = SpoilPoint - SkinTuning.SwellNoticeLead(Skill);
                if (Time < notice) return 0;
                return SkinTuning.Clamp01(0.2 + (Time - notice) / 8.0);
            }
        }

        // ------------------------------------------------------------------ open

        /// <summary>Make the opening cuts.</summary>
        public void ChooseStyle(SkinStyle style)
        {
            if (Stage != SkinStage.Open) return;
            Style = style;
            TubeKept = style == SkinStyle.Cased;
            foreach (var p in _patches)
            {
                bool cut = style == SkinStyle.Open
                    ? p.OnMidline || p.Region == Region.Foreleg || p.Region == Region.Hindleg
                    : p.Region == Region.Hindleg || p.Region == Region.Tail;
                if (cut) Open(p);
            }
            Stage = SkinStage.Skin;
            Events.Add(new SkinEvent(SkinEventKind.StyleChosen,
                style == SkinStyle.Open ? "You cut along the belly from neck to tail and down each leg"
                                        : "You cut across the hind legs, under the tail"));
            Events.Add(new SkinEvent(SkinEventKind.StageChanged, "Skin"));
        }

        private void Open(Patch p)
        {
            _opened.Add(p.Id);
        }

        /// <summary>A wound the opening cut runs straight through costs nothing.</summary>
        private bool WoundCounts(Patch p) => p.Wound && !_opened.Contains(p.Id);

        // ------------------------------------------------------------------ skin

        /// <summary>Feel a patch with your fingers. Costs no time, but you only have so much patience for it.</summary>
        public Hold? Feel(int patchId)
        {
            var p = PatchById(patchId);
            if (Stage != SkinStage.Skin || p == null || p.State != PatchState.OnBody || FeelsLeft <= 0) return null;
            FeelsLeft--;
            var hold = HoldNow(p);
            p.Felt = hold;
            p.FeltFat = p.Fat;
            string msg = $"The hide at the {SkinTuning.Describe(p.Region)} {SkinTuning.Describe(hold)}";
            if (p.Fat) msg += ", over a thick layer of fat";
            if (p.Wound)
            {
                if (!p.WoundSeen) msg += ". There's a hole from the fight under the fur";
                p.WoundSeen = true;
            }
            Events.Add(new SkinEvent(SkinEventKind.Felt, msg, p.Id));
            return hold;
        }

        /// <summary>Pull or cut a patch free. Costs time whatever happens.</summary>
        public void Work(SkinTool tool, int patchId)
        {
            if (tool == SkinTool.Feel) { Feel(patchId); return; }
            var p = PatchById(patchId);
            if (Stage != SkinStage.Skin || p == null || p.State != PatchState.OnBody) return;

            bool workable = IsWorkable(p);
            bool wound = WoundCounts(p);
            if (wound && p.WoundSeen) _woundsFound++;
            if (p.Wound && !wound) _woundsAbsorbed++;
            var hold = HoldNow(p);
            bool set = IsSet;

            if (tool == SkinTool.Pull)
            {
                if (!workable) Finish(p, PatchState.Torn, Layer.Membrane, SkinEventKind.Tore, "Nothing to grip there, and it tears");
                else if (wound) { p.WoundSeen = true; Finish(p, PatchState.Torn, Layer.Membrane, SkinEventKind.Tore, "The hole from the fight tears wider"); }
                else if (p.Fat) Finish(p, PatchState.Torn, Layer.Fat, SkinEventKind.Tore, "The fat holds it and the hide tears");
                else if (hold == Hold.Loose)
                    Finish(p, PatchState.Clean, _rng.NextDouble() < 0.5 ? Layer.Clean : Layer.Membrane, SkinEventKind.CameAway, "It peels away clean");
                else if (hold == Hold.Clinging)
                {
                    if (p.Thin) Finish(p, PatchState.Torn, Layer.Membrane, SkinEventKind.Tore, "The thin skin gives way and tears");
                    else Finish(p, PatchState.Stretched, Layer.Membrane, SkinEventKind.Stretched, "It comes, but stretches as it goes");
                }
                else Finish(p, PatchState.Torn, Layer.Membrane, SkinEventKind.Tore, "It's held tight and tears");
                Spend(SkinTuning.PullCost);
                return;
            }

            // Knife
            bool dull = Edge < SkinTuning.DullBelow;
            Edge = Math.Max(0, Edge - Setup.Knife.DullPerStroke * (p.Fat ? SkinTuning.FatDull : 1));
            var under = p.Fat ? Layer.Fat : set ? Layer.Meat : Layer.Membrane;
            if (!workable) Finish(p, PatchState.Scored, under, SkinEventKind.Scored, "Cutting blind, you score the hide");
            else if (dull)
            {
                if (wound) { p.WoundSeen = true; Finish(p, PatchState.Torn, under, SkinEventKind.Tore, "The dull blade catches the hole and tears it wider"); }
                else Finish(p, PatchState.Scored, under, SkinEventKind.Scored, "The dull blade skips and scores the hide");
            }
            else if (wound) { p.WoundSeen = true; Finish(p, PatchState.Holed, under, SkinEventKind.Holed, "You cut neatly around the hole"); }
            else if (hold == Hold.Loose && !p.Fat)
                Finish(p, PatchState.Scored, under, SkinEventKind.Scored, "The loose hide flaps against the blade and you score it");
            else Finish(p, PatchState.Clean, under, SkinEventKind.CameAway,
                set ? "It cuts free, but it's gone stiff and bits stay stuck to it" : p.Fat ? "You cut it free of the fat" : "It cuts free clean");
            Spend(SkinTuning.KnifeCost);
            if (!_dragNoticed && Edge < SkinTuning.DragNoticeAt(Skill))
            {
                _dragNoticed = true;
                Events.Add(new SkinEvent(SkinEventKind.Drag, "The knife has started to drag"));
            }
        }

        /// <summary>Hone the knife on the stone. Takes a moment; the edge comes back.</summary>
        public void Hone()
        {
            if (Stage != SkinStage.Skin) return;
            Edge = 1.0;
            Hones++;
            _dragNoticed = false;
            Events.Add(new SkinEvent(SkinEventKind.Honed, "You hone the edge"));
            Spend(SkinTuning.HoneCost);
        }

        /// <summary>Gut the carcass. A cased skin still on the body has to be slit open to do it.</summary>
        public void Gut()
        {
            if (Stage != SkinStage.Skin || Gutted) return;
            DoGut();
            Spend(SkinTuning.GutCost);
        }

        private void DoGut()
        {
            Gutted = true;
            GutAt = Time;
            if (Style == SkinStyle.Cased)
            {
                bool slit = false;
                foreach (var p in _patches)
                    if (p.OnMidline && p.Region == Region.Belly && p.State == PatchState.OnBody) { Open(p); slit = true; }
                if (slit)
                {
                    TubeKept = false;
                    Events.Add(new SkinEvent(SkinEventKind.Gutted, "You open the belly to field-dress it. The pelt won't come off as a tube now"));
                    return;
                }
            }
            Events.Add(new SkinEvent(SkinEventKind.Gutted, "You field-dress it"));
        }

        /// <summary>Stop skinning: whatever is still on the body stays there.</summary>
        public void StopSkinning()
        {
            if (Stage != SkinStage.Skin) return;
            foreach (var p in _patches) if (p.State == PatchState.OnBody) p.State = PatchState.Left;
            ToFlesh();
        }

        private void Finish(Patch p, PatchState state, Layer layer, SkinEventKind kind, string message)
        {
            p.State = state;
            p.Layer = layer;
            Events.Add(new SkinEvent(kind, message, p.Id));
        }

        private void Spend(int units)
        {
            Time += units;
            if (!_swellNoticed && Swelling > 0)
            {
                _swellNoticed = true;
                Events.Add(new SkinEvent(SkinEventKind.Swelling, "The meat won't keep much longer"));
            }
            bool allOff = true;
            foreach (var p in _patches) if (p.State == PatchState.OnBody) { allOff = false; break; }
            if (allOff) ToFlesh();
        }

        private void ToFlesh()
        {
            if (!Gutted) DoGut(); // gutted once the hide is off, at whatever time that is
            Stage = SkinStage.Flesh;
            Events.Add(new SkinEvent(SkinEventKind.StageChanged, "Flesh"));
        }

        // ------------------------------------------------------------------ flesh

        /// <summary>Work a patch on the beam. Scraping takes off film and fat; the knife trims off meat and fat.</summary>
        public void FleshWork(FleshTool tool, int patchId)
        {
            var p = PatchById(patchId);
            if (Stage != SkinStage.Flesh || p == null || !p.IsOff) return;
            if (tool == FleshTool.Scrape)
            {
                _scrapes++;
                switch (p.Layer)
                {
                    case Layer.Membrane: p.Layer = Layer.Clean; Beam(SkinEventKind.Scraped, "The film scrapes away", p); break;
                    case Layer.Fat: p.Layer = Layer.Membrane; Beam(SkinEventKind.Scraped, "The fat comes off in curls", p); break;
                    case Layer.Meat: _skids++; Beam(SkinEventKind.Skid, "The scraper skids over the stuck bits", p); break;
                    default:
                        if (p.Thin || p.Mark == FleshMark.Thinned) { p.Mark = FleshMark.Torn; Beam(SkinEventKind.Tore, "You scrape straight through the skin", p); }
                        else { p.Mark = FleshMark.Thinned; Beam(SkinEventKind.Scraped, "You scrape into bare hide and thin it", p); }
                        break;
                }
            }
            else
            {
                _trims++;
                switch (p.Layer)
                {
                    case Layer.Meat: p.Layer = Layer.Membrane; Beam(SkinEventKind.Trimmed, "You trim the bits away", p); break;
                    case Layer.Fat: p.Layer = Layer.Membrane; Beam(SkinEventKind.Trimmed, "You slice the fat away", p); break;
                    default:
                        p.Mark = p.Mark == FleshMark.Scored || p.Mark == FleshMark.Torn ? FleshMark.Torn : FleshMark.Scored;
                        Beam(SkinEventKind.Scored, p.Mark == FleshMark.Torn ? "The blade goes through" : "The blade bites into the hide", p);
                        break;
                }
            }
        }

        private void Beam(SkinEventKind kind, string message, Patch p) => Events.Add(new SkinEvent(kind, message, p.Id));

        public void FinishFleshing()
        {
            if (Stage != SkinStage.Flesh) return;
            Stage = SkinStage.Preserve;
            Events.Add(new SkinEvent(SkinEventKind.StageChanged, "Preserve"));
        }

        // ------------------------------------------------------------------ keep

        public void Keep(Preserve how)
        {
            if (Stage != SkinStage.Preserve || how == Preserve.None) return;
            Preserve = how;
            Complete();
        }

        // ------------------------------------------------------------------ scoring

        /// <summary>Hide quality cap: the lower of the knife's cap and what the skinner's skill can bring out.</summary>
        public static int ComputeCeiling(SkinSetup setup, out string reason)
        {
            int skillCap = SkinTuning.SkillCap(setup.SkillLevel);
            if (setup.Knife.QualityCap < skillCap) { reason = $"Tool ({setup.Knife.Name})"; return setup.Knife.QualityCap; }
            reason = $"Skill ({setup.SkillLevel})";
            return skillCap;
        }

        private void Complete()
        {
            var s = Setup;
            var r = new SkinResult
            {
                Style = Style,
                TubeKept = TubeKept,
                Patches = _patches.Count,
                Hones = Hones,
                TimeUsed = Time,
                GutAt = GutAt,
                SpoilPoint = SpoilPoint,
                WoundsFound = _woundsFound,
                WoundsAbsorbed = _woundsAbsorbed,
                Scrapes = _scrapes,
                Trims = _trims,
                Skids = _skids,
                Preserve = Preserve,
                AnimalGrade = s.AnimalGrade,
                HideUnits = Species.HideUnits,
                Extras = Species.Extras,
            };

            double skinSum = 0, fleshSum = 0;
            int off = 0;
            foreach (var p in _patches)
            {
                skinSum += SkinTuning.PatchValue(p.State);
                switch (p.State)
                {
                    case PatchState.Clean: r.Clean++; break;
                    case PatchState.Stretched: r.Stretched++; break;
                    case PatchState.Scored: r.Scored++; break;
                    case PatchState.Holed: r.Holed++; break;
                    case PatchState.Torn: r.Torn++; break;
                    default: r.Left++; break;
                }
                if (!p.IsOff) continue;
                off++;
                fleshSum += SkinTuning.LayerValue(p.Layer) * SkinTuning.MarkValue(p.Mark);
                switch (p.Layer)
                {
                    case Layer.Clean: r.LayerClean++; break;
                    case Layer.Membrane: r.LayerMembrane++; break;
                    case Layer.Fat: r.LayerFat++; break;
                    default: r.LayerMeat++; break;
                }
                if (p.Mark == FleshMark.Thinned) r.Thinned++;
                else if (p.Mark == FleshMark.Scored) r.ScoredOnBeam++;
                else if (p.Mark == FleshMark.Torn) r.TornOnBeam++;
            }

            r.HideShare = off / (double)_patches.Count;
            r.SkinScore = skinSum / _patches.Count;
            r.FleshScore = off > 0 ? fleshSum / off : 0;
            r.HideScore = r.SkinScore * r.FleshScore;
            r.StyleFactor = SkinTuning.StyleFactor(Species, Style, TubeKept);
            r.PreserveFactor = SkinTuning.PreserveFactor(Species, Preserve, s.Season);
            r.SeasonHide = SkinTuning.SeasonHide(s.Season);
            r.Ceiling = ComputeCeiling(s, out r.CeilingReason);
            r.HideRaw = (int)Math.Round(s.AnimalGrade * (0.4 + 0.6 * r.HideScore) * r.StyleFactor * r.PreserveFactor * r.SeasonHide);

            if (r.HideShare >= SkinTuning.MinHideShare)
            {
                r.HideQuality = Math.Max(1, Math.Min(r.Ceiling, r.HideRaw));
                r.HideGrade = SkinTuning.GradeWord(r.HideQuality);
                r.HideItem = SkinTuning.HideSeasonWord(s.Season) + Species.HideName;
            }
            else
            {
                r.HideQuality = 0;
                r.HideGrade = "";
                r.HideItem = "Hide scraps";
            }

            // Meat: sours if the body wasn't gutted in time. The knife doesn't touch meat quality; skill caps it.
            r.MeatFactor = GutAt <= SpoilPoint ? 1.0
                : Math.Max(SkinTuning.SpoilFloor, 1 - (GutAt - SpoilPoint) * SkinTuning.SpoilPerUnit);
            r.SeasonMeat = SkinTuning.SeasonMeat(s.Season);
            r.MeatYield = Species.Meat;
            if (Species.Meat > 0)
            {
                r.MeatRaw = (int)Math.Round(s.AnimalGrade * r.MeatFactor * r.SeasonMeat);
                r.MeatQuality = Math.Max(1, Math.Min(SkinTuning.SkillCap(s.SkillLevel), r.MeatRaw));
                r.MeatGrade = SkinTuning.GradeWord(r.MeatQuality);
                r.MeatItem = SkinTuning.MeatSeasonWord(s.Season) + Species.MeatName;
            }
            else
            {
                r.MeatItem = "";
                r.MeatGrade = "";
            }

            Explain(r);

            if (r.HideQuality > 0 && Skill < 100 && Skill < Species.RequiredSkill + 30)
                r.SkillGain = r.HideScore >= 0.7 ? 2 : 1;

            Result = r;
            Stage = SkinStage.Done;
            Events.Add(new SkinEvent(SkinEventKind.Done, r.HideItem));
        }

        /// <summary>Plain-words lessons for the results screen, worst first.</summary>
        private void Explain(SkinResult r)
        {
            var n = r.Notes;
            if (Style == SkinStyle.Cased && !Species.CanCase)
                n.Add($"A {Species.Name.ToLowerInvariant()} is too big to case: the tube tore over the shoulders.");
            else if (Species.FurBearer && !TubeKept)
                n.Add(Style == SkinStyle.Cased
                    ? "Opening the belly to field-dress it before the pelt was off spoiled the tube. A furrier pays more for a whole one."
                    : "A furrier pays more for a cased pelt, peeled off whole.");

            int pulledTight = 0, pulledFat = 0, pulledWound = 0, knifedLoose = 0, set = 0;
            foreach (var p in _patches)
            {
                if (p.State == PatchState.Torn && p.Fat) pulledFat++;
                else if (p.State == PatchState.Torn && WoundCounts(p)) pulledWound++;
                else if (p.State == PatchState.Torn) pulledTight++;
                if (p.State == PatchState.Scored) knifedLoose++;
                if (p.IsOff && p.Layer == Layer.Meat) set++;
            }
            if (pulledFat > 0) n.Add($"{pulledFat} patch(es) over fat were pulled and tore. Fat only lets go to the knife.");
            if (pulledWound > 0) n.Add($"{pulledWound} hole(s) from the fight tore wider. Cut around a hole; pulling tears it.");
            if (pulledTight > 0) n.Add($"{pulledTight} patch(es) tore: held too tight to pull, thin skin pulled while it clung, or pulled with no open edge to work from.");
            if (r.Stretched > 0) n.Add($"{r.Stretched} patch(es) stretched: they clung, and a pull drags them thin. The knife frees clinging hide clean.");
            if (knifedLoose > 0) n.Add($"{knifedLoose} patch(es) scored by the knife: loose hide flaps against the blade, a dull edge skips, and cutting where there's no open edge is cutting blind.");
            if (set > 0) n.Add($"{set} patch(es) came off with bits stuck on: the body had gone cold and stiff before you got to them.");
            if (r.Left > 0) n.Add($"{r.Left} patch(es) left on the body.");
            if (r.GuttedLate && Species.Meat > 0) n.Add($"The meat spoiled before it was field-dressed (dressed after {r.GutAt} units of work; it would keep for {r.SpoilPoint}).");
            if (r.LayerFat + r.LayerMeat > 0) n.Add($"{r.LayerFat + r.LayerMeat} patch(es) went into keeping with fat or bits still on. It spoils and the tan won't take.");
            if (r.Thinned + r.TornOnBeam > 0) n.Add($"{r.Thinned + r.TornOnBeam} patch(es) scraped when already bare. Thin skin goes straight through.");
            if (r.ScoredOnBeam > 0) n.Add($"{r.ScoredOnBeam} patch(es) cut on the beam: the knife is for stuck bits and fat, not film.");
            if (r.PreserveFactor < 1) n.Add(PreserveNote(Species, Preserve, Setup.Season));
        }

        private static string PreserveNote(Species sp, Preserve p, Season s)
        {
            switch (p)
            {
                case Preserve.Salt: return "Salt kept it, but stiffened the fur. Pelts keep best stretched and dried.";
                case Preserve.Stretch:
                    return s == Season.Summer ? "Drying in summer heat spoiled it a little: the hair started to loosen."
                                              : "A leather hide dries flint-hard on the stretcher. Salt keeps it supple for the tanner.";
                default:
                    return s == Season.Summer ? "An untreated hide in summer spoils before the day is out."
                                              : "Rolled up untreated, it started to spoil before the tanner had it. Only real cold keeps it.";
            }
        }
    }
}
