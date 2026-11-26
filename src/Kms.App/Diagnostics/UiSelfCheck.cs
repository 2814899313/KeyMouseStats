using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kms.App.Widgets;

namespace Kms.App.Diagnostics;

/// <summary>
/// --selfcheck-ui：真实创建主窗与悬浮组，读取 Win32 扩展样式并输出 JSON。
/// 不启动 InputService（不挂钩子），用于验收透明/不抢焦点/置顶的代码路径。
/// </summary>
internal static class UiSelfCheck
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    public static void Run(
        MainWindow main,
        WidgetWindow widget,
        FocusPillWindow pill,
        GameHudWindow hud,
        FocusBreakToast toast,
        IClassicDesktopStyleApplicationLifetime desktop,
        Action? beforeShutdown = null)
    {
        DispatcherTimer.RunOnce(
            () =>
            {
                List<WindowCheck> checks =
                [
                    Check("main", main, requireTransparent: false, requireNoActivate: false,
                        requireTopmost: false, requireToolWindow: false),
                    Check("widget", widget, requireTransparent: false, requireNoActivate: true),
                    Check("pill", pill, requireTransparent: false, requireNoActivate: true),
                    Check("hud", hud, requireTransparent: true, requireNoActivate: true),
                    Check("toast", toast, requireTransparent: false, requireNoActivate: true)
                ];

                SelfCheckResult result = new()
                {
                    Timestamp = DateTimeOffset.Now,
                    Machine = Environment.MachineName,
                    Os = RuntimeInformation.OSDescription,
                    RenderScaling = main.RenderScaling,
                    PageCount = main.Shell.PageCount,
                    HudClickThrough = hud.IsClickThrough,
                    Pass = checks.All(check => check.Pass),
                    Checks = checks
                };

                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "kms-v4-selfcheck.json"),
                    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

                beforeShutdown?.Invoke();
                desktop.Shutdown();
            },
            TimeSpan.FromMilliseconds(800));
    }

    private static WindowCheck Check(
        string name,
        Window window,
        bool requireTransparent,
        bool requireNoActivate,
        bool requireTopmost = true,
        bool requireToolWindow = true)
    {
        IntPtr handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        long style = handle == IntPtr.Zero ? 0 : GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        bool toolWindow = (style & WsExToolWindow) != 0;
        bool noActivate = (style & WsExNoActivate) != 0;
        bool transparent = (style & WsExTransparent) != 0;
        bool hitSelf = false;
        IntPtr hit = IntPtr.Zero;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out Rect rect))
        {
            hit = WindowFromPoint(new Point(rect.Left + (rect.Right - rect.Left) / 2, rect.Top + (rect.Bottom - rect.Top) / 2));
            hitSelf = hit == handle || IsChild(handle, hit);
        }

        bool pass = handle != IntPtr.Zero
            && window.IsVisible
            && (!requireTopmost || window.Topmost)
            && (!requireToolWindow || toolWindow)
            && (!requireNoActivate || noActivate)
            && (!requireTransparent || transparent);

        return new WindowCheck
        {
            Name = name,
            Handle = handle.ToInt64(),
            Visible = window.IsVisible,
            Topmost = window.Topmost,
            ToolWindow = toolWindow,
            NoActivate = noActivate,
            Transparent = transparent,
            HitTest = hitSelf,
            HitHandle = hit.ToInt64(),
            Pass = pass
        };
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    private sealed class SelfCheckResult
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Machine { get; set; } = "";
        public string Os { get; set; } = "";
        public double RenderScaling { get; set; }
        public int PageCount { get; set; }
        public bool HudClickThrough { get; set; }
        public bool Pass { get; set; }
        public List<WindowCheck> Checks { get; set; } = [];
    }

    private sealed class WindowCheck
    {
        public string Name { get; set; } = "";
        public long Handle { get; set; }
        public bool Visible { get; set; }
        public bool Topmost { get; set; }
        public bool ToolWindow { get; set; }
        public bool NoActivate { get; set; }
        public bool Transparent { get; set; }
        public bool HitTest { get; set; }
        public long HitHandle { get; set; }
        public bool Pass { get; set; }
    }
}
