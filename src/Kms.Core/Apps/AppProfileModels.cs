using Kms.Core.Model;

namespace Kms.Core.Apps;

/// <summary>后台只读查询的输入。日期为本地自然日闭区间。</summary>
public sealed class AppProfileQuery
{
    public required AppFilterScope Filter { get; init; }

    public required AppScopeSet Scope { get; init; }

    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    public string CacheKey =>
        Scope.CacheKey + "|" +
        Start.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "|" +
        End.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static AppProfileQuery Create(
        AppFilterScope filter,
        IEnumerable<string>? categoryAppKeys,
        DateTime start,
        DateTime end) => new()
    {
        Filter = filter,
        Scope = filter.ToScopeSet(categoryAppKeys),
        Start = start.Date,
        End = end.Date
    };
}

public sealed class AppTotalsSnapshot
{
    public long Keys { get; init; }
    public long Clicks { get; init; }
    public long Left { get; init; }
    public long Right { get; init; }
    public long Middle { get; init; }
    public long XButtons { get; init; }
    public long Wheel { get; init; }
    public double ActiveSeconds { get; init; }
    public double MovePx { get; init; }
    public long Sessions { get; init; }
    public long Switches { get; init; }
    public long PeakApm { get; init; }
    public int AppCount { get; init; }

    /// <summary>六类键位分组：移动/数字/空格/功能/文字/未归位。</summary>
    public IReadOnlyList<long> KeyGroups { get; init; } = new long[6];
}

public readonly record struct AppHourlyBucket(
    long Keys,
    long Clicks,
    double ActiveSeconds);

public readonly record struct AppKeyCount(int Vk, long Count);

public readonly record struct AppHeatCell(
    int Channel,
    int CellX,
    int CellY,
    long Count);

public sealed record AppWindowUsage(
    string AppKey,
    string ProcessPath,
    string Title,
    long Keys,
    long Clicks,
    long Wheel,
    double ActiveSeconds);

/// <summary>单次应用画像查询的不可变快照。UI 只消费此对象，不直接碰 SQLite。</summary>
public sealed class AppProfileSnapshot
{
    public required AppFilterScope Filter { get; init; }

    public required AppScopeSet Scope { get; init; }

    public required DateTime Start { get; init; }

    public required DateTime End { get; init; }

    public required AppDataCapabilities Capabilities { get; init; }

    public required AppTotalsSnapshot Totals { get; init; }

    public IReadOnlyList<AppHourlyBucket> Hours { get; init; } = [];

    public IReadOnlyList<AppKeyCount> Keys { get; init; } = [];

    public IReadOnlyList<AppHeatCell> Heat { get; init; } = [];

    public IReadOnlyList<ActiveSession> Sessions { get; init; } = [];

    public IReadOnlyList<AppWindowUsage> Windows { get; init; } = [];

    public string SourceNote { get; init; } = "";
}
