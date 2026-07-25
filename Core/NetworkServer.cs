using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LanRemoteControl.Core
{
    // ============================================================
    //  被控端 TCP 服务器
    //  - 监听端口 7321
    //  - 接受远控端连接
    //  - 每个客户端启动独立的捕获/推送循环（10 FPS）
    //  - 协议：
    //      文本指令：MODE|<mode>|<WxH>\n  /  PING\n
    //      帧推送  ：FRAME|<size>|<WxH>\n  + <size> 字节 JPEG
    // ============================================================
    public static class NetworkServer
    {
        public static event Action<string, string>? ClientConnected;
        public static event Action<string>? ClientDisconnected;
        public static event Action<string, string>? ModeChanged;

        private static TcpListener? _listener;
        private static readonly Dictionary<string, ClientSession> _sessions = new();
        private static readonly object _lock = new();
        private static CancellationTokenSource? _cts;

        public static async Task StartAsync(int port)
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        _ = HandleClientAsync(client);
                    }
                    catch { break; }
                }
            }, _cts.Token);
        }

        public static void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            lock (_lock)
            {
                foreach (var s in _sessions.Values)
                {
                    try { s.CaptureCts?.Cancel(); s.Client.Close(); } catch { }
                }
                _sessions.Clear();
            }
        }

        public static void KickClient(string ip)
        {
            ClientSession? session;
            lock (_lock)
            {
                _sessions.TryGetValue(ip, out session);
                _sessions.Remove(ip);
            }
            if (session != null)
            {
                try { session.CaptureCts?.Cancel(); session.Client.Close(); } catch { }
                ClientDisconnected?.Invoke(ip);
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            var ep = (IPEndPoint)client.Client.RemoteEndPoint!;
            var ip = ep.Address.ToString();
            var initialMode = "Control";

            var session = new ClientSession { Client = client, Mode = initialMode, CaptureCts = new CancellationTokenSource() };
            lock (_lock) { _sessions[ip] = session; }
            ClientConnected?.Invoke(ip, initialMode);

            // 启动画面推送循环（仅 Control/View 模式）
            _ = Task.Run(() => FrameLoopAsync(session), session.CaptureCts!.Token);

            // 接收指令循环（直接读字节，避免 StreamReader 缓冲与帧推送线程抢流）
            try
            {
                var stream = client.GetStream();
                while (client.Connected)
                {
                    var line = await ReadLineAsync(stream);
                    if (line == null) break;
                    HandleMessage(ip, session, line, stream);
                }
            }
            catch { }
            finally
            {
                try { session.CaptureCts?.Cancel(); } catch { }
                lock (_lock) { _sessions.Remove(ip); }
                try { client.Close(); } catch { }
                ClientDisconnected?.Invoke(ip);
            }
        }

        // 简易行读取：按字节扫描 \n，避免 StreamReader 预读缓冲
        private static async Task<string?> ReadLineAsync(NetworkStream stream)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            while (true)
            {
                var n = await stream.ReadAsync(buf, 0, 1);
                if (n == 0) return null;
                var c = (char)buf[0];
                if (c == '\n') return sb.ToString();
                sb.Append(c);
            }
        }

        private static void HandleMessage(string ip, ClientSession session, string line, NetworkStream stream)
        {
            var parts = line.Split('|');
            if (parts.Length == 0) return;
            switch (parts[0])
            {
                // 协议：MODE|<mode>[|<WxH>]
                // 例：MODE|Control|1280x720
                case "MODE" when parts.Length >= 2:
                    session.Mode = parts[1];
                    // 解析可选分辨率
                    if (parts.Length >= 3 && TryParseRes(parts[2], out int w, out int h))
                    {
                        session.Width = w;
                        session.Height = h;
                    }
                    ModeChanged?.Invoke(ip, parts[1]);
                    break;
                case "PING":
                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes("PONG\n");
                        stream.Write(bytes, 0, bytes.Length);
                    } catch { }
                    break;
            }
        }

        private static bool TryParseRes(string s, out int w, out int h)
        {
            w = h = 0;
            var seg = s.Split('x');
            if (seg.Length != 2) return false;
            return int.TryParse(seg[0], out w) && int.TryParse(seg[1], out h)
                   && w >= 320 && w <= 3840 && h >= 240 && h <= 2160;
        }

        // ===== 画面推送循环：10 FPS，模式为 File 时暂停 =====
        private static async Task FrameLoopAsync(ClientSession session)
        {
            const int frameInterval = 100; // ms → 10 FPS
            var token = session.CaptureCts!.Token;
            // 等待远控端发送首个 MODE 指令后再开始推帧（避免连接建立瞬间推流导致对端未就绪）
            await Task.Delay(200, token);
            try
            {
                while (!token.IsCancellationRequested && session.Client.Connected)
                {
                    // 文件模式不推画面
                    if (session.Mode != "File")
                    {
                        try { await SendFrameAsync(session); }
                        catch (OperationCanceledException) { break; }
                        catch (IOException) { break; }   // 连接断开
                        catch { /* 捕获/编码瞬时错误，跳过本帧继续 */ }
                    }
                    await Task.Delay(frameInterval, token);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private static async Task SendFrameAsync(ClientSession session)
        {
            var jpeg = ScreenCapture.CaptureToJpeg(session.Width, session.Height);
            var stream = session.Client.GetStream();
            // 帧头：FRAME|<size>|<W>x<H>\n
            var header = Encoding.UTF8.GetBytes($"FRAME|{jpeg.Length}|{session.Width}x{session.Height}\n");
            await stream.WriteAsync(header, 0, header.Length);
            // 帧数据
            await stream.WriteAsync(jpeg, 0, jpeg.Length);
            await stream.FlushAsync();
        }

        private class ClientSession
        {
            public TcpClient Client = null!;
            public string Mode = "Control";
            public int Width = 960;
            public int Height = 540;
            public CancellationTokenSource? CaptureCts;
        }
    }
}
