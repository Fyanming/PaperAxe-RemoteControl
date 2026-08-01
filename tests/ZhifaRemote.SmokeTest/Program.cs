using System.IO;
using System.Net.Sockets;
using System.Windows.Threading;
using ZhifaRemote.Services;

var failures = new List<string>();

async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

await RunAsync("协议编解码", async () =>
{
    var hello = Protocol.BuildHello(1920, 1080, "测试机");
    var (type, payload) = Protocol.Decode(Protocol.Encode(MsgType.Hello, hello));
    if (type != MsgType.Hello) throw new Exception("消息类型不一致");
    var (w, h, host) = Protocol.ParseHello(payload);
    if (w != 1920 || h != 1080 || host != "测试机") throw new Exception("Hello 字段不一致");

    var ev = new InputEvent { Kind = InputKind.MouseMove, X = 800, Y = 600 };
    var parsed = Protocol.ParseInput(Protocol.BuildInput(ev));
    if (parsed.Kind != InputKind.MouseMove || parsed.X != 800 || parsed.Y != 600)
        throw new Exception("输入事件不一致");

    var (fid, fname, fsize) = Protocol.ParseFileRequest(
        Protocol.BuildFileRequest(7, "报告.pdf", 123456789));
    if (fid != 7 || fname != "报告.pdf" || fsize != 123456789)
        throw new Exception("文件请求不一致");

    var frameBytes = new byte[] { 1, 2, 3, 4, 5 };
    var (seq, frameW, frameH, frameJpeg) = Protocol.ParseScreenFrame(
        Protocol.BuildScreenFrame(11, 1280, 720, frameBytes));
    if (seq != 11 || frameW != 1280 || frameH != 720 || !frameJpeg.SequenceEqual(frameBytes))
        throw new Exception("屏幕帧协议不一致");

    var audioBytes = new byte[] { 9, 8, 7, 6 };
    var (pcm, sampleRate, channels) = Protocol.ParseAudioData(
        Protocol.BuildAudioData(audioBytes, 48000, 2));
    if (sampleRate != 48000 || channels != 2 || !pcm.SequenceEqual(audioBytes))
        throw new Exception("音频协议不一致");
    if (!Protocol.ParseBool(Protocol.BuildBool(true)) || Protocol.ParseBool(Protocol.BuildBool(false)))
        throw new Exception("布尔协议不一致");
    await Task.CompletedTask;
});

await RunAsync("TCP 握手与 Hello 往返", async () =>
{
    using var server = new RemoteServer();
    await server.StartAsync(0);
    var helloReceived = new TaskCompletionSource<(int, int, string)>();
    server.SessionConnected += s =>
    {
        s.MessageReceived += (_, type, payload) =>
        {
            if (type == MsgType.Hello)
            {
                var (w, h, host) = Protocol.ParseHello(payload);
                helloReceived.TrySetResult((w, h, host));
            }
            else if (type == MsgType.Ping)
            {
                s.SendAsync(MsgType.Pong, Array.Empty<byte>());
            }
        };
    };

    using var client = new RemoteClient();
    var pongReceived = new TaskCompletionSource();
    client.MessageReceived += (type, _) =>
    {
        if (type == MsgType.Pong) pongReceived.TrySetResult();
    };
    await client.ConnectAsync("127.0.0.1", server.Port);
    await client.SendAsync(MsgType.Hello, Protocol.BuildHello(2560, 1440, "被控机"));
    var (w, h, host) = await helloReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    if (w != 2560 || h != 1440 || host != "被控机") throw new Exception("Hello 往返内容不一致");
    await client.SendAsync(MsgType.Ping, Array.Empty<byte>());
    await pongReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
});

await RunAsync("服务器停止后可再次启动", async () =>
{
    using var server = new RemoteServer();
    await server.StartAsync(0);
    var firstPort = server.Port;
    server.Stop();
    if (server.IsRunning) throw new Exception("停止后仍标记为运行中");

    await server.StartAsync(0);
    if (!server.IsRunning) throw new Exception("再次启动失败");
    using var client = new RemoteClient();
    await client.ConnectAsync("127.0.0.1", server.Port);
    if (firstPort <= 0) throw new Exception("首次端口无效");
});

await RunAsync("未加密客户端无法通过握手", async () =>
{
    using var server = new RemoteServer();
    var connected = false;
    server.SessionConnected += _ => connected = true;
    await server.StartAsync(0);

    using var raw = new TcpClient();
    await raw.ConnectAsync("127.0.0.1", server.Port);
    var stream = raw.GetStream();
    var plainHello = Protocol.Encode(MsgType.Hello, Protocol.BuildHello(100, 100, "外部"));
    await stream.WriteAsync(plainHello);
    await Task.Delay(600);

    if (connected) throw new Exception("未加密连接被当作合法会话");
});

await RunAsync("文件双向传输", async () =>
{
    using var server = new RemoteServer();
    await server.StartAsync(0);
    RemoteSession? session = null;
    var sessionReady = new TaskCompletionSource();
    server.SessionConnected += s =>
    {
        session = s;
        sessionReady.TrySetResult();
    };

    using var client = new RemoteClient();
    await client.ConnectAsync("127.0.0.1", server.Port);
    await sessionReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

    var noticeReceived = new TaskCompletionSource();
    client.MessageReceived += (t, _) =>
    {
        if (t == MsgType.Notice) noticeReceived.TrySetResult();
    };
    await session!.SendAsync(MsgType.Notice, Protocol.BuildNotice("服务端到客户端直达"));
    await noticeReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

    var tempDir = Path.Combine(Path.GetTempPath(), "zhifa-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var sourceFile = Path.Combine(tempDir, "source.bin");
    var sourceBytes = new byte[300_000];
    Random.Shared.NextBytes(sourceBytes);
    File.WriteAllBytes(sourceFile, sourceBytes);

    var serverTransfer = new FileTransferService(
        (t, p) => session!.SendAsync(t, p),
        (name, _) => Path.Combine(tempDir, "server-" + name));
    var clientTransfer = new FileTransferService(
        (t, p) => client.SendAsync(t, p),
        (name, _) => Path.Combine(tempDir, "client-" + name));

    session!.MessageReceived += (sess, t, p) =>
    {
        Console.WriteLine($"  [S->C] type={t} len={p.Length}");
        if (IsFileMessage(t)) _ = serverTransfer.HandleMessageAsync(t, p);
    };
    client.MessageReceived += (t, p) =>
    {
        Console.WriteLine($"  [C->S] type={t} len={p.Length}");
        if (IsFileMessage(t)) _ = clientTransfer.HandleMessageAsync(t, p);
    };

    var sendOk = await serverTransfer.SendFileAsync(sourceFile);
    if (!sendOk) throw new Exception("服务端发送文件请求失败");
    var clientCopy = Path.Combine(tempDir, "client-source.bin");
    await WaitForFileAsync(clientCopy, sourceBytes.Length);
    if (!File.ReadAllBytes(clientCopy).SequenceEqual(sourceBytes))
        throw new Exception("客户端接收文件校验失败");

    await clientTransfer.SendFileAsync(sourceFile);
    var serverCopy = Path.Combine(tempDir, "server-source.bin");
    await WaitForFileAsync(serverCopy, sourceBytes.Length);
    if (!File.ReadAllBytes(serverCopy).SequenceEqual(sourceBytes))
        throw new Exception("服务端接收文件校验失败");
});

await RunAsync("屏幕捕获一帧", async () =>
{
    using var capture = new ScreenCaptureService { Fps = 5, Quality = 60, Scale = 0.5 };
    byte[]? frame = null;
    int width = 0, height = 0;
    capture.FrameCaptured += (jpeg, w, h) =>
    {
        frame = jpeg;
        width = w;
        height = h;
    };
    capture.Start();
    await Task.Delay(1200);
    capture.Stop();
    if (frame is null || frame.Length < 100) throw new Exception("未能捕获屏幕帧");
    if (width <= 0 || height <= 0) throw new Exception("屏幕尺寸无效");
    using var image = System.Drawing.Image.FromStream(new MemoryStream(frame));
    var expectedW = Math.Max(1, (int)(width * 0.5));
    var expectedH = Math.Max(1, (int)(height * 0.5));
    if (image.Width != expectedW || image.Height != expectedH)
        throw new Exception($"画面缩放尺寸不符: {image.Width}x{image.Height} != {expectedW}x{expectedH}");
});

await RunAsync("音频设备枚举", async () =>
{
    using var audio = new AudioControlService();
    var devices = audio.ListDevices();
    Console.WriteLine($"  音频输出设备数量: {devices.Count}");
});

await RunAsync("被控端画面流式传输", async () =>
{
    var dispatcher = await StartTestDispatcherAsync();
    try
    {
        using var controller = new MainController(dispatcher);
        var frameReceived = new TaskCompletionSource<(int W, int H)>();
        using var client = new RemoteClient();
        client.MessageReceived += (type, payload) =>
        {
            if (type == MsgType.ScreenFrame)
            {
                var (_, w, h, _) = Protocol.ParseScreenFrame(payload);
                frameReceived.TrySetResult((w, h));
            }
        };

        if (!await controller.StartServerAsync(0)) throw new Exception("被控端启动失败");
        await client.ConnectAsync("127.0.0.1", controller.Server.Port);
        var (w, h) = await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (w <= 0 || h <= 0) throw new Exception("收到的画面尺寸无效");
    }
    finally
    {
        dispatcher.InvokeShutdown();
    }
});

if (failures.Count > 0)
{
    Console.WriteLine($"失败 {failures.Count} 项");
    return 1;
}

Console.WriteLine("全部冒烟测试通过");
return 0;

static bool IsFileMessage(byte type)
    => type is MsgType.FileRequest or MsgType.FileAccept or MsgType.FileReject
        or MsgType.FileChunk or MsgType.FileDone or MsgType.FileCancel;

static async Task WaitForFileAsync(string path, int expectedLength)
{
    var deadline = DateTime.UtcNow.AddSeconds(8);
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path) && new FileInfo(path).Length == expectedLength) return;
        await Task.Delay(100);
    }
    throw new Exception($"等待文件超时: {path}");
}

static async Task<Dispatcher> StartTestDispatcherAsync()
{
    var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        ready.TrySetResult(Dispatcher.CurrentDispatcher);
        Dispatcher.Run();
    })
    {
        IsBackground = true
    };
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return await ready.Task;
}
