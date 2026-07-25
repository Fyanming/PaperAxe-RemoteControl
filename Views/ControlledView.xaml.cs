using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LanRemoteControl.Core;
using LanRemoteControl.Models;

namespace LanRemoteControl.Views
{
    public partial class ControlledView : UserControl
    {
        public event Action<string, bool, bool>? StatusChanged;
        // 远控端连接进入：(远控端IP, 模式)
        public event Action<string, string>? IncomingConnection;

        private readonly ObservableCollection<ClientInfo> _clients = new();
        private bool _listening;
        private const int Port = 7321;

        public ControlledView()
        {
            InitializeComponent();
            ClientList.ItemsSource = _clients;
            // 监听 NetworkServer 的事件
            NetworkServer.ClientConnected += OnClientConnected;
            NetworkServer.ClientDisconnected += OnClientDisconnected;
            NetworkServer.ModeChanged += OnModeChanged;
        }

        // ===== 监听开关 =====
        private async void Listen_Click(object sender, MouseButtonEventArgs e)
        {
            _listening = !_listening;
            UpdateToggle();
            if (_listening)
            {
                try
                {
                    await NetworkServer.StartAsync(Port);
                    PortValue.Text = Port.ToString();
                    StatusChanged?.Invoke($"监听 :{Port}", true, false);
                    ToastNotifier.Show("已开启被控", $"监听端口 {Port} · 等待连接");
                }
                catch (Exception ex)
                {
                    _listening = false;
                    UpdateToggle();
                    ToastNotifier.Show("启动失败", ex.Message);
                }
            }
            else
            {
                NetworkServer.Stop();
                PortValue.Text = "—";
                StatusChanged?.Invoke("未连接", false, false);
                foreach (var c in _clients.ToArray()) RemoveClient(c.Ip);
                ToastNotifier.Show("已关闭被控", "不再接受新连接");
            }
        }

        private void UpdateToggle()
        {
            ListenToggle.BorderBrush = _listening ? FindResource("GreenBrush") as Brush : FindResource("LineBrush") as Brush;
            ListenToggle.Background = _listening ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF7, 0xF0)) : new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xEE));
            ToggleSwitch.Background = _listening ? FindResource("GreenBrush") as Brush : FindResource("LineBrush") as Brush;
            var targetX = _listening ? 18 : 0;
            var anim = new DoubleAnimation(KnobTrans.X, targetX, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new ElasticEase { Springiness = 6, Oscillations = 1, EasingMode = EasingMode.EaseOut }
            };
            KnobTrans.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // ===== 端口复制 =====
        private void PortCopy_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_listening) return;
            try
            {
                Clipboard.SetText(Port.ToString());
                PortCopyText.Text = "已复制";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (s, e2) => { PortCopyText.Text = "复制"; timer.Stop(); };
                timer.Start();
            }
            catch { /* 剪贴板被占用，忽略 */ }
        }

        // ===== 客户端管理 =====
        private void OnClientConnected(string ip, string mode)
        {
            Dispatcher.Invoke(() =>
            {
                if (_clients.Any(c => c.Ip == ip)) return;
                _clients.Add(new ClientInfo { Ip = ip, Mode = ModeName(mode) });
                UpdateClientEmpty();
                // 触发右下角通知 + 音量归零 + 切换音频设备
                IncomingConnection?.Invoke(ip, ModeName(mode));
            });
        }

        private void OnClientDisconnected(string ip)
        {
            Dispatcher.Invoke(() => RemoveClient(ip));
        }

        private void OnModeChanged(string ip, string mode)
        {
            Dispatcher.Invoke(() =>
            {
                var client = _clients.FirstOrDefault(c => c.Ip == ip);
                if (client != null) client.Mode = ModeName(mode);
            });
        }

        private void RemoveClient(string ip)
        {
            var c = _clients.FirstOrDefault(x => x.Ip == ip);
            if (c != null) _clients.Remove(c);
            UpdateClientEmpty();
            if (_clients.Count == 0) AudioDeviceManager.OnRemoteEnd();
        }

        private void UpdateClientEmpty() => ClientEmpty.Visibility = _clients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void Kick_Click(object sender, MouseButtonEventArgs e)
        {
            var ip = (string)((TextBlock)sender).Tag;
            NetworkServer.KickClient(ip);
            RemoveClient(ip);
        }

        private static string ModeName(string m) => m switch
        {
            "Control" => "远程控制",
            "View" => "观看模式",
            "File" => "文件传输",
            _ => m
        };

        // 暴露给外部触发（同应用两端互通时）
        public void SimulateIncoming(string ip, string mode) => OnClientConnected(ip, mode);
    }
}
