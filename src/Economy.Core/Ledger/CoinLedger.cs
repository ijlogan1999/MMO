using System;
using System.Collections.Generic;

namespace Economy.Core.Ledger
{
    public sealed class LedgerException : Exception
    {
        public LedgerException(string message) : base(message) { }
    }

    public readonly struct AccountId : IEquatable<AccountId>
    {
        internal AccountId(int value) { Value = value; }
        public int Value { get; }
        public bool Equals(AccountId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AccountId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"acct{Value}";
        public static bool operator ==(AccountId a, AccountId b) => a.Equals(b);
        public static bool operator !=(AccountId a, AccountId b) => !a.Equals(b);
    }

    /// <summary>One coin movement, as recorded.</summary>
    public readonly struct LedgerEntry
    {
        public LedgerEntry(long sequence, AccountId from, AccountId to, long amount, Flow flow)
        {
            Sequence = sequence;
            From = from;
            To = to;
            Amount = amount;
            Flow = flow;
        }

        public long Sequence { get; }
        public AccountId From { get; }
        public AccountId To { get; }
        public long Amount { get; }
        public Flow Flow { get; }
    }

    /// <summary>
    /// Double-entry coin ledger. Every coin movement goes through
    /// <see cref="Transfer"/> with a named <see cref="Flow"/>, so the books
    /// always balance and every faucet and sink can be totalled. Coins are
    /// whole numbers (long); there are no fractional coins anywhere.
    /// </summary>
    public sealed class CoinLedger
    {
        private readonly List<Account> _accounts = new List<Account>();
        private readonly long[] _flowTotals = new long[Enum.GetValues(typeof(Flow)).Length];
        private long _sequence;
        private long _circulating;
        private long _faucetTotal;
        private long _sinkTotal;

        /// <param name="verifyEveryTransfer">
        /// Re-adds every balance after each transfer. Slow; meant for tests and
        /// debug runs so a leak is caught on the exact transfer that caused it.
        /// </param>
        public CoinLedger(bool verifyEveryTransfer = false)
        {
            VerifyEveryTransfer = verifyEveryTransfer;
            Mint = Open(AccountKind.Mint, "mint");
            Treasury = Open(AccountKind.Treasury, "treasury");
        }

        public bool VerifyEveryTransfer { get; }
        public AccountId Mint { get; }
        public AccountId Treasury { get; }

        /// <summary>Raised after every successful transfer, for event logs.</summary>
        public event Action<LedgerEntry>? Recorded;

        /// <summary>Coins held by players and escrow.</summary>
        public long Circulating => _circulating;

        public long TreasuryBalance => Balance(Treasury);

        /// <summary>Every coin ever created (the mint's balance, negated).</summary>
        public long TotalCreated => -Balance(Mint);

        public long FaucetTotal => _faucetTotal;
        public long SinkTotal => _sinkTotal;
        public long EntryCount => _sequence;

        public AccountId OpenPlayer(string name) => Open(AccountKind.Player, name);
        public AccountId OpenEscrow(string name) => Open(AccountKind.Escrow, name);

        public long Balance(AccountId id) => Get(id).Balance;
        public AccountKind KindOf(AccountId id) => Get(id).Kind;
        public string NameOf(AccountId id) => Get(id).Name;

        /// <summary>Total moved through one flow since the ledger opened.</summary>
        public long TotalFor(Flow flow) => _flowTotals[(int)flow];

        /// <summary>Moves coins, or throws if the flow is not allowed or the payer cannot cover it.</summary>
        public void Transfer(AccountId from, AccountId to, long amount, Flow flow)
        {
            if (!TryTransfer(from, to, amount, flow, out string? reason))
                throw new LedgerException(reason!);
        }

        /// <summary>
        /// Moves coins if the flow is allowed between these accounts and the
        /// payer can cover it. On failure nothing changes.
        /// </summary>
        public bool TryTransfer(AccountId from, AccountId to, long amount, Flow flow, out string? reason)
        {
            reason = null;
            if (amount <= 0) { reason = $"Amount must be positive (got {amount})."; return false; }
            if (from == to) { reason = "Cannot transfer to the same account."; return false; }

            var src = Get(from);
            var dst = Get(to);
            if (!Flows.IsAllowed(flow, src.Kind, dst.Kind))
            {
                reason = $"{flow} cannot move coins from {src.Kind} '{src.Name}' to {dst.Kind} '{dst.Name}'.";
                return false;
            }
            if (src.Kind != AccountKind.Mint && src.Balance < amount)
            {
                reason = $"'{src.Name}' has {src.Balance} coins, needs {amount} for {flow}.";
                return false;
            }

            checked
            {
                src.Balance -= amount;
                dst.Balance += amount;
                _flowTotals[(int)flow] += amount;
                if (Flows.InCirculation(src.Kind)) _circulating -= amount;
                if (Flows.InCirculation(dst.Kind)) _circulating += amount;

                switch (Flows.DirectionOf(flow))
                {
                    case FlowDirection.Faucet: _faucetTotal += amount; break;
                    case FlowDirection.Sink: _sinkTotal += amount; break;
                }
            }

            var entry = new LedgerEntry(++_sequence, from, to, amount, flow);
            if (VerifyEveryTransfer) Verify();
            Recorded?.Invoke(entry);
            return true;
        }

        /// <summary>
        /// Checks the books, to the coin:
        /// all balances sum to zero; no account but the mint is negative;
        /// coins in circulation equal faucets minus sinks;
        /// the treasury equals its seed plus sinks minus the faucets it paid.
        /// Throws <see cref="LedgerException"/> on the first mismatch.
        /// </summary>
        public void Verify()
        {
            long sum = 0, circulating = 0;
            foreach (var a in _accounts)
            {
                if (a.Kind != AccountKind.Mint && a.Balance < 0)
                    throw new LedgerException($"Account '{a.Name}' is negative: {a.Balance}.");
                sum += a.Balance;
                if (Flows.InCirculation(a.Kind)) circulating += a.Balance;
            }

            if (sum != 0) throw new LedgerException($"Books do not balance: accounts sum to {sum}.");
            if (circulating != _circulating)
                throw new LedgerException($"Running circulation {_circulating} differs from recount {circulating}.");
            if (circulating != _faucetTotal - _sinkTotal)
                throw new LedgerException($"Circulation {circulating} differs from faucets {_faucetTotal} minus sinks {_sinkTotal}.");

            long treasuryPaidFaucets = _faucetTotal - TotalFor(Flow.StarterPurse) - TotalFor(Flow.ExploitDupe);
            long expectedTreasury = TotalFor(Flow.TreasurySeed) + _sinkTotal - treasuryPaidFaucets;
            if (Balance(Treasury) != expectedTreasury)
                throw new LedgerException($"Treasury {Balance(Treasury)} differs from expected {expectedTreasury}.");
        }

        private AccountId Open(AccountKind kind, string name)
        {
            var id = new AccountId(_accounts.Count);
            _accounts.Add(new Account(kind, name ?? string.Empty));
            return id;
        }

        private Account Get(AccountId id)
        {
            if (id.Value < 0 || id.Value >= _accounts.Count) throw new LedgerException($"Unknown account {id}.");
            return _accounts[id.Value];
        }

        private sealed class Account
        {
            public Account(AccountKind kind, string name)
            {
                Kind = kind;
                Name = name;
            }

            public AccountKind Kind { get; }
            public string Name { get; }
            public long Balance { get; set; }
        }
    }
}
