using System.Runtime.InteropServices;

namespace ZhifaRemote.Services;

public static class InputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventXDown = 0x0080;
    private const uint MouseEventXUp = 0x0100;
    private const uint MouseEventWheel = 0x0800;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

    public static void Move(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public static void Button(int button, bool down)
    {
        var flag = button switch
        {
            1 => down ? MouseEventLeftDown : MouseEventLeftUp,
            2 => down ? MouseEventRightDown : MouseEventRightUp,
            3 => down ? MouseEventMiddleDown : MouseEventMiddleUp,
            4 => down ? MouseEventXDown : MouseEventXUp,
            5 => down ? MouseEventXDown : MouseEventXUp,
            _ => 0u
        };
        if (flag == 0) return;
        var input = new INPUT { type = InputMouse };
        input.U.mi.dwFlags = flag;
        input.U.mi.mouseData = button is 4 or 5 ? (uint)(button == 4 ? 1 : 2) : 0;
        Send(input);
    }

    public static void Wheel(int delta)
    {
        var input = new INPUT { type = InputMouse };
        input.U.mi.dwFlags = MouseEventWheel;
        input.U.mi.mouseData = unchecked((uint)delta);
        Send(input);
    }

    public static void Key(int vk, bool down, bool extended)
    {
        var input = new INPUT { type = InputKeyboard };
        input.U.ki.wVk = (ushort)vk;
        input.U.ki.dwFlags = down ? 0 : KeyEventKeyUp;
        if (extended) input.U.ki.dwFlags |= KeyEventExtendedKey;
        Send(input);
    }

    private static void Send(INPUT input)
    {
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
