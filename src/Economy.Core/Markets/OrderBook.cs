using System;
using System.Collections.Generic;

namespace Economy.Core.Markets
{
    public enum Side
    {
        Buy,
        Sell,
    }

    /// <summary>
    /// A resting or incoming limit order. For a sell order <see cref="Quality"/>
    /// is the quality of the units offered; for a buy order it is the lowest
    /// quality the buyer accepts.
    /// </summary>
    public sealed class Order
    {
        public Order(long id, int ownerId, Side side, long price, int quantity, int quality, long sequence)
        {
            if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive.");
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
            Id = id;
            OwnerId = ownerId;
            Side = side;
            Price = price;
            Remaining = quantity;
            Quality = quality;
            Sequence = sequence;
        }

        public long Id { get; }
        public int OwnerId { get; }
        public Side Side { get; }
        public long Price { get; }
        public int Remaining { get; internal set; }
        public int Quality { get; }
        public long Sequence { get; }

        /// <summary>Free-form tag for the owner (for example which stabilizer role placed it).</summary>
        public int Tag { get; set; }
    }

    /// <summary>One match between a buy and a sell order.</summary>
    public readonly struct Fill
    {
        public Fill(Order buy, Order sell, long price, int quantity)
        {
            Buy = buy;
            Sell = sell;
            Price = price;
            Quantity = quantity;
        }

        public Order Buy { get; }
        public Order Sell { get; }

        /// <summary>Per-unit price: always the resting order's price.</summary>
        public long Price { get; }

        public int Quantity { get; }
        public long Total => Price * Quantity;
    }

    /// <summary>
    /// A limit order book for one good in one town. Matching is price first,
    /// then time; a buy only matches sells at or above its minimum quality.
    /// Fills settle at the resting order's price. The book only matches:
    /// holding coins and goods for open orders is the caller's job.
    /// </summary>
    public sealed class OrderBook
    {
        private readonly List<Order> _buys = new List<Order>();  // best (highest) price first
        private readonly List<Order> _sells = new List<Order>(); // best (lowest) price first
        private long _sequence;

        public IReadOnlyList<Order> Buys => _buys;
        public IReadOnlyList<Order> Sells => _sells;

        public long? BestBid => _buys.Count > 0 ? _buys[0].Price : (long?)null;
        public long? BestAsk => _sells.Count > 0 ? _sells[0].Price : (long?)null;

        public long NextSequence() => ++_sequence;

        /// <summary>
        /// Optional: how much a unit of a given quality is worth relative to a
        /// standard one (1.0). When set, a bid is a price for standard quality:
        /// it matches a sell of quality q priced up to bid x weight(q), and sells
        /// are ranked by price per unit of worth, so better goods can ask more
        /// and still sell. A buyer never pays more than its bid.
        /// </summary>
        public Func<int, double>? QualityWeight { get; set; }

        private double Weight(int quality) => QualityWeight == null ? 1.0 : Math.Max(0.05, QualityWeight(quality));

        /// <summary>Price per unit of standard worth.</summary>
        public double StandardPrice(Order sell) => sell.Price / Weight(sell.Quality);

        /// <summary>
        /// Matches an incoming order against the other side and rests what is
        /// left (unless <paramref name="restRemainder"/> is false, which makes it
        /// fill-or-kill for the remainder). Fills are appended to <paramref name="fills"/>.
        /// </summary>
        public void Submit(Order order, List<Fill> fills, bool restRemainder = true)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (fills == null) throw new ArgumentNullException(nameof(fills));

            if (order.Side == Side.Buy)
            {
                for (int i = 0; i < _sells.Count && order.Remaining > 0;)
                {
                    var resting = _sells[i];
                    if (QualityWeight == null ? resting.Price > order.Price : StandardPrice(resting) > order.Price) break;
                    if (resting.Quality < order.Quality || resting.OwnerId == order.OwnerId || resting.Price > order.Price) { i++; continue; }
                    int qty = Math.Min(order.Remaining, resting.Remaining);
                    order.Remaining -= qty;
                    resting.Remaining -= qty;
                    fills.Add(new Fill(order, resting, resting.Price, qty));
                    if (resting.Remaining == 0) _sells.RemoveAt(i); else i++;
                }
                if (order.Remaining > 0 && restRemainder) Insert(_buys, order, BuyBefore);
            }
            else
            {
                for (int i = 0; i < _buys.Count && order.Remaining > 0;)
                {
                    var resting = _buys[i];
                    if (resting.Price < order.Price) break;
                    if (order.Quality < resting.Quality || resting.OwnerId == order.OwnerId) { i++; continue; }
                    // A buyer values this unit at bid x weight(quality), but never pays above the bid.
                    long price = resting.Price;
                    if (QualityWeight != null)
                    {
                        double worth = resting.Price * Weight(order.Quality);
                        if (worth < order.Price) { i++; continue; }
                        price = Math.Min(resting.Price, Math.Max(order.Price, (long)Math.Round(worth)));
                    }
                    int qty = Math.Min(order.Remaining, resting.Remaining);
                    order.Remaining -= qty;
                    resting.Remaining -= qty;
                    fills.Add(new Fill(resting, order, price, qty));
                    if (resting.Remaining == 0) _buys.RemoveAt(i); else i++;
                }
                if (order.Remaining > 0 && restRemainder) Insert(_sells, order, SellOrder);
            }
        }

        /// <summary>
        /// Removes a resting order and returns how many units were still open
        /// (zero if it had already filled or was not in the book).
        /// </summary>
        public int Cancel(Order order)
        {
            var list = order.Side == Side.Buy ? _buys : _sells;
            int i = IndexOf(list, order, order.Side == Side.Buy ? (Func<Order, Order, bool>)BuyBefore : SellOrder);
            if (i < 0) return 0;
            list.RemoveAt(i);
            int left = order.Remaining;
            order.Remaining = 0;
            return left;
        }

        /// <summary>Units offered at or below <paramref name="maxPrice"/>.</summary>
        public int AskDepthAtOrBelow(long maxPrice)
        {
            int total = 0;
            foreach (var o in _sells)
            {
                if (o.Price > maxPrice) break;
                total += o.Remaining;
            }
            return total;
        }

        /// <summary>Units wanted at or above <paramref name="minPrice"/>.</summary>
        public int BidDepthAtOrAbove(long minPrice)
        {
            int total = 0;
            foreach (var o in _buys)
            {
                if (o.Price < minPrice) break;
                total += o.Remaining;
            }
            return total;
        }

        public int TotalAskUnits()
        {
            int total = 0;
            foreach (var o in _sells) total += o.Remaining;
            return total;
        }

        public int TotalBidUnits()
        {
            int total = 0;
            foreach (var o in _buys) total += o.Remaining;
            return total;
        }

        private static bool BuyBefore(Order a, Order b) => a.Price > b.Price || (a.Price == b.Price && a.Sequence < b.Sequence);
        private static bool SellBefore(Order a, Order b) => a.Price < b.Price || (a.Price == b.Price && a.Sequence < b.Sequence);

        /// <summary>Sells rank by price, or by price per unit of worth when quality is weighed.</summary>
        private bool SellOrder(Order a, Order b)
        {
            if (QualityWeight == null) return SellBefore(a, b);
            double pa = StandardPrice(a), pb = StandardPrice(b);
            return pa < pb || (pa == pb && a.Sequence < b.Sequence);
        }

        /// <summary>
        /// Finds a resting order by binary search: books are sorted by price
        /// then sequence, and sequences are unique within a book.
        /// </summary>
        private static int IndexOf(List<Order> list, Order order, Func<Order, Order, bool> before)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (before(list[mid], order)) lo = mid + 1; else hi = mid;
            }
            return lo < list.Count && ReferenceEquals(list[lo], order) ? lo : -1;
        }

        private static void Insert(List<Order> list, Order order, Func<Order, Order, bool> before)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (before(list[mid], order)) lo = mid + 1; else hi = mid;
            }
            list.Insert(lo, order);
        }
    }
}
