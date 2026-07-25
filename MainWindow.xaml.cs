using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanRemoteControl
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ControllerPanel.StatusChanged += UpdateStatus;
            ControlledPanel.StatusChanged += UpdateStatus;
            ControlledPanel.IncomingConnection += OnIncomingConnection;
        }

        // 顶栏 Tab 切换：淡入淡出 + 微位移
        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            TabController.Tag = null;
            TabControlled.Tag = null;
            btn.Tag = "Active";

            bool toController = btn == TabController;
            UserControl showPanel = toController ? ControllerPanel : ControlledPanel;
            UserControl hidePanel = toController ? ControlledPanel : ControllerPanel;

            // 旧面板淡出
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var moveOut = new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            hidePanel.BeginAnimation(OpacityProperty, fadeOut);
            var trans = new TranslateTransform();
            hidePanel.RenderTransform = trans;
            trans.BeginAnimation(TranslateTransform.YProperty, moveOut);

            // 延迟 80ms 后显示新面板（人工感延迟）
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (s, e2) =>
            {
                hidePanel.Visibility = Visibility.Collapsed;
                showPanel.Visibility = Visibility.Visible;
                showPanel.Opacity = 0;
                var trans2 = new TranslateTransform { Y = 8 };
                showPanel.RenderTransform = trans2;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var moveIn = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                showPanel.BeginAnimation(OpacityProperty, fadeIn);
                trans2.BeginAnimation(TranslateTransform.YProperty, moveIn);
                timer.Stop();
            };
            timer.Start();
        }

        private void UpdateStatus(string text, bool online, bool connecting)
        {
            StatusText.Text = text;
            StatusLamp.Fill = online ? FindResource("GreenBrush") as Brush :
                               connecting ? FindResource("AccentBrush") as Brush :
                               FindResource("InkFaintBrush") as Brush;
        }

        // 远控端连接成功 → 通知被控端（同一应用两端可互通）
        private void OnIncomingConnection(string remoteIp, string mode)
        {
            // 触发右下角 Toast 通知 + 音量归零 + 切换音频设备
            Core.ToastNotifier.Show($"你已被 {remoteIp} 远控", $"模式：{mode} · 音频已转发");
            Core.AudioDeviceManager.OnRemoteBegin();
        }
    }
}
