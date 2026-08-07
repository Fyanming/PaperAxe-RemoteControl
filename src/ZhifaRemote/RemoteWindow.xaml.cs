using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using ZhifaRemote.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;

namespace ZhifaRemote;

public partial class RemoteWindow : Window
{
    private static readonly HashSet<Key> ExtendedKeys = new()
    {
        Key.Insert, Key.Delete, Key.Home, Key.End, Key.PageUp, Key.PageDown,
        Key.Left, Key.Right, Key.Up, Key.Down,
        Key.RightCtrl, Key.RightAlt
    };

    private readonly MainController _controller;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly string _peerIp;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _fpsTimer;
    private readonly DispatcherTimer _latencyTimer;
    private readonly System.Threading.Timer _inputFlushTimer;
    private readonly object _inputLock = new();
    private readonly object _frameLock = new();
    private int _frameCount;
    private volatile bool _controlMode = true;
    private bool _initialized;
    private (int X, int Y, bool Valid) _pendingMove;
    private byte[]? _latestFrame;
    private volatile bool _frameRenderScheduled;

    public RemoteWindow(MainController controller, RemoteWindowInfo info)
    {
        _controller = controller;
        InitializeComponent();
        _screenWidth = info.ScreenWidth;
        _screenHeight = info.ScreenHeight;
        _peerIp = info.PeerIp;
        TitleText.Text = $"正在远控 {info.HostName} · {info.PeerIp}";

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        _hideTimer.Tick += (_, _) => HideBars();
        _fpsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fpsTimer.Tick += (_, _) =>
        {
            StatusText.Text = $"画面 {_frameCount} FPS · 远端分辨率 {_screenWidth}×{_screenHeight} · {_peerIp}";
            _frameCount = 0;
        };
        _latencyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _latencyTimer.Tick += (_, _) => _ = _controller.SendPingAsync();
        _inputFlushTimer = new System.Threading.Timer(
            _ => FlushPendingMoveFromTimer(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        _controller.FrameReceived += OnFrameReceived;
        _controller.LatencyChanged += OnLatencyChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _initialized = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Focus();
        RootGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        RootGrid.RenderTransform = new ScaleTransform(0.94, 0.94);
        Opacity = 0;

        var storyboard = new Storyboard();
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleX = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleY = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTargetProperty(fade, new PropertyPath(Window.OpacityProperty));
        Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
        storyboard.Children.Add(fade);
        storyboard.Children.Add(scaleX);
        storyboard.Children.Add(scaleY);
        storyboard.Begin(this);

        ShowBars();
        _fpsTimer.Start();
        _latencyTimer.Start();
        _ = _controller.SendPingAsync();
        PrivacyToggle.IsChecked = _controller.PrivacyEnabled;
        AudioToggle.IsChecked = _controller.AudioEnabled;
    }

    private void OnFrameReceived(int width, int height, byte[] jpeg)
    {
        _frameCount++;
        lock (_frameLock)
        {
            _latestFrame = jpeg;
        }
        if (_frameRenderScheduled) return;
        _frameRenderScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(RenderLatestFrame));
    }

    private void RenderLatestFrame()
    {
        byte[] frame;
        lock (_frameLock)
        {
            frame = _latestFrame!;
            _latestFrame = null;
        }
        if (frame is null)
        {
            _frameRenderScheduled = false;
            return;
        }
        _ = Task.Run(() => DecodeAndShowFrame(frame));
    }

    private void DecodeAndShowFrame(byte[] jpeg)
    {
        BitmapImage? bitmap = null;
        try
        {
            using var stream = new MemoryStream(jpeg);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            bitmap = image;
        }
        catch
        {
        }

        try
        {
            Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (bitmap is not null)
                {
                    ScreenImage.Source = bitmap;
                }
                _frameRenderScheduled = false;
                lock (_frameLock)
                {
                    if (_latestFrame is not null)
                    {
                        _frameRenderScheduled = true;
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(RenderLatestFrame));
                    }
                }
            }));
        }
        catch
        {
            _frameRenderScheduled = false;
        }
    }

    private void OnLatencyChanged(int ms)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnLatencyChanged(ms));
            return;
        }
        RemoteLatencyText.Text = ms < 0 ? "延迟 -- ms" : $"延迟 {ms} ms";
        RemoteLatencyText.Foreground = ms switch
        {
            < 0 => System.Windows.Media.Brushes.White,
            <= 50 => System.Windows.Media.Brushes.LightGreen,
            <= 150 => System.Windows.Media.Brushes.Gold,
            _ => System.Windows.Media.Brushes.OrangeRed
        };
    }

    private void ScreenImage_OnMouseMove(object sender, MouseEventArgs e)
    {
        ShowBars();
        if (!_controlMode) return;
        var (x, y, inside) = MapPointToScreen(e.GetPosition(ScreenImage));
        if (!inside) return;
        lock (_inputLock)
        {
            _pendingMove = (x, y, true);
        }
        _inputFlushTimer.Change(16, 16);
    }

    private void ScreenImage_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_controlMode) return;
        FlushPendingMove();
        var button = ToButtonId(e.ChangedButton);
        if (button == 0) return;
        var (_, _, inside) = MapPointToScreen(e.GetPosition(ScreenImage));
        if (!inside) return;
        _controller.SendInputEvent(new InputEvent
        {
            Kind = InputKind.Button,
            Button = button,
            Down = true
        });
        ScreenImage.Focus();
    }

    private void ScreenImage_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_controlMode) return;
        FlushPendingMove();
        var button = ToButtonId(e.ChangedButton);
        if (button == 0) return;
        _controller.SendInputEvent(new InputEvent
        {
            Kind = InputKind.Button,
            Button = button,
            Down = false
        });
    }

    private void ScreenImage_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_controlMode) return;
        FlushPendingMove();
        _controller.SendInputEvent(new InputEvent
        {
            Kind = InputKind.Wheel,
            WheelDelta = e.Delta
        });
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_controlMode || e.IsRepeat) return;
        FlushPendingMove();
        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
        if (vk <= 0) return;
        _controller.SendInputEvent(new InputEvent
        {
            Kind = InputKind.Key,
            Vk = vk,
            Down = true,
            Extended = ExtendedKeys.Contains(e.Key)
        });
        e.Handled = true;
    }

    private void Window_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_controlMode) return;
        FlushPendingMove();
        var vk = KeyInterop.VirtualKeyFromKey(e.Key);
        if (vk <= 0) return;
        _controller.SendInputEvent(new InputEvent
        {
            Kind = InputKind.Key,
            Vk = vk,
            Down = false,
            Extended = ExtendedKeys.Contains(e.Key)
        });
        e.Handled = true;
    }

    private (int X, int Y, bool Inside) MapPointToScreen(Point point)
    {
        if (_screenWidth <= 0 || _screenHeight <= 0) return (0, 0, false);
        var actualWidth = ScreenImage.ActualWidth;
        var actualHeight = ScreenImage.ActualHeight;
        if (actualWidth <= 0 || actualHeight <= 0) return (0, 0, false);
        var scale = Math.Min(actualWidth / _screenWidth, actualHeight / _screenHeight);
        var drawWidth = _screenWidth * scale;
        var drawHeight = _screenHeight * scale;
        var offsetX = (actualWidth - drawWidth) / 2;
        var offsetY = (actualHeight - drawHeight) / 2;
        if (point.X < offsetX || point.Y < offsetY ||
            point.X > offsetX + drawWidth || point.Y > offsetY + drawHeight)
        {
            return (0, 0, false);
        }
        var x = (int)((point.X - offsetX) / scale);
        var y = (int)((point.Y - offsetY) / scale);
        return (Math.Clamp(x, 0, _screenWidth - 1), Math.Clamp(y, 0, _screenHeight - 1), true);
    }

    private void FlushPendingMove()
    {
        (int X, int Y, bool Valid) pending;
        lock (_inputLock)
        {
            pending = _pendingMove;
            _pendingMove = default;
        }
        _inputFlushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (!_controlMode || !pending.Valid) return;
        _controller.SendInputEvent(new InputEvent
        {
            Kind = InputKind.MouseMove,
            X = pending.X,
            Y = pending.Y
        });
    }

    private void FlushPendingMoveFromTimer()
    {
        (int X, int Y, bool Valid) pending;
        lock (_inputLock)
        {
            pending = _pendingMove;
            _pendingMove = default;
        }
        if (!pending.Valid)
        {
            _inputFlushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }
        if (!_controlMode) return;
        _controller.SendInputEvent(new InputEvent
        {
            Kind = InputKind.MouseMove,
            X = pending.X,
            Y = pending.Y
        });
    }

    private static int ToButtonId(MouseButton button)
        => button switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 3,
            MouseButton.XButton1 => 4,
            MouseButton.XButton2 => 5,
            _ => 0
        };

    private void ShowBars()
    {
        AnimateBarOpacity(TopBar, 1);
        AnimateBarOpacity(BottomBar, 1);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideBars()
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            ShowBars();
            return;
        }
        AnimateBarOpacity(TopBar, 0);
        AnimateBarOpacity(BottomBar, 0);
    }

    private static void AnimateBarOpacity(FrameworkElement bar, double to)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        bar.BeginAnimation(OpacityProperty, animation);
    }

    private void RemoteMode_OnChecked(object sender, RoutedEventArgs e)
    {
        _controlMode = ControlRadio.IsChecked == true;
        _controller.SetControlMode(_controlMode);
    }

    private void RemoteQuality_OnChecked(object sender, RoutedEventArgs e)
    {
        var index = 0;
        if (RemoteQualityMediumRadio.IsChecked == true) index = 1;
        else if (RemoteQualityHighRadio.IsChecked == true) index = 2;
        else if (RemoteQualityUltraRadio.IsChecked == true) index = 3;
        _controller.SetQuality(index);
    }

    private void RemoteFpsSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        RemoteFpsValueText.Text = $"{e.NewValue:F0} FPS";
        _controller.SetFps((int)e.NewValue);
    }

    private async void RemoteSendFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要发送的文件",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        await _controller.SendFileAsync(dialog.FileName);
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        var isFullscreen = WindowState != WindowState.Maximized;
        WindowState = isFullscreen ? WindowState.Maximized : WindowState.Normal;
        FullscreenButton.Content = isFullscreen ? "退出全屏" : "全屏";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var shouldShow = SettingsPanel.Visibility == Visibility.Collapsed;
        SettingsPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
        if (shouldShow)
        {
            RefreshSettingsStatus();
            ShowBars();
        }
    }

    private void RefreshSettingsStatus()
    {
        var virtualDisplay = VirtualDisplayProbe.IsVirtualDisplayDriverInstalled();
        VirtualDisplayStatusText.Text = virtualDisplay
            ? "已检测到虚拟显示器驱动：配合黑屏模式可改由虚拟显示器输出画面"
            : "未检测到虚拟显示器驱动：当前使用系统熄屏黑屏模式（无需驱动）";
    }

    private void PrivacyToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        _controller.SetPrivacyMode(PrivacyToggle.IsChecked == true);
        PrivacyStatusText.Text = PrivacyToggle.IsChecked == true
            ? "已开启：被控端显示器熄灭，控制端画面不受影响"
            : "已关闭：被控端显示器恢复正常";
    }

    private void AudioToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        _controller.SetAudioEnabled(AudioToggle.IsChecked == true);
        AudioStatusText.Text = AudioToggle.IsChecked == true
            ? "已开启：被控端系统声音实时回传到控制端"
            : "已关闭：不再回传被控端声音";
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _controller.Disconnect();
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _controller.FrameReceived -= OnFrameReceived;
        _controller.LatencyChanged -= OnLatencyChanged;
        _controller.Disconnect();
        _hideTimer.Stop();
        _fpsTimer.Stop();
        _latencyTimer.Stop();
        _inputFlushTimer.Dispose();
    }
}
