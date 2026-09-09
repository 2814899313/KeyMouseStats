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
        AppUsage app = new AppUsage { ProcessPath = @"C:\Apps\code.exe", Title = "code", Keys = keys / 2, Clicks = 10, ActiveSeconds = 1200 };
        day.Apps[app.Id] = app;
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
        Check(ReportDesign.Names.Length == 14, "fourteen report pages");
        Check(ReportDesign.Scope.Length == ReportDesign.Names.Length, "scope array matches page count");
        Check(ReportDesign.Names[11] == "区间回顾" && ReportDesign.Names[12] == "星期节律" && ReportDesign.Names[13] == "分布", "new page titles");

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
                if (range != null) range.RenderTo(graphics, scale);
                else if (distribution != null) distribution.RenderTo(graphics, scale);
                else rhythm.RenderTo(graphics, scale);
            }
            bitmap.Save(Path.Combine("previews", name + ".png"), ImageFormat.Png);
            Check(CountColors(bitmap) > 3, name + " renders more than a flat background");
        }
    }
}
