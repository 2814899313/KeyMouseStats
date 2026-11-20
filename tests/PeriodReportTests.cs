using System;
using System.Collections.Generic;
using KeyMouseStats;

/// <summary>周报/月报聚合:边界、合计、活跃天数、自然周。</summary>
internal static class PeriodReportTests
{
    [STAThread]
    static void Main()
    {
        DateTime today = DateTime.Today;
        Store.History.Clear();
        for (int ago = 120; ago >= 1; ago--)
        {
            DateTime date = today.AddDays(-ago);
            DayRecord day = new DayRecord();
            day.Date = date;
            day.Keys = 5000 + (ago * 733 % 9000);
            day.Clicks = 800 + (ago * 97 % 1200);
            day.ActiveSeconds = (4.0 + (ago % 5)) * 3600;
            day.PeakApm = 100 + (ago % 120);
            for (int h = 9; h < 19; h++) day.HourKeys[h] = 400 + (h * 31 + ago * 17) % 500;
            day.Sessions.Add(new ActiveSession { Start = date.AddHours(9), End = date.AddHours(11), Seconds = 7200 });
            AppUsage app = new AppUsage();
            app.ProcessPath = @"C:\Apps\Code.exe";
            app.Title = "Code";
            app.Keys = day.Keys / 2;
            app.ActiveSeconds = day.ActiveSeconds / 2;
            day.Apps[app.Id] = app;
            Store.History[date] = day;
        }

        PeriodReport week = PeriodReport.BuildWeek(today.AddDays(-1));
        Check(week.Days.Count == 7, "week has 7 day slots");
        Check(week.Start == today.AddDays(-7) && week.End == today.AddDays(-1), "week range");
        Check(week.ActiveDays == 7, "week active days");
        Check(week.Keys > 0 && week.Clicks > 0 && week.ActiveSeconds > 0, "week totals");
        Check(week.Apps.Count == 1 && week.Apps[0].Name.Length > 0, "week apps");
        Check(week.Headline.Length > 0 && week.Lede.Length > 0, "week copy");
        Check(week.PrevKeys > 0, "week previous totals");

        DateTime previousMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        PeriodReport month = PeriodReport.BuildMonth(previousMonth);
        int monthDays = DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month);
        Check(month.Days.Count == monthDays, "month day slots " + month.Days.Count);
        Check(month.DayCount == monthDays, "month day count");
        Check(month.ActiveDays == monthDays, "month active days");
        Check(month.Weeks.Count >= 4 && month.Weeks.Count <= 6, "month weeks " + month.Weeks.Count);
        Check(month.LongestStreak == monthDays, "month streak");
        Check(month.LongestStreakStart == month.Start, "month streak start");
        Check(month.BestDay >= month.Start && month.BestDay < month.Start.AddDays(monthDays), "month best day");
        Check(month.PrevKeys > 0, "month previous totals");

        Console.WriteLine("PeriodReportTests: week and month aggregates passed.");
    }

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
