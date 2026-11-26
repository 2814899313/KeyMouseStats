using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.Core.Tests;

public sealed class ModelTests
{
    [Fact]
    public void DayRecord_ArraysHaveV3Sizes()
    {
        DayRecord day = new();

        Assert.Equal(24, day.HourKeys.Length);
        Assert.Equal(24, day.HourClicks.Length);
        Assert.Equal(24, day.ActiveHours.Length);
        Assert.Equal(24, day.Cross.ObservedHours.Length);
        Assert.Empty(day.Apps);
        Assert.Empty(day.Sessions);
    }

    [Fact]
    public void RepairDerivedCounts_ScalesBucketsAndKeepsTotal()
    {
        DayRecord day = new() { Keys = 100, Clicks = 50 };
        day.HourKeys[0] = 10;
        day.HourKeys[1] = 10;
        day.KeyCounts[65] = 5;
        day.KeyCounts[66] = 5;
        day.HourClicks[0] = 10;
        day.Left = 10;

        bool changed = day.RepairDerivedCounts(repairButtons: true);

        Assert.True(changed);
        Assert.Equal(100, day.HourKeys.Sum());
        Assert.Equal(100, day.KeyCounts.Values.Sum());
        Assert.Equal(50, day.HourClicks.Sum());
        Assert.Equal(50, day.Left);
    }

    [Fact]
    public void RepairDerivedCounts_DoesNotInventDataWhenBucketsAreEmpty()
    {
        DayRecord day = new() { Keys = 100, Clicks = 50 };

        bool changed = day.RepairDerivedCounts(repairButtons: false);

        Assert.False(changed);
        Assert.All(day.HourKeys, value => Assert.Equal(0, value));
        Assert.Empty(day.KeyCounts);
    }

    [Fact]
    public void AppUsage_IdAndNameMatchV3()
    {
        AppUsage app = new() { ProcessPath = @"C:\Apps\Chrome.EXE", Title = "Inbox" };

        Assert.Equal(@"c:\apps\chrome.exe" + "\n" + "Inbox", app.Id);
        Assert.Equal("Chrome.EXE", app.AppName);
        Assert.Equal("未识别应用", new AppUsage().AppName);
    }

    [Fact]
    public void KeyHold_MeanIsNaNWithoutSamples()
    {
        KeyHold hold = new();
        Assert.True(double.IsNaN(hold.Mean));

        hold.Add(100);
        hold.Add(300);
        Assert.Equal(200, hold.Mean);
        Assert.Equal(300, hold.MaxMs);
    }

    [Fact]
    public void MouseProfile_RingsLastSevenDays()
    {
        MouseProfile mouse = new();
        DateTime start = new(2026, 11, 1);

        for (int i = 0; i < 9; i++)
        {
            mouse.Count(1, start.AddDays(i));
        }

        Assert.Equal(9, mouse.Left);
        Assert.Equal(9, mouse.TotalClicks);
        Assert.Equal(7, mouse.DayStamp.Count(stamp => stamp != 0));
        Assert.Equal(start.AddDays(2).Ticks, mouse.DayStamp[0]);
        Assert.Equal(start.AddDays(8).Ticks, mouse.DayStamp[6]);
    }

    [Fact]
    public void UnixTime_RoundTripsAndKeepsYearOneOffset()
    {
        DateTime value = new(2026, 11, 22, 12, 34, 56, DateTimeKind.Utc);
        long ms = UnixTime.ToUnixMs(value);

        Assert.Equal(value, UnixTime.FromUnixMs(ms));
        Assert.Equal(62_135_596_800_000, UnixTime.YearOneToUnixMs);
    }

    [Fact]
    public void AddFrom_MergesDictionariesAndKeepsMaxPeak()
    {
        DayRecord a = new() { Keys = 10, PeakApm = 100 };
        DayRecord b = new() { Keys = 5, PeakApm = 200 };
        a.KeyCounts[65] = 3;
        b.KeyCounts[65] = 4;
        b.Apps["x"] = new AppUsage { ProcessPath = "a.exe", Title = "t", Keys = 5 };

        a.AddFrom(b);

        Assert.Equal(15, a.Keys);
        Assert.Equal(200, a.PeakApm);
        Assert.Equal(7, a.KeyCounts[65]);
        Assert.Equal(5, a.Apps["x"].Keys);
    }
}
