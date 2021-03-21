using System;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Core;
using Websocket.Client;

namespace Websocket
{
    // vendor specific implementation
    public class WsClient : IWebsocket
    {
        private WebsocketClient _client;

        public WsClient(Uri uri, string name)
        {
            _client = new WebsocketClient(uri);
            _client.Name = name;
            _client.ReconnectTimeout = TimeSpan.FromSeconds(3);
            _client.ErrorReconnectTimeout = TimeSpan.FromSeconds(3);
        }

        public void Start()
        {
            _client.Start();
        }

        public void Send(string msg)
        {
            _client.Send(msg);
        }

        public IObservable<string> ReconnectionHappened =>
            _client.ReconnectionHappened.Select(x => $"reconnection type: {x.Type}");

        public IObservable<string> DisconnectionHappened =>
            _client.DisconnectionHappened.Select(x => $"disconnect type: {x.Type}");

        public IObservable<string> MessageReceived =>
            _client.MessageReceived.Select(msg =>
                msg.MessageType switch
                {
                    WebSocketMessageType.Text => msg.Text,
                    WebSocketMessageType.Binary => System.Text.Encoding.UTF8.GetString(msg.Binary),
                    WebSocketMessageType.Close => "close",
                    _ => throw new ArgumentOutOfRangeException(null),
                }
            );

        public Task<bool> Stop()
        {
            return _client.Stop(WebSocketCloseStatus.Empty, "manually stopped");
        }
    }
}