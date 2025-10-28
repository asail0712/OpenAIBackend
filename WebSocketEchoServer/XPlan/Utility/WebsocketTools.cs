using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace XPlan.WebSockets
{
    static public class WebsocketTools
    {
        static public RouteHandlerBuilder Map<T>(this WebApplication app, string path) where T : WebsocketBase
        {
            return app.Map(path, async (HttpContext ctx, T hub) =>
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
        }
    }
}
