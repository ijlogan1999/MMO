using System;
using System.Collections.Generic;

namespace Mmo.Crafting.Cooking
{
    /// <summary>
    /// The cooking juggling minigame as a plain state machine (spec, Minigames: Cooking).
    ///  1. Season: pick a wild herb for each dish (which suits what is rolled per world).
    ///  2. Chop: if any dish uses vegetables, chop them in your own steady rhythm. Even gaps make even pieces.
    ///  3. Cook: every dish cooks at once on its own station. Set each one's heat, turn spits, flip bread, stir and skim
    ///     pots, add ingredients in their own time, baste the roast, and take each dish off when it looks done.
    ///     A roast rests on the board until you carve it.
    /// Nothing tells the player when: no doneness bars, prompts or timers. They read the food.
    /// Drive it with Update(dt) and the input methods. It never touches UnityEngine.
    /// </summary>
    public sealed class CookingMinigame
    {
        public CookSetup Setup { get; }
        public int Skill => Setup.SkillLevel;
        public CookStage Stage { get; private set; } = CookStage.Season;
        public IReadOnlyList<Dish> Dishes => _dishes;
        public int Selected { get; private set; }
        public Dish SelectedDish => _dishes.Count > 0 ? _dishes[Selected] : null;
        public bool NeedsChop { get; }

        /// <summary>Chop gaps so far, in seconds (the view draws pieces this wide).</summary>
        public IReadOnlyList<double> ChopGaps => _chopGaps;
        public int ChopsDone { get; private set; }

        public CookResult Result { get; private set; }
        public List<CookEvent> Events { get; } = new List<CookEvent>();

        private readonly List<Dish> _dishes = new List<Dish>();
        private readonly List<double> _chopGaps = new List<double>();
        private readonly Random _rng;
        private double _clock;
        private double _lastChop = -1;
        private double _chopScore = 1.0;

        public CookingMinigame(CookSetup setup, int seed)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _rng = new Random(seed);
            int stations = CookTuning.StationsFor(setup.SkillLevel);
            for (int i = 0; i < setup.Dishes.Length && _dishes.Count < stations; i++)
            {
                var recipe = setup.Dishes[i];
                if (recipe == null || setup.Blocker(recipe) != null) continue;
                var dish = new Dish(_dishes.Count, recipe, setup.World.DoneTarget(recipe))
                {
                    Wobble = (_rng.NextDouble() * 2 - 1) * setup.Hearth.Wobble,
                };
                _dishes.Add(dish);
                if (recipe.NeedsChop) NeedsChop = true;
            }
            if (_dishes.Count == 0) throw new ArgumentException("No dish can be cooked with this setup.", nameof(setup));
        }

        // ------------------------------------------------------------------ update

        public void Update(double dt)
        {
            if (dt <= 0) return;
            _clock += dt;
            if (Stage != CookStage.Cook) return;

            foreach (var d in _dishes)
            {
                if (d.State == DishState.Resting) { d.RestTime += dt; continue; }
                if (d.State != DishState.Cooking) continue;
                Cook(d, dt);
            }
            if (AllServed()) Complete();
        }

        private void Cook(Dish d, double dt)
        {
            d.CookTime += dt;
            // The fire wanders: a bounded random walk, more on a campfire, none in a good kitchen.
            double w = Setup.Hearth.Wobble;
            if (w > 0)
                d.Wobble = Math.Max(-w, Math.Min(w, d.Wobble + (_rng.NextDouble() * 2 - 1) * CookTuning.WobbleSpeed * w * Math.Sqrt(dt) * 4));

            double heat = d.EffectiveHeat;
            var r = d.Recipe;

            if (d.TwoSided)
            {
                double back = r.Station == StationKind.Griddle ? CookTuning.GriddleBackFactor : CookTuning.SpitBackFactor;
                double rate = 1.0 / ((0.5 + 0.5 * back) * r.CookSeconds);
                d.Sides[d.Facing] += rate * heat * dt;
                d.Sides[1 - d.Facing] += rate * back * heat * dt;

                if (r.CanBaste)
                    d.Moisture = Math.Max(0, d.Moisture - CookTuning.DryRateMedium * heat * dt);

                if (!d.CharringSaid && Math.Max(d.Sides[0], d.Sides[1]) >= CookTuning.CharredAt * d.Target)
                {
                    d.CharringSaid = true;
                    Events.Add(new CookEvent(CookEventKind.Charring, d.Slot, "smoke"));
                }
                if (Math.Max(d.Sides[0], d.Sides[1]) >= CookTuning.CinderAt * d.Target) Ruin(d, "Burnt to a cinder");
                return;
            }

            // Pot: every part that is in cooks.
            for (int i = 0; i < d.PartsAdded; i++)
                d.Parts[i] += heat / r.Parts[i].Seconds * dt;

            if (r.HasScum)
                d.Scum = Math.Min(1.0, d.Scum + CookTuning.ScumRate * heat * Math.Exp(-d.CookTime / CookTuning.ScumFade) * dt);

            // Unstirred, the bottom catches. On low heat it never does.
            d.SinceStir += dt;
            double grace = d.Heat == Heat.High ? CookTuning.ScorchGraceHigh : d.Heat == Heat.Medium ? CookTuning.ScorchGraceMedium : double.MaxValue;
            if (d.SinceStir > grace)
            {
                if (!d.CatchingSaid)
                {
                    d.CatchingSaid = true;
                    Events.Add(new CookEvent(CookEventKind.Catching, d.Slot, "catching"));
                }
                double rate = d.Heat == Heat.High ? CookTuning.ScorchRateHigh : CookTuning.ScorchRateMedium;
                d.Scorch = Math.Min(1.0, d.Scorch + rate * (1 + d.Wobble) * dt);
            }

            if (!d.CharringSaid && d.Parts[0] >= CookTuning.CharredAt * d.Target)
            {
                d.CharringSaid = true;
                Events.Add(new CookEvent(CookEventKind.Charring, d.Slot, "boiling dry"));
            }
            if (d.Scorch >= 1.0) Ruin(d, "Burnt onto the pot");
            else if (d.Parts[0] >= CookTuning.CinderAt * d.Target) Ruin(d, "Boiled dry");
        }

        private void Ruin(Dish d, string why)
        {
            d.Ruined = true;
            d.State = DishState.Served;
            _ruinReason[d.Slot] = why;
            Events.Add(new CookEvent(CookEventKind.Ruined, d.Slot, why));
        }

        private readonly Dictionary<int, string> _ruinReason = new Dictionary<int, string>();

        private bool AllServed()
        {
            foreach (var d in _dishes) if (d.State != DishState.Served) return false;
            return true;
        }

        private void SetStage(CookStage stage, string message)
        {
            Stage = stage;
            Events.Add(new CookEvent(CookEventKind.StageChanged, -1, message));
        }

        // ------------------------------------------------------------------ season

        public void Select(int slot)
        {
            if (slot < 0 || slot >= _dishes.Count || Stage == CookStage.Done) return;
            Selected = slot;
            Events.Add(new CookEvent(CookEventKind.Selected, slot, _dishes[slot].Recipe.Name));
        }

        public void SelectNext() => Select((Selected + 1) % _dishes.Count);

        public void SetHerb(int slot, Herb herb)
        {
            if (Stage != CookStage.Season || slot < 0 || slot >= _dishes.Count) return;
            _dishes[slot].Herb = herb;
        }

        public void CycleHerb()
        {
            if (Stage != CookStage.Season) return;
            var d = SelectedDish;
            d.Herb = (Herb)(((int)d.Herb + 1) % 5);
        }

        /// <summary>Done seasoning: on to the chopping board, or straight to the fire.</summary>
        public void FinishSeasoning()
        {
            if (Stage != CookStage.Season) return;
            if (NeedsChop) SetStage(CookStage.Chop, "Chop the vegetables");
            else StartCooking();
        }

        // ------------------------------------------------------------------ chop

        /// <summary>One knife stroke. The gaps between strokes become the pieces: an even rhythm makes even pieces.</summary>
        public void Chop()
        {
            if (Stage != CookStage.Chop) return;
            if (_lastChop >= 0) _chopGaps.Add(_clock - _lastChop);
            _lastChop = _clock;
            ChopsDone++;
            Events.Add(new CookEvent(CookEventKind.Chop, -1, "chop"));
            if (ChopsDone >= CookTuning.Chops)
            {
                _chopScore = CookTuning.ChopScore(ChopSpread(), Skill);
                StartCooking();
            }
        }

        /// <summary>Coefficient of variation of the gaps between chops (0 = perfectly even).</summary>
        public double ChopSpread()
        {
            if (_chopGaps.Count < 2) return 0;
            double mean = 0;
            foreach (var g in _chopGaps) mean += g;
            mean /= _chopGaps.Count;
            if (mean <= 0) return 1;
            double v = 0;
            foreach (var g in _chopGaps) v += (g - mean) * (g - mean);
            return Math.Sqrt(v / _chopGaps.Count) / mean;
        }

        private void StartCooking() => SetStage(CookStage.Cook, "Onto the fire");

        // ------------------------------------------------------------------ cook

        private Dish Active => Stage == CookStage.Cook ? SelectedDish : null;

        private void Nothing(Dish d) => Events.Add(new CookEvent(CookEventKind.Nothing, d?.Slot ?? -1, ""));

        /// <summary>Closer to the coals.</summary>
        public void HeatUp() => ChangeHeat(+1);

        /// <summary>Further from the coals.</summary>
        public void HeatDown() => ChangeHeat(-1);

        private void ChangeHeat(int step)
        {
            var d = Active;
            if (d == null || d.State != DishState.Cooking) { Nothing(d); return; }
            int h = Math.Max(0, Math.Min(2, (int)d.Heat + step));
            if (h == (int)d.Heat) { Nothing(d); return; }
            d.Heat = (Heat)h;
            Events.Add(new CookEvent(CookEventKind.HeatChanged, d.Slot, d.Heat.ToString()));
        }

        /// <summary>The main hand action: turn the spit, flip the bread, or stir the pot.</summary>
        public void Tend()
        {
            var d = Active;
            if (d == null || d.State != DishState.Cooking) { Nothing(d); return; }
            switch (d.Recipe.Station)
            {
                case StationKind.Spit:
                    d.Facing = 1 - d.Facing;
                    d.Turns++;
                    Events.Add(new CookEvent(CookEventKind.Turn, d.Slot, "turn"));
                    break;
                case StationKind.Griddle:
                    d.Facing = 1 - d.Facing;
                    d.Turns++;
                    Events.Add(new CookEvent(CookEventKind.Flip, d.Slot, "flip"));
                    break;
                default:
                    d.SinceStir = 0;
                    d.CatchingSaid = false;
                    Events.Add(new CookEvent(CookEventKind.Stir, d.Slot, "stir"));
                    break;
            }
        }

        /// <summary>The ladle or the brush: skim a pot, or baste a roast with its fat.</summary>
        public void SkimOrBaste()
        {
            var d = Active;
            if (d == null || d.State != DishState.Cooking) { Nothing(d); return; }
            if (d.Recipe.HasScum)
            {
                d.Scum = Math.Max(0, d.Scum - CookTuning.SkimAmount);
                Events.Add(new CookEvent(CookEventKind.Skim, d.Slot, "skim"));
            }
            else if (d.Recipe.CanBaste && d.BastesLeft > 0)
            {
                d.BastesLeft--;
                d.Moisture = Math.Min(1.0, d.Moisture + CookTuning.BasteAmount);
                Events.Add(new CookEvent(CookEventKind.Baste, d.Slot, "baste"));
            }
            else Nothing(d);
        }

        /// <summary>Put the next ingredient in the pot.</summary>
        public void AddNext()
        {
            var d = Active;
            if (d == null || d.State != DishState.Cooking || d.AllPartsIn) { Nothing(d); return; }
            string name = d.NextPartName;
            d.PartsAdded++;
            Events.Add(new CookEvent(CookEventKind.Add, d.Slot, name));
        }

        /// <summary>Take the dish off the fire. A roast goes to rest on the board; take it again to carve.</summary>
        public void TakeOff()
        {
            var d = Active;
            if (d == null || d.State == DishState.Served) { Nothing(d); return; }
            if (d.State == DishState.Resting)
            {
                d.State = DishState.Served;
                Events.Add(new CookEvent(CookEventKind.Carve, d.Slot, "carve"));
            }
            else if (d.Recipe.Rests)
            {
                d.State = DishState.Resting;
                Events.Add(new CookEvent(CookEventKind.TakeOff, d.Slot, "to the board"));
            }
            else
            {
                d.State = DishState.Served;
                Events.Add(new CookEvent(CookEventKind.TakeOff, d.Slot, "served"));
            }
            if (AllServed()) Complete();
        }

        // ------------------------------------------------------------------ scoring

        /// <summary>
        /// Quality range before cooking. Ceiling = lowest of the method cap, hearth cap, cookware cap and the inputs'
        /// weighted mean quality (main ingredient counts most). Floor comes from skill. Returns the inputs' mean quality.
        /// </summary>
        public static int ComputeCaps(CookSetup setup, CookRecipe recipe, out int floor, out int ceiling, out string ceilingReason)
        {
            double wq = 0, w = 0;
            foreach (var input in recipe.Inputs)
            {
                wq += input.Weight * setup.QualityOf(input.Ingredient);
                w += input.Weight;
            }
            int inputMean = (int)Math.Round(w > 0 ? wq / w : 0);
            ceiling = recipe.MethodCap;
            ceilingReason = $"Method ({recipe.Name})";
            if (setup.Hearth.QualityCap < ceiling) { ceiling = setup.Hearth.QualityCap; ceilingReason = $"Hearth ({setup.Hearth.Name})"; }
            if (setup.Cookware.QualityCap < ceiling) { ceiling = setup.Cookware.QualityCap; ceilingReason = $"Cookware ({setup.Cookware.Name})"; }
            if (inputMean < ceiling) { ceiling = inputMean; ceilingReason = "Ingredients"; }
            ceiling = Math.Max(1, ceiling);
            floor = Math.Min(CookTuning.SkillFloor(setup.SkillLevel), ceiling);
            return inputMean;
        }

        private void Complete()
        {
            if (Stage == CookStage.Done) return;
            var result = new CookResult { ChopScore = _chopScore, Chopped = NeedsChop };
            double sum = 0;
            int gain = 0;
            foreach (var d in _dishes)
            {
                var r = ScoreDish(d);
                result.Dishes.Add(r);
                sum += r.Score;
                // Skill only grows from recipes that still challenge you.
                if (!r.Botch && Skill < 100 && Skill < d.Recipe.RequiredSkill + 30) gain += r.Score >= 0.75 ? 2 : 1;
            }
            result.AverageScore = sum / _dishes.Count;
            result.SkillGain = Math.Min(3, gain);
            Result = result;
            SetStage(CookStage.Done, "Done");
            Events.Add(new CookEvent(CookEventKind.Done, -1, "Done"));
        }

        private DishResult ScoreDish(Dish d)
        {
            var recipe = d.Recipe;
            var r = new DishResult { Slot = d.Slot, Recipe = recipe, Herb = d.Herb, Ruined = d.Ruined };

            // Doneness: each side (or each thing in the pot) against this world's done point.
            if (d.TwoSided)
            {
                double a = CookTuning.DonenessScore(d.Sides[0], d.Target, Skill);
                double b = CookTuning.DonenessScore(d.Sides[1], d.Target, Skill);
                r.Doneness = (a + b) / 2;
                string w0 = CookTuning.DonenessWord(d.Sides[0], d.Target, Skill);
                string w1 = CookTuning.DonenessWord(d.Sides[1], d.Target, Skill);
                if (recipe.Station == StationKind.Griddle)
                    r.Notes.Add(w0 == w1 ? $"Both sides {w0}" : $"One side {w0}, the other {w1}");
                else
                    r.Notes.Add(w0 == w1 ? $"Cooked {w0} all round" : $"One side {w0}, the other {w1}" + (d.Turns < 2 ? " (hardly turned)" : ""));
            }
            else
            {
                double s = 0;
                var words = new List<string>();
                for (int i = 0; i < recipe.Parts.Count; i++)
                {
                    if (i >= d.PartsAdded)
                    {
                        r.Flaws.Add($"Forgot the {recipe.Parts[i].Name}");
                        continue;
                    }
                    s += CookTuning.DonenessScore(d.Parts[i], d.Target, Skill);
                    words.Add($"{recipe.Parts[i].Name} {CookTuning.DonenessWord(d.Parts[i], d.Target, Skill)}");
                }
                r.Doneness = s / recipe.Parts.Count;
                r.Notes.Add(Capitalise(string.Join(", ", words)));
            }

            // Handling: the pot's bottom and surface, the roast's moisture and rest.
            if (d.IsPot)
            {
                r.Scorch = d.Scorch;
                r.ScumLeft = recipe.HasScum ? d.Scum : 0;
                r.Handling = (1 - d.Scorch) * (1 - CookTuning.ScumPenalty * r.ScumLeft);
                if (d.Scorch >= 0.05) r.Notes.Add(d.Scorch >= 0.4 ? "Badly caught on the bottom: it tastes burnt" : "Caught a little on the bottom");
                if (r.ScumLeft >= 0.1) r.Notes.Add(r.ScumLeft >= 0.4 ? "Murky with scum" : "A little cloudy with scum");
                else if (recipe.HasScum) r.Notes.Add("Clear, well skimmed");
            }
            if (recipe.CanBaste)
            {
                r.Moisture = d.Moisture;
                r.Handling *= 0.6 + 0.4 * d.Moisture;
                r.Notes.Add(d.Moisture >= 0.75 ? "Juicy" : d.Moisture >= 0.4 ? "A bit dry" : "Dried out on the spit");
            }
            if (recipe.Rests && !d.Ruined)
            {
                double target = Setup.World.RestCounts;
                r.Rested = d.RestTime;
                r.RestScore = CookTuning.RestScore(d.RestTime, target);
                r.Handling *= r.RestScore;
                r.Notes.Add(d.RestTime < target * 0.5 ? "Carved too soon: the juices ran out" :
                    d.RestTime < target ? "Carved a little early" :
                    r.RestScore >= 1 ? "Well rested before carving" : "Rested so long it went cold");
            }

            // Prep: herb pairing and the chopping.
            Setup.World.HerbPairing(recipe.Flavor, out Herb good, out Herb clash);
            r.HerbScore = CookTuning.HerbScore(d.Herb, good, clash);
            r.Prep = r.HerbScore;
            string food = recipe.Flavor == FlavorBase.Bread ? "bread" : recipe.Flavor == FlavorBase.Bones ? "broth" : "meat";
            if (d.Herb == Herb.None) r.Notes.Add("No herb");
            else if (r.HerbScore >= CookTuning.HerbGood) r.Notes.Add($"{CookTuning.HerbName(d.Herb)} suits the {food} here");
            else if (r.HerbScore <= CookTuning.HerbClash) r.Notes.Add($"{CookTuning.HerbName(d.Herb)} clashed with the {food}");
            else r.Notes.Add($"{CookTuning.HerbName(d.Herb)} did little for the {food}");
            if (recipe.NeedsChop)
            {
                r.ChopScore = _chopScore;
                r.Prep *= 0.7 + 0.3 * _chopScore;
                r.Notes.Add(_chopScore >= 1 ? "Even pieces" : _chopScore >= 0.6 ? "Slightly uneven pieces" : "Uneven pieces cooked unevenly");
            }

            r.Score = CookTuning.Clamp01(r.Doneness * r.Handling * r.Prep);
            if (d.Ruined)
            {
                r.Score = 0;
                r.Flaws.Add(_ruinReason.TryGetValue(d.Slot, out var why) ? why : "Ruined");
            }

            r.InputMeanQuality = ComputeCaps(Setup, recipe, out r.Floor, out r.Ceiling, out r.CeilingReason);
            r.Roll = (_rng.NextDouble() * 2 - 1) * CookTuning.RollRange;
            r.Botch = r.Score < CookTuning.BotchBelow;
            if (r.Botch) r.Quality = 1 + _rng.Next(10);
            else
            {
                double q = r.Floor + (r.Ceiling - r.Floor) * CookTuning.Clamp01(r.Score + r.Roll);
                r.Quality = Math.Max(1, Math.Min(100, (int)Math.Round(q)));
            }
            r.Grade = CookTuning.GradeWord(r.Quality);
            r.ItemName = d.Ruined ? $"Burnt {recipe.Name}" : $"{r.Grade} {recipe.Name}";
            return r;
        }

        private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
