using Economy.Core.Crafting;
using Economy.Core.Random;

namespace Economy.Core.Tests;

public class QualityTests
{
    private static Recipe MakeRecipe(int methodCeiling = 90) =>
        new("forge_test", "sword", 1, new[] { new RecipeInput("iron_ingot", 2) }, "forge", "blacksmithing", 0, methodCeiling, 10);

    private static readonly InputQuality[] GoodInputs = { new(100, 2) };

    [Fact]
    public void CeilingIsCappedByMethodSkillAndWeakestInput()
    {
        var rules = new QualityRules();
        Assert.Equal(70, rules.Range(MakeRecipe(70), 100, GoodInputs).ceiling);       // method
        Assert.Equal(45, rules.Range(MakeRecipe(), 20, GoodInputs).ceiling);          // skill + 25
        var mixed = new[] { new InputQuality(80, 3), new InputQuality(30, 1) };
        Assert.Equal(30, rules.Range(MakeRecipe(), 100, mixed).ceiling);             // weakest input
    }

    [Fact]
    public void WeightedAverageInputCapIsAvailable()
    {
        var rules = new QualityRules(new QualitySettings { InputCap = InputCapMode.WeightedAverage });
        var mixed = new[] { new InputQuality(80, 3), new InputQuality(40, 1) };
        Assert.Equal(70, rules.Range(MakeRecipe(), 100, mixed).ceiling);
    }

    [Fact]
    public void ResultAlwaysLandsInsideTheRange()
    {
        var rules = new QualityRules();
        var rng = new SimRandom(3);
        for (int i = 0; i < 2000; i++)
        {
            int skill = rng.NextInt(0, 101);
            var roll = rules.Roll(MakeRecipe(), skill, GoodInputs, rules.SimulatedPerformance(skill, rng), rng);
            if (!roll.Botched) Assert.InRange(roll.Quality, roll.Floor, roll.Ceiling);
            Assert.InRange(roll.Quality, 1, 100);
        }
    }

    [Fact]
    public void MasterIsReliablyBetterThanNovice()
    {
        var rules = new QualityRules();
        var rng = new SimRandom(11);
        double Average(int skill) => Enumerable.Range(0, 2000)
            .Average(_ => rules.Roll(MakeRecipe(), skill, GoodInputs, rules.SimulatedPerformance(skill, rng), rng).Quality);

        double novice = Average(10), expert = Average(60), master = Average(95);
        Assert.True(expert > novice + 10, $"expert {expert} vs novice {novice}");
        Assert.True(master > expert + 10, $"master {master} vs expert {expert}");
    }

    [Fact]
    public void LowPerformanceBotchesIntoATerribleItem()
    {
        var rules = new QualityRules();
        var roll = rules.Roll(MakeRecipe(), 80, GoodInputs, performance: 0.05, new SimRandom(1));
        Assert.True(roll.Botched);
        Assert.True(roll.Quality < roll.Floor);
        Assert.True(roll.Quality >= 1);
    }

    [Fact]
    public void QuickCraftUsesTheAverageOfRecentManualCrafts()
    {
        var history = new QuickCraftHistory(unlockAfter: 3, averageOver: 2);
        history.RecordManual(10);
        history.RecordManual(50);
        Assert.False(history.Unlocked);
        Assert.Throws<InvalidOperationException>(() => history.QuickCraftQuality());
        history.RecordManual(70);
        Assert.True(history.Unlocked);
        Assert.Equal(60, history.QuickCraftQuality()); // last two: 50 and 70
    }
}
