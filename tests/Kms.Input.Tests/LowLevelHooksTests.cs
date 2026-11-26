using Kms.Input.Hooks;

namespace Kms.Input.Tests;

/// <summary>
/// 低级钩子的纯逻辑：消息 → 按钮编号、滚轮高位取符号、看门狗健康判定。
/// 不调用 SetWindowsHookEx：CI 没有交互式桌面与消息循环，真实安装放到宿主的 UI 线程验收。
/// </summary>
public sealed class LowLevelHooksTests
{
    [Theory]
    [InlineData(InputMessages.WM_LBUTTONDOWN, 0u, 1)]
    [InlineData(InputMessages.WM_LBUTTONUP, 0u, 1)]
    [InlineData(InputMessages.WM_RBUTTONDOWN, 0u, 2)]
    [InlineData(InputMessages.WM_RBUTTONUP, 0u, 2)]
    [InlineData(InputMessages.WM_MBUTTONDOWN, 0u, 3)]
    [InlineData(InputMessages.WM_MBUTTONUP, 0u, 3)]
    [InlineData(InputMessages.WM_XBUTTONDOWN, 0x0001_0000u, 4)]
    [InlineData(InputMessages.WM_XBUTTONUP, 0x0001_0000u, 4)]
    [InlineData(InputMessages.WM_XBUTTONDOWN, 0x0002_0000u, 5)]
    [InlineData(InputMessages.WM_XBUTTONUP, 0x0002_0000u, 5)]
    [InlineData(InputMessages.WM_XBUTTONDOWN, 0u, 0)]
    [InlineData(InputMessages.WM_XBUTTONUP, 0x0003_0000u, 0)]
    [InlineData(InputMessages.WM_MOUSEMOVE, 0u, 0)]
    [InlineData(InputMessages.WM_MOUSEWHEEL, 0x0078_0000u, 0)]
    public void MouseButtonId_MapsHookMessages(int message, uint mouseData, int expected)
    {
        Assert.Equal(expected, InputMessages.MouseButtonId(message, mouseData));
    }

    [Theory]
    [InlineData(0x0078_0000u, 120)]
    [InlineData(0xFF88_0000u, -120)]
    [InlineData(0x00F0_0000u, 240)]
    [InlineData(0x0001_0000u, 1)]
    [InlineData(0x8000_0000u, -32768)]
    [InlineData(0x0000_0000u, 0)]
    [InlineData(0x0000_FFFFu, 0)]
    public void WheelDelta_ExtractsSignedHighWord(uint mouseData, int expected)
    {
        Assert.Equal(expected, HookNative.WheelDelta(mouseData));
    }

    [Fact]
    public void HealthMonitor_FreshMonitorIsHealthy()
    {
        long now = 5_000;
        HookHealthMonitor health = new(() => now, static () => 500u, static () => false);

        Assert.Equal(HookRepair.None, health.Evaluate(keyboardAlive: true, mouseAlive: true));
    }

    [Fact]
    public void HealthMonitor_RepairsOnlyTheStalledDevice()
    {
        long now = 1_000;
        HookHealthMonitor health = new(() => now, static () => 800u, static () => false);

        now += HookWatchdog.StallWindowMs + 1;
        health.NoteKeyboard();

        Assert.Equal(now, health.LastKeyboardTickMs);
        Assert.Equal(HookRepair.Mouse, health.Evaluate(keyboardAlive: true, mouseAlive: true));

        health.NoteMouse();

        Assert.Equal(now, health.LastMouseTickMs);
        Assert.Equal(HookRepair.None, health.Evaluate(keyboardAlive: true, mouseAlive: true));
    }

    [Fact]
    public void HealthMonitor_BusySessionNeverRepairs()
    {
        long now = 0;
        bool busy = false;
        HookHealthMonitor health = new(() => now, static () => 800u, () => busy);

        now = 100_000;
        busy = true;

        Assert.Equal(HookRepair.None, health.Evaluate(keyboardAlive: true, mouseAlive: true));

        // 锁屏期间句柄丢失也不能重装：会打断仍在按住的键。
        Assert.Equal(HookRepair.None, health.Evaluate(keyboardAlive: false, mouseAlive: false));
    }

    [Theory]
    [InlineData(60_000u)]
    [InlineData(HookWatchdog.SystemIdleLimitMs)]
    [InlineData(uint.MaxValue)]
    public void HealthMonitor_IdleOrUnknownIdleIsNotAStall(uint idleMs)
    {
        long now = 0;
        uint idle = 500;
        HookHealthMonitor health = new(() => now, () => idle, static () => false);

        now = 100_000;
        idle = idleMs;

        Assert.Equal(HookRepair.None, health.Evaluate(keyboardAlive: true, mouseAlive: true));
    }

    [Fact]
    public void HealthMonitor_LostHandleRepairsEvenWhenUserIdle()
    {
        long now = 0;
        HookHealthMonitor health = new(() => now, static () => 60_000u, static () => false);

        now = 100_000;

        Assert.Equal(
            HookRepair.Keyboard | HookRepair.Mouse,
            health.Evaluate(keyboardAlive: false, mouseAlive: false));
        Assert.Equal(HookRepair.Keyboard, health.Evaluate(keyboardAlive: false, mouseAlive: true));
        Assert.Equal(HookRepair.Mouse, health.Evaluate(keyboardAlive: true, mouseAlive: false));
    }

    [Fact]
    public void HealthMonitor_ResetRestartsTheObservationWindow()
    {
        long now = 0;
        HookHealthMonitor health = new(() => now, static () => 800u, static () => false);

        now = HookWatchdog.StallWindowMs + 1;
        health.Reset();

        Assert.Equal(now, health.LastKeyboardTickMs);
        Assert.Equal(now, health.LastMouseTickMs);
        Assert.Equal(HookRepair.None, health.Evaluate(keyboardAlive: true, mouseAlive: true));
    }

    [Fact]
    public void LowLevelHooks_WithoutInstall_ReportsBothHooksForRepair()
    {
        long now = 1_234;
        using LowLevelHooks hooks = new(
            new NoopSink(),
            tickCountMs: () => now,
            systemIdleMs: static () => uint.MaxValue,
            busy: static () => false,
            eventTicks: static () => 0L);

        Assert.False(hooks.IsKeyboardAlive);
        Assert.False(hooks.IsMouseAlive);
        Assert.Equal(1_234, hooks.LastKeyboardTickMs);
        Assert.Equal(1_234, hooks.LastMouseTickMs);
        Assert.Equal(
            HookRepair.Keyboard | HookRepair.Mouse,
            hooks.CheckHealth());
    }

    [Fact]
    public void LowLevelHooks_CheckHealthHonorsBusySession()
    {
        using LowLevelHooks hooks = new(
            new NoopSink(),
            tickCountMs: static () => 0L,
            systemIdleMs: static () => 800u,
            busy: static () => true,
            eventTicks: static () => 0L);

        // 锁屏/休眠期间即使句柄丢了也先不重装：安全桌面里装不上，还会打断按住的键。
        Assert.Equal(HookRepair.None, hooks.CheckHealth());
    }

    [Fact]
    public void LowLevelHooks_UninstallWithoutInstallIsSafeAndRepeatable()
    {
        using LowLevelHooks hooks = new(
            new NoopSink(),
            tickCountMs: static () => 0L,
            systemIdleMs: static () => uint.MaxValue,
            busy: static () => false,
            eventTicks: static () => 0L);

        hooks.Uninstall();
        hooks.Uninstall();

        Assert.False(hooks.IsKeyboardAlive);
        Assert.False(hooks.IsMouseAlive);
    }

    private sealed class NoopSink : IInputSink
    {
        public void Key(int virtualKey, bool down, long ticks)
        {
        }

        public void MouseButton(int button, bool down, int x, int y, long ticks)
        {
        }

        public void Wheel(bool horizontal, int delta, long ticks)
        {
        }

        public void Move(int x, int y, long ticks)
        {
        }
    }
}
