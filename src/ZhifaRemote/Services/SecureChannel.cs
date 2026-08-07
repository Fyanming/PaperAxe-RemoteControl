using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ZhifaRemote.Services;

internal sealed class SecureChannel : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int FrameOverhead = NonceSize + TagSize;
    private const int MaxFrameSize = Protocol.MaxMessageSize + 64;

    private readonly NetworkStream _stream;
    private readonly byte[] _key;
    private readonly AesGcm _writeAes;
    private readonly AesGcm _readAes;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _nonceCounter;

    public SecureChannel(NetworkStream stream, byte[] key)
    {
        _stream = stream;
        _key = key;
        _writeAes = new AesGcm(key, TagSize);
        _readAes = new AesGcm(key, TagSize);
    }

    public async Task WriteMessageAsync(byte type, byte[] payload)
    {
        var plain = Protocol.Encode(type, payload);
        var nonce = BuildNonce();
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        await _writeLock.WaitAsync();
        try
        {
            _writeAes.Encrypt(nonce, plain, cipher, tag);
            var frame = new byte[4 + FrameOverhead + cipher.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame, FrameOverhead + cipher.Length);
            Buffer.BlockCopy(nonce, 0, frame, 4, NonceSize);
            Buffer.BlockCopy(cipher, 0, frame, 4 + NonceSize, cipher.Length);
            Buffer.BlockCopy(tag, 0, frame, 4 + NonceSize + cipher.Length, TagSize);
            await _stream.WriteAsync(frame);
            await _stream.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<(byte Type, byte[] Payload)?> ReadMessageAsync(CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        if (!await NetworkIO.ReadExactlyAsync(_stream, lengthBuf, 0, 4, ct)) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuf);
        if (length <= FrameOverhead || length > MaxFrameSize) return null;

        var frame = new byte[length];
        if (!await NetworkIO.ReadExactlyAsync(_stream, frame, 0, length, ct)) return null;

        var nonce = frame.AsSpan(0, NonceSize);
        var tag = frame.AsSpan(length - TagSize, TagSize);
        var cipher = frame.AsSpan(NonceSize, length - FrameOverhead);
        var plain = new byte[cipher.Length];
        try
        {
            _readAes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            return null;
        }

        if (plain.Length < 5) return null;
        return Protocol.Decode(plain);
    }

    private byte[] BuildNonce()
    {
        var nonce = new byte[NonceSize];
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), Interlocked.Increment(ref _nonceCounter));
        return nonce;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        _writeAes.Dispose();
        _readAes.Dispose();
        _writeLock.Dispose();
    }
}

internal static class SecureHandshake
{
    private const int MaxPublicKeySize = 1024;
    private static readonly byte[] HkdfSalt = Encoding.UTF8.GetBytes("zhifa-lan-remote-v1");
    private static readonly byte[] HkdfInfo = Encoding.UTF8.GetBytes("zhifa-aes-gcm-key");

    public static Task<SecureChannel?> CreateClientChannelAsync(NetworkStream stream, CancellationToken ct)
        => CreateChannelAsync(stream, ct, initiator: true);

    public static Task<SecureChannel?> CreateServerChannelAsync(NetworkStream stream, CancellationToken ct)
        => CreateChannelAsync(stream, ct, initiator: false);

    private static async Task<SecureChannel?> CreateChannelAsync(
        NetworkStream stream, CancellationToken ct, bool initiator)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var ecdh = ECDiffieHellman.Create();
            var publicKey = ecdh.ExportSubjectPublicKeyInfo();

            if (initiator)
            {
                await WriteKeyAsync(stream, publicKey, timeout.Token);
            }

            var peerKey = await ReadKeyAsync(stream, timeout.Token);
            if (peerKey is null) return null;

            if (!initiator)
            {
                await WriteKeyAsync(stream, publicKey, timeout.Token);
            }

            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(peerKey, out _);
            var secret = ecdh.DeriveKeyMaterial(peer.PublicKey);
            var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, HkdfSalt, HkdfInfo);
            CryptographicOperations.ZeroMemory(secret);
            return new SecureChannel(stream, key);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteKeyAsync(NetworkStream stream, byte[] key, CancellationToken ct)
    {
        var frame = new byte[4 + key.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, key.Length);
        Buffer.BlockCopy(key, 0, frame, 4, key.Length);
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<byte[]?> ReadKeyAsync(NetworkStream stream, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        if (!await NetworkIO.ReadExactlyAsync(stream, lengthBuf, 0, 4, ct)) return null;
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuf);
        if (length <= 0 || length > MaxPublicKeySize) return null;

        var key = new byte[length];
        if (!await NetworkIO.ReadExactlyAsync(stream, key, 0, length, ct)) return null;
        return key;
    }
}
