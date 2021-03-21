using System;
using System.Collections.Generic;
using System.Globalization;
using JsonParsing;
using System.Linq;
using System.Text;
using Core;
using Force.Crc32;


namespace OrderbookTracking
{
    using OrderbookSide = SortedDictionary<decimal, decimal>;

    // Representation of strings like "00123.0032100"
    // public readonly struct decimal
    // {
    //     public readonly int BeforeDecimal;
    //     public readonly int AfterDecimal;
    //     public readonly int Exponent;
    //
    //     public decimal(string value)
    //     {
    //         var sp = value.Split(".");
    //         if (sp.Length != 2) throw new Exception($"invalid arg {value}");
    //         var decs = sp[1];
    //         // calculate 10-based negative exponent
    //         var count = 0;
    //         foreach (var c in decs)
    //             if (c == '0') count++;
    //             else break;
    //         Exponent = count;
    //         decs = decs.TrimEnd('0');
    //         AfterDecimal = int.Parse(decs != "" ? decs : "0"); // final int value
    //         BeforeDecimal = int.Parse(sp[0]);
    //     }
    //
    //     // convert back to string
    //     public override string ToString()
    //     {
    //         return (BeforeDecimal + "." + new string('0', Exponent) + AfterDecimal);
    //     }
    // }
    //
    // public class decimalComparer : Comparer<decimal>
    // {
    //     // Efficiently compares e.g. "00123.12300"  with "12300.00123"
    //     // without converting to float (i.e. without any risk of losing precision).
    //     public override int Compare(decimal x, decimal y)
    //     {
    //         // if (x == null) throw new ArgumentNullException(nameof(x));
    //         // if (y == null) throw new ArgumentNullException(nameof(y));
    //
    //         // compares before decimal point first,
    //         // then compare (-1 times) number of leading zeroes after decimal point,
    //         // finally, compare value after decimal point
    //         var comp1 = x.BeforeDecimal.CompareTo(y.BeforeDecimal);
    //         if (comp1 != 0) return comp1;
    //         var xZeroDecs = x.AfterDecimal == 0;
    //         if (xZeroDecs) return x.AfterDecimal.CompareTo(y.AfterDecimal);
    //         var yZeroDecs = x.AfterDecimal == 0;
    //         if (yZeroDecs) return 1;
    //         var compExponents = y.Exponent.CompareTo(x.Exponent); // note x/y swap: more zeroes == smaller
    //         return compExponents != 0 ? compExponents : x.AfterDecimal.CompareTo(y.AfterDecimal);
    //     }
    // }

    // Define a class to hold checksum mismatch info
    public class ChecksumMismatch : EventArgs
    {
        public readonly string Name;
        public readonly string ExpectedChecksum;
        public readonly string ActualChecksum;

        public ChecksumMismatch(string name, string expectedChecksum, string actualChecksum)
        {
            Name = name;
            ExpectedChecksum = expectedChecksum;
            ActualChecksum = actualChecksum;
        }
    }

    // base class for tracking kraken orderbooks, requires
    // implementers to decide what to do with the resulting orderoboks
    public abstract class TrackerBase
    {
        private static OrderbookSide _newOrderbookSide() => new();

        private bool _initialized;
        private OrderbookSide _askSide;
        private OrderbookSide _bidSide;
        private string _timestamp = "0";
        public readonly string Name;
        public readonly int Depth;
        public event EventHandler<ChecksumMismatch> RaiseChecksumMismatch;

        public TrackerBase(string name, int depth)
        {
            Name = name;
            Depth = depth;
        }

        // callback-styled implementation: method is called whenever a new orderbook is available 
        protected abstract void OnBookAvailable(string timestamp, in OrderbookSide askSide,
            in OrderbookSide bidSide);

        // helper, as class is very stateful
        private void PublishBook() => OnBookAvailable(_timestamp, _askSide, _bidSide);

        // looping an orderbook side, allows reuse for both bid and ask, despite being slightly different 
        private static void FillSide(in Levels levels, ref OrderbookSide side)
        {
            foreach (var tuple in levels.Tuples)
            {
                side.Add(decimal.Parse(tuple.PriceLevel), decimal.Parse(tuple.Volume));
            }
        }

        // tiny helper 
        private static string MaxTs(in Levels levels) =>
            levels.Tuples.Max(x => x.Timestamp) ?? throw new ArgumentNullException(nameof(levels));

        // (Re)initialize tracking when receiving a snapshot
        public void NewSnapshot(in Book book)
        {
            _askSide = _newOrderbookSide();
            _bidSide = _newOrderbookSide();

            FillSide(book.Asks, ref _askSide);
            FillSide(book.Bids, ref _bidSide);
            var maxAskTs = MaxTs(book.Asks);
            var maxBidTs = MaxTs(book.Bids);
            _timestamp = string.Compare(maxAskTs, maxBidTs, StringComparison.Ordinal) >= 0
                ? maxAskTs
                : maxBidTs;

            // snapshot ok
            _initialized = true;
            PublishBook();
        }

        // looping an orderbook side, allows reuse for both bid and ask, despite being slightly different 
        private static void UpdateSide(in Levels levels, ref OrderbookSide side)
        {
            foreach (var tuple in levels.Tuples)
            {
                if (tuple.Volume != "0.00000000")
                    side[decimal.Parse(tuple.PriceLevel)] = decimal.Parse(tuple.Volume);
                else side.Remove(decimal.Parse(tuple.PriceLevel));
            }
        }

        // tiny helper
        private static string MaxNonRepublishedTs(in Levels levels) =>
            levels.Tuples.Where(x => x.UpdateType != "r").Max(x => x.Timestamp);

        // Given the fixed subscription depth, the worst orderbook levels needs to be explicitly trimmed when
        // better levels are inserted
        private void TrimDepth(ref OrderbookSide side, bool trimHighest)
        {
            var currentDepth = side.Count;
            if (currentDepth <= Depth) return;
            if (trimHighest)
            {
                for (var i = currentDepth - 1; i >= Depth; i--)
                {
                    side.Remove(side.ElementAt(i).Key);
                }
            }
            else
            {
                for (var i = 0; i < currentDepth - Depth; i++)
                {
                    side.Remove(side.ElementAt(0).Key);
                }
            }
        }

        // tiny helper
        private static string DecimalToChecksumString(decimal x)
        {
            return x.ToString(CultureInfo.InvariantCulture).Replace(".", string.Empty).TrimStart('0');
        }

        // looping an orderbook side, allows reuse for both bid and ask, despite being slightly different 
        private static IEnumerable<string> CheckSumSide(in OrderbookSide side, bool useHighest)
        {
            var values = useHighest ? side.TakeLast(10).Reverse() : side.Take(10);
            return values.SelectMany(x =>
                new[] {DecimalToChecksumString(x.Key), DecimalToChecksumString(x.Value)});
        }

        // calculates the checksum of an orderbook, according to documentation, to ensure no messages have been dropped
        private string Checksum()
        {
            var asks = CheckSumSide(in _askSide, false);
            var bids = CheckSumSide(in _bidSide, true);
            var bytes = Encoding.ASCII.GetBytes(string.Join("", asks.Concat(bids)));
            return Crc32Algorithm.Compute(bytes).ToString();
        }

        // Update orderbook, and halt tracking (while alerting) on checksum mismatches
        public void NewUpdate(in Book book)
        {
            // if snapshot is not ok, skip update
            if (!_initialized) return;
            UpdateSide(book.Asks, ref _askSide);
            UpdateSide(book.Bids, ref _bidSide);
            var maxAskTs = MaxNonRepublishedTs(book.Asks);
            var maxBidTs = MaxNonRepublishedTs(book.Bids);
            if (maxAskTs != string.Empty || maxBidTs != string.Empty)
            {
                var newTsCand = string.Compare(maxAskTs, maxBidTs, StringComparison.Ordinal) >= 0
                    ? maxAskTs
                    : maxBidTs;

                _timestamp = string.Compare(newTsCand, _timestamp, StringComparison.Ordinal) >= 0
                    ? newTsCand
                    : _timestamp;
            }

            TrimDepth(ref _askSide, true);
            TrimDepth(ref _bidSide, false);

            var actualChecksum = Checksum();
            if (book.Checksum != actualChecksum)
            {
                // snapshot not ok
                _initialized = false;
                RaiseChecksumMismatch?.Invoke(this,
                    new ChecksumMismatch(Name, book.Checksum, actualChecksum));
                return;
            }

            PublishBook();
        }
    }

    namespace GlobalTrackerImplementations
    {
        // A simple synchronous multi-pair orderbook tracker, maining a dictionary of all individual trackers and 
        // invoking these as needed
        public class SyncDictDelegator<TTracker> : IGlobalTracker where TTracker : TrackerBase
        {
            public event EventHandler<string> RaiseSnapshotRequest;
            private readonly Func<string, int, TTracker> _factory;
            private readonly int _depth;

            public SyncDictDelegator(Func<string, int, TTracker> trackerFactory, int depth = 10)
            {
                _factory = trackerFactory;
                _depth = depth;
            }

            private readonly Dictionary<string, TTracker> _trackers = new();

            // relay checksum mismatches as SnapshotRequests (or rather, just pair as string)
            private void ChecksumMismatchHandler(object sender, ChecksumMismatch mismatch)
            {
                RaiseSnapshotRequest?.Invoke(this, mismatch.Name);
            }

            // helper, setting up checksum mismatch handler
            private TTracker GetOrAddTracker(in Book book)
            {
                var pair = book.Pair;
                if (_trackers.TryGetValue(pair, out var tracker)) return tracker;
                tracker = _factory(pair, _depth);
                tracker.RaiseChecksumMismatch += ChecksumMismatchHandler;
                _trackers.Add(pair, tracker);
                return tracker;
            }

            // Pass snapshot to tracker
            public void NewSnapshot(in Book book)
            {
                var tracker = GetOrAddTracker(in book);
                tracker.NewSnapshot(book);
            }

            // Pass update to tracker
            public void NewUpdate(in Book book)
            {
                var pair = book.Pair;
                var success = _trackers.TryGetValue(pair, out var tracker);
                if (!success)
                {
                    RaiseSnapshotRequest?.Invoke(this, pair);
                    return;
                }

                tracker?.NewUpdate(book);
            }

            // Release resources on unsubscribe 
            public void NewUnsubscribe(string pair)
            {
                var success = _trackers.TryGetValue(pair, out var tracker);
                if (!success) return;
                tracker.RaiseChecksumMismatch -= ChecksumMismatchHandler;
                _trackers.Remove(pair);
            }
        }
    }
}