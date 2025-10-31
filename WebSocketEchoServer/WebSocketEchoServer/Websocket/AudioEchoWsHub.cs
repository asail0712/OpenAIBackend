using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using XPlan.WebSockets;

namespace WebSocketEchoServer.Websocket
{
    public class AudioEchoWsHub : WebsocketBase
    {
        override public async Task HandleTextAsync(string fromUid, string json)
        {
            WsEnvelope? env;
            try
            {
                env = JsonSerializer.Deserialize<WsEnvelope>(json);
            }
            catch
            {
                if (TryGetValue(fromUid, out var ws))
                    await SendAsync(ws, new { Type = "error", Payload = new { message = "invalid_json" } });
                return;
            }

            switch (env?.Type)
            {
                case "echo":
                    if (TryGetValue(fromUid, out var ws))
                        await SendAsync(ws, new { Type = "echo", env.Payload });
                    break;

                case "broadcast":
                    await BroadcastAsync(new { Type = "broadcast", Payload = new { from = fromUid, data = env.Payload } });
                    break;

                case "dm":
                    // 直接訊息：payload 需包含 to, data
                    if (env.Payload is JsonElement p &&
                        p.TryGetProperty("to", out var to) &&
                        p.TryGetProperty("data", out var data))
                    {
                        var toUid = to.GetString();
                        if (!string.IsNullOrEmpty(toUid) && TryGetValue(toUid, out var dst))
                            await SendAsync(dst, new { Type = "dm", Payload = new { from = fromUid, data } });
                    }
                    break;

                default:
                    if (TryGetValue(fromUid, out var ws2))
                        await SendAsync(ws2, new { Type = "error", Payload = new { message = "unknown_type" } });
                    break;
            }
        }

        override public async Task HandleBinaryAsync(string fromUid, byte[] bytes)
        {
            if (TryGetValue(fromUid, out var ws) && ws.State == WebSocketState.Open)
            {
                await SendBinaryAsync(ws, bytes); // 只送一包 Binary
            }                
        }
    }
}
