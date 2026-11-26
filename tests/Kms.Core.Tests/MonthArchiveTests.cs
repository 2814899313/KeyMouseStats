using Kms.Core.Model;

namespace Kms.Core.Tests;

public sealed class MonthArchiveTests
{
    [Fact]
    public void Fold_AggregatesAndKeepsTopLevelOnly()
    {
        Dictionary<string, MonthArchive> archives = [];
        DayRecord day = new()
        {
            Date = new DateTime(2026, 11, 22),
            Keys = 100,
            Clicks = 20,
            Wheel = 5,
            ActiveSeconds = 3600,
            MoveMeters = 12.5,
            HoldMaxMs = 700
        };
        day.Sessions.Add(new ActiveSession { Seconds = 10 });
        day.ComboCounts["ctrl_c"] = 3;
        day.Apps["x"] = new AppUsage { ProcessPath = @"C:\a\b.exe", Keys = 7 };

        MonthArchive.Fold(archives, day);
        MonthArchive.Fold(archives, day);

        MonthArchive archive = archives["2026-11"];
        Assert.Equal(2, archive.Days);
        Assert.Equal(200, archive.Keys);
        Assert.Equal(2, archive.Sessions);
        Assert.Equal(6, archive.Combos);
        Assert.Equal(14, archive.AppKeys[@"C:\a\b.exe"]);
        Assert.Equal(700, archive.HoldMaxMs);
    }

    [Fact]
    public void Fold_SkipsEmptyDays()
    {
        Dictionary<string, MonthArchive> archives = [];
        MonthArchive.Fold(archives, new DayRecord { Date = new DateTime(2026, 11, 22) });

        Assert.Empty(archives);
    }
}
