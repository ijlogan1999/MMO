using Economy.Core.Markets;
using Economy.Core.Random;
using Economy.Core.Stabilizer;

namespace Economy.Core.Tests;

public class OrderBookTests
{
    private static Order O(OrderBook b, long id, int owner, Side side, long price, int qty, int quality = 50) =>
        new Order(id, owner, side, price, qty, quality, b.NextSequence());

    [Fact]
    public void FillsAtRestingPriceInPriceTimeOrder()
    {
        var book = new OrderBook();
        var fills = new List<Fill>();
        book.Submit(O(book, 1, 1, Side.Sell, 12, 5), fills);
        book.Submit(O(book, 2, 2, Side.Sell, 10, 5), fills);
        book.Submit(O(book, 3, 3, Side.Sell, 10, 5), fills);
        Assert.Empty(fills);

        var buy = O(book, 4, 4, Side.Buy, 12, 8);
        book.Submit(buy, fills);
        Assert.Equal(2, fills.Count);
        Assert.Equal(2, fills[0].Sell.OwnerId); // cheapest, then oldest
        Assert.Equal(10, fills[0].Price);
        Assert.Equal(5, fills[0].Quantity);
        Assert.Equal(3, fills[1].Sell.OwnerId);
        Assert.Equal(3, fills[1].Quantity);
        Assert.Equal(0, buy.Remaining);
        Assert.Equal(10, book.BestAsk); // two units of owner 3's order remain
    }

    [Fact]
    public void QualityWeighsAgainstPrice()
    {
        // Worth of a unit: quality 100 counts 1.0, quality 20 counts 0.5.
        var book = new OrderBook { QualityWeight = q => q >= 100 ? 1.0 : 0.5 };
        var fills = new List<Fill>();
        book.Submit(O(book, 1, 1, Side.Sell, 60, 1, quality: 20), fills);  // 120 per unit of worth
        book.Submit(O(book, 2, 2, Side.Sell, 90, 1, quality: 100), fills); // 90 per unit of worth
        var buy = O(book, 3, 3, Side.Buy, 100, 1, quality: 1);
        book.Submit(buy, fills);
        Assert.Single(fills);
        Assert.Equal(2, fills[0].Sell.OwnerId); // the better buy, though dearer
        Assert.Equal(90, fills[0].Price);

        // A poor unit only sells if its price fits what the buyer thinks it's worth.
        var poorBook = new OrderBook { QualityWeight = q => q >= 100 ? 1.0 : 0.5 };
        var f2 = new List<Fill>();
        poorBook.Submit(O(poorBook, 1, 1, Side.Buy, 100, 1, quality: 1), f2);
        poorBook.Submit(O(poorBook, 2, 2, Side.Sell, 60, 1, quality: 20), f2);
        Assert.Empty(f2); // worth 50 to this buyer
        poorBook.Submit(O(poorBook, 3, 3, Side.Sell, 40, 1, quality: 20), f2);
        Assert.Single(f2);
        Assert.Equal(50, f2[0].Price); // pays its valuation, never above the bid
    }

    [Fact]
    public void SkipsOwnOrdersAndLowQuality()
    {
        var book = new OrderBook();
        var fills = new List<Fill>();
        book.Submit(O(book, 1, 7, Side.Sell, 10, 5, quality: 20), fills);
        book.Submit(O(book, 2, 8, Side.Sell, 11, 5, quality: 60), fills);
        book.Submit(O(book, 3, 8, Side.Buy, 20, 5, quality: 40), fills);
        Assert.Empty(fills); // 7's goods too poor, 8's are its own
    }

    [Fact]
    public void CancelReturnsWhatWasLeft()
    {
        var book = new OrderBook();
        var fills = new List<Fill>();
        var sell = O(book, 1, 1, Side.Sell, 10, 5);
        book.Submit(sell, fills);
        book.Submit(O(book, 2, 2, Side.Buy, 10, 2), fills);
        Assert.Equal(3, book.Cancel(sell));
        Assert.Equal(0, book.Cancel(sell));
        Assert.Null(book.BestAsk);
    }
}

public class StabilizerTests
{
    private static MarketSignals Signals(double price) => new MarketSignals
    {
        Price = price, Reference = 100, ShortageRate = 0, Liquidity = 100, DailyVolume = 100, Spread = 0.05,
        PlayerProducersActive = true, HasDemand = true,
    };

    [Fact]
    public void SpawnsOnlyAfterSustainedHighPriceAndRetiresLater()
    {
        var settings = new StabilizerSettings { JitterHours = 0 };
        var c = new StabilizerController(settings);
        var rng = new SimRandom(1);
        var events = new List<StabilizerEvent>();
        for (int h = 0; h < settings.SpawnAfterHours - 1; h++) c.Evaluate(Signals(150), true, rng, events);
        Assert.Equal(0, c.Strength(NpcRole.TraderCaravan));
        c.Evaluate(Signals(150), true, rng, events);
        Assert.Equal(1, c.Strength(NpcRole.TraderCaravan));
        for (int h = 0; h < 10; h++) c.Evaluate(Signals(150), true, rng, events);
        Assert.Equal(settings.MaxStrength, c.Strength(NpcRole.TraderCaravan));

        // Back in band: holds for a day, then steps down to zero.
        for (int h = 0; h < settings.RetireAfterHours - 1; h++) c.Evaluate(Signals(100), true, rng, events);
        Assert.Equal(settings.MaxStrength, c.Strength(NpcRole.TraderCaravan));
        for (int h = 0; h < settings.MaxStrength + 1; h++) c.Evaluate(Signals(100), true, rng, events);
        Assert.Equal(0, c.Strength(NpcRole.TraderCaravan));
        Assert.Contains(events, e => e.Kind == StabilizerEventKind.Retire);
    }

    [Fact]
    public void WaitsOutCooldownBeforeRespawning()
    {
        var settings = new StabilizerSettings { JitterHours = 0, RespawnCooldownHours = 48 };
        var c = new StabilizerController(settings);
        var rng = new SimRandom(2);
        var events = new List<StabilizerEvent>();
        for (int h = 0; h < 7; h++) c.Evaluate(Signals(150), true, rng, events);
        for (int h = 0; h < 40; h++) c.Evaluate(Signals(100), true, rng, events);
        Assert.Equal(0, c.Strength(NpcRole.TraderCaravan));

        events.Clear();
        // About 15 hours of cooldown were used up in band; 33 remain.
        for (int h = 0; h < 30; h++) c.Evaluate(Signals(150), true, rng, events);
        Assert.DoesNotContain(events, e => e.Kind == StabilizerEventKind.Spawn);
        for (int h = 0; h < 30; h++) c.Evaluate(Signals(150), true, rng, events);
        Assert.Contains(events, e => e.Kind == StabilizerEventKind.Spawn);
    }

    [Fact]
    public void GuildCannotSpawnWhenTreasuryCannotPay()
    {
        var c = new StabilizerController(new StabilizerSettings { JitterHours = 0 });
        var rng = new SimRandom(3);
        var events = new List<StabilizerEvent>();
        for (int h = 0; h < 30; h++) c.Evaluate(Signals(50), canAfford: false, rng, events);
        Assert.Equal(0, c.Strength(NpcRole.BuyerGuild));
        for (int h = 0; h < 30; h++) c.Evaluate(Signals(50), canAfford: true, rng, events);
        Assert.True(c.Strength(NpcRole.BuyerGuild) > 0);
    }

    [Fact]
    public void ReferenceMovesHalfwayInOneHalfLife()
    {
        var r = new ReferencePrice(100, 10);
        for (int h = 0; h < 10; h++) r.Observe(200);
        Assert.Equal(150, r.Value, 6);
    }
}
