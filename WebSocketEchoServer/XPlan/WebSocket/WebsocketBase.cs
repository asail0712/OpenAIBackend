using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace XPlan.WebSockets
{
    // 連線管理 + 簡單路由

    public class WebsocketBase
    {
        private readonly ConcurrentDictionary<string, WebSocket> _peers = new();

        virtual public Task AddAsync(string uid, WebSocket ws, CancellationToken ct = default)
        {
            _peers[uid] = ws;
            return SendAsync(ws, new { type = "welcome", payload = new { uid } });
        }

        virtual public Task RemoveAsync(string uid)
        {
            _peers.TryRemove(uid, out _);
            return Task.CompletedTask;
        }

        virtual public Task HandleTextAsync(string fromUid, string json)
        {
            return Task.CompletedTask;
        }

        virtual public Task HandleBinaryAsync(string fromUid, byte[] bytes)
        {
            return Task.CompletedTask;
        }

        protected async Task BroadcastAsync(object obj)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
            foreach (var (_, ws) in _peers)
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, default);
        }

        protected async Task BroadcastBinaryAsync(byte[] bytes)
        {
            foreach (var (_, ws) in _peers)
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(bytes, WebSocketMessageType.Binary, true, default);
        }

        protected Task SendAsync(WebSocket ws, object obj)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj));
            return ws.SendAsync(bytes, WebSocketMessageType.Text, true, default);
        }

        protected Task SendBinaryAsync(WebSocket ws, byte[] bytes)
        {
            return ws.SendAsync(bytes, WebSocketMessageType.Binary, true, default);
        }

        protected bool TryGetValue(string fromUid, out WebSocket ws)
        {
            return _peers.TryGetValue(fromUid, out ws);
        }
    }
}
