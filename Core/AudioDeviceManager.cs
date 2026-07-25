using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace LanRemoteControl.Core
{
    // ============================================================
    //  音频设备管理
    //  职责：
    //   1) 启动时注册虚拟音频设备（LFBolt Virtual Cable）
    //   2) 远控建立时：记录音量 → 归零 → 切换默认输出至虚拟设备
    //   3) 远控断开时：恢复原音量与原默认设备
    //  注：虚拟设备实际部署需安装驱动（如 VB-Cable / 自研虚拟音频驱动）
    //      本模块负责调用 IPolicyConfig 切换；驱动安装不在本代码内
    // ============================================================
    public static class AudioDeviceManager
    {
        private const string VirtualDeviceName = "LFBolt Virtual Cable";
        private static string? _virtualDeviceId;
        private static string? _originalDeviceId;
        private static float _originalVolume;
        private static bool _switched;

        /// <summary>启动时确保虚拟设备已注册（如未注册则提示用户安装）</summary>
        public static void EnsureVirtualDevice()
        {
            _virtualDeviceId = FindDeviceIdByName(VirtualDeviceName);
            // 实际部署：若 _virtualDeviceId == null，应调用驱动安装包
            // 此处仅做检测，不阻断启动
        }

        /// <summary>远控开始：音量归零 + 切换默认输出</summary>
        public static void OnRemoteBegin()
        {
            if (_switched) return;
            try
            {
                var dev = GetDefaultRenderDevice();
                if (dev == null) return;
                using (dev)
                {
                    _originalDeviceId = dev.ID;
                    using var vol = dev.AudioEndpointVolume;
                    _originalVolume = vol.MasterVolumeLevelScalar;
                    // 归零
                    vol.MasterVolumeLevelScalar = 0f;
                }
                // 切换至虚拟设备
                if (!string.IsNullOrEmpty(_virtualDeviceId))
                {
                    PolicyConfigClient.SetDefaultEndpoint(_virtualDeviceId);
                }
                _switched = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AudioDeviceManager] OnRemoteBegin 失败: " + ex.Message);
            }
        }

        /// <summary>远控结束：恢复原音量与默认设备</summary>
        public static void OnRemoteEnd()
        {
            if (!_switched) return;
            try
            {
                // 恢复默认设备
                if (!string.IsNullOrEmpty(_originalDeviceId))
                {
                    PolicyConfigClient.SetDefaultEndpoint(_originalDeviceId);
                }
                // 恢复音量
                var dev = GetDefaultRenderDevice();
                if (dev != null)
                {
                    using (dev)
                    using (var vol = dev.AudioEndpointVolume)
                    {
                        vol.MasterVolumeLevelScalar = _originalVolume;
                    }
                }
                _switched = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[AudioDeviceManager] OnRemoteEnd 失败: " + ex.Message);
            }
        }

        // ===== NAudio 辅助 =====
        private static MMDevice? GetDefaultRenderDevice()
        {
            var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        }

        private static string? FindDeviceIdByName(string name)
        {
            var enumerator = new MMDeviceEnumerator();
            foreach (var wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    if (wasapi.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase))
                        return wasapi.ID;
                }
                finally { wasapi.Dispose(); }
            }
            return null;
        }
    }
}
