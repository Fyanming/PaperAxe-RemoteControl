using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Color = System.Windows.Media.Color;

namespace ZhifaRemote.Helpers;

public static class WindowBlur
{
    public static void EnableAcrylic(Window window, Color tint)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Apply(window, tint);
            return;
        }
        window.SourceInitialized += (_, _) => Apply(window, tint);
    }

    private static void Apply(Window window, Color tint)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var accent = new AccentPolicy
        {
            AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
            GradientColor = ToAbgr(tint)
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var data = new WindowCompositionAttributeData
        {
            Attribute = 19,
            SizeOfData = size,
            Data = Marshal.AllocHGlobal(size)
        };
        Marshal.StructureToPtr(accent, data.Data, false);
        SetWindowCompositionAttribute(hwnd, ref data);
        Marshal.FreeHGlobal(data.Data);
    }

    private static uint ToAbgr(Color color)
        => ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}
