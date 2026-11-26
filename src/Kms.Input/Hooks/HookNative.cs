using System.Runtime.InteropServices;

namespace Kms.Input.Hooks;

/// <summary>
/// Win32 低级键鼠钩子的原生层：P/Invoke 声明、回调数据结构和纯解码函数。
/// 生命周期与健康判定在 <see cref="LowLevelHooks"/>，这里只保证「取字段」足够廉价。
///
/// 注意：所有 P/Invoke 必须留在 Kms.Input 内。Kms.Core 是平台无关内核，禁止出现 P/Invoke。
/// </summary>
public static class HookNative
{
    /// <summary>
    /// 从 <c>MSLLHOOKSTRUCT.mouseData</c> 的高 16 位取滚轮增量。
    /// 先按 <see cref="short"/> 解释符号，再扩宽成 <see cref="int"/>：
    /// 向上滚一格是 120，向下滚一格是 -120。
    /// </summary>
    public static int WheelDelta(uint mouseData) => (short)((mouseData >> 16) & 0xFFFF);

    /// <summary>
    /// 系统空闲毫秒数，即「距上一次任何键鼠输入」的时长。
    /// 读不到时返回 <see cref="uint.MaxValue"/>——看门狗据此放弃停摆判断，
    /// 只信钩子句柄，避免把「读不到空闲信息」误判成钩子停摆。
    /// </summary>
    public static uint GetSystemIdleMilliseconds()
    {
        LastInputInfo info = new() { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return uint.MaxValue;
        }

        // TickCount 回绕按 32 位无符号差值处理，与 HookWatchdog.Elapsed 的口径一致。
        return unchecked((uint)Environment.TickCount - info.Time);
    }

    /// <summary>
    /// 看门狗时钟：<see cref="Environment.TickCount"/> 毫秒，约 49.7 天回绕。
    /// 差值必须交给 <see cref="HookWatchdog.Elapsed"/> 计算，不能直接相减。
    /// </summary>
    public static long TickCountMs() => Environment.TickCount;

    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const string User32 = "user32.dll";
    private const string Kernel32 = "kernel32.dll";

#pragma warning disable SYSLIB1054 // LibraryImport 源生成需要 AllowUnsafeBlocks；项目未开启，保留 DllImport。
    [DllImport(User32, EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport(User32)]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport(Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);
#pragma warning restore SYSLIB1054

    /// <summary><c>KBDLLHOOKSTRUCT</c>。字段顺序必须与 Win32 完全一致。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    /// <summary><c>MSLLHOOKSTRUCT</c>。坐标是屏幕像素，正是统计需要的坐标系。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MsLlHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    /// <summary><c>LASTINPUTINFO</c>。Size 必须是 8，否则 <c>GetLastInputInfo</c> 直接失败。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
