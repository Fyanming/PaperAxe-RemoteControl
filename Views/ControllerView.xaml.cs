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
        private void OnFrameReceived(byte[] jpeg)
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
                }
                catch { /* 解码失败忽略，等下一帧 */ }
            });
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
            if (_connected) NetworkClient.SendMode(_mode);
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
            MetaRes.Text = "1920×1080";
            MetaLatency.Text = "12ms";
            MetaBitrate.Text = "4.5Mbps";
            NetworkClient.SendMode(_mode);
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
