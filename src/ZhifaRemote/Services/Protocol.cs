using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ZhifaRemote.Services;

public static class MsgType
{
    public const byte Hello = 1;
    public const byte ScreenFrame = 2;
    public const byte InputEvent = 3;
    public const byte ModeChange = 4;
    public const byte SettingsChange = 5;
    public const byte AudioData = 6;
    public const byte FileRequest = 7;
    public const byte FileAccept = 8;
    public const byte FileReject = 9;
    public const byte FileChunk = 10;
    public const byte FileDone = 11;
    public const byte FileCancel = 12;
    public const byte Notice = 13;
    public const byte Ping = 14;
    public const byte Pong = 15;
    public const byte PrivacyMode = 16;
    public const byte AudioMode = 17;
}

public enum InputKind : byte
{
    MouseMove = 0,
    Button = 1,
    Wheel = 2,
    Key = 3
}

public sealed class InputEvent
{
    public InputKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Button { get; set; }
    public bool Down { get; set; }
    public int WheelDelta { get; set; }
    public int Vk { get; set; }
    public bool Extended { get; set; }
}

public static class Protocol
{
    public const int MaxMessageSize = 16 * 1024 * 1024;

    public static byte[] Encode(byte type, byte[] payload)
    {
        var msg = new byte[payload.Length + 5];
        BinaryPrimitives.WriteInt32BigEndian(msg, payload.Length + 1);
        msg[4] = type;
        Buffer.BlockCopy(payload, 0, msg, 5, payload.Length);
        return msg;
    }

    public static (byte Type, byte[] Payload) Decode(byte[] msg)
    {
        var length = BinaryPrimitives.ReadInt32BigEndian(msg);
        var type = msg[4];
        var payload = new byte[length - 1];
        Buffer.BlockCopy(msg, 5, payload, 0, payload.Length);
        return (type, payload);
    }

    public static byte[] BuildHello(int screenWidth, int screenHeight, string hostName)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(screenWidth);
        w.Write(screenHeight);
        w.Write(hostName);
        return ms.ToArray();
    }

    public static (int Width, int Height, string HostName) ParseHello(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r = new BinaryReader(ms);
        return (r.ReadInt32(), r.ReadInt32(), r.ReadString());
    }

    public static byte[] BuildScreenFrame(int seq, int width, int height, byte[] jpeg)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(seq);
        w.Write(width);
        w.Write(height);
        w.Write(jpeg);
        return ms.ToArray();
    }

    public static (int Seq, int Width, int Height, byte[] Jpeg) ParseScreenFrame(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r = new BinaryReader(ms);
        var seq = r.ReadInt32();
        var width = r.ReadInt32();
        var height = r.ReadInt32();
        var jpeg = r.ReadBytes(payload.Length - 12);
        return (seq, width, height, jpeg);
    }

    public static byte[] BuildInput(InputEvent ev)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)ev.Kind);
        switch (ev.Kind)
        {
            case InputKind.MouseMove:
                w.Write(ev.X);
                w.Write(ev.Y);
                break;
            case InputKind.Button:
                w.Write((byte)ev.Button);
                w.Write(ev.Down ? (byte)1 : (byte)0);
                break;
            case InputKind.Wheel:
                w.Write(ev.WheelDelta);
                break;
            case InputKind.Key:
                w.Write(ev.Vk);
                w.Write(ev.Extended ? (byte)1 : (byte)0);
                break;
        }
        return ms.ToArray();
    }

    public static InputEvent ParseInput(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r = new BinaryReader(ms);
        var ev = new InputEvent { Kind = (InputKind)r.ReadByte() };
        switch (ev.Kind)
        {
            case InputKind.MouseMove:
                ev.X = r.ReadInt32();
                ev.Y = r.ReadInt32();
                break;
            case InputKind.Button:
                ev.Button = r.ReadByte();
                ev.Down = r.ReadByte() == 1;
                break;
            case InputKind.Wheel:
                ev.WheelDelta = r.ReadInt32();
                break;
            case InputKind.Key:
                ev.Vk = r.ReadInt32();
                ev.Extended = r.ReadByte() == 1;
                break;
        }
        return ev;
    }

    public static byte[] BuildModeChange(bool control)
        => new[] { control ? (byte)1 : (byte)0 };

    public static bool ParseModeChange(byte[] payload)
        => payload.Length > 0 && payload[0] == 1;

    public static byte[] BuildBool(bool value)
        => new[] { value ? (byte)1 : (byte)0 };

    public static bool ParseBool(byte[] payload)
        => payload.Length > 0 && payload[0] == 1;

    public static byte[] BuildSettings(int quality, int fps)
        => new[] { (byte)Math.Clamp(quality, 0, 3), (byte)Math.Clamp(fps, 1, 60) };

    public static (int Quality, int Fps) ParseSettings(byte[] payload)
    {
        if (payload.Length < 2) return (1, 10);
        return (payload[0], payload[1]);
    }

    public static byte[] BuildAudioData(byte[] pcm, int sampleRate, short channels)
    {
        var msg = new byte[pcm.Length + 6];
        BinaryPrimitives.WriteInt32BigEndian(msg, sampleRate);
        BinaryPrimitives.WriteInt16BigEndian(msg.AsSpan(4), channels);
        Buffer.BlockCopy(pcm, 0, msg, 6, pcm.Length);
        return msg;
    }

    public static (byte[] Pcm, int SampleRate, short Channels) ParseAudioData(byte[] payload)
    {
        var sampleRate = BinaryPrimitives.ReadInt32BigEndian(payload);
        var channels = BinaryPrimitives.ReadInt16BigEndian(payload.AsSpan(4));
        var pcm = new byte[payload.Length - 6];
        Buffer.BlockCopy(payload, 6, pcm, 0, pcm.Length);
        return (pcm, sampleRate, channels);
    }

    public static byte[] BuildFileRequest(int fileId, string fileName, long size)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fileName);
        var msg = new byte[4 + 4 + nameBytes.Length + 8];
        BinaryPrimitives.WriteInt32BigEndian(msg, fileId);
        BinaryPrimitives.WriteInt32BigEndian(msg.AsSpan(4), nameBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, msg, 8, nameBytes.Length);
        BinaryPrimitives.WriteInt64BigEndian(msg.AsSpan(8 + nameBytes.Length), size);
        return msg;
    }

    public static (int FileId, string FileName, long Size) ParseFileRequest(byte[] payload)
    {
        var fileId = BinaryPrimitives.ReadInt32BigEndian(payload);
        var nameLen = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        var name = Encoding.UTF8.GetString(payload, 8, nameLen);
        var size = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(8 + nameLen));
        return (fileId, name, size);
    }

    public static byte[] BuildFileId(int fileId)
    {
        var msg = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(msg, fileId);
        return msg;
    }

    public static int ParseFileId(byte[] payload)
        => BinaryPrimitives.ReadInt32BigEndian(payload);

    public static byte[] BuildFileChunk(int fileId, int index, byte[] data)
    {
        var msg = new byte[8 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(msg, fileId);
        BinaryPrimitives.WriteInt32BigEndian(msg.AsSpan(4), index);
        Buffer.BlockCopy(data, 0, msg, 8, data.Length);
        return msg;
    }

    public static (int FileId, int Index, byte[] Data) ParseFileChunk(byte[] payload)
    {
        var fileId = BinaryPrimitives.ReadInt32BigEndian(payload);
        var index = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        var data = new byte[payload.Length - 8];
        Buffer.BlockCopy(payload, 8, data, 0, data.Length);
        return (fileId, index, data);
    }

    public static byte[] BuildNotice(string text)
        => Encoding.UTF8.GetBytes(text);

    public static string ParseNotice(byte[] payload)
        => Encoding.UTF8.GetString(payload);
}
