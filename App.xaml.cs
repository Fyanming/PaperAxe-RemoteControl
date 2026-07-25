using System.Windows;
using LanRemoteControl.Core;

namespace LanRemoteControl
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 启动时确保虚拟音频设备已注册（驱动安装不在本代码内）
            try { AudioDeviceManager.EnsureVirtualDevice(); }
            catch { /* 设备未安装不阻断启动 */ }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 退出前恢复音频状态，避免系统卡在虚拟设备
            try
            {
                AudioDeviceManager.OnRemoteEnd();
                NetworkServer.Stop();
                NetworkClient.Disconnect();
                ToastNotifier.Dispose();
            }
            catch { }
            base.OnExit(e);
        }
    }
}
