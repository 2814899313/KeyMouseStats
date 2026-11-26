using Kms.Core.Model;
using Kms.Input.Activity;

namespace Kms.Input.Tests;

public sealed class ActivityTrackerTests
{
    private static readonly DateTime Origin = new(2026, 9, 3, 12, 0, 0);

    [Fact]
    public void Sample_CountsOnlyTheGuaranteedActivePrefix()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);

        Assert.False(tracker.Sample(Origin, 0, 0, 60));
        Assert.Empty(days);

        Assert.False(tracker.Sample(Origin.AddMilliseconds(250), 250, 250, 60));
        Assert.Empty(days);

        for (int i = 1; i <= 65; i++)
        {
            tracker.Sample(Origin.AddSeconds(i), i * 1000L, i * 1000.0, 60);
        }

        DayRecord day = days[Origin.Date];
        Assert.Equal(60, day.ActiveSeconds, 6);
        Assert.Equal(5, day.IdleSeconds, 6);
        Assert.Single(day.Sessions);
        Assert.Single(day.IdlePeriods);
        Assert.Equal(Origin.AddSeconds(60), day.IdlePeriods[0].Start);
        Assert.Equal(5, day.IdlePeriods[0].Seconds, 6);
        Assert.Equal(60, day.ActiveHours[12], 6);
        Assert.Equal(60, day.Sessions[0].Seconds, 6);
        Assert.False(tracker.IsActive);
    }

    [Fact]
    public void Sample_NewInputAfterIdleStartsANewSession()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 0, 60);
        for (int i = 1; i <= 65; i++)
        {
            tracker.Sample(Origin.AddSeconds(i), i * 1000L, i * 1000.0, 60);
        }

        DayRecord day = days[Origin.Date];

        tracker.Sample(Origin.AddSeconds(66), 66_000, 0, 60);
        tracker.Sample(Origin.AddSeconds(67), 67_000, 1000, 60);
        Assert.Equal(61, day.ActiveSeconds, 6);
        Assert.Equal(2, day.Sessions.Count);
        Assert.Equal(Origin.AddSeconds(66), day.Sessions[1].Start);
        Assert.Equal(SessionStartReason.Idle, day.Sessions[1].StartReason);

        tracker.Sample(Origin.AddSeconds(69.5), 69_500, 3500, 60);
        Assert.Equal(63.5, day.ActiveSeconds, 6);
    }

    [Fact]
    public void Sample_ObservationGapNeverBackfills()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 55_000, 60);
        tracker.Sample(Origin.AddSeconds(1), 1000, 55_000, 60);

        DayRecord day = days[Origin.Date];
        double before = day.ActiveSeconds;
        Assert.Equal(1, before, 6);

        // 55 s + 10 s 已经越过阈值：这段 10 秒无法保证活跃，只能断开。
        Assert.False(tracker.Sample(Origin.AddSeconds(11), 11_000, 1000, 60));
        Assert.Equal(before, day.ActiveSeconds, 6);

        // 下一枪重新建立基准，再从新观测点开始积分。
        Assert.True(tracker.Sample(Origin.AddSeconds(12), 12_000, 2000, 60));
        Assert.Equal(1, day.ActiveSeconds - before, 6);
        Assert.Equal(SessionStartReason.ObservationGap, day.Sessions[^1].StartReason);
    }

    [Fact]
    public void Sample_ClockDiscontinuityIsNotBridged()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 0, 60);
        tracker.Sample(Origin.AddSeconds(1), 1000, 1000, 60);

        DayRecord day = days[Origin.Date];
        double before = day.ActiveSeconds;

        // 壁钟跳 1 小时，单调钟只走 1 秒：必须断开。
        Assert.False(tracker.Sample(Origin.AddHours(1), 2000, 1000, 60));
        Assert.Equal(before, day.ActiveSeconds, 6);

        Assert.True(tracker.Sample(Origin.AddHours(1).AddSeconds(1), 3000, 2000, 60));
        Assert.Equal(1, day.ActiveSeconds - before, 6);
        Assert.Equal(SessionStartReason.ClockChange, day.Sessions[^1].StartReason);
    }

    [Fact]
    public void Sample_SplitsSessionAtEveryHour()
    {
        DateTime start = new(2026, 9, 3, 12, 59, 59);
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(start, 0, 0, 60);

        Assert.True(tracker.Sample(start.AddSeconds(2), 2000, 0, 60));

        DayRecord day = days[start.Date];
        Assert.Equal(2, day.ActiveSeconds, 6);
        Assert.Equal(1, day.ActiveHours[12], 6);
        Assert.Equal(1, day.ActiveHours[13], 6);

        // v3 的「整点切段」只切小时桶，活跃段本身保持连续。
        Assert.Single(day.Sessions);
        Assert.Equal(start.AddSeconds(2), day.Sessions[0].End);
        Assert.Equal(2, day.Sessions[0].Seconds, 6);

        Assert.True(tracker.Sample(start.AddSeconds(3), 3000, 0, 60));
        Assert.Single(day.Sessions);
        Assert.Equal(3, day.Sessions[0].Seconds, 6);
    }

    [Fact]
    public void Sample_SplitsDayAndMarksNewDay()
    {
        DateTime midnight = new(2026, 9, 4, 0, 0, 0);
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(midnight.AddSeconds(-1), 0, 0, 60);

        Assert.True(tracker.Sample(midnight.AddSeconds(1), 2000, 0, 60));

        DayRecord previous = days[midnight.AddDays(-1).Date];
        DayRecord current = days[midnight.Date];
        Assert.Equal(1, previous.ActiveSeconds, 6);
        Assert.Equal(1, current.ActiveSeconds, 6);
        Assert.Single(previous.Sessions);
        Assert.Single(current.Sessions);
        Assert.Equal(SessionStartReason.NewDay, current.Sessions[0].StartReason);
        Assert.Equal(1, previous.ActiveHours[23], 6);
        Assert.Equal(1, current.ActiveHours[0], 6);

        Assert.True(tracker.Sample(midnight.AddSeconds(2), 3000, 0, 60));
        Assert.Single(current.Sessions);
        Assert.Equal(2, current.Sessions[0].Seconds, 6);
    }

    [Fact]
    public void Break_LockStartsFreshSessionAndDoesNotCountPause()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 0, 60);
        tracker.Sample(Origin.AddSeconds(1), 1000, 0, 60);

        DayRecord day = days[Origin.Date];
        Assert.Equal(1, day.ActiveSeconds, 6);

        tracker.Break(SessionStartReason.Lock);
        Assert.False(tracker.IsActive);
        Assert.Null(tracker.CurrentSession);

        Assert.False(tracker.Sample(Origin.AddSeconds(6), 6000, 0, 60));
        Assert.True(tracker.Sample(Origin.AddSeconds(7), 7000, 0, 60));

        Assert.Equal(2, day.ActiveSeconds, 6);
        Assert.Equal(2, day.Sessions.Count);
        Assert.Equal(Origin.AddSeconds(6), day.Sessions[1].Start);
        Assert.Equal(SessionStartReason.Lock, day.Sessions[1].StartReason);
    }

    [Fact]
    public void Break_RetainsSuspendAndDisconnectReasons()
    {
        foreach (SessionStartReason reason in new[] { SessionStartReason.Suspend, SessionStartReason.Disconnect })
        {
            Dictionary<DateTime, DayRecord> days = [];
            ActivityTracker tracker = CreateTracker(days);
            tracker.Sample(Origin, 0, 0, 60);
            tracker.Sample(Origin.AddSeconds(1), 1000, 0, 60);

            tracker.Break(reason);
            tracker.Sample(Origin.AddSeconds(10), 10_000, 0, 60);
            tracker.Sample(Origin.AddSeconds(11), 11_000, 0, 60);

            DayRecord day = days[Origin.Date];
            Assert.Equal(reason, tracker.CurrentSession!.StartReason);
            Assert.Equal(2, day.Sessions.Count);
        }
    }

    [Fact]
    public void RollDay_SwitchesDayAndMarksNewDaySession()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 0, 60);
        tracker.Sample(Origin.AddSeconds(1), 1000, 0, 60);

        DateTime next = Origin.Date.AddDays(1).AddHours(8);
        DayRecord rolled = tracker.RollDay(next);
        Assert.Same(rolled, tracker.GetDay(next));
        Assert.Empty(rolled.Sessions);
        Assert.False(tracker.IsActive);

        Assert.False(tracker.Sample(next, 2000, 0, 60));
        Assert.True(tracker.Sample(next.AddSeconds(1), 3000, 0, 60));

        Assert.Equal(1, rolled.ActiveSeconds, 6);
        Assert.Single(rolled.Sessions);
        Assert.Equal(SessionStartReason.NewDay, rolled.Sessions[0].StartReason);
    }

    [Fact]
    public void Sample_InvalidIdleResetsAndRecoversWithoutBackfill()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 0, 60);
        tracker.Sample(Origin.AddSeconds(1), 1000, 0, 60);

        DayRecord day = days[Origin.Date];
        Assert.Equal(1, day.ActiveSeconds, 6);

        Assert.False(tracker.Sample(Origin.AddSeconds(2), 2000, double.NaN, 60));
        Assert.False(tracker.IsActive);
        Assert.Equal(1, day.ActiveSeconds, 6);

        Assert.False(tracker.Sample(Origin.AddSeconds(3), 3000, 0, 60));
        Assert.True(tracker.Sample(Origin.AddSeconds(4), 4000, 0, 60));
        Assert.Equal(2, day.ActiveSeconds, 6);
        Assert.Equal(SessionStartReason.InputUnavailable, day.Sessions[^1].StartReason);
    }

    [Fact]
    public void Sample_UintMaxIdleSentinelIsUnavailable()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 0, 60);
        tracker.Sample(Origin.AddSeconds(1), 1000, 0, 60);

        Assert.False(tracker.Sample(Origin.AddSeconds(2), 2000, uint.MaxValue, 60));
        Assert.False(tracker.IsActive);

        Assert.False(tracker.Sample(Origin.AddSeconds(3), 3000, 0u, 60));
        Assert.True(tracker.Sample(Origin.AddSeconds(4), 4000, 0u, 60));
        Assert.Equal(SessionStartReason.InputUnavailable, days[Origin.Date].Sessions[^1].StartReason);
    }

    [Fact]
    public void Sample_DelayedGuaranteedActiveIntervalKeepsSameSession()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        tracker.Sample(Origin, 0, 0, 300);
        tracker.Sample(Origin.AddSeconds(1), 1000, 1000, 300);
        ActiveSession first = tracker.CurrentSession!;

        Assert.True(tracker.Sample(Origin.AddSeconds(10), 10_000, 10_000, 300));
        DayRecord day = days[Origin.Date];
        Assert.Single(day.Sessions);
        Assert.Same(first, tracker.CurrentSession);

        Assert.True(tracker.Sample(Origin.AddSeconds(190), 190_000, 190_000, 300));
        Assert.Equal(190, day.ActiveSeconds, 6);
        Assert.Equal(190, day.ActiveHours[12], 6);
        Assert.Equal(SessionStartReason.Startup, first.StartReason);

        // 阈值已经过期，无法推断未观测的 120 秒。
        Assert.False(tracker.Sample(Origin.AddSeconds(310), 310_000, 1000, 300));
        Assert.True(tracker.Sample(Origin.AddSeconds(311), 311_000, 2000, 300));
        Assert.Equal(191, day.ActiveSeconds, 6);
        Assert.Equal(2, day.Sessions.Count);
        Assert.Equal(SessionStartReason.ObservationGap, tracker.CurrentSession!.StartReason);
    }

    [Fact]
    public void Sample_DataContextFactoryCreatesMissingDay()
    {
        StatsData data = new();
        ActivityTracker tracker = new(data);
        tracker.Sample(Origin, 0, 0, 60);
        Assert.True(tracker.Sample(Origin.AddSeconds(1), 1000, 0, 60));

        string key = Origin.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(data.History.TryGetValue(key, out DayRecord? day));
        Assert.Equal(1, day.ActiveSeconds, 6);
    }

    [Fact]
    public void Attribution_MatchesV3BoundaryRules()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        ActivityAttribution attribution = new(date => GetDay(days, date));
        tracker.OnActiveInterval = attribution.Interval;

        WindowInfo a = new(@"C:\a.exe", "a.exe", "A");
        WindowInfo b = new(@"C:\b.exe", "b.exe", "B");
        string aId = @"c:\a.exe" + "\nA";
        string bId = @"c:\b.exe" + "\nB";

        attribution.Prepare(Origin, a);
        Assert.False(tracker.Sample(Origin, 0, 0, 60));
        attribution.Finish(Origin, false, true);

        attribution.Prepare(Origin.AddSeconds(1), a);
        Assert.True(tracker.Sample(Origin.AddSeconds(1), 1000, 0, 60));
        attribution.Finish(Origin.AddSeconds(1), true, true);

        DayRecord day = days[Origin.Date];
        Assert.Equal(1, day.Apps[aId].ActiveSeconds, 6);

        attribution.Prepare(Origin.AddSeconds(2), b);
        Assert.True(tracker.Sample(Origin.AddSeconds(2), 2000, 0, 60));
        attribution.Finish(Origin.AddSeconds(2), true, true);

        Assert.Equal(1, day.AppSwitches);
        Assert.False(day.Apps.ContainsKey(bId));
        Assert.Equal(2, day.AppObservedSeconds, 6);

        attribution.Prepare(Origin.AddSeconds(3), b);
        Assert.True(tracker.Sample(Origin.AddSeconds(3), 3000, 0, 60));
        attribution.Finish(Origin.AddSeconds(3), true, true);

        Assert.Equal(1, day.Apps[bId].ActiveSeconds, 6);
        Assert.Equal(3, day.AppObservedSeconds, 6);

        attribution.Reset();
        attribution.Prepare(Origin.AddSeconds(4), a);
        Assert.True(tracker.Sample(Origin.AddSeconds(4), 4000, 0, 60));
        attribution.Finish(Origin.AddSeconds(4), false, true);
        Assert.Equal(1, day.AppSwitches);

        attribution.Prepare(Origin.AddSeconds(5), b);
        Assert.True(tracker.Sample(Origin.AddSeconds(5), 5000, 0, 60));
        attribution.Finish(Origin.AddSeconds(5), true, false);
        Assert.Equal(1, day.AppSwitches);
    }

    [Fact]
    public void Attribution_SnapshotGapOverFiveSecondsIsNotAttributed()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        ActivityAttribution attribution = new(date => GetDay(days, date));
        tracker.OnActiveInterval = attribution.Interval;

        WindowInfo a = new(@"C:\a.exe", "a.exe", "A");
        attribution.Prepare(Origin, a);
        tracker.Sample(Origin, 0, 0, 60);
        attribution.Finish(Origin, false, true);

        // 快照间隔 6 秒：观测秒数照记，但边界不可靠，不归因。
        attribution.Prepare(Origin.AddSeconds(6), a);
        tracker.Sample(Origin.AddSeconds(6), 6000, 0, 60);
        attribution.Finish(Origin.AddSeconds(6), true, true);

        DayRecord day = days[Origin.Date];
        Assert.Equal(6, day.AppObservedSeconds, 6);
        Assert.Empty(day.Apps);
    }

    [Fact]
    public void Monitor_LockSuspendAndDisconnectStartFreshSessions()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        DateTime now = Origin;
        long monotonic = 0;
        uint idle = 0;
        ActivityMonitor monitor = new(tracker, () => idle, () => now, () => monotonic);

        Assert.False(monitor.Tick(60));
        Advance(TimeSpan.FromSeconds(1));
        Assert.True(monitor.Tick(60));

        DayRecord day = days[Origin.Date];
        Assert.Equal(1, day.ActiveSeconds, 6);

        monitor.Message(ActivityMonitor.WM_WTSSESSION_CHANGE, ActivityMonitor.WTS_SESSION_LOCK);
        Assert.True(monitor.Busy);
        Assert.False(monitor.IsActive);
        Advance(TimeSpan.FromSeconds(5));
        Assert.False(monitor.Tick(60));

        monitor.Message(ActivityMonitor.WM_WTSSESSION_CHANGE, ActivityMonitor.WTS_SESSION_UNLOCK);
        Assert.False(monitor.Busy);
        Assert.False(monitor.Tick(60));
        Advance(TimeSpan.FromSeconds(1));
        Assert.True(monitor.Tick(60));
        Assert.Equal(2, day.ActiveSeconds, 6);
        Assert.Equal(SessionStartReason.Lock, day.Sessions[^1].StartReason);

        monitor.Message(ActivityMonitor.WM_POWERBROADCAST, ActivityMonitor.PBT_APMSUSPEND);
        Assert.True(monitor.Busy);
        Advance(TimeSpan.FromSeconds(2));
        Assert.False(monitor.Tick(60));
        monitor.Message(ActivityMonitor.WM_POWERBROADCAST, ActivityMonitor.PBT_APMRESUMESUSPEND);
        Assert.False(monitor.Busy);
        Assert.False(monitor.Tick(60));
        Advance(TimeSpan.FromSeconds(1));
        Assert.True(monitor.Tick(60));
        Assert.Equal(3, day.ActiveSeconds, 6);
        Assert.Equal(SessionStartReason.Suspend, day.Sessions[^1].StartReason);

        monitor.Message(ActivityMonitor.WM_WTSSESSION_CHANGE, ActivityMonitor.WTS_REMOTE_DISCONNECT);
        Assert.True(monitor.Busy);
        Advance(TimeSpan.FromSeconds(2));
        Assert.False(monitor.Tick(60));
        monitor.Message(ActivityMonitor.WM_WTSSESSION_CHANGE, ActivityMonitor.WTS_REMOTE_CONNECT);
        Assert.False(monitor.Busy);
        Assert.False(monitor.Tick(60));
        Advance(TimeSpan.FromSeconds(1));
        Assert.True(monitor.Tick(60));
        Assert.Equal(4, day.ActiveSeconds, 6);
        Assert.Equal(SessionStartReason.Disconnect, day.Sessions[^1].StartReason);

        void Advance(TimeSpan delta)
        {
            now += delta;
            monotonic += (long)delta.TotalMilliseconds;
        }
    }

    [Fact]
    public void Monitor_UnavailableIdleResetsTracker()
    {
        Dictionary<DateTime, DayRecord> days = [];
        ActivityTracker tracker = CreateTracker(days);
        DateTime now = Origin;
        long monotonic = 0;
        uint idle = 0;
        ActivityMonitor monitor = new(tracker, () => idle, () => now, () => monotonic);

        monitor.Tick(60);
        now = Origin.AddSeconds(1);
        monotonic = 1000;
        monitor.Tick(60);
        Assert.Equal(1, days[Origin.Date].ActiveSeconds, 6);

        idle = uint.MaxValue;
        now = Origin.AddSeconds(2);
        monotonic = 2000;
        Assert.False(monitor.Tick(60));
        Assert.Equal("无法读取空闲状态", monitor.Status);
        Assert.False(tracker.IsActive);

        idle = 0;
        now = Origin.AddSeconds(3);
        monotonic = 3000;
        Assert.False(monitor.Tick(60));
        now = Origin.AddSeconds(4);
        monotonic = 4000;
        Assert.True(monitor.Tick(60));
        Assert.Equal(2, days[Origin.Date].ActiveSeconds, 6);
    }

    [Fact]
    public void Codec_ParsesAndEncodesV3Formats()
    {
        Assert.Equal(0, ActivityMonitor.ParseSeconds("NaN"));
        Assert.Equal(0, ActivityMonitor.ParseSeconds("-5"));
        Assert.Equal(0, ActivityMonitor.ParseSeconds("Infinity"));
        Assert.Equal(2.5, ActivityMonitor.ParseSeconds("2.5"), 6);

        double[] hours = new double[24];
        hours[3] = 12.5;
        hours[23] = 0.25;
        double[] loaded = new double[24];
        ActivityMonitor.LoadHours(loaded, ActivityMonitor.EncodeHours(hours));
        Assert.Equal(12.5, loaded[3], 6);
        Assert.Equal(0.25, loaded[23], 6);

        DayRecord day = new() { Date = Origin.Date };
        ActiveSession session = new()
        {
            Start = Origin,
            End = Origin.AddSeconds(10),
            Seconds = 10,
            RateMeasured = true,
            PeakApm = 120,
            LastApm = 80,
            PeakOffsetSeconds = 3
        };
        ActivityMonitor.LoadSession(day, ActivityMonitor.EncodeSession(session));
        Assert.Single(day.Sessions);
        Assert.True(day.Sessions[0].RateMeasured);
        Assert.Equal(120, day.Sessions[0].PeakApm);
        Assert.Equal(80, day.Sessions[0].LastApm);
        Assert.Equal(3, day.Sessions[0].PeakOffsetSeconds, 6);

        day.Sessions[0].StartReason = SessionStartReason.Legacy;
        ActivityMonitor.LoadSessionReason(day, ActivityMonitor.EncodeSessionReason(
            new ActiveSession { Start = Origin, StartReason = SessionStartReason.Lock }));
        Assert.Equal(SessionStartReason.Lock, day.Sessions[0].StartReason);

        int count = day.Sessions.Count;
        ActivityMonitor.LoadSession(
            day,
            Origin.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                + "|"
                + Origin.AddSeconds(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                + "|1");
        Assert.Equal(count, day.Sessions.Count);

        Assert.Equal("59 秒", ActivityMonitor.FormatDuration(59.9));
        Assert.Equal("1 分 1 秒", ActivityMonitor.FormatDuration(61));
        Assert.Equal("1 时 1 分", ActivityMonitor.FormatDuration(3661));
        Assert.Equal(60, ActivityMonitor.NormalizeIdleThreshold(5));
        Assert.Equal(500, ActivityMonitor.NormalizeIdleThreshold(9999));
        Assert.Equal(120, ActivityMonitor.NormalizeIdleThreshold(120));
        Assert.Equal(1000u, ActivityMonitor.IdleMilliseconds(500, uint.MaxValue - 499));
    }

    [Fact]
    public void RemoveLegacyPhantomSessions_OnlyRemovesKnownArtifact()
    {
        DayRecord day = new() { Date = Origin.Date };
        DateTime edge = Origin.AddTicks(2000);
        day.IdlePeriods.Add(new ActiveSession { Start = Origin.AddSeconds(-1), End = edge, Seconds = 1.0002 });
        day.Sessions.Add(new ActiveSession { Start = edge, End = edge.AddTicks(1000), Seconds = 0.0001 });
        day.Sessions.Add(new ActiveSession { Start = Origin.AddSeconds(5), End = Origin.AddSeconds(5).AddTicks(1000), Seconds = 0.0001 });
        day.Sessions.Add(new ActiveSession { Start = edge, End = edge.AddSeconds(2), Seconds = 2 });

        Assert.Equal(1, ActivityMonitor.RemoveLegacyPhantomSessions(day));
        Assert.Equal(2, day.Sessions.Count);
        Assert.Equal(0, ActivityMonitor.RemoveLegacyPhantomSessions(day));
    }

    private static ActivityTracker CreateTracker(Dictionary<DateTime, DayRecord> days)
        => new(date => GetDay(days, date));

    private static DayRecord GetDay(Dictionary<DateTime, DayRecord> days, DateTime date)
    {
        DateTime key = date.Date;
        if (!days.TryGetValue(key, out DayRecord? day))
        {
            day = new DayRecord { Date = key };
            days[key] = day;
        }

        return day;
    }
}
