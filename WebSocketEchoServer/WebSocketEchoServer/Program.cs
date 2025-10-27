using System.Net;
using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using WebSocketEchoServer.Websocket;

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
app.Map("/ws", async (HttpContext ctx, AudioEchoWsHub hub) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        return;
    }

    // 可選：簡單 JWT 驗證（範例以 query 的 token 取 uid）
    // 實務建議用真正的 JWT 驗證中介層
    var uid = ctx.Request.Query["uid"].ToString();
    if (string.IsNullOrWhiteSpace(uid))
    {
        ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return;
    }

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await hub.AddAsync(uid, socket);

    var buffer = new byte[64 * 1024];
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ctx.RequestAborted);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await hub.HandleTextAsync(uid, msg);
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
                // 收到二進位資料（如音訊/檔案切片）
                await hub.HandleBinaryAsync(uid, buffer.AsSpan(0, result.Count).ToArray());
            }
        }
    }
    catch (OperationCanceledException) { /* client aborted */ }
    catch (WebSocketException) { /* network error */ }
    finally
    {
        await hub.RemoveAsync(uid);
        if (socket.State == WebSocketState.Open)
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ctx.RequestAborted);
    }
});

app.MapGet("/", () => "WS backend running");
await app.RunAsync();


