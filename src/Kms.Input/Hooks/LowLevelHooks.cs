using System.Runtime.InteropServices;
using Kms.Core.Time;

namespace Kms.Input.Hooks;

/// <summary>
/// 低级键盘/鼠标钩子（<c>WH_KEYBOARD_LL</c> / <c>WH_MOUSE_LL</c>）。
///
/// 线程与生命周期（硬约束）：
/// <list type="bullet">
/// <item>低级钩子只能在**有消息循环的线程**上安装；回调也由该线程派发。
///       <see cref="Install"/> 只记录句柄，不创建线程、不启动消息循环。</item>
/// <item><see cref="Install"/> / <see cref="Uninstall"/> 应在同一线程串行调用
///       （通常是 UI/托盘线程），本类型内部用锁兜底，但不会替你切线程。</item>
/// <item>安装失败不抛异常：返回 <c>false</c>，把重试交给看门狗
///       （每 <see cref="HookWatchdog.CheckIntervalMs"/> 毫秒体检一次）。</item>
/// <item>必须持有本对象直到卸载：回调委托的根挂在实例字段上，实例被回收后系统仍可能回调。</item>
/// </list>
///
/// 回调热路径只做字段读取：<see cref="Marshal.PtrToStructure{T}(IntPtr)"/> + 直接调用 sink，
/// 不做 LINQ、不拼字符串、不解析窗口；任何异常都吞掉后继续 <c>CallNextHookEx</c>，
/// 绝不能把异常或阻塞泄漏给系统输入链（超过 LowLevelHooksTimeout 会被静默摘钩）。
/// </summary>
public sealed class LowLevelHooks : IDisposable
{
    private readonly IInputSink _sink;
    private readonly HookHealthMonitor _health;
    private readonly Func<long> _eventTicks;
    private readonly HookNative.HookProc _keyboardProc;
    private readonly HookNative.HookProc _mouseProc;
    private readonly object _gate = new();

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private int _keyboardAttached;
    private int _mouseAttached;
    private bool _disposed;

    /// <param name="sink">事件出口。实现方可以慢，但不要在回调里做跨进程解析。</param>
    /// <param name="tickCountMs">看门狗毫秒时钟，默认 <see cref="HookNative.TickCountMs"/>。</param>
    /// <param name="systemIdleMs">系统空闲毫秒，默认 <see cref="HookNative.GetSystemIdleMilliseconds"/>。</param>
    /// <param name="busy">锁屏/休眠/会话断开，默认 <c>false</c>。</param>
    /// <param name="eventTicks">事件时间戳，默认 <see cref="SystemClock.MonotonicTicks"/>（Stopwatch tick）。</param>
    public LowLevelHooks(
        IInputSink sink,
        Func<long>? tickCountMs = null,
        Func<uint>? systemIdleMs = null,
        Func<bool>? busy = null,
        Func<long>? eventTicks = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        _sink = sink;
        _health = new HookHealthMonitor(tickCountMs, systemIdleMs, busy);
        _eventTicks = eventTicks ?? (static () => SystemClock.Instance.MonotonicTicks);
        _keyboardProc = KeyboardHookProc;
        _mouseProc = MouseHookProc;
    }

    /// <summary>键盘钩子句柄是否仍然存在（不代表回调还在触发，需配合 <see cref="CheckHealth"/>）。</summary>
    public bool IsKeyboardAlive => Volatile.Read(ref _keyboardAttached) != 0;

    /// <summary>鼠标钩子句柄是否仍然存在。</summary>
    public bool IsMouseAlive => Volatile.Read(ref _mouseAttached) != 0;

    /// <summary>键盘钩子最后一次收到事件的本机毫秒时刻（TickCount 口径）。</summary>
    public long LastKeyboardTickMs => _health.LastKeyboardTickMs;

    /// <summary>鼠标钩子最后一次收到事件的本机毫秒时刻（TickCount 口径）。</summary>
    public long LastMouseTickMs => _health.LastMouseTickMs;

    /// <summary>
    /// 安装两条钩子。幂等：先卸掉旧句柄再装。
    /// 返回 <c>true</c> 仅当两条都安装成功；部分成功时已成功的那条会保留，
    /// 看门狗下一轮只补缺失的那条（调用方重装时整组重建即可）。
    /// </summary>
    public bool Install()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                UninstallCore();

                IntPtr module = HookNative.GetModuleHandle(null);

                // 显式钉住委托指针：只要本实例还活着，_keyboardProc/_mouseProc 就不会被回收，
                // 系统保存的函数指针始终有效（比依赖 P/Invoke 的隐式委托封送更直白）。
                IntPtr keyboardProc = Marshal.GetFunctionPointerForDelegate(_keyboardProc);
                IntPtr mouseProc = Marshal.GetFunctionPointerForDelegate(_mouseProc);

                _keyboardHook = HookNative.SetWindowsHookEx(
                    InputMessages.WH_KEYBOARD_LL, keyboardProc, module, 0);
                Volatile.Write(ref _keyboardAttached, _keyboardHook != IntPtr.Zero ? 1 : 0);

                _mouseHook = HookNative.SetWindowsHookEx(
                    InputMessages.WH_MOUSE_LL, mouseProc, module, 0);
                Volatile.Write(ref _mouseAttached, _mouseHook != IntPtr.Zero ? 1 : 0);
            }
            catch
            {
                // 安装期的任何异常都折叠成 false，绝不把失败抛给 UI 线程；看门狗稍后重试。
            }

            // 无论成败都重新计时：装失败时句柄为 0，看门狗会立即要求重装；
            // 部分成功时健康的那条也不会被旧时间戳误判为停摆。
            _health.Reset();

            return _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
        }
    }

    /// <summary>
    /// 卸载两条钩子。可重复调用；应在安装钩子的同一线程调用（低级钩子的常规约束）。
    /// </summary>
    public void Uninstall()
    {
        lock (_gate)
        {
            UninstallCore();
        }
    }

    /// <summary>
    /// 体检一次，返回需要重装的位。调用频率由看门狗决定（建议
    /// <see cref="HookWatchdog.CheckIntervalMs"/>）；本方法自身不做节流。
    /// </summary>
    public HookRepair CheckHealth() => _health.Evaluate(IsKeyboardAlive, IsMouseAlive);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UninstallCore();
        }
    }

    private void UninstallCore()
    {
        IntPtr keyboard = _keyboardHook;
        if (keyboard != IntPtr.Zero)
        {
            Volatile.Write(ref _keyboardAttached, 0);
            HookNative.UnhookWindowsHookEx(keyboard);
            _keyboardHook = IntPtr.Zero;
        }

        IntPtr mouse = _mouseHook;
        if (mouse != IntPtr.Zero)
        {
            Volatile.Write(ref _mouseAttached, 0);
            HookNative.UnhookWindowsHookEx(mouse);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                // 先记存活：即使后面的字段读取/统计失败，系统确实把事件送到了钩子。
                _health.NoteKeyboard();

                int message = unchecked((int)wParam.ToInt64());
                bool down;
                if (message is InputMessages.WM_KEYDOWN or InputMessages.WM_SYSKEYDOWN)
                {
                    down = true;
                }
                else if (message is InputMessages.WM_KEYUP or InputMessages.WM_SYSKEYUP)
                {
                    down = false;
                }
                else
                {
                    return HookNative.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                }

                HookNative.KbdLlHookStruct info =
                    Marshal.PtrToStructure<HookNative.KbdLlHookStruct>(lParam);
                _sink.KeyEx(
                    unchecked((int)info.VkCode),
                    unchecked((int)info.ScanCode),
                    info.Flags,
                    down,
                    _eventTicks());
            }
            catch
            {
                // 钩子回调绝不能让异常逃逸：那会打断系统输入链，还可能被系统摘钩。
            }
        }

        // hhk 参数被系统忽略；传 IntPtr.Zero 省掉一次跨线程句柄读取。
        return HookNative.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                _health.NoteMouse();

                int message = unchecked((int)wParam.ToInt64());
                HookNative.MsLlHookStruct info =
                    Marshal.PtrToStructure<HookNative.MsLlHookStruct>(lParam);
                long ticks = _eventTicks();

                switch (message)
                {
                    case InputMessages.WM_MOUSEMOVE:
                        _sink.Move(info.Point.X, info.Point.Y, ticks);
                        break;

                    case InputMessages.WM_LBUTTONDOWN:
                    case InputMessages.WM_LBUTTONUP:
                    case InputMessages.WM_RBUTTONDOWN:
                    case InputMessages.WM_RBUTTONUP:
                    case InputMessages.WM_MBUTTONDOWN:
                    case InputMessages.WM_MBUTTONUP:
                    case InputMessages.WM_XBUTTONDOWN:
                    case InputMessages.WM_XBUTTONUP:
                    {
                        int button = InputMessages.MouseButtonId(message, info.MouseData);
                        if (button != 0)
                        {
                            bool down =
                                message is InputMessages.WM_LBUTTONDOWN
                                    or InputMessages.WM_RBUTTONDOWN
                                    or InputMessages.WM_MBUTTONDOWN
                                    or InputMessages.WM_XBUTTONDOWN;
                            _sink.MouseButton(button, down, info.Point.X, info.Point.Y, ticks);
                        }

                        break;
                    }

                    case InputMessages.WM_MOUSEWHEEL:
                        _sink.Wheel(false, HookNative.WheelDelta(info.MouseData), ticks);
                        break;

                    case InputMessages.WM_MOUSEHWHEEL:
                        _sink.Wheel(true, HookNative.WheelDelta(info.MouseData), ticks);
                        break;
                }
            }
            catch
            {
                // 同上：吞掉后仍要把事件交还给下一个钩子。
            }
        }

        return HookNative.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}
