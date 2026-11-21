using Economy.Core.Crafting;
using Economy.Core.Items;

namespace Economy.Sim;

/// <summary>
/// Level bands (Ian, 2026-11-19). Levels run 1-80 in four bands (1-20, 21-40,
/// 41-60, 61-80). Each piece of combat gear comes in a version per band with a
/// minimum level, so nobody wears gear above their band however they got the
/// materials. Higher bands take more materials (and rare wilds materials at
/// the top) and more crafting skill. Quality tiers (Common to Masterwork)
/// still come from the crafter's skill, inside every band.
/// </summary>
public static class LevelBands
{
    public const int Count = 4;
    public const int LevelsPerBand = 20;
    public const int MaxLevel = 80;

    /// <summary>Gear that comes in a version per band; the band-1 version keeps the plain id.</summary>
    public static readonly string[] BandedGear = { "sword", "leather_armor", "cloth_armor" };
    private static readonly double[] InputMultiplier = { 1, 1.5, 2, 2.5 };
    private static readonly int[] ExtraSkill = { 0, 15, 30, 45 };
    private static readonly int[] RareUnits = { 0, 0, 1, 2 };
    /// <summary>Alloy ingots in band 3 and 4 swords (band 2 takes copper ingots instead).</summary>
    private static readonly int[] MetalUnits = { 0, 2, 2, 4 };

    /// <summary>
    /// Weapons also come in steps inside each band (Ian, 2026-11-19: players
    /// should get a new weapon every few hours), about one every 4 levels.
    /// </summary>
    public const int StepsPerBand = 5;
    public const int LevelsPerStep = 4;
    public static readonly string[] SteppedGear = { "sword" };
    public static bool IsStepped(string baseId) => Array.IndexOf(SteppedGear, baseId) >= 0;

    public static string Id(string baseId, int band) => band == 0 ? baseId : $"{baseId}_b{band + 1}";

    /// <summary>Id of a weapon step: rank = band * StepsPerBand + step.</summary>
    public static string StepId(string baseId, int rank)
    {
        int band = rank / StepsPerBand, step = rank % StepsPerBand;
        return step == 0 ? Id(baseId, band) : $"{Id(baseId, band)}_s{step + 1}";
    }

    private static (string bandId, int step) SplitStep(string id)
    {
        int i = id.LastIndexOf("_s", StringComparison.Ordinal);
        if (i > 0 && int.TryParse(id[(i + 2)..], out int n)) return (id[..i], n - 1);
        return (id, 0);
    }

    /// <summary>Band of a good: from its id, or fixed for the special top gear.</summary>
    public static int BandOf(string id)
    {
        if (id == "plate_armor") return 2;
        if (id == "relic_sword") return 3;
        id = SplitStep(id).bandId;
        int i = id.LastIndexOf("_b", StringComparison.Ordinal);
        return i > 0 && int.TryParse(id[(i + 2)..], out int n) ? n - 1 : 0;
    }

    /// <summary>How far up the gear ladder a piece sits: band * StepsPerBand + step.</summary>
    public static int RankOf(string id) => BandOf(id) * StepsPerBand + SplitStep(id).step;

    public static int BandForLevel(int level) => Math.Clamp((level - 1) / LevelsPerBand, 0, Count - 1);

    private static int Hash(string baseId, int salt)
    {
        int h = 17;
        foreach (char c in baseId) h = unchecked(h * 31 + c);
        return (int)((uint)unchecked(h + salt * 7919) & 0x7fffffff);
    }

    private static string BaseOf(string id)
    {
        id = SplitStep(id).bandId;
        int i = id.LastIndexOf("_b", StringComparison.Ordinal);
        return i > 0 ? id[..i] : id;
    }

    /// <summary>
    /// Each piece has its own minimum level, spread out inside its band so
    /// requirements aren't all 1/21/41/61 (Ian, 2026-11-19). Band 1 starts at
    /// level 1 so new players can gear up. Weapon steps come about every 4 levels.
    /// </summary>
    public static int MinLevel(string id)
    {
        if (id == "plate_armor" || id == "relic_sword") return BandOf(id) * LevelsPerBand + 1;
        int band = BandOf(id);
        var (_, step) = SplitStep(id);
        string baseId = BaseOf(id);
        if (IsStepped(baseId))
        {
            int start = band == 0 ? 1 : band * LevelsPerBand + 1 + Hash(baseId, band) % 3;
            return step == 0 ? start : start + step * LevelsPerStep - 1 + Hash(baseId, band * 10 + step) % 2;
        }
        if (band == 0) return 1;
        return band * LevelsPerBand + 1 + Hash(baseId, band) % 13;
    }

    /// <summary>Highest band of this gear the player's level allows.</summary>
    public static int WearableBand(string baseId, int level) => WearableRank(baseId, level) / (IsStepped(baseId) ? StepsPerBand : 1);

    /// <summary>Highest rank (band, or band and step for weapons) the player's level allows.</summary>
    public static int WearableRank(string baseId, int level)
    {
        if (!IsStepped(baseId))
        {
            int b = BandForLevel(level);
            while (b > 0 && MinLevel(Id(baseId, b)) > level) b--;
            return b;
        }
        int r = Count * StepsPerBand - 1;
        while (r > 0 && MinLevel(StepId(baseId, r)) > level) r--;
        return r;
    }

    /// <summary>The id a player of this level should wear.</summary>
    public static string WearableId(string baseId, int level) =>
        IsStepped(baseId) ? StepId(baseId, WearableRank(baseId, level)) : Id(baseId, WearableRank(baseId, level));

    /// <summary>Adds the band 2-4 versions of each banded good and its recipe.</summary>
    public static (List<GoodDefinition> goods, List<Recipe> recipes) Expand(List<GoodDefinition> goods, List<Recipe> recipes)
    {
        var outGoods = new List<GoodDefinition>(goods);
        var outRecipes = new List<Recipe>(recipes);
        foreach (var id in BandedGear)
        {
            var g = goods.FirstOrDefault(x => x.Id == id);
            var r = recipes.FirstOrDefault(x => x.OutputGoodId == id);
            if (g == null || r == null) continue;
            string rare = id == "sword" ? "ember_slag" : "ice_crystal";
            for (int b = 1; b < Count; b++)
            {
                string bid = Id(id, b);
                outGoods.Add(new GoodDefinition(bid, $"{g.Name} (level {MinLevel(bid)}+)", g.Tier, g.Use,
                    (int)Math.Round(g.BaseDurability * (1 + 0.25 * b)), g.IsMagical));
                List<RecipeInput> inputs;
                if (id == "sword" && goods.Any(x => x.Id == "copper_ingot"))
                {
                    // Metal path (Ian, 2026-11-19): every sword starts from an iron
                    // blade; levels 21-40 add copper, 41+ a copper-iron alloy smelted
                    // from both, so early iron stays in demand all the way up.
                    inputs = r.Inputs.Select(i => new RecipeInput(i.GoodId, i.Quantity)).ToList();
                    inputs.Add(b == 1 ? new RecipeInput("copper_ingot", 2) : new RecipeInput("alloy_ingot", MetalUnits[b]));
                }
                else inputs = r.Inputs.Select(i => new RecipeInput(i.GoodId, (int)Math.Ceiling(i.Quantity * InputMultiplier[b]))).ToList();
                if (RareUnits[b] > 0 && goods.Any(x => x.Id == rare)) inputs.Add(new RecipeInput(rare, RareUnits[b]));
                outRecipes.Add(new Recipe($"{r.Id}_b{b + 1}", bid, r.OutputQuantity, inputs, r.Station, r.Skill,
                    Math.Min(95, r.MinSkill + ExtraSkill[b]), r.MethodCeiling, r.CraftMinutes + 10 * b));
            }
        }
        // Weapon steps: each step up adds an iron ingot (early ore stays in demand)
        // and a little skill and time; durability rises slightly.
        foreach (var id in SteppedGear)
            for (int b = 0; b < Count; b++)
            {
                string bid = Id(id, b);
                var g = outGoods.FirstOrDefault(x => x.Id == bid);
                var r = outRecipes.FirstOrDefault(x => x.OutputGoodId == bid);
                if (g == null || r == null) continue;
                string baseName = goods.First(x => x.Id == id).Name;
                for (int st = 1; st < StepsPerBand; st++)
                {
                    string sid = StepId(id, b * StepsPerBand + st);
                    outGoods.Add(new GoodDefinition(sid, $"{baseName} (level {MinLevel(sid)}+)", g.Tier, g.Use,
                        (int)Math.Round(g.BaseDurability * (1 + 0.05 * st)), g.IsMagical));
                    var inputs = r.Inputs.Select(i => new RecipeInput(i.GoodId, i.Quantity)).ToList();
                    int iron = inputs.FindIndex(i => i.GoodId == "iron_ingot");
                    if (iron >= 0) inputs[iron] = new RecipeInput("iron_ingot", inputs[iron].Quantity + st);
                    else inputs.Add(new RecipeInput("iron_ingot", st));
                    outRecipes.Add(new Recipe($"{r.Id}_s{st + 1}", sid, r.OutputQuantity, inputs, r.Station, r.Skill,
                        Math.Min(95, r.MinSkill + 3 * st), r.MethodCeiling, r.CraftMinutes + 2 * st));
                }
            }
        return (outGoods, outRecipes);
    }
}
