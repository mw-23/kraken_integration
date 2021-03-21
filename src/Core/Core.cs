#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JsonParsing;

namespace Core
{
    // very simple helper class
    public class SimpleConcurrentHashset<T>
    {
        private readonly object _lock = new();
        private readonly HashSet<T> _set = new();

        public bool Add(T item)
        {
            lock (_lock)
            {
                return _set.Add(item);
            }
        }

        public bool Remove(T item)
        {
            lock (_lock)
            {
                return _set.Remove(item);
            }
        }

        public HashSet<T> ShallowCopy()
        {
            lock (_lock)
            {
                return new HashSet<T>(_set);
            }
        }

        public bool Contains(T item)
        {
            lock (_lock)
            {
                return _set.Contains(item);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _set.Clear();
            }
        }
    }


    // Interface required for tracking orderbooks across multiple pairs. Allows for easy vendor switching and testing.
    public interface IGlobalTracker
    {
        public event EventHandler<string> RaiseSnapshotRequest;
        void NewSnapshot(in Book book);
        void NewUpdate(in Book book);
        void NewUnsubscribe(string pair);
    }

    // Interface required for connecting to exhcange. Allows for easy vendor switching and testing.
    public interface IWebsocket
    {
        public void Start();
        public void Send(string msg);
        public Task<bool> Stop();
        public IObservable<string> ReconnectionHappened { get; }
        public IObservable<string> DisconnectionHappened { get; }
        public IObservable<string> MessageReceived { get; }
    }

    // main interface to connecting to Kraken. Only implements tracking orderbooks, not making orders.
    public class KrakenConnection
    {
        // internally used to determine how to go about parsing received JSON
        private static class KrakenMsgIdentifiers
        {
            public const string SubscriptionStatusEvent = "\"event\":\"subscriptionStatus\"";
            public const string SystemStatusEvent = "\"event\":\"systemStatus\"";
            public const string HeartBeatEvent = "{\"event\":\"heartbeat\"}";
        }

        // internally used to map business logic to JSON requests
        private enum SubscriptionEvent
        {
            Subscribe,
            Unsubscribe,
        }


        // internally used to create JSON requests
        private class SubscriptionEventMsg
        {
            public readonly string Name;

            [JsonPropertyName("event")] [JsonInclude]
            public readonly SubscriptionEvent Event;

            [JsonPropertyName("pair")] [JsonInclude]
            public readonly List<string> Pair;

            [JsonPropertyName("subscription")] [JsonInclude]
            public Dictionary<string, object> Subscription;

            public SubscriptionEventMsg(SubscriptionEvent subEvent, string pair, int depth)
            {
                Event = subEvent;
                Name = pair;
                Pair = new List<string> {pair};
                Subscription = new Dictionary<string, object> {{"name", "book"}, {"depth", depth}};
            }
        }

        // internally used to create JSON requests
        private class LowerCaseNamingPolicy : JsonNamingPolicy
        {
            public override string ConvertName(string name) =>
                name.ToLower();
        }

        // fields
        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            Converters =
            {
                new JsonStringEnumConverter(new LowerCaseNamingPolicy())
            }
        };

        private readonly IWebsocket _client;
        private readonly SimpleConcurrentHashset<string> _desiredSubscriptions = new();

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _subCancellationTokens =
            new();

        private readonly int _depth;

        // very large constructor, containing all major logic
        public KrakenConnection(IGlobalTracker tracker, Func<Uri, string, IWebsocket> websocketFactory, int depth = 10)
        {
            _client = websocketFactory(new Uri("wss://ws.kraken.com"), "Kraken");
            _depth = depth;
            tracker.RaiseSnapshotRequest += (_, pair) =>
                _runSubscriptionEventLoop(new SubscriptionEventMsg(SubscriptionEvent.Subscribe, pair, _depth));


            _client.ReconnectionHappened.Subscribe(type =>
            {
                // Log.Information($"Reconnection happened, type: {type}, url: {_client.Url}");
                foreach (var name in _desiredSubscriptions.ShallowCopy())
                {
                    _runSubscriptionEventLoop(new SubscriptionEventMsg(SubscriptionEvent.Subscribe, name, _depth));
                }
            });
            _client.DisconnectionHappened.Subscribe(info =>
            {
                // _logger.Warning($"Disconnection happened, type: {info}")
            });

            _client.MessageReceived.Subscribe(msg =>
            {
                // Console.WriteLine(msg);
                try
                {
                    if (msg.StartsWith("[")) // most messages will be updates
                    {
                        var book = new Book(msg);
                        if (book.Update) // most messages will be updates
                        {
                            tracker.NewUpdate(book);
                        }
                        else if (_desiredSubscriptions.Contains(book.Pair)) // it's a snapshot
                        {
                            tracker.NewSnapshot(book);
                        }
                        else // it's a snapshot, but we didn't want it, so ensure we're actually unsubscribed
                        {
                            _runSubscriptionEventLoop(new SubscriptionEventMsg(
                                SubscriptionEvent.Unsubscribe,
                                book.Pair,
                                _depth)
                            );
                            tracker.NewUnsubscribe(book.Pair);
                        }
                    }
                    // not a book, so parsing time
                    else if (msg.Contains(KrakenMsgIdentifiers.SystemStatusEvent)) 
                    {
                        var obj = JsonSerializer.Deserialize<SystemStatus>(msg);
                        if (obj is null) throw new NullReferenceException();
                        // _logger.trace($"SystemStatusEvent: {obj}");
                    }
                    else if (msg.Contains(KrakenMsgIdentifiers.SubscriptionStatusEvent))
                    {
                        var subscriptionStatus = JsonSerializer.Deserialize<SubscriptionStatus>(msg);
                        if (subscriptionStatus is null) throw new NullReferenceException();
                        if (subscriptionStatus.status != "subscribed") return;
                        // _logger.trace($"SubscriptionStatusEvent: {subscriptionStatus.pair}");
                        _subCancellationTokens.TryRemove(subscriptionStatus.pair, out var source);
                        source?.Cancel();

                    }
                    else
                        switch (msg)
                        {
                            case KrakenMsgIdentifiers.HeartBeatEvent:
                                // _logger.trace(responses.KrakenMsgIdentifiers.HeartBeatEvent);
                                break;
                            case "close":
                                // _logger.debug("closing")
                                break;
                            
                            // unknown message
                            default:
                                throw new Exception($"unexpected msg: {msg}");
                        }
                }
                catch (Exception e) // entirely unable to parse message
                {
                    Console.WriteLine(e);
                    throw;
                }
            });
        }

        // send messages in a loop, until cancelled, key element for ensuring eventual consistency
        private async void _subEvent_loop(SubscriptionEventMsg subEvent, CancellationToken token)
        {
            var msg = JsonSerializer.Serialize(subEvent, JsonSerializerOptions);
            while (!token.IsCancellationRequested)
            {
                _client.Send(msg);
                try
                {
                    await Task.Delay(1000, token);
                }
                catch (Exception e)
                {
                    if (e is not TaskCanceledException && e is not ObjectDisposedException) throw;
                    return;
                }
            }
        }

        // send messages in background, until cancelled
        private void _runSubscriptionEventLoop(SubscriptionEventMsg subEvent)
        {
            var src = _subCancellationTokens.GetOrAdd(subEvent.Name,
                (key) => new CancellationTokenSource());
            Task.Run(() => _subEvent_loop(subEvent, src.Token), src.Token);
        }

        // shared method for handling desired state. Helps ensuring eventual conistency
        private void _subEvent(SubscriptionEventMsg subEvent)
        {
            var somethingChanged = subEvent.Event switch
            {
                SubscriptionEvent.Subscribe => _desiredSubscriptions.Add(subEvent.Name),
                SubscriptionEvent.Unsubscribe => _desiredSubscriptions.Remove(subEvent.Name),
                _ => throw new ArgumentOutOfRangeException(),
            };
            if (somethingChanged) _runSubscriptionEventLoop(subEvent);
        }

        // subscribe to orderbook updates on pair
        public void Subscribe(string name)
        {
            _subEvent(new SubscriptionEventMsg(SubscriptionEvent.Subscribe, name, _depth));
        }

        // unsubscribe from orderbook updates on pair
        public void Unsubscribe(string name)
        {
            _subEvent(new SubscriptionEventMsg(SubscriptionEvent.Unsubscribe, name, _depth));
        }

        // Stop background tasks, reset state and close connection to the exchange
        public async Task<bool> Stop()
        {
            _desiredSubscriptions.Clear();
            foreach (var source in _subCancellationTokens.Values)
            {
                source.Cancel();
            }

            return await _client.Stop();
        }

        // Connect to the exchange
        public void Run()
        {
            Task.Run(() => _client.Start());
        }
    }
}