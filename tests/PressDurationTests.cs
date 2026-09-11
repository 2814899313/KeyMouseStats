using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 按键时长、应用×键位与月度归档的回归:配对与丢弃规则、编码守恒、Top N 截断、
/// 归档折叠与保留期边界。只使用内存数据与临时目录。
/// </summary>
internal static class PressDurationTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
    private static void Near(double actual, double expected, double tolerance, string name)
    {
        Check(Math.Abs(actual - expected) <= tolerance, name + " (expected " + expected + ", got " + actual + ")");
    }
    private static long Ticks(double milliseconds)
    {
        return (long)(milliseconds / 1000.0 * Stopwatch.Frequency);
    }

    private static DayRecord NewDay(DateTime date)
    {
        DayRecord day = new DayRecord();
        day.Date = date;
        day.Keys = 1;
        return day;
    }

    private static void Main()
    {
        // ---- 分档边界 ----
        Check(HoldTracker.BucketIndex(0) == 0 && HoldTracker.BucketIndex(49.9) == 0, "under 50 ms is the first bucket");
        Check(HoldTracker.BucketIndex(50) == 1 && HoldTracker.BucketIndex(99) == 1, "50–100 ms bucket");
        Check(HoldTracker.BucketIndex(4999) == 6, "under 5 s bucket");
        Check(HoldTracker.BucketIndex(5000) == 7 && HoldTracker.BucketIndex(99999) == 7, "5 s and above is the last bucket");
        Check(HoldTracker.BucketIndex(250) == 3 && HoldTracker.BucketIndex(700) == 4, "middle buckets");

        // ---- 配对与计时 ----
        HoldTracker.DiscardPending();
        DayRecord day = NewDay(DateTime.Today);
        long start = Stopwatch.GetTimestamp();
        HoldTracker.Press(day, 65, start);
        Check(HoldTracker.PendingCount == 1, "press is pending");
        Check(HoldTracker.Release(65, start + Ticks(120)), "release settles a sample");
        Check(day.HoldCount == 1 && day.HoldDiscarded == 0, "one valid sample");
        Near(day.HoldTotalMs, 120, 2, "duration measured from ticks");
        Near(day.HoldMaxMs, 120, 2, "max duration recorded");
        Check(day.HoldBuckets[2] == 1, "sample lands in the 100–200 ms bucket");
        Check(day.Holds[65].Count == 1, "per-key sample recorded");
        Near(day.Holds[65].Mean, 120, 2, "per-key mean");

        // ---- 自动重复不重置起点:重复的按下在 ShortcutTracker 就被丢掉,不会到达 HoldTracker ----
        HoldTracker.DiscardPending();
        day = NewDay(DateTime.Today);
        ShortcutTracker seq = new ShortcutTracker();
        start = Stopwatch.GetTimestamp();
        bool first;
        seq.Process(Native.WM_KEYDOWN, 87, 0, 0, start, out first);
        HoldTracker.Press(day, 87, start);
        for (int i = 1; i <= 20; i++)
        {
            seq.Process(Native.WM_KEYDOWN, 87, 0, 0, start + Ticks(i * 30), out first);
            if (first) HoldTracker.Press(day, 87, start + Ticks(i * 30));   // 自动重复不该走到这里
        }
        Check(HoldTracker.PendingCount == 1, "auto-repeat keeps a single pending press");
        Check(HoldTracker.Release(87, start + Ticks(800)), "auto-repeat still settles once");
        Check(day.HoldCount == 1 && day.HoldDiscarded == 0, "auto-repeat does not create extra samples");
        Near(day.HoldTotalMs, 800, 3, "auto-repeat keeps the first press time");

        // ---- 丢 UP / 超长按 ----
        HoldTracker.DiscardPending();
        day = NewDay(DateTime.Today);
        Check(!HoldTracker.Release(65, Stopwatch.GetTimestamp()), "release without press is ignored");
        Check(day.HoldCount == 0 && day.HoldDiscarded == 0, "ignored release does not touch the day");
        HoldTracker.Press(day, 87, Stopwatch.GetTimestamp());
        Check(!HoldTracker.Release(87, Stopwatch.GetTimestamp() + Ticks(90000)), "over the maximum is discarded");
        Check(day.HoldCount == 0 && day.HoldDiscarded == 1, "discarded sample is counted separately");
        HoldTracker.Press(day, 87, Stopwatch.GetTimestamp());
        Check(HoldTracker.DiscardPending() == 1 && day.HoldDiscarded == 2, "pending keys are discarded on request");
        Check(HoldTracker.PendingCount == 0, "discard clears the pending map");

        // ---- 跨日:样本算在按下那天 ----
        HoldTracker.DiscardPending();
        DayRecord yesterday = NewDay(DateTime.Today.AddDays(-1));
        DayRecord today = NewDay(DateTime.Today);
        start = Stopwatch.GetTimestamp();
        HoldTracker.Press(yesterday, 65, start);
        Check(HoldTracker.Release(65, start + Ticks(150)), "cross-day release settles");
        Check(yesterday.HoldCount == 1 && today.HoldCount == 0, "cross-day sample belongs to the day it started");

        // ---- 1.7.5:丢失抬起后的重新按下不与旧按下配对 ----
        HoldTracker.DiscardPending();
        day = NewDay(DateTime.Today);
        start = Stopwatch.GetTimestamp();
        HoldTracker.Press(day, 65, start);                                  // 第一次按下
        HoldTracker.Press(day, 65, start + Ticks(3000));                    // UP 丢失,3 秒后重新按下
        Check(day.HoldDiscarded == 1 && day.HoldCount == 0, "a re-press without a release discards the stale sample");
        Check(HoldTracker.PendingCount == 1, "the re-press opens a fresh pending sample");
        Check(HoldTracker.Release(65, start + Ticks(3100)), "the fresh sample settles on its own release");
        Check(day.HoldCount == 1 && day.HoldDiscarded == 1, "only the real hold survives");
        Near(day.HoldTotalMs, 100, 2, "the 3 s gap between two taps never becomes a hold sample");
        Check(day.HoldBuckets[2] == 1 && day.HoldMaxMs < 200, "the surviving sample lands in the 100-200 ms bucket");
        Check(day.Holds[65].Count == 1, "per-key detail keeps exactly one sample");

        // 跨日的重新按下:旧样本丢弃在它开始的那天,新样本算在重新按下那天。
        HoldTracker.DiscardPending();
        yesterday = NewDay(DateTime.Today.AddDays(-1));
        today = NewDay(DateTime.Today);
        start = Stopwatch.GetTimestamp();
        HoldTracker.Press(yesterday, 65, start);
        HoldTracker.Press(today, 65, start + Ticks(4000));
        Check(yesterday.HoldDiscarded == 1 && today.HoldDiscarded == 0, "the stale sample is discarded on the day it started");
        Check(HoldTracker.Release(65, start + Ticks(4100)), "the cross-day re-press settles");
        Check(today.HoldCount == 1 && yesterday.HoldCount == 0, "the fresh sample belongs to the re-press day");

        // ---- 1.7.5:锁屏 / 休眠丢弃还按着的键 ----
        HoldTracker.DiscardPending();
        day = NewDay(DateTime.Today);
        HoldTracker.Press(day, 65, Stopwatch.GetTimestamp());
        HoldTracker.Press(day, 87, Stopwatch.GetTimestamp());
        ActivityMonitor.Message(0x02B1, new IntPtr(7));                     // WM_WTSSESSION_CHANGE:锁屏
        Check(HoldTracker.PendingCount == 0 && day.HoldDiscarded == 2, "locking the session discards every key still down");
        HoldTracker.Press(day, 65, Stopwatch.GetTimestamp());
        ActivityMonitor.Message(0x0218, new IntPtr(4));                     // WM_POWERBROADCAST:休眠
        Check(HoldTracker.PendingCount == 0 && day.HoldDiscarded == 3, "suspending discards keys still down");
        ActivityMonitor.Message(0x02B1, new IntPtr(8));                     // 解锁
        ActivityMonitor.Message(0x0218, new IntPtr(7));                     // 恢复

        // ---- 编码往返 ----
        HoldTracker.DiscardPending();
        day = NewDay(DateTime.Today);
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 5; i++)
        {
            HoldTracker.Press(day, 65, start);
            HoldTracker.Release(65, start + Ticks(80 + i * 10));
        }
        HoldTracker.Press(day, 87, start);
        HoldTracker.Release(87, start + Ticks(1500));
        day.HoldDiscarded = 3;
        string encoded = HoldCodec.Encode(day);
        Check(encoded != null && encoded.Split('|').Length == 5, "hold summary has five fields");
        DayRecord restored = NewDay(DateTime.Today);
        HoldCodec.Load(restored, encoded);
        Check(restored.HoldCount == day.HoldCount, "hold count round trip");
        Near(restored.HoldTotalMs, day.HoldTotalMs, 0.001, "hold total round trip");
        Check(restored.HoldDiscarded == 3, "discarded count round trip");
        long buckets = 0;
        foreach (long value in restored.HoldBuckets) buckets += value;
        Check(buckets == restored.HoldCount, "buckets conserve the sample count");

        string keys = HoldCodec.EncodeKeys(day);
        DayRecord keyRestored = NewDay(DateTime.Today);
        HoldCodec.LoadKeys(keyRestored, keys);
        Check(keyRestored.Holds.Count == 2, "per-key detail round trip");
        Check(keyRestored.Holds[65].Count == 5 && keyRestored.Holds[87].Count == 1, "per-key counts round trip");
        Check(string.Join(",", HoldCodec.EncodeAll(day)).Contains("hold_key_v1="), "EncodeAll emits both lines");

        // ---- 1.7.5:同一天区段重复解析时,汇总与逐键明细都累加,保持自洽 ----
        DayRecord twice = NewDay(DateTime.Today);
        HoldCodec.Load(twice, encoded);
        HoldCodec.LoadKeys(twice, keys);
        HoldCodec.Load(twice, encoded);
        HoldCodec.LoadKeys(twice, keys);
        long twiceBuckets = 0, twiceDetail = 0;
        foreach (long value in twice.HoldBuckets) twiceBuckets += value;
        foreach (KeyValuePair<int, KeyHold> pair in twice.Holds) twiceDetail += pair.Value.Count;
        Check(twice.HoldCount == day.HoldCount * 2, "repeated day sections accumulate instead of overwriting");
        Check(twiceBuckets == twice.HoldCount && twiceDetail == twice.HoldCount,
            "repeated day sections keep summary and per-key detail consistent");

        // ---- 逐键 Top N 截断:次数与总时长必须守恒 ----
        HoldTracker.DiscardPending();
        day = NewDay(DateTime.Today);
        long expectedCount = 0;
        double expectedTotal = 0;
        for (int vk = 1; vk <= HoldTracker.KeyDetailLimit + 7; vk++)
        {
            int count = 30 - vk;
            if (count <= 0) count = 1;
            start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                HoldTracker.Press(day, vk, start);
                HoldTracker.Release(day == null ? vk : vk, start + Ticks(100 + vk));
            }
            expectedCount += count;
            expectedTotal += count * (100 + vk);
        }
        string many = HoldCodec.EncodeKeys(day);
        DayRecord manyRestored = NewDay(DateTime.Today);
        HoldCodec.LoadKeys(manyRestored, many);
        long restoredCount = 0;
        double restoredTotal = 0;
        foreach (KeyValuePair<int, KeyHold> pair in manyRestored.Holds)
        {
            restoredCount += pair.Value.Count;
            restoredTotal += pair.Value.TotalMs;
        }
        Check(restoredCount == expectedCount, "Top N truncation conserves the sample count");
        Near(restoredTotal, expectedTotal, 1, "Top N truncation conserves the total duration");
        Check(manyRestored.Holds.ContainsKey(HoldTracker.OtherKey), "truncated keys fold into the other bucket");
        Check(day.Holds.Count == HoldTracker.KeyDetailLimit + 7, "all keys are kept in memory before encoding");

        // ---- 应用 × 键位 ----
        day = NewDay(DateTime.Today);
        long[] groupTotals = new long[6];
        for (int app = 0; app < HoldTracker.AppDetailLimit + 3; app++)
        {
            AppUsage usage = new AppUsage { ProcessPath = @"C:\app" + app + ".exe", Title = "a" };
            for (int g = 0; g < 6; g++)
            {
                long value = app + 1 + g;
                usage.KeyGroups[g] = value;
                groupTotals[g] += value;
            }
            usage.HasKeyGroups = true;
            day.Apps[usage.Id] = usage;
        }
        string[] lines = AppKeyCodec.EncodeAll(day);
        Check(lines.Length == HoldTracker.AppDetailLimit + 1, "top apps plus an aggregate line");
        DayRecord appRestored = NewDay(DateTime.Today);
        foreach (string line in lines) AppKeyCodec.Load(appRestored, line.Substring("app_keys_v1=".Length));
        long[] restoredTotals = new long[6];
        foreach (AppUsage app in appRestored.Apps.Values)
            for (int g = 0; g < 6; g++) restoredTotals[g] += app.KeyGroups[g];
        for (int g = 0; g < 6; g++)
            Check(restoredTotals[g] == groupTotals[g], "app key groups conserve group " + g);
        Check(appRestored.Apps.Count == HoldTracker.AppDetailLimit + 1, "aggregate app is stored once");

        // 没有该维度的旧记录不该产生任何行
        DayRecord legacy = NewDay(DateTime.Today);
        legacy.Apps["x"] = new AppUsage { ProcessPath = @"C:\old.exe", Title = "old" };
        Check(AppKeyCodec.EncodeAll(legacy).Length == 0, "records without key groups emit nothing");

        // ---- 月度归档 ----
        Dictionary<string, MonthArchive> archives = new Dictionary<string, MonthArchive>(StringComparer.Ordinal);
        DayRecord archival = NewDay(new DateTime(2025, 3, 12));
        archival.Keys = 1000; archival.Clicks = 100; archival.Wheel = 20;
        archival.ActiveSeconds = 3600; archival.MoveMeters = 500;
        archival.ComboCounts["ctrl_c"] = 7;
        archival.Sessions.Add(new ActiveSession { Start = archival.Date, End = archival.Date.AddHours(1), Seconds = 3600 });
        AppUsage usage2 = new AppUsage { ProcessPath = @"C:\game.exe", Title = "g", Keys = 400 };
        archival.Apps[usage2.Id] = usage2;
        MonthArchive.Fold(archives, archival);
        MonthArchive.Fold(archives, NewDay(new DateTime(2025, 3, 20)));
        Check(archives.Count == 1, "one archive per month");
        MonthArchive march = archives["2025-03"];
        Check(march.Days == 2 && march.Keys == 1001, "archive folds days together");
        Check(march.Combos == 7 && march.Sessions == 1, "archive keeps combos and sessions");
        Check(march.AppKeys[@"C:\game.exe"] == 400, "archive keeps per-app keys");

        Dictionary<string, MonthArchive> reloaded = new Dictionary<string, MonthArchive>(StringComparer.Ordinal);
        MonthArchive.AddTo(reloaded, MonthArchive.Encode(march));
        Check(reloaded.Count == 1, "archive round trip keeps its month");
        MonthArchive again = reloaded["2025-03"];
        Check(again.Days == march.Days && again.Keys == march.Keys, "archive scalars round trip");
        Near(again.ActiveSeconds, march.ActiveSeconds, 0.001, "archive seconds round trip");
        Check(again.AppKeys[@"C:\game.exe"] == 400, "archive app keys round trip");
        Check(MonthArchive.Encode(march).Split('|').Length >= 10, "archive encoding has a trailing app list");

        // ---- 保留期:超期先归档再移除,永久保留则不归档也不删 ----
        string directory = Path.Combine(Path.GetTempPath(), "KeyMousePressTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Store.DataDirectory = directory;
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            Store.Archives.Clear();
            Store.RollDay(DateTime.Today);
            Store.KeepDays = 30;
            Store.History[DateTime.Today.AddDays(-45)] = NewDay(DateTime.Today.AddDays(-45));
            Store.History[DateTime.Today.AddDays(-45)].Keys = 500;
            Store.History[DateTime.Today.AddDays(-5)] = NewDay(DateTime.Today.AddDays(-5));
            Store.Save();
            Check(!Store.History.ContainsKey(DateTime.Today.AddDays(-45)), "expired day is removed from daily records");
            Check(Store.History.ContainsKey(DateTime.Today.AddDays(-5)), "recent day is kept");
            Check(Store.Archives.Count == 1, "expired day was archived instead of dropped");
            foreach (MonthArchive archive in Store.Archives.Values)
                Check(archive.Keys == 500 && archive.Days == 1, "archive holds the expired day");

            Store.Archives.Clear();
            Store.History.Clear();
            Store.Total = new Counters();
            Store.RollDay(DateTime.Today);
            Store.KeepDays = 0;                       // 永久保留
            Store.History[DateTime.Today.AddDays(-800)] = NewDay(DateTime.Today.AddDays(-800));
            Store.Save();
            Check(Store.History.ContainsKey(DateTime.Today.AddDays(-800)), "unlimited retention keeps old days");
            Check(Store.Archives.Count == 0, "unlimited retention archives nothing");
            Store.KeepDays = 365;
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }

        // ---- 只选「今日」:完整日为空,但进行中的今天必须照旧计入 ----
        string todayDir = Path.Combine(Path.GetTempPath(), "KeyMouseTodayTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(todayDir);
        Store.DataDirectory = todayDir;
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            Store.RollDay(DateTime.Today);
            DayRecord running = Store.Today;
            running.Keys = 1000;
            running.HoldCount = 900; running.HoldTotalMs = 900 * 124; running.HoldMaxMs = 664; running.HoldDiscarded = 12;
            running.HoldBuckets[2] = 900;
            running.Holds[87] = new KeyHold { Count = 900, TotalMs = 900 * 124, MaxMs = 664 };
            AppUsage usage = new AppUsage { ProcessPath = @"C:\Apps\Code.exe", Keys = 1000 };
            usage.HasKeyGroups = true; usage.KeyGroups[0] = 600; usage.KeyGroups[1] = 400;
            running.Apps[usage.Id] = usage;

            HoldReportData held = HoldReportData.Build(DateTime.Today, DateTime.Today);
            Check(held.Samples == 900, "today-only range still counts the running day's hold samples");
            Check(held.Keys == 1000, "today-only range still counts the running day's keys");
            Check(held.TopKeys.Count == 1 && held.TopKeys[0].Key == 87, "today-only range still ranks keys");
            Check(held.Caption.Contains("今日"), "today-only caption names the running day");

            AppKeyReportData apps = AppKeyReportData.Build(DateTime.Today, DateTime.Today);
            Check(apps.AttributedKeys == 1000, "today-only range still attributes app keys");
            Check(apps.Groups.Count == 1 && apps.Groups[0][0] == 600, "today-only range keeps the group split");

            // 昨天到今天:今天只能算一次,不能被拼成两天。
            HoldReportData both = HoldReportData.Build(DateTime.Today.AddDays(-1), DateTime.Today);
            Check(both.Samples == 900, "a range ending today counts the running day exactly once");

            // 完整日区间以昨天结尾时,今天照旧接上(1.7.3 起的行为不能被这次修复改掉)。
            HoldReportData throughYesterday = HoldReportData.Build(DateTime.Today.AddDays(-6), DateTime.Today.AddDays(-1));
            Check(throughYesterday.Samples == 900, "a range ending yesterday still appends the running day");
        }
        finally
        {
            try { Directory.Delete(todayDir, true); } catch { }
        }

        HoldTracker.DiscardPending();
        Console.WriteLine("PASS: " + _checks + " press duration / app key / archive checks");
    }
}
