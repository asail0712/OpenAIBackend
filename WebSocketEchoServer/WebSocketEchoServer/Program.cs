using System.Net;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using WebSocketEchoServer.Websocket;

using XPlan.WebSockets;

var builder = WebApplication.CreateBuilder(args);

// 從環境變數或 appsettings.json 取 API Key 與 Realtime 參數
// 環境變數：OPENAI_API_KEY、OPENAI_REALTIME_MODEL、OPENAI_REALTIME_VOICE、OPENAI_RT_INSTRUCTIONS
var apiKey          = builder.Configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
var model           = builder.Configuration["OPENAI_REALTIME_MODEL"] ?? "gpt-4o-mini-realtime-preview";
var voice           = builder.Configuration["OPENAI_REALTIME_VOICE"] ?? "alloy";
var instructions    = builder.Configuration["OPENAI_RT_INSTRUCTIONS"] ?? "You are a helpful, concise voice assistant.";
var autoCreateRes   = bool.TryParse(builder.Configuration["OPENAI_RT_AUTO_CREATE"], out var b) && b;

builder.Services.AddSingleton(new OpenAIProxyOptions
{
    ApiKey              = apiKey,
    Model               = model,
    Voice               = voice,
    BasicInstructions   = instructions,
    AutoCreate          = autoCreateRes
});

// 你的 WsHub（繼承 WebsocketBase）
builder.Services.AddSingleton<OpenAIProxyWsHub>();

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
app.Map<OpenAIProxyWsHub>("/ws");
app.MapGet("/", () => "WS backend running");

await app.RunAsync();


