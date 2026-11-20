using System;
using System.Collections.Generic;

namespace Mmo.Crafting.Blacksmithing
{
    /// <summary>One beat marker on the strike track.</summary>
    public sealed class ForgeNote
    {
        public double Time;     // song time in seconds
        public Lane Lane;
        public bool Judged;
        public Judgement Result;
    }

    /// <summary>
    /// The blacksmithing rhythm minigame as a plain state machine (spec, Minigames: Blacksmithing).
    ///  1. Heat: the bar climbs from dull red to white; pull it at the recipe's target colour.
    ///  2. Strike: markers slide to the hammer; hit the right lane (tip, body, edge) on the beat.
    ///  3. Cooling: the metal cools while you work; below cherry, strikes stop counting. Reheat costs quality after the second.
    ///  4. Quench (Journeyman recipes): pick water, oil or brine, hold for a timed count.
    ///  5. Finish: a short sharpening pass that can add bonus traits.
    /// Drive it with Update(dt) and the input methods. It never touches UnityEngine.
    /// </summary>
    public sealed class ForgeMinigame
    {
        public ForgeSetup Setup { get; }
        public ForgeRecipe Recipe => Setup.Recipe;
        public int Skill => Setup.SkillLevel;

        public ForgeStage Stage { get; private set; } = ForgeStage.Heat;
        public double Temperature { get; private set; } = ForgeTuning.StartHeat;
        public HeatBand Band => ForgeTuning.BandFor(Temperature);
        public bool IsReheating { get; private set; }
        public bool CanStrike => Stage == ForgeStage.Strike && Temperature >= ForgeTuning.WorkMin;
        public int Reheats { get; private set; }

        public double SongTime { get; private set; }
        public double BeatInterval { get; }
        public double PerfectWindow { get; }
        public double GoodWindow { get; }
        public IReadOnlyList<ForgeNote> Notes => _notes;
        public double[] Progress { get; } = new double[3];
        public int[] Target => Recipe.StrikesPerLane;

        public QuenchMedium Medium { get; private set; }
        public bool QuenchHeld { get; private set; }
        public double QuenchSeconds { get; private set; }

        public double FinishCursor { get; private set; }
        public double FinishSweetCenter { get; private set; }
        public double FinishSweetWidth { get; }
        public int FinishStrokesDone { get; private set; }

        public ForgeResult Result { get; private set; }

        /// <summary>Things that happened since the last call to DrainEvents (for sounds and popups).</summary>
        public List<ForgeEvent> Events { get; } = new List<ForgeEvent>();

        private readonly Random _rng;
        private readonly List<ForgeNote> _notes = new List<ForgeNote>();
        private readonly List<double> _pullScores = new List<double>();
        private readonly List<double> _finishScores = new List<double>();
        private readonly List<string> _flaws = new List<string>();
        private int _perfect, _good, _miss, _offBeat, _cold;
        private double _quenchScore = 1.0;
        private double _strikeEndTimer = -1;
        private double _finishPhase;

        public ForgeMinigame(ForgeSetup setup, int seed)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _rng = new Random(seed);
            BeatInterval = 60.0 / Recipe.Bpm;
            PerfectWindow = ForgeTuning.PerfectWindow(Skill, setup.Hammer);
            GoodWindow = ForgeTuning.GoodWindow(Skill, setup.Hammer);
            FinishSweetWidth = ForgeTuning.FinishSweetWidth(Skill);
            BuildChart();
            SongTime = -ForgeTuning.LeadInBeats * BeatInterval;
        }

        private void BuildChart()
        {
            var lanes = new List<Lane>();
            for (int lane = 0; lane < 3; lane++)
                for (int i = 0; i < Recipe.StrikesPerLane[lane]; i++)
                    lanes.Add((Lane)lane);
            // Shuffle so the pattern differs every craft.
            for (int i = lanes.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (lanes[i], lanes[j]) = (lanes[j], lanes[i]);
            }
            double t = 0;
            for (int i = 0; i < lanes.Count; i++)
            {
                _notes.Add(new ForgeNote { Time = t, Lane = lanes[i] });
                bool half = i > 0 && _rng.NextDouble() < Recipe.HalfBeatChance;
                t += half ? BeatInterval * 0.5 : BeatInterval;
            }
        }

        // ------------------------------------------------------------------ update

        public void Update(double dt)
        {
            if (dt <= 0) return;
            switch (Stage)
            {
                case ForgeStage.Heat:
                    Temperature += ForgeTuning.HeatRatePerSecond * dt;
                    if (Temperature >= ForgeTuning.BurnLimit) PullFromForge();
                    break;

                case ForgeStage.Strike:
                    UpdateStrike(dt);
                    break;

                case ForgeStage.QuenchHold:
                    Temperature = Math.Max(0, Temperature - ForgeTuning.CoolRatePerSecond * 4 * dt);
                    if (QuenchHeld)
                    {
                        QuenchSeconds += dt;
                        double limit = ForgeTuning.QuenchTargetSeconds(Medium) * 2 + 1;
                        if (QuenchSeconds >= limit) EndQuench();
                    }
                    break;

                case ForgeStage.Finish:
                    _finishPhase += dt * ForgeTuning.FinishCursorSpeed;
                    double p = _finishPhase % 2.0;
                    FinishCursor = p <= 1 ? p : 2 - p;
                    break;
            }
        }

        private void UpdateStrike(double dt)
        {
            // The bar cools once the beat starts (the lead-in is free).
            if (SongTime >= 0) Temperature = Math.Max(0, Temperature - ForgeTuning.CoolRatePerSecond * dt);

            double prev = SongTime;
            SongTime += dt;

            // Metronome: one event per beat crossed.
            double lastNote = _notes[_notes.Count - 1].Time;
            if (SongTime <= lastNote + BeatInterval * 0.5 &&
                Math.Floor(prev / BeatInterval) < Math.Floor(SongTime / BeatInterval))
                Events.Add(new ForgeEvent(ForgeEventKind.Beat, "beat"));

            // Notes that slid past the hammer are misses.
            foreach (var n in _notes)
            {
                if (n.Judged || n.Time + GoodWindow >= SongTime) continue;
                n.Judged = true;
                n.Result = Judgement.Miss;
                _miss++;
                Events.Add(new ForgeEvent(ForgeEventKind.Miss, "Miss", Judgement.Miss, n.Lane));
            }

            if (_strikeEndTimer < 0 && AllNotesJudged()) _strikeEndTimer = ForgeTuning.StrikeEndDelay;
            if (_strikeEndTimer >= 0)
            {
                _strikeEndTimer -= dt;
                if (_strikeEndTimer < 0) AfterStrike();
            }
        }

        private bool AllNotesJudged()
        {
            foreach (var n in _notes) if (!n.Judged) return false;
            return true;
        }

        private void SetStage(ForgeStage stage, string message)
        {
            Stage = stage;
            Events.Add(new ForgeEvent(ForgeEventKind.StageChanged, message));
        }

        // ------------------------------------------------------------------ inputs

        /// <summary>Pull the bar out of the forge (Heat stage).</summary>
        public void PullFromForge()
        {
            if (Stage != ForgeStage.Heat) return;
            double score = ForgeTuning.HeatPullScore(Temperature, Recipe.TargetHeat, Skill, out string flaw);
            _pullScores.Add(score);
            if (flaw != null)
            {
                _flaws.Add(flaw);
                Events.Add(new ForgeEvent(Temperature > 1 ? ForgeEventKind.Burned : ForgeEventKind.Cracked, flaw));
            }
            Events.Add(new ForgeEvent(ForgeEventKind.Pulled, $"Pulled at {Band} ({score:P0})"));
            IsReheating = false;
            SetStage(ForgeStage.Strike, "Strike on the beat");
        }

        /// <summary>Put the bar back in the fire (Strike stage). The beat track pauses.</summary>
        public void Reheat()
        {
            if (Stage != ForgeStage.Strike || _strikeEndTimer >= 0) return;
            Reheats++;
            IsReheating = true;
            SetStage(ForgeStage.Heat, Reheats > ForgeTuning.FreeReheats ? "Reheat (costs a little quality)" : "Reheat");
        }

        /// <summary>Strike the anvil in a lane. Judged against the nearest note in that lane.</summary>
        public void Strike(Lane lane)
        {
            if (Stage != ForgeStage.Strike) return;

            ForgeNote best = null;
            double bestErr = double.MaxValue;
            foreach (var n in _notes)
            {
                if (n.Judged || n.Lane != lane) continue;
                double err = Math.Abs(n.Time - SongTime);
                if (err <= GoodWindow && err < bestErr) { best = n; bestErr = err; }
            }

            if (best == null)
            {
                _offBeat++;
                Events.Add(new ForgeEvent(ForgeEventKind.OffBeat, "Off-beat: dents the blade", Judgement.OffBeat, lane));
                return;
            }

            best.Judged = true;
            if (Temperature < ForgeTuning.WorkMin)
            {
                best.Result = Judgement.Cold;
                _cold++;
                Events.Add(new ForgeEvent(ForgeEventKind.ColdStrike, "Too cold: reheat!", Judgement.Cold, lane));
                return;
            }

            if (bestErr <= PerfectWindow)
            {
                best.Result = Judgement.Perfect;
                _perfect++;
                Progress[(int)lane] += ForgeTuning.PerfectProgress;
                Events.Add(new ForgeEvent(ForgeEventKind.Strike, "Perfect", Judgement.Perfect, lane));
            }
            else
            {
                best.Result = Judgement.Good;
                _good++;
                Progress[(int)lane] += ForgeTuning.GoodProgress;
                Events.Add(new ForgeEvent(ForgeEventKind.Strike, "Good", Judgement.Good, lane));
            }
        }

        private void AfterStrike()
        {
            if (Recipe.HasQuench) SetStage(ForgeStage.QuenchChoose, "Choose a quench");
            else if (Recipe.HasFinish) StartFinish();
            else Complete();
        }

        public void ChooseQuench(QuenchMedium medium)
        {
            if (Stage != ForgeStage.QuenchChoose) return;
            Medium = medium;
            QuenchSeconds = 0;
            SetStage(ForgeStage.QuenchHold, $"Hold the blade in the {medium.ToString().ToLowerInvariant()}");
        }

        /// <summary>Start holding the blade in the quench (press).</summary>
        public void BeginQuench()
        {
            if (Stage != ForgeStage.QuenchHold || QuenchHeld) return;
            QuenchHeld = true;
            Events.Add(new ForgeEvent(ForgeEventKind.QuenchStart, "Hiss"));
        }

        /// <summary>Lift the blade out (release).</summary>
        public void EndQuench()
        {
            if (Stage != ForgeStage.QuenchHold || !QuenchHeld) return;
            QuenchHeld = false;

            double target = ForgeTuning.QuenchTargetSeconds(Medium);
            double tol = ForgeTuning.QuenchTolerance(Medium, Skill);
            double err = Math.Abs(QuenchSeconds - target);
            double score = err <= tol ? 1.0 : Math.Max(0.35, 1.0 - (err - tol) / target);
            score *= ForgeTuning.QuenchMax(Medium);

            bool tooLong = QuenchSeconds > target + tol;
            if (_rng.NextDouble() < ForgeTuning.QuenchCrackChance(Medium, Skill, tooLong))
            {
                score = Math.Min(score, 0.4);
                _flaws.Add($"Quench crack ({Medium.ToString().ToLowerInvariant()})");
                Events.Add(new ForgeEvent(ForgeEventKind.QuenchCrack, "The blade cracked in the quench!"));
            }
            else
            {
                string how = err <= tol ? "Clean quench" : QuenchSeconds < target ? "Pulled early (soft)" : "Held too long (brittle)";
                Events.Add(new ForgeEvent(ForgeEventKind.QuenchEnd, how));
            }
            _quenchScore = score;

            if (Recipe.HasFinish) StartFinish();
            else Complete();
        }

        private void StartFinish()
        {
            FinishStrokesDone = 0;
            _finishPhase = 0;
            FinishCursor = 0;
            NewSweetSpot();
            SetStage(ForgeStage.Finish, "Sharpen: stroke when the stone is in the bright zone");
        }

        private void NewSweetSpot() => FinishSweetCenter = 0.25 + _rng.NextDouble() * 0.5;

        /// <summary>One sharpening stroke (Finish stage).</summary>
        public void FinishStroke()
        {
            if (Stage != ForgeStage.Finish) return;
            double dist = Math.Abs(FinishCursor - FinishSweetCenter);
            double half = FinishSweetWidth / 2;
            double score = dist <= half ? 1.0 : dist <= FinishSweetWidth ? 0.6 : 0.2;
            _finishScores.Add(score);
            FinishStrokesDone++;
            Events.Add(new ForgeEvent(ForgeEventKind.FinishStroke, score >= 1 ? "Clean stroke" : score > 0.5 ? "Close" : "Rough stroke"));
            if (FinishStrokesDone >= ForgeTuning.FinishStrokes) Complete();
            else NewSweetSpot();
        }

        // ------------------------------------------------------------------ scoring

        /// <summary>
        /// Quality range before playing. Ceiling = lowest of the method cap, station cap, tool cap and the inputs'
        /// weighted mean quality (main part counts most). Floor comes from skill. Returns the inputs' mean quality.
        /// </summary>
        public static int ComputeCaps(ForgeSetup setup, out int floor, out int ceiling, out string ceilingReason)
        {
            var recipe = setup.Recipe;
            double wq = 0, w = 0;
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                wq += recipe.Inputs[i].Weight * setup.InputQualities[i];
                w += recipe.Inputs[i].Weight;
            }
            int inputMean = (int)Math.Round(w > 0 ? wq / w : 0);
            ceiling = recipe.MethodCap;
            ceilingReason = $"Method ({recipe.Name})";
            if (setup.Station.QualityCap < ceiling) { ceiling = setup.Station.QualityCap; ceilingReason = $"Station ({setup.Station.Name})"; }
            if (setup.Hammer.QualityCap < ceiling) { ceiling = setup.Hammer.QualityCap; ceilingReason = $"Tool ({setup.Hammer.Name})"; }
            if (inputMean < ceiling) { ceiling = inputMean; ceilingReason = "Input materials"; }
            ceiling = Math.Max(1, ceiling);
            floor = Math.Min(ForgeTuning.SkillFloor(setup.SkillLevel), ceiling);
            return inputMean;
        }

        private void Complete()
        {
            var r = new ForgeResult
            {
                Perfect = _perfect,
                Good = _good,
                Missed = _miss,
                OffBeat = _offBeat,
                Cold = _cold,
                Reheats = Reheats,
                ItemLevel = Recipe.ItemLevel,
            };
            r.Flaws.AddRange(_flaws);

            // Shape: how close each part got to the ghost outline, weighted by how much work each part needs.
            double shape = 0, totalWeight = 0;
            for (int i = 0; i < 3; i++)
            {
                double target = Target[i];
                if (target <= 0) continue;
                double match = ForgeTuning.Clamp01(1.0 - Math.Abs(Progress[i] - target) / target);
                shape += match * target;
                totalWeight += target;
            }
            shape = totalWeight > 0 ? shape / totalWeight : 0;
            r.Shape = ForgeTuning.Clamp01(shape - ForgeTuning.OffBeatDent * _offBeat);

            double attempts = _notes.Count + 0.5 * _offBeat;
            r.Accuracy = attempts > 0 ? (_perfect + ForgeTuning.GoodAccuracy * _good) / attempts : 0;

            double pullAvg = 0;
            foreach (var s in _pullScores) pullAvg += s;
            pullAvg = _pullScores.Count > 0 ? pullAvg / _pullScores.Count : 0;
            r.HeatPullAverage = pullAvg;
            r.QuenchScore = _quenchScore;
            int extraReheats = Math.Max(0, Reheats - ForgeTuning.FreeReheats);
            r.HeatControl = pullAvg * Math.Pow(ForgeTuning.ReheatPenalty, extraReheats) * _quenchScore;

            double finish = 0;
            foreach (var s in _finishScores) finish += s;
            r.FinishScore = _finishScores.Count > 0 ? finish / _finishScores.Count : 0;

            r.Score = ForgeTuning.Clamp01(r.Shape * r.Accuracy * r.HeatControl);

            r.InputMeanQuality = ComputeCaps(Setup, out r.Floor, out r.Ceiling, out r.CeilingReason);

            r.Roll = (_rng.NextDouble() * 2 - 1) * ForgeTuning.RollRange;
            r.Botch = r.Score < ForgeTuning.BotchBelow;
            if (r.Botch)
            {
                r.Quality = 1 + _rng.Next(10);
            }
            else
            {
                double q = r.Floor + (r.Ceiling - r.Floor) * ForgeTuning.Clamp01(r.Score + r.Roll);
                r.Quality = Math.Max(1, Math.Min(100, (int)Math.Round(q)));
            }
            r.Grade = ForgeTuning.GradeWord(r.Quality);
            r.ItemName = $"{r.Grade} {Recipe.Name}";

            // Bonus traits from the finish pass.
            if (Recipe.HasFinish && !r.Botch)
            {
                var pool = new List<string>(ForgeTuning.Traits);
                int max = ForgeTuning.MaxTraits(Skill);
                for (int i = 0; i < max && pool.Count > 0; i++)
                {
                    if (_rng.NextDouble() >= ForgeTuning.TraitChance(r.FinishScore, Skill)) continue;
                    int pick = _rng.Next(pool.Count);
                    r.Traits.Add(pool[pick]);
                    pool.RemoveAt(pick);
                }
            }

            // Skill only grows from recipes that still challenge you.
            if (!r.Botch && Skill < 100 && Skill < Recipe.RequiredSkill + 30)
                r.SkillGain = r.Score >= 0.75 ? 2 : 1;

            Result = r;
            SetStage(ForgeStage.Done, r.ItemName);
            Events.Add(new ForgeEvent(ForgeEventKind.Done, r.ItemName));
        }
    }
}
