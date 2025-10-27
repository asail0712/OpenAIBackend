using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WebSocketEchoServer.Common;

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
                    await SendAsync(ws, new { type = "error", payload = new { message = "invalid_json" } });
                return;
            }

            switch (env?.type)
            {
                case "echo":
                    if (TryGetValue(fromUid, out var ws))
                        await SendAsync(ws, new { type = "echo", env.payload });
                    break;

                case "broadcast":
                    await BroadcastAsync(new { type = "broadcast", payload = new { from = fromUid, data = env.payload } });
                    break;

                case "dm":
                    // 直接訊息：payload 需包含 to, data
                    if (env.payload is JsonElement p &&
                        p.TryGetProperty("to", out var to) &&
                        p.TryGetProperty("data", out var data))
                    {
                        var toUid = to.GetString();
                        if (!string.IsNullOrEmpty(toUid) && TryGetValue(toUid, out var dst))
                            await SendAsync(dst, new { type = "dm", payload = new { from = fromUid, data } });
                    }
                    break;

                default:
                    if (TryGetValue(fromUid, out var ws2))
                        await SendAsync(ws2, new { type = "error", payload = new { message = "unknown_type" } });
                    break;
            }
        }

        override public Task HandleBinaryAsync(string fromUid, byte[] bytes)
        {
            // 範例：把二進位資料回送或轉發（如音訊流）
            return BroadcastBinaryAsync(bytes);
        }
    }
}
