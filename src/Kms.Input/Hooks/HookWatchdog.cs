namespace Kms.Input.Hooks;

/// <summary>
/// 钩子看门狗。Windows 会在两种情况下静默摘掉低级钩子：
/// 回调超过 LowLevelHooksTimeout，或被安全软件/会话切换卸载。
/// 判断做成纯函数，测试不依赖真实钩子。
/// </summary>
public static class HookWatchdog
{
    public const int CheckIntervalMs = 5_000;
    public const int StallWindowMs = 30_000;
    public const uint SystemIdleLimitMs = 10_000;

    public static bool ShouldRepair(bool alive, bool busy, uint systemIdleMs, long msSinceLastEvent)
    {
        if (busy)
        {
            return false;
        }

        if (!alive)
        {
            return true;
        }

        if (systemIdleMs == uint.MaxValue)
        {
            return false;
        }

        if (systemIdleMs >= SystemIdleLimitMs)
        {
            return false;
        }

        return msSinceLastEvent > StallWindowMs;
    }

    public static void Evaluate(
        bool keyboardAlive,
        bool mouseAlive,
        bool busy,
        uint systemIdleMs,
        long msSinceLastKeyboardEvent,
        long msSinceLastMouseEvent,
        out bool repairKeyboard,
        out bool repairMouse)
    {
        repairKeyboard = ShouldRepair(keyboardAlive, busy, systemIdleMs, msSinceLastKeyboardEvent);
        repairMouse = ShouldRepair(mouseAlive, busy, systemIdleMs, msSinceLastMouseEvent);
    }

    /// <summary>TickCount 差值按 32 位无符号处理，跨越 49.7 天回绕不变负。</summary>
    public static long Elapsed(long now, long then) => unchecked((uint)(now - then));
}
