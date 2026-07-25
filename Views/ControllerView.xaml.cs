using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LanRemoteControl.Core;
using LanRemoteControl.Models;

namespace LanRemoteControl.Views
{
    public partial class ControllerView : UserControl
    {
        // 状态变更事件（向主窗口广播顶栏状态）
        public event Action<string, bool, bool>? StatusChanged;

        private readonly ObservableCollection<FileTask> _files = new();
        private string _mode = "Control";
        private bool _connecting, _connected;
        private int _resW = 1280, _resH = 720;

        // 全屏状态
        private bool _isFullscreen;
        private Window? _fsWindow;

        public ControllerView()
        {
            InitializeComponent();
            FileList.ItemsSource = _files;
            AllowDrop = true;
            // 订阅帧接收事件 + 断连事件
            NetworkClient.FrameReceived += OnFrameReceived;
            NetworkClient.Disconnected += OnDisconnected;
        }

        // ===== 帧到达：切回 UI 线程更新 Image =====
        private void OnFrameReceived(byte[] jpeg, int w, int h)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    using (var ms = new System.IO.MemoryStream(jpeg))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    bmp.Freeze(); // 跨线程安全
                    RemoteImage.Source = bmp;
                    // 更新分辨率显示
                    if (w > 0 && h > 0) MetaRes.Text = $"{w}×{h}";
                }
                catch { /* 解码失败忽略，等下一帧 */ }
            });
        }

        // ===== 分辨率切换 =====
        private void ResCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResCombo.SelectedItem is not ComboBoxItem item) return;
            var tag = item.Tag?.ToString() ?? "1280x720";
            var seg = tag.Split('x');
            if (seg.Length != 2 || !int.TryParse(seg[0], out int w) || !int.TryParse(seg[1], out int h)) return;
            _resW = w; _resH = h;
            // 原画质：使用主屏物理分辨率
            if (w == 0 || h == 0)
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen != null)
                {
                    _resW = screen.Bounds.Width;
                    _resH = screen.Bounds.Height;
                }
            }
            // 已连接则发送新分辨率
            if (_connected) NetworkClient.SendMode(_mode, _resW, _resH);
        }

        // ===== 全屏切换 =====
        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (!_isFullscreen) EnterFullscreen();
            else ExitFullscreen();
        }

        private void EnterFullscreen()
        {
            if (!_connected) return;
            _isFullscreen = true;
            FullscreenBtn.Content = "⤡ 退出";
            // 创建独立全屏窗口承载 RemoteImage
            _fsWindow = new Window
            {
                Title = "远程画面（全屏）",
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Maximized,
                Background = Brushes.Black,
                Content = new Image
                {
                    Source = RemoteImage.Source,
                    Stretch = Stretch.Uniform,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                }
            };
            _fsWindow.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) ExitFullscreen();
            };
            _fsWindow.Closed += (s, e) => ExitFullscreen();
            _fsWindow.Show();
            // 持续把帧同步到全屏窗口
            CompositionTarget.Rendering += SyncFsImage;
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen) return;
            _isFullscreen = false;
            FullscreenBtn.Content = "⤢ 全屏";
            CompositionTarget.Rendering -= SyncFsImage;
            if (_fsWindow != null)
            {
                _fsWindow.Close();
                _fsWindow = null;
            }
        }

        private void SyncFsImage(object? sender, EventArgs e)
        {
            if (_fsWindow?.Content is Image img)
            {
                img.Source = RemoteImage.Source;
            }
        }

        private void OnDisconnected()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_connected || _connecting) Disconnect();
            });
        }

        // ===== 模式选择 =====
        private void Mode_Click(object sender, MouseButtonEventArgs e)
        {
            var clicked = (Border)sender;
            var selectedBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xEE));
            var normalBg = FindResource("BgElevBrush") as Brush;
            foreach (var child in ModeGroup.Children.OfType<Border>())
            {
                var radio = child.FindVisualChild<Ellipse>();
                bool selected = child == clicked;
                child.BorderBrush = selected ? FindResource("InkBrush") as Brush : FindResource("LineBrush") as Brush;
                child.Background = selected ? selectedBg : normalBg;
                if (radio != null)
                    radio.Fill = selected ? FindResource("AccentBrush") as Brush : Brushes.Transparent;
            }
            _mode = clicked.Tag?.ToString() ?? "Control";
            if (_connected) NetworkClient.SendMode(_mode, _resW, _resH);
        }

        // ===== 建立连接 =====
        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_connecting || _connected)
            {
                Disconnect();
                return;
            }
            var ip = IpInput.Text.Trim();
            if (!IsValidIP(ip))
            {
                ToastNotifier.Show("地址无效", "请输入合法的 IPv4 地址");
                return;
            }
            ConnectTo(ip);
        }

        private static bool IsValidIP(string ip) =>
            System.Text.RegularExpressions.Regex.IsMatch(ip, @"^(\d{1,3}\.){3}\d{1,3}$") &&
            ip.Split('.').All(n => int.TryParse(n, out var v) && v >= 0 && v <= 255);

        private async void ConnectTo(string ip)
        {
            _connecting = true;
            UpdateStatus("正在连接 " + ip, false, true);
            try
            {
                await NetworkClient.ConnectAsync(ip, 7321);
                _connecting = false;
                _connected = true;
                OnConnected(ip);
            }
            catch (Exception ex)
            {
                _connecting = false;
                UpdateStatus("连接失败", false, false);
                ToastNotifier.Show("连接失败", ex.Message);
            }
        }

        private void OnConnected(string ip)
        {
            UpdateStatus("已连接 " + ip, true, false);
            ConnectBtn.Content = "断开连接";
            // 启动画面占位 + 红点闪烁
            ScreenPlaceholder.Visibility = Visibility.Collapsed;
            LiveDot.Opacity = 1;
            var blink = new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(0.6))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
            };
            LiveDot.BeginAnimation(OpacityProperty, blink);
            MetaRes.Text = "—";
            MetaLatency.Text = "—";
            MetaBitrate.Text = "—";
            NetworkClient.SendMode(_mode, _resW, _resH);
            ToastNotifier.Show("连接已建立", "远控通道开启 → " + ip);
        }

        private void Disconnect()
        {
            _connected = false;
            _connecting = false;
            NetworkClient.Disconnect();
            UpdateStatus("未连接", false, false);
            ConnectBtn.Content = "建立连接";
            ScreenPlaceholder.Visibility = Visibility.Visible;
            RemoteImage.Source = null;
            LiveDot.Opacity = 0;
            LiveDot.BeginAnimation(OpacityProperty, null);
            MetaRes.Text = "—";
            MetaLatency.Text = "—";
            MetaBitrate.Text = "—";
        }

        private void UpdateStatus(string text, bool online, bool connecting) =>
            StatusChanged?.Invoke(text, online, connecting);

        // ===== 文件传输 =====
        private void FilePick_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (dlg.ShowDialog() == true) HandleFiles(dlg.FileNames);
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                HandleFiles((string[])e.Data.GetData(DataFormats.FileDrop));
        }

        private async void HandleFiles(System.Collections.Generic.IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                var info = new System.IO.FileInfo(path);
                var task = new FileTask { Name = info.Name, SizeDisplay = FmtSize(info.Length), Status = "Sending" };
                _files.Add(task);
                FileCount.Text = _files.Count + " 个文件";
                try
                {
                    await NetworkClient.SendFileAsync(path);
                    task.Status = "Done";
                }
                catch { task.Status = "Fail"; }
            }
        }

        private static string FmtSize(long b) => b < 1024 ? b + " B" :
                                                  b < 1048576 ? (b / 1024.0).ToString("F1") + " KB" :
                                                  (b / 1048576.0).ToString("F2") + " MB";
    }

    // 可视化树查找扩展
    internal static class Ext
    {
        public static T? FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = child.FindVisualChild<T>();
                if (result != null) return result;
            }
            return null;
        }
    }
}
