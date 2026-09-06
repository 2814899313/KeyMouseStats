using System;
using KeyMouseStats;

internal static class HourlyCompareTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    [STAThread]
    private static void Main()
    {
        DateTime selected = new DateTime(2026, 9, 5);
        Check(Dashboard.HourlyComparisonDate(selected, 1) == new DateTime(2026, 9, 4), "yesterday comparison");
        Check(Dashboard.HourlyComparisonDate(selected, 7) == new DateTime(2026, 8, 29), "same weekday last week");
        Check(Dashboard.HourlyComparisonDate(new DateTime(2026, 1, 1), 7) == new DateTime(2025, 12, 25), "comparison crosses year");
        Check(Dashboard.HourlyComparisonDate(selected, 14) == new DateTime(2026, 8, 22), "custom comparison window");

        Store.History.Clear();
        DayRecord averageA = new DayRecord { Date = selected.AddDays(-1) };
        DayRecord averageB = new DayRecord { Date = selected.AddDays(-3) };
        averageA.HourKeys[8] = 100; averageB.HourKeys[8] = 300;
        Store.History[averageA.Date] = averageA; Store.History[averageB.Date] = averageB;
        double[] average = Dashboard.AverageHourlyValues(selected, 3, 0, selected);
        Check(average[8] == 200, "custom average ignores missing dates instead of filling zero");

        DayRecord day = new DayRecord { Date = selected, ActiveSeconds = 120 };
        day.HourKeys[8] = 123;
        day.HourClicks[8] = 30;
        day.ActiveHours[8] = 120;
        double[] keys = Dashboard.HourlyValues(day, selected, 0, selected.AddHours(9));
        double[] active = Dashboard.HourlyValues(day, selected, 2, selected.AddHours(9));
        Check(keys[8] == 123, "key bucket retained");
        Check(active[8] == 2, "active seconds converted to minutes");
        Check(double.IsNaN(keys[10]), "future hour remains missing");
        Check(double.IsNaN(Dashboard.HourlyValues(null, selected.AddDays(-1), 0, selected)[8]), "missing comparison does not become zero");
        Console.WriteLine("PASS: " + checks + " hourly comparison checks");
    }
}
