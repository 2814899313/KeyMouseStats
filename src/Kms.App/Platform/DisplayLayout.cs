using System.Runtime.InteropServices;

namespace Kms.App.Platform;

/// <summary>虚拟桌面中的一块显示器区域，单位为屏幕物理像素。</summary>
internal readonly record struct DisplayRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>
/// 当前显示器布局。使用 Win32 EnumDisplayMonitors，调用方在页面构建阶段读取即可；
/// 结果不做持久化，拔插/重排显示器后重新进入页面会自动刷新。
/// </summary>
internal static class DisplayLayout
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    /// <summary>测试/预览可注入固定布局，避免 golden 依赖本机屏幕。</summary>
    public static IReadOnlyList<DisplayRect>? Override { get; set; }

    public static IReadOnlyList<DisplayRect> GetMonitors()
    {
        if (Override is { Count: > 0 })
        {
            return Override;
        }

        List<DisplayRect> monitors = [];

        bool Collect(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect rect, IntPtr data)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width > 0 && height > 0)
            {
                monitors.Add(new DisplayRect(rect.Left, rect.Top, width, height));
            }

            return true;
        }

        try
        {
            MonitorEnumProc callback = Collect;
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        catch
        {
            monitors.Clear();
        }

        if (monitors.Count == 0)
        {
            int x = GetSystemMetrics(SmXVirtualScreen);
            int y = GetSystemMetrics(SmYVirtualScreen);
            int width = GetSystemMetrics(SmCxVirtualScreen);
            int height = GetSystemMetrics(SmCyVirtualScreen);
            if (width > 0 && height > 0)
            {
                monitors.Add(new DisplayRect(x, y, width, height));
            }
        }

        return monitors
            .OrderBy(monitor => monitor.X)
            .ThenBy(monitor => monitor.Y)
            .ToArray();
    }

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        ref NativeRect lprcMonitor,
        IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
