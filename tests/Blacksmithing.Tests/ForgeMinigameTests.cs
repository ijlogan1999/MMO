using System;
using System.Linq;
using Mmo.Crafting.Blacksmithing;
using Xunit;

public class ForgeMinigameTests
{
    private const double Dt = 1.0 / 60.0;

    /// <summary>Plays a whole craft. accuracy 1 = always on the beat, 0 = never strikes.</summary>
    private static ForgeResult Play(ForgeSetup setup, int seed, bool strike = true, double timingOffset = 0,
        QuenchMedium medium = QuenchMedium.Oil, double pullAt = double.NaN, bool reheat = true)
    {
        var g = new ForgeMinigame(setup, seed);
        double pull = double.IsNaN(pullAt) ? setup.Recipe.TargetHeat : pullAt;
        for (int frame = 0; frame < 60 * 600 && g.Stage != ForgeStage.Done; frame++)
        {
            g.Update(Dt);
            switch (g.Stage)
            {
                case ForgeStage.Heat:
                    if (g.Temperature >= pull) g.PullFromForge();
                    break;
                case ForgeStage.Strike:
                    if (reheat && g.Temperature < ForgeTuning.WorkMin + 0.01) { g.Reheat(); break; }
                    if (!strike) break;
                    // Strike the part that most needs work, on the beat; set the hammer down when the shape matches.
                    int best = -1; double need = 0.4;
                    for (int lane = 0; lane < 3; lane++)
                    {
                        double left = g.Target[lane] - g.Progress[lane];
                        if (left > need) { need = left; best = lane; }
                    }
                    if (best < 0) { g.SetDownHammer(); break; }
                    double k = Math.Round((g.SongTime - timingOffset) / g.BeatInterval);
                    if (k >= 0 && Math.Abs(k * g.BeatInterval + timingOffset - g.SongTime) <= Dt / 2) g.Strike((Lane)best);
                    break;
                case ForgeStage.QuenchChoose:
                    g.ChooseQuench(medium);
                    break;
                case ForgeStage.QuenchHold:
                    if (!g.QuenchHeld) g.BeginQuench();
                    else if (g.QuenchSeconds >= ForgeTuning.QuenchTargetSeconds(medium)) g.EndQuench();
                    break;
                case ForgeStage.Finish:
                    if (Math.Abs(g.FinishCursor - g.FinishSweetCenter) <= ForgeTuning.FinishCursorSpeed * Dt) g.FinishStroke();
                    break;
            }
            g.Events.Clear();
        }
        Assert.Equal(ForgeStage.Done, g.Stage);
        return g.Result;
    }

    private static ForgeSetup Setup(ForgeRecipe recipe, int skill, int inputQ = 100) => new ForgeSetup(recipe)
    {
        SkillLevel = skill,
        Station = ForgeStation.ShopForge,
        Hammer = Hammer.SteelHammer,
        InputQualities = Enumerable.Repeat(inputQ, recipe.Inputs.Count).ToArray(),
    };

    [Theory]
    [InlineData("plain_iron_sword")]
    [InlineData("iron_shortsword")]
    [InlineData("iron_longsword")]
    public void PerfectPlayScoresNearOne(string id)
    {
        var recipe = ForgeRecipe.All.Single(r => r.Id == id);
        var r = Play(Setup(recipe, 50), seed: 1);
        Assert.Equal(recipe.TotalStrikes, r.Perfect);
        Assert.Equal(0, r.OffBeat);
        Assert.True(r.Shape > 0.99, $"shape {r.Shape}");
        Assert.True(r.Score > 0.85, $"score {r.Score}");
        Assert.False(r.Botch);
        Assert.True(r.Quality >= r.Floor + (r.Ceiling - r.Floor) * 0.7, $"Q {r.Quality}");
    }

    [Fact]
    public void NoStrikesIsABotch()
    {
        var r = Play(Setup(ForgeRecipe.PlainIronSword, 10), seed: 2, strike: false);
        Assert.True(r.Botch);
        Assert.InRange(r.Quality, 1, 10);
        Assert.Equal(0, r.SkillGain);
    }

    [Fact]
    public void CeilingIsTheLowestCap()
    {
        var s = Setup(ForgeRecipe.IronLongsword, 60, inputQ: 90);
        s.Station = ForgeStation.TownForge; // 75
        var r = Play(s, 3);
        Assert.Equal(75, r.Ceiling);
        Assert.StartsWith("Station", r.CeilingReason);

        s.Hammer = Hammer.WornHammer; // 60
        r = Play(s, 3);
        Assert.Equal(60, r.Ceiling);
        Assert.StartsWith("Tool", r.CeilingReason);

        s.InputQualities = new[] { 30, 30, 30 };
        r = Play(s, 3);
        Assert.Equal(30, r.Ceiling);
        Assert.Equal("Input materials", r.CeilingReason);
    }

    [Fact]
    public void InputQualityIsWeightedTowardTheBlade()
    {
        var s = Setup(ForgeRecipe.IronLongsword, 60);
        s.InputQualities = new[] { 100, 0, 0 }; // blade 60%, guard 25%, grip 15%
        Assert.Equal(60, Play(s, 4).InputMeanQuality);
    }

    [Fact]
    public void SkillWidensTheTimingWindow()
    {
        Assert.Equal(0.060, ForgeTuning.PerfectWindow(1, Hammer.IronHammer), 3);
        Assert.Equal(0.140, ForgeTuning.PerfectWindow(100, Hammer.IronHammer), 3);
        // 100 ms late is a Good for a novice and a Perfect for a master.
        var novice = Play(Setup(ForgeRecipe.PlainIronSword, 1), 5, timingOffset: 0.1);
        var master = Play(Setup(ForgeRecipe.PlainIronSword, 100), 5, timingOffset: 0.1);
        Assert.Equal(0, novice.Perfect);
        Assert.True(novice.Good > 0);
        Assert.Equal(ForgeRecipe.PlainIronSword.TotalStrikes, master.Perfect);
        Assert.True(master.Score > novice.Score);
    }

    [Fact]
    public void BetterSkillRaisesTheFloor()
    {
        Assert.True(ForgeTuning.SkillFloor(80) > ForgeTuning.SkillFloor(10));
    }

    [Fact]
    public void ColdMetalStrikesDoNotCount()
    {
        // Never reheat on a long recipe: the bar goes cold and later strikes stop counting.
        var r = Play(Setup(ForgeRecipe.IronLongsword, 50), 6, reheat: false);
        Assert.True(r.Cold > 0, $"cold {r.Cold}");
        var warm = Play(Setup(ForgeRecipe.IronLongsword, 50), 6);
        Assert.True(r.Shape < warm.Shape, $"cold shape {r.Shape} vs warm {warm.Shape}");
    }

    [Fact]
    public void ReheatingKeepsTheBladeWorkable()
    {
        var r = Play(Setup(ForgeRecipe.IronLongsword, 50), 6);
        Assert.Equal(0, r.Cold);
        Assert.True(r.Reheats >= 1);
    }

    [Fact]
    public void PullingTooColdCracksAndTooHotBurns()
    {
        var cold = Play(Setup(ForgeRecipe.PlainIronSword, 50), 7, pullAt: 0.15);
        Assert.Contains(cold.Flaws, f => f.StartsWith("Cracked"));
        var hot = Play(Setup(ForgeRecipe.PlainIronSword, 50), 7, pullAt: 2.0); // never pulled: auto-burn
        Assert.Contains(hot.Flaws, f => f.StartsWith("Burned"));
        var good = Play(Setup(ForgeRecipe.PlainIronSword, 50), 7);
        Assert.True(good.HeatControl > cold.HeatControl && good.HeatControl > hot.HeatControl);
    }

    [Fact]
    public void OilNeverCracksWhenHeldRight()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var r = Play(Setup(ForgeRecipe.IronLongsword, 1), seed, medium: QuenchMedium.Oil);
            Assert.DoesNotContain(r.Flaws, f => f.StartsWith("Quench crack"));
        }
    }

    [Fact]
    public void NoviceWaterQuenchSometimesCracks()
    {
        int cracks = Enumerable.Range(0, 200)
            .Count(seed => Play(Setup(ForgeRecipe.IronLongsword, 1), seed, medium: QuenchMedium.Water)
                .Flaws.Any(f => f.StartsWith("Quench crack")));
        Assert.InRange(cracks, 15, 80); // about 20%
    }

    [Fact]
    public void GradeWords()
    {
        Assert.Equal("Torn", ForgeTuning.GradeWord(5));
        Assert.Equal("Rough", ForgeTuning.GradeWord(30));
        Assert.Equal("Common", ForgeTuning.GradeWord(50));
        Assert.Equal("Fine", ForgeTuning.GradeWord(70));
        Assert.Equal("Superb", ForgeTuning.GradeWord(95));
    }

    [Fact]
    public void TrackHasSomeSpareBeats()
    {
        foreach (var recipe in ForgeRecipe.All)
        {
            var g = new ForgeMinigame(Setup(recipe, 50), 9);
            Assert.True(g.BeatCount > recipe.TotalStrikes);
            Assert.Equal(g.BeatCount, g.BeatsLeft);
        }
    }

    [Fact]
    public void OverworkingAPartHurtsTheShape()
    {
        var g = new ForgeMinigame(Setup(ForgeRecipe.PlainIronSword, 50), 11);
        while (g.Stage == ForgeStage.Heat) { g.Update(Dt); if (g.Temperature >= 0.55) g.PullFromForge(); }
        // Hammer only the body on every beat.
        while (g.Stage == ForgeStage.Strike)
        {
            g.Update(Dt);
            double k = Math.Round(g.SongTime / g.BeatInterval);
            if (k >= 0 && Math.Abs(k * g.BeatInterval - g.SongTime) <= Dt / 2) g.Strike(Lane.Body);
        }
        Assert.True(g.Progress[1] > g.Target[1] * 1.5);
        Assert.True(g.Result.Shape < 0.4, $"shape {g.Result.Shape}");
    }

    [Fact]
    public void SecondStrikeOnTheSameBeatIsOffBeat()
    {
        var g = new ForgeMinigame(Setup(ForgeRecipe.PlainIronSword, 50), 12);
        while (g.Stage == ForgeStage.Heat) { g.Update(Dt); if (g.Temperature >= 0.55) g.PullFromForge(); }
        while (g.SongTime < 0) g.Update(Dt);
        g.Strike(Lane.Body);
        g.Strike(Lane.Tip);
        Assert.Equal(1.0, g.Progress[1]);
        Assert.Equal(0.0, g.Progress[0]);
    }

    [Fact]
    public void CraftLengthIsInsideTheSpecRange()
    {
        // Spec: each minigame lasts 20 to 90 seconds (the novice sword is allowed to be a bit shorter).
        foreach (var recipe in ForgeRecipe.All)
        {
            var g = new ForgeMinigame(Setup(recipe, 50), 10);
            double strikeSeconds = g.TrackEnd + ForgeTuning.LeadInBeats * g.BeatInterval;
            double heatSeconds = recipe.TargetHeat / ForgeTuning.HeatRatePerSecond;
            Assert.InRange(strikeSeconds + heatSeconds, 10, 90);
        }
    }
}
