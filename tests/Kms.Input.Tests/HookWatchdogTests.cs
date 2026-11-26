using Kms.Input.Hooks;

namespace Kms.Input.Tests;

public sealed class HookWatchdogTests
{
    [Theory]
    [InlineData(false, true, 0u, 999999L, false)]
    [InlineData(true, true, 0u, 999999L, false)]
    [InlineData(false, false, 0u, 0L, true)]
    [InlineData(false, false, uint.MaxValue, 0L, true)]
    [InlineData(true, false, 500u, 1200L, false)]
    [InlineData(true, false, HookWatchdog.SystemIdleLimitMs, 999999L, false)]
    [InlineData(true, false, 60000u, 999999L, false)]
    [InlineData(true, false, 800u, HookWatchdog.StallWindowMs + 1000, true)]
    [InlineData(true, false, HookWatchdog.SystemIdleLimitMs - 1, HookWatchdog.StallWindowMs + 1, true)]
    [InlineData(true, false, uint.MaxValue, 999999L, false)]
    public void ShouldRepair_MatchesV3Table(
        bool alive,
        bool busy,
        uint idleMs,
        long sinceLastEvent,
        bool expected)
    {
        Assert.Equal(expected, HookWatchdog.ShouldRepair(alive, busy, idleMs, sinceLastEvent));
    }

    [Fact]
    public void Evaluate_RepairsStalledKeyboardIndependently()
    {
        HookWatchdog.Evaluate(true, true, false, 800, HookWatchdog.StallWindowMs + 1, 500,
            out bool keyboard, out bool mouse);

        Assert.True(keyboard);
        Assert.False(mouse);
    }

    [Fact]
    public void Evaluate_RepairsStalledMouseIndependently()
    {
        HookWatchdog.Evaluate(true, true, false, 800, 500, HookWatchdog.StallWindowMs + 1,
            out bool keyboard, out bool mouse);

        Assert.False(keyboard);
        Assert.True(mouse);
    }

    [Fact]
    public void Evaluate_BusySessionNeverRepairs()
    {
        HookWatchdog.Evaluate(true, true, true, 800, long.MaxValue, long.MaxValue,
            out bool keyboard, out bool mouse);

        Assert.False(keyboard);
        Assert.False(mouse);
    }

    [Fact]
    public void Elapsed_HandlesTickCountWrap()
    {
        Assert.Equal(1, HookWatchdog.Elapsed(-2147483648L, 2147483647L));
    }

    [Fact]
    public void Thresholds_AreContract()
    {
        Assert.Equal(5_000, HookWatchdog.CheckIntervalMs);
        Assert.Equal(30_000, HookWatchdog.StallWindowMs);
        Assert.Equal(10_000u, HookWatchdog.SystemIdleLimitMs);
    }
}
