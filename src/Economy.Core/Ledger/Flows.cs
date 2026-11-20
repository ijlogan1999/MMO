using System;

namespace Economy.Core.Ledger
{
    /// <summary>Who holds an account.</summary>
    public enum AccountKind
    {
        /// <summary>
        /// Outside the world. The only account allowed below zero: its negative
        /// balance is every coin ever created.
        /// </summary>
        Mint,
        /// <summary>The economy's central bank. Funded by sinks, pays faucets, never prints.</summary>
        Treasury,
        /// <summary>A player (or simulated agent).</summary>
        Player,
        /// <summary>Coins held for open buy orders and posted contracts.</summary>
        Escrow,
    }

    /// <summary>Whether a flow adds coins to circulation, removes them, or just moves them.</summary>
    public enum FlowDirection
    {
        /// <summary>Into circulation (players and escrow) from the treasury or the mint.</summary>
        Faucet,
        /// <summary>Out of circulation into the treasury.</summary>
        Sink,
        /// <summary>Between players or escrow; circulation unchanged.</summary>
        Transfer,
        /// <summary>Mint to treasury; sets the treasury's starting balance.</summary>
        TreasurySeed,
    }

    /// <summary>
    /// Every named way a coin can move. The design doc's rule is that coins
    /// enter only through named faucets and leave only through named sinks, so
    /// a movement that is not listed here cannot happen.
    /// </summary>
    public enum Flow
    {
        // Faucets
        StarterPurse,
        TownContract,
        BuyerGuildPurchase,
        NpcScrapPurchase,
        HiredHandWage,
        MarketMakerPurchase,

        /// <summary>Coins created by a bug or exploit (a dupe). Only for testing the gold-flood scenario.</summary>
        ExploitDupe,

        // Sinks
        MarketTax,
        StationFee,
        RepairFee,
        PlotRent,
        ShopUpkeep,
        KeeperCut,
        GuardUpkeep,
        NpcSale,
        TokenListingFee,
        SubclassSwitchFee,
        NpcHireFee,
        /// <summary>Paid to retrain into a new profession.</summary>
        ProfessionSwitchFee,

        // Transfers between players
        PlayerTrade,
        EscrowHold,
        EscrowRelease,
        ContractPayment,

        // Setup
        TreasurySeed,
    }

    public static class Flows
    {
        public static FlowDirection DirectionOf(Flow flow) => flow switch
        {
            Flow.StarterPurse => FlowDirection.Faucet,
            Flow.TownContract => FlowDirection.Faucet,
            Flow.BuyerGuildPurchase => FlowDirection.Faucet,
            Flow.NpcScrapPurchase => FlowDirection.Faucet,
            Flow.HiredHandWage => FlowDirection.Faucet,
            Flow.MarketMakerPurchase => FlowDirection.Faucet,
            Flow.ExploitDupe => FlowDirection.Faucet,

            Flow.MarketTax => FlowDirection.Sink,
            Flow.StationFee => FlowDirection.Sink,
            Flow.RepairFee => FlowDirection.Sink,
            Flow.PlotRent => FlowDirection.Sink,
            Flow.ShopUpkeep => FlowDirection.Sink,
            Flow.KeeperCut => FlowDirection.Sink,
            Flow.GuardUpkeep => FlowDirection.Sink,
            Flow.NpcSale => FlowDirection.Sink,
            Flow.TokenListingFee => FlowDirection.Sink,
            Flow.SubclassSwitchFee => FlowDirection.Sink,
            Flow.NpcHireFee => FlowDirection.Sink,
            Flow.ProfessionSwitchFee => FlowDirection.Sink,

            Flow.PlayerTrade => FlowDirection.Transfer,
            Flow.EscrowHold => FlowDirection.Transfer,
            Flow.EscrowRelease => FlowDirection.Transfer,
            Flow.ContractPayment => FlowDirection.Transfer,

            Flow.TreasurySeed => FlowDirection.TreasurySeed,
            _ => throw new ArgumentOutOfRangeException(nameof(flow), flow, "Unclassified flow."),
        };

        /// <summary>Accounts that count as coins in circulation.</summary>
        public static bool InCirculation(AccountKind kind) => kind == AccountKind.Player || kind == AccountKind.Escrow;

        /// <summary>
        /// Checks a flow moves between the right kinds of account. The starter
        /// purse (and, in tests, an exploit dupe) is paid by the mint (true money creation);
        /// every other faucet is paid from the treasury.
        /// </summary>
        public static bool IsAllowed(Flow flow, AccountKind from, AccountKind to)
        {
            switch (DirectionOf(flow))
            {
                case FlowDirection.Faucet:
                    if (!InCirculation(to)) return false;
                    return flow == Flow.StarterPurse || flow == Flow.ExploitDupe
                        ? from == AccountKind.Mint
                        : from == AccountKind.Treasury;
                case FlowDirection.Sink:
                    return InCirculation(from) && to == AccountKind.Treasury;
                case FlowDirection.Transfer:
                    return InCirculation(from) && InCirculation(to);
                case FlowDirection.TreasurySeed:
                    return from == AccountKind.Mint && to == AccountKind.Treasury;
                default:
                    return false;
            }
        }
    }
}
