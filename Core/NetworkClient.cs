using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LanRemoteControl.Core
{
    // ============================================================
    //  远控端 TCP 客户端
    //  - 连接被控端监听端口
    //  - 发送模式切换指令 / 文件传输
    //  - 接收并解析帧（FRAME|size|WxH\n + JPEG 数据）
    //  - 通过 FrameReceived 事件回调 UI 层
    // ============================================================
    public static class NetworkClient
    {
        // 帧到达事件：参数为 JPEG 字节数组 + 帧分辨率
        public static event Action<byte[], int, int>? FrameReceived;
        public static event Action? Disconnected;

        private static TcpClient? _client;
        private static NetworkStream? _stream;
        private static bool _running;

        public static async Task ConnectAsync(string ip, int port)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port);
            _stream = _client.GetStream();
            _running = true;
            _ = Task.Run(ReceiveLoopAsync);
        }

        public static void Disconnect()
        {
            _running = false;
            try { _stream?.Close(); _client?.Close(); } catch { }
            _stream = null;
            _client = null;
            Disconnected?.Invoke();
        }

        // 发送模式 + 分辨率：MODE|<mode>|<WxH>
        public static void SendMode(string mode, int width, int height) =>
            Send($"MODE|{mode}|{width}x{height}");

        public static async Task SendFileAsync(string path)
        {
            if (_stream == null) throw new InvalidOperationException("未连接");

            var info = new FileInfo(path);
            var header = $"FILE|{info.Name}|{info.Length}\n";
            var headerBytes = Encoding.UTF8.GetBytes(header);
            await _stream.WriteAsync(headerBytes);

            await using var fs = info.OpenRead();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await fs.ReadAsync(buffer)) > 0)
            {
                await _stream.WriteAsync(buffer, 0, read);
            }
            var end = Encoding.UTF8.GetBytes("\nENDFILE\n");
            await _stream.WriteAsync(end);
        }

        private static void Send(string msg)
        {
            if (_stream == null) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(msg + "\n");
                _stream.Write(bytes, 0, bytes.Length);
            }
            catch { }
        }

        // ===== 接收循环：直接读 NetworkStream，先读一行文本头，再按类型处理 =====
        private static async Task ReceiveLoopAsync()
        {
            if (_stream == null || _client == null) return;
            try
            {
                while (_running && _client.Connected)
                {
                    var header = await ReadLineAsync();
                    if (header == null) break;
                    HandleHeader(header);
                }
            }
            catch { }
            finally
            {
                if (_running) Disconnect();
            }
        }

        private static async void HandleHeader(string header)
        {
            var parts = header.Split('|');
            if (parts.Length == 0) return;
            switch (parts[0])
            {
                // 协议：FRAME|<size>|<WxH>\n
                case "FRAME" when parts.Length >= 3 && int.TryParse(parts[1], out int size):
                    var (w, h) = ParseRes(parts[2]);
                    await ReadFrameAsync(size, w, h);
                    break;
                case "PONG":
                    // 心跳回应，暂不处理
                    break;
            }
        }

        private static (int, int) ParseRes(string s)
        {
            var seg = s.Split('x');
            if (seg.Length == 2 && int.TryParse(seg[0], out int w) && int.TryParse(seg[1], out int h))
                return (w, h);
            return (1280, 720);
        }

        private static async Task ReadFrameAsync(int size, int w, int h)
        {
            if (_stream == null) return;
            var buf = new byte[size];
            var total = 0;
            while (total < size)
            {
                var n = await _stream.ReadAsync(buf, total, size - total);
                if (n == 0) break;
                total += n;
            }
            if (total == size)
            {
                FrameReceived?.Invoke(buf, w, h);
            }
        }

        // 简易行读取（按字节扫描 \n，避免与帧二进制数据冲突）
        private static async Task<string?> ReadLineAsync()
        {
            if (_stream == null) return null;
            var sb = new StringBuilder();
            var buf = new byte[1];
            while (true)
            {
                var n = await _stream.ReadAsync(buf, 0, 1);
                if (n == 0) return null;
                var c = (char)buf[0];
                if (c == '\n') return sb.ToString();
                sb.Append(c);
            }
        }
    }
}
