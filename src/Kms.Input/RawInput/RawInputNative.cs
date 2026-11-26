using System.Runtime.InteropServices;

namespace Kms.Input.RawInput;

/// <summary>
/// Raw Input 的 Win32 常量、结构体与 P/Invoke。
/// 只在本程序集内部使用；解码层不直接依赖这些声明，测试可以完全脱离桌面。
/// </summary>
internal static class RawInputNative
{
    // ---- GetRawInputData ----

    /// <summary>dwCommand：取出完整 RAWINPUT（含 header）。</summary>
    public const uint RidInput = 0x10000003;

    public const uint RimTypeMouse = 0;
    public const uint RimTypeKeyboard = 1;

    // ---- RAWINPUTDEVICE.dwFlags ----

    public const uint RidevRemove = 0x00000001;
    public const uint RidevInputSink = 0x00000100;
    public const uint RidevDevNotify = 0x00002000;

    // ---- HID usage ----

    public const ushort HidUsagePageGeneric = 0x0001;
    public const ushort HidUsageMouse = 0x0002;
    public const ushort HidUsageKeyboard = 0x0006;

    // ---- WM_INPUT_DEVICE_CHANGE 的 wParam ----

    public const int GidcArrival = 1;
    public const int GidcRemoval = 2;

    /// <summary>RAWINPUTHEADER 的固定部分：dwType + dwSize。</summary>
    public const int HeaderPrefixSize = 8;

    /// <summary>RAWINPUTHEADER 的大小（以后跟着 hDevice 与 wParam 两个指针宽度字段）。</summary>
    public static int HeaderSize => HeaderPrefixSize + (2 * IntPtr.Size);

    /// <summary>HWND_MESSAGE：只收消息、不参与 Z 序的窗口。</summary>
    public static readonly IntPtr HwndMessage = new(-3);

    internal delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint numDevices,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        [Out] byte[] data,
        ref uint size,
        uint headerSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetMessage(out Msg message, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);
}
