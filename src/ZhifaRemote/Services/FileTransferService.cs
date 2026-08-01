using System.Collections.Concurrent;
using System.IO;
using ZhifaRemote.Models;

namespace ZhifaRemote.Services;

public sealed class FileTransferService
{
    private const int ChunkSize = 256 * 1024;
    private readonly Func<byte, byte[], Task> _sendAsync;
    private readonly Func<string, long, string?> _savePathRequested;
    private readonly ConcurrentDictionary<int, ActiveTransfer> _active = new();
    private int _nextId;

    public event Action<TransferItem>? ItemChanged;

    public void AbortAll()
    {
        foreach (var fileId in _active.Keys)
        {
            if (!_active.TryGetValue(fileId, out var transfer)) continue;
            try
            {
                transfer.SaveStream?.Dispose();
            }
            catch
            {
            }
            if (transfer.Item.State is not (TransferState.Completed or TransferState.Failed))
            {
                transfer.Item.State = TransferState.Cancelled;
                ItemChanged?.Invoke(transfer.Item);
            }
        }
        _active.Clear();
    }

    public FileTransferService(
        Func<byte, byte[], Task> sendAsync,
        Func<string, long, string?> savePathRequested)
    {
        _sendAsync = sendAsync;
        _savePathRequested = savePathRequested;
    }

    public async Task<bool> SendFileAsync(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            var fileId = Interlocked.Increment(ref _nextId);
            var item = new TransferItem
            {
                Id = fileId,
                Name = info.Name,
                Size = info.Length,
                Direction = TransferDirection.Sending,
                State = TransferState.Waiting
            };
            _active[fileId] = new ActiveTransfer { Item = item, SourcePath = path };
            ItemChanged?.Invoke(item);
            await _sendAsync(MsgType.FileRequest, Protocol.BuildFileRequest(fileId, info.Name, info.Length));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task HandleMessageAsync(byte type, byte[] payload)
    {
        switch (type)
        {
            case MsgType.FileRequest:
                await HandleFileRequestAsync(payload);
                break;
            case MsgType.FileAccept:
                await HandleFileAcceptAsync(payload);
                break;
            case MsgType.FileReject:
                HandleFileReject(payload);
                break;
            case MsgType.FileChunk:
                await HandleFileChunkAsync(payload);
                break;
            case MsgType.FileDone:
                HandleFileDone(payload);
                break;
            case MsgType.FileCancel:
                HandleFileCancel(payload);
                break;
        }
    }

    private async Task HandleFileRequestAsync(byte[] payload)
    {
        var (fileId, name, size) = Protocol.ParseFileRequest(payload);
        var savePath = _savePathRequested(name, size);
        if (string.IsNullOrWhiteSpace(savePath))
        {
            await _sendAsync(MsgType.FileReject, Protocol.BuildFileId(fileId));
            return;
        }

        var item = new TransferItem
        {
            Id = fileId,
            Name = name,
            Size = size,
            Direction = TransferDirection.Receiving,
            State = TransferState.Waiting
        };
        try
        {
            var stream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024);
            _active[fileId] = new ActiveTransfer { Item = item, SaveStream = stream };
        }
        catch
        {
            await _sendAsync(MsgType.FileReject, Protocol.BuildFileId(fileId));
            return;
        }
        ItemChanged?.Invoke(item);
        await _sendAsync(MsgType.FileAccept, Protocol.BuildFileId(fileId));
    }

    private async Task HandleFileAcceptAsync(byte[] payload)
    {
        var fileId = Protocol.ParseFileId(payload);
        if (!_active.TryGetValue(fileId, out var transfer) || transfer.Item.State != TransferState.Waiting)
        {
            return;
        }
        transfer.Item.State = TransferState.Active;
        ItemChanged?.Invoke(transfer.Item);
        await Task.Run(async () => await SendChunksAsync(transfer));
    }

    private async Task SendChunksAsync(ActiveTransfer transfer)
    {
        var fileId = transfer.Item.Id;
        try
        {
            await using var stream = new FileStream(transfer.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize);
            var buffer = new byte[ChunkSize];
            var index = 0;
            long sent = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read <= 0) break;
                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                await _sendAsync(MsgType.FileChunk, Protocol.BuildFileChunk(fileId, index++, chunk));
                sent += read;
                UpdateProgress(transfer, sent);
            }
            await _sendAsync(MsgType.FileDone, Protocol.BuildFileId(fileId));
            transfer.Item.State = TransferState.Completed;
            transfer.Item.Progress = 100;
            ItemChanged?.Invoke(transfer.Item);
            _active.TryRemove(fileId, out _);
        }
        catch (Exception)
        {
            transfer.Item.State = TransferState.Failed;
            ItemChanged?.Invoke(transfer.Item);
            _active.TryRemove(fileId, out _);
        }
    }

    private async Task HandleFileChunkAsync(byte[] payload)
    {
        var (fileId, index, data) = Protocol.ParseFileChunk(payload);
        if (!_active.TryGetValue(fileId, out var transfer) || transfer.SaveStream is null)
        {
            return;
        }
        await transfer.SaveStream.WriteAsync(data);
        transfer.Item.State = TransferState.Active;
        var received = transfer.Received + data.Length;
        transfer.Received = received;
        UpdateProgress(transfer, received);
    }

    private void HandleFileDone(byte[] payload)
    {
        var fileId = Protocol.ParseFileId(payload);
        if (!_active.TryGetValue(fileId, out var transfer)) return;
        try
        {
            transfer.SaveStream?.Flush();
            transfer.SaveStream?.Dispose();
            transfer.Item.State = TransferState.Completed;
            transfer.Item.Progress = 100;
        }
        catch
        {
            transfer.Item.State = TransferState.Failed;
        }
        ItemChanged?.Invoke(transfer.Item);
        _active.TryRemove(fileId, out _);
    }

    private void HandleFileReject(byte[] payload)
    {
        var fileId = Protocol.ParseFileId(payload);
        if (!_active.TryGetValue(fileId, out var transfer)) return;
        transfer.Item.State = TransferState.Cancelled;
        ItemChanged?.Invoke(transfer.Item);
        _active.TryRemove(fileId, out _);
    }

    private void HandleFileCancel(byte[] payload)
    {
        var fileId = Protocol.ParseFileId(payload);
        if (!_active.TryGetValue(fileId, out var transfer)) return;
        try
        {
            transfer.SaveStream?.Dispose();
        }
        catch
        {
        }
        transfer.Item.State = TransferState.Cancelled;
        ItemChanged?.Invoke(transfer.Item);
        _active.TryRemove(fileId, out _);
    }

    private void UpdateProgress(ActiveTransfer transfer, long bytes)
    {
        if (transfer.Item.Size <= 0) return;
        transfer.Item.Progress = Math.Clamp(bytes * 100.0 / transfer.Item.Size, 0, 100);
        ItemChanged?.Invoke(transfer.Item);
    }

    private sealed class ActiveTransfer
    {
        public TransferItem Item { get; init; } = new();
        public string? SourcePath { get; init; }
        public FileStream? SaveStream { get; init; }
        public long Received { get; set; }
    }
}
