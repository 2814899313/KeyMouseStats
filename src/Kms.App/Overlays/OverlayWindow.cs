using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Kms.App.Overlays;

/// <summary>
/// 悬浮窗基类：无边框、透明、置顶、不抢焦点、不进 Alt+Tab。
/// 点击穿透由 <see cref="SetClickThrough"/> 显式切换（GameHud/FocusBreakToast 用）。
/// </summary>
public class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    private IntPtr _hwnd;
    private bool _clickThrough;

    protected OverlayWindow()
    {
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    protected IntPtr Hwnd => _hwnd;

    /// <summary>是否当前处于点击穿透。</summary>
    public bool IsClickThrough => _clickThrough;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style));
        SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        style = enabled ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style));
    }

    protected void EnableDrag(PointerPressedEventArgs e)
    {
        if (!_clickThrough && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
