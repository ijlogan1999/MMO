using System;
using System.Collections.Generic;
using System.Linq;
using Economy.Core.Items;

namespace Economy.Core.Crafting
{
    public sealed class RecipeInput
    {
        public RecipeInput(string goodId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(goodId)) throw new ArgumentException("Input good id is required.", nameof(goodId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Input quantity must be positive.");
            GoodId = goodId;
            Quantity = quantity;
        }

        public string GoodId { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// How to make a good: inputs, station, skill and time. Loaded from data
    /// so the simulation and the server use the same recipe files.
    /// </summary>
    public sealed class Recipe
    {
        public Recipe(
            string id,
            string outputGoodId,
            int outputQuantity,
            IEnumerable<RecipeInput> inputs,
            string station,
            string skill,
            int minSkill,
            int methodCeiling,
            int craftMinutes)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Recipe id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(outputGoodId)) throw new ArgumentException("Output good is required.", nameof(outputGoodId));
            if (outputQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(outputQuantity));
            if (minSkill < 0 || minSkill > 100) throw new ArgumentOutOfRangeException(nameof(minSkill));
            if (methodCeiling < Quality.Min || methodCeiling > Quality.Max) throw new ArgumentOutOfRangeException(nameof(methodCeiling));
            if (craftMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(craftMinutes));

            var list = (inputs ?? throw new ArgumentNullException(nameof(inputs))).ToList();
            if (list.Count == 0) throw new ArgumentException($"Recipe '{id}' has no inputs.", nameof(inputs));
            if (list.Select(i => i.GoodId).Distinct().Count() != list.Count)
                throw new ArgumentException($"Recipe '{id}' lists the same input twice.", nameof(inputs));

            Id = id;
            OutputGoodId = outputGoodId;
            OutputQuantity = outputQuantity;
            Inputs = list;
            Station = station ?? string.Empty;
            Skill = skill ?? string.Empty;
            MinSkill = minSkill;
            MethodCeiling = methodCeiling;
            CraftMinutes = craftMinutes;
        }

        public string Id { get; }
        public string OutputGoodId { get; }
        public int OutputQuantity { get; }
        public IReadOnlyList<RecipeInput> Inputs { get; }

        /// <summary>Crafting station needed, such as "forge" or "kitchen".</summary>
        public string Station { get; }

        /// <summary>Skill used and trained, such as "blacksmithing".</summary>
        public string Skill { get; }

        /// <summary>Skill needed to attempt the recipe at all.</summary>
        public int MinSkill { get; }

        /// <summary>
        /// Best quality this method can ever reach. Simple methods cap out early
        /// (meat over a fire); discovered methods raise the ceiling.
        /// </summary>
        public int MethodCeiling { get; }

        public int CraftMinutes { get; }

        public override string ToString() => Id;
    }

    /// <summary>Goods and recipes, cross-checked so every recipe refers to real goods.</summary>
    public sealed class Catalog
    {
        private readonly Dictionary<string, GoodDefinition> _goods;
        private readonly Dictionary<string, Recipe> _recipes;

        public Catalog(IEnumerable<GoodDefinition> goods, IEnumerable<Recipe> recipes)
        {
            _goods = new Dictionary<string, GoodDefinition>(StringComparer.Ordinal);
            foreach (var g in goods)
            {
                if (_goods.ContainsKey(g.Id)) throw new ArgumentException($"Good '{g.Id}' is defined twice.");
                _goods[g.Id] = g;
            }

            _recipes = new Dictionary<string, Recipe>(StringComparer.Ordinal);
            foreach (var r in recipes)
            {
                if (_recipes.ContainsKey(r.Id)) throw new ArgumentException($"Recipe '{r.Id}' is defined twice.");
                var output = Good(r.OutputGoodId, $"recipe '{r.Id}' output");
                if (output.Tier == GoodTier.Raw)
                    throw new ArgumentException($"Recipe '{r.Id}' makes raw good '{output.Id}'; raw goods are gathered.");
                if (output.Tier == GoodTier.RelicCore)
                    throw new ArgumentException($"Recipe '{r.Id}' makes relic core '{output.Id}'; cores only drop from dungeons.");
                foreach (var input in r.Inputs) Good(input.GoodId, $"recipe '{r.Id}' input");
                _recipes[r.Id] = r;
            }
        }

        public IEnumerable<GoodDefinition> Goods => _goods.Values;
        public IEnumerable<Recipe> Recipes => _recipes.Values;

        public GoodDefinition GetGood(string id) => Good(id, "lookup");
        public Recipe GetRecipe(string id) =>
            _recipes.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"Unknown recipe '{id}'.");

        public bool TryGetGood(string id, out GoodDefinition good) => _goods.TryGetValue(id, out good!);

        /// <summary>Recipes that make the given good.</summary>
        public IEnumerable<Recipe> RecipesFor(string goodId) => _recipes.Values.Where(r => r.OutputGoodId == goodId);

        private GoodDefinition Good(string id, string where) =>
            _goods.TryGetValue(id, out var g) ? g : throw new KeyNotFoundException($"Unknown good '{id}' in {where}.");
    }
}
