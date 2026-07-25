using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace LanRemoteControl.Core
{
    // ============================================================
    //  屏幕捕获（GDI+ BitBlt → JPEG 编码）
    //  - 支持任意目标分辨率（由远控端指定）
    //  - JPEG quality 60，单帧约 50-200 KB
    //  - 局域网下 10 FPS 约 1-2 Mbps
    // ============================================================
    public static class ScreenCapture
    {
        private const int JpegQuality = 60;

        private static readonly ImageCodecInfo JpegEncoder = GetEncoder(ImageFormat.Jpeg);
        private static readonly EncoderParameters EncoderParams = BuildParams(JpegQuality);

        /// <summary>捕获主屏 → 按指定尺寸降采样 → JPEG 字节数组</summary>
        public static byte[] CaptureToJpeg(int targetWidth, int targetHeight)
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen
                         ?? throw new InvalidOperationException("未检测到屏幕");
            var bounds = screen.Bounds;
            using var src = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(src))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            // 降采样到目标尺寸（保持比例由调用方保证）
            using var dst = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.InterpolationMode = InterpolationMode.Low;
                g.SmoothingMode = SmoothingMode.HighSpeed;
                g.DrawImage(src, 0, 0, targetWidth, targetHeight);
            }
            using var ms = new MemoryStream(64 * 1024);
            dst.Save(ms, JpegEncoder, EncoderParams);
            return ms.ToArray();
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.FormatID == format.Guid) return codec;
            }
            throw new InvalidOperationException("未找到 JPEG 编码器");
        }

        private static EncoderParameters BuildParams(long quality)
        {
            var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            return p;
        }
    }
}
