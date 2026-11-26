using System.Globalization;
using Kms.Core.Model;

namespace Kms.Core.Stats;

/// <summary>「今天还没结束」的同期裁剪与环比文案。移植自 v3 <c>TrendCutoff</c>。</summary>
public static class TrendCutoff
{
    public sealed class Comparison
    {
        public bool Available { get; set; }
        public bool Live { get; set; }
        public int Hours { get; set; }
        public DateTime Cutoff { get; set; }
        public double Today { get; set; }
        public double Yesterday { get; set; }
        public double? Percent { get; set; }
        public string Note { get; set; } = "";
        public bool NoInputToday => Available && Today <= 0;
    }

    public static Comparison Compare(DayRecord? today, DayRecord? yesterday, int metric, DateTime now)
    {
        Comparison comparison = new()
        {
            Live = true,
            Hours = Math.Max(0, Math.Min(24, now.Hour))
        };
        DateTime day = today is not null && today.Date != DateTime.MinValue ? today.Date : now.Date;
        comparison.Cutoff = day.AddHours(comparison.Hours);

        if (metric is not (0 or 1))
        {
            comparison.Note = "该指标没有逐小时数据";
            return comparison;
        }

        if (comparison.Hours < 1)
        {
            comparison.Note = "今天还没有完整小时";
            return comparison;
        }

        long[]? current = today is null ? null : metric == 0 ? today.HourKeys : today.HourClicks;
        long[]? previous = yesterday is null ? null : metric == 0 ? yesterday.HourKeys : yesterday.HourClicks;
        for (int i = 0; i < comparison.Hours && i < 24; i++)
        {
            if (current is not null)
            {
                comparison.Today += current[i];
            }

            if (previous is not null)
            {
                comparison.Yesterday += previous[i];
            }
        }

        if (yesterday is null)
        {
            comparison.Note = "没有昨天的记录";
            return comparison;
        }

        if (comparison.Yesterday <= 0)
        {
            comparison.Note = comparison.Today > 0 ? "昨日同期无记录" : "昨天同期也没有记录";
            return comparison;
        }

        comparison.Available = true;
        comparison.Percent = (comparison.Today - comparison.Yesterday) * 100.0 / comparison.Yesterday;
        return comparison;
    }

    public static string DeltaText(Comparison? comparison)
    {
        if (comparison is null)
        {
            return "--";
        }

        if (!comparison.Available)
        {
            if (comparison.Hours < 1 || comparison.Note == "该指标没有逐小时数据")
            {
                return "累计中";
            }

            return comparison.Today > 0 ? "新增" : "--";
        }

        if (comparison.NoInputToday)
        {
            return "今日尚无输入";
        }

        double percent = comparison.Percent ?? 0;
        double rounded = Math.Round(percent, MidpointRounding.AwayFromZero);
        if (rounded == 0 && percent < 0)
        {
            return "0%";
        }

        return (percent >= 0 ? "+" : "") + rounded.ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    public static string NoteText(Comparison? comparison)
    {
        if (comparison is null)
        {
            return "";
        }

        if (comparison.Available)
        {
            return "截至 " + comparison.Cutoff.ToString("HH:mm", CultureInfo.InvariantCulture)
                + " · 昨日同期 " + FormatCount((long)comparison.Yesterday);
        }

        return comparison.Note;
    }

    public static string LiveTag(DateTime now) =>
        "今日截至 " + now.ToString("HH:mm", CultureInfo.InvariantCulture) + " · 累计中";

    private static string FormatCount(long value) =>
        value >= 1_000_000 ? (value / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) + "M"
        : value >= 1000 ? (value / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + "k"
        : value.ToString(CultureInfo.InvariantCulture);
}
