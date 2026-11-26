using System.Diagnostics;
using Kms.Core.Input;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Input.Counting;

namespace Kms.Input.Tests;

public sealed class CountingInputSinkTests
{
    private static readonly long TickPerMs = Stopwatch.Frequency / 1000;

    [Fact]
    public void Key_IgnoresAutoRepeatAndCountsAfterRelease()
    {
        FakeClock clock = new(new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Local));
        StatsData data = new();
        CountingInputSink sink = new(data, new EventRing(64), clock);

        sink.Key(65, true, 0);
        sink.Key(65, true, 100 * TickPerMs);
        sink.Key(65, true, 200 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(1, day.Keys);
        Assert.Equal(1, day.KeyCounts[65]);
        Assert.Equal(1, data.Totals.Keys);

        sink.Key(65, false, 300 * TickPerMs);
        sink.Key(65, true, 1_400 * TickPerMs);
        Assert.Equal(2, day.Keys);
    }

    [Fact]
    public void MouseButton_CountsOnlyDownAndMapsButtons()
    {
        FakeClock clock = new(new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Local));
        StatsData data = new();
        CountingInputSink sink = new(data, new EventRing(64), clock);

        sink.MouseButton(1, true, 10, 10, 0);
        sink.MouseButton(1, false, 10, 10, TickPerMs);
        sink.MouseButton(2, true, 20, 20, 2 * TickPerMs);
        sink.MouseButton(3, true, 30, 30, 3 * TickPerMs);
        sink.MouseButton(4, true, 40, 40, 4 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(4, day.Clicks);
        Assert.Equal(1, day.Left);
        Assert.Equal(1, day.Right);
        Assert.Equal(1, day.Middle);
        Assert.Equal(1, day.XButtons);
        Assert.Equal(4, data.Totals.Clicks);
    }

    [Fact]
    public void Wheel_CountsEventsAndHorizontalSubset()
    {
        FakeClock clock = new(new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Local));
        StatsData data = new();
        CountingInputSink sink = new(data, new EventRing(64), clock);

        sink.Wheel(false, 60, 0);
        sink.Wheel(false, 60, TickPerMs);
        sink.Wheel(true, -120, 2 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(3, day.Wheel);
        Assert.Equal(1, day.HWheel);
        Assert.Equal(3, data.Totals.Wheel);
        Assert.Equal(1, data.Totals.HWheel);
    }

    [Fact]
    public void Move_AccumulatesPixelsOnlyAfterFirstPoint()
    {
        FakeClock clock = new(new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Local));
        StatsData data = new();
        CountingInputSink sink = new(data, new EventRing(64), clock);

        sink.Move(0, 0, 0);
        sink.Move(3, 4, TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(5, day.MovePx, 9);
        Assert.Equal(5, data.Totals.MovePx, 9);
    }

    [Fact]
    public void DayRollover_UsesLocalDateFromClock()
    {
        FakeClock clock = new(new DateTime(2026, 11, 23, 23, 59, 0, DateTimeKind.Local));
        StatsData data = new();
        CountingInputSink sink = new(data, new EventRing(64), clock);

        sink.Key(65, true, 0);
        clock.UtcNow = new DateTime(2026, 11, 24, 0, 1, 0, DateTimeKind.Local).ToUniversalTime();
        sink.Key(66, true, 2 * 60 * 1000 * TickPerMs);

        Assert.Equal(1, data.History["2026-11-23"].Keys);
        Assert.Equal(1, data.History["2026-11-24"].Keys);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime local) => UtcNow = local.ToUniversalTime();

        public DateTime UtcNow { get; set; }

        public long MonotonicTicks => Stopwatch.GetTimestamp();
    }
}
