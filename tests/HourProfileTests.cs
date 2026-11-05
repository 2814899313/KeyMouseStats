using System;
using System.Collections.Generic;
using KeyMouseStats;

/// <summary>
/// 周 × 24 小时矩阵:按星期几分桶、按有记录日求平均,并排除今天(未完成)与无记录日。
/// </summary>
internal static class HourProfileTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Near(double actual, double expected, string what)
    {
        Check(Math.Abs(actual - expected) < 1e-9, what + " (got " + actual + ", want " + expected + ")");
    }

    private static DayRecord Day(DateTime date, long nine, long ten, double activeNineMinutes)
    {
        DayRecord day = new DayRecord();
        day.Date = date;
        day.HourKeys[9] = nine;
        day.HourKeys[10] = ten;
        day.HourClicks[9] = nine / 2;
        day.HourClicks[10] = ten / 2;
        day.ActiveHours[9] = activeNineMinutes * 60;
        day.ActiveSeconds = activeNineMinutes * 60;
        return day;
    }

    private static void Run()
    {
        // 2026-11-05 是周四。造两个周三、一个周四、一个周六,再加今天与缺失日。
        DateTime today = new DateTime(2026, 11, 5);
        DateTime firstWednesday = new DateTime(2026, 10, 28);
        DateTime secondWednesday = firstWednesday.AddDays(7);   // 11-04
        DayRecord wed1 = Day(firstWednesday, 100, 20, 30);
        DayRecord wed2 = Day(secondWednesday, 200, 40, 50);
        DayRecord thursday = Day(new DateTime(2026, 10, 29), 50, 10, 15);
        DayRecord saturday = Day(new DateTime(2026, 10, 31), 10, 5, 5);
        DayRecord todayRecord = Day(today, 9999, 9999, 60);
        DayRecord outside = Day(today.AddDays(-91), 7777, 7777, 60);
        List<DayRecord> history = new List<DayRecord> { wed1, wed2, thursday, saturday, todayRecord, outside };

        HourProfile.Result result = HourProfile.Build(history, 0, today, 90);
        Check(result.TotalDays == 4, "today, out-of-range days and missing days do not join the matrix");
        Check(result.Days[2] == 2, "both Wednesdays are bucketed together");
        Check(result.Days[3] == 1, "Thursday has one recorded day");
        Check(result.Days[5] == 1 && result.Days[6] == 0, "Saturday is row 5, Sunday stays empty");
        Near(result.Average[2, 9], 150, "Wednesday 09:00 averages 100 and 200 keys");
        Near(result.Average[2, 10], 30, "Wednesday 10:00 averages the second bucket");
        Near(result.Average[3, 9], 50, "Thursday keeps its own row");
        Near(result.Average[4, 9], 0, "Friday row stays empty");
        Near(result.Average[5, 9], 10, "Saturday sits on row 5");
        Near(result.Max, 150, "max is the largest weekday/hour average");

        // 点击指标独立分桶。
        HourProfile.Result clicks = HourProfile.Build(history, 1, today, 90);
        Near(clicks.Average[2, 9], 75, "clicks average 50 and 100");
        Near(clicks.Average[2, 10], 15, "Wednesday click bucket 10:00");

        // 活跃时长按分钟平均。
        HourProfile.Result active = HourProfile.Build(history, 2, today, 90);
        Near(active.Average[2, 9], 40, "active minutes average 30 and 50");

        // 没有记录 / 全部都过期。
        HourProfile.Result empty = HourProfile.Build(new DayRecord[0], 0, today, 90);
        Check(!empty.HasData && empty.TotalDays == 0 && empty.Max == 0, "empty history yields an empty matrix");
        HourProfile.Result stale = HourProfile.Build(new DayRecord[] { outside }, 0, today, 90);
        Check(!stale.HasData, "days older than the window are ignored");
        Check(HourProfile.Build(null, 0, today, 90).TotalDays == 0, "null history is safe");

        // 缺记录的日期不参与分母:如果只有一个周三有记录,平均就只按一天算。
        List<DayRecord> oneWeek = new List<DayRecord> { wed1 };
        Near(HourProfile.Build(oneWeek, 0, today, 90).Average[2, 9], 100, "the denominator is recorded days, not calendar days");

        // 时间跨度:今日 / 近 7 天 / 全部历史,矩阵与柱子共用同一批日期。
        HourProfile.Result todayOnly = HourProfile.BuildRange(history, 0, today, today);
        Check(todayOnly.TotalDays == 1 && todayOnly.Days[3] == 1, "the today range only contains today");
        Near(todayOnly.Average[3, 9], 9999, "today's own values are used without averaging");

        HourProfile.Result lastWeek = HourProfile.BuildRange(history, 0, today.AddDays(-7), today.AddDays(-1));
        Check(lastWeek.TotalDays == 3, "the last-seven-days range keeps 10-29, 10-31 and 11-04 only");
        Check(lastWeek.Days[2] == 1 && lastWeek.Days[3] == 1 && lastWeek.Days[5] == 1, "weekday buckets inside the range");
        Near(lastWeek.Average[2, 9], 200, "11-04 is the only Wednesday inside the last seven days");

        HourProfile.Result allTime = HourProfile.BuildRange(history, 0, DateTime.Today.AddYears(-100), today.AddDays(-1));
        Check(allTime.TotalDays == 5, "the all-history range includes every recorded day except today, including older ones outside the 90-day window");

        Console.WriteLine("Hour profile matrix: " + checks + " checks passed");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("HourProfileTests", Run);
    }
}
