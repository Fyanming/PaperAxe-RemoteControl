using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ZhifaRemote.Services;

public sealed class ScreenCaptureService : IDisposable
{
    private Thread? _thread;
    private volatile bool _running;
    private volatile int _fps = 10;
    private volatile int _quality = 70;
    private double _scale = 0.75;
    public int Fps
    {
        get => _fps;
        set => _fps = Math.Clamp(value, 1, 60);
    }

    public int Quality
    {
        get => _quality;
        set => _quality = Math.Clamp(value, 20, 95);
    }

    public double Scale
    {
        get => _scale;
        set => _scale = Math.Clamp(value, 0.3, 1.0);
    }

    public bool IsRunning => _running;

    public event Action<byte[], int, int>? FrameCaptured;

    public static (int Width, int Height) GetVirtualScreenSize()
    {
        var bounds = GetVirtualScreenBounds();
        return (bounds.Width, bounds.Height);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "ScreenCapture" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        if (_thread is { IsAlive: true })
        {
            _thread.Join(500);
        }
        _thread = null;
    }

    private void CaptureLoop()
    {
        var sw = new Stopwatch();
        var bounds = GetVirtualScreenBounds();
        var sourceWidth = bounds.Width;
        var sourceHeight = bounds.Height;

        while (_running)
        {
            sw.Restart();
            try
            {
                var targetW = Math.Max(1, (int)(sourceWidth * _scale));
                var targetH = Math.Max(1, (int)(sourceHeight * _scale));
                using var bmp = new Bitmap(targetW, targetH, PixelFormat.Format24bppRgb);
                if (_scale >= 0.999)
                {
                    using var g = Graphics.FromImage(bmp);
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(sourceWidth, sourceHeight));
                }
                else
                {
                    using var full = new Bitmap(sourceWidth, sourceHeight, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(full))
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(sourceWidth, sourceHeight));
                    }
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(full, 0, 0, targetW, targetH);
                    }
                }
                var jpeg = EncodeJpeg(bmp, _quality);
                FrameCaptured?.Invoke(jpeg, sourceWidth, sourceHeight);
            }
            catch (Exception)
            {
                // 截屏瞬间失败时跳过本帧
            }

            var elapsed = sw.ElapsedMilliseconds;
            var interval = 1000.0 / _fps;
            var wait = (int)Math.Max(0, interval - elapsed);
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    private static Rectangle GetVirtualScreenBounds()
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            left = Math.Min(left, screen.Bounds.Left);
            top = Math.Min(top, screen.Bounds.Top);
            right = Math.Max(right, screen.Bounds.Right);
            bottom = Math.Max(bottom, screen.Bounds.Bottom);
        }
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static byte[] EncodeJpeg(Bitmap bmp, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ms = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bmp.Save(ms, codec, parameters);
        return ms.ToArray();
    }

    public void Dispose()
    {
        Stop();
    }
}
