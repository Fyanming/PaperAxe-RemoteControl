using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ZhifaRemote.Controls;
using ZhifaRemote.Models;
using ZhifaRemote.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;

namespace ZhifaRemote;

public partial class MainWindow : Window
{
    private readonly MainController _controller;
    private readonly ObservableCollection<string> _logs = new();
    private readonly ObservableCollection<TransferItem> _transfers = new();
    private readonly Dictionary<int, TransferItem> _transferById = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly Stopwatch _dynamicClock = new();
    private static readonly Color[] DynamicPalette =
    {
        Color.FromRgb(0x10, 0x18, 0x21),
        Color.FromRgb(0x45, 0xBF, 0xA5),
        Color.FromRgb(0xFF, 0x7A, 0x59),
        Color.FromRgb(0xEA, 0xF4, 0xFB)
    };
    private AppSettings _appSettings = new();
    private LinearGradientBrush? _dynamicBrush;
    private RemoteWindow? _remoteWindow;
    private bool _initialized;
    private bool _syncingBackgroundUi;
    private bool _lightStreamMode;
    private double _dynamicSpeed = 1;

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
        MainFrame.SizeChanged += MainFrame_OnSizeChanged;
        UpdateMainFrameClip();
        _appSettings = _settingsService.Load();
        SyncBackgroundUi();
        ApplyBackgroundSettings();
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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            SyncBackgroundUi();
        }
    }

    private void BackgroundMode_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingBackgroundUi || !_initialized) return;
        _appSettings.BackgroundMode = BgStaticRadio.IsChecked == true
            ? BackgroundMode.Static
            : BgDynamicRadio.IsChecked == true ? BackgroundMode.Dynamic : BackgroundMode.Default;
        UpdateBackgroundSections();
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void DynamicKind_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_syncingBackgroundUi || !_initialized) return;
        _appSettings.DynamicKind = VideoRadio.IsChecked == true
            ? DynamicBackgroundKind.Video
            : StreamRadio.IsChecked == true
                ? DynamicBackgroundKind.LightStream
                : DynamicBackgroundKind.Aurora;
        UpdateDynamicSections();
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void Swatch_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is RippleButton { Tag: string hex })
        {
            _appSettings.StaticColorHex = hex;
            UpdateSwatchHighlight();
            ApplyBackgroundSettings();
            SaveSettings();
        }
    }

    private void PickImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog(this) != true) return;
        _appSettings.StaticImagePath = dialog.FileName;
        ImagePathBox.Text = dialog.FileName;
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void ClearImageButton_Click(object sender, RoutedEventArgs e)
    {
        _appSettings.StaticImagePath = "";
        ImagePathBox.Text = "未选择图片";
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void PickVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择动态背景视频",
            Filter = "视频文件|*.mp4;*.wmv;*.avi;*.mkv;*.mov"
        };
        if (dialog.ShowDialog(this) != true) return;
        _appSettings.DynamicVideoPath = dialog.FileName;
        VideoPathBox.Text = dialog.FileName;
        VideoStatusText.Text = "循环播放";
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void ClearVideoButton_Click(object sender, RoutedEventArgs e)
    {
        _appSettings.DynamicVideoPath = "";
        VideoPathBox.Text = "未选择视频";
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void SpeedSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingBackgroundUi || !_initialized) return;
        _appSettings.AnimationSpeed = Math.Round(e.NewValue, 1);
        SpeedValueText.Text = $"{_appSettings.AnimationSpeed:0.0}x";
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void ResetBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        _appSettings = new AppSettings();
        SyncBackgroundUi();
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void SyncBackgroundUi()
    {
        _syncingBackgroundUi = true;
        BgDefaultRadio.IsChecked = _appSettings.BackgroundMode == BackgroundMode.Default;
        BgStaticRadio.IsChecked = _appSettings.BackgroundMode == BackgroundMode.Static;
        BgDynamicRadio.IsChecked = _appSettings.BackgroundMode == BackgroundMode.Dynamic;
        AuroraRadio.IsChecked = _appSettings.DynamicKind == DynamicBackgroundKind.Aurora;
        StreamRadio.IsChecked = _appSettings.DynamicKind == DynamicBackgroundKind.LightStream;
        VideoRadio.IsChecked = _appSettings.DynamicKind == DynamicBackgroundKind.Video;
        SpeedSlider.Value = _appSettings.AnimationSpeed;
        SpeedValueText.Text = $"{_appSettings.AnimationSpeed:0.0}x";
        ImagePathBox.Text = string.IsNullOrEmpty(_appSettings.StaticImagePath)
            ? "未选择图片"
            : _appSettings.StaticImagePath;
        VideoPathBox.Text = string.IsNullOrEmpty(_appSettings.DynamicVideoPath)
            ? "未选择视频"
            : _appSettings.DynamicVideoPath;
        _syncingBackgroundUi = false;
        UpdateBackgroundSections();
        UpdateDynamicSections();
        UpdateSwatchHighlight();
    }

    private void UpdateDynamicSections()
    {
        var videoSelected = VideoRadio.IsChecked == true;
        VideoPanel.Visibility = videoSelected ? Visibility.Visible : Visibility.Collapsed;
        SpeedSection.Visibility = videoSelected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateBackgroundSections()
    {
        StaticPanel.Visibility = BgStaticRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DynamicPanel.Visibility = BgDynamicRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TitleBar.Background = _appSettings.BackgroundMode == BackgroundMode.Default
            ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
    }

    private void ApplyBackgroundSettings()
    {
        StopDynamicBackground();
        switch (_appSettings.BackgroundMode)
        {
            case BackgroundMode.Static:
                ApplyStaticBackground();
                break;
            case BackgroundMode.Dynamic:
                StartDynamicBackground();
                break;
            default:
                MainFrame.Background = new SolidColorBrush(Color.FromArgb(0xD9, 0xF7, 0xFA, 0xFD));
                break;
        }
    }

    private void ApplyStaticBackground()
    {
        if (!string.IsNullOrEmpty(_appSettings.StaticImagePath) && File.Exists(_appSettings.StaticImagePath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(_appSettings.StaticImagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                MainFrame.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                return;
            }
            catch
            {
            }
        }
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_appSettings.StaticColorHex);
            MainFrame.Background = new SolidColorBrush(color);
        }
        catch
        {
            MainFrame.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFB));
        }
    }

    private void StartDynamicBackground()
    {
        StopDynamicBackground();
        if (_appSettings.DynamicKind == DynamicBackgroundKind.Video)
        {
            if (!string.IsNullOrEmpty(_appSettings.DynamicVideoPath) && File.Exists(_appSettings.DynamicVideoPath))
            {
                StartVideoBackground();
                return;
            }
            VideoStatusText.Text = "未找到视频文件，已切换为极光渐变";
        }
        _lightStreamMode = _appSettings.DynamicKind == DynamicBackgroundKind.LightStream;
        _dynamicSpeed = Math.Clamp(_appSettings.AnimationSpeed, 0.3, 2.5);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(DynamicPalette[0], 0.0));
        brush.GradientStops.Add(new GradientStop(DynamicPalette[1], 0.5));
        brush.GradientStops.Add(new GradientStop(DynamicPalette[2], 1.0));
        MainFrame.Background = brush;
        _dynamicBrush = brush;
        _dynamicClock.Restart();
        CompositionTarget.Rendering += DynamicTick;
    }

    private void StopDynamicBackground()
    {
        if (_dynamicBrush is not null)
        {
            CompositionTarget.Rendering -= DynamicTick;
            _dynamicBrush = null;
        }
        StopVideoBackground();
    }

    private void StartVideoBackground()
    {
        StopVideoBackground();
        MainFrame.Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x14));
        BackgroundVideo.Source = new Uri(_appSettings.DynamicVideoPath, UriKind.Absolute);
        BackgroundVideo.Visibility = Visibility.Visible;
        BackgroundVideo.Play();
    }

    private void StopVideoBackground()
    {
        if (BackgroundVideo.Source is null && BackgroundVideo.Visibility == Visibility.Collapsed) return;
        BackgroundVideo.Close();
        BackgroundVideo.Source = null;
        BackgroundVideo.Visibility = Visibility.Collapsed;
    }

    private void BackgroundVideo_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        BackgroundVideo.Position = TimeSpan.Zero;
        BackgroundVideo.Play();
    }

    private void BackgroundVideo_OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        VideoStatusText.Text = "视频播放失败，请检查文件格式与解码器";
        if (_appSettings.DynamicKind != DynamicBackgroundKind.Video) return;
        _appSettings.DynamicKind = DynamicBackgroundKind.Aurora;
        SyncBackgroundUi();
        ApplyBackgroundSettings();
        SaveSettings();
    }

    private void DynamicTick(object? sender, EventArgs e)
    {
        if (_dynamicBrush is null) return;
        var t = _dynamicClock.Elapsed.TotalSeconds * _dynamicSpeed;
        if (_lightStreamMode)
        {
            var cycle = (t % 16.0) / 16.0;
            _dynamicBrush.StartPoint = new Point(-0.25 + cycle * 1.5, 0.15 - cycle * 0.3);
            _dynamicBrush.EndPoint = new Point(1.25 - cycle * 1.5, 0.85 + cycle * 0.3);
        }
        else
        {
            var stops = _dynamicBrush.GradientStops;
            for (var i = 0; i < stops.Count; i++)
            {
                var phase = (t + i * 6.0) % 12.0;
                var segment = (int)(phase / 4.0);
                var progress = (phase - segment * 4.0) / 4.0;
                stops[i].Color = LerpColor(
                    DynamicPalette[segment % DynamicPalette.Length],
                    DynamicPalette[(segment + 1) % DynamicPalette.Length],
                    progress);
            }
        }
    }

    private static Color LerpColor(Color a, Color b, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * progress),
            (byte)(a.G + (b.G - a.G) * progress),
            (byte)(a.B + (b.B - a.B) * progress));
    }

    private void UpdateSwatchHighlight()
    {
        foreach (var child in ColorSwatchPanel.Children)
        {
            if (child is RippleButton button && button.Tag is string hex)
            {
                var selected = hex.Equals(_appSettings.StaticColorHex, StringComparison.OrdinalIgnoreCase);
                button.BorderBrush = new SolidColorBrush(selected
                    ? Color.FromRgb(0xFF, 0x7A, 0x59)
                    : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
                button.BorderThickness = new Thickness(selected ? 3 : 1);
            }
        }
    }

    private void SaveSettings()
        => _settingsService.Save(_appSettings);

    private void MainFrame_OnSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateMainFrameClip();

    private void UpdateMainFrameClip()
    {
        if (MainFrame.ActualWidth <= 0 || MainFrame.ActualHeight <= 0) return;
        MainFrame.Clip = new RectangleGeometry(
            new Rect(0, 0, MainFrame.ActualWidth, MainFrame.ActualHeight),
            22,
            22);
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
        StopDynamicBackground();
        _settingsService.Save(_appSettings);
        _controller.Log -= AddLog;
        _controller.ServerStateChanged -= UpdateServerStatus;
        _controller.ClientStateChanged -= UpdateClientStatus;
        _controller.RemoteWindowRequested -= CreateRemoteWindow;
        _controller.TransferChanged -= OnTransferChanged;
        _controller.Dispose();
    }
}
