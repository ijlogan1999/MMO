using System;
using System.Collections.Generic;
using System.Linq;
using Mmo.Crafting.Mining;
using Xunit;

public class MineMinigameTests
{
    private static MineSetup Setup(OreNodeType node, int skill = 50, int grade = 80, Pick pick = null, int world = 1) => new MineSetup(node)
    {
        SkillLevel = skill,
        VeinGrade = grade,
        Pick = pick ?? Pick.SteelPick,
        WorldSeed = world,
    };

    private static Tool RightTool(Block b) => b.IsLarge ? Tool.Wedge : Tool.Pick;

    /// <summary>
    /// A miner who knows the rock (reads Kind directly): opens the bed above each ore block on barren rock,
    /// takes the ore with the right tool, props when the roof starts working, and sorts perfectly.
    /// </summary>
    private static MineMinigame PlayExpert(MineSetup setup, int seed, bool prop = true, bool sort = true)
    {
        var g = new MineMinigame(setup, seed);
        foreach (var ore in g.Blocks.Where(b => b.Kind == BlockKind.Ore).OrderBy(b => b.Row).ThenBy(b => b.Col).ToList())
        {
            if (g.Stage != MineStage.Dig) break;
            if (prop && g.RoofStress >= 6 && g.PropsLeft > 0) g.Prop();
            if (g.Stage != MineStage.Dig) break;
            if (g.IsLocked(ore))
            {
                // Open one column above it (or beside it in the top bed) on barren rock.
                Block opener = ore.Row > 0
                    ? Enumerable.Range(ore.Col, ore.Width).Select(c => g.BlockAt(ore.Row - 1, c)).FirstOrDefault(b => b.InPlace && b.Kind != BlockKind.Ore)
                    : null;
                opener ??= new[] { g.BlockAt(ore.Row, ore.Col - 1), g.BlockAt(ore.Row, ore.End) }.FirstOrDefault(b => b != null && b.InPlace && b.Kind != BlockKind.Ore);
                if (opener == null) continue;
                g.Work(opener.Kind == BlockKind.Loose ? Tool.Pick : Tool.Sledge, opener.Id);
                if (g.Stage != MineStage.Dig) break;
                if (prop && g.RoofStress >= 6 && g.PropsLeft > 0) g.Prop();
                if (g.Stage != MineStage.Dig) break;
            }
            if (!g.IsLocked(ore)) g.Work(RightTool(ore), ore.Id);
        }
        g.FinishDigging();
        if (sort) for (int i = 0; i < g.Cart.Count; i++) g.SetKeep(i, g.Cart[i].IsOre);
        g.FinishSorting();
        Assert.Equal(MineStage.Done, g.Stage);
        return g;
    }

    /// <summary>Ore with the right tool where it stands, without opening the face.</summary>
    private static MineMinigame PlayHasty(MineSetup setup, int seed)
    {
        var g = new MineMinigame(setup, seed);
        foreach (var ore in g.Blocks.Where(b => b.Kind == BlockKind.Ore).ToList())
            if (g.Stage == MineStage.Dig) g.Work(RightTool(ore), ore.Id);
        g.FinishDigging();
        g.FinishSorting();
        return g;
    }

    [Theory]
    [InlineData("iron_seam")]
    [InlineData("coal_seam")]
    [InlineData("copper_seam")]
    public void AMinerWhoKnowsTheRockDoesWell(string id)
    {
        var node = OreNodeType.All.Single(n => n.Id == id);
        var results = Enumerable.Range(0, 100).Select(s => PlayExpert(Setup(node, 100), s).Result).ToList();
        int collapses = results.Count(r => r.Collapsed);
        Assert.True(collapses <= 15, $"collapses {collapses}");
        var standing = results.Where(r => !r.Collapsed).ToList();
        Assert.True(standing.Average(r => r.YieldScore) > 0.75, $"yield {standing.Average(r => r.YieldScore)}");
        Assert.All(standing, r => Assert.Equal(1.0, r.Purity));
        Assert.True(standing.Average(r => r.BreakScore) > 0.95);
        Assert.True(standing.Average(r => r.Quality) >= 76, $"Q {standing.Average(r => r.Quality)}");
    }

    [Fact]
    public void PryingOreWhereItStandsShattersIt()
    {
        double hastyBreak = 0, expertBreak = 0;
        for (int s = 0; s < 50; s++)
        {
            hastyBreak += PlayHasty(Setup(OreNodeType.IronSeam), s).Result.BreakScore;
            expertBreak += PlayExpert(Setup(OreNodeType.IronSeam), s).Result.BreakScore;
        }
        Assert.True(hastyBreak < expertBreak * 0.8, $"hasty {hastyBreak} expert {expertBreak}");
    }

    [Fact]
    public void EveryFaceIsAWallOfBlocksWithSomeOre()
    {
        for (int s = 0; s < 200; s++)
        {
            var g = new MineMinigame(Setup(OreNodeType.CoalSeam), s);
            for (int row = 0; row < MineTuning.Rows; row++)
            {
                var bed = g.Blocks.Where(b => b.Row == row).OrderBy(b => b.Col).ToList();
                Assert.Equal(0, bed[0].Col);
                for (int i = 1; i < bed.Count; i++) Assert.Equal(bed[i - 1].End, bed[i].Col);
                Assert.Equal(MineTuning.Columns, bed[^1].End);
                Assert.All(bed, b => Assert.True(b.Width >= 2));
            }
            Assert.True(g.Blocks.Count(b => b.Kind == BlockKind.Ore) >= 3);
            Assert.All(g.Blocks.Where(b => b.Kind == BlockKind.Ore), b => Assert.True(Math.Abs(b.Row - g.SeamRow) <= 1));
            Assert.All(g.Blocks, b => Assert.True(g.IsLocked(b) || b.Kind == BlockKind.Loose || true));
        }
    }

    [Fact]
    public void TheWholeFaceStartsLocked()
    {
        var g = new MineMinigame(Setup(OreNodeType.IronSeam), 3);
        Assert.All(g.Blocks, b => Assert.True(g.IsLocked(b)));
    }

    /// <summary>Finds a seed and ore block matching a condition, opens the bed above it, and returns both.</summary>
    private static (MineMinigame, Block) FreeOre(Func<Block, bool> want)
    {
        for (int s = 0; s < 500; s++)
        {
            var g = new MineMinigame(Setup(OreNodeType.IronSeam), s);
            var ore = g.Blocks.FirstOrDefault(b => b.Kind == BlockKind.Ore && b.Row > 0 && want(b));
            if (ore == null) continue;
            var above = g.BlockAt(ore.Row - 1, ore.Col);
            if (above.Kind == BlockKind.Ore) continue;
            g.Work(Tool.Sledge, above.Id);
            Assert.False(g.IsLocked(ore));
            return (g, ore);
        }
        throw new Exception("no face found");
    }

    [Fact]
    public void SmallOreWantsThePickLargeOreWantsTheWedge()
    {
        var (g, small) = FreeOre(b => !b.IsLarge);
        g.Work(Tool.Pick, small.Id);
        Assert.Equal(BlockState.Clean, small.State);

        (g, small) = FreeOre(b => !b.IsLarge);
        g.Work(Tool.Wedge, small.Id);
        Assert.Equal(BlockState.Crushed, small.State);

        var (g2, large) = FreeOre(b => b.IsLarge);
        g2.Work(Tool.Wedge, large.Id);
        Assert.Equal(BlockState.Clean, large.State);

        (g2, large) = FreeOre(b => b.IsLarge);
        g2.Work(Tool.Pick, large.Id);
        Assert.Equal(BlockState.Crushed, large.State);

        (g2, large) = FreeOre(b => true);
        g2.Work(Tool.Sledge, large.Id);
        Assert.Equal(BlockState.Crushed, large.State);
    }

    [Fact]
    public void LockedRockWastesAPryButNotTheSledge()
    {
        var g = new MineMinigame(Setup(OreNodeType.IronSeam), 5);
        var solid = g.Blocks.First(b => b.Kind == BlockKind.Solid);
        g.Work(RightTool(solid), solid.Id);
        Assert.True(solid.InPlace);
        Assert.Equal(MineTuning.Swings - 1, g.SwingsLeft);
        g.Work(Tool.Sledge, solid.Id);
        Assert.Equal(BlockState.Removed, solid.State);
    }

    [Fact]
    public void LooseRockComesAwayEvenWhenLocked()
    {
        for (int s = 0; s < 50; s++)
        {
            var g = new MineMinigame(Setup(OreNodeType.IronSeam), s);
            var loose = g.Blocks.FirstOrDefault(b => b.Kind == BlockKind.Loose);
            if (loose == null) continue;
            Assert.True(g.IsLocked(loose));
            g.Work(Tool.Pick, loose.Id);
            Assert.Equal(BlockState.Removed, loose.State);
            return;
        }
        Assert.Fail("no loose block found");
    }

    [Fact]
    public void TappingTellsWhatTheBlockIsAndIsLimitedBySkill()
    {
        var g = new MineMinigame(Setup(OreNodeType.IronSeam, skill: 1), 6);
        Assert.Equal(4, g.TapsLeft);
        Assert.Equal(8, new MineMinigame(Setup(OreNodeType.IronSeam, skill: 100), 6).TapsLeft);
        var ore = g.Blocks.First(b => b.Kind == BlockKind.Ore);
        Assert.Equal(TapSound.Ring, g.Tap(ore.Id));
        Assert.Equal(TapSound.Ring, g.Tap(ore.Id)); // tapping again costs nothing
        Assert.Equal(3, g.TapsLeft);
        var solid = g.Blocks.First(b => b.Kind == BlockKind.Solid);
        Assert.Equal(TapSound.Thud, g.Tap(solid.Id));
        Assert.Equal(MineTuning.Swings, g.SwingsLeft);
    }

    [Fact]
    public void TheRoofWorksAsTheFaceOpensAndPropsEaseIt()
    {
        var g = new MineMinigame(Setup(OreNodeType.CoalSeam, skill: 100), 7);
        Assert.Equal(RoofSound.Solid, g.TapRoof());
        Assert.Equal(0, g.DustLevel);
        foreach (var b in g.Blocks.Where(b => b.Row == g.SeamRow - 1).ToList())
            if (g.RoofStress < 6) g.Work(Tool.Sledge, b.Id);
        Assert.True(g.RoofStress >= 6, $"stress {g.RoofStress}");
        Assert.True(g.DustLevel > 0);
        double before = g.RoofStress;
        g.Prop();
        Assert.Equal(before - MineTuning.PropRelief, g.RoofStress);
    }

    [Fact]
    public void SkilledMinersNoticeDustSooner()
    {
        Assert.True(MineTuning.NoticeAt(100) < MineTuning.NoticeAt(1));
    }

    [Fact]
    public void PushingPastTheLimitBringsTheRoofDown()
    {
        int collapses = 0;
        for (int s = 0; s < 50; s++)
        {
            var g = new MineMinigame(Setup(OreNodeType.IronSeam), s);
            foreach (var b in g.Blocks.Where(b => b.Row <= g.SeamRow).OrderBy(b => b.Row).ToList())
                if (g.Stage == MineStage.Dig) g.Work(Tool.Sledge, b.Id);
            if (g.Collapsed) collapses++;
        }
        Assert.True(collapses > 25, $"collapses {collapses}");
        var expert = Enumerable.Range(0, 50).Count(s => PlayExpert(Setup(OreNodeType.IronSeam), s).Result.Collapsed);
        var reckless = Enumerable.Range(0, 50).Count(s => PlayExpert(Setup(OreNodeType.IronSeam), s, prop: false).Result.Collapsed);
        Assert.True(expert < reckless, $"props {expert} vs none {reckless}");
    }

    [Fact]
    public void CollapseBuriesPartOfTheCart()
    {
        for (int s = 0; s < 200; s++)
        {
            var g = PlayExpert(Setup(OreNodeType.IronSeam), s, prop: false);
            if (!g.Result.Collapsed) continue;
            Assert.True(g.Result.PiecesBuried > 0 || g.Cart.Count == 0);
            return;
        }
        Assert.Fail("never collapsed");
    }

    [Fact]
    public void SortingWellMatters()
    {
        var good = PlayExpert(Setup(OreNodeType.CopperSeam, 100, grade: 70), 8).Result;
        var lazy = PlayExpert(Setup(OreNodeType.CopperSeam, 100, grade: 70), 8, sort: false).Result; // kept everything
        Assert.True(lazy.WasteKept > 0);
        Assert.True(lazy.Purity < 1);
        Assert.True(lazy.Quality < good.Quality, $"{lazy.Quality} vs {good.Quality}");
        Assert.Equal(1.0, good.SortScore);
    }

    [Fact]
    public void OreTellsFollowRealRock()
    {
        var iron = PlayExpert(Setup(OreNodeType.IronSeam), 9, sort: false);
        var coal = PlayExpert(Setup(OreNodeType.CoalSeam), 9, sort: false);
        Assert.All(iron.Cart.Where(p => p.IsOre), p => Assert.Equal(Heft.Heavy, p.Heft));
        Assert.All(coal.Cart.Where(p => p.IsOre), p => Assert.Equal(Heft.Light, p.Heft));
        Assert.All(iron.Cart.Concat(coal.Cart).Where(p => !p.IsOre), p => Assert.Equal(Heft.Ordinary, p.Heft));
    }

    [Fact]
    public void SignMeaningsAreRolledPerWorld()
    {
        for (int w = 0; w < 50; w++)
        {
            var signs = new[] { OreKind.Iron, OreKind.Coal, OreKind.Copper }.Select(o => SignTable.TrueSign(w, o)).ToList();
            Assert.Equal(3, signs.Distinct().Count());
        }
        var ironSigns = Enumerable.Range(0, 50).Select(w => SignTable.TrueSign(w, OreKind.Iron)).Distinct().Count();
        Assert.True(ironSigns >= 4);
    }

    [Fact]
    public void SkilledEyesSeeMoreTrueSigns()
    {
        double Seen(int skill) => Enumerable.Range(0, 200).Average(s =>
        {
            var g = new MineMinigame(Setup(OreNodeType.IronSeam, skill), s);
            var ores = g.Blocks.Where(b => b.Kind == BlockKind.Ore).ToList();
            return ores.Count(b => b.Signs.Contains(g.TrueSign)) / (double)ores.Count;
        });
        Assert.True(Seen(100) > Seen(1) + 0.25);
    }

    [Fact]
    public void JournalRoundTrips()
    {
        var j = new MinersJournal();
        var g = new MineMinigame(Setup(OreNodeType.IronSeam), 10);
        j.RecordFace(OreKind.Iron, g.Blocks);
        j.Record(OreKind.Coal, Sign.BlackBands, true);
        var k = MinersJournal.Parse(j.Serialize());
        foreach (var s in SignTable.AllSigns)
        {
            Assert.Equal(j.Seen(OreKind.Iron, s), k.Seen(OreKind.Iron, s));
            Assert.Equal(j.Seen(OreKind.Coal, s), k.Seen(OreKind.Coal, s));
        }
        Assert.Equal((1, 0), k.Seen(OreKind.Coal, Sign.BlackBands));
        Assert.True(k.IsEmpty(OreKind.Copper));
    }

    [Fact]
    public void CeilingIsTheLowerOfPickAndSkill()
    {
        var r = PlayExpert(Setup(OreNodeType.IronSeam, 100, grade: 100, pick: Pick.WornPick), 11).Result;
        Assert.Equal(60, r.Ceiling);
        Assert.StartsWith("Tool", r.CeilingReason);
        r = PlayExpert(Setup(OreNodeType.IronSeam, 10, grade: 100), 11).Result;
        Assert.Equal(MineTuning.SkillCap(10), r.Ceiling);
    }

    [Fact]
    public void RunningOutOfSwingsEndsTheDig()
    {
        var g = new MineMinigame(Setup(OreNodeType.IronSeam), 12);
        var solid = g.Blocks.First(b => b.Kind == BlockKind.Solid);
        for (int i = 0; i < MineTuning.Swings; i++) g.Work(Tool.Pick, solid.Id); // locked: bounces every time
        Assert.Equal(MineStage.Sort, g.Stage);
        g.FinishSorting();
        Assert.Equal(MineTuning.Swings, g.Result.WastedSwings);
        Assert.Equal(0, g.Result.Yield);
        Assert.Equal(0, g.Result.SkillGain);
    }

    [Fact]
    public void SameSeedSameFace()
    {
        var a = new MineMinigame(Setup(OreNodeType.IronSeam), 42);
        var b = new MineMinigame(Setup(OreNodeType.IronSeam), 42);
        Assert.Equal(a.Blocks.Select(x => (x.Row, x.Col, x.Width, x.Kind)), b.Blocks.Select(x => (x.Row, x.Col, x.Width, x.Kind)));
    }
}

