using System.ComponentModel;
using Kms.Core.Time;

namespace Kms.Core.Tests;

public sealed class TimeScopeTests
{
    [Fact]
    public void Today_IsSingleDayEndingNow()
    {
        TimeScopeRange range = TimeScopeRange.Create(TimeScope.Today, new DateTime(2026, 11, 26, 18, 30, 0));

        Assert.Equal(new DateTime(2026, 11, 26), range.Start);
        Assert.Equal(new DateTime(2026, 11, 26), range.End);
        Assert.Equal(1, range.Days);
        Assert.Equal("今日 · 11-26", range.Caption);
    }

    [Fact]
    public void Week_StartsMondayAndStopsToday()
    {
        // 2026-11-26 是周四；本周应为 11-23 → 11-26。
        TimeScopeRange range = TimeScopeRange.Create(TimeScope.ThisWeek, new DateTime(2026, 11, 26, 9, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 23), range.Start);
        Assert.Equal(new DateTime(2026, 11, 26), range.End);
        Assert.Equal(4, range.Days);
        Assert.Equal("本周 · 11-23 → 11-26", range.Caption);
    }

    [Fact]
    public void Month_StartsFirstAndStopsToday()
    {
        TimeScopeRange range = TimeScopeRange.Create(TimeScope.ThisMonth, new DateTime(2026, 11, 26, 9, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 1), range.Start);
        Assert.Equal(new DateTime(2026, 11, 26), range.End);
        Assert.Equal(26, range.Days);
    }

    [Fact]
    public void Sunday_StillBelongsToPreviousMondayWeek()
    {
        TimeScopeRange range = TimeScopeRange.Create(TimeScope.ThisWeek, new DateTime(2026, 11, 22, 23, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 16), range.Start);
        Assert.Equal(new DateTime(2026, 11, 22), range.End);
    }

    [Fact]
    public void Service_RaisesChangeWithComputedRange()
    {
        DateTime now = new(2026, 11, 26, 12, 0, 0);
        GlobalTimeScopeService service = new(() => now);
        TimeScopeRange? observed = null;
        service.TimeScopeChanged += (_, e) => observed = e.Range;
        int propertyEvents = 0;
        ((INotifyPropertyChanged)service).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GlobalTimeScopeService.SelectedTimeScope))
            {
                propertyEvents++;
            }
        };

        service.SelectedTimeScope = TimeScope.ThisMonth;

        Assert.NotNull(observed);
        Assert.Equal(TimeScope.ThisMonth, observed.Value.Scope);
        Assert.Equal(new DateTime(2026, 11, 1), observed.Value.Start);
        Assert.Equal(1, propertyEvents);
    }

    [Fact]
    public void Service_RollsRangeAcrossMidnightWithoutChangingSelection()
    {
        DateTime now = new(2026, 11, 30, 23, 50, 0);
        GlobalTimeScopeService service = new(() => now)
        {
            SelectedTimeScope = TimeScope.ThisMonth
        };

        now = new DateTime(2026, 12, 1, 0, 1, 0);
        service.Refresh();

        Assert.Equal(TimeScope.ThisMonth, service.SelectedTimeScope);
        Assert.Equal(new DateTime(2026, 12, 1), service.Range.Start);
        Assert.Equal(new DateTime(2026, 12, 1), service.Range.End);
    }
}
