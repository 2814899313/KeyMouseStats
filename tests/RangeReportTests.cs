using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 区间回顾 / 分布 / 星期节律 三个报告页的回归:窗口口径、缺失日不补零、对比区间、
/// 分布样本守恒与图表渲染。只使用内存中的合成数据。
/// </summary>
internal static class RangeReportTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
    private static void Near(double actual, double expected, string name)
    {
        Check(Math.Abs(actual - expected) < 1e-9, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static DayRecord MakeDay(DateTime date, long keys)
    {
        DayRecord day = new DayRecord();
        day.Date = date;
        day.Keys = keys;
        day.Clicks = 100 + (int)(keys % 7);
        day.Wheel = 20;
        day.ActiveSeconds = 3600;
        day.HourKeys[9] = 100;
        day.HourKeys[15] = 40;
        day.Sessions.Add(new ActiveSession { Start = date.AddHours(9), End = date.AddHours(10), Seconds = 3600 });
        AppUsage app = new AppUsage { ProcessPath = @"C:\Apps\code.exe", Title = "code", Keys = keys, Clicks = 10, ActiveSeconds = 1200 };
        app.HasKeyGroups = true;
        app.KeyGroups[0] = keys / 2;
        app.KeyGroups[4] = keys / 2;
        day.Apps[app.Id] = app;
        // 按键时长:直接写入聚合值,不依赖真实钩子;分箱之和必须等于样本数。
        day.HoldCount = 100;
        day.HoldTotalMs = 100 * 120;
        day.HoldMaxMs = 800;
        day.HoldBuckets[2] = 60;
        day.HoldBuckets[3] = 40;
        KeyHold fast = new KeyHold();
        fast.Count = 40; fast.TotalMs = 40 * 90; fast.MaxMs = 200;
        day.Holds[87] = fast;
        KeyHold slow = new KeyHold();
        slow.Count = 60; slow.TotalMs = 60 * 140; slow.MaxMs = 800;
        day.Holds[65] = slow;
        return day;
    }

    private static int CountColors(Bitmap bitmap)
    {
        HashSet<int> seen = new HashSet<int>();
        for (int y = 0; y < bitmap.Height; y += 3)
            for (int x = 0; x < bitmap.Width; x += 3)
                seen.Add(bitmap.GetPixel(x, y).ToArgb());
        return seen.Count;
    }

    [STAThread]
    private static void Main()
    {
        Check(ReportDesign.Names.Length == 16, "sixteen report pages");
        Check(ReportDesign.Scope.Length == ReportDesign.Names.Length, "scope array matches page count");
        Check(ReportDesign.Names[11] == "区间回顾" && ReportDesign.Names[12] == "星期节律" && ReportDesign.Names[13] == "分布", "new page titles");
        Check(ReportDesign.Names[14] == "按键时长" && ReportDesign.Names[15] == "应用键位", "press pages are registered");

        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        Store.MouseDpi = 1600;

        long expectedCurrent = 0, expectedPrevious = 0, expectedAll = 0;
        int expectedObserved = 0;
        for (int k = 0; k < 56; k++)
        {
            DateTime date = DateTime.Today.AddDays(-1 - k);
            if (k == 3 || k == 9) continue;              // 当前窗口与上期各一个缺口
            long keys = 1000 + k * 10;
            Store.History[date] = MakeDay(date, keys);
            expectedAll += keys;
            expectedObserved++;
            if (k < 7) expectedCurrent += keys;
            else if (k < 14) expectedPrevious += keys;
        }
        Store.Today.Keys = 999999;                       // 今日不计入任何区间
        // 今天也带上 v11 维度的样本:累积类页面必须计入今天,对比类页面必须排除今天。
        Store.Today.HoldCount = 7;
        Store.Today.HoldTotalMs = 700;
        Store.Today.HoldMaxMs = 150;
        Store.Today.HoldBuckets[2] = 7;
        KeyHold todayHold = new KeyHold();
        todayHold.Count = 7; todayHold.TotalMs = 700; todayHold.MaxMs = 150;
        Store.Today.Holds[65] = todayHold;
        AppUsage todayApp = new AppUsage { ProcessPath = @"C:\Apps\code.exe", Title = "code", Keys = 40 };
        todayApp.HasKeyGroups = true;
        todayApp.KeyGroups[0] = 40;
        Store.Today.Apps[todayApp.Id] = todayApp;

        // ---- 区间回顾 ----
        DateTime rangeStart = DateTime.Today.AddDays(-7), rangeEnd = DateTime.Today;
        RangeReportData range = RangeReportData.Build(rangeStart, rangeEnd);
        Check(range.Current.End == DateTime.Today.AddDays(-1), "range ends at the last complete day");
        Check(range.Current.Start == DateTime.Today.AddDays(-7), "range starts seven days back");
        Check(range.Current.Days == 7 && range.Current.Observed == 6, "range reports seven days with six records");
        Check(range.Current.Keys == expectedCurrent, "current range keys");
        Check(range.Previous.Keys == expectedPrevious, "previous range keys");
        Check(range.Previous.End == range.Current.Start.AddDays(-1), "previous range ends just before the current one");
        Check(range.Previous.Days == 7, "previous range has the same length");
        Check(range.Daily.Count == 6, "daily series skips the missing day");
        Check(range.CardValues[0] == Analysis.FmtCount(expectedCurrent), "card shows the range total");
        Check(range.CardValues[3] == "6 / 7 天", "card reports effective days");
        Near(range.Current.KeysPerDay, expectedCurrent / 6.0, "daily average uses observed days");
        Check(range.Rows[0][0] == "击键（合计）" && range.Rows[1][0] == "日均击键", "row labels");
        Check(range.Rows[1][1] == ((long)Math.Round(expectedCurrent / 6.0)).ToString(), "daily average row value");
        Check(range.Rows[0][4].Contains("6 / 7"), "coverage column");
        Check(range.Footer.Contains("缺失日不补零"), "footer states the missing-day rule");
        Check(range.HasPrevious && range.PreviousDailyMean > 0, "reference line has a value");

        // ---- 分布 ----
        DistributionReportData distribution = DistributionReportData.Build(rangeStart, rangeEnd);
        Check(distribution.Series.Count == 3, "three distribution series");
        Check(distribution.SeriesNames[0] == "逐日击键" && distribution.SeriesNames[1] == "连续段时长", "series labels");
        Check(distribution.DailyKeys.Count == 6, "distribution samples skip missing days");
        Check(distribution.Rows.Count == 3, "one detail row per series");
        Check(distribution.Rows[0][1] == "6", "daily sample count in the detail row");
        Check(distribution.CardValues[1] == "6 / 7 天", "distribution card reports coverage");
        Check(distribution.CardValues[2] == Analysis.FmtCount((long)Math.Round(distribution.DailyKeys.P50)), "P50 card");
        int binTotal = 0;
        foreach (int bin in distribution.DailyKeys.Bins) binTotal += bin;
        Check(binTotal == distribution.DailyKeys.Count, "histogram bins conserve every sample");
        Check(distribution.Series[1].Count == 6, "one session sample per recorded day");

        // ---- 星期节律 ----
        RhythmReportData rhythm = RhythmReportData.Build(DateTime.Today);
        int sampleTotal = 0;
        long keysTotal = 0;
        for (int i = 0; i < 7; i++) { sampleTotal += rhythm.SampleDays[i]; keysTotal += rhythm.Keys[i]; }
        Check(sampleTotal == expectedObserved, "weekday samples cover every recorded day");
        Check(keysTotal == expectedAll, "weekday totals conserve all keys");
        Check(rhythm.CardValues[1] == expectedObserved + " / 56 天", "rhythm card reports coverage");
        for (int i = 0; i < 7; i++)
        {
            if (rhythm.SampleDays[i] == 0) { Check(rhythm.Rows[i][2] == "--", "empty weekday shows no average"); continue; }
            Check(rhythm.Rows[i][5].StartsWith("09:00"), "peak hour is the busiest recorded hour");
        }

        // ---- 对比区间:可指定为任意区间 ----
        RangeReportData compared = RangeReportData.Build(rangeStart, rangeEnd, DateTime.Today.AddDays(-30), DateTime.Today.AddDays(-24), "自定义");
        Check(compared.ComparisonLabel == "自定义", "comparison label is kept");
        Check(compared.Previous.Start == DateTime.Today.AddDays(-30), "custom comparison start");
        Check(compared.Previous.End == DateTime.Today.AddDays(-24), "custom comparison end");
        Check(compared.Previous.Observed == 7, "custom comparison window counts its own days");
        Check(compared.Footer.Contains("自定义"), "footer names the comparison window");

        // ---- 拖拽刷选 ----
        using (RangeReportVisual brush = new RangeReportVisual(rangeStart, rangeEnd))
        {
            brush.Size = new Size(1000, 300);
            DateTime selectedStart = DateTime.MinValue, selectedEnd = DateTime.MinValue;
            int raised = 0;
            brush.RangeSelected += delegate(DateTime start, DateTime end) { selectedStart = start; selectedEnd = end; raised++; };
            using (Bitmap bitmap = new Bitmap(1000, 300))
            using (Graphics graphics = Graphics.FromImage(bitmap)) brush.RenderTo(graphics, 1f);
            Check(!brush.SelectByX(100, 102), "a too-narrow drag is ignored");
            float chartLeft = 56, chartRight = 980, step = (chartRight - chartLeft) / 7f;
            Check(brush.SelectByX(chartLeft + 2 * step + 10, chartLeft + 4 * step + 10), "a drag reports a selection");
            Check(raised == 1, "a drag raises the event once");
            Check(selectedStart == range.Current.Start.AddDays(2), "selection maps to the third day");
            Check(selectedEnd == range.Current.Start.AddDays(4), "selection maps to the fifth day");
        }

        // ---- 按键时长 ----
        HoldReportData hold = HoldReportData.Build(rangeStart, rangeEnd);
        Check(hold.Samples == 600 + 7, "hold samples aggregate over the range and include today");
        Check(hold.Caption.Contains("今日（进行中）"), "hold page states that today is appended");
        Check(hold.Keys == expectedCurrent + 999999, "coverage denominator includes today");
        long bucketTotal = 0;
        foreach (long value in hold.Buckets) bucketTotal += value;
        Check(bucketTotal == hold.Samples, "hold buckets conserve the sample count");
        Check(hold.TopKeys.Count >= 1, "keys are ranked by mean hold time");
        Check(hold.CardValues[3].EndsWith("%"), "coverage card is a percentage");
        Check(hold.Rows.Count >= 1 && hold.Rows[0][0] != "--", "per-key hold rows exist");
        Check(Math.Abs(hold.TotalMs - (600 * 120 + 700)) < 1, "hold total duration aggregates");

        // ---- 应用 × 键位 ----
        AppKeyReportData appKey = AppKeyReportData.Build(rangeStart, rangeEnd);
        Check(appKey.AttributedKeys == expectedCurrent + 40, "app key groups aggregate by process and include today");
        Check(appKey.Caption.Contains("今日（进行中）"), "app key page states that today is appended");
        Check(appKey.Groups.Count == 1, "one recorded app");
        Check(appKey.Groups[0][0] + appKey.Groups[0][4] == appKey.AttributedKeys, "group counts conserve the attributed keys");
        Check(appKey.Headings.Length == 7, "app key table has six group columns");
        Check(appKey.Rows.Count >= 1 && appKey.Rows[0][0].Contains("code.exe"), "app row names the process");

        // ---- 渲染 ----
        Directory.CreateDirectory("previews");
        using (RangeReportVisual visual = new RangeReportVisual(rangeStart, rangeEnd))
        {
            visual.Size = new Size(1000, 300);
            RenderAndCheck(visual, "range-review", 1f);
            RenderAndCheck(visual, "range-review-150", 1.5f);
        }
        using (DistributionReportVisual visual = new DistributionReportVisual(rangeStart, rangeEnd))
        {
            visual.Size = new Size(1000, 430);
            RenderAndCheck(visual, "distribution", 1f);
            RenderAndCheck(visual, "distribution-150", 1.5f);
        }
        using (RhythmReportVisual rhythmVisual = new RhythmReportVisual(DateTime.Today))
        {
            rhythmVisual.Size = new Size(1000, 300);
            RenderAndCheck(rhythmVisual, "weekday-rhythm", 1f);
        }
        using (HoldReportVisual holdVisual = new HoldReportVisual(rangeStart, rangeEnd))
        {
            holdVisual.Size = new Size(1000, 330);
            RenderAndCheck(holdVisual, "press-duration", 1f);
            RenderAndCheck(holdVisual, "press-duration-150", 1.5f);
        }
        using (AppKeyReportVisual appKeyVisual = new AppKeyReportVisual(rangeStart, rangeEnd))
        {
            appKeyVisual.Size = new Size(1000, 432);
            RenderAndCheck(appKeyVisual, "app-key-groups", 1f);
            RenderAndCheck(appKeyVisual, "app-key-groups-150", 1.5f);
        }

        // 空数据也必须能渲染,不能除零或抛异常。
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        RangeReportData emptyRange = RangeReportData.Build(DateTime.Today.AddDays(-30), DateTime.Today);
        Check(emptyRange.Current.Observed == 0 && emptyRange.CardValues[0] == "无记录", "empty range reports no records");
        DistributionReportData emptyDistribution = DistributionReportData.Build(DateTime.Today.AddDays(-30), DateTime.Today);
        Check(emptyDistribution.DailyKeys.Count == 0 && emptyDistribution.Series[0].Count == 0, "empty distribution has no samples");
        using (RangeReportVisual visual = new RangeReportVisual(DateTime.Today.AddDays(-30), DateTime.Today))
        {
            visual.Size = new Size(1000, 300);
            RenderAndCheck(visual, "range-review-empty", 1f);
        }
        using (DistributionReportVisual visual = new DistributionReportVisual(DateTime.Today.AddDays(-30), DateTime.Today))
        {
            visual.Size = new Size(1000, 430);
            RenderAndCheck(visual, "distribution-empty", 1f);
        }
        using (RhythmReportVisual rhythmVisual = new RhythmReportVisual(DateTime.Today))
        {
            rhythmVisual.Size = new Size(1000, 300);
            RenderAndCheck(rhythmVisual, "weekday-rhythm-empty", 1f);
        }

        Console.WriteLine("PASS: " + _checks + " range review / distribution / rhythm checks");
    }

    private static void RenderAndCheck(Control visual, string name, float scale)
    {
        int width = Math.Max(1, (int)(visual.Width * scale));
        int height = Math.Max(1, (int)(visual.Height * scale));
        using (Bitmap bitmap = new Bitmap(width, height))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                RangeReportVisual range = visual as RangeReportVisual;
                DistributionReportVisual distribution = visual as DistributionReportVisual;
                RhythmReportVisual rhythm = visual as RhythmReportVisual;
                HoldReportVisual hold = visual as HoldReportVisual;
                AppKeyReportVisual appKey = visual as AppKeyReportVisual;
                if (range != null) range.RenderTo(graphics, scale);
                else if (distribution != null) distribution.RenderTo(graphics, scale);
                else if (hold != null) hold.RenderTo(graphics, scale);
                else if (appKey != null) appKey.RenderTo(graphics, scale);
                else rhythm.RenderTo(graphics, scale);
            }
            bitmap.Save(Path.Combine("previews", name + ".png"), ImageFormat.Png);
            Check(CountColors(bitmap) > 3, name + " renders more than a flat background");
        }
    }
}
