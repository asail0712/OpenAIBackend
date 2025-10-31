using System.Text.Json;

namespace XPlan.WebSockets
{
    public record WsEnvelope(string Type, JsonElement? Payload);
}
