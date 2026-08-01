using NAudio.Wave;

namespace ZhifaRemote.Services;

public sealed class AudioPlaybackService : IDisposable
{
    private readonly object _sync = new();
    private BufferedWaveProvider? _provider;
    private WaveOutEvent? _waveOut;
    private WaveFormat? _format;
    private bool _started;

    public void EnsureStarted(int sampleRate, short channels)
    {
        var format = new WaveFormat(Math.Max(8000, sampleRate), 16, Math.Clamp(channels, (short)1, (short)2));
        lock (_sync)
        {
            if (_started && _format is not null &&
                _format.SampleRate == format.SampleRate &&
                _format.Channels == format.Channels)
            {
                return;
            }

            StopCore();
            _format = format;
            _provider = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromSeconds(3),
                DiscardOnBufferOverflow = true
            };
            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 120
            };
            _waveOut.Init(_provider);
            _waveOut.Play();
            _started = true;
        }
    }

    public void Play(byte[] pcm)
    {
        if (!_started || _provider is null || pcm.Length == 0) return;
        _provider.AddSamples(pcm, 0, pcm.Length);
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopCore();
        }
    }

    private void StopCore()
    {
        _started = false;
        try
        {
            _waveOut?.Stop();
        }
        catch
        {
        }
        _waveOut?.Dispose();
        _waveOut = null;
        _provider = null;
        _format = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
