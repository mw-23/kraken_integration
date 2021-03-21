using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Core;
using OrderbookTracking;
using OrderbookTracking.GlobalTrackerImplementations;
using Websocket;

namespace cli
{
    using OrderbookSide = SortedDictionary<ExactFloat, ExactFloat>;

    // example implementation
    public class PrintingTracker : TrackerBase
    {
        public PrintingTracker(string name, int depth) : base(name, depth)
        {
            RaiseChecksumMismatch += (_, mismatch) => Console.WriteLine($"checksum mismatch: name: {mismatch.Name}, " +
                                                                        $"expected: {mismatch.ExpectedChecksum}, " +
                                                                        $"actual: {mismatch.ActualChecksum}");
        }

        private static string ExactFloatToString(ExactFloat f)
        {
            return (f.Integer * Math.Pow(10, -1 * f.NegativeExponent)).ToString(CultureInfo.InvariantCulture);
        }

        protected override void OnBookAvailable(string timestamp, in OrderbookSide askSide, in OrderbookSide bidSide)
        {
            var exitTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds() / 1000.0;
            var (askKey, askValue) = askSide.ElementAt(0);
            var (bidKey, bidValue) = bidSide.ElementAt(bidSide.Count - 1);
            Console.WriteLine($"pair: {Name} " +
                              $"latency: {exitTimestamp - double.Parse(timestamp):F6}, " +
                              $"best ask: price: {ExactFloatToString(askKey)} " +
                              $"amount: {ExactFloatToString(askValue)}, " +
                              $"best bid: price: {ExactFloatToString(bidKey)} amount: {ExactFloatToString(bidValue)}");
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("cli running");
            var tracker = new SyncDictDelegator<PrintingTracker>(
                (pair, depth) => new PrintingTracker(pair, depth)
            );
            var conn = new KrakenConnection(tracker, (uri, name) => new WsClient(uri, name));
            Console.WriteLine("cli: sub to XBT/USDT");
            conn.Subscribe("XBT/USDT");
            conn.Run();
            Thread.Sleep(3000);
            Console.WriteLine("cli: sub to ETH/USDT");
            conn.Subscribe("ETH/USDT");
            Thread.Sleep(3000);
            Console.WriteLine("cli: unsub from XBT/USDT");
            conn.Unsubscribe("XBT/USDT");
            Thread.Sleep(2000);
            Console.WriteLine("bye :)");
            conn.Stop().Wait();
        }
    }
}