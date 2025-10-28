using System.Net;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using WebSocketEchoServer.Websocket;

using XPlan.WebSockets;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<AudioEchoWsHub>();

var app = builder.Build();

// 1) 啟用 WebSockets，設置保活間隔與收包緩衝
var wsOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
    ReceiveBufferSize = 64 * 1024
};
// 可選：限制允許的 Origin
// wsOptions.AllowedOrigins.Add("https://your-frontend.example.com");

app.UseWebSockets(wsOptions);

// 2) WebSocket 端點：/ws
app.Map<AudioEchoWsHub>("/ws");
app.MapGet("/", () => "WS backend running");

await app.RunAsync();


