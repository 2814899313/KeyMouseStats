using Kms.Core.Model;
using Kms.Storage.Text;

namespace Kms.Storage.Tests;

public sealed class TextStatsCodecTests
{
    [Theory]
    [InlineData("v13-text-current.txt")]
    [InlineData("v13-synthetic.txt")]
    public void SaveThenLoadPreservesAggregates(string fixture)
    {
        string path = Path.Combine(RepoRoot(), "tests", "Fixtures", fixture);
        StatsData first = TextStatsCodec.Load(File.ReadAllText(path));
        string saved = TextStatsCodec.Save(first);
        StatsData second = TextStatsCodec.Load(saved);

        Assert.Equal(13, second.FormatVersion);
        Assert.Equal(first.History.Count, second.History.Count);
        Assert.Equal(SumKeys(first), SumKeys(second));
        Assert.Equal(SumClicks(first), SumClicks(second));
        Assert.Equal(CountApps(first), CountApps(second));
        Assert.Equal(CountSessions(first), CountSessions(second));
        Assert.Equal(first.Archives.Count, second.Archives.Count);
        AssertTotals(first.Totals, second.Totals);
    }

    [Theory]
    [InlineData("v13-text-current.txt")]
    [InlineData("v13-synthetic.txt")]
    public void SaveThenLoadPreservesDetailFields(string fixture)
    {
        string path = Path.Combine(RepoRoot(), "tests", "Fixtures", fixture);
        StatsData first = TextStatsCodec.Load(File.ReadAllText(path));
        StatsData second = TextStatsCodec.Load(TextStatsCodec.Save(first));

        Assert.Equal(first.History.Keys.Order(StringComparer.Ordinal), second.History.Keys.Order(StringComparer.Ordinal));

        foreach ((string dayKey, DayRecord day) in first.History)
        {
            DayRecord other = second.History[dayKey];
            Assert.Equal(day.Keys, other.Keys);
            Assert.Equal(day.Clicks, other.Clicks);
            Assert.Equal(day.MovePx, other.MovePx);
            Assert.Equal(day.ActiveSeconds, other.ActiveSeconds);
            Assert.Equal(day.AppObservedSeconds, other.AppObservedSeconds);
            Assert.Equal(day.HourKeys, other.HourKeys);
            Assert.Equal(day.HourClicks, other.HourClicks);
            Assert.Equal(day.ActiveHours, other.ActiveHours);
            Assert.Equal(day.KeyCounts, other.KeyCounts);
            Assert.Equal(day.PhysicalKeys, other.PhysicalKeys);
            Assert.Equal(day.PhysicalVks, other.PhysicalVks);
            Assert.Equal(day.ComboCounts, other.ComboCounts);
            Assert.Equal(day.ClickCells, other.ClickCells);
            Assert.Equal(day.ClickCellsV1, other.ClickCellsV1);
            Assert.Equal(day.HoldBuckets, other.HoldBuckets);
            Assert.Equal(day.HoldCount, other.HoldCount);
            Assert.Equal(day.HoldTotalMs, other.HoldTotalMs);
            Assert.Equal(day.HoldMaxMs, other.HoldMaxMs);
            Assert.Equal(day.HoldActiveSeconds, other.HoldActiveSeconds);
            Assert.Equal(day.Sessions.Count, other.Sessions.Count);
            Assert.Equal(day.IdlePeriods.Count, other.IdlePeriods.Count);
            Assert.Equal(day.Apps.Count, other.Apps.Count);

            foreach ((string id, AppUsage app) in day.Apps)
            {
                AppUsage copied = other.Apps[id];
                Assert.Equal(app.Keys, copied.Keys);
                Assert.Equal(app.Clicks, copied.Clicks);
                Assert.Equal(app.Wheel, copied.Wheel);
                Assert.Equal(app.ActiveSeconds, copied.ActiveSeconds);
            }

            Assert.Equal(TotalKeyGroups(day), TotalKeyGroups(other));
        }
    }

    private static long[] TotalKeyGroups(DayRecord day)
    {
        long[] totals = new long[6];
        foreach (AppUsage app in day.Apps.Values)
        {
            if (!app.HasKeyGroups)
            {
                continue;
            }

            for (int i = 0; i < 6; i++)
            {
                totals[i] += app.KeyGroups[i];
            }
        }

        return totals;
    }

    [Fact]
    public void LoadKeepsOldFormatDefaults()
    {
        const string text = """
            # 键鼠统计数据文件 v10 / 正式版 1.6.0
            format=10
            wellbeing_v1=0|0||0|90|30||0|22|
            archive_v1=2024-01|2|20|3|1|0|100|0.5|1
            [day=2024-01-01]
            keys=10
            clicks=2
            left=2
            """;

        StatsData data = TextStatsCodec.Load(text);

        Assert.Equal(10, data.FormatVersion);
        Assert.Equal("0|0||0|90|30||0|22|", data.Settings["wellbeing_v1"]);
        Assert.Single(data.Archives);
        Assert.Equal(20, data.Archives["2024-01"].Keys);
        Assert.Equal(10, data.History["2024-01-01"].Keys);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static long SumKeys(StatsData data)
    {
        long total = 0;
        foreach (DayRecord day in data.History.Values)
        {
            total += day.Keys;
        }

        return total;
    }

    private static long SumClicks(StatsData data)
    {
        long total = 0;
        foreach (DayRecord day in data.History.Values)
        {
            total += day.Clicks;
        }

        return total;
    }

    private static int CountApps(StatsData data)
    {
        int total = 0;
        foreach (DayRecord day in data.History.Values)
        {
            total += day.Apps.Count;
        }

        return total;
    }

    private static int CountSessions(StatsData data)
    {
        int total = 0;
        foreach (DayRecord day in data.History.Values)
        {
            total += day.Sessions.Count;
        }

        return total;
    }

    private static void AssertTotals(StatsTotals expected, StatsTotals actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        Assert.Equal(expected.Clicks, actual.Clicks);
        Assert.Equal(expected.Left, actual.Left);
        Assert.Equal(expected.Right, actual.Right);
        Assert.Equal(expected.Middle, actual.Middle);
        Assert.Equal(expected.XButtons, actual.XButtons);
        Assert.Equal(expected.Wheel, actual.Wheel);
        Assert.Equal(expected.HWheel, actual.HWheel);
        Assert.Equal(expected.MovePx, actual.MovePx);
        Assert.Equal(expected.MoveMeters, actual.MoveMeters);
        Assert.Equal(expected.HoldCount, actual.HoldCount);
        Assert.Equal(expected.HoldDiscarded, actual.HoldDiscarded);
        Assert.Equal(expected.HoldTotalMs, actual.HoldTotalMs);
        Assert.Equal(expected.HoldMaxMs, actual.HoldMaxMs);
        Assert.Equal(expected.HoldActiveSeconds, actual.HoldActiveSeconds);
    }
}
