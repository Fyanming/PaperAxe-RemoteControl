using System;
using System.Runtime.InteropServices;

namespace LanRemoteControl.Core
{
    // ============================================================
    //  IPolicyConfig COM 互操作
    //  用于切换 Windows 默认音频输出设备
    //  CLSID: {870AF99C-88D7-4A99-83F5-6C6B58F6E8C0}
    //  IID:   {F8679F50-850A-41CF-9C72-430F290290C8}
    //  参考:  Windows SDK mmdeviceapi.h
    // ============================================================

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string device, out IntPtr format);
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, int mod, out IntPtr format);
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device);
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, IntPtr format, IntPtr format2);
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, int mod, out long period, out long capPeriod);
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, ref long period);
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, out int mode);
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, int mode);
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, [MarshalAs(UnmanagedType.LPWStr)] string key, out IntPtr prop);
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, [MarshalAs(UnmanagedType.LPWStr)] string key, IntPtr prop);
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, int role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string device, int visible);
    }

    internal static class PolicyConfigClient
    {
        private static readonly Guid CLSID_PolicyConfigClient = new("870AF99C-88D7-4A99-83F5-6C6B58F6E8C0");

        private static IPolicyConfig Create()
        {
            var type = Type.GetTypeFromCLSID(CLSID_PolicyConfigClient)
                       ?? throw new InvalidOperationException("无法创建 IPolicyConfig COM 实例");
            return (IPolicyConfig)Activator.CreateInstance(type)!;
        }

        // 切换默认渲染设备：同时设置 eConsole / eMultimedia / eCommunications
        public static void SetDefaultEndpoint(string deviceId)
        {
            var cfg = Create();
            cfg.SetDefaultEndpoint(deviceId, 0);
            cfg.SetDefaultEndpoint(deviceId, 1);
            cfg.SetDefaultEndpoint(deviceId, 2);
            if (Marshal.IsComObject(cfg)) Marshal.ReleaseComObject(cfg);
        }
    }
}
