using System.Text.Json;
using Economy.Core.Crafting;
using Economy.Core.Items;

namespace Economy.Sim.World;

/// <summary>Goods by index, with prices and lookups the hot loop needs.</summary>
public sealed class GoodsTable
{
    public GoodsTable(Catalog catalog, IReadOnlyDictionary<string, double> rawBasePrices)
    {
        Catalog = catalog;
        Defs = catalog.Goods.OrderBy(g => g.Id, StringComparer.Ordinal).ToArray();
        Index = Defs.Select((g, i) => (g.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        Count = Defs.Length;

        RecipesByOutput = new List<Recipe>[Count];
        for (int i = 0; i < Count; i++) RecipesByOutput[i] = catalog.RecipesFor(Defs[i].Id).ToList();

        BasePrice = new double[Count];
        var done = new bool[Count];
        for (int i = 0; i < Count; i++) PriceOf(i, rawBasePrices, done, 0);

        RepairMaterial = new int[Count];
        for (int i = 0; i < Count; i++)
        {
            RepairMaterial[i] = -1;
            if (!Defs[i].HasDurability) continue;
            var recipe = RecipesByOutput[i].FirstOrDefault();
            var material = recipe?.Inputs
                .Select(inp => Index[inp.GoodId])
                .FirstOrDefault(idx => Defs[idx].Tier == GoodTier.Intermediate, -1) ?? -1;
            RepairMaterial[i] = material;
        }
    }

    public Catalog Catalog { get; }
    public GoodDefinition[] Defs { get; }
    public Dictionary<string, int> Index { get; }
    public int Count { get; }
    public List<Recipe>[] RecipesByOutput { get; }

    /// <summary>Starting reference price for every good, in coins.</summary>
    public double[] BasePrice { get; }

    /// <summary>Intermediate used to repair each equipment good (iron ingot, leather), or -1.</summary>
    public int[] RepairMaterial { get; }

    public int this[string id] => Index[id];

    private Dictionary<int, int[]>? _ladders;

    /// <summary>All goods on the same gear ladder (same base piece, every band and step).</summary>
    public int[] GearLadder(int good)
    {
        _ladders ??= new();
        if (_ladders.TryGetValue(good, out var l)) return l;
        string id = Defs[good].Id;
        string baseId = LevelBands.BandedGear.FirstOrDefault(b => id == b || id.StartsWith(b + "_b") || id.StartsWith(b + "_s")) ?? id;
        l = Enumerable.Range(0, Count).Where(i => Defs[i].Id == baseId || Defs[i].Id.StartsWith(baseId + "_b") || Defs[i].Id.StartsWith(baseId + "_s")).ToArray();
        _ladders[good] = l;
        return l;
    }

    /// <summary>Crafted goods: inputs at base price, a 25% margin, plus labor at half a coin a minute.</summary>
    private double PriceOf(int i, IReadOnlyDictionary<string, double> raw, bool[] done, int depth)
    {
        if (done[i]) return BasePrice[i];
        if (depth > 20) throw new InvalidDataException("Recipe loop detected while pricing goods.");
        var def = Defs[i];
        double price;
        if (raw.TryGetValue(def.Id, out var p)) price = p;
        else if (RecipesByOutput[i].Count > 0)
        {
            var r = RecipesByOutput[i][0];
            double inputs = r.Inputs.Sum(inp => PriceOf(Index[inp.GoodId], raw, done, depth + 1) * inp.Quantity);
            price = (inputs * 1.25 + r.CraftMinutes * 5) / r.OutputQuantity;
        }
        else throw new InvalidDataException($"Good '{def.Id}' has no base price and no recipe.");
        BasePrice[i] = Math.Max(1, price);
        done[i] = true;
        return BasePrice[i];
    }
}

public sealed class TownDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public double Size { get; set; }
    public Dictionary<string, double> Nodes { get; set; } = new();
    /// <summary>Rare materials in the dangerous wilds around the town (party trips only).</summary>
    public Dictionary<string, double> Wilds { get; set; } = new();
}

public sealed class WorldFile
{
    public List<string> Regions { get; set; } = new();
    public List<TownDef> Towns { get; set; } = new();
    public Dictionary<string, int> TravelHours { get; set; } = new();
    public List<string> RegionOrder { get; set; } = new();
    public Dictionary<string, JsonElement> BasePrices { get; set; } = new();

    public static WorldFile Load(string dataDirectory)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        WorldFile world;
        using (var s = File.OpenRead(Path.Combine(dataDirectory, "world.json")))
            world = JsonSerializer.Deserialize<WorldFile>(s, options) ?? throw new InvalidDataException("world.json is empty.");
        var startPath = Path.Combine(dataDirectory, StartPricesFile);
        if (File.Exists(startPath))
        {
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(startPath), options) ?? new();
            foreach (var kv in doc)
                if (kv.Value.ValueKind == JsonValueKind.Number) world.StartPrices[kv.Key] = kv.Value.GetDouble();
        }
        return world;
    }

    /// <summary>Optional file of calibrated starting prices (written by --calibrate).</summary>
    public const string StartPricesFile = "start_prices.json";

    /// <summary>Calibrated starting prices; these override recipe roll-ups and raw base prices.</summary>
    public Dictionary<string, double> StartPrices { get; } = new();

    /// <summary>Design prices from world.json only, ignoring calibrated start prices.</summary>
    public Dictionary<string, double> DesignRawPrices() => BasePrices
        .Where(kv => kv.Value.ValueKind == JsonValueKind.Number)
        .ToDictionary(kv => kv.Key, kv => kv.Value.GetDouble());

    public Dictionary<string, double> RawPrices()
    {
        var prices = BasePrices
            .Where(kv => kv.Value.ValueKind == JsonValueKind.Number)
            .ToDictionary(kv => kv.Key, kv => kv.Value.GetDouble());
        foreach (var kv in StartPrices) prices[kv.Key] = kv.Value;
        return prices;
    }

    public int TravelHoursBetween(string regionA, string regionB)
    {
        if (regionA == regionB) return TravelHours.GetValueOrDefault("sameRegion", 2);
        int a = RegionOrder.IndexOf(regionA), b = RegionOrder.IndexOf(regionB);
        return Math.Abs(a - b) <= 1
            ? TravelHours.GetValueOrDefault("adjacentRegion", 4)
            : TravelHours.GetValueOrDefault("farRegion", 7);
    }
}
