using Kms.Core.Model;
using Kms.Core.Stats;

namespace Kms.Core.Tests;

public sealed class HourlyAxisTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(5.0)]
    [InlineData(69.5)]
    [InlineData(999.0)]
    public void Compute_ActiveMinutesAlwaysUsesSixtyMinuteCeiling(double axisMax)
    {
        HourlyAxis.Compute(2, axisMax, out double top, out double step);

        Assert.Equal(HourlyAxis.MaxActiveMinutes, top);
        Assert.Equal(60.0, top);
        Assert.Equal(15.0, step);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Compute_CountMetricsUseIntegerNiceTicks(int metric)
    {
        HourlyAxis.Compute(metric, 14440, out double top, out double step);

        Assert.Equal(16000.0, top);
        Assert.Equal(4000.0, step);
        Assert.Equal(0.0, top % step, 9);
    }

    [Theory]
    [InlineData(3, 1.7)]
    [InlineData(4, 1.7)]
    [InlineData(3, 3.0)]
    public void Compute_RateMetricsAllowFractionalSteps(int metric, double axisMax)
    {
        HourlyAxis.Compute(metric, axisMax, out double top, out double step);

        Assert.True(step < 1);
        Assert.True(top >= axisMax);
        Assert.Equal(top, step * 4, 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Compute_EmptyCountDataFallsBackToUnitStep(double axisMax)
    {
        HourlyAxis.Compute(0, axisMax, out double top, out double step);

        Assert.Equal(1.0, step);
        Assert.Equal(4.0, top);
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(7.4, "7")]
    [InlineData(15.0, "15")]
    [InlineData(45.0, "45")]
    [InlineData(60.0, "60")]
    public void Label_ActiveMinutesAreWholeNumbers(double value, string expected) =>
        Assert.Equal(expected, HourlyAxis.Label(2, value));

    [Theory]
    [InlineData(0.5, "0.5")]
    [InlineData(1.234, "1.23")]
    [InlineData(2.0, "2")]
    public void Label_RateMetricsKeepUpToTwoDecimals(double value, string expected) =>
        Assert.Equal(expected, HourlyAxis.Label(3, value));

    [Theory]
    [InlineData(999.0, "999")]
    [InlineData(4000.0, "4k")]
    [InlineData(1600000.0, "1.6M")]
    public void Label_CountMetricsReuseAxisScaleSuffixes(double value, string expected) =>
        Assert.Equal(expected, HourlyAxis.Label(0, value));

    [Theory]
    [InlineData(0, " 次")]
    [InlineData(1, " 次")]
    [InlineData(2, " 分钟")]
    [InlineData(3, " 次/分")]
    [InlineData(4, " 次/分")]
    public void Unit_MatchesV3(int metric, string expected) =>
        Assert.Equal(expected, HourlyAxis.Unit(metric));

    [Theory]
    [InlineData(75.0, 60.0)]
    [InlineData(60.0, 60.0)]
    [InlineData(59.9, 59.9)]
    [InlineData(0.0, 0.0)]
    public void Clamp_CapsActiveMinutesAtTop(double value, double expected) =>
        Assert.Equal(expected, HourlyAxis.Clamp(2, value, HourlyAxis.MaxActiveMinutes), 9);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Clamp_LeavesOtherMetricsAlone(int metric) =>
        Assert.Equal(75.0, HourlyAxis.Clamp(metric, 75, 60), 9);

    [Fact]
    public void Clamp_KeepsNaN() =>
        Assert.True(double.IsNaN(HourlyAxis.Clamp(2, double.NaN, HourlyAxis.MaxActiveMinutes)));
}

public sealed class HourlySeriesTests
{
    private static readonly DateTime Selected = new(2026, 11, 5);
    private static readonly DateTime NextDay = new(2026, 11, 6);

    private static void AssertAllNaN(double[] values) =>
        Assert.All(values, value => Assert.True(double.IsNaN(value)));

    private static void AssertNoNaN(double[] values) =>
        Assert.All(values, value => Assert.False(double.IsNaN(value)));

    [Fact]
    public void Values_AlwaysHasTwentyFourSlots()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[0] = 60;

        Assert.Equal(24, HourlySeries.Values(day, Selected, 0, NextDay).Length);
        Assert.Equal(24, HourlySeries.Values(null, Selected, 0, NextDay).Length);
    }

    [Fact]
    public void Values_KeyCountsPassThrough()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.HourKeys[8] = 123;
        day.HourKeys[23] = 7;

        double[] values = HourlySeries.Values(day, Selected, 0, NextDay);

        AssertNoNaN(values);
        Assert.Equal(123.0, values[8]);
        Assert.Equal(7.0, values[23]);
        Assert.Equal(0.0, values[9]);
    }

    [Fact]
    public void Values_ClickCountsPassThrough()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.HourClicks[8] = 30;

        double[] values = HourlySeries.Values(day, Selected, 1, NextDay);

        Assert.Equal(30.0, values[8]);
        Assert.Equal(0.0, values[9]);
    }

    [Fact]
    public void Values_ActiveSecondsBecomeMinutes()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[8] = 120;
        day.ActiveHours[9] = 30;

        double[] values = HourlySeries.Values(day, Selected, 2, NextDay);

        Assert.Equal(2.0, values[8], 9);
        Assert.Equal(0.5, values[9], 9);
        Assert.Equal(0.0, values[10]);
    }

    [Fact]
    public void Values_ActiveMinutesAreNotClampedAtDataLayer()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[8] = 4200; // 历史异常值 70 分钟，绘制时才由 HourlyAxis.Clamp 收口

        double[] values = HourlySeries.Values(day, Selected, 2, NextDay);

        Assert.Equal(70.0, values[8], 9);
    }

    [Fact]
    public void Values_KeyRateIsPerActiveMinute()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[8] = 60;
        day.HourKeys[8] = 120; // 1 分钟活跃敲了 120 下 = 120 次/分
        day.ActiveHours[9] = 30;
        day.HourKeys[9] = 10;  // 半分钟 10 下 = 20 次/分

        double[] values = HourlySeries.Values(day, Selected, 3, NextDay);

        Assert.Equal(120.0, values[8], 9);
        Assert.Equal(20.0, values[9], 9);
    }

    [Fact]
    public void Values_ClickRateIsPerActiveMinute()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[8] = 60;
        day.HourClicks[8] = 60;
        day.ActiveHours[9] = 30;
        day.HourClicks[9] = 5;

        double[] values = HourlySeries.Values(day, Selected, 4, NextDay);

        Assert.Equal(60.0, values[8], 9);
        Assert.Equal(10.0, values[9], 9);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Values_RateHourWithoutActiveTimeIsMissing(int metric)
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[8] = 60;
        day.HourKeys[9] = 20;
        day.HourClicks[9] = 20;

        double[] values = HourlySeries.Values(day, Selected, metric, NextDay);

        Assert.Equal(0.0, values[8]);
        Assert.True(double.IsNaN(values[9]));
    }

    [Fact]
    public void Values_HidesFutureHoursOfToday()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[10] = 60;
        DateTime now = new(2026, 11, 5, 12, 34, 0);

        double[] values = HourlySeries.Values(day, Selected, 2, now);

        for (int hour = 0; hour <= now.Hour; hour++)
        {
            Assert.False(double.IsNaN(values[hour]), "hour " + hour + " should be visible");
        }

        for (int hour = now.Hour + 1; hour < 24; hour++)
        {
            Assert.True(double.IsNaN(values[hour]), "hour " + hour + " should be hidden");
        }

        Assert.Equal(1.0, values[10]);
    }

    [Fact]
    public void Values_KeepsLateHoursOfPastDay()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[23] = 60;

        double[] values = HourlySeries.Values(day, Selected, 2, NextDay);

        AssertNoNaN(values);
        Assert.Equal(1.0, values[23]);
    }

    [Fact]
    public void Values_UsesDatePartOfSelectedDate()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.ActiveHours[8] = 60;

        double[] values = HourlySeries.Values(day, new DateTime(2026, 11, 5, 18, 45, 0), 2, NextDay);

        AssertNoNaN(values);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Values_FutureDateIsAllMissing(int metric)
    {
        DayRecord day = new() { Date = NextDay };
        day.ActiveHours[8] = 60;
        day.HourKeys[8] = 10;
        day.HourClicks[8] = 5;

        AssertAllNaN(HourlySeries.Values(day, NextDay, metric, new DateTime(2026, 11, 5, 23, 59, 0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Values_NullDayIsAllMissing(int metric) =>
        AssertAllNaN(HourlySeries.Values(null, Selected, metric, NextDay));

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Values_RateMetricsNeedHourBuckets(int metric)
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 3600 };
        day.HourKeys[8] = 100;
        day.HourClicks[8] = 20;

        AssertAllNaN(HourlySeries.Values(day, Selected, metric, NextDay));
    }

    [Fact]
    public void Values_ActiveMinutesFallBackToZeroWhenHourBucketsEmpty()
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 3600 };

        double[] values = HourlySeries.Values(day, Selected, 2, NextDay);

        AssertNoNaN(values);
        Assert.All(values, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(0, 100.0)]
    [InlineData(1, 20.0)]
    public void Values_CountMetricsDoNotNeedHourBuckets(int metric, double expected)
    {
        DayRecord day = new() { Date = Selected, ActiveSeconds = 1 };
        day.HourKeys[8] = 100;
        day.HourClicks[8] = 20;

        double[] values = HourlySeries.Values(day, Selected, metric, NextDay);

        AssertNoNaN(values);
        Assert.Equal(expected, values[8]);
    }

    [Fact]
    public void HasTime_NullDayIsFalse() => Assert.False(HourlySeries.HasTime(null));

    [Fact]
    public void HasTime_EmptyDayIsFalse() => Assert.False(HourlySeries.HasTime(new DayRecord()));

    [Fact]
    public void HasTime_TrueForTotalsOrSessions()
    {
        Assert.True(HourlySeries.HasTime(new DayRecord { ActiveSeconds = 1 }));
        Assert.True(HourlySeries.HasTime(new DayRecord { IdleSeconds = 1 }));
        DayRecord session = new();
        session.Sessions.Add(new ActiveSession { Seconds = 60 });
        Assert.True(HourlySeries.HasTime(session));
    }

    [Fact]
    public void HasTime_IgnoresHourBucketsWhenTotalsEmpty()
    {
        DayRecord day = new();
        day.HourKeys[8] = 100;
        day.HourClicks[8] = 20;

        Assert.False(HourlySeries.HasTime(day));
    }

    [Theory]
    [InlineData(1, 9, 4)]
    [InlineData(3, 9, 2)]
    [InlineData(7, 8, 29)]
    [InlineData(14, 8, 22)]
    [InlineData(0, 9, 4)]
    [InlineData(-5, 9, 4)]
    public void ComparisonDate_ClampsToAtLeastOneDay(int compareDays, int month, int day)
    {
        DateTime selected = new(2026, 9, 5);

        Assert.Equal(new DateTime(2026, month, day), HourlySeries.ComparisonDate(selected, compareDays));
    }

    [Fact]
    public void ComparisonDate_StripsTimeOfDay()
    {
        DateTime result = HourlySeries.ComparisonDate(new DateTime(2026, 9, 5, 18, 45, 30), 3);

        Assert.Equal(new DateTime(2026, 9, 2), result);
        Assert.Equal(TimeSpan.Zero, result.TimeOfDay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void ComparisonDates_UnknownModeIsEmpty(int mode) =>
        Assert.Empty(HourlySeries.ComparisonDates(new DateTime(2026, 9, 5), mode, 3));

    [Fact]
    public void ComparisonDates_ModeOneIsYesterday()
    {
        DateTime[] dates = HourlySeries.ComparisonDates(new DateTime(2026, 9, 5), 1, 3);

        Assert.Equal(new[] { new DateTime(2026, 9, 4) }, dates);
    }

    [Fact]
    public void ComparisonDates_ModeTwoIsLastWeekAndCrossesYear()
    {
        DateTime[] dates = HourlySeries.ComparisonDates(new DateTime(2026, 1, 1), 2, 3);

        Assert.Equal(new[] { new DateTime(2025, 12, 25) }, dates);
    }

    [Fact]
    public void ComparisonDates_RecentDaysExcludesSelectedDay()
    {
        DateTime[] dates = HourlySeries.ComparisonDates(new DateTime(2026, 9, 5), 3, 3);

        Assert.Equal(new[] { new DateTime(2026, 9, 4), new DateTime(2026, 9, 3) }, dates);
    }

    [Fact]
    public void ComparisonDates_RecentDaysClampToTwoThroughFourteen()
    {
        DateTime selected = new(2026, 9, 5);

        DateTime[] minimum = HourlySeries.ComparisonDates(selected, 3, 1);
        Assert.Equal(new[] { new DateTime(2026, 9, 4) }, minimum);

        DateTime[] maximum = HourlySeries.ComparisonDates(selected, 3, 20);
        Assert.Equal(13, maximum.Length);
        Assert.Equal(new DateTime(2026, 9, 4), maximum[0]);
        Assert.Equal(new DateTime(2026, 8, 23), maximum[^1]);
        Assert.DoesNotContain(selected, maximum);
    }

    [Fact]
    public void ComparisonDates_ModeFourStartsMondayAndSkipsSelectedDay()
    {
        // 2026-09-05 是周六，所在周为 08-31(周一)…09-06(周日)。
        DateTime[] dates = HourlySeries.ComparisonDates(new DateTime(2026, 9, 5), 4, 7);

        Assert.Equal(
            new[]
            {
                new DateTime(2026, 8, 31),
                new DateTime(2026, 9, 1),
                new DateTime(2026, 9, 2),
                new DateTime(2026, 9, 3),
                new DateTime(2026, 9, 4),
                new DateTime(2026, 9, 6)
            },
            dates);
    }

    [Fact]
    public void ComparisonDates_ModeFourOnMondaySkipsMonday()
    {
        DateTime monday = new(2026, 8, 31);

        DateTime[] dates = HourlySeries.ComparisonDates(monday, 4, 7);

        Assert.Equal(6, dates.Length);
        Assert.Equal(new DateTime(2026, 9, 1), dates[0]);
        Assert.Equal(new DateTime(2026, 9, 6), dates[^1]);
        Assert.DoesNotContain(monday, dates);
    }

    [Fact]
    public void ComparisonDates_ModeFourCrossesYear()
    {
        DateTime[] dates = HourlySeries.ComparisonDates(new DateTime(2026, 1, 1), 4, 7);

        Assert.Equal(
            new[]
            {
                new DateTime(2025, 12, 29),
                new DateTime(2025, 12, 30),
                new DateTime(2025, 12, 31),
                new DateTime(2026, 1, 2),
                new DateTime(2026, 1, 3),
                new DateTime(2026, 1, 4)
            },
            dates);
    }

    [Fact]
    public void ComparisonDates_ModeFourIgnoresTimeOfDay()
    {
        DateTime[] withTime = HourlySeries.ComparisonDates(new DateTime(2026, 9, 5, 23, 59, 0), 4, 7);
        DateTime[] midnight = HourlySeries.ComparisonDates(new DateTime(2026, 9, 5), 4, 7);

        Assert.Equal(midnight, withTime);
    }
}
