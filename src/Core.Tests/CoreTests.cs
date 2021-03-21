using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using JsonParsing;
using Xunit;

namespace Core.Tests
{
    public class ConnectionTests
    {
        public class GlobalTestTracker : IGlobalTracker
        {
            public readonly List<Book> NewSnapshots = new();
            public readonly List<Book> NewUpdates = new();
            public readonly List<string> NewUnsubscribes = new();
            public event EventHandler<string> RaiseSnapshotRequest;

            public void NewSnapshot(in Book book)
            {
                NewSnapshots.Add(book);
            }

            public void NewUpdate(in Book book)
            {
                NewUpdates.Add(book);
            }

            public void NewUnsubscribe(string pair)
            {
                NewUnsubscribes.Add(pair);
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

            public async Task<bool> Stop()
            {
                return await Task.Run(() => true);
            }

            public IObservable<string> ReconnectionHappened => ReconnectionHappenedSubject;

            public IObservable<string> DisconnectionHappened => DisconnectionHappenedSubject;

            public IObservable<string> MessageReceived => MessageReceivedSubject;
        }

        [Fact]
        // incomplete tests
        public void Sending()
        {
            var tracker = new GlobalTestTracker();

            var socket = new TestWebocket();
            var conn = new KrakenConnection(tracker, (_, _) => socket);
            conn.Subscribe("a");
            conn.Run();
            conn.Subscribe("b");
            Thread.Sleep(2000);
            var success = conn.Stop();
            success.Wait();
            Assert.True(success.Result);
            Assert.Equal(2, socket.sendMsgs.Count);
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