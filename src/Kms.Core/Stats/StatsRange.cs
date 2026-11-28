using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.Core.Stats;

/// <summary>
/// 某段闭区间日期的只读聚合。九个页面共用同一口径：
/// 汇总用 <see cref="DayRecord.AddFrom"/>，所以按键、小时桶、按住、应用与点击热区都会相加；
/// 峰值 APM 保持最大值而不是求和。
/// </summary>
public sealed class StatsRange
{
    private StatsRange(DateTime start, DateTime end)
    {
        Start = start;
        End = end;
    }

    public DateTime Start { get; }

    public DateTime End { get; }

    /// <summary>自然日数量（含两端）；反向区间为 0。</summary>
    public int CalendarDays => End < Start ? 0 : (End - Start).Days + 1;

    /// <summary>实际有记录的自然日数量。</summary>
    public int ActiveDays { get; private set; }

    /// <summary>区间内的记录，按日期升序。</summary>
    public IReadOnlyList<DayRecord> Days => _days;

    public DayRecord Totals { get; } = new();

    private readonly List<DayRecord> _days = [];

    public static StatsRange Build(StatsData data, DateTime start, DateTime end)
    {
        DateTime from = start.Date;
        DateTime to = end.Date;
        StatsRange range = new(from, to);
        if (to < from)
        {
            return range;
        }

        foreach (DayRecord day in data.History.Values)
        {
            if (day.Date < from || day.Date > to)
            {
                continue;
            }

            range._days.Add(day);
            range.Totals.AddFrom(day);
            if (!day.IsEmpty)
            {
                range.ActiveDays++;
            }
        }

        range._days.Sort(static (a, b) => a.Date.CompareTo(b.Date));
        return range;
    }

    public static StatsRange Build(StatsData data, TimeScopeRange scope) =>
        Build(data, scope.Start, scope.End);
}
