using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

using XPlan.WebSockets;

namespace WebSocketEchoServer.Websocket
{
    public static class AudioMsgTypes
    {
        public const string Start               = "audio.Start";            // server -> client
        public const string Finish              = "audio.Finish";           // server -> client
        public const string Logging             = "audio.Logging";           // server -> client

        public const string Send                = "audio.Send";             // client -> server
        public const string InterruptReceive    = "audio.InterruptReceive"; // client -> server
        public const string ReceiveAudio        = "audio.ReceiveAudio";     // server -> client
        public const string ReceiveText         = "audio.ReceiveText";      // server -> client
    }

    public class AIMessage
    {
        public string Type { get; set; }        = string.Empty;

        // Base64 編碼的 PCM16 音訊資料 或是文字資料
        public string Payload { get; set; }     = string.Empty;
    }

    public class OpenAIProxyOptions
    {
        public string ApiKey { get; set; }              = "";
        public string Model { get; set; }               = "gpt-4o-mini-realtime-preview";
        public string Voice { get; set; }               = "alloy";
        public string BasicInstructions { get; set; }   = "You are a helpful, concise voice assistant.";
        public bool AutoCreate { get; set; }            = false;
        public CancellationToken ct { get; set; }
    }

    /// <summary>
    /// 客戶端一連線就自動接到 OpenAI Realtime，
    /// 並把 client 的文字/音訊送給 OpenAI，把 OpenAI 的文字/音訊回來再回推給 client。
    /// </summary>
    public class OpenAIProxyWsHub : WebsocketBase
    {
        private readonly OpenAIProxyOptions _opt;
        private readonly CancellationToken _ct;
        private readonly ConcurrentDictionary<string, OpenAIRealtime> _rtByUid = new();

        public OpenAIProxyWsHub(OpenAIProxyOptions opt)
        {
            _opt    = opt;
            _ct     = opt.ct;
        }

        /// <summary>
        /// 外部路由呼叫：註冊 client socket，建立上游 OpenAIRealtime，然後交給基底的接收迴圈（HandleText/HandleBinary）處理。
        /// </summary>
        override public async Task AddAsync(string uid, WebSocket clientSocket)
        {
            if (_rtByUid.TryRemove(uid, out var oldRt))
            {
                try { oldRt.Dispose(); } catch { }
            }

            // 1) 註冊 client
            await base.AddAsync(uid, clientSocket);

            // 2) 建立 & 連線 OpenAIRealtime（和 client 同壽終）
            var rt = new OpenAIRealtime(
                openAIApiKey:           _opt.ApiKey,
                model:                  _opt.Model,
                voice:                  _opt.Voice,
                basicInstructions:      _opt.BasicInstructions,
                bAutoCreateResponse:    _opt.AutoCreate,
                bEventAsync:            false
            );

            // 綁定回傳事件：把 OpenAI 的輸出回推 client
            rt.OnResposeStart       += () =>
            {
                TrySend(uid, new 
                { 
                    Type = AudioMsgTypes.Start,
                });
            };
            rt.OnResposeFinish      += () =>
            {
                TrySend(uid, new 
                {
                    Type = AudioMsgTypes.Finish,
                });
            };
            rt.OnAssistantTextDelta += (txt) =>
            {
                TrySend(uid, new 
                { 
                    Type    = AudioMsgTypes.ReceiveText, 
                    Payload = txt
                });
            };
            //rt.OnAssistantTextDone  += (txt) => TrySend(uid, new { type = "assistant.text.done", payload = new { text = txt } });

            // 音訊：以 binary（raw PCM16）回推給 client；若你偏好 base64，也可以改成文字訊息
            rt.OnAssistantAudioDelta    += (bytes) =>
            {
                TrySend(uid, new 
                { 
                    Type        = AudioMsgTypes.ReceiveAudio,
                    Payload     = Convert.ToBase64String(bytes),
                });
            };
            //rt.OnAssistantAudioDone     += (_) => TrySend(uid, new { type = "assistant.audio.done" });

            // 伺服器端除錯訊息
            rt.OnLoggingDone += (lvl, msg) =>
            {
                TrySend(uid, new
                {
                    Type    = AudioMsgTypes.Logging,
                    Payload = $"[{lvl.ToString()}] {msg}"
                });
            };

            // 3) 連線 OpenAI Realtime（綁定相同取消權）
            var ok = await rt.ConnectAndConfigure(_ct);
            if (!ok)
            {
                TrySend(uid, new { Type = "error", Payload = "openai_connect_failed" });
                await Cleanup(uid, rt);
                return;
            }

            _rtByUid[uid] = rt;
        }

        public override Task RemoveAsync(string uid)
        {
            Task task = base.RemoveAsync(uid);

            // 關閉並移除 RT
            if (_rtByUid.TryRemove(uid, out var rt))
            {
                try { rt.Dispose(); } catch { /* log if needed */ }
            }

            return task;
        }

        private async Task Cleanup(string uid, OpenAIRealtime rt)
        {
            _rtByUid.TryRemove(uid, out _);
            try { rt.Dispose(); } catch { }
            try { await RemoveAsync(uid); } catch { }
        }

        private void TrySend(string uid, object obj)
        {
            if (TryGetValue(uid, out var ws) && ws.State == WebSocketState.Open)
                _ = SendAsync(ws, obj);
        }

        // -----------------------------------------
        // Client 傳上來的訊息
        // -----------------------------------------
        public override async Task HandleTextAsync(string fromUid, string json)
        {
            if (!_rtByUid.TryGetValue(fromUid, out var rt) || rt is null || !rt.IsConnected())
            {
                TrySend(fromUid, new { Type = "error", Payload = "realtime_not_ready" } );
                return;
            }

            AIMessage? env;
            try { env = JsonSerializer.Deserialize<AIMessage>(json); }
            catch
            {
                if (TryGetValue(fromUid, out var ws))
                    await SendAsync(ws, new { Type = "error", Payload = "invalid_json" } );
                return;
            }

            if (env is null || string.IsNullOrWhiteSpace(env.Type)) return;

            switch (env?.Type)
            {
                // 用戶傳純文字 -> 要求助理生成（text + audio）
                case AudioMsgTypes.Send:
                    {
                        if (string.IsNullOrWhiteSpace(env.Payload))
                        {
                            return;
                        }

                        await rt.SendAudioBase64Async(env?.Payload!);
                        break;
                    }
                case AudioMsgTypes.InterruptReceive:
                    {
                        await rt.BargeInAsync(0f);
                        break;
                    }
            }
        }
    }
}
