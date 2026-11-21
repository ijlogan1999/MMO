using Economy.Core.Ledger;
using Economy.Core.Random;

namespace Economy.Core.Tests;

public class LedgerTests
{
    private static CoinLedger Seeded(out AccountId alice, out AccountId bob)
    {
        var ledger = new CoinLedger(verifyEveryTransfer: true);
        ledger.Transfer(ledger.Mint, ledger.Treasury, 10_000, Flow.TreasurySeed);
        alice = ledger.OpenPlayer("alice");
        bob = ledger.OpenPlayer("bob");
        ledger.Transfer(ledger.Mint, alice, 500, Flow.StarterPurse);
        ledger.Transfer(ledger.Mint, bob, 500, Flow.StarterPurse);
        return ledger;
    }

    [Fact]
    public void CirculationEqualsFaucetsMinusSinks()
    {
        var ledger = Seeded(out var alice, out var bob);
        ledger.Transfer(alice, bob, 120, Flow.PlayerTrade);
        ledger.Transfer(bob, ledger.Treasury, 6, Flow.MarketTax);
        ledger.Transfer(ledger.Treasury, alice, 50, Flow.TownContract);

        Assert.Equal(1000 + 50 - 6, ledger.Circulating);
        Assert.Equal(1050, ledger.FaucetTotal);
        Assert.Equal(6, ledger.SinkTotal);
        Assert.Equal(10_000 + 6 - 50, ledger.TreasuryBalance);
        Assert.Equal(11_000, ledger.TotalCreated);
        ledger.Verify();
    }

    [Theory]
    [InlineData(Flow.MarketTax)]      // a sink cannot pay a player
    [InlineData(Flow.TownContract)]   // a faucet cannot be paid by a player
    [InlineData(Flow.TreasurySeed)]
    public void FlowsMustRunInTheirOwnDirection(Flow flow)
    {
        var ledger = Seeded(out var alice, out var bob);
        Assert.False(ledger.TryTransfer(alice, bob, 10, flow, out var reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void OnlyTheStarterPurseIsPaidByTheMint()
    {
        var ledger = Seeded(out var alice, out _);
        Assert.False(ledger.TryTransfer(ledger.Mint, alice, 10, Flow.TownContract, out _));
        Assert.False(ledger.TryTransfer(ledger.Treasury, alice, 10, Flow.StarterPurse, out _));
    }

    [Fact]
    public void SinksAlwaysGoToTheTreasury()
    {
        var ledger = Seeded(out var alice, out var bob);
        Assert.False(ledger.TryTransfer(alice, bob, 10, Flow.SubclassSwitchFee, out _));
        Assert.True(ledger.TryTransfer(alice, ledger.Treasury, 10, Flow.SubclassSwitchFee, out _));
        Assert.Equal(10, ledger.TotalFor(Flow.SubclassSwitchFee));
    }

    [Fact]
    public void NobodyCanOverspendAndAFailedTransferChangesNothing()
    {
        var ledger = Seeded(out var alice, out var bob);
        Assert.False(ledger.TryTransfer(alice, bob, 501, Flow.PlayerTrade, out _));
        Assert.Equal(500, ledger.Balance(alice));
        Assert.Equal(500, ledger.Balance(bob));
        Assert.Throws<LedgerException>(() => ledger.Transfer(alice, bob, 501, Flow.PlayerTrade));
    }

    [Fact]
    public void TheTreasuryCannotPrintMoney()
    {
        var ledger = Seeded(out var alice, out _);
        Assert.False(ledger.TryTransfer(ledger.Treasury, alice, 10_001, Flow.BuyerGuildPurchase, out _));
    }

    [Fact]
    public void ThousandsOfRandomTransfersNeverLeakACoin()
    {
        var ledger = new CoinLedger(verifyEveryTransfer: false);
        ledger.Transfer(ledger.Mint, ledger.Treasury, 1_000_000, Flow.TreasurySeed);
        var players = Enumerable.Range(0, 50).Select(i => ledger.OpenPlayer($"p{i}")).ToList();
        var escrow = ledger.OpenEscrow("orders");
        foreach (var p in players) ledger.Transfer(ledger.Mint, p, 1_000, Flow.StarterPurse);

        var rng = new SimRandom(2024);
        var sinks = new[] { Flow.MarketTax, Flow.RepairFee, Flow.ShopUpkeep, Flow.NpcSale, Flow.TokenListingFee, Flow.SubclassSwitchFee };
        var faucets = new[] { Flow.TownContract, Flow.BuyerGuildPurchase, Flow.NpcScrapPurchase };
        int ok = 0;
        for (int i = 0; i < 20_000; i++)
        {
            var a = players[rng.NextInt(0, players.Count)];
            var b = players[rng.NextInt(0, players.Count)];
            long amount = rng.NextInt(1, 200);
            bool done = rng.NextInt(0, 5) switch
            {
                0 => ledger.TryTransfer(a, b, amount, Flow.PlayerTrade, out _),
                1 => ledger.TryTransfer(a, ledger.Treasury, amount, sinks[rng.NextInt(0, sinks.Length)], out _),
                2 => ledger.TryTransfer(ledger.Treasury, a, amount, faucets[rng.NextInt(0, faucets.Length)], out _),
                3 => ledger.TryTransfer(a, escrow, amount, Flow.EscrowHold, out _),
                _ => ledger.TryTransfer(escrow, b, amount, Flow.EscrowRelease, out _),
            };
            if (done) ok++;
        }

        Assert.True(ok > 10_000);
        ledger.Verify();
        Assert.Equal(ledger.FaucetTotal - ledger.SinkTotal, ledger.Circulating);
        Assert.Equal(players.Sum(ledger.Balance) + ledger.Balance(escrow), ledger.Circulating);
    }
}
