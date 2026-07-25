using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanRemoteControl.Core
{
    // ============================================================
    //  系统右下角通知
    //  使用 NotifyIcon 托盘气泡（无额外依赖）
    //  需引用 System.Windows.Forms 与 System.Drawing
    // ============================================================
    public static class ToastNotifier
    {
        private static NotifyIcon? _icon;
        private static readonly object _lock = new();

        private static NotifyIcon Icon
        {
            get
            {
                lock (_lock)
                {
                    if (_icon == null)
                    {
                        _icon = new NotifyIcon
                        {
                            Visible = true,
                            Text = "纸笺 · 局域网远控",
                            // 使用系统自带信息图标
                            Icon = SystemIcons.Information
                        };
                    }
                    return _icon;
                }
            }
        }

        public static void Show(string title, string body)
        {
            try
            {
                Icon.BalloonTipTitle = title;
                Icon.BalloonTipText = body;
                Icon.ShowBalloonTip(5000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ToastNotifier] 失败: " + ex.Message);
            }
        }

        public static void Dispose()
        {
            lock (_lock)
            {
                _icon?.Dispose();
                _icon = null;
            }
        }
    }
}
