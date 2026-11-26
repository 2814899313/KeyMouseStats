using System.Globalization;
using Kms.Core.Model;
using Kms.Storage.Sqlite;
using Kms.Storage.Text;

namespace Kms.App;

/// <summary>首启数据加载：v4 文本 → v4 库 → v3 文本 → v3 库 → 开发固件。</summary>
public sealed record LoadedStats(StatsData Data, string Source, string Status)
{
    public static LoadedStats Empty(string status) => new(new StatsData(), "空", status);
}

internal static class StatsLoader
{
    public static LoadedStats Load()
    {
        List<string> errors = [];

        if (File.Exists(DataPaths.V4Text))
        {
            if (TryText(DataPaths.V4Text, "v4 文本", errors) is { } v4Text)
            {
                return v4Text;
            }
        }

        if (File.Exists(DataPaths.V4Database))
        {
            if (TryDatabase(DataPaths.V4Database, "v4 数据库", errors) is { } v4Db)
            {
                return v4Db;
            }
        }

        if (File.Exists(DataPaths.V3Text))
        {
            if (TryText(DataPaths.V3Text, "v3 文本", errors) is { } v3Text)
            {
                return v3Text;
            }
        }

        if (File.Exists(DataPaths.V3Database))
        {
            if (TryDatabase(DataPaths.V3Database, "v3 数据库", errors) is { } v3Db)
            {
                return v3Db;
            }
        }

        string? fixture = FindFixture();
        if (fixture is not null)
        {
            if (TryText(fixture, "开发固件", errors) is { } sample)
            {
                return sample;
            }
        }

        if (errors.Count > 0)
        {
            return LoadedStats.Empty("数据读取失败：" + string.Join("；", errors));
        }

        return LoadedStats.Empty("暂无数据；开始使用后会自动采集");
    }

    private static LoadedStats? TryText(string path, string source, List<string> errors)
    {
        try
        {
            string text = File.ReadAllText(path);
            StatsData data = TextStatsCodec.Load(text);

            // 有 [day=...] 行却一天都没解析出来，视为损坏，继续尝试下一个来源，
            // 避免把用户的完整历史当成空数据。
            if (text.Contains("[day=", StringComparison.Ordinal) && data.History.Count == 0)
            {
                errors.Add($"{source} 无可解析的日记录");
                return null;
            }

            SeedAllTimePeakApm(data);
            return new LoadedStats(data, source, $"已载入 {data.History.Count} 天");
        }
        catch (Exception error)
        {
            errors.Add($"{source}：{error.Message}");
            return null;
        }
    }

    private static LoadedStats? TryDatabase(string path, string source, List<string> errors)
    {
        try
        {
            StatsData data = SqliteStatsReader.Read(path);
            SeedAllTimePeakApm(data);
            return new LoadedStats(data, source, $"已载入 {data.History.Count} 天");
        }
        catch (Exception error)
        {
            errors.Add($"{source}：{error.Message}");
            return null;
        }
    }

    /// <summary>
    /// 旧库/迁移档可能缺 <c>all_time_peak_apm</c>；用历史日峰值补齐，避免实时 APM 把全时峰值重置成个位数。
    /// </summary>
    private static void SeedAllTimePeakApm(StatsData data)
    {
        long max = 0;
        foreach (DayRecord day in data.History.Values)
        {
            if (day.PeakApm > max)
            {
                max = day.PeakApm;
            }
        }

        long current = long.TryParse(
            data.Settings.GetValueOrDefault("all_time_peak_apm"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long parsed)
            ? parsed
            : 0;

        if (max > current)
        {
            data.Settings["all_time_peak_apm"] = max.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string? FindFixture()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")))
            {
                string path = Path.Combine(
                    directory.FullName,
                    "tests",
                    "Fixtures",
                    "v13-text-current.txt");
                return File.Exists(path) ? path : null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
