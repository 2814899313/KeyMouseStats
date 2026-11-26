namespace Kms.Input.Hooks;

/// <summary>需要重装的钩子位。可与 <c>|</c> 组合，0 表示两条钩子都健康。</summary>
[Flags]
public enum HookRepair
{
    None = 0,
    Keyboard = 1,
    Mouse = 2,
}

/// <summary>
/// 钩子健康采样器：记录每一路最后一次收到事件的本机毫秒时刻，
/// 再交给 <see cref="HookWatchdog.Evaluate"/> 判断是否需要重装。
///
/// 三条时钟全部可注入，测试不需要真实钩子，也不需要交互式桌面：
/// <list type="bullet">
/// <item><c>nowMs</c>：本机毫秒时钟，默认 <see cref="HookNative.TickCountMs"/>（会回绕）。</item>
/// <item><c>systemIdleMs</c>：系统空闲时长，默认 <see cref="HookNative.GetSystemIdleMilliseconds"/>。</item>
/// <item><c>busy</c>：锁屏/休眠/会话断开，默认永远 <c>false</c>，由宿主注入真实状态。</item>
/// </list>
///
/// 线程模型：<see cref="NoteKeyboard"/>/<see cref="NoteMouse"/> 在钩子回调线程调用，
/// <see cref="Evaluate"/> 在看门狗线程调用；时刻用 <see cref="Volatile"/> 读写，避免撕裂。
/// </summary>
public sealed class HookHealthMonitor
{
    private readonly Func<long> _nowMs;
    private readonly Func<uint> _systemIdleMs;
    private readonly Func<bool> _busy;

    private long _lastKeyboardTickMs;
    private long _lastMouseTickMs;

    public HookHealthMonitor(
        Func<long>? nowMs = null,
        Func<uint>? systemIdleMs = null,
        Func<bool>? busy = null)
    {
        _nowMs = nowMs ?? HookNative.TickCountMs;
        _systemIdleMs = systemIdleMs ?? HookNative.GetSystemIdleMilliseconds;
        _busy = busy ?? (static () => false);

        // 刚创建时视为「刚收到事件」：给新装的钩子一个完整的 StallWindow 观察期。
        long now = _nowMs();
        _lastKeyboardTickMs = now;
        _lastMouseTickMs = now;
    }

    /// <summary>键盘钩子最后一次收到事件的本机毫秒时刻。</summary>
    public long LastKeyboardTickMs => Volatile.Read(ref _lastKeyboardTickMs);

    /// <summary>鼠标钩子最后一次收到事件的本机毫秒时刻。</summary>
    public long LastMouseTickMs => Volatile.Read(ref _lastMouseTickMs);

    /// <summary>重装成功后调用：把两路计时一起拨到「现在」，重新获得一个完整观察期。</summary>
    public void Reset()
    {
        long now = _nowMs();
        Volatile.Write(ref _lastKeyboardTickMs, now);
        Volatile.Write(ref _lastMouseTickMs, now);
    }

    /// <summary>键盘回调收到任何事件（按下/抬起）。由钩子热路径调用，无分配。</summary>
    public void NoteKeyboard() => Volatile.Write(ref _lastKeyboardTickMs, _nowMs());

    /// <summary>鼠标回调收到任何事件（移动/点击/滚轮）。由钩子热路径调用，无分配。</summary>
    public void NoteMouse() => Volatile.Write(ref _lastMouseTickMs, _nowMs());

    /// <summary>
    /// 体检一次。返回需要重装的钩子位；返回 <see cref="HookRepair.None"/> 表示两条都健康。
    /// 锁屏/休眠（busy）与系统空闲期间永远不要求重装：那不是钩子坏了，是用户没在用。
    /// </summary>
    public HookRepair Evaluate(bool keyboardAlive, bool mouseAlive)
    {
        long now = _nowMs();
        HookWatchdog.Evaluate(
            keyboardAlive,
            mouseAlive,
            _busy(),
            _systemIdleMs(),
            HookWatchdog.Elapsed(now, LastKeyboardTickMs),
            HookWatchdog.Elapsed(now, LastMouseTickMs),
            out bool repairKeyboard,
            out bool repairMouse);

        HookRepair repair = HookRepair.None;
        if (repairKeyboard)
        {
            repair |= HookRepair.Keyboard;
        }

        if (repairMouse)
        {
            repair |= HookRepair.Mouse;
        }

        return repair;
    }
}
