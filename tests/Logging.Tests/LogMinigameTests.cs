using System;
using System.Collections.Generic;
using System.Linq;
using Mmo.Crafting.Logging;
using Xunit;

public class LogMinigameTests
{
    private static LogSetup Setup(StandType stand, int skill = 50, int grade = 80, Axe axe = null) => new LogSetup(stand)
    {
        SkillLevel = skill,
        StandGrade = grade,
        Axe = axe ?? Axe.SteelAxe,
    };

    /// <summary>
    /// A logger who knows the woods (reads the hidden truth directly): picks a sound tree with a clear line to fall
    /// along, cuts a proper notch and a stepped back cut, wedges a back-leaner early, bucks around defects from the
    /// right side, and stacks and peels the clean lengths.
    /// </summary>
    public static LogMinigame PlayExpert(LogSetup setup, int seed, bool peel = true)
    {
        var g = new LogMinigame(setup, seed);
        var plan = g.Trees
            .Where(t => t.Rot == RotKind.None)
            .SelectMany(t => Enumerable.Range(0, 8).Select(d => (t, d)))
            .Where(x => g.CheckLine(x.t, x.d).Clear && g.Forward(x.t, x.d) > -2)
            .OrderByDescending(x => LogMinigame.BestTimber(x.t) * x.t.Grade)
            .ThenByDescending(x => g.Forward(x.t, x.d) >= 0 ? 1 : 0)
            .ThenByDescending(x => x.t.Knots.Sum() == 0 ? 0 : -x.t.Knots.Sum())
            .Select(x => ((Tree, int)?)x)
            .FirstOrDefault()
            ?? g.Trees.SelectMany(t => Enumerable.Range(0, 8).Select(d => (t, d)))
                .OrderBy(x => g.CheckLine(x.t, x.d).HungUp ? 1 : 0).ThenBy(x => x.t.Rot == RotKind.None ? 0 : 1)
                .Select(x => ((Tree, int)?)x).First();
        var (tree, dir) = plan.Value;

        g.Select(tree.Id);
        g.ChooseTree();
        g.SetAim(dir);
        g.ConfirmAim();
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        bool backLeaner = g.Forward(tree, dir) < 0;
        while (g.Stage == LogStage.BackCut && g.Hinge > 2)
        {
            if (backLeaner && g.Wedges == 0 && g.BackDepth >= LogTuning.WedgeRoom) g.DriveWedge();
            else g.Saw();
        }
        while (g.Stage == LogStage.BackCut && g.Wedges < LogTuning.MaxWedges) g.DriveWedge();
        while (g.Stage == LogStage.BackCut) g.Saw();
        Assert.Equal(LogStage.Buck, g.Stage);

        // Buck: clean runs into 4 m lengths, defects cut out.
        int n = g.Stem.Count;
        var cuts = new List<int>();
        int runStart = 0;
        for (int i = 0; i <= n; i++)
        {
            bool bad = i == n || g.Stem[i].Defect;
            if (!bad) continue;
            int len = i - runStart;
            for (int k = runStart + LogTuning.TimberLength; k <= runStart + len / LogTuning.TimberLength * LogTuning.TimberLength; k += LogTuning.TimberLength)
                cuts.Add(k);
            if (len >= LogTuning.TimberLength) cuts.Add(runStart);
            if (i < n) { cuts.Add(i); cuts.Add(i + 1); }
            runStart = i + 1;
        }
        foreach (int k in cuts.Where(k => k >= 1 && k < n).Distinct().OrderBy(k => k))
            g.Buck(k, g.RightSide(k));
        g.FinishBucking();

        for (int i = 0; i < g.Pieces.Count; i++)
        {
            var p = g.Pieces[i];
            if (!p.IsCrown && p.Length >= LogTuning.TimberLength && !g.HasDefect(p))
                g.SetUse(i, peel ? PieceUse.PeeledTimber : PieceUse.Timber);
        }
        for (int i = 0; i < g.Pieces.Count; i++)
            if (g.Pieces[i].Use == PieceUse.Leave) g.SetUse(i, PieceUse.Firewood);
        g.FinishYard();
        Assert.Equal(LogStage.Done, g.Stage);
        return g;
    }

    /// <summary>The nearest tree, felled toward the north with a shallow notch, sawn level until it drops; bucked every 4 m from the top.</summary>
    public static LogMinigame PlayHasty(LogSetup setup, int seed)
    {
        var g = new LogMinigame(setup, seed);
        g.Select(0);
        g.ChooseTree();
        g.SetAim(0);
        g.ConfirmAim();
        for (int i = 0; i < 3; i++) g.Chop();
        g.FinishNotch();
        while (g.Stage == LogStage.BackCut && g.Strength > 0) { double s = g.Strength; g.Saw(); if (g.Strength == s) break; }
        if (g.Stage != LogStage.Buck) { g.WalkAway(); return g; }
        for (int k = LogTuning.TimberLength; k < g.Stem.Count; k += LogTuning.TimberLength) g.Buck(k, BuckSide.Top);
        g.FinishBucking();
        for (int i = 0; i < g.Pieces.Count; i++) g.SetUse(i, PieceUse.Timber);
        g.FinishYard();
        return g;
    }

    [Theory]
    [InlineData("pine_stand")]
    [InlineData("birch_copse")]
    [InlineData("oak_grove")]
    public void ALoggerWhoKnowsTheWoodsDoesWell(string id)
    {
        var stand = StandType.All.Single(s => s.Id == id);
        var results = Enumerable.Range(0, 200).Select(s => PlayExpert(Setup(stand, 100), s).Result).ToList();
        Assert.All(results, r => Assert.True(r.Felled));
        double onLine = results.Average(r => r.OnLine ? 1.0 : 0);
        double yield = results.Average(r => r.YieldScore);
        double q = results.Where(r => r.Timber > 0).Average(r => r.Quality);
        double strength = results.Average(r => r.StrengthLeft);
        double clear = results.Average(r => r.CrackedMetres == 0 && !r.HungUp ? 1.0 : 0);
        Console.WriteLine($"{id}: on line {onLine:P0} clear {clear:P0} yield {yield:0.00} Q {q:0} timber {results.Average(r => r.Timber):0.0} bark {results.Average(r => r.Bark):0.0} fire {results.Average(r => r.Firewood):0.0} strength left {strength:0}");
        Assert.True(onLine > 0.95, $"on line {onLine}");
        Assert.True(clear > 0.9, $"clear {clear}");
        Assert.True(yield > 0.9, $"yield {yield}");
        Assert.True(q >= 70, $"Q {q}");
        Assert.All(results, r => Assert.Equal(1.0, r.Purity));
        Assert.True(results.Count(r => r.CloseCalls > 0) <= results.Count / 10);
    }

    [Fact]
    public void TheExpertAlmostAlwaysHasStrengthToFinish()
    {
        foreach (var stand in StandType.All)
        {
            var results = Enumerable.Range(0, 200).Select(s => PlayExpert(Setup(stand, 100), s).Result).ToList();
            // Peeling every clean length should be affordable on most trees.
            Assert.True(results.Count(r => r.Bark == r.Timber * stand.BarkPerLog) >= 170, stand.Id);
        }
    }

    [Fact]
    public void HastyLoggingIsWorseThanKnowingTheWoods()
    {
        double hasty = 0, expert = 0;
        int hastyCalls = 0;
        for (int s = 0; s < 100; s++)
        {
            var h = PlayHasty(Setup(StandType.PineStand), s).Result;
            hasty += h.Timber * (h.Quality / 100.0);
            hastyCalls += h.CloseCalls;
            var e = PlayExpert(Setup(StandType.PineStand), s).Result;
            expert += e.Timber * (e.Quality / 100.0);
        }
        Assert.True(hasty < expert * 0.6, $"hasty {hasty} expert {expert}");
        Assert.True(hastyCalls > 30, $"close calls {hastyCalls}");
    }

    [Fact]
    public void BetterAxesRaiseQualityNotYield()
    {
        for (int s = 0; s < 40; s++)
        {
            var worn = PlayExpert(Setup(StandType.OakGrove, 100, 95, Axe.WornAxe), s).Result;
            var steel = PlayExpert(Setup(StandType.OakGrove, 100, 95, Axe.SteelAxe), s).Result;
            Assert.Equal(worn.Timber, steel.Timber);
            Assert.Equal(worn.Bark, steel.Bark);
            Assert.Equal(worn.Firewood, steel.Firewood);
            Assert.True(worn.Quality <= 60);
            Assert.True(steel.Quality >= worn.Quality);
        }
    }

    [Fact]
    public void SameSeedSameStand()
    {
        var a = new LogMinigame(Setup(StandType.PineStand), 42);
        var b = new LogMinigame(Setup(StandType.PineStand), 42);
        Assert.Equal(a.Trees.Select(t => (t.X, t.Y, t.Height, t.Rot, t.Grade)), b.Trees.Select(t => (t.X, t.Y, t.Height, t.Rot, t.Grade)));
        Assert.Equal(a.WindDir, b.WindDir);
        Assert.Equal(PlayExpert(Setup(StandType.PineStand), 42).Result.Quality, PlayExpert(Setup(StandType.PineStand), 42).Result.Quality);
    }

    [Fact]
    public void TheStandHasFiveSpacedTreesAndSomeRocks()
    {
        for (int s = 0; s < 200; s++)
        {
            var g = new LogMinigame(Setup(StandType.OakGrove), s);
            Assert.Equal(LogTuning.Trees, g.Trees.Count);
            Assert.True(g.Rocks.Count >= 2);
            foreach (var a in g.Trees)
                foreach (var b in g.Trees)
                    if (a != b) Assert.True(Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) >= LogTuning.TreeSpacing);
            // Every stand has at least one sound tree with a clear line.
        }
        int good = Enumerable.Range(0, 200).Count(s =>
        {
            var g = new LogMinigame(Setup(StandType.PineStand), s);
            return g.Trees.Any(t => t.Rot == RotKind.None && Enumerable.Range(0, 8).Any(d => g.CheckLine(t, d).Clear));
        });
        Assert.True(good >= 190, $"stands with a good tree and a clear line: {good}/200");
    }

    [Fact]
    public void KnockingTellsRotAndIsLimitedBySkill()
    {
        Assert.Equal(4, new LogMinigame(Setup(StandType.PineStand, 1), 1).TapsLeft);
        Assert.Equal(8, new LogMinigame(Setup(StandType.PineStand, 100), 1).TapsLeft);
        for (int s = 0; s < 100; s++)
        {
            var g = new LogMinigame(Setup(StandType.BirchCopse, 100), s);
            var rotten = g.Trees.FirstOrDefault(t => t.Rot != RotKind.None);
            var sound = g.Trees.FirstOrDefault(t => t.Rot == RotKind.None);
            if (rotten == null || sound == null) continue;
            Assert.Equal(rotten.Rot == RotKind.Hollow ? TrunkSound.Boom : TrunkSound.Dull, g.Tap(rotten.Id));
            Assert.Equal(TrunkSound.Ring, g.Tap(sound.Id));
            Assert.Equal(TrunkSound.Ring, g.Tap(sound.Id)); // again costs nothing
            Assert.Equal(6, g.TapsLeft);
            return;
        }
        Assert.Fail("no stand with both");
    }

    [Fact]
    public void SkilledEyesSightHeightTruer()
    {
        double Err(int skill) => Enumerable.Range(0, 100).SelectMany(s =>
        {
            var g = new LogMinigame(Setup(StandType.PineStand, skill), s);
            return g.Trees.Select(t => Math.Abs(g.SightReach(t.Id) / t.Height - 1));
        }).Average();
        Assert.True(Err(100) < Err(1) / 3, $"{Err(100)} vs {Err(1)}");
    }

    [Fact]
    public void RotSignsShowMoreOftenOnRottenTrees()
    {
        int rotWith = 0, rotAll = 0, soundWith = 0, soundAll = 0;
        for (int s = 0; s < 300; s++)
            foreach (var t in new LogMinigame(Setup(StandType.OakGrove, 50), s).Trees)
            {
                bool sign = t.Signs.Any(LogTuning.MeansRot);
                if (t.Rot != RotKind.None) { rotAll++; if (sign) rotWith++; }
                else { soundAll++; if (sign) soundWith++; }
            }
        Assert.True(rotWith / (double)rotAll > 0.6);
        Assert.True(soundWith / (double)soundAll < 0.2);
    }

    /// <summary>Finds a stand and a sound tree with a clear line in some direction where the pull is as wanted.</summary>
    private static (LogMinigame, Tree, int) Find(Func<LogMinigame, Tree, int, bool> want, StandType stand = null)
    {
        for (int s = 0; s < 2000; s++)
        {
            var g = new LogMinigame(Setup(stand ?? StandType.PineStand), s);
            foreach (var t in g.Trees.Where(t => t.Rot == RotKind.None))
                for (int d = 0; d < 8; d++)
                    if (g.CheckLine(t, d).Clear && want(g, t, d))
                    {
                        g.Select(t.Id);
                        g.ChooseTree();
                        g.SetAim(d);
                        g.ConfirmAim();
                        return (g, t, d);
                    }
        }
        throw new Exception("none found");
    }

    [Fact]
    public void AGoodHingeHoldsTheLineAgainstASidePull()
    {
        var (g, t, d) = Find((g, t, d) => g.PullStrength(t) >= 1.5 && LogTuning.DirDiff(g.PullDir(t), d) == 2);
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        while (g.Stage == LogStage.BackCut && g.Hinge > 2) g.Saw();
        while (g.Stage == LogStage.BackCut) g.DriveWedge();
        Assert.Equal(d, g.FellDir);
    }

    [Fact]
    public void AThinHingeLetsItSwingTowardItsPull()
    {
        var (g, t, d) = Find((g, t, d) => g.PullStrength(t) >= 1.5 && LogTuning.DirDiff(g.PullDir(t), d) == 2 && g.Forward(t, d) > -0.5);
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        while (g.Stage == LogStage.BackCut && g.Hinge > 1) g.Saw();
        while (g.Stage == LogStage.BackCut) g.DriveWedge();
        Assert.Equal(LogTuning.StepToward(d, g.PullDir(t)), g.FellDir);
    }

    [Fact]
    public void SawingThroughTheHingeLetsItGoItsOwnWay()
    {
        var (g, t, d) = Find((g, t, d) => g.PullStrength(t) >= 1 && g.Forward(t, d) < -0.5);
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        while (g.Stage == LogStage.BackCut)
        {
            if (g.Pinched) g.DriveWedge(); else g.Saw();
        }
        Assert.True(g.Result == null);
        Assert.Equal(g.PullDir(t), g.FellDir);
    }

    [Fact]
    public void ATreeLeaningBackSitsOnTheSawUnlessWedgedEarly()
    {
        var (g, t, d) = Find((g, t, d) => g.Forward(t, d) <= -1.5);
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        for (int i = 0; i < LogTuning.SitBackDepth; i++) g.Saw();
        Assert.True(g.Pinched);
        double before = g.Strength;
        g.Saw();
        Assert.Equal(before - 2 * LogTuning.StepCost(t.Diameter), g.Strength, 6);
        g.DriveWedge();
        Assert.False(g.Pinched);

        (g, t, d) = Find((g, t, d) => g.Forward(t, d) <= -1.5);
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        for (int i = 0; i < LogTuning.WedgeRoom; i++) g.Saw();
        g.DriveWedge();
        while (g.Hinge > 2) g.Saw();
        Assert.False(g.Pinched);
        while (g.Stage == LogStage.BackCut) g.DriveWedge();
        Assert.Equal(d, g.FellDir);
    }

    [Fact]
    public void BackCutBelowTheNotchKicksBackAndSplitsTheButt()
    {
        var (g, t, d) = Find((g, t, d) => g.Forward(t, d) >= 0.5);
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Below);
        while (g.Stage == LogStage.BackCut && g.Hinge > 2) g.Saw();
        while (g.Stage == LogStage.BackCut) g.DriveWedge();
        Assert.True(g.Stem[0].Cracked);
    }

    [Fact]
    public void AShallowNotchOnAHardForwardLeanerBarberChairs()
    {
        var (g, t, d) = Find((g, t, d) => g.Forward(t, d) >= 2.2);
        for (int i = 0; i < 3; i++) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        while (g.Stage == LogStage.BackCut) g.Saw();
        Assert.True(g.Stem[0].Cracked && g.Stem[1].Cracked && g.Stem[2].Cracked);
    }

    [Fact]
    public void FallingIntoANeighbourHangsItUp()
    {
        for (int s = 0; s < 500; s++)
        {
            var g = new LogMinigame(Setup(StandType.PineStand), s);
            foreach (var t in g.Trees)
                for (int d = 0; d < 8; d++)
                {
                    var line = g.CheckLine(t, d);
                    if (!line.Caught || g.Forward(t, d) < 0) continue;
                    g.Select(t.Id);
                    g.ChooseTree();
                    g.SetAim(d);
                    g.ConfirmAim();
                    while (g.NotchDepth < 6) g.Chop();
                    g.FinishNotch();
                    g.SetBackHeight(BackCutHeight.Step);
                    while (g.Stage == LogStage.BackCut && g.Hinge > 2) g.Saw();
                    while (g.Stage == LogStage.BackCut) g.DriveWedge();
                    if (g.FellDir != d) goto next;
                    Assert.True(g.HungUp);
                    Assert.True(g.Stem[^1].Cracked);
                    return;
                }
            next:;
        }
        Assert.Fail("no hang-up found");
    }

    /// <summary>Fells a pine cleanly and returns the game at the Buck stage.</summary>
    private static LogMinigame Felled(int seed)
    {
        var g = new LogMinigame(Setup(StandType.PineStand), seed);
        var choice = g.Trees.SelectMany(t => Enumerable.Range(0, 8).Select(d => (t, d)))
            .FirstOrDefault(x => x.t.Rot == RotKind.None && g.CheckLine(x.t, x.d).Clear && g.Forward(x.t, x.d) >= 0);
        if (choice.t == null) return null;
        g.Select(choice.t.Id);
        g.ChooseTree();
        g.SetAim(choice.d);
        g.ConfirmAim();
        while (g.NotchDepth < 6) g.Chop();
        g.FinishNotch();
        g.SetBackHeight(BackCutHeight.Step);
        while (g.Stage == LogStage.BackCut && g.Hinge > 2) g.Saw();
        while (g.Stage == LogStage.BackCut) g.DriveWedge();
        return g;
    }

    [Fact]
    public void BuckingFromTheWrongSidePinchesAndTears()
    {
        bool sag = false, hang = false, dirt = false;
        for (int s = 0; s < 300 && !(sag && hang && dirt); s++)
        {
            var g = Felled(s);
            if (g == null) continue;
            for (int k = 1; k < g.Stem.Count; k++)
            {
                var rightSide = g.RightSide(k);
                bool ground = g.Stem[k - 1].Resting && g.Stem[k].Resting;
                double before = g.Strength;
                if (ground && !dirt)
                {
                    g.Buck(k, BuckSide.Under);
                    Assert.DoesNotContain(k, g.Cuts);
                    Assert.True(g.Strength < before);
                    dirt = true;
                }
                else if (!ground && rightSide == BuckSide.Under && !sag)
                {
                    g.Buck(k, BuckSide.Top);
                    Assert.True(g.Stem[k].SplitEnd);
                    sag = true;
                }
                else if (!ground && rightSide == BuckSide.Top && !hang)
                {
                    g.Buck(k, BuckSide.Under);
                    Assert.True(g.Stem[k].SplitEnd || g.Stem[k - 1].SplitEnd);
                    hang = true;
                }
            }
        }
        Assert.True(sag && hang && dirt, $"sag {sag} hang {hang} dirt {dirt}");
    }

    [Fact]
    public void ShortLengthsAndTheCrownCannotBeTimber()
    {
        var g = Felled(3) ?? Felled(4);
        g.Buck(2, g.RightSide(2));
        g.FinishBucking();
        g.SetUse(0, PieceUse.Timber);
        Assert.Equal(PieceUse.Leave, g.Pieces[0].Use);
        int crown = g.Pieces.Count - 1;
        Assert.True(g.Pieces[crown].IsCrown);
        g.SetUse(crown, PieceUse.Timber);
        Assert.Equal(PieceUse.Leave, g.Pieces[crown].Use);
        g.CycleUse(crown);
        Assert.Equal(PieceUse.Firewood, g.Pieces[crown].Use);
        g.CycleUse(1);
        Assert.Equal(PieceUse.Timber, g.Pieces[1].Use);
        g.CycleUse(1);
        Assert.Equal(PieceUse.PeeledTimber, g.Pieces[1].Use);
    }

    [Fact]
    public void StackingRottenOrCrookedLengthsAsTimberLowersPurity()
    {
        for (int s = 0; s < 500; s++)
        {
            var g = new LogMinigame(Setup(StandType.BirchCopse), s);
            var t = g.Trees.FirstOrDefault(x => x.Rot != RotKind.None && x.RotLength >= 2);
            if (t == null) continue;
            int d = Enumerable.Range(0, 8).FirstOrDefault(d => g.CheckLine(t, d).Clear && g.Forward(t, d) >= 0, -1);
            if (d < 0) continue;
            g.Select(t.Id); g.ChooseTree(); g.SetAim(d); g.ConfirmAim();
            while (g.NotchDepth < 6) g.Chop();
            g.FinishNotch();
            g.SetBackHeight(BackCutHeight.Step);
            while (g.Stage == LogStage.BackCut && g.Hinge > 2) g.Saw();
            while (g.Stage == LogStage.BackCut) g.DriveWedge();
            for (int k = 4; k < g.Stem.Count; k += 4) g.Buck(k, g.RightSide(k));
            g.FinishBucking();
            for (int i = 0; i < g.Pieces.Count; i++) g.SetUse(i, PieceUse.Timber);
            g.FinishYard();
            Assert.True(g.Result.TimberCulled >= 1);
            Assert.True(g.Result.Purity < 1);
            Assert.NotEmpty(g.Result.CullNotes);
            return;
        }
        Assert.Fail("no rotten tree");
    }

    [Fact]
    public void WalkingAwayGivesNothing()
    {
        var g = new LogMinigame(Setup(StandType.PineStand), 1);
        g.ChooseTree();
        g.ConfirmAim();
        g.Chop();
        g.WalkAway();
        Assert.Equal(LogStage.Done, g.Stage);
        Assert.False(g.Result.Felled);
        Assert.Equal(0, g.Result.Timber);
        Assert.Equal(0, g.Result.SkillGain);
    }

    [Fact]
    public void SkillRaisesTheCapButNotStrength()
    {
        Assert.Equal(LogTuning.Strength, new LogMinigame(Setup(StandType.PineStand, 1), 1).Strength);
        Assert.Equal(LogTuning.Strength, new LogMinigame(Setup(StandType.PineStand, 100), 1).Strength);
        Assert.True(LogTuning.SkillCap(1) < LogTuning.SkillCap(100));
        var low = PlayExpert(Setup(StandType.PineStand, 1, 95), 7).Result;
        Assert.True(low.Quality <= LogTuning.SkillCap(1));
    }
}
