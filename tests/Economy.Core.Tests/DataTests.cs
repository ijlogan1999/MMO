using Economy.Core.Crafting;
using Economy.Core.Items;
using Economy.Sim;

namespace Economy.Core.Tests;

public class DataTests
{
    private static readonly Catalog Catalog = DataLoader.LoadCatalog(DataLoader.FindDataDirectory(AppContext.BaseDirectory));

    [Fact]
    public void GameDataLoads()
    {
        Assert.True(Catalog.Goods.Count() >= 25);
        Assert.NotEmpty(Catalog.Recipes);
    }

    [Fact]
    public void EveryNonRawGoodCanBeCraftedExceptRelicCores()
    {
        foreach (var good in Catalog.Goods.Where(g => g.Tier != GoodTier.Raw))
        {
            bool craftable = Catalog.RecipesFor(good.Id).Any();
            if (good.Tier == GoodTier.RelicCore) Assert.False(craftable, $"{good.Id} must only drop from dungeons.");
            else Assert.True(craftable, $"{good.Id} has no recipe; every purchasable item must be craftable.");
        }
    }

    [Fact]
    public void MagicalGearNeedsARelicCoreToCraft()
    {
        foreach (var good in Catalog.Goods.Where(g => g.IsMagical))
            foreach (var recipe in Catalog.RecipesFor(good.Id))
                Assert.Contains(recipe.Inputs, i => Catalog.GetGood(i.GoodId).Tier == GoodTier.RelicCore);
    }

    [Fact]
    public void EveryGoodIsUsedOrUsedUp()
    {
        // Demand never dries up: each good is either a recipe input, or is eaten, drunk, or worn out.
        var inputs = Catalog.Recipes.SelectMany(r => r.Inputs).Select(i => i.GoodId).ToHashSet();
        foreach (var good in Catalog.Goods)
            Assert.True(inputs.Contains(good.Id) || good.Use != GoodUse.Material, $"{good.Id} is never used.");
    }

    [Fact]
    public void CatalogRejectsRecipesForRawGoodsOrCores()
    {
        var goods = new[]
        {
            new GoodDefinition("ore", "Ore", GoodTier.Raw, GoodUse.Material),
            new GoodDefinition("core", "Core", GoodTier.RelicCore, GoodUse.Material),
        };
        Assert.Throws<ArgumentException>(() => new Catalog(goods,
            new[] { new Recipe("bad", "ore", 1, new[] { new RecipeInput("core", 1) }, "x", "y", 0, 50, 1) }));
        Assert.Throws<ArgumentException>(() => new Catalog(goods,
            new[] { new Recipe("bad", "core", 1, new[] { new RecipeInput("ore", 1) }, "x", "y", 0, 50, 1) }));
    }
}
