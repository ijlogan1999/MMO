using Economy.Core.Crafting;
using Economy.Sim;
using Xunit;

namespace Economy.Core.Tests;

public class LevelBandTests
{
    private static readonly Catalog Catalog = DataLoader.LoadCatalog(DataLoader.FindDataDirectory(AppContext.BaseDirectory));

    [Fact]
    public void MinimumLevelsVaryInsideEachBand()
    {
        foreach (var id in LevelBands.BandedGear)
            Assert.Equal(1, LevelBands.MinLevel(id));
        for (int b = 1; b < LevelBands.Count; b++)
        {
            var levels = LevelBands.BandedGear.Select(id => LevelBands.MinLevel(LevelBands.Id(id, b))).ToList();
            Assert.All(levels, l => Assert.InRange(l, b * LevelBands.LevelsPerBand + 1, b * LevelBands.LevelsPerBand + 13));
        }
        var all = Enumerable.Range(1, LevelBands.Count - 1).SelectMany(b => LevelBands.BandedGear.Select(id => LevelBands.MinLevel(LevelBands.Id(id, b)) % LevelBands.LevelsPerBand));
        Assert.True(all.Distinct().Count() > 2, "minimum levels should not all sit on the band edge");
    }

    [Fact]
    public void EverySwordNeedsIronAndHigherBandsAddCopperThenAlloy()
    {
        Recipe For(string id) => Catalog.RecipesFor(id).First();
        Assert.Contains(For("blade_blank").Inputs, i => i.GoodId == "iron_ingot");
        for (int b = 0; b < LevelBands.Count; b++)
            Assert.Contains(For(LevelBands.Id("sword", b)).Inputs, i => i.GoodId == "blade_blank");
        Assert.Contains(For("sword_b2").Inputs, i => i.GoodId == "copper_ingot");
        Assert.Contains(For("sword_b3").Inputs, i => i.GoodId == "alloy_ingot");
        Assert.Contains(For("sword_b4").Inputs, i => i.GoodId == "alloy_ingot");
        var alloy = For("alloy_ingot").Inputs.Select(i => i.GoodId).ToList();
        Assert.Contains("iron_ingot", alloy);
        Assert.Contains("copper_ingot", alloy);
    }

    [Fact]
    public void WearableBandWaitsForEachPiecesOwnLevel()
    {
        int min = LevelBands.MinLevel("sword_b2");
        Assert.Equal(0, LevelBands.WearableBand("sword", min - 1));
        Assert.Equal(1, LevelBands.WearableBand("sword", min));
    }
}
