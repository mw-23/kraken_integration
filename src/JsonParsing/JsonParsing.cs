#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace JsonParsing
{
    // used for parsing kraken websocket messages
    public record Subscription(string name, int? depth);

    // used for parsing kraken websocket messages
    public record SubscriptionStatus(int channelID, string channelName, string @event, string pair, string status,
        Subscription subscription, string errorMessage);

    // used for parsing kraken websocket messages
    public record SystemStatus(ulong connectionID, string @event, string status, string version);

    // represents orderbook level (or update to orderbook level)
    public record LevelTuple
    {
        public readonly string PriceLevel;
        public readonly string Volume;
        public readonly string Timestamp;
        public readonly string UpdateType;

        public LevelTuple(IReadOnlyList<string> update)
        {
            var count = update.Count;
            if (count < 3 || 4 < count) throw new ArgumentException("invalid length of argument");
            PriceLevel = update[0];
            Volume = update[1];
            Timestamp = update[2];
            if (count == 3)
            {
                UpdateType = "";
                return;
            }

            UpdateType = update[3];
        }
    }

    // record-like: implements custom (sequence) equality on immutable reference type
    public class Levels : IEquatable<Levels>
    {
        public readonly ImmutableArray<LevelTuple> Tuples;

        public Levels(IEnumerable<LevelTuple> tuples)
        {
            Tuples = ImmutableArray.Create(tuples.ToArray());
        }

        public bool Equals(Levels? other)
        {
            if (ReferenceEquals(null, other)) return false;
            return ReferenceEquals(this, other) || Tuples.SequenceEqual(other.Tuples);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((Levels) obj);
        }

        // quick-and-dirty
        public override int GetHashCode()
        {
            return (Tuples.ToString() ?? "").GetHashCode();
        }
    }

    // represents either a full snapshot or an update to the orderbook
    public record Book
    {
        public readonly ulong ChannelId;
        public readonly string ChannelName;
        public readonly string Pair;
        public readonly Levels Asks;
        public readonly Levels Bids;
        public readonly string Checksum;
        public readonly bool Update;

        public Book(ulong channelId, string channelName, string pair, IEnumerable<LevelTuple> asks,
            IEnumerable<LevelTuple> bids,
            string checksum, bool update)
        {
            ChannelId = channelId;
            ChannelName = channelName;
            Pair = pair;
            Asks = new Levels(asks);
            Bids = new Levels(bids);
            Checksum = checksum;
            Update = update;
        }


        // Combined snapshot and update parsing
        public Book(string jsonText)
        {
            var json = JsonSerializer.Deserialize<JsonElement>(jsonText);
            JsonElement bids = default;
            var okChId = json[0].TryGetUInt64(out var channelId);
            if (!okChId) throw new InvalidOperationException();

            var updateAsks = json[1].TryGetProperty("a", out var asks);
            var snapAsks = false;
            if (!updateAsks)
            {
                snapAsks = json[1].TryGetProperty("as", out asks);
            }

            // surprisingly sufficient, if we have asks, check json[2] for bids. If not, json[2] will be string.
            var updateBids = updateAsks && json[2].ValueKind == JsonValueKind.Object
                ? json[2].TryGetProperty("b", out bids)
                : !snapAsks && json[1].TryGetProperty("b", out bids);

            var snapBids = false;
            if (!updateBids)
            {
                snapBids = json[1].TryGetProperty("bs", out bids);
            }

            var okAsks = snapAsks || updateAsks;
            var okBids = snapBids || updateBids;
            var bothSidesUpdate = updateAsks && updateBids;
            string checksum = (snapAsks || snapBids
                    ? ""
                    : bothSidesUpdate
                        ? json[2].GetProperty("c").GetString()
                        : json[1].GetProperty("c").GetString()
                ) ?? throw new InvalidOperationException();

            var a = okAsks
                ? asks.EnumerateArray()
                    .Select(x =>
                        new LevelTuple(JsonSerializer.Deserialize<List<string>>(x.GetRawText()) ??
                                       throw new InvalidOperationException()) ??
                        throw new InvalidOperationException())
                : Enumerable.Empty<LevelTuple>();
            var b = okBids
                ? bids.EnumerateArray()
                    .Select(x =>
                        new LevelTuple(JsonSerializer.Deserialize<List<string>>(x.GetRawText()) ??
                                       throw new InvalidOperationException()) ??
                        throw new InvalidOperationException())
                : Enumerable.Empty<LevelTuple>();

            var channelName = json[2 + (bothSidesUpdate ? 1 : 0)].GetString() ?? throw new InvalidOperationException();
            var pair = json[3 + (bothSidesUpdate ? 1 : 0)].GetString() ?? throw new InvalidOperationException();

            ChannelId = channelId;
            ChannelName = channelName;
            Pair = pair;
            Asks = new Levels(a);
            Bids = new Levels(b);
            Checksum = checksum;
            Update = updateAsks || updateBids;
        }
    }
}