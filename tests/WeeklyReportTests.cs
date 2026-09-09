using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 「本周回顾」与「星期节律」回归:窗口口径、缺失日不补零、变化计算、按星期聚合与图表渲染。
/// 只使用内存中的合成数据,不读写用户的统计数据。
/// </summary>
internal static class WeeklyReportTests
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
        // 报告页名称与简短口径必须一一对应,避免以后加页时漏改其中一个。
        Check(ReportDesign.Names.Length == 13, "thirteen report pages");
        Check(ReportDesign.Scope.Length == ReportDesign.Names.Length, "scope array matches page count");
        Check(ReportDesign.Names[11] == "本周回顾" && ReportDesign.Names[12] == "星期节律", "new page titles");

        Check(WeekAnalysis.CompleteEnd(DateTime.Today) == DateTime.Today.AddDays(-1), "today is excluded from complete-day windows");
        Check(WeekAnalysis.CompleteEnd(DateTime.Today.AddDays(-30)) == DateTime.Today.AddDays(-30), "past dates are already complete");
        Check(WeekAnalysis.WeekdayIndex(new DateTime(2026, 9, 7)) == 0, "monday is index zero");

        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        Store.MouseDpi = 1600;

        long expectedCurrentKeys = 0, expectedPreviousKeys = 0, expectedAllKeys = 0;
        int expectedObserved = 0;
        for (int k = 0; k < 56; k++)
        {
            DateTime date = DateTime.Today.AddDays(-1 - k);
            if (k == 3 || k == 9) continue;   // 两个缺口:当前窗口与上期窗口各一个
            long keys = 1000 + k * 10;
            Store.History[date] = MakeDay(date, keys);
            expectedAllKeys += keys;
            expectedObserved++;
            if (k < 7) expectedCurrentKeys += keys;
            else if (k < 14) expectedPreviousKeys += keys;
        }
        // 今日未结束,不能进入任何完整日窗口。
        Store.Today.Keys = 999999;
        Store.Today.HourKeys[3] = 999999;

        WeeklyReportData week = WeeklyReportData.Build(DateTime.Today);
        Check(week.End == DateTime.Today.AddDays(-1), "weekly window ends at the last complete day");
        Check(week.PreviousEnd == DateTime.Today.AddDays(-8), "previous window ends seven days earlier");
        Check(week.Current.Count == 7 && week.Previous.Count == 7, "both windows span seven days");
        Check(week.CurrentTotals.Observed == 6, "current window counts six recorded days");
        Check(week.PreviousTotals.Observed == 6, "previous window counts six recorded days");
        Check(week.CurrentTotals.Keys == expectedCurrentKeys, "current window keys");
        Check(week.PreviousTotals.Keys == expectedPreviousKeys, "previous window keys");
        Near(week.CurrentTotals.KeysPerDay, expectedCurrentKeys / 6.0, "missing days are not counted as zero in the daily average");
        Check(week.Rows[0][1] == Analysis.FmtCount(expectedCurrentKeys), "row shows the current window total");
        Check(week.Rows[0][3] == PersonalStats.Change(expectedCurrentKeys, expectedPreviousKeys), "change compares the two windows");
        Check(week.Rows[0][4].Contains("6/7"), "row reports coverage for both windows");
        Check(week.CardValues[0] == Analysis.FmtCount(expectedCurrentKeys), "card shows the current window total");
        Check(week.CardValues[3] == "6 / 7 天", "card reports effective days");
        Check(week.Footer.Contains("缺失日不补零"), "footer states the missing-day rule");

        RhythmReportData rhythm = RhythmReportData.Build(DateTime.Today);
        int sampleTotal = 0;
        long keysTotal = 0;
        for (int i = 0; i < 7; i++) { sampleTotal += rhythm.SampleDays[i]; keysTotal += rhythm.Keys[i]; }
        Check(sampleTotal == expectedObserved, "weekday samples cover every recorded day");
        Check(keysTotal == expectedAllKeys, "weekday totals conserve all keys");
        Check(rhythm.End == DateTime.Today.AddDays(-1), "rhythm window also ends at the last complete day");
        for (int i = 0; i < 7; i++)
        {
            if (rhythm.SampleDays[i] == 0) { Check(rhythm.Rows[i][2] == "--", "empty weekday shows no average"); continue; }
            Check(rhythm.Rows[i][1] == rhythm.SampleDays[i] + " 天", "weekday sample count is shown");
            Check(rhythm.Rows[i][5].StartsWith("09:00"), "peak hour is the busiest recorded hour");
        }
        Check(rhythm.CardValues[1] == expectedObserved + " / 56 天", "rhythm card reports coverage");

        // 渲染检查:两个新图表在 100% 与 150% 下都要能画出内容,并能作为 PNG 导出。
        Directory.CreateDirectory("previews");
        using (WeeklyReportVisual visual = new WeeklyReportVisual(DateTime.Today))
        {
            visual.Size = new Size(1000, 300);
            RenderAndCheck(visual, "weekly-review", 1f);
            RenderAndCheck(visual, "weekly-review-150", 1.5f);
        }
        using (RhythmReportVisual rhythmVisual = new RhythmReportVisual(DateTime.Today))
        {
            rhythmVisual.Size = new Size(1000, 300);
            RenderAndCheck(rhythmVisual, "weekday-rhythm", 1f);
            RenderAndCheck(rhythmVisual, "weekday-rhythm-150", 1.5f);
        }

        // 空数据也要能渲染,不能除零或抛异常。
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        using (WeeklyReportVisual empty = new WeeklyReportVisual(DateTime.Today))
        {
            empty.Size = new Size(1000, 300);
            RenderAndCheck(empty, "weekly-review-empty", 1f);
        }
        using (RhythmReportVisual emptyRhythm = new RhythmReportVisual(DateTime.Today))
        {
            emptyRhythm.Size = new Size(1000, 300);
            RenderAndCheck(emptyRhythm, "weekday-rhythm-empty", 1f);
        }

        Console.WriteLine("PASS: " + _checks + " weekly review / weekday rhythm checks");
    }

    private static void RenderAndCheck(Control visual, string name, float scale)
    {
        int width = Math.Max(1, (int)(visual.Width * scale));
        int height = Math.Max(1, (int)(visual.Height * scale));
        using (Bitmap bitmap = new Bitmap(width, height))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                if (visual is WeeklyReportVisual) ((WeeklyReportVisual)visual).RenderTo(graphics, scale);
                else ((RhythmReportVisual)visual).RenderTo(graphics, scale);
            }
            bitmap.Save(Path.Combine("previews", name + ".png"), ImageFormat.Png);
            Check(CountColors(bitmap) > 3, name + " renders more than a flat background");
        }
    }
}
