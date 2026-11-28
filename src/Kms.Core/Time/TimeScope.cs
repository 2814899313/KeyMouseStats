using System.ComponentModel;
using System.Globalization;

namespace Kms.Core.Time;

/// <summary>全局时间范围。九个分析视图共用同一个选择，页面内不再各自维护。</summary>
public enum TimeScope
{
    Today = 0,
    ThisWeek = 1,
    ThisMonth = 2
}

/// <summary>
/// 一次全局时间范围的不可变快照。周一为一周起点；End 始终裁到“今天”，
/// 因此本周/本月不会把尚未发生的未来日期画进趋势或报表。
/// </summary>
public readonly record struct TimeScopeRange(TimeScope Scope, DateTime Start, DateTime End)
{
    public static TimeScopeRange Create(TimeScope scope, DateTime now)
    {
        DateTime today = now.Date;
        DateTime start = scope switch
        {
            TimeScope.ThisWeek => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            TimeScope.ThisMonth => new DateTime(today.Year, today.Month, 1),
            _ => today
        };

        return new TimeScopeRange(scope, start, today);
    }

    public int Days => End < Start ? 0 : (End - Start).Days + 1;

    public bool IsToday => Scope == TimeScope.Today;

    public string Label => Scope switch
    {
        TimeScope.ThisWeek => "本周",
        TimeScope.ThisMonth => "本月",
        _ => "今日"
    };

    /// <summary>用于副标与角标的紧凑范围文本，例如 <c>11-24 → 11-26</c>。</summary>
    public string RangeText
    {
        get
        {
            if (Days <= 0)
            {
                return "--";
            }

            string format = Start.Year == End.Year ? "MM-dd" : "yyyy-MM-dd";
            return Scope == TimeScope.Today
                ? Start.ToString(format, CultureInfo.InvariantCulture)
                : Start.ToString(format, CultureInfo.InvariantCulture)
                  + " → "
                  + End.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>完整标题，例如 <c>本周 · 11-24 → 11-26</c>。</summary>
    public string Caption => Label + " · " + RangeText;

    public bool Contains(DateTime date)
    {
        DateTime day = date.Date;
        return day >= Start && day <= End;
    }

    public IEnumerable<DateTime> DaySequence()
    {
        for (DateTime day = Start; day <= End; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}

public sealed class TimeScopeChangedEventArgs(TimeScopeRange range) : EventArgs
{
    public TimeScopeRange Range { get; } = range;
}

/// <summary>
/// 全局时间范围状态中枢。只保存 UI 选择与派生范围，不读取磁盘、不接触采集钩子线程；
/// 所有变更都应发生在 Avalonia UI 线程。页面通过 <see cref="TimeScopeChanged"/> 拿到新上下文。
/// </summary>
public sealed class GlobalTimeScopeService : INotifyPropertyChanged
{
    private readonly Func<DateTime> _clock;
    private TimeScope _selectedTimeScope;
    private TimeScopeRange _range;

    public GlobalTimeScopeService(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (static () => DateTime.Now);
        _range = TimeScopeRange.Create(_selectedTimeScope, _clock());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<TimeScopeChangedEventArgs>? TimeScopeChanged;

    public TimeScope SelectedTimeScope
    {
        get => _selectedTimeScope;
        set
        {
            if (_selectedTimeScope == value)
            {
                return;
            }

            _selectedTimeScope = value;
            ApplyRange(TimeScopeRange.Create(value, _clock()), changedSelection: true);
        }
    }

    /// <summary>当前范围快照；跨零点时由 <see cref="Refresh"/> 自动滚动到新的“今天”。</summary>
    public TimeScopeRange Range => _range;

    public string RangeText => _range.RangeText;

    /// <summary>跨日刷新：只重算派生范围，不重新读取任何数据。</summary>
    public void Refresh()
    {
        TimeScopeRange next = TimeScopeRange.Create(_selectedTimeScope, _clock());
        if (next != _range)
        {
            ApplyRange(next, changedSelection: false);
        }
    }

    private void ApplyRange(TimeScopeRange range, bool changedSelection)
    {
        _range = range;
        if (changedSelection)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTimeScope)));
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Range)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RangeText)));
        TimeScopeChanged?.Invoke(this, new TimeScopeChangedEventArgs(range));
    }
}
