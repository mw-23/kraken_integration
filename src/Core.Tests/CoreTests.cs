using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using JsonParsing;
using Prime.Services;
using Xunit;

namespace Core.Tests
{
    public class ConnectionTests
    {
        public class globalTestTracker : IGlobalTracker
        {
            public readonly List<Book> newSnapshots = new();
            public readonly List<Book> newUpdates = new();
            public readonly List<string> newUnsubscribes = new();
            public event EventHandler<string> RaiseSnapshotRequest;

            public void NewSnapshot(in Book book)
            {
                newSnapshots.Add(book);
            }

            public void NewUpdate(in Book book)
            {
                newUpdates.Add(book);
            }

            public void NewUnsubscribe(string pair)
            {
                newUnsubscribes.Add(pair);
            }
        }

        public class TestWebocket : IWebsocket
        {
            public HashSet<string> sendMsgs = new();
            public readonly Subject<string> ReconnectionHappenedSubject = new();
            public readonly Subject<string> DisconnectionHappenedSubject = new();
            public readonly Subject<string> MessageReceivedSubject = new();

            public void Start()
            {
            }

            public void Send(string msg)
            {
                sendMsgs.Add(msg);
            }

            public Task<bool> Stop()
            {
                throw new NotImplementedException();
            }

            public IObservable<string> ReconnectionHappened => ReconnectionHappenedSubject;

            public IObservable<string> DisconnectionHappened => DisconnectionHappenedSubject;

            public IObservable<string> MessageReceived => MessageReceivedSubject;
        }

        [Fact]
        // incomplete tests
        public void Sending()
        {
            var tracker = new globalTestTracker();

            var socket = new TestWebocket();
            var conn = new KrakenConnection(tracker, (_, _) => socket);
            conn.Subscribe("a");
            conn.Run();
            conn.Subscribe("b");
            Thread.Sleep(2000);
            var success = conn.Stop();
            success.Wait();
            Assert.True(success.Result);
            Assert.Contains("a", socket.sendMsgs);
            Assert.Contains("b", socket.sendMsgs);
        }        
        
        [Fact]
        public void Recv()
        {
            Assert.True(true);
            // var tracker = new globalTestTracker();
            // var socket = new TestWebocket();
            // var conn = new KrakenConnection(tracker, (_, _) => socket);
            // conn.Subscribe("a");
            // conn.Subscribe("b");
            // conn.Run();
            
            // // send messages and ensure they get relayed properly to the tracker 
            
            // socket.MessageReceivedSubject.OnNext();
        }
    }
}