using Kms.Core.Model;
using Kms.Core.Stats;

namespace Kms.Core.Tests;

public sealed class TrendCutoffTests
{
    private static DayRecord Day(DateTime date, params long[] hours)
    {
        DayRecord day = new() { Date = date };
        for (int i = 0; i < hours.Length && i < 24; i++)
        {
            day.HourKeys[i] = hours[i];
        }

        return day;
    }

    [Fact]
    public void Compare_OnlyUsesCompletedHours()
    {
        DateTime now = new(2026, 11, 23, 9, 57, 0);
        DayRecord today = Day(now.Date, 1, 1, 1, 1, 1, 1, 1, 1, 1, 99);
        DayRecord yesterday = Day(now.Date.AddDays(-1), 1, 1, 1, 1, 1, 1, 1, 1, 1, 99);

        TrendCutoff.Comparison comparison = TrendCutoff.Compare(today, yesterday, 0, now);

        Assert.True(comparison.Available);
        Assert.Equal(9, comparison.Hours);
        Assert.Equal(9, comparison.Today);
        Assert.Equal(9, comparison.Yesterday);
        Assert.Equal(0, comparison.Percent);
    }

    [Fact]
    public void Compare_UnsupportedMetricIsUnavailable()
    {
        TrendCutoff.Comparison comparison =
            TrendCutoff.Compare(Day(DateTime.Today), Day(DateTime.Today.AddDays(-1)), 2, DateTime.Now);

        Assert.False(comparison.Available);
        Assert.Equal("该指标没有逐小时数据", comparison.Note);
        Assert.Equal("累计中", TrendCutoff.DeltaText(comparison));
    }

    [Fact]
    public void Compare_NoFullHourYet()
    {
        TrendCutoff.Comparison comparison =
            TrendCutoff.Compare(Day(DateTime.Today), Day(DateTime.Today.AddDays(-1)), 0,
                new DateTime(2026, 11, 23, 0, 30, 0));

        Assert.False(comparison.Available);
        Assert.Equal("今天还没有完整小时", comparison.Note);
    }

    [Fact]
    public void Compare_NoYesterdayRecord()
    {
        TrendCutoff.Comparison comparison =
            TrendCutoff.Compare(Day(DateTime.Today, 5), null, 0,
                new DateTime(2026, 11, 23, 10, 0, 0));

        Assert.False(comparison.Available);
        Assert.Equal("没有昨天的记录", comparison.Note);
    }

    [Fact]
    public void DeltaText_NoInputTodayIsNeutral()
    {
        DateTime now = new(2026, 11, 23, 10, 0, 0);
        TrendCutoff.Comparison comparison =
            TrendCutoff.Compare(Day(now.Date), Day(now.Date.AddDays(-1), 1, 1, 1), 0, now);

        Assert.True(comparison.Available);
        Assert.True(comparison.NoInputToday);
        Assert.Equal("今日尚无输入", TrendCutoff.DeltaText(comparison));
    }

    [Fact]
    public void DeltaText_RoundsNegativeZero()
    {
        DateTime now = new(2026, 11, 23, 10, 0, 0);
        DayRecord today = Day(now.Date, 999, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        DayRecord yesterday = Day(now.Date.AddDays(-1), 1000, 1, 1, 1, 1, 1, 1, 1, 1, 1);

        TrendCutoff.Comparison comparison = TrendCutoff.Compare(today, yesterday, 0, now);
        string text = TrendCutoff.DeltaText(comparison);

        Assert.True(text == "0%" || text.StartsWith("+", StringComparison.Ordinal));
    }

    [Fact]
    public void LiveTag_UsesMinutes()
    {
        Assert.Equal("今日截至 09:57 · 累计中", TrendCutoff.LiveTag(new DateTime(2026, 11, 23, 9, 57, 0)));
    }
}
