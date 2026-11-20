using System;
using System.Linq;
using Mmo.Crafting.Cooking;
using Xunit;

public class CookingMinigameTests
{
    private const double Dt = 1.0 / 60.0;

    private static CookSetup Setup(int skill, params CookRecipe[] dishes)
    {
        var s = new CookSetup
        {
            SkillLevel = skill,
            Hearth = Hearth.ShopKitchen,
            Cookware = Cookware.SteelCookware,
            World = new CookWorld(7),
        };
        for (int i = 0; i < dishes.Length; i++) s.Dishes[i] = dishes[i];
        foreach (Ingredient i in Enum.GetValues(typeof(Ingredient))) s.Quality[i] = 100;
        return s;
    }

    private sealed class Bot
    {
        public bool UseGoodHerb = true;
        public Herb ForceHerb = Herb.None;
        public bool ForceHerbSet;
        public double ChopGap = 0.4;
        public double ChopJitter;
        public bool Turn = true;
        public bool Stir = true;
        public bool Skim = true;
        public bool Baste = true;
        public bool AddParts = true;
        public double RestExtra = 1.0;
        public bool CarveAtOnce;
        public Heat PotHeat = Heat.Medium;
        public double PullAt = 1.0; // fraction of the world's done point
    }

    /// <summary>Plays a whole session, attending to every dish each frame (a perfect juggler) unless told not to.</summary>
    private static CookResult Play(CookSetup setup, Bot bot, int seed = 3, Action<CookingMinigame> check = null)
    {
        var g = new CookingMinigame(setup, seed);
        var rng = new Random(seed);
        double clock = 0, nextChop = 0.2;
        var restStarted = new double[g.Dishes.Count];
        for (int frame = 0; frame < 60 * 900 && g.Stage != CookStage.Done; frame++)
        {
            g.Update(Dt);
            clock += Dt;
            switch (g.Stage)
            {
                case CookStage.Season:
                    foreach (var d in g.Dishes)
                    {
                        setup.World.HerbPairing(d.Recipe.Flavor, out Herb good, out _);
                        g.SetHerb(d.Slot, bot.ForceHerbSet ? bot.ForceHerb : bot.UseGoodHerb ? good : Herb.None);
                    }
                    g.FinishSeasoning();
                    break;
                case CookStage.Chop:
                    if (clock >= nextChop)
                    {
                        g.Chop();
                        nextChop = clock + bot.ChopGap * (1 + (rng.NextDouble() * 2 - 1) * bot.ChopJitter);
                    }
                    break;
                case CookStage.Cook:
                    foreach (var d in g.Dishes) Attend(g, d, bot, clock, restStarted, setup);
                    break;
            }
            check?.Invoke(g);
            g.Events.Clear();
        }
        Assert.Equal(CookStage.Done, g.Stage);
        return g.Result;
    }

    private static void Attend(CookingMinigame g, Dish d, Bot bot, double clock, double[] restStarted, CookSetup setup)
    {
        if (d.State == DishState.Served) return;
        g.Select(d.Slot);
        if (d.State == DishState.Resting)
        {
            if (bot.CarveAtOnce || clock - restStarted[d.Slot] >= setup.World.RestCounts + bot.RestExtra) g.TakeOff();
            return;
        }
        if (d.TwoSided)
        {
            if (bot.Turn && d.Sides[d.Facing] > d.Sides[1 - d.Facing] + 0.04) g.Tend();
            if (bot.Baste && d.Recipe.CanBaste && d.Moisture < 0.5) g.SkimOrBaste();
            if ((d.Sides[0] + d.Sides[1]) / 2 >= bot.PullAt * d.Target)
            {
                restStarted[d.Slot] = clock;
                g.TakeOff();
            }
            return;
        }
        if (d.Heat != bot.PotHeat) { if (d.Heat < bot.PotHeat) g.HeatUp(); else g.HeatDown(); }
        if (bot.Stir && d.SinceStir > 2.5) g.Tend();
        if (bot.Skim && d.Scum > 0.08) g.SkimOrBaste();
        if (bot.AddParts && !d.AllPartsIn)
        {
            // Add each part so it finishes with the liquid.
            double left = (d.Target - d.Parts[0]) * d.Recipe.Parts[0].Seconds;
            double need = d.Target * d.Recipe.Parts[d.PartsAdded].Seconds;
            if (left <= need) g.AddNext();
        }
        if (d.Parts[0] >= bot.PullAt * d.Target) g.TakeOff();
    }

    private static readonly CookRecipe[] Everything = CookRecipe.All;

    [Fact]
    public void PerfectJugglerMakesTopQualityFood()
    {
        var setup = Setup(90, Everything);
        var result = Play(setup, new Bot());
        Assert.Equal(5, result.Dishes.Count);
        foreach (var d in result.Dishes)
        {
            Assert.True(d.Score >= 0.85, $"{d.Recipe.Name}: {d.Score:0.00} ({string.Join("; ", d.Notes)})");
            Assert.False(d.Botch);
            Assert.True(d.Quality >= d.Ceiling - 12, $"{d.Recipe.Name}: Q{d.Quality} of {d.Ceiling}");
        }
    }

    [Fact]
    public void StationCountFollowsSkill()
    {
        Assert.Equal(2, new CookingMinigame(Setup(1, Everything), 1).Dishes.Count);
        Assert.Equal(3, new CookingMinigame(Setup(20, Everything), 1).Dishes.Count);
        Assert.Equal(4, new CookingMinigame(Setup(60, Everything), 1).Dishes.Count);
        Assert.Equal(5, new CookingMinigame(Setup(80, Everything), 1).Dishes.Count);
    }

    [Fact]
    public void PotsNeedAPot()
    {
        var setup = Setup(50, CookRecipe.HuntersStew, CookRecipe.MeatOnAStick);
        setup.Cookware = Cookware.StarterKit;
        Assert.NotNull(setup.Blocker(CookRecipe.HuntersStew));
        var g = new CookingMinigame(setup, 1);
        Assert.Single(g.Dishes);
        Assert.Equal(CookRecipe.MeatOnAStick, g.Dishes[0].Recipe);
        Assert.False(g.NeedsChop);
    }

    [Fact]
    public void NeverTurningTheSpitBurnsOneSideAndLeavesTheOtherRaw()
    {
        var turned = Play(Setup(30, CookRecipe.MeatOnAStick), new Bot()).Dishes[0];
        var unturned = Play(Setup(30, CookRecipe.MeatOnAStick), new Bot { Turn = false }).Dishes[0];
        Assert.True(unturned.Doneness < 0.5, $"unturned doneness {unturned.Doneness:0.00}");
        Assert.True(turned.Doneness > 0.95);
        Assert.Contains("One side", unturned.Notes[0]);
    }

    [Fact]
    public void UnstirredPotOnHighHeatBurnsOnto()
    {
        var r = Play(Setup(50, CookRecipe.BoneBroth), new Bot { Stir = false, PotHeat = Heat.High }).Dishes[0];
        Assert.True(r.Ruined);
        Assert.Equal(0, r.Score);
        Assert.Contains("Burnt onto the pot", r.Flaws);
    }

    [Fact]
    public void LowHeatNeverCatchesButCooksSlowly()
    {
        var r = Play(Setup(50, CookRecipe.BoneBroth), new Bot { Stir = false, PotHeat = Heat.Low }).Dishes[0];
        Assert.False(r.Ruined);
        Assert.Equal(0, r.Scorch);
        Assert.True(r.Score > 0.8, $"{r.Score:0.00}");
    }

    [Fact]
    public void UnskimmedPotIsCloudy()
    {
        var skimmed = Play(Setup(50, CookRecipe.BoneBroth), new Bot()).Dishes[0];
        var murky = Play(Setup(50, CookRecipe.BoneBroth), new Bot { Skim = false }).Dishes[0];
        Assert.True(murky.ScumLeft > 0.4, $"scum {murky.ScumLeft:0.00}");
        Assert.True(murky.Handling < skimmed.Handling - 0.1);
    }

    [Fact]
    public void ForgettingAnIngredientIsAFlaw()
    {
        var r = Play(Setup(50, CookRecipe.HuntersStew), new Bot { AddParts = false }).Dishes[0];
        Assert.Contains("Forgot the meat", r.Flaws);
        Assert.Contains("Forgot the vegetables", r.Flaws);
        Assert.True(r.Doneness < 0.4);
    }

    [Fact]
    public void RestingTheRoastHelps()
    {
        var rested = Play(Setup(40, CookRecipe.SpitRoast), new Bot()).Dishes[0];
        var rushed = Play(Setup(40, CookRecipe.SpitRoast), new Bot { CarveAtOnce = true }).Dishes[0];
        Assert.Equal(1.0, rested.RestScore);
        Assert.True(rushed.RestScore < 0.8);
        Assert.Contains("Carved too soon: the juices ran out", rushed.Notes);
    }

    [Fact]
    public void UnbastedRoastDriesOut()
    {
        var basted = Play(Setup(40, CookRecipe.SpitRoast), new Bot()).Dishes[0];
        var dry = Play(Setup(40, CookRecipe.SpitRoast), new Bot { Baste = false }).Dishes[0];
        Assert.True(dry.Moisture < basted.Moisture);
        Assert.True(dry.Handling < basted.Handling);
    }

    [Fact]
    public void HerbPairingMatters()
    {
        var world = new CookWorld(7);
        world.HerbPairing(FlavorBase.GameMeat, out Herb good, out Herb clash);
        Assert.NotEqual(good, clash);
        double Score(Herb h) => Play(Setup(50, CookRecipe.MeatOnAStick), new Bot { ForceHerbSet = true, ForceHerb = h }).Dishes[0].Prep;
        Assert.True(Score(good) > Score(Herb.None));
        Assert.True(Score(Herb.None) > Score(clash));
    }

    [Fact]
    public void EvenChoppingBeatsUnevenChopping()
    {
        var even = Play(Setup(50, CookRecipe.BoneBroth), new Bot());
        var uneven = Play(Setup(50, CookRecipe.BoneBroth), new Bot { ChopJitter = 0.6 });
        Assert.Equal(1.0, even.ChopScore);
        Assert.True(uneven.ChopScore < 0.8, $"{uneven.ChopScore:0.00}");
        Assert.True(uneven.Dishes[0].Prep < even.Dishes[0].Prep);
    }

    [Fact]
    public void PullingEarlyLeavesItUnderdone()
    {
        var r = Play(Setup(50, CookRecipe.Flatbread), new Bot { PullAt = 0.6 }).Dishes[0];
        Assert.True(r.Doneness < 0.5);
        Assert.Contains("underdone", r.Notes[0]);
    }

    [Fact]
    public void LeftOnTheFireItBurnsToACinder()
    {
        var r = Play(Setup(50, CookRecipe.Flatbread), new Bot { PullAt = 99 }).Dishes[0];
        Assert.True(r.Ruined);
        Assert.Equal("Burnt Hearth Flatbread", r.ItemName);
    }

    [Fact]
    public void WorldRollsAreStableAndInRange()
    {
        for (int seed = 1; seed < 50; seed++)
        {
            var w = new CookWorld(seed);
            Assert.InRange(w.RestCounts, 1, 5);
            foreach (var r in CookRecipe.All) Assert.InRange(w.DoneTarget(r), 0.9, 1.1);
            Assert.Equal(w.RestCounts, new CookWorld(seed).RestCounts);
        }
        Assert.NotEqual(new CookWorld(1).RestCounts, new CookWorld(2).RestCounts);
    }

    [Fact]
    public void HearthCapsQuality()
    {
        var setup = Setup(90, CookRecipe.HuntersStew);
        setup.Hearth = Hearth.Campfire;
        var r = Play(setup, new Bot()).Dishes[0];
        Assert.Equal(50, r.Ceiling);
        Assert.StartsWith("Hearth", r.CeilingReason);
        Assert.True(r.Quality <= 50);
    }

    [Fact]
    public void WeakestIngredientsLowerTheCeiling()
    {
        var setup = Setup(90, CookRecipe.SpitRoast);
        setup.Quality[Ingredient.LargeCut] = 30;
        var r = Play(setup, new Bot()).Dishes[0];
        Assert.Equal("Ingredients", r.CeilingReason);
        Assert.Equal((int)Math.Round(0.85 * 30 + 0.15 * 100), r.Ceiling);
    }

    [Fact]
    public void CampfireHeatWandersButStaysBounded()
    {
        var setup = Setup(50, CookRecipe.MeatOnAStick, CookRecipe.Flatbread);
        setup.Hearth = Hearth.Campfire;
        double min = 0, max = 0;
        Play(setup, new Bot(), check: g =>
        {
            foreach (var d in g.Dishes) { min = Math.Min(min, d.Wobble); max = Math.Max(max, d.Wobble); }
        });
        Assert.InRange(min, -0.25, -0.01);
        Assert.InRange(max, 0.01, 0.25);
    }

    [Fact]
    public void SkillGainOnlyFromChallengingRecipes()
    {
        Assert.True(Play(Setup(10, CookRecipe.MeatOnAStick), new Bot()).SkillGain > 0);
        Assert.Equal(0, Play(Setup(60, CookRecipe.MeatOnAStick), new Bot()).SkillGain);
    }
}
