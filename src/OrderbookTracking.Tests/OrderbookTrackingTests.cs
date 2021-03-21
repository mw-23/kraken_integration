using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Force.Crc32;
using JsonParsing;
using Xunit;
using OrderbookTracking;
using OrderbookTracking.GlobalTrackerImplementations;

namespace OrderbookTracking.Tests
{
    using OrderbookSide = SortedDictionary<ExactFloat, ExactFloat>;

    public class ExactFloatTests
    {
        [Theory]
        [InlineData("123.0", "123.0", 0)]
        [InlineData("0123.0", "123.0", 0)]
        [InlineData("000123.0", "123.0", 0)]
        [InlineData("123.00", "123.0", 0)]
        [InlineData("1230.00", "1230.0", 0)]
        [InlineData("123.1", "123.0", 1)]
        [InlineData("123.10", "123.00", 1)]
        [InlineData("123.10", "123.001", 1)]
        [InlineData("0123.10", "0123.01", 1)]
        [InlineData("321.0", "123.0", 1)]
        [InlineData("321.1", "123.0", 1)]
        [InlineData("321.0", "123.1", 1)]
        [InlineData("0321.0", "123.0", 1)]
        [InlineData("0321.0", "123.1", 1)]
        [InlineData("0321.00", "123.10", 1)]
        [InlineData("0321.00", "123.001", 1)]
        [InlineData("12300.0", "1230.0", 1)]
        [InlineData("12300.00", "1230.0", 1)]
        [InlineData("123.00", "123.10", -1)]
        [InlineData("123.001", "123.10", -1)]
        [InlineData("0123.01", "0123.10", -1)]
        [InlineData("123.0", "321.0", -1)]
        [InlineData("123.0", "321.1", -1)]
        [InlineData("123.1", "321.0", -1)]
        [InlineData("123.0", "0321.0", -1)]
        [InlineData("123.1", "0321.0", -1)]
        [InlineData("123.10", "0321.00", -1)]
        [InlineData("123.001", "0321.00", -1)]
        [InlineData("123.000", "1230.0", -1)]
        [InlineData("1230.00", "12300.0", -1)]
        [InlineData("1230.000", "12300.00", -1)]
        public void Compare(string x, string y, int comp)
        {
            var fx = new ExactFloat(x);
            var fy = new ExactFloat(y);
            var comparer = new ExactFloatComparer();
            Assert.Equal(comparer.Compare(fx, fy), comp);
        }
    }

    public class TestTracker : TrackerBase
    {
        public List<string> Timstamps = new();
        public List<OrderbookSide> AskSides = new();
        public List<OrderbookSide> BidSides = new();
        public List<ChecksumMismatch> MisMatches = new();

        public TestTracker(string name, int depth) : base(name, depth)
        {
            RaiseChecksumMismatch += (_, mismatch) => MisMatches.Add(mismatch);
        }

        protected override void OnBookAvailable(string timestamp, in OrderbookSide askSide,
            in OrderbookSide bidSide)
        {
            Timstamps.Add(timestamp);
            AskSides.Add(askSide);
            BidSides.Add(bidSide);
        }
    }

    public class TrackerTests
    {
        public static Book InitialSnapshot(string pair = "XBT/USDT")
        {
            var asks = new List<List<string>>
            {
                new() {"61074.90000", "0.03300000", "1615680100.357494"},
                new() {"61076.90000", "0.15000000", "1615680100.316398"},
                new() {"61083.90000", "0.08189270", "1615680100.270453"},
                new() {"61091.90000", "0.03226286", "1615680094.993643"},
                new() {"61092.00000", "0.15000000", "1615680093.894549"},
                new() {"61093.20000", "0.12283571", "1615680097.383483"},
                new() {"61097.90000", "0.06983296", "1615680100.335221"},
                new() {"61098.00000", "0.20000000", "1615680095.828050"},
                new() {"61100.40000", "0.08446000", "1615680089.209693"},
                new() {"61102.50000", "0.12281758", "1615680091.383754"}
            };
            var bids = new List<List<string>>
            {
                new() {"61064.70000", "0.08189044", "1615680096.384581"},
                new() {"61059.70000", "0.16360772", "1615680058.895440"},
                new() {"61047.20000", "0.12282923", "1615680094.966206"},
                new() {"61033.00000", "0.10000000", "1615680098.967141"},
                new() {"61032.80000", "0.10000000", "1615680099.376576"},
                new() {"61028.70000", "0.30000000", "1615680099.363348"},
                new() {"61028.60000", "0.16371139", "1615680084.251615"},
                new() {"61025.80000", "0.03411080", "1615680091.509969"},
                new() {"61025.30000", "0.16371961", "1615680085.974857"},
                new() {"61023.90000", "0.00474282", "1615680072.457932"}
            };
            var snapshot = new Book(channelId: 2304, pair: pair, channelName: "book-10",
                asks: asks.Select(tup => new LevelTuple(tup)),
                bids: bids.Select(tup => new LevelTuple(tup)),
                checksum: "", update: false);
            return snapshot;
        }

        public static Book AskUpdate(bool correctChecksum, string pair = "XBT/USDT")
        {
            var asks = new List<List<string>>
            {
                new() {"61100.40000", "2.0", "1700000001.000000"},
                new() {"61102.50000", "1.0", "1700000000.000000"}
            };
            var snapshot = new Book(channelId: 2304, pair: pair, channelName: "book-10",
                asks: asks.Select(tup => new LevelTuple(tup)),
                bids: new List<LevelTuple>(),
                checksum: correctChecksum ? "3904097099" : "1", update: false);
            return snapshot;
        }

        public static Book AskRepublishUpdate()
        {
            var asks = new List<List<string>>
            {
                new() {"61100.40000", "2.0", "1700000001.000000", "r"},
                new() {"61102.50000", "1.0", "1700000000.000000"}
            };
            var snapshot = new Book(channelId: 2304, pair: "XBT/USDT", channelName: "book-10",
                asks: asks.Select(tup => new LevelTuple(tup)),
                bids: new List<LevelTuple>(),
                checksum: "3904097099", update: false);
            return snapshot;
        }

        public static Book AskDeleteUpdate()
        {
            var asks = new List<List<string>>
            {
                new() {"61102.50000", "0.00000000", "1700000000.000000"}
            };
            var snapshot = new Book(channelId: 2304, pair: "XBT/USDT", channelName: "book-10",
                asks: asks.Select(tup => new LevelTuple(tup)),
                bids: new List<LevelTuple>(),
                checksum: "400301931", update: false);
            return snapshot;
        }

        public static Book AskUpdateAddingLevels()
        {
            var asks = new List<List<string>>
            {
                // new best ask
                new() {"61066.70000", "2.0", "1700000001.000000"},
                new() {"61065.70000", "1.0", "1700000000.000000"}
            };
            var snapshot = new Book(channelId: 2304, pair: "XBT/USDT", channelName: "book-10",
                asks: asks.Select(tup => new LevelTuple(tup)),
                bids: new List<LevelTuple>(),
                checksum: "2354439088", update: false);
            return snapshot;
        }


        public static Book BidUpdate(bool correctChecksum)
        {
            var bids = new List<List<string>>
            {
                new() {"61033.00000", "2.0", "1700000001.000000"},
                new() {"61028.70000", "1.0", "1700000000.000000"}
            };
            var snapshot = new Book(channelId: 2304, pair: "XBT/USDT", channelName: "book-10",
                asks: new List<LevelTuple>(),
                bids: bids.Select(tup => new LevelTuple(tup)),
                checksum: correctChecksum ? "435180619" : "1", update: false);
            return snapshot;
        }

        public static Book BidUpdateAddingLevels()
        {
            var bids = new List<List<string>>
            {
                // new best bid
                new() {"61066.70000", "2.0", "1700000001.000000"},
                new() {"61065.70000", "1.0", "1700000000.000000"}
            };
            var snapshot = new Book(channelId: 2304, pair: "XBT/USDT", channelName: "book-10",
                asks: new List<LevelTuple>(),
                bids: bids.Select(tup => new LevelTuple(tup)),
                checksum: "3197673906", update: false);
            return snapshot;
        }

        public static Book AskBidUpdate()
        {
            var asks = new List<List<string>>
            {
                new() {"61100.40000", "2.0", "1700000001.000000"},
                new() {"61102.50000", "1.0", "1700000000.000000"}
            };
            var bids = new List<List<string>>
            {
                new() {"61033.00000", "2.0", "1700000001.000000"},
                new() {"61028.70000", "1.0", "1700000000.000000"}
            };
            var snapshot = new Book(channelId: 2304, pair: "XBT/USDT", channelName: "book-10",
                asks: asks.Select(tup => new LevelTuple(tup)),
                bids: bids.Select(tup => new LevelTuple(tup)),
                checksum: "601930136", update: false);
            return snapshot;
        }

        [Fact]
        public void Snapshot()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(snapshot);

            Assert.Single(tracker.AskSides);
            Assert.Single(tracker.BidSides);
            Assert.Single(tracker.Timstamps);
            Assert.Empty(tracker.MisMatches);

            var expectedAsks = new OrderbookSide(new ExactFloatComparer());
            var expectedBids = new OrderbookSide(new ExactFloatComparer());

            foreach (LevelTuple tup in snapshot.Asks.Tuples)
            {
                expectedAsks.Add(new ExactFloat(tup.PriceLevel), new ExactFloat(tup.Volume));
            }

            foreach (LevelTuple tup in snapshot.Bids.Tuples)
            {
                expectedBids.Add(new ExactFloat(tup.PriceLevel), new ExactFloat(tup.Volume));
            }


            Assert.Equal(expectedAsks.Count, tracker.AskSides[0].Count);
            Assert.Equal(expectedBids.Count, tracker.BidSides[0].Count);

            foreach (KeyValuePair<ExactFloat, ExactFloat> pair in expectedAsks)
            {
                Assert.Equal(tracker.AskSides[0][pair.Key], pair.Value);
            }

            foreach (KeyValuePair<ExactFloat, ExactFloat> pair in expectedBids)
            {
                Assert.Equal(tracker.BidSides[0][pair.Key], pair.Value);
            }

            // max timestamp
            string maxA = snapshot.Asks.Tuples.Max(x => x.Timestamp) ??
                          throw new ArgumentNullException(nameof(snapshot));
            string maxB = snapshot.Asks.Tuples.Max(x => x.Timestamp) ??
                          throw new ArgumentNullException(nameof(snapshot));
            var em = string.Compare(maxA, maxB, StringComparison.Ordinal) >= 0 ? maxA : maxB;

            Assert.Equal(em, tracker.Timstamps[0]);
            Assert.Equal(em, tracker.Timstamps[0]);
        }

        [Fact]
        public void AskMismatch()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = AskUpdate(false);
            tracker.NewUpdate(in update);
            Assert.Single(tracker.MisMatches);
            Assert.Single(tracker.AskSides);
            Assert.Single(tracker.BidSides);
            Assert.Single(tracker.Timstamps);
        }

        [Fact]
        public void Ask()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = AskUpdate(true);
            tracker.NewUpdate(in update);
            Assert.Empty(tracker.MisMatches);
            Assert.Equal(2, tracker.AskSides.Count);
            Assert.Equal(2, tracker.BidSides.Count);
            Assert.Equal(2, tracker.Timstamps.Count);
            Assert.Equal("1700000001.000000", tracker.Timstamps[1]);
            var updatedAsks = tracker.AskSides[1];
            Assert.Equal(new ExactFloat("2.0"), updatedAsks[new ExactFloat("61100.40000")]);
            Assert.Equal(new ExactFloat("1.0"), updatedAsks[new ExactFloat("61102.50000")]);
        }

        [Fact]
        public void Bid()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = BidUpdate(true);
            tracker.NewUpdate(in update);
            Assert.Empty(tracker.MisMatches);
            Assert.Equal(2, tracker.AskSides.Count);
            Assert.Equal(2, tracker.BidSides.Count);
            Assert.Equal(2, tracker.Timstamps.Count);
            Assert.Equal("1700000001.000000", tracker.Timstamps[1]);
            var updated = tracker.BidSides[1];
            Assert.Equal(new ExactFloat("2.0"), updated[new ExactFloat("61033.00000")]);
            Assert.Equal(new ExactFloat("1.0"), updated[new ExactFloat("61028.70000")]);
        }

        [Fact]
        public void BidMismatch()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = BidUpdate(false);
            tracker.NewUpdate(in update);
            Assert.Single(tracker.MisMatches);
            Assert.Single(tracker.AskSides);
            Assert.Single(tracker.BidSides);
            Assert.Single(tracker.Timstamps);
        }

        [Fact]
        public void AskBid()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = AskBidUpdate();
            tracker.NewUpdate(in update);
            Assert.Empty(tracker.MisMatches);
            Assert.Equal(2, tracker.AskSides.Count);
            Assert.Equal(2, tracker.BidSides.Count);
            Assert.Equal(2, tracker.Timstamps.Count);
            Assert.Equal("1700000001.000000", tracker.Timstamps[1]);
            var updatedAsks = tracker.AskSides[1];
            Assert.Equal(new ExactFloat("2.0"), updatedAsks[new ExactFloat("61100.40000")]);
            Assert.Equal(new ExactFloat("1.0"), updatedAsks[new ExactFloat("61102.50000")]);
            var updatedBids = tracker.BidSides[1];
            Assert.Equal(new ExactFloat("2.0"), updatedBids[new ExactFloat("61033.00000")]);
            Assert.Equal(new ExactFloat("1.0"), updatedBids[new ExactFloat("61028.70000")]);
        }

        [Fact]
        public void AskAdding()
        {
            var depth = 10;
            var tracker = new TestTracker("name", depth);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = AskUpdateAddingLevels();
            tracker.NewUpdate(in update);
            Assert.Empty(tracker.MisMatches);
            Assert.Equal(2, tracker.AskSides.Count);
            Assert.Equal(2, tracker.BidSides.Count);
            Assert.Equal(2, tracker.Timstamps.Count);
            Assert.Equal(depth, tracker.AskSides[1].Count);
            Assert.Equal("1700000001.000000", tracker.Timstamps[1]);
            var updatedAsks = tracker.AskSides[1];
            Assert.Equal(new ExactFloat("2.0"), updatedAsks[new ExactFloat("61066.70000")]);
            Assert.Equal(new ExactFloat("1.0"), updatedAsks[new ExactFloat("61065.70000")]);
        }

        [Fact]
        public void BidAdding()
        {
            var depth = 10;
            var tracker = new TestTracker("name", depth);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = BidUpdateAddingLevels();
            tracker.NewUpdate(in update);
            Assert.Empty(tracker.MisMatches);
            Assert.Equal(2, tracker.AskSides.Count);
            Assert.Equal(2, tracker.BidSides.Count);
            Assert.Equal(2, tracker.Timstamps.Count);
            Assert.Equal(depth, tracker.BidSides[1].Count);
            Assert.Equal("1700000001.000000", tracker.Timstamps[1]);
            var updatedAsks = tracker.BidSides[1];
            Assert.Equal(new ExactFloat("2.0"), updatedAsks[new ExactFloat("61066.70000")]);
            Assert.Equal(new ExactFloat("1.0"), updatedAsks[new ExactFloat("61065.70000")]);
        }

        [Fact]
        public void AskDelete()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = AskDeleteUpdate();
            tracker.NewUpdate(in update);
            Assert.Empty(tracker.MisMatches);
            Assert.Equal(2, tracker.AskSides.Count);
            Assert.Equal(2, tracker.BidSides.Count);
            Assert.Equal(2, tracker.Timstamps.Count);
            Assert.Equal("1700000000.000000", tracker.Timstamps[1]);
            var updatedAsks = tracker.AskSides[1];
            Assert.False(updatedAsks.ContainsKey(new ExactFloat("61102.50000")));
        }

        [Fact]
        public void Repub()
        {
            var tracker = new TestTracker("name", 10);
            var snapshot = InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var update = AskRepublishUpdate();
            tracker.NewUpdate(in update);
            Assert.Empty(tracker.MisMatches);
            Assert.Equal(2, tracker.AskSides.Count);
            Assert.Equal(2, tracker.BidSides.Count);
            Assert.Equal(2, tracker.Timstamps.Count);
            Assert.Equal("1700000000.000000", tracker.Timstamps[1]);
            var updatedAsks = tracker.AskSides[1];
            Assert.Equal(new ExactFloat("2.0"), updatedAsks[new ExactFloat("61100.40000")]);
            Assert.Equal(new ExactFloat("1.0"), updatedAsks[new ExactFloat("61102.50000")]);
        }
    }

    public class TestSyncDictDelegator
    {
        [Fact]
        public void AskMismatch()
        {
            var internalTrackers = new Dictionary<string, TestTracker>();
            var snapshotRequests = new List<string>();
            var tracker = new SyncDictDelegator<TestTracker>(
                (s, i) =>
                {
                    var internalTracker = new TestTracker(s, i);
                    internalTrackers[s] = internalTracker;
                    return internalTracker;
                }
            );
            tracker.RaiseSnapshotRequest += (_, pair) => snapshotRequests.Add(pair);
            var snapshot = TrackerTests.InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var xbtTracker = internalTrackers["XBT/USDT"];
            Assert.Single(xbtTracker.AskSides);
            Assert.Single(xbtTracker.BidSides);
            Assert.Single(xbtTracker.Timstamps);
            var update = TrackerTests.AskUpdate(false);
            tracker.NewUpdate(in update);
            Assert.Single(xbtTracker.MisMatches);
            Assert.Single(snapshotRequests);
        }        

        [Fact]
        public void AskMismatchMultiplePairs()
        {
            var internalTrackers = new Dictionary<string, TestTracker>();
            var snapshotRequests = new List<string>();
            var tracker = new SyncDictDelegator<TestTracker>(
                (s, i) =>
                {
                    var internalTracker = new TestTracker(s, i);
                    internalTrackers[s] = internalTracker;
                    return internalTracker;
                }
            );
            tracker.RaiseSnapshotRequest += (_, pair) => snapshotRequests.Add(pair);
            
            // all snapshots
            var snapshot = TrackerTests.InitialSnapshot();
            var initialSnapshot = TrackerTests.InitialSnapshot("ETH/USDT");
            tracker.NewSnapshot(in snapshot);
            tracker.NewSnapshot(in initialSnapshot);
            var xbtTracker = internalTrackers["XBT/USDT"];
            var ethTracker = internalTrackers["ETH/USDT"];
            
             // snapshots processed
            Assert.Single(xbtTracker.AskSides);
            Assert.Single(xbtTracker.BidSides);
            Assert.Single(xbtTracker.Timstamps);
            Assert.Single(ethTracker.AskSides);
            Assert.Single(ethTracker.BidSides);
            Assert.Single(ethTracker.Timstamps);
            
            // all updates
            var update = TrackerTests.AskUpdate(false);
            tracker.NewUpdate(in update);
            var ethUpdate = TrackerTests.AskUpdate(false, "ETH/USDT");
            tracker.NewUpdate(in ethUpdate);
            
            // updates processed
            Assert.Single(xbtTracker.MisMatches);
            Assert.Single(ethTracker.MisMatches);

            Assert.Equal( "XBT/USDT", snapshotRequests[0]);
            Assert.Equal( "ETH/USDT", snapshotRequests[1]);
            
            Assert.Equal(2, snapshotRequests.Count);
        }

        [Fact]
        public void Ask()
        {
            var internalTrackers = new Dictionary<string, TestTracker>();
            var snapshotRequests = new List<string>();
            var tracker = new SyncDictDelegator<TestTracker>(
                (s, i) =>
                {
                    var internalTracker = new TestTracker(s, i);
                    internalTrackers[s] = internalTracker;
                    return internalTracker;
                }
            );
            tracker.RaiseSnapshotRequest += (_, pair) => snapshotRequests.Add(pair);
            
            // all snapshots
            var snapshot = TrackerTests.InitialSnapshot();
            var initialSnapshot = TrackerTests.InitialSnapshot("ETH/USDT");
            tracker.NewSnapshot(in snapshot);
            tracker.NewSnapshot(in initialSnapshot);
            var xbtTracker = internalTrackers["XBT/USDT"];
            var ethTracker = internalTrackers["ETH/USDT"];

            // snapshots processed
            Assert.Single(xbtTracker.AskSides);
            Assert.Single(xbtTracker.BidSides);
            Assert.Single(xbtTracker.Timstamps);
            Assert.Single(ethTracker.AskSides);
            Assert.Single(ethTracker.BidSides);
            Assert.Single(ethTracker.Timstamps);
            
            // all updates
            var update = TrackerTests.AskUpdate(true);
            tracker.NewUpdate(in update);
            var ethUpdate = TrackerTests.AskUpdate(true, "ETH/USDT");
            tracker.NewUpdate(in ethUpdate);
            
            // updates processed
            foreach (var cTracker in new[]{xbtTracker, ethTracker})
            {
                Assert.Empty(cTracker.MisMatches);
                Assert.Equal(2, cTracker.AskSides.Count);
                Assert.Equal(2, cTracker.BidSides.Count);
                Assert.Equal(2, cTracker.Timstamps.Count);
                Assert.Equal("1700000001.000000", cTracker.Timstamps[1]);
                var updatedAsks = cTracker.AskSides[1];
                Assert.Equal(new ExactFloat("2.0"), updatedAsks[new ExactFloat("61100.40000")]);
                Assert.Equal(new ExactFloat("1.0"), updatedAsks[new ExactFloat("61102.50000")]);
            }
            Assert.Empty(snapshotRequests);
        }
        
        [Fact]
        public void Unsub()
        {
            var internalTrackers = new Dictionary<string, TestTracker>();
            var snapshotRequests = new List<string>();
            var tracker = new SyncDictDelegator<TestTracker>(
                (s, i) =>
                {
                    var internalTracker = new TestTracker(s, i);
                    internalTrackers[s] = internalTracker;
                    return internalTracker;
                }
            );
            tracker.RaiseSnapshotRequest += (_, pair) => snapshotRequests.Add(pair);
            var snapshot = TrackerTests.InitialSnapshot();
            tracker.NewSnapshot(in snapshot);
            var xbtTracker = internalTrackers["XBT/USDT"];
            tracker.NewUnsubscribe("XBT/USDT");
            var update = TrackerTests.AskUpdate(false);
            tracker.NewUpdate(in update);
            
            Assert.Empty(xbtTracker.MisMatches);
            Assert.Single(snapshotRequests);
            Assert.Single(xbtTracker.AskSides);
            Assert.Single(xbtTracker.BidSides);
            Assert.Single(xbtTracker.Timstamps);
        }        

    }
}