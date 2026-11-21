using System.Text.Json;
using Economy.Core.Crafting;
using Economy.Core.Items;

namespace Economy.Sim;

/// <summary>
/// Reads goods and recipes from the JSON files in data/. JSON parsing lives
/// here rather than in Economy.Core so the core stays free of serializer
/// dependencies (Unity and the server can each load data their own way).
/// </summary>
public static class DataLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Catalog LoadCatalog(string dataDirectory)
    {
        var goods = Read<GoodsFile>(Path.Combine(dataDirectory, "goods.json")).Goods
            .Select(g => new GoodDefinition(
                g.Id,
                g.Name ?? g.Id,
                Parse<GoodTier>(g.Tier, g.Id),
                Parse<GoodUse>(g.Use, g.Id),
                g.BaseDurability,
                g.IsMagical));

        var recipes = Read<RecipesFile>(Path.Combine(dataDirectory, "recipes.json")).Recipes
            .Select(r => new Recipe(
                r.Id,
                r.Output,
                r.Qty,
                r.Inputs.Select(kv => new RecipeInput(kv.Key, kv.Value)),
                r.Station,
                r.Skill,
                r.MinSkill,
                r.MethodCeiling,
                r.Minutes));

        return new Catalog(goods.ToList(), recipes.ToList());
    }

    /// <summary>Walks up from the working directory to find the repo's data/ folder.</summary>
    public static string FindDataDirectory(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "goods.json"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find data/goods.json above the working directory.");
    }

    private static T Read<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options)
               ?? throw new InvalidDataException($"{path} is empty.");
    }

    private static TEnum Parse<TEnum>(string? value, string goodId) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var result)
            ? result
            : throw new InvalidDataException($"Good '{goodId}': '{value}' is not a valid {typeof(TEnum).Name}.");

    private sealed class GoodsFile
    {
        public List<GoodRow> Goods { get; set; } = new();
    }

    private sealed class GoodRow
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string? Tier { get; set; }
        public string? Use { get; set; }
        public int BaseDurability { get; set; }
        public bool IsMagical { get; set; }
    }

    private sealed class RecipesFile
    {
        public List<RecipeRow> Recipes { get; set; } = new();
    }

    private sealed class RecipeRow
    {
        public string Id { get; set; } = "";
        public string Output { get; set; } = "";
        public int Qty { get; set; } = 1;
        public Dictionary<string, int> Inputs { get; set; } = new();
        public string Station { get; set; } = "";
        public string Skill { get; set; } = "";
        public int MinSkill { get; set; }
        public int MethodCeiling { get; set; } = 100;
        public int Minutes { get; set; } = 1;
    }
}
