using System;
using System.Collections.Generic;
using System.Linq;
using Economy.Core.Items;
using Economy.Core.Random;

namespace Economy.Core.Crafting
{
    /// <summary>How input quality limits output quality.</summary>
    public enum InputCapMode
    {
        /// <summary>Output can never beat the weakest input.</summary>
        Weakest,
        /// <summary>Output can never beat the quantity-weighted average of the inputs.</summary>
        WeightedAverage,
    }

    public sealed class QualitySettings
    {
        /// <summary>Range floor as a share of the crafter's skill.</summary>
        public double FloorPerSkill { get; set; } = 0.6;

        /// <summary>How far above skill the range ceiling reaches before other caps apply.</summary>
        public int CeilingAboveSkill { get; set; } = 25;

        /// <summary>
        /// Spread of the performance roll that stands in for the minigame in the
        /// headless sim. The live game replaces the roll with the minigame score.
        /// </summary>
        public double PerformanceSpread { get; set; } = 0.15;

        /// <summary>Random nudge applied after performance, as a share of the range.</summary>
        public double RandomNudge { get; set; } = 0.10;

        /// <summary>Performance below this is a botch.</summary>
        public double BotchThreshold { get; set; } = 0.15;

        /// <summary>A botched item lands at this share of the floor.</summary>
        public double BotchQualityFactor { get; set; } = 0.25;

        /// <summary>Open question in the crafting spec; weakest input by default.</summary>
        public InputCapMode InputCap { get; set; } = InputCapMode.Weakest;
    }

    public readonly struct CraftRoll
    {
        public CraftRoll(int quality, bool botched, int floor, int ceiling, double performance)
        {
            Quality = quality;
            Botched = botched;
            Floor = floor;
            Ceiling = ceiling;
            Performance = performance;
        }

        public int Quality { get; }

        /// <summary>A botch never deletes materials: it makes a terrible but usable item.</summary>
        public bool Botched { get; }

        public int Floor { get; }
        public int Ceiling { get; }

        /// <summary>0 to 1; the minigame score in the live game, a skill-based roll in the sim.</summary>
        public double Performance { get; }
    }

    public readonly struct InputQuality
    {
        public InputQuality(int quality, int quantity)
        {
            Quality = quality;
            Quantity = quantity;
        }

        public int Quality { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// Skill and method set the range an item can land in; performance and a
    /// random roll pick where. A master is reliably better than a novice, but
    /// no two items are exactly alike.
    /// </summary>
    public sealed class QualityRules
    {
        public QualityRules(QualitySettings? settings = null)
        {
            Settings = settings ?? new QualitySettings();
        }

        public QualitySettings Settings { get; }

        /// <summary>The quality range a crafter can land in for this recipe and these inputs.</summary>
        public (int floor, int ceiling) Range(Recipe recipe, int skill, IReadOnlyCollection<InputQuality> inputs)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            int s = Math.Max(0, Math.Min(100, skill));

            int inputCap = InputCap(inputs);
            int ceiling = Math.Min(Math.Min(recipe.MethodCeiling, s + Settings.CeilingAboveSkill), inputCap);
            ceiling = Quality.Clamp(ceiling);
            int floor = Quality.Clamp(Math.Min(s * Settings.FloorPerSkill, ceiling));
            return (floor, ceiling);
        }

        /// <summary>
        /// Sim stand-in for the minigame: a skilled crafter performs better on
        /// average, with some spread.
        /// </summary>
        public double SimulatedPerformance(int skill, SimRandom rng)
        {
            double mean = 0.25 + 0.6 * Math.Max(0, Math.Min(100, skill)) / 100.0;
            double p = rng.NextGaussian(mean, Settings.PerformanceSpread);
            return Math.Max(0.0, Math.Min(1.0, p));
        }

        /// <summary>Rolls a craft's quality from a performance score (0 to 1).</summary>
        public CraftRoll Roll(Recipe recipe, int skill, IReadOnlyCollection<InputQuality> inputs, double performance, SimRandom rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var (floor, ceiling) = Range(recipe, skill, inputs);
            double perf = Math.Max(0.0, Math.Min(1.0, performance));

            if (perf < Settings.BotchThreshold)
            {
                int botchQuality = Quality.Clamp(floor * Settings.BotchQualityFactor);
                return new CraftRoll(botchQuality, true, floor, ceiling, perf);
            }

            double span = ceiling - floor;
            double nudge = (rng.NextDouble() * 2.0 - 1.0) * Settings.RandomNudge * span;
            double raw = floor + span * perf + nudge;
            int quality = Quality.Clamp(Math.Max(floor, Math.Min(ceiling, raw)));
            return new CraftRoll(quality, false, floor, ceiling, perf);
        }

        private int InputCap(IReadOnlyCollection<InputQuality> inputs)
        {
            if (inputs == null || inputs.Count == 0) return Quality.Max;
            if (Settings.InputCap == InputCapMode.Weakest) return inputs.Min(i => i.Quality);

            long weight = inputs.Sum(i => (long)i.Quantity);
            if (weight <= 0) return Quality.Max;
            double avg = inputs.Sum(i => (double)i.Quality * i.Quantity) / weight;
            return Quality.Clamp(avg);
        }
    }

    /// <summary>
    /// Quick craft: after enough manual crafts of an item, a player can skip the
    /// minigame and get the average quality of their recent manual crafts.
    /// Only manual crafts count, so players keep crafting by hand to raise it.
    /// </summary>
    public sealed class QuickCraftHistory
    {
        private readonly Queue<int> _recent = new Queue<int>();

        public QuickCraftHistory(int unlockAfter, int averageOver)
        {
            if (unlockAfter <= 0) throw new ArgumentOutOfRangeException(nameof(unlockAfter));
            if (averageOver <= 0) throw new ArgumentOutOfRangeException(nameof(averageOver));
            UnlockAfter = unlockAfter;
            AverageOver = averageOver;
        }

        public int UnlockAfter { get; }
        public int AverageOver { get; }
        public int ManualCrafts { get; private set; }
        public bool Unlocked => ManualCrafts >= UnlockAfter;

        public void RecordManual(int quality)
        {
            ManualCrafts++;
            _recent.Enqueue(Quality.Clamp(quality));
            while (_recent.Count > AverageOver) _recent.Dequeue();
        }

        /// <summary>Quality a quick craft produces right now.</summary>
        public int QuickCraftQuality()
        {
            if (!Unlocked) throw new InvalidOperationException($"Quick craft unlocks after {UnlockAfter} manual crafts ({ManualCrafts} so far).");
            return Quality.Clamp(_recent.Average());
        }
    }
}
