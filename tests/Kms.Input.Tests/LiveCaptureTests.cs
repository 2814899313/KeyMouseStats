using System.Diagnostics;
using Kms.Core.Input;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Input.Counting;

namespace Kms.Input.Tests;

public sealed class LiveCaptureTests
{
    private static readonly long TickPerMs = Stopwatch.Frequency / 1000;

    [Fact]
    public void KeyEx_NormalizesLeftControlAndRecordsPhysicalKey()
    {
        (CountingInputSink sink, StatsData data) = NewSink();

        sink.KeyEx(0x11, 0x1D, 0, down: true, ticks: 0);
        sink.KeyEx(0x11, 0x1D, 0, down: false, ticks: 10 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(1, day.KeyCounts[0xA2]);
        Assert.Equal(1, day.PhysicalKeys[0x1D]);
        Assert.Equal(1, day.PhysicalVks[0xA2]);
        Assert.Equal(1, day.Keys);
    }

    [Fact]
    public void KeyEx_DetectsCtrlACombo()
    {
        (CountingInputSink sink, StatsData data) = NewSink();

        sink.KeyEx(0x11, 0x1D, 0, true, 0);
        sink.KeyEx(0x41, 0x1E, 0, true, 50 * TickPerMs);
        sink.KeyEx(0x41, 0x1E, 0, false, 100 * TickPerMs);
        sink.KeyEx(0x11, 0x1D, 0, false, 150 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(1, day.ComboCounts["ctrl_a"]);
        Assert.Equal(2, day.Keys);
    }

    [Fact]
    public void Hold_SettlesOnReleaseAndUpdatesTotals()
    {
        (CountingInputSink sink, StatsData data) = NewSink();

        sink.KeyEx(0x41, 0x1E, 0, true, 0);
        sink.KeyEx(0x41, 0x1E, 0, false, 120 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(1, day.HoldCount);
        Assert.Equal(1, day.Holds[0x41].Count);
        Assert.Equal(120, day.Holds[0x41].TotalMs, 6);
        Assert.Equal(1, day.HoldBuckets[HoldTracker.BucketIndex(120)]);
        Assert.Equal(1, data.Totals.HoldCount);
    }

    [Fact]
    public void Hold_OverLimitIsDiscarded()
    {
        (CountingInputSink sink, StatsData data) = NewSink();

        sink.KeyEx(0x41, 0x1E, 0, true, 0);
        sink.KeyEx(0x41, 0x1E, 0, false, 61_000 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(0, day.HoldCount);
        Assert.Equal(1, day.HoldDiscarded);
        Assert.Equal(1, data.Totals.HoldDiscarded);
    }

    [Fact]
    public void Hold_ActiveSpanCountsWhileAnyKeyIsDown()
    {
        (CountingInputSink sink, StatsData data) = NewSink();

        sink.KeyEx(0x41, 0x1E, 0, true, 0);
        sink.KeyEx(0x42, 0x30, 0, true, 50 * TickPerMs);
        sink.KeyEx(0x41, 0x1E, 0, false, 100 * TickPerMs);
        sink.KeyEx(0x42, 0x30, 0, false, 200 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(0.2, day.HoldActiveSeconds, 6);
        Assert.Equal(0.2, data.Totals.HoldActiveSeconds, 6);
    }

    [Fact]
    public void Queue_PreservesScanCodeAndFlags()
    {
        (CountingInputSink sink, StatsData data) = NewSink();
        QueuedInputSink queue = new();

        queue.KeyEx(0x41, 0x1E, 1, down: true, ticks: 0);
        Assert.Equal(1, queue.ReceivedKeys);
        Assert.Equal(1, queue.Drain(sink));

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(1, day.PhysicalKeys[0x11E]);
        Assert.Equal(1, day.Keys);
    }

    [Fact]
    public void ClickHeat_RecordsCellAndChannel()
    {
        (CountingInputSink sink, StatsData data) = NewSink();
        ClickHeat.ResetDedupe();

        sink.MouseButton(1, true, 100, 50, 0);

        DayRecord day = data.History["2026-11-23"];
        int cell = ClickHeat.CellKey(3, 1);
        Assert.Equal(1, day.ClickCells[cell]);
        Assert.Equal(1, day.ClickCellsV1[ClickHeat.ChannelKey(1, cell)]);
    }

    [Fact]
    public void ClickHeat_SameCellSameButtonWithinWindowIsDeduped()
    {
        (CountingInputSink sink, StatsData data) = NewSink();
        ClickHeat.ResetDedupe();

        sink.MouseButton(1, true, 100, 50, 0);
        sink.MouseButton(1, true, 101, 51, TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(2, day.Clicks);
        Assert.Equal(1, day.ClickCells[ClickHeat.CellKey(3, 1)]);
    }

    [Fact]
    public void AppWindow_ReceivesKeyGroupsAndClicks()
    {
        (CountingInputSink sink, StatsData data) = NewSink();
        sink.AppWindow = static () => new Kms.Input.Activity.WindowInfo(
            @"C:\Apps\demo.exe", "demo", "Demo Window");

        sink.KeyEx(0x41, 0x1E, 0, true, 0);
        sink.MouseButton(1, true, 10, 10, TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        AppUsage app = Assert.Single(day.Apps.Values);
        Assert.Equal(1, app.Keys);
        Assert.Equal(1, app.Clicks);
        Assert.True(app.HasKeyGroups);
        Assert.Equal(1, app.KeyGroups[0]);
    }

    private static (CountingInputSink Sink, StatsData Data) NewSink()
    {
        FakeClock clock = new(new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Local));
        StatsData data = new();
        return (new CountingInputSink(data, new EventRing(64), clock), data);
    }

    [Fact]
    public void HoldTracker_DiscardPendingCountsDiscard()
    {
        StatsTotals totals = new();
        HoldTracker holds = new(totals);
        DayRecord day = new() { Date = new DateTime(2026, 11, 23) };

        holds.Press(day, 65, 0);
        Assert.Equal(1, holds.PendingCount);
        Assert.Equal(1, holds.DiscardPending(10 * TickPerMs));

        Assert.Equal(0, holds.PendingCount);
        Assert.Equal(1, day.HoldDiscarded);
        Assert.Equal(1, totals.HoldDiscarded);
    }

    [Fact]
    public void RateTracker_UpdatesDayAndAllTimePeakApm()
    {
        (CountingInputSink sink, StatsData data) = NewSink();

        sink.KeyEx(0x41, 0x1E, 0, true, 0);
        sink.KeyEx(0x41, 0x1E, 0, false, 10 * TickPerMs);
        sink.KeyEx(0x42, 0x30, 0, true, 20 * TickPerMs);
        sink.KeyEx(0x42, 0x30, 0, false, 30 * TickPerMs);
        sink.MouseButton(1, true, 10, 10, 40 * TickPerMs);

        DayRecord day = data.History["2026-11-23"];
        Assert.Equal(3, day.PeakApm);
        Assert.Equal(3, sink.Apm);
        Assert.Equal("3", data.Settings["all_time_peak_apm"]);
    }

    [Fact]
    public void RateTracker_MeasuresSessionPeak()
    {
        RateTracker rate = new();
        DateTime now = DateTime.Now;
        ActiveSession session = new() { Start = now.AddSeconds(-10), End = now };

        rate.AddKey(now);
        rate.AddClick(now);
        rate.Measure(session, now);

        Assert.True(session.RateMeasured);
        Assert.Equal(2, session.LastApm);
        Assert.Equal(2, session.PeakApm);
    }

    [Fact]
    public void ShortcutTracker_NormalizesRightAltAndSuppressesAutoRepeat()
    {
        ShortcutTracker tracker = new();

        tracker.Process(true, 18, 0x38, flags: 1, ticks: 0, out bool first);
        Assert.True(first);
        Assert.Equal(0xA5, ShortcutTracker.Normalize(18, 0x38, 1));

        tracker.Process(true, 18, 0x38, flags: 1, ticks: 100 * TickPerMs, out first);
        Assert.False(first);

        tracker.Process(false, 18, 0x38, flags: 1, ticks: 200 * TickPerMs, out _);
        tracker.Process(true, 18, 0x38, flags: 1, ticks: 1_500 * TickPerMs, out first);
        Assert.True(first);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTime local) => UtcNow = local.ToUniversalTime();

        public DateTime UtcNow { get; set; }

        public long MonotonicTicks => Stopwatch.GetTimestamp();
    }
}
