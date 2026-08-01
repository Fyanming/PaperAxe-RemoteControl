using NAudio.Wave;

namespace ZhifaRemote.Services;

public sealed class AudioLoopbackService : IDisposable
{
    private readonly object _sync = new();
    private WasapiLoopbackCapture? _capture;
    private bool _running;

    public bool IsRunning => _running;

    public event Action<byte[], int, short>? AudioCaptured;

    public void Start()
    {
        lock (_sync)
        {
            if (_running) return;
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _running = true;
            _capture.StartRecording();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopCore();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running || e.BytesRecorded <= 0 || _capture is null) return;
        var format = _capture.WaveFormat;
        byte[] pcm;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            pcm = FloatToPcm16(e.Buffer, e.BytesRecorded);
        }
        else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            pcm = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);
        }
        else
        {
            return;
        }

        AudioCaptured?.Invoke(pcm, format.SampleRate, (short)format.Channels);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            _running = false;
            _capture?.Dispose();
            _capture = null;
        }
    }

    private static byte[] FloatToPcm16(byte[] buffer, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / 4;
        var result = new byte[sampleCount * 2];
        for (var i = 0; i < sampleCount; i++)
        {
            var value = BitConverter.ToSingle(buffer, i * 4);
            value = Math.Clamp(value, -1f, 1f);
            var sample = (short)(value * 32767f);
            result[i * 2] = (byte)(sample & 0xFF);
            result[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        return result;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            StopCore();
            _capture?.Dispose();
            _capture = null;
        }
    }

    private void StopCore()
    {
        if (!_running) return;
        _running = false;
        try
        {
            _capture?.StopRecording();
        }
        catch
        {
        }
    }
}
