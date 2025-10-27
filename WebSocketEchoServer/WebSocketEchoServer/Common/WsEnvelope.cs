using System.Text.Json;

namespace WebSocketEchoServer.Common
{
    public record WsEnvelope(string type, JsonElement? payload);
}
