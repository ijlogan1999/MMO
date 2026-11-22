using Economy.Core.Random;

namespace Economy.Core.Tests;

public class SimRandomTests
{
    [Fact]
    public void SameSeedReplaysSameSequence()
    {
        var a = new SimRandom(42);
        var b = new SimRandom(42);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void SequenceIsPinned()
    {
        // If this changes, every saved seed replays differently. Change it only on purpose.
        var r = new SimRandom(1);
        var first = new[] { r.NextULong(), r.NextULong(), r.NextULong() };
        var again = new SimRandom(1);
        Assert.Equal(first, new[] { again.NextULong(), again.NextULong(), again.NextULong() });
        Assert.Equal(PinnedFirstValue, first[0]);
    }

    // Recorded from the current implementation (xoshiro256** seeded by SplitMix64 with seed 1).
    private const ulong PinnedFirstValue = 0xB3F2AF6D0FC710C5UL;

    [Fact]
    public void NextIntStaysInRange()
    {
        var r = new SimRandom(7);
        for (int i = 0; i < 10_000; i++)
        {
            int v = r.NextInt(-3, 4);
            Assert.InRange(v, -3, 3);
        }
    }

    [Fact]
    public void NextDoubleStaysInUnitInterval()
    {
        var r = new SimRandom(9);
        for (int i = 0; i < 10_000; i++) Assert.InRange(r.NextDouble(), 0.0, 0.9999999999);
    }

    [Fact]
    public void ForksAreIndependentOfParentDraws()
    {
        var parentA = new SimRandom(5);
        var forkA = parentA.Fork(1);
        var parentB = new SimRandom(5);
        var forkB = parentB.Fork(1);
        parentB.NextULong(); // drawing from the parent must not shift the fork
        Assert.Equal(forkA.NextULong(), forkB.NextULong());
        Assert.NotEqual(new SimRandom(5).Fork(1).NextULong(), new SimRandom(5).Fork(2).NextULong());
    }
}
