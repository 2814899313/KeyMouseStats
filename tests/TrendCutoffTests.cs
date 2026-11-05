using System;
using System.Globalization;
using KeyMouseStats;

/// <summary>
/// 未完成时间窗口:今日环比只和昨日同一时段比;图表末端标注「今日截至 HH:mm」。
/// 这里是纯函数,不依赖真实时钟,用固定 now 覆盖跨天/整点边界。
/// </summary>
internal static class TrendCutoffTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static DayRecord Day(DateTime date, long[] hours, long fullDayTail)
    {
        DayRecord day = new DayRecord();
        day.Date = date;
        for (int h = 0; h < 24; h++)
        {
            day.HourKeys[h] = h < hours.Length ? hours[h] : 0;
            day.HourClicks[h] = (h < hours.Length ? hours[h] : 0) / 2;
        }
        if (fullDayTail > 0)
        {
            // 后半天的数据仍然存在于全天总量里,但同期环比不应该看到它们。
            for (int h = hours.Length; h < 24; h++) day.HourKeys[h] += fullDayTail;
            day.Keys = 0;
            for (int h = 0; h < 24; h++) day.Keys += day.HourKeys[h];
        }
        return day;
    }

    private static void Run()
    {
        DateTime today = new DateTime(2026, 11, 5);
        DateTime now = today.AddHours(9).AddMinutes(57);   // 09:57 → 已完成 0–8 点

        long[] current = { 100, 100, 100, 100, 100, 100, 100, 100, 100, 900, 900, 900 };   // 今天上午 + 之后
        long[] previous = { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 5000, 5000 }; // 昨天有完整一天
        DayRecord currentDay = Day(today, current, 1);
        DayRecord previousDay = Day(today.AddDays(-1), previous, 1);

        TrendCutoff.Comparison compare = TrendCutoff.Compare(currentDay, previousDay, 0, now);
        Check(compare.Available, "same-period comparison is available after one completed hour");
        Check(compare.Hours == 9 && compare.Cutoff == today.AddHours(9), "09:57 compares the nine completed hours");
        Check(Math.Abs(compare.Today - 900) < 1e-9, "today only sums completed hours");
        Check(Math.Abs(compare.Yesterday - 900) < 1e-9, "yesterday only sums the same nine hours");
        Check(compare.Percent.HasValue && Math.Abs(compare.Percent.Value) < 1e-9, "identical same-period values compare to 0%");
        Check(TrendCutoff.DeltaText(compare) == "+0%", "zero change is written with an explicit sign");
        Check(TrendCutoff.NoteText(compare).StartsWith("截至 09:00") && TrendCutoff.NoteText(compare).Contains("昨日同期"),
            "the note shows the comparison window");

        // 拿半天去比整天会得到 -95% 左右;同期口径不应该出现这种断崖。
        long fullToday = 0, fullYesterday = 0;
        for (int h = 0; h < 24; h++) { fullToday += currentDay.HourKeys[h]; fullYesterday += previousDay.HourKeys[h]; }
        double naive = 100.0 * (fullToday - fullYesterday) / fullYesterday;
        Check(naive < -50, "the naive whole-day comparison is the cliff this fix removes");
        Check(compare.Percent.Value > -1, "the same-period comparison is not distorted by the unfinished day");

        // 点击指标走 HourClicks。
        TrendCutoff.Comparison clicks = TrendCutoff.Compare(currentDay, previousDay, 1, now);
        Check(clicks.Available && Math.Abs(clicks.Today - 450) < 1e-9 && Math.Abs(clicks.Yesterday - 450) < 1e-9,
            "clicks use the hourly click buckets");

        // 刚过零点:没有完整小时,不硬算环比。
        TrendCutoff.Comparison midnight = TrendCutoff.Compare(currentDay, previousDay, 0, today.AddMinutes(5));
        Check(!midnight.Available && midnight.Hours == 0, "just after midnight there is nothing to compare");
        Check(TrendCutoff.DeltaText(midnight) == "累计中", "midnight shows a neutral accumulating label");

        // 23:xx:完成 0–22 点,不包括仍在累计的 23 点。
        DayRecord morningOnly = Day(today, new long[] { 100, 100, 100, 100, 100, 100, 100, 100, 100 }, 0);
        TrendCutoff.Comparison late = TrendCutoff.Compare(morningOnly, previousDay, 0, today.AddHours(23).AddMinutes(30));
        Check(late.Hours == 23 && Math.Abs(late.Today - 900) < 1e-9, "the current hour is excluded near midnight too");

        // 滚轮 / 鼠标路程没有逐小时数据,不能做同期环比。
        Check(TrendCutoff.DeltaText(TrendCutoff.Compare(currentDay, previousDay, 2, now)) == "累计中",
            "wheel has no hourly buckets, so the comparison is unavailable");
        Check(TrendCutoff.DeltaText(TrendCutoff.Compare(currentDay, previousDay, 3, now)) == "累计中",
            "mouse distance has no hourly buckets either");

        // 昨天没记录 / 昨天同期为 0。
        TrendCutoff.Comparison noYesterday = TrendCutoff.Compare(currentDay, null, 0, now);
        Check(!noYesterday.Available && TrendCutoff.DeltaText(noYesterday) == "新增", "a missing yesterday is reported as new");
        DayRecord zeroYesterday = Day(today.AddDays(-1), new long[0], 0);
        Check(TrendCutoff.DeltaText(TrendCutoff.Compare(currentDay, zeroYesterday, 0, now)) == "新增",
            "zero data in yesterday's same period is reported as new");

        // 正常涨跌的格式化。
        long[] rising = { 110, 110, 110, 110, 110, 110, 110, 110, 110 };
        TrendCutoff.Comparison up = TrendCutoff.Compare(Day(today, rising, 0), Day(today.AddDays(-1), new long[] { 100, 100, 100, 100, 100, 100, 100, 100, 100 }, 0), 0, now);
        Check(TrendCutoff.DeltaText(up) == "+10%", "positive change is rounded and signed");
        Check(TrendCutoff.LiveTag(now).Contains("今日截至 09:57") && TrendCutoff.LiveTag(now).Contains("累计中"),
            "the chart tag names the live cutoff");

        Console.WriteLine("Trend cutoff: " + checks + " checks passed");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("TrendCutoffTests", Run);
    }
}
