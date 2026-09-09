using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KeyMouseStats;

internal static class StatisticsTests
{
    private static int checks;
    private static void Check(bool result, string name) { if (!result) throw new Exception(name); checks++; }
    private static void Near(double a, double b, string name) { Check(Math.Abs(a - b) < .00001, name); }
    private static void Main()
    {
        List<double> values = new List<double> { 5, 1, 3, 2, 4 };
        Near(PersonalStats.Quantile(values, .5), 3, "median"); Near(PersonalStats.Quantile(values, .9), 4.6, "interpolated P90");
        Near(PersonalStats.Mean(values), 3, "mean"); Near(PersonalStats.Sd(values), Math.Sqrt(2.5), "sample sd");
        Check(double.IsNaN(PersonalStats.Ratio(3, 0)), "zero denominator missing");
        Check(double.IsNaN(PersonalStats.Quantile(new List<double>(), .9)), "empty percentile missing");
        Check(PersonalStats.Change(3, 0) == "基准为 0" && PersonalStats.Change(0, 0) == "持平", "zero baseline no infinity");
        Check(PersonalStats.Change(150, 100) == "+50.0%", "relative change");
        Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today); Store.MouseDpi = 1600;
        DayRecord day = Store.Today; day.Keys = 120; day.Clicks = 60; day.Wheel = 30; day.ActiveSeconds = 60; day.MoveMeters = 18; day.ComboCounts["ctrl_c"] = 12;
        Near(PersonalStats.Metric(day, 0), 2, "key mouse ratio"); Near(PersonalStats.Metric(day, 1), 2, "keys per active second");
        Near(PersonalStats.Metric(day, 2), 60, "clicks per active minute"); Near(PersonalStats.Metric(day, 3), .1, "meters per operation");
        Near(PersonalStats.Metric(day, 4), 30, "wheel intensity"); Near(PersonalStats.Metric(day, 5), 10, "combo percentage");
        for (int i = 1; i <= 7; i++) Store.History[DateTime.Today.AddDays(-i)] = new DayRecord { Date = DateTime.Today.AddDays(-i), Keys = i };
        Near(PersonalStats.MovingAverage(DateTime.Today.AddDays(-1), 0), 4, "7 day mean excludes candidate day");
        Store.History.Remove(DateTime.Today.AddDays(-4));
        Check(double.IsNaN(PersonalStats.MovingAverage(DateTime.Today.AddDays(-1), 0)), "missing day breaks moving mean");
        Check(PersonalStats.History(DateTime.Today.AddDays(-1), 7, delegate(DayRecord d) { return d.Keys; }).Count == 6, "coverage excludes missing days");
        day.ActiveHours[12] = 60; day.HourKeys[12] = 120; day.HourClicks[12] = 10;
        Near(Dashboard.HourlyValues(day, DateTime.Today, 3, DateTime.Today.AddHours(13))[12], 2, "hourly key intensity");
        Check(double.IsNaN(Dashboard.HourlyValues(day, DateTime.Today, 3, DateTime.Today.AddHours(13))[11]), "no active seconds no intensity");

        AppTelemetry.Reset(); DateTime t = DateTime.Today.AddHours(10);
        AppUsage a = new AppUsage { ProcessPath = @"C:\a.exe", Title = "A" }, b = new AppUsage { ProcessPath = @"C:\b.exe", Title = "B" };
        ActiveSession session = new ActiveSession { Start = t, End = t.AddSeconds(3), Seconds = 3 };
        AppTelemetry.Prepare(t, a); AppTelemetry.Finish(t, false, true);
        AppTelemetry.Prepare(t.AddSeconds(1), a); AppTelemetry.Interval(day, session, t, t.AddSeconds(1)); AppTelemetry.Finish(t.AddSeconds(1), true, true);
        Near(day.Apps[a.Id].ActiveSeconds, 1, "stable app active interval attributed");
        AppTelemetry.Prepare(t.AddSeconds(2), b); AppTelemetry.Interval(day, session, t.AddSeconds(1), t.AddSeconds(2)); AppTelemetry.Finish(t.AddSeconds(2), true, true);
        Check(day.AppSwitches == 1 && !day.Apps.ContainsKey(b.Id), "switch counted but uncertain boundary not attributed");
        AppTelemetry.Prepare(t.AddSeconds(3), b); AppTelemetry.Interval(day, session, t.AddSeconds(2), t.AddSeconds(3)); AppTelemetry.Finish(t.AddSeconds(3), true, true);
        Near(day.AppObservedSeconds, 3, "all observed active intervals tracked"); Near(day.Apps[b.Id].ActiveSeconds, 1, "next stable interval attributed");
        AppTelemetry.Reset(); AppTelemetry.Prepare(t.AddSeconds(4), a); AppTelemetry.Finish(t.AddSeconds(4), false, true);
        Check(day.AppSwitches == 1, "restart never creates a switch");
        AppTelemetry.Prepare(t.AddSeconds(5), b); AppTelemetry.Finish(t.AddSeconds(5), true, false);
        Check(day.AppSwitches == 1, "idle switch excluded");

        LiveRate.Reset(); Store.AllTimePeakApm = 0; day.PeakApm = 0;
        LiveRate.AddKey(); LiveRate.AddClick(); LiveRate.AddKey();
        Check(LiveRate.Apm == 3 && day.PeakApm == 3 && Store.AllTimePeakApm == 3, "APM and peaks share rolling key plus click count");
        RateMemory.Clear(); RateMemory.Add(t.AddSeconds(1)); RateMemory.Add(t.AddSeconds(4));
        ActiveSession newer = new ActiveSession { Start = t.AddSeconds(2), End = t.AddSeconds(5), Seconds = 3 };
        RateMemory.UpdateSession(newer, t.AddSeconds(5));
        Check(newer.PeakApm == 1 && newer.LastApm == 1 && newer.RateMeasured, "session excludes preceding session events");
        Near(newer.PeakOffsetSeconds, 3, "time to sampled peak");
        RateMemory.UpdateSession(newer, t.AddSeconds(65));
        Check(newer.LastApm == 0 && newer.PeakApm == 1, "session falloff does not erase peak");
        DayRecord decoded = new DayRecord { Date = t.Date };
        ActivityMonitor.LoadSession(decoded, ActivityMonitor.EncodeSession(newer));
        Check(decoded.Sessions.Count == 1 && decoded.Sessions[0].RateMeasured && decoded.Sessions[0].PeakApm == 1, "session peak serialization");
        ActiveSession old = new ActiveSession { Start = t, End = t.AddSeconds(1), Seconds = 1 };
        ActivityMonitor.LoadSession(decoded, ActivityMonitor.EncodeSession(old));
        Check(!decoded.Sessions[1].RateMeasured, "legacy session peak remains unknown");
        DayRecord decodedApp = new DayRecord(); AppActivity.LoadRecord(decodedApp, AppActivity.Serialize(day.Apps[a.Id]));
        Near(decodedApp.Apps[a.Id].ActiveSeconds, 1, "app duration serialization");

        string directory = Path.Combine(Path.GetTempPath(), "StatisticsTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "stats.txt");
        Store.DataDirectory = directory;
        try
        {
            day.Sessions.Add(newer); Store.Save(); Store.History.Clear(); Store.RollDay(DateTime.Today); Store.AllTimePeakApm = 0; Store.Load();
            Check(Store.Today.PeakApm == 3 && Store.AllTimePeakApm == 3 && Store.Today.AppSwitches == 1, "daily and lifetime peaks and switches persisted");
            Near(Store.Today.AppObservedSeconds, 3, "app observation coverage persisted");
            Near(Store.Today.Apps[a.Id].ActiveSeconds, 1, "app active time survives full save/load");
            Check(Store.Today.Sessions[0].RateMeasured, "session telemetry survives full save/load");
        }
        finally
        {
            foreach (string name in new string[] { "stats.txt", "stats.txt.tmp", "stats.txt.bak" }) if (File.Exists(Path.Combine(directory, name))) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }
        Console.WriteLine("PASS: " + checks + " statistical and telemetry checks");
    }
}
