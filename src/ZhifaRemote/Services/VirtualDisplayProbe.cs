using Microsoft.Win32;

namespace ZhifaRemote.Services;

public static class VirtualDisplayProbe
{
    private static readonly string[] DriverMarkers =
    {
        "IddSampleDriver",
        "VirtualDisplay",
        "Virtual-Display",
        "ParsecVDD",
        "Easy-Virtual-Display",
        "Usb4Display"
    };

    public static bool IsVirtualDisplayDriverInstalled()
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return false;
            foreach (var name in services.GetSubKeyNames())
            {
                if (DriverMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }
}
