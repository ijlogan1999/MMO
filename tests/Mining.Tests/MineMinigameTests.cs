using System;
using System.Linq;
using Mmo.Crafting.Mining;
using Xunit;

public class MineMinigameTests
{
    private const double Dt = 1.0 / 60.0;

    /// <summary>
    /// Plays a whole node. markOffset moves the mark away from the seam (in seam half-widths).
    /// reactAfter = seconds after a crack opens before the bot strikes; aimError = how far off the point it aims.
    /// strikeEvery = only strike every n-th point (others are left to close). wildSwings = extra blows between points.
    /// </summary>
    private static (MineMinigame Game, MineResult Result) Play(MineSetup setup, int seed, double markOffset = 0,
        double reactAfter = 0.2, double aimError = 0, int strikeEvery = 1, int wildSwings = 0, int stepBackAfter = -1)
    {
        var g = new MineMinigame(setup, seed);
        // Mark on (or beside) the seam's centre, at the middle of the face.
        double ax = -g.Seam.DirY, ay = g.Seam.DirX;
        double t = 0.5 - g.Seam.X;
        double mx = 0.5, my = g.Seam.Y + g.Seam.DirY / g.Seam.DirX * t;
        mx += ax * markOffset * g.Seam.HalfWidth;
        my += ay * markOffset * g.Seam.HalfWidth;
        g.Mark(Math.Clamp(mx, 0, 1), Math.Clamp(my, 0, MineTuning.FaceHeight));
        Assert.Equal(MineStage.Strike, g.Stage);

        int wild = wildSwings;
        for (int frame = 0; frame < 60 * 600 && g.Stage != MineStage.Done; frame++)
        {
            g.Update(Dt);
            if (g.Stage == MineStage.Done) break;
            if (stepBackAfter >= 0 && g.Current >= stepBackAfter) { g.StepBack(); break; }
            var p = g.CurrentPoint;
            if (p.State == PointState.Waiting && wild > 0 && frame % 30 == 0)
            {
                wild--;
                g.Swing(0.5, 0.3);
                continue;
            }
            if (p.State == PointState.Showing && g.OpenFor >= reactAfter && g.Current % strikeEvery == 0)
                g.Swing(p.X + (p.X > 0.5 ? -aimError : aimError), p.Y);
            g.Events.Clear();
        }
        Assert.Equal(MineStage.Done, g.Stage);
        return (g, g.Result);
    }

    private static MineSetup Setup(OreNodeType node, int skill, int grade = 80, int size = 10, Pick pick = null) => new MineSetup(node)
    {
        SkillLevel = skill,
        VeinGrade = grade,
        NodeSize = size,
        Pick = pick ?? Pick.SteelPick,
    };

    [Theory]
    [InlineData("iron_seam")]
    [InlineData("coal_seam")]
    [InlineData("copper_seam")]
    public void PerfectPlayOnTheSeamTakesTheWholeNode(string id)
    {
        var node = OreNodeType.All.Single(n => n.Id == id);
        for (int seed = 0; seed < 20; seed++)
        {
            var (g, r) = Play(Setup(node, 100, grade: 80), seed);
            Assert.Equal(10, r.Clean);
            Assert.Equal(0, r.Fractures);
            Assert.Equal(1.0, r.BreakScore, 6);
            Assert.True(r.Read > 0.75, $"seed {seed} read {r.Read}");
            Assert.True(r.Yield >= 8, $"seed {seed} yield {r.Yield}");
            Assert.Equal(80, r.Quality);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void OneWeakPointPerSwingAllInsideTheFace(int size)
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var g = new MineMinigame(Setup(OreNodeType.CoalSeam, 50, size: size), seed);
            g.Mark(seed % 2 == 0 ? 0.02 : 0.98, 0.3); // even from the very edge
            Assert.Equal(size, g.Points.Count);
            Assert.All(g.Points, p => Assert.True(MineMinigame.InsideFace(p.X, p.Y)));
        }
    }

    [Fact]
    public void MarkingBesideTheSeamSplitsBarrenRock()
    {
        double on = 0, off = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            on += Play(Setup(OreNodeType.IronSeam, 50), seed).Result.Yield;
            off += Play(Setup(OreNodeType.IronSeam, 50), seed, markOffset: 4).Result.Yield;
        }
        Assert.True(off < on * 0.5, $"on {on} off {off}");
    }

    [Fact]
    public void BlowsOffTheWeakPointCrushOreAndLowerQuality()
    {
        var clean = Play(Setup(OreNodeType.CoalSeam, 100), 3).Result;
        var messy = Play(Setup(OreNodeType.CoalSeam, 100), 3, wildSwings: 3).Result;
        Assert.Equal(3, messy.Fractures);
        Assert.Equal(3, messy.Crushed);             // each fracture used up a chunk
        Assert.True(messy.Quality < clean.Quality, $"{messy.Quality} vs {clean.Quality}");
        Assert.True(messy.Yield < clean.Yield);
    }

    [Fact]
    public void MissingTheCrackCostsYieldNotQuality()
    {
        var all = Play(Setup(OreNodeType.CoalSeam, 50), 4).Result;
        var half = Play(Setup(OreNodeType.CoalSeam, 50), 4, strikeEvery: 2).Result;
        Assert.Equal(5, half.Missed);
        Assert.Equal(all.Quality, half.Quality);
        Assert.True(half.Yield < all.Yield);
    }

    [Fact]
    public void AimingWideOfTheCrackIsAFracture()
    {
        var r = Play(Setup(OreNodeType.CoalSeam, 50), 5, aimError: 0.2).Result;
        Assert.Equal(0, r.Clean);
        Assert.Equal(10, r.Fractures);
        Assert.Equal(0, r.Yield);
        Assert.Equal("Only rubble", r.ItemName);
        Assert.Equal(0, r.SkillGain);
    }

    [Fact]
    public void SkillAndPickKeepTheCrackOpenLonger()
    {
        Assert.Equal(0.6, MineTuning.OpenSeconds(1, Pick.IronPick, OreNodeType.CopperSeam), 6);
        Assert.Equal(1.5, MineTuning.OpenSeconds(100, Pick.IronPick, OreNodeType.CopperSeam), 6);
        Assert.True(MineTuning.OpenSeconds(50, Pick.SteelPick, OreNodeType.IronSeam) > MineTuning.OpenSeconds(50, Pick.WornPick, OreNodeType.IronSeam));
        Assert.True(MineTuning.HitRadius(100, Pick.IronPick) > MineTuning.HitRadius(1, Pick.IronPick));

        // Reacting after 0.8 s: a novice's crack has closed, a master's has not.
        var novice = Play(Setup(OreNodeType.CopperSeam, 1, pick: Pick.IronPick), 6, reactAfter: 0.8).Result;
        var master = Play(Setup(OreNodeType.CopperSeam, 100, pick: Pick.IronPick), 6, reactAfter: 0.8).Result;
        Assert.Equal(10, novice.Missed);
        Assert.Equal(10, master.Clean);
    }

    [Fact]
    public void CeilingIsTheLowerOfPickAndSkill()
    {
        var r = Play(Setup(OreNodeType.IronSeam, 100, grade: 100, pick: Pick.WornPick), 7).Result;
        Assert.Equal(60, r.Ceiling);
        Assert.StartsWith("Tool", r.CeilingReason);
        Assert.Equal(60, r.Quality);

        r = Play(Setup(OreNodeType.IronSeam, 10, grade: 100), 7).Result;
        Assert.Equal(MineTuning.SkillCap(10), r.Ceiling);
        Assert.StartsWith("Skill", r.CeilingReason);
    }

    [Fact]
    public void VeinGradeSetsQualityPerSpec()
    {
        // quality = vein grade x (0.5 + 0.5 x break score)
        var r = Play(Setup(OreNodeType.CoalSeam, 100, grade: 70), 8, wildSwings: 2).Result;
        double expected = 70 * (0.5 + 0.5 * r.Clean / (double)(r.Clean + r.Fractures));
        Assert.Equal((int)Math.Round(expected), r.Quality);
    }

    [Fact]
    public void SteppingBackLeavesOreInTheNode()
    {
        var r = Play(Setup(OreNodeType.CoalSeam, 50), 9, stepBackAfter: 4).Result;
        Assert.Equal(4, r.Clean);
        Assert.Equal(6, r.Left);
        Assert.True(r.Yield <= 4);
    }

    [Fact]
    public void SwingsHaveARecovery()
    {
        var g = new MineMinigame(Setup(OreNodeType.CoalSeam, 50), 10);
        g.Mark(0.5, 0.3);
        g.Swing(0.1, 0.1);
        g.Swing(0.1, 0.1); // too soon, ignored
        Assert.Equal(1, g.Fractures);
    }

    [Fact]
    public void SameSeedSameRock()
    {
        var a = new MineMinigame(Setup(OreNodeType.IronSeam, 30), 42);
        var b = new MineMinigame(Setup(OreNodeType.IronSeam, 30), 42);
        Assert.Equal(a.Seam.Angle, b.Seam.Angle);
        a.Mark(0.4, 0.3); b.Mark(0.4, 0.3);
        Assert.Equal(a.Points.Select(p => (p.X, p.Y)), b.Points.Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void SkillStopsGrowingOnEasyNodes()
    {
        Assert.True(Play(Setup(OreNodeType.CoalSeam, 10), 11).Result.SkillGain > 0);
        Assert.Equal(0, Play(Setup(OreNodeType.CoalSeam, 40), 11).Result.SkillGain);
        Assert.True(Play(Setup(OreNodeType.CopperSeam, 40), 11).Result.SkillGain > 0);
    }
}
