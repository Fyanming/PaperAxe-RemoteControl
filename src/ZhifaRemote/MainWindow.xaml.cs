using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZhifaRemote.Helpers;
using ZhifaRemote.Models;
using ZhifaRemote.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ZhifaRemote;

public partial class MainWindow : Window
{
    private readonly MainController _controller;
    private readonly ObservableCollection<string> _logs = new();
    private readonly ObservableCollection<TransferItem> _transfers = new();
    private readonly Dictionary<int, TransferItem> _transferById = new();
    private RemoteWindow? _remoteWindow;
    private bool _initialized;

    public MainWindow()
    {
        _controller = new MainController(Dispatcher);
        InitializeComponent();
        LogList.ItemsSource = _logs;
        TransfersList.ItemsSource = _transfers;
        _controller.Log += AddLog;
        _controller.ServerStateChanged += UpdateServerStatus;
        _controller.ClientStateChanged += UpdateClientStatus;
        _controller.RemoteWindowRequested += CreateRemoteWindow;
        _controller.TransferChanged += OnTransferChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _initialized = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowBlur.EnableAcrylic(this, System.Windows.Media.Color.FromArgb(150, 245, 248, 252));
        PlayEntranceAnimation();
        RefreshLocalIps();
        RefreshAudioStatus();
        UpdateServerStatus();
        UpdateClientStatus();
    }

    private void PlayEntranceAnimation()
    {
        var cards = new FrameworkElement[]
        {
            ConnectionCard, AudioCard, ModeCard, QualityCard, FileCard, LogCard
        };
        for (var i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            card.Opacity = 0;
            card.RenderTransform = new TranslateTransform(0, 14);
            var storyboard = new Storyboard
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 40)
            };
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            storyboard.Children.Add(fade);
            storyboard.Children.Add(slide);
            storyboard.Begin(card);
        }
    }

    private void RefreshLocalIps()
    {
        try
        {
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToArray();
            LocalIpText.Text = ips.Length == 0 ? "未检测到局域网 IP" : string.Join("  ", ips);
        }
        catch
        {
            LocalIpText.Text = "本机 IP 读取失败";
        }
    }

    private void RefreshAudioStatus()
    {
        try
        {
            var devices = _controller.AudioControl.ListDevices();
            var virtualDevice = devices.FirstOrDefault(d => d.IsVirtual);
            AudioStatusText.Text = virtualDevice is null
                ? "未检测到虚拟输出设备。建议安装 VB-CABLE 虚拟声卡，安装后远控连接会自动切换默认输出并回传被控端声音。"
                : $"已检测到虚拟输出设备：{virtualDevice.Name}，远控连接时自动切换并回传声音。";
        }
        catch
        {
            AudioStatusText.Text = "音频设备检测失败，请检查系统音频服务。";
        }
    }

    private void AddLog(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddLog(text));
            return;
        }
        _logs.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
        while (_logs.Count > 300) _logs.RemoveAt(0);
        if (LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
    }

    private void UpdateServerStatus()
    {
        var running = _controller.Server.IsRunning;
        var sessions = _controller.Server.GetSessions().Count;
        StartServerButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        StopServerButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ServerStatusText.Text = running
            ? $"已监听 0.0.0.0:{_controller.Server.Port}，共 {sessions} 台远控端接入"
            : "等待启动，启动后输入本机 IP 即可被远控";
        if (RoleServerRadio.IsChecked == true)
        {
            ConnectionDot.Fill = running
                ? (SolidColorBrush)FindResource("MintBrush")
                : (SolidColorBrush)FindResource("MutedBrush");
            ConnectionStateText.Text = running
                ? sessions > 0 ? $"{sessions} 台已连接" : "监听中"
                : "未监听";
        }
    }

    private void UpdateClientStatus()
    {
        var connected = _controller.IsClientConnected;
        ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ClientStatusText.Text = connected
            ? $"已连接 {_controller.Client!.RemoteIp}，远控窗口即将打开"
            : "输入被控端局域网 IP 与端口，自动建立远控连接";
        if (RoleClientRadio.IsChecked == true)
        {
            ConnectionDot.Fill = connected
                ? (SolidColorBrush)FindResource("MintBrush")
                : (SolidColorBrush)FindResource("MutedBrush");
            ConnectionStateText.Text = connected ? "已连接" : "未连接";
        }
        if (!connected && _remoteWindow is not null)
        {
            _remoteWindow.Close();
            _remoteWindow = null;
        }
    }

    private void CreateRemoteWindow(RemoteWindowInfo info)
    {
        if (_remoteWindow is not null)
        {
            _remoteWindow.Close();
        }
        _remoteWindow = new RemoteWindow(_controller, info);
        _remoteWindow.Closed += (_, _) => _remoteWindow = null;
        _remoteWindow.Show();
    }

    private void OnTransferChanged(TransferItem item)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnTransferChanged(item));
            return;
        }
        if (_transferById.TryGetValue(item.Id, out var existing))
        {
            existing.Progress = item.Progress;
            existing.State = item.State;
        }
        else
        {
            _transferById[item.Id] = item;
            _transfers.Add(item);
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Role_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var serverMode = RoleServerRadio.IsChecked == true;
        ServerModePanel.Visibility = serverMode ? Visibility.Visible : Visibility.Collapsed;
        ClientModePanel.Visibility = serverMode ? Visibility.Collapsed : Visibility.Visible;
        UpdateServerStatus();
        UpdateClientStatus();
    }

    private async void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ServerPortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            AddLog("端口无效，请输入 1-65535");
            return;
        }
        await _controller.StartServerAsync(port);
        UpdateServerStatus();
    }

    private void StopServerButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.StopServer();
        UpdateServerStatus();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var ip = ClientIpBox.Text.Trim();
        if (!int.TryParse(ClientPortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            AddLog("端口无效，请输入 1-65535");
            return;
        }
        await _controller.ConnectAsync(ip, port);
        UpdateClientStatus();
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.Disconnect();
        UpdateClientStatus();
    }

    private void Mode_OnChecked(object sender, RoutedEventArgs e)
        => _controller.SetControlMode(ControlModeRadio.IsChecked == true);

    private void Quality_OnChecked(object sender, RoutedEventArgs e)
    {
        var index = 0;
        if (QualityMediumRadio.IsChecked == true) index = 1;
        else if (QualityHighRadio.IsChecked == true) index = 2;
        else if (QualityUltraRadio.IsChecked == true) index = 3;
        _controller.SetQuality(index);
    }

    private void FpsSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        FpsValueText.Text = $"{e.NewValue:F0} FPS";
        _controller.SetFps((int)e.NewValue);
    }

    private async void SendFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要发送的文件",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        await _controller.SendFileAsync(dialog.FileName);
    }

    private void OpenVirtualButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AddLog($"打开下载页失败: {ex.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _controller.Log -= AddLog;
        _controller.ServerStateChanged -= UpdateServerStatus;
        _controller.ClientStateChanged -= UpdateClientStatus;
        _controller.RemoteWindowRequested -= CreateRemoteWindow;
        _controller.TransferChanged -= OnTransferChanged;
        _controller.Dispose();
    }
}
