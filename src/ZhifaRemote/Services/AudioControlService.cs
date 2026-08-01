using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using ZhifaRemote.Models;

namespace ZhifaRemote.Services;

public sealed class AudioControlService : IDisposable
{
    private static readonly string[] VirtualMarkers =
    {
        "cable", "vb-audio", "vb audio", "virtual", "虚拟", "线路", "voicemeeter"
    };

    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDeviceInfo> ListDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        MMDevice? defaultDevice = null;
        try
        {
            defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
        }

        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            var name = device.FriendlyName ?? "";
            var lower = name.ToLowerInvariant();
            devices.Add(new AudioDeviceInfo
            {
                Id = device.ID,
                Name = name,
                IsDefault = defaultDevice is not null && device.ID == defaultDevice.ID,
                IsVirtual = VirtualMarkers.Any(m => lower.Contains(m))
            });
        }
        return devices;
    }

    public string? GetDefaultDeviceId()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        }
        catch
        {
            return null;
        }
    }

    public float GetDefaultVolume()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                .AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return -1f;
        }
    }

    public bool SetVolumeById(string deviceId, float scalar)
    {
        try
        {
            var device = _enumerator.GetDevice(deviceId);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar, 0f, 1f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetDefaultVolume(float scalar)
    {
        try
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar, 0f, 1f);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool SetDefaultDevice(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new CPolicyConfigClient();
            for (var role = 0; role <= 2; role++)
            {
                policy.SetDefaultEndpoint(deviceId, role);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _enumerator.Dispose();
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    private class CPolicyConfigClient
    {
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string deviceId, IntPtr format);
        int GetDeviceFormat(string deviceId, bool def, IntPtr format);
        int ResetDeviceFormat(string deviceId);
        int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod(string deviceId, bool def, IntPtr defPeriod, IntPtr minPeriod);
        int SetProcessingPeriod(string deviceId, IntPtr period);
        int GetShareMode(string deviceId, IntPtr mode);
        int SetShareMode(string deviceId, IntPtr mode);
        int GetPropertyValue(string deviceId, bool fxStore, IntPtr key, IntPtr value);
        int SetPropertyValue(string deviceId, bool fxStore, IntPtr key, IntPtr value);
        int SetDefaultEndpoint(string deviceId, int role);
        int SetEndpointVisibility(string deviceId, bool visible);
    }
}
