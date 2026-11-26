using System.Globalization;

namespace Kms.Core.Model;

/// <summary>按月归档的汇总额。对应 v3 <c>MonthArchive</c>。</summary>
public sealed class MonthArchive
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Days { get; set; }
    public int Sessions { get; set; }
    public long Keys { get; set; }
    public long Clicks { get; set; }
    public long Wheel { get; set; }
    public long HWheel { get; set; }
    public long Combos { get; set; }
    public double ActiveSeconds { get; set; }
    public double MoveMeters { get; set; }
    public long HoldCount { get; set; }
    public long HoldDiscarded { get; set; }
    public double HoldTotalMs { get; set; }
    public double HoldMaxMs { get; set; }
    public double HoldActiveSeconds { get; set; }
    public Dictionary<string, long> AppKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Key =>
        Year.ToString("0000", CultureInfo.InvariantCulture) + "-" +
        Month.ToString("00", CultureInfo.InvariantCulture);

    public string Label => $"{Year} 年 {Month} 月";

    /// <summary>把一天折进月归档；只保留粗略合计，不参与日级统计。</summary>
    public static void Fold(Dictionary<string, MonthArchive> archives, DayRecord day)
    {
        if (day.IsEmpty)
        {
            return;
        }

        string key = day.Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        if (!archives.TryGetValue(key, out MonthArchive? archive))
        {
            archive = new MonthArchive { Year = day.Date.Year, Month = day.Date.Month };
            archives[key] = archive;
        }

        archive.Days++;
        archive.Keys += day.Keys;
        archive.Clicks += day.Clicks;
        archive.Wheel += day.Wheel;
        archive.HWheel += day.HWheel;
        archive.Combos += Kms.Core.Stats.PersonalStats.Combos(day);
        archive.ActiveSeconds += day.ActiveSeconds;
        archive.MoveMeters += day.MoveMeters;
        archive.Sessions += day.Sessions.Count;
        archive.HoldCount += day.HoldCount;
        archive.HoldDiscarded += day.HoldDiscarded;
        archive.HoldTotalMs += day.HoldTotalMs;
        archive.HoldActiveSeconds += day.HoldActiveSeconds;
        if (day.HoldMaxMs > archive.HoldMaxMs)
        {
            archive.HoldMaxMs = day.HoldMaxMs;
        }

        foreach (AppUsage app in day.Apps.Values)
        {
            string path = app.ProcessPath ?? "";
            archive.AppKeys[path] = archive.AppKeys.GetValueOrDefault(path) + app.Keys;
        }
    }
}
