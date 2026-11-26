using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Kms.TransparentSpike;

public sealed partial class SpikeWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkT = 0x54;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;

    private readonly DispatcherTimer _timer;
    private IntPtr _hwnd;
    private bool _clickThrough;
    private bool _hotkeyDown;
    private bool _opacityUpDown;
    private bool _opacityDownDown;

    public SpikeWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += (_, _) => PollHotkeys();

        if (TryGetSeconds(out int seconds))
        {
            DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromSeconds(seconds) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                Close();
            };
            closeTimer.Start();
        }
    }

    private static bool TryGetSeconds(out int seconds)
    {
        seconds = 0;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--seconds", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int value) && value > 0)
            {
                seconds = value;
                return true;
            }
        }

        return false;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hwnd != IntPtr.Zero)
        {
            long style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            style |= WsExToolWindow | WsExNoActivate;
            SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style));
            SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0010); // NOSIZE | NOMOVE | NOACTIVATE
        }

        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void PollHotkeys()
    {
        bool toggle = Down(VkControl) && Down(VkMenu) && Down(VkT);
        if (toggle && !_hotkeyDown)
        {
            SetClickThrough(!_clickThrough);
        }

        _hotkeyDown = toggle;

        bool up = Down(VkControl) && Down(VkMenu) && Down(VkUp);
        if (up && !_opacityUpDown)
        {
            Opacity = Math.Min(1, Opacity + 0.1);
        }

        _opacityUpDown = up;

        bool down = Down(VkControl) && Down(VkMenu) && Down(VkDown);
        if (down && !_opacityDownDown)
        {
            Opacity = Math.Max(0.2, Opacity - 0.1);
        }

        _opacityDownDown = down;
    }

    private void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        style = enabled ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style));
        Status.Text = enabled
            ? "当前：点击穿透（Ctrl+Alt+T 恢复）"
            : "当前：可交互（不抢焦点）";
    }

    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
