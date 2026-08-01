using System.IO;
using System.Diagnostics;
using System.Threading.Channels;
using System.Windows.Threading;
using ZhifaRemote.Models;

namespace ZhifaRemote.Services;

public sealed record RemoteWindowInfo(int ScreenWidth, int ScreenHeight, string HostName, string PeerIp);

public sealed class MainController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly RemoteServer _server = new();
    private readonly ScreenCaptureService _capture = new();
    private readonly AudioControlService _audioControl = new();
    private readonly AudioLoopbackService _audioLoopback = new();
    private readonly AudioPlaybackService _audioPlayback = new();
    private readonly PrivacyScreenService _privacyScreen = new();
    private readonly NotifyService _notify = new();
    private readonly Dictionary<RemoteSession, FileTransferService> _sessionTransfers = new();
    private readonly Dictionary<RemoteSession, SessionStreamSink> _streamSinks = new();
    private readonly Channel<(RemoteClient Client, InputEvent Event)> _inputQueue =
        Channel.CreateUnbounded<(RemoteClient, InputEvent)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _lock = new();
    private readonly object _latencyLock = new();
    private readonly object _inputLock = new();
    private readonly object _audioLock = new();
    private readonly Stopwatch _latencyClock = Stopwatch.StartNew();
    private int _frameSeq;
    private Task _inputWorker = Task.CompletedTask;

    private RemoteClient? _client;
    private FileTransferService? _clientTransfer;
    private string? _savedDefaultDevice;
    private float _savedVolume = 1f;
    private bool _audioPrepared;
    private bool _controlMode = true;
    private bool _clientConnected;
    private bool _privacyEnabled;
    private bool _audioEnabled = true;
    private bool _pingInFlight;
    private long _pingSentAt;
    private int _latencyMs = -1;
    private int _quality = 1;
    private int _fps = 12;

    public event Action<string>? Log;
    public event Action? ServerStateChanged;
    public event Action? ClientStateChanged;
    public event Action<RemoteWindowInfo>? RemoteWindowRequested;
    public event Action<int, int, byte[]>? FrameReceived;
    public event Action<TransferItem>? TransferChanged;
    public event Action<int>? LatencyChanged;

    public MainController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _server.SessionConnected += OnSessionConnected;
        _server.SessionDisconnected += OnSessionDisconnected;
    }

    public RemoteServer Server => _server;
    public RemoteClient? Client => _client;
    public ScreenCaptureService Capture => _capture;
    public AudioControlService AudioControl => _audioControl;
    public bool IsClientConnected => _clientConnected;
    public bool PrivacyEnabled => _privacyEnabled;
    public bool AudioEnabled => _audioEnabled;
    public int LatencyMs => _latencyMs;
    public bool ControlMode => _controlMode;
    public int QualityIndex => _quality;
    public int Fps => _fps;

    public async Task<bool> StartServerAsync(int port)
    {
        try
        {
            await _server.StartAsync(port);
            PostUi(() =>
            {
                Log?.Invoke($"被控端已监听 0.0.0.0:{_server.Port}");
                ServerStateChanged?.Invoke();
            });
            return true;
        }
        catch (Exception ex)
        {
            PostUi(() => Log?.Invoke($"启动被控端失败: {ex.Message}"));
            return false;
        }
    }

    public void StopServer()
    {
        _server.Stop();
        _privacyScreen.StopBlackout();
        StopAudioState();
        _capture.Stop();
        PostUi(() =>
        {
            Log?.Invoke("被控端已停止监听");
            ServerStateChanged?.Invoke();
        });
    }

    public async Task<bool> ConnectAsync(string ip, int port)
    {
        try
        {
            Disconnect();
            var client = new RemoteClient();
            client.MessageReceived += OnClientMessage;
            client.Disconnected += () => OnClientDisconnected(client);
            _client = client;
            _clientTransfer = new FileTransferService(
                (type, payload) => client.SendAsync(type, payload),
                RequestSavePath);
            _clientTransfer.ItemChanged += item => PostUi(() => TransferChanged?.Invoke(item));
            await client.ConnectAsync(ip, port);
            await client.SendAsync(MsgType.ModeChange, Protocol.BuildModeChange(_controlMode));
            await SendSettingsAsync();
            await client.SendAsync(MsgType.PrivacyMode, Protocol.BuildBool(_privacyEnabled));
            await client.SendAsync(MsgType.AudioMode, Protocol.BuildBool(_audioEnabled));
            _clientConnected = true;
            PostUi(() =>
            {
                Log?.Invoke($"已连接 {ip}:{port}");
                ClientStateChanged?.Invoke();
            });
            return true;
        }
        catch (Exception ex)
        {
            _client = null;
            _clientTransfer = null;
            PostUi(() =>
            {
                Log?.Invoke($"连接失败: {ex.Message}");
                ClientStateChanged?.Invoke();
            });
            return false;
        }
    }

    public void Disconnect()
    {
        _clientConnected = false;
        lock (_latencyLock)
        {
            _pingInFlight = false;
            _latencyMs = -1;
        }
        LatencyChanged?.Invoke(-1);
        _client?.Disconnect();
    }

    public void SetControlMode(bool control)
    {
        _controlMode = control;
        if (_client is not null)
        {
            _ = _client.SendAsync(MsgType.ModeChange, Protocol.BuildModeChange(control));
        }
    }

    public void SetQuality(int quality)
    {
        _quality = Math.Clamp(quality, 0, 3);
        _ = SendSettingsAsync();
    }

    public void SetFps(int fps)
    {
        _fps = Math.Clamp(fps, 1, 60);
        _ = SendSettingsAsync();
    }

    public void SetPrivacyMode(bool enabled)
    {
        _privacyEnabled = enabled;
        if (_client is not null && _clientConnected)
        {
            _ = _client.SendAsync(MsgType.PrivacyMode, Protocol.BuildBool(enabled));
        }
    }

    public void SetAudioEnabled(bool enabled)
    {
        _audioEnabled = enabled;
        if (_client is not null && _clientConnected)
        {
            _ = _client.SendAsync(MsgType.AudioMode, Protocol.BuildBool(enabled));
        }
    }

    public async Task SendFileAsync(string path)
    {
        if (_client is not null && _clientTransfer is not null)
        {
            var ok = await _clientTransfer.SendFileAsync(path);
            if (!ok) PostUi(() => Log?.Invoke($"发送文件失败: {path}"));
            return;
        }

        var sessions = _server.GetSessions();
        if (sessions.Count == 0)
        {
            PostUi(() => Log?.Invoke("当前没有可传输的对端"));
            return;
        }
        if (_sessionTransfers.TryGetValue(sessions[0], out var transfer))
        {
            await transfer.SendFileAsync(path);
        }
    }

    public void SendInputEvent(InputEvent ev)
    {
        var client = _client;
        if (client is null) return;
        _inputQueue.Writer.TryWrite((client, ev));
        lock (_inputLock)
        {
            if (_inputWorker.IsCompleted)
            {
                _inputWorker = Task.Run(InputLoopAsync);
            }
        }
    }

    private async Task InputLoopAsync()
    {
        await foreach (var (client, ev) in _inputQueue.Reader.ReadAllAsync())
        {
            try
            {
                await client.SendAsync(MsgType.InputEvent, Protocol.BuildInput(ev));
            }
            catch
            {
            }
        }
    }

    public async Task SendPingAsync()
    {
        if (_client is null || !_clientConnected) return;
        lock (_latencyLock)
        {
            if (_pingInFlight) return;
            _pingInFlight = true;
            _pingSentAt = _latencyClock.ElapsedMilliseconds;
        }
        try
        {
            await _client.SendAsync(MsgType.Ping, Array.Empty<byte>());
        }
        catch
        {
            lock (_latencyLock)
            {
                _pingInFlight = false;
            }
        }
    }

    private async Task SendSettingsAsync()
    {
        if (_client is null) return;
        await _client.SendAsync(MsgType.SettingsChange, Protocol.BuildSettings(_quality, _fps));
    }

    private async void OnSessionConnected(RemoteSession session)
    {
        try
        {
            _notify.Show("远控提示", $"你已被 {session.RemoteIp} 远控");
            _ = Task.Run(ApplyAudioState);
            ApplyPrivacyState();

            _capture.Start();

            var (width, height) = ScreenCaptureService.GetVirtualScreenSize();
            try
            {
                await session.SendAsync(MsgType.Hello, Protocol.BuildHello(width, height, Environment.MachineName));
            }
            catch
            {
                return;
            }

            var sink = new SessionStreamSink();
            sink.FrameHandler = (jpeg, w, h) => OnFrameCaptured(session, sink, jpeg, w, h);
            sink.AudioHandler = (pcm, sampleRate, channels) => OnAudioCaptured(session, sink, pcm, sampleRate, channels);
            _capture.FrameCaptured += sink.FrameHandler;
            _audioLoopback.AudioCaptured += sink.AudioHandler;
            lock (_lock)
            {
                _streamSinks[session] = sink;
            }

            var transfer = new FileTransferService(
                (type, payload) => session.SendAsync(type, payload),
                RequestSavePath);
            transfer.ItemChanged += item => PostUi(() => TransferChanged?.Invoke(item));
            _sessionTransfers[session] = transfer;
            session.MessageReceived += OnSessionMessage;
            session.Closed += OnSessionClosed;

            PostUi(() =>
            {
                Log?.Invoke($"远端 {session.RemoteIp} 已接入，远控画面与音频已就绪");
                ServerStateChanged?.Invoke();
            });
        }
        catch (Exception ex)
        {
            PostUi(() => Log?.Invoke($"被控端会话初始化失败: {ex.Message}"));
        }
    }

    private void OnSessionDisconnected(RemoteSession session)
    {
        lock (_lock)
        {
            _sessionTransfers.Remove(session);
            if (_streamSinks.Remove(session, out var sink))
            {
                _capture.FrameCaptured -= sink.FrameHandler;
                _audioLoopback.AudioCaptured -= sink.AudioHandler;
            }
        }
        session.MessageReceived -= OnSessionMessage;
        session.Closed -= OnSessionClosed;

        if (_server.GetSessions().Count == 0)
        {
            _capture.Stop();
            StopAudioState();
            _privacyScreen.StopBlackout();
        }
        else
        {
            _ = Task.Run(ApplyAudioState);
            ApplyPrivacyState();
        }
        PostUi(() =>
        {
            Log?.Invoke($"远端 {session.RemoteIp} 已断开");
            ServerStateChanged?.Invoke();
        });
    }

    private void OnSessionClosed(RemoteSession session)
    {
        _server.RemoveSession(session);
    }

    private async void OnSessionMessage(RemoteSession session, byte type, byte[] payload)
    {
        try
        {
            switch (type)
            {
                case MsgType.InputEvent:
                    if (!session.IsControlMode) return;
                    InjectInput(Protocol.ParseInput(payload));
                    break;
                case MsgType.ModeChange:
                    session.IsControlMode = Protocol.ParseModeChange(payload);
                    break;
                case MsgType.SettingsChange:
                    var (quality, fps) = Protocol.ParseSettings(payload);
                    ApplyCaptureSettings(quality, fps);
                    break;
                case MsgType.PrivacyMode:
                    session.PrivacyEnabled = Protocol.ParseBool(payload);
                    ApplyPrivacyState();
                    break;
                case MsgType.AudioMode:
                    session.AudioEnabled = Protocol.ParseBool(payload);
                    _ = Task.Run(ApplyAudioState);
                    break;
                case MsgType.Ping:
                    await session.SendAsync(MsgType.Pong, Array.Empty<byte>());
                    break;
                default:
                    if (IsFileMessage(type) && _sessionTransfers.TryGetValue(session, out var transfer))
                    {
                        await transfer.HandleMessageAsync(type, payload);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            PostUi(() => Log?.Invoke($"处理被控端消息失败: {ex.Message}"));
        }
    }

    private async void OnClientMessage(byte type, byte[] payload)
    {
        try
        {
            switch (type)
            {
                case MsgType.Hello:
                    var (width, height, host) = Protocol.ParseHello(payload);
                    var peerIp = _client?.RemoteIp ?? "未知";
                    PostUi(() => RemoteWindowRequested?.Invoke(new RemoteWindowInfo(width, height, host, peerIp)));
                    break;
                case MsgType.ScreenFrame:
                    var (_, w, h, jpeg) = Protocol.ParseScreenFrame(payload);
                    FrameReceived?.Invoke(w, h, jpeg);
                    break;
                case MsgType.AudioData:
                    var (pcm, sampleRate, channels) = Protocol.ParseAudioData(payload);
                    _audioPlayback.EnsureStarted(sampleRate, channels);
                    _audioPlayback.Play(pcm);
                    break;
                case MsgType.Pong:
                    lock (_latencyLock)
                    {
                        if (!_pingInFlight) break;
                        _pingInFlight = false;
                        _latencyMs = (int)Math.Max(0, _latencyClock.ElapsedMilliseconds - _pingSentAt);
                    }
                    LatencyChanged?.Invoke(_latencyMs);
                    break;
                default:
                    if (_clientTransfer is not null && IsFileMessage(type))
                    {
                        await _clientTransfer.HandleMessageAsync(type, payload);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            PostUi(() => Log?.Invoke($"处理远控端消息失败: {ex.Message}"));
        }
    }

    private void OnClientDisconnected(RemoteClient client)
    {
        if (!ReferenceEquals(client, _client)) return;
        _clientConnected = false;
        lock (_latencyLock)
        {
            _pingInFlight = false;
            _latencyMs = -1;
        }
        LatencyChanged?.Invoke(-1);
        _clientTransfer?.AbortAll();
        _audioPlayback.Stop();
        PostUi(() =>
        {
            Log?.Invoke("远控连接已断开");
            ClientStateChanged?.Invoke();
        });
    }

    private void InjectInput(InputEvent ev)
    {
        switch (ev.Kind)
        {
            case InputKind.MouseMove:
                InputInjector.Move(ev.X, ev.Y);
                break;
            case InputKind.Button:
                InputInjector.Button(ev.Button, ev.Down);
                break;
            case InputKind.Wheel:
                InputInjector.Wheel(ev.WheelDelta);
                break;
            case InputKind.Key:
                InputInjector.Key(ev.Vk, ev.Down, ev.Extended);
                break;
        }
    }

    private void ApplyCaptureSettings(int quality, int fps)
    {
        var (jpeg, scale) = MapQuality(quality);
        _capture.Quality = jpeg;
        _capture.Scale = scale;
        _capture.Fps = fps;
        PostUi(() => Log?.Invoke($"远控端调整画质={quality}, 帧率={fps}fps"));
    }

    private void ApplyAudioState()
    {
        lock (_audioLock)
        {
            var enabled = _server.GetSessions().Any(s => s.AudioEnabled);
            if (enabled)
            {
                try
                {
                    PrepareAudioState();
                    _audioLoopback.Start();
                }
                catch (Exception ex)
                {
                    PostUi(() => Log?.Invoke($"音频回传初始化失败: {ex.Message}"));
                }
            }
            else
            {
                _audioLoopback.Stop();
            }
        }
    }

    private void ApplyPrivacyState()
    {
        var enabled = _server.GetSessions().Any(s => s.PrivacyEnabled);
        if (enabled)
        {
            _privacyScreen.StartBlackout();
        }
        else
        {
            _privacyScreen.StopBlackout();
        }
    }

    private void OnFrameCaptured(RemoteSession session, SessionStreamSink sink, byte[] jpeg, int width, int height)
    {
        if (Interlocked.CompareExchange(ref sink.FrameSending, 1, 0) != 0) return;
        _ = SendFrameAsync(session, sink, Interlocked.Increment(ref _frameSeq), width, height, jpeg);
    }

    private static async Task SendFrameAsync(
        RemoteSession session, SessionStreamSink sink, int seq, int width, int height, byte[] jpeg)
    {
        try
        {
            await session.SendAsync(MsgType.ScreenFrame, Protocol.BuildScreenFrame(seq, width, height, jpeg));
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref sink.FrameSending, 0);
        }
    }

    private void OnAudioCaptured(
        RemoteSession session, SessionStreamSink sink, byte[] pcm, int sampleRate, short channels)
    {
        if (Interlocked.CompareExchange(ref sink.AudioSending, 1, 0) != 0) return;
        _ = SendAudioAsync(session, sink, pcm, sampleRate, channels);
    }

    private static async Task SendAudioAsync(
        RemoteSession session, SessionStreamSink sink, byte[] pcm, int sampleRate, short channels)
    {
        try
        {
            await session.SendAsync(MsgType.AudioData, Protocol.BuildAudioData(pcm, sampleRate, channels));
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref sink.AudioSending, 0);
        }
    }

    private void PrepareAudioState()
    {
        if (_audioPrepared) return;
        var virtualDevice = _audioControl.ListDevices().FirstOrDefault(d => d.IsVirtual);
        if (virtualDevice is null)
        {
            PostUi(() => Log?.Invoke("未检测到虚拟音频设备，直接回传系统默认输出声音"));
            return;
        }
        _savedDefaultDevice = _audioControl.GetDefaultDeviceId();
        _savedVolume = _audioControl.GetDefaultVolume();
        if (_savedDefaultDevice is not null)
        {
            _audioControl.SetVolumeById(_savedDefaultDevice, 0f);
        }
        _audioControl.SetDefaultDevice(virtualDevice.Id);
        _audioPrepared = true;
        PostUi(() => Log?.Invoke($"已切换到虚拟音频设备: {virtualDevice.Name}"));
    }

    private void RestoreAudioState()
    {
        lock (_audioLock)
        {
            if (!_audioPrepared) return;
            if (_savedDefaultDevice is not null)
            {
                _audioControl.SetDefaultDevice(_savedDefaultDevice);
                _audioControl.SetVolumeById(_savedDefaultDevice, _savedVolume);
            }
            _audioPrepared = false;
            PostUi(() => Log?.Invoke("已恢复被控端音频设备与音量"));
        }
    }

    private void StopAudioState()
    {
        lock (_audioLock)
        {
            _audioLoopback.Stop();
            RestoreAudioState();
        }
    }

    private string? RequestSavePath(string fileName, long size)
    {
        return _dispatcher.Invoke(() =>
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "纸伐接收");
            Directory.CreateDirectory(directory);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存接收文件",
                FileName = fileName,
                InitialDirectory = directory,
                OverwritePrompt = true
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });
    }

    private static bool IsFileMessage(byte type)
        => type is MsgType.FileRequest or MsgType.FileAccept or MsgType.FileReject
            or MsgType.FileChunk or MsgType.FileDone or MsgType.FileCancel;

    public static (int Jpeg, double Scale) MapQuality(int quality)
        => quality switch
        {
            0 => (45, 0.45),
            1 => (65, 0.70),
            2 => (80, 0.90),
            _ => (92, 1.0)
        };

    private void PostUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _inputQueue.Writer.TryComplete();
        Disconnect();
        StopServer();
        _privacyScreen.Dispose();
        _clientTransfer?.AbortAll();
        _audioPlayback.Dispose();
        lock (_audioLock)
        {
            _audioLoopback.Dispose();
        }
        _audioControl.Dispose();
        _capture.Dispose();
        _notify.Dispose();
        _server.Dispose();
    }

    private sealed class SessionStreamSink
    {
        public Action<byte[], int, int> FrameHandler { get; set; } = (_, _, _) => { };
        public Action<byte[], int, short> AudioHandler { get; set; } = (_, _, _) => { };
        public int FrameSending;
        public int AudioSending;
    }
}
