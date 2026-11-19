using Economy.Core.Items;

namespace Economy.Core.Tests;

public class DurabilityTests
{
    private static readonly GoodDefinition Pickaxe = new("pickaxe", "Pickaxe", GoodTier.Finished, GoodUse.Equipment, 400);
    private static readonly GoodDefinition RelicSword = new("relic_sword", "Relic sword", GoodTier.Finished, GoodUse.Equipment, 600, isMagical: true);

    [Fact]
    public void HigherQualityMeansMoreDurability()
    {
        var rules = new DurabilityRules();
        Assert.True(rules.Create(Pickaxe, 90).MaxDurability > rules.Create(Pickaxe, 20).MaxDurability);
        Assert.Equal(400, rules.Create(Pickaxe, 100).MaxDurability);
    }

    [Fact]
    public void WearBreaksAtZeroAndBrokenItemsDoNothing()
    {
        var rules = new DurabilityRules();
        var item = rules.Create(Pickaxe, 100);
        Assert.Equal(WearOutcome.Worn, rules.Wear(item, 399));
        Assert.Equal(WearOutcome.Broke, rules.Wear(item, 50));
        Assert.True(item.IsBroken);
        Assert.False(item.IsUsable);
        Assert.Equal(WearOutcome.NotUsable, rules.Wear(item, 1));
        Assert.Equal(1, item.BreakCount);
    }

    [Fact]
    public void NoviceRepairTakesFortyPercentOffAndExpertFifteen()
    {
        var rules = new DurabilityRules();
        var a = rules.Create(Pickaxe, 100);
        var b = rules.Create(Pickaxe, 100);
        rules.Wear(a, 400);
        rules.Wear(b, 400);
        Assert.True(rules.RepairBroken(a, repairerSkill: 0));
        Assert.True(rules.RepairBroken(b, repairerSkill: 100));
        Assert.Equal(240, a.MaxDurability);
        Assert.Equal(340, b.MaxDurability);
        Assert.Equal(a.MaxDurability, a.Durability);
    }

    [Fact]
    public void EachBreakLowersTheCeilingAndRepairNeverRaisesIt()
    {
        var rules = new DurabilityRules();
        var item = rules.Create(Pickaxe, 100);
        int previous = item.MaxDurability;
        for (int i = 0; i < 3; i++)
        {
            rules.Wear(item, item.Durability);
            Assert.True(rules.RepairBroken(item, repairerSkill: 100));
            Assert.True(item.MaxDurability < previous);
            previous = item.MaxDurability;
        }
    }

    [Fact]
    public void UpkeepCostsLessCeilingThanABreak()
    {
        var rules = new DurabilityRules();
        var maintained = rules.Create(Pickaxe, 100);
        var broken = rules.Create(Pickaxe, 100);

        rules.Wear(maintained, 200); // half worn, then topped up
        rules.Upkeep(maintained, 100);
        rules.Wear(broken, 400);
        rules.RepairBroken(broken, repairerSkill: 100);

        Assert.True(maintained.MaxDurability > broken.MaxDurability);
        Assert.Equal(maintained.MaxDurability, maintained.Durability);
    }

    [Fact]
    public void ItemsEventuallyWearOutForGood()
    {
        var rules = new DurabilityRules();
        var item = rules.Create(Pickaxe, 100);
        int repairs = 0;
        while (true)
        {
            rules.Wear(item, item.Durability);
            if (!rules.RepairBroken(item, repairerSkill: 0)) break;
            repairs++;
            Assert.True(repairs < 50, "Item never wore out.");
        }
        Assert.True(item.IsBroken);
        Assert.Equal(2, repairs); // novice: 100, 60, 20, then done
    }

    [Fact]
    public void MasterGetsAboutFiveRepairsOutOfAnItem()
    {
        var rules = new DurabilityRules();
        var item = rules.Create(Pickaxe, 100);
        int repairs = 0;
        while (repairs < 50)
        {
            rules.Wear(item, item.Durability);
            if (!rules.RepairBroken(item, repairerSkill: 100)) break;
            repairs++;
        }
        Assert.Equal(5, repairs); // 100, 85, 70, 55, 40, 25
    }

    [Fact]
    public void MagicalItemsNeedACoreToBeMade()
    {
        var rules = new DurabilityRules();
        Assert.Throws<ArgumentException>(() => rules.Create(RelicSword, 80));
        Assert.Throws<ArgumentException>(() => rules.Create(Pickaxe, 80, coreQuality: 50));
        var sword = rules.Create(RelicSword, 80, coreQuality: 70);
        Assert.True(sword.IsUsable);
        Assert.Equal(70, sword.CoreQuality);
    }

    [Fact]
    public void CoreShattersWhenAMagicalItemBreaks()
    {
        var rules = new DurabilityRules();
        var sword = rules.Create(RelicSword, 80, coreQuality: 70);
        Assert.Equal(WearOutcome.BrokeAndCoreShattered, rules.Wear(sword, sword.Durability));
        Assert.False(sword.HasCore);
        Assert.False(sword.IsUsable);
    }

    [Fact]
    public void RepairingAMagicalItemNeedsANewCore()
    {
        var rules = new DurabilityRules();
        var sword = rules.Create(RelicSword, 80, coreQuality: 70);
        rules.Wear(sword, sword.Durability);

        Assert.Throws<InvalidOperationException>(() => rules.RepairBroken(sword, repairerSkill: 100));
        Assert.True(rules.RepairBroken(sword, repairerSkill: 100, newCoreQuality: 55));
        Assert.Equal(55, sword.CoreQuality);
        Assert.True(sword.IsUsable);
    }

    [Fact]
    public void UpkeepKeepsTheCoreOnAMagicalItem()
    {
        var rules = new DurabilityRules();
        var sword = rules.Create(RelicSword, 80, coreQuality: 70);
        rules.Wear(sword, sword.Durability - 1);
        rules.Upkeep(sword, 100);
        Assert.Equal(70, sword.CoreQuality);
        Assert.True(sword.IsUsable);
    }
}
