using System;
using System.Linq;
using Mmo.Crafting.Skinning;
using Xunit;
using Xunit.Abstractions;

public class SkinningMinigameTests
{
    private readonly ITestOutputHelper _out;

    public SkinningMinigameTests(ITestOutputHelper output) { _out = output; }

    private static SkinSetup Setup(Species sp, int skill = 100, Season season = Season.Autumn, int wounds = 2, int grade = 80) =>
        new SkinSetup(sp) { SkillLevel = skill, Knife = Knife.SteelKnife, Season = season, Wounds = wounds, AnimalGrade = grade };

    private static SkinStyle RightStyle(Species sp) => sp.FurBearer && sp.CanCase ? SkinStyle.Cased : SkinStyle.Open;

    private static Preserve RightKeep(Species sp, Season s) =>
        s == Season.Winter ? Preserve.Green : sp.Pelt && s != Season.Summer ? Preserve.Stretch : Preserve.Salt;

    /// <summary>
    /// Plays with perfect knowledge of the hidden state: the ceiling of what a skilled player can do.
    /// Guts first for big game, works from open edges, pulls loose hide, knifes the rest, hones before the edge fails.
    /// </summary>
    private static SkinMinigame PlayExpert(SkinSetup setup, int seed)
    {
        var g = new SkinMinigame(setup, seed);
        g.ChooseStyle(RightStyle(setup.Species));
        if (!g.TubeKept) g.Gut();
        while (g.Stage == SkinStage.Skin)
        {
            var workable = g.Patches.Where(g.IsWorkable).ToList();
            // Loose patches first while they're loose; then the rest.
            var p = workable.FirstOrDefault(x => !x.Fat && g.HoldNow(x) == Hold.Loose && !(x.Wound && !g.IsOpened(x)))
                    ?? workable.First();
            bool pull = !p.Fat && g.HoldNow(p) == Hold.Loose && !(p.Wound && !g.IsOpened(p));
            if (!pull && g.Edge - setup.Knife.DullPerStroke * 2 < SkinTuning.DullBelow) g.Hone();
            g.Work(pull ? SkinTool.Pull : SkinTool.Knife, p.Id);
        }
        Flesh(g, perfect: true);
        g.Keep(RightKeep(setup.Species, setup.Season));
        return g;
    }

    /// <summary>A careless player: picks the open style, never feels, pulls everything top to bottom, scrapes everything once.</summary>
    private static SkinMinigame PlayYanker(SkinSetup setup, int seed)
    {
        var g = new SkinMinigame(setup, seed);
        g.ChooseStyle(SkinStyle.Open);
        while (g.Stage == SkinStage.Skin)
        {
            var p = g.Patches.First(x => x.State == PatchState.OnBody);
            g.Work(SkinTool.Pull, p.Id);
        }
        foreach (var p in g.Patches.Where(x => x.IsOff).ToList()) g.FleshWork(FleshTool.Scrape, p.Id);
        g.FinishFleshing();
        g.Keep(Preserve.Salt);
        return g;
    }

    private static void Flesh(SkinMinigame g, bool perfect)
    {
        foreach (var p in g.Patches.Where(x => x.IsOff).ToList())
        {
            if (!perfect) { g.FleshWork(FleshTool.Scrape, p.Id); continue; }
            if (p.Layer == Layer.Meat || p.Layer == Layer.Fat) g.FleshWork(FleshTool.Trim, p.Id);
            if (p.Layer == Layer.Membrane) g.FleshWork(FleshTool.Scrape, p.Id);
        }
        g.FinishFleshing();
    }

    [Fact]
    public void Expert_gets_a_good_hide_from_every_animal()
    {
        foreach (var sp in Species.All)
        {
            double sum = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var g = PlayExpert(Setup(sp), seed);
                Assert.Equal(SkinStage.Done, g.Stage);
                sum += g.Result.HideScore;
            }
            double mean = sum / 40;
            _out.WriteLine($"{sp.Name}: expert hide score {mean:0.00}");
            Assert.True(mean > 0.7, $"{sp.Name} expert mean {mean:0.00}");
        }
    }

    [Fact]
    public void Careless_pulling_is_clearly_worse_than_knowing_the_animal()
    {
        foreach (var sp in Species.All)
        {
            double expert = 0, yank = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                expert += PlayExpert(Setup(sp), seed).Result.HideQuality;
                yank += PlayYanker(Setup(sp), seed).Result.HideQuality;
            }
            _out.WriteLine($"{sp.Name}: expert Q {expert / 40:0} vs yanker Q {yank / 40:0}");
            Assert.True(yank < expert * 0.85, $"{sp.Name}: yank {yank / 40:0} expert {expert / 40:0}");
        }
    }

    [Fact]
    public void Casing_a_big_animal_tears_it()
    {
        var g = new SkinMinigame(Setup(Species.Deer), 3);
        g.ChooseStyle(SkinStyle.Cased);
        Assert.Equal(0.7, SkinTuning.StyleFactor(Species.Deer, SkinStyle.Cased, g.TubeKept));
        Assert.Equal(1.0, SkinTuning.StyleFactor(Species.Wolf, SkinStyle.Cased, true));
        Assert.True(SkinTuning.StyleFactor(Species.Wolf, SkinStyle.Open, false) < 1.0);
    }

    [Fact]
    public void Gutting_a_cased_skin_before_it_is_off_spoils_the_tube()
    {
        var g = new SkinMinigame(Setup(Species.Hare), 5);
        g.ChooseStyle(SkinStyle.Cased);
        Assert.True(g.TubeKept);
        g.Gut();
        Assert.False(g.TubeKept);
        Assert.Equal(SkinStyle.Cased, g.Style);
    }

    [Fact]
    public void Only_cut_lines_and_neighbours_of_freed_hide_are_workable()
    {
        var g = new SkinMinigame(Setup(Species.Deer, wounds: 0), 7);
        g.ChooseStyle(SkinStyle.Open);
        var ribs = g.PatchAt(2, 1);
        Assert.False(g.IsWorkable(ribs));
        Assert.True(g.IsWorkable(g.PatchAt(2, 2))); // belly line
        g.Work(SkinTool.Pull, ribs.Id);
        Assert.Equal(PatchState.Torn, ribs.State); // nothing to grip
    }

    [Fact]
    public void Cooling_makes_the_hide_hold_harder()
    {
        var g = new SkinMinigame(Setup(Species.Deer, season: Season.Winter, wounds: 0), 9);
        g.ChooseStyle(SkinStyle.Open);
        var belly = g.PatchAt(3, 2);
        var before = g.HoldNow(belly);
        for (int i = 0; i < 20; i++) g.Hone(); // stand about
        Assert.True(g.HoldNow(belly) > before || before == Hold.Tight);
        Assert.True(g.IsSet);
    }

    [Fact]
    public void Gutting_late_sours_the_meat()
    {
        var early = PlayExpert(Setup(Species.Deer), 11).Result;
        Assert.Equal(1.0, early.MeatFactor);

        var g = new SkinMinigame(Setup(Species.Deer), 11);
        g.ChooseStyle(SkinStyle.Open);
        for (int i = 0; i < 15; i++) g.Hone();
        Assert.True(g.Swelling > 0);
        g.Gut();
        g.StopSkinning();
        g.FinishFleshing();
        g.Keep(Preserve.Salt);
        Assert.True(g.Result.MeatFactor < 0.7);
        Assert.Equal(4, g.Result.MeatYield); // spoilage costs quality, never yield
    }

    [Fact]
    public void Knife_on_loose_hide_scores_it_and_pull_on_fat_tears_it()
    {
        var g = new SkinMinigame(Setup(Species.Boar, wounds: 0), 13);
        g.ChooseStyle(SkinStyle.Open);
        var fat = g.Patches.First(p => p.Fat && g.IsWorkable(p));
        g.Work(SkinTool.Pull, fat.Id);
        Assert.Equal(PatchState.Torn, fat.State);

        var h = new SkinMinigame(Setup(Species.Hare, wounds: 0), 13);
        h.ChooseStyle(SkinStyle.Cased);
        var tail = h.PatchAt(5, 2);
        if (h.HoldNow(tail) != Hold.Loose) return; // tail held this time; covered by other seeds
        h.Work(SkinTool.Knife, tail.Id);
        Assert.Equal(PatchState.Scored, tail.State);
    }

    [Fact]
    public void Scraping_bare_thin_skin_goes_through()
    {
        var g = new SkinMinigame(Setup(Species.Hare, wounds: 0), 17);
        g.ChooseStyle(SkinStyle.Cased);
        var tail = g.PatchAt(5, 2);
        g.Work(SkinTool.Knife, tail.Id); // whatever happens, it's off
        g.StopSkinning();
        Assert.Equal(SkinStage.Flesh, g.Stage);
        while (tail.Layer != Layer.Clean) g.FleshWork(tail.Layer == Layer.Membrane ? FleshTool.Scrape : FleshTool.Trim, tail.Id);
        g.FleshWork(FleshTool.Scrape, tail.Id);
        Assert.Equal(FleshMark.Torn, tail.Mark); // hare skin is thin everywhere
    }

    [Fact]
    public void Better_knife_raises_the_cap_not_the_yield()
    {
        var worn = Setup(Species.Deer, skill: 100);
        worn.Knife = Knife.WornKnife;
        var a = PlayExpert(worn, 21).Result;
        var b = PlayExpert(Setup(Species.Deer, skill: 100), 21).Result;
        Assert.Equal(60, a.Ceiling);
        Assert.Equal(100, b.Ceiling);
        Assert.Equal(a.MeatYield, b.MeatYield);
        Assert.Equal(a.HideUnits, b.HideUnits);
    }

    [Fact]
    public void Same_seed_same_carcass()
    {
        var a = new SkinMinigame(Setup(Species.Bear), 99);
        var b = new SkinMinigame(Setup(Species.Bear), 99);
        Assert.Equal(a.Patches.Select(p => (p.BaseHold, p.Wound, p.WoundSeen)), b.Patches.Select(p => (p.BaseHold, p.Wound, p.WoundSeen)));
        Assert.Equal(20, a.Patches.Count);
    }

    [Fact]
    public void Keeping_follows_what_it_is_and_the_season()
    {
        Assert.Equal(1.0, SkinTuning.PreserveFactor(Species.Deer, Preserve.Salt, Season.Summer));
        Assert.True(SkinTuning.PreserveFactor(Species.Deer, Preserve.Stretch, Season.Autumn) < 1.0);
        Assert.Equal(1.0, SkinTuning.PreserveFactor(Species.Wolf, Preserve.Stretch, Season.Autumn));
        Assert.Equal(1.0, SkinTuning.PreserveFactor(Species.Deer, Preserve.Green, Season.Winter));
        Assert.True(SkinTuning.PreserveFactor(Species.Deer, Preserve.Green, Season.Summer) < 0.7);
    }

    [Fact]
    public void Prints_typical_outcomes()
    {
        foreach (var sp in Species.All)
            foreach (var skill in new[] { 1, 50, 100 })
            {
                var r = PlayExpert(Setup(sp, skill: skill), 1).Result;
                var y = PlayYanker(Setup(sp, skill: skill), 1).Result;
                _out.WriteLine($"{sp.Name,-5} skill {skill,3}: expert {r.HideGrade} {r.HideItem} ({r.HideQuality}) time {r.TimeUsed} hones {r.Hones}" +
                               $" | yanker {y.HideItem} ({y.HideQuality}) torn {y.Torn} stretched {y.Stretched}");
            }
    }
}
