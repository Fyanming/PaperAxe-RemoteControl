using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ZhifaRemote.Services;

internal static class NetworkIO
{
    public static async Task<byte[]?> ReadMessageAsync(NetworkStream stream, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        if (!await ReadExactlyAsync(stream, lengthBuf, 0, 4, ct)) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuf);
        if (length <= 0 || length > Protocol.MaxMessageSize) return null;
        var msg = new byte[length + 4];
        lengthBuf.CopyTo(msg, 0);
        if (!await ReadExactlyAsync(stream, msg, 4, length, ct)) return null;
        return msg;
    }

    internal static async Task<bool> ReadExactlyAsync(
        NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var readOffset = offset;
        var remaining = count;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(readOffset, remaining), ct);
            if (read <= 0) return false;
            readOffset += read;
            remaining -= read;
        }
        return true;
    }
}

public sealed class RemoteSession : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private SecureChannel? _channel;
    private bool _closed;

    public string RemoteIp { get; }
    public int RemotePort { get; }
    public bool IsControlMode { get; set; } = true;
    public bool PrivacyEnabled { get; set; }
    public bool AudioEnabled { get; set; } = true;

    public event Action<RemoteSession, byte, byte[]>? MessageReceived;
    public event Action<RemoteSession>? Closed;

    public RemoteSession(TcpClient tcp)
    {
        _tcp = tcp;
        _tcp.NoDelay = true;
        _tcp.ReceiveBufferSize = 512 * 1024;
        _tcp.SendBufferSize = 512 * 1024;
        _stream = tcp.GetStream();
        var ep = (IPEndPoint)tcp.Client.RemoteEndPoint!;
        RemoteIp = ep.Address.ToString();
        RemotePort = ep.Port;
    }

    public async Task<bool> HandshakeAsync(CancellationToken ct)
    {
        try
        {
            _channel = await SecureHandshake.CreateServerChannelAsync(_stream, ct);
            return _channel is not null;
        }
        catch (OperationCanceledException)
        {
            Close();
            return false;
        }
        catch (IOException)
        {
            Close();
            return false;
        }
        catch (ObjectDisposedException)
        {
            Close();
            return false;
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var channel = _channel;
        if (channel is null)
        {
            Closed?.Invoke(this);
            Close();
            return;
        }
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await channel.ReadMessageAsync(ct);
                if (msg is null) break;
                var (type, payload) = msg.Value;
                MessageReceived?.Invoke(this, type, payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Closed?.Invoke(this);
            Close();
        }
    }

    public async Task SendAsync(byte type, byte[] payload)
    {
        if (_closed || _channel is null) return;
        await _channel.WriteMessageAsync(type, payload);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        try { _tcp.Close(); } catch { }
    }

    public void Dispose()
    {
        Close();
        _channel?.Dispose();
        _stream.Dispose();
        _tcp.Dispose();
    }
}

public sealed class RemoteServer : IDisposable
{
    private readonly object _lock = new();
    private readonly List<RemoteSession> _sessions = new();
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;

    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    public event Action<RemoteSession>? SessionConnected;
    public event Action<RemoteSession>? SessionDisconnected;

    public async Task StartAsync(int port)
    {
        if (_listener is not null) return;
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        await Task.CompletedTask;
        _ = AcceptLoopAsync(listener, _cts);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationTokenSource cts)
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var tcp = await listener.AcceptTcpClientAsync(cts.Token);
                var session = new RemoteSession(tcp);
                if (!await session.HandshakeAsync(cts.Token))
                {
                    session.Dispose();
                    continue;
                }
                lock (_lock) _sessions.Add(session);
                SessionConnected?.Invoke(session);
                _ = session.RunAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    public IReadOnlyList<RemoteSession> GetSessions()
    {
        lock (_lock) return _sessions.ToArray();
    }

    public async Task BroadcastAsync(byte type, byte[] payload)
    {
        RemoteSession[] sessions;
        lock (_lock) sessions = _sessions.ToArray();
        foreach (var session in sessions)
        {
            try { await session.SendAsync(type, payload); }
            catch { }
        }
    }

    public void RemoveSession(RemoteSession session)
    {
        lock (_lock) _sessions.Remove(session);
        SessionDisconnected?.Invoke(session);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        RemoteSession[] sessions;
        lock (_lock)
        {
            sessions = _sessions.ToArray();
            _sessions.Clear();
        }
        foreach (var session in sessions)
        {
            session.Close();
            SessionDisconnected?.Invoke(session);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _cts = null;
    }
}

public sealed class RemoteClient : IDisposable
{
    private CancellationTokenSource _cts = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private SecureChannel? _channel;

    public string? RemoteIp { get; private set; }
    public bool IsConnected => _tcp is { Connected: true };

    public event Action<byte, byte[]>? MessageReceived;
    public event Action? Disconnected;

    public async Task ConnectAsync(string ip, int port)
    {
        CloseExisting();
        _cts = new CancellationTokenSource();
        var tcp = new TcpClient
        {
            NoDelay = true,
            ReceiveBufferSize = 512 * 1024,
            SendBufferSize = 512 * 1024
        };
        await tcp.ConnectAsync(ip, port);
        _tcp = tcp;
        _stream = tcp.GetStream();
        RemoteIp = ip;
        _channel = await SecureHandshake.CreateClientChannelAsync(_stream, _cts.Token);
        if (_channel is null)
        {
            Disconnect();
            throw new IOException("加密握手失败");
        }
        _ = ReadLoopAsync(_cts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _channel is not null)
            {
                var msg = await _channel.ReadMessageAsync(ct);
                if (msg is null) break;
                var (type, payload) = msg.Value;
                MessageReceived?.Invoke(type, payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    public async Task SendAsync(byte type, byte[] payload)
    {
        if (_tcp is null || _stream is null || _channel is null) return;
        await _channel.WriteMessageAsync(type, payload);
    }

    public void Disconnect()
    {
        _cts.Cancel();
        CloseExisting();
    }

    private void CloseExisting()
    {
        try { _tcp?.Close(); } catch { }
        _channel?.Dispose();
        _channel = null;
        _tcp = null;
        _stream = null;
        RemoteIp = null;
    }

    public void Dispose()
    {
        Disconnect();
        _cts.Dispose();
    }
}
