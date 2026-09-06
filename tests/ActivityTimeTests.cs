using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using KeyMouseStats;

internal static class ActivityTimeTests
{
    private static int _checks;
    private static readonly DateTime Origin = new DateTime(2026, 9, 3, 12, 0, 0);
    private static void Check(bool result, string name)
    {
        if (!result) throw new Exception(name); _checks++;
    }
    private static void Near(double result, double expected, string name) { Check(Math.Abs(result - expected) < 0.00001, name); }
    private static ActivityTracker Tracker(Dictionary<DateTime, DayRecord> days)
    {
        return new ActivityTracker(delegate(DateTime date)
        {
            DayRecord day;
            if (!days.TryGetValue(date, out day)) { day = new DayRecord { Date = date }; days[date] = day; }
            return day;
        });
    }
    private static void Main()
    {
        Dictionary<DateTime, DayRecord> days = new Dictionary<DateTime, DayRecord>();
        ActivityTracker tracker = Tracker(days);
        Check(!tracker.Sample(Origin, 0, 0, 60) && days.Count == 0, "startup does not backfill");
        Check(!tracker.Sample(Origin.AddMilliseconds(250), 250, 0.25, 60), "subsecond timer does not overcount");
        for (int i = 1; i <= 65; i++) tracker.Sample(Origin.AddSeconds(i), i * 1000, i, 60);
        DayRecord day = days[Origin.Date];
        Near(day.ActiveSeconds, 60, "threshold tail counted once");
        Near(day.IdleSeconds, 5, "idle starts exactly at threshold");
        Check(day.IdlePeriods.Count == 1 && day.IdlePeriods[0].Start == Origin.AddSeconds(60), "idle interval begins at threshold");
        Near(day.IdlePeriods[0].Seconds, 5, "idle samples merge into one interval");
        Check(day.Sessions.Count == 1 && !tracker.IsActive, "idle closes session");
        Near(day.ActiveHours[12], 60, "hour accounting");
        Near(day.Sessions[0].Seconds, 60, "session duration matches active time");
        tracker.Sample(Origin.AddSeconds(66), 66000, 0, 60);
        tracker.Sample(Origin.AddSeconds(67), 67000, 1, 60);
        Near(day.ActiveSeconds, 61, "new input resumes without counting prior idle second");
        Check(day.Sessions.Count == 2 && day.Sessions[1].Start == Origin.AddSeconds(66), "new session after idle");
        tracker.Sample(Origin.AddSeconds(69.5), 69500, 3.5, 60);
        Near(day.ActiveSeconds, 63.5, "timer jitter uses elapsed interval");
        tracker.Sample(Origin.AddHours(2), 7200000, 0, 60);
        Near(day.ActiveSeconds, 63.5, "sleep or long stall not backfilled");
        tracker.Sample(Origin.AddHours(2).AddSeconds(1), 7201000, 0.1, 60);
        Check(day.Sessions.Count == 3, "resume starts separate session");
        Near(day.ActiveSeconds, 64.5, "resume counts only observed second");
        tracker.Sample(Origin.AddHours(3), 7202000, 0.1, 60);
        Near(day.ActiveSeconds, 64.5, "wall-clock jump not counted");
        tracker.Sample(Origin.AddHours(3).AddSeconds(1), 7203000, 0.2, 60);
        Near(day.ActiveSeconds, 65.5, "counting recovers after clock jump");
        tracker.Reset();
        tracker.Sample(Origin.AddHours(4), 14400000, 0, 60);
        Near(day.ActiveSeconds, 65.5, "lock/restart reset does not bridge offline interval");
        tracker.Sample(Origin.AddHours(4).AddSeconds(1), 14401000, 0, 60);
        Check(day.Sessions.Count == 5, "reset starts new segment");

        days = new Dictionary<DateTime, DayRecord>(); tracker = Tracker(days);
        tracker.Sample(Origin, 0, 59.5, 60);
        tracker.Sample(Origin.AddSeconds(1), 1000, 0.2, 60);
        day = days[Origin.Date];
        Near(day.ActiveSeconds, 0.7, "threshold and new input in same interval");
        Near(day.IdleSeconds, 0.3, "short idle gap retained");
        Near(day.IdlePeriods[0].Seconds, 0.3, "subsecond idle boundaries retained");
        Check(day.Sessions.Count == 2, "subsecond idle gap separates sessions");

        days = new Dictionary<DateTime, DayRecord>(); tracker = Tracker(days);
        DateTime midnight = Origin.Date.AddDays(1);
        tracker.Sample(midnight.AddSeconds(-1), 0, 0, 60);
        tracker.Sample(midnight.AddSeconds(1), 2000, 0, 60);
        Near(days[midnight.AddDays(-1)].ActiveSeconds, 1, "midnight previous-day portion");
        Near(days[midnight].ActiveSeconds, 1, "midnight new-day portion");
        Check(days[midnight].Sessions.Count == 1 && days[midnight.AddDays(-1)].Sessions.Count == 1, "sessions split per day");
        Near(days[midnight.AddDays(-1)].ActiveHours[23], 1, "hour 23 portion");
        Near(days[midnight].ActiveHours[0], 1, "hour 0 portion");
        tracker.Sample(midnight.AddSeconds(2), 3000, 0, 60);
        Check(days[midnight].Sessions.Count == 1, "midnight segment continues");
        double total = days[midnight].ActiveSeconds;
        tracker.Sample(midnight.AddSeconds(3), 4000, double.NaN, 60);
        tracker.Sample(midnight.AddSeconds(4), 5000, 0, 60);
        Near(days[midnight].ActiveSeconds, total, "API failure recovery has no backfill");
        Check(ActivityMonitor.IdleMilliseconds(500, uint.MaxValue - 499) == 1000, "32-bit tick wrap");
        Check(ActivityMonitor.FormatDuration(59.9) == "59 秒" && ActivityMonitor.FormatDuration(3661) == "1 时 1 分", "duration formatting");
        Check(ActivityMonitor.ParseSeconds("NaN") == 0 && ActivityMonitor.ParseSeconds("-5") == 0
            && ActivityMonitor.ParseSeconds("Infinity") == 0, "invalid stored durations rejected");

        days = new Dictionary<DateTime, DayRecord>(); tracker = Tracker(days);
        tracker.Sample(midnight.AddSeconds(-1), 0, 100, 60);
        tracker.Sample(midnight.AddSeconds(1), 2000, 102, 60);
        Check(days[midnight.AddDays(-1)].IdlePeriods[0].End == midnight && days[midnight].IdlePeriods[0].Start == midnight, "idle interval splits at midnight");
        tracker.Reset();
        tracker.Sample(midnight.AddSeconds(2), 3000, 103, 60);
        tracker.Sample(midnight.AddSeconds(3), 4000, 104, 60);
        Check(days[midnight].IdlePeriods.Count == 2, "reset separates idle intervals");
        tracker.Sample(midnight.AddSeconds(20), 21000, 121, 60);
        tracker.Sample(midnight.AddSeconds(21), 22000, 122, 60);
        Check(days[midnight].IdlePeriods.Count == 3, "unobserved gap is never filled as idle");
        Near(days[midnight].IdleSeconds, 3, "idle gap totals exclude unobserved time");
        Check(!TimeInsights.HasTime(new DayRecord { Keys = 200 }) && !TimeInsights.HasTime(null), "legacy and missing days lack time coverage");
        DayRecord insight = new DayRecord();
        insight.Sessions.Add(new ActiveSession { Seconds = 60 }); insight.Sessions.Add(new ActiveSession { Seconds = 180 });
        Near(TimeInsights.MeanSession(insight), 120, "mean uses actual session seconds");
        Near(TimeInsights.MeanSession(new DayRecord()), 0, "empty mean safe");
        insight.IdlePeriods.Add(new ActiveSession { Seconds = 899 });
        insight.IdlePeriods.Add(new ActiveSession { Seconds = 900 });
        insight.IdlePeriods.Add(new ActiveSession { Seconds = 1200 });
        List<ActiveSession> gaps = TimeInsights.LongGaps(insight, 900);
        Check(gaps.Count == 2 && gaps[0].Seconds == 1200, "long gaps include threshold and sort longest first");

        // Real wall timestamps have sub-millisecond precision on .NET Framework.
        days=new Dictionary<DateTime,DayRecord>();tracker=Tracker(days);
        DateTime precise=Origin.AddTicks(1234);
        tracker.Sample(precise,0,100,60);
        for(int i=1;i<=200;i++)tracker.Sample(precise.AddTicks(i*10001234L),i*1000,100+i,60);
        day=days[Origin.Date];
        Check(day.Sessions.Count==0,"submillisecond idle timestamps never create phantom activity");
        Check(day.IdlePeriods.Count==1,"precise idle endpoints remain contiguous");
        Near(day.ActiveSeconds,0,"fully idle contributes zero active time");
        Near(day.IdleSeconds,200*1.0001234,"idle preserves full sampled duration");
        tracker.Sample(precise.AddTicks(201*10001234L),201000,0.2,60);
        tracker.Sample(precise.AddTicks(202*10001234L),202000,1.2,60);
        Check(day.Sessions.Count==1,"genuine input after precise idle creates one session");
        DayRecord legacy=new DayRecord();
        DateTime edge=Origin.AddTicks(2000);
        legacy.IdlePeriods.Add(new ActiveSession{Start=Origin.AddSeconds(-1),End=edge,Seconds=1.0002});
        legacy.Sessions.Add(new ActiveSession{Start=edge,End=edge.AddTicks(1000),Seconds=0.0001});
        legacy.Sessions.Add(new ActiveSession{Start=Origin.AddSeconds(5),End=Origin.AddSeconds(5).AddTicks(1000),Seconds=0.0001});
        legacy.Sessions.Add(new ActiveSession{Start=edge,End=edge.AddSeconds(2),Seconds=2});
        Check(ActivityMonitor.RemoveLegacyPhantomSessions(legacy)==1 && legacy.Sessions.Count==2,"repair only known idle-tail artifacts, preserving genuine short sessions");
        Check(ActivityMonitor.RemoveLegacyPhantomSessions(legacy)==0,"legacy cleanup is idempotent");

        // 1.1.1: delayed UI sampling must not split a fully guaranteed active interval.
        days=new Dictionary<DateTime,DayRecord>();tracker=Tracker(days);
        tracker.Sample(Origin,0,0,300);tracker.Sample(Origin.AddSeconds(1),1000,1,300);
        ActiveSession first=tracker.CurrentSession;
        Check(tracker.Sample(Origin.AddSeconds(10),10000,10,300),"nine second active timer delay accepted");
        day=days[Origin.Date];Check(day.Sessions.Count==1&&object.ReferenceEquals(first,tracker.CurrentSession),"delayed sample keeps same session");
        tracker.Sample(Origin.AddSeconds(190),190000,190,300);
        Near(day.ActiveSeconds,190,"guaranteed active time counted exactly once");Near(day.ActiveHours[12],190,"delayed active time assigned to correct hour");
        Check(first.StartReason==SessionStartReason.Startup,"first segment startup reason");
        // Threshold has already expired before the newest input: cannot infer the unobserved gap.
        tracker.Sample(Origin.AddSeconds(310),310000,1,300);tracker.Sample(Origin.AddSeconds(311),311000,2,300);
        Check(day.Sessions.Count==2&&tracker.CurrentSession.StartReason==SessionStartReason.ObservationGap,"uncertain delayed interval remains separate");
        Near(day.ActiveSeconds,191,"uncertain gap is not backfilled");
        tracker.Reset(SessionStartReason.Lock);tracker.Sample(Origin.AddSeconds(312),312000,0,300);tracker.Sample(Origin.AddSeconds(313),313000,1,300);
        Check(day.Sessions.Count==3&&tracker.CurrentSession.StartReason==SessionStartReason.Lock,"short lock interval never bridged");
        tracker.Reset(SessionStartReason.Suspend);tracker.Sample(Origin.AddSeconds(314),314000,0,300);tracker.Sample(Origin.AddSeconds(315),315000,1,300);
        Check(tracker.CurrentSession.StartReason==SessionStartReason.Suspend,"resume reason retained");
        tracker.Sample(Origin.AddSeconds(400),316000,2,300);tracker.Sample(Origin.AddSeconds(401),317000,3,300);
        Check(tracker.CurrentSession.StartReason==SessionStartReason.ClockChange,"paired clocks reject wall jump even within threshold");
        // Five minutes without input is already included at the old segment's end.
        days=new Dictionary<DateTime,DayRecord>();tracker=Tracker(days);tracker.Sample(Origin,0,0,300);
        for(int i=1;i<=301;i++)tracker.Sample(Origin.AddSeconds(i),i*1000,i,300);
        tracker.Sample(Origin.AddSeconds(302),302000,0,300);tracker.Sample(Origin.AddSeconds(303),303000,1,300);
        day=days[Origin.Date];Check(day.Sessions.Count==2&&day.Sessions[1].StartReason==SessionStartReason.Idle,"two second visible gap after 302 seconds without input is legitimate");
        Near(day.ActiveSeconds,301,"threshold buffer not counted twice");Near(day.IdleSeconds,2,"post-threshold gap retained");
        // An accepted long interval still splits cleanly across midnight.
        days=new Dictionary<DateTime,DayRecord>();tracker=Tracker(days);tracker.Sample(midnight.AddSeconds(-10),0,0,300);
        tracker.Sample(midnight.AddSeconds(10),20000,20,300);
        Near(days[midnight.AddDays(-1)].ActiveSeconds,10,"delayed midnight previous portion");Near(days[midnight].ActiveSeconds,10,"delayed midnight new portion");
        Check(days[midnight].Sessions[0].StartReason==SessionStartReason.NewDay,"midnight reason distinct");

        // Redirect all persistence to an isolated directory before reading or writing Store.
        string directory = Path.Combine(Path.GetTempPath(), "ActivityTimeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string file = Path.Combine(directory, "stats.txt");
        typeof(Store).GetField("Dir", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, directory);
        typeof(Store).GetField("FilePath", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, file);
        try
        {
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            Store.IdleThresholdSeconds = 120;
            Store.ThemeId = 3;
            DateTime start = DateTime.Today.AddHours(10);
            Store.Today.ActiveSeconds = 75.125; Store.Today.IdleSeconds = 15.75;
            Store.Today.ActiveHours[10] = 75.125;
            Store.Today.Sessions.Add(new ActiveSession { Start = start, End = start.AddSeconds(75.125), Seconds = 75.125, StartReason=SessionStartReason.Lock });
            Store.Today.IdlePeriods.Add(new ActiveSession { Start = start.AddSeconds(75.125), End = start.AddSeconds(90.875), Seconds = 15.75 });
            Store.History[DateTime.Today.AddDays(-364)] = new DayRecord { Date = DateTime.Today.AddDays(-364), Keys = 1 };
            Store.History[DateTime.Today.AddDays(-365)] = new DayRecord { Date = DateTime.Today.AddDays(-365), Keys = 2 };
            Check(!Store.Today.IsEmpty, "time-only days retained");
            Store.Save(); Store.History.Clear(); Store.RollDay(DateTime.Today); Store.IdleThresholdSeconds = 60; Store.ThemeId = 0; Store.Load();
            Check(Store.ThemeId == 3, "art theme persisted");
            Check(ArtTheme.Validate(-1) == 0 && ArtTheme.Validate(999) == 0, "invalid theme safely defaults");
            Near(Store.Today.ActiveSeconds, 75.125, "active duration round trip");
            Near(Store.Today.IdleSeconds, 15.75, "idle duration round trip");
            Check(Store.Today.IdlePeriods.Count == 1 && Store.Today.IdlePeriods[0].End == start.AddSeconds(90.875), "idle boundaries round trip");
            Check(Store.History.ContainsKey(DateTime.Today.AddDays(-364)) && !Store.History.ContainsKey(DateTime.Today.AddDays(-365)), "365 day retention boundary");
            Check(TimeInsights.Export(Store.Today).Contains("观测空闲,") && TimeInsights.Export(Store.Today).Contains(",15.75"), "time export includes observed idle");
            Near(Store.Today.ActiveHours[10], 75.125, "hourly duration round trip");
            Check(Store.Today.Sessions.Count == 1 && Store.Today.Sessions[0].End == start.AddSeconds(75.125), "session boundaries round trip");
            Check(Store.Today.Sessions[0].StartReason==SessionStartReason.Lock,"start reason survives actual save/load");
            ActivityMonitor.LoadSessionReason(Store.Today,start.Ticks+"|999");Check(Store.Today.Sessions[0].StartReason==SessionStartReason.Lock,"invalid reason ignored");
            DayRecord oldDay=new DayRecord{Date=start.Date};ActivityMonitor.LoadSession(oldDay,ActivityMonitor.EncodeSession(Store.Today.Sessions[0]));Check(oldDay.Sessions[0].StartReason==SessionStartReason.Legacy,"legacy records stay unclassified, never merged");
            Check(Store.IdleThresholdSeconds == 120, "threshold persisted");
            ActivityMonitor.LoadSession(Store.Today, start.ToString("O") + "|" + start.AddSeconds(-1).ToString("O") + "|1");
            Check(Store.Today.Sessions.Count == 1, "invalid reversed session rejected");
            File.WriteAllText(file, "idle_threshold=999999\n[day=" + DateTime.Today.ToString("yyyy-MM-dd") + "]\nkeys=5\n");
            Store.History.Clear(); Store.RollDay(DateTime.Today); Store.Load();
            Check(Store.Today.Keys == 5 && Store.Today.ActiveSeconds == 0 && Store.Today.Sessions.Count == 0, "old records do not invent duration");
            Check(Store.IdleThresholdSeconds == 60, "invalid threshold defaults safely");
        }
        finally
        {
            foreach (string name in new string[] { "stats.txt", "stats.txt.tmp", "stats.txt.bak" })
                if (File.Exists(Path.Combine(directory, name))) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }
        ActivityMonitor.Message(0x02B1, new IntPtr(7));
        ActivityMonitor.Message(0x02B1, new IntPtr(1));
        Check((bool)typeof(ActivityMonitor).GetField("_locked", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), "reconnect does not unlock session");
        ActivityMonitor.Message(0x02B1, new IntPtr(8));
        Check(!(bool)typeof(ActivityMonitor).GetField("_locked", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), "unlock notification");
        ActivityMonitor.Message(0x0218, new IntPtr(4));
        Check(!ActivityMonitor.IsActive, "suspend clears active state");
        ActivityMonitor.Message(0x0218, new IntPtr(18));
        Check(!ActivityMonitor.IsActive, "resume awaits fresh observation");
        Console.WriteLine("PASS: " + _checks + " activity timing checks; " + (IntPtr.Size * 8) + "-bit");
    }
}
