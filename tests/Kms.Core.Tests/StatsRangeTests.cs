using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Core.Time;

namespace Kms.Core.Tests;

public sealed class StatsRangeTests
{
    [Fact]
    public void Build_SumsValuesAndKeepsPeakApm()
    {
        StatsData data = new();
        AddDay(data, new DateTime(2026, 11, 23), keys: 10, clicks: 2, peakApm: 40);
        AddDay(data, new DateTime(2026, 11, 24), keys: 20, clicks: 3, peakApm: 90);
        AddDay(data, new DateTime(2026, 11, 25), keys: 30, clicks: 4, peakApm: 60);

        StatsRange range = StatsRange.Build(data, new DateTime(2026, 11, 23), new DateTime(2026, 11, 25));

        Assert.Equal(3, range.CalendarDays);
        Assert.Equal(3, range.ActiveDays);
        Assert.Equal(60, range.Totals.Keys);
        Assert.Equal(9, range.Totals.Clicks);
        Assert.Equal(90, range.Totals.PeakApm);
        Assert.Equal(new[] { 23, 24, 25 }, range.Days.Select(day => day.Date.Day).ToArray());
    }

    [Fact]
    public void Build_ClipsToInclusiveRange()
    {
        StatsData data = new();
        AddDay(data, new DateTime(2026, 11, 22), keys: 5, clicks: 1, peakApm: 20);
        AddDay(data, new DateTime(2026, 11, 23), keys: 10, clicks: 2, peakApm: 30);
        AddDay(data, new DateTime(2026, 11, 24), keys: 20, clicks: 3, peakApm: 40);

        StatsRange range = StatsRange.Build(data, new DateTime(2026, 11, 23), new DateTime(2026, 11, 24));

        Assert.Equal(30, range.Totals.Keys);
        Assert.Equal(40, range.Totals.PeakApm);
        Assert.DoesNotContain(range.Days, day => day.Date.Day == 22);
    }

    [Fact]
    public void Build_EmptyRangeIsZero()
    {
        StatsData data = new();
        StatsRange range = StatsRange.Build(data, new DateTime(2026, 11, 24), new DateTime(2026, 11, 23));

        Assert.Equal(0, range.CalendarDays);
        Assert.Equal(0, range.ActiveDays);
        Assert.Equal(0, range.Totals.Keys);
        Assert.Empty(range.Days);
    }

    [Fact]
    public void Build_FromTimeScope_UsesSameWindow()
    {
        StatsData data = new();
        AddDay(data, new DateTime(2026, 11, 23), keys: 10, clicks: 2, peakApm: 30);
        AddDay(data, new DateTime(2026, 11, 26), keys: 50, clicks: 5, peakApm: 80);
        TimeScopeRange scope = TimeScopeRange.Create(TimeScope.ThisWeek, new DateTime(2026, 11, 26, 12, 0, 0));

        StatsRange range = StatsRange.Build(data, scope);

        Assert.Equal(new DateTime(2026, 11, 23), range.Start);
        Assert.Equal(new DateTime(2026, 11, 26), range.End);
        Assert.Equal(60, range.Totals.Keys);
    }

    private static void AddDay(StatsData data, DateTime date, long keys, long clicks, long peakApm)
    {
        DayRecord day = data.GetOrCreate(date);
        day.Keys = keys;
        day.Clicks = clicks;
        day.PeakApm = peakApm;
        day.HourKeys[9] = keys;
        data.Totals.Keys += keys;
        data.Totals.Clicks += clicks;
    }
}
