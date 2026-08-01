using System.Runtime.InteropServices;

namespace ZhifaRemote.Services;

public sealed class PrivacyScreenService : IDisposable
{
    private const uint WmSysCommand = 0x0112;
    private const uint ScMonitorPower = 0xF170;
    private static readonly IntPtr HwndBroadcast = (IntPtr)0xFFFF;

    private readonly System.Threading.Timer _timer;
    private volatile bool _blackout;

    public PrivacyScreenService()
    {
        _timer = new System.Threading.Timer(_ => KeepMonitorOff(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsBlackout => _blackout;

    public void StartBlackout()
    {
        if (_blackout) return;
        _blackout = true;
        SetMonitorPower(2);
        _timer.Change(2000, 2000);
    }

    public void StopBlackout()
    {
        if (!_blackout) return;
        _blackout = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        SetMonitorPower(-1);
    }

    public void Dispose()
    {
        StopBlackout();
        _timer.Dispose();
    }

    private void KeepMonitorOff()
    {
        if (_blackout) SetMonitorPower(2);
    }

    private static void SetMonitorPower(int power)
    {
        try
        {
            _ = SendMessage(HwndBroadcast, WmSysCommand, (IntPtr)ScMonitorPower, (IntPtr)power);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
