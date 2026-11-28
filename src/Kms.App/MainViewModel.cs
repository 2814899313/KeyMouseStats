using System.ComponentModel;
using System.Globalization;
using Kms.App.Apps;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Text;
using Kms.Core.Time;
using Kms.Storage.Apps;

namespace Kms.App;

/// <summary>
/// 主窗口状态中枢。全局时间范围、当前页与日报选日都集中在这里：
/// 页面只读取快照，切换标签页不会重置时间跨度；时间范围变化时经 <see cref="TimeScopeChanged"/>
/// 广播给当前激活页面。此类只服务 Avalonia UI 线程，不参与采集钩子或写入线程。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly object _rimeGate = new();
    private RimeLogStats? _rimeStats;
    private AppFilterScope _selectedAppFilter = AppFilterScope.All;
    private int _activePageIndex;
    private DateTime? _selectedDay;

    public MainViewModel(
        LoadedStats loaded,
        InputService? service,
        GlobalTimeScopeService? timeScope = null)
    {
        Loaded = loaded;
        Service = service;
        TimeScope = timeScope ?? new GlobalTimeScopeService();
        TimeScope.TimeScopeChanged += OnTimeScopeChanged;
        Profiles = new AppProfileRepository(DataPaths.V4Database, loaded.Data);
        Categories = service?.Categories
            ?? new AppCategoryService(null, loaded.Data, loadFromDisk: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<TimeScopeChangedEventArgs>? TimeScopeChanged;

    public event EventHandler<AppFilterChangedEventArgs>? AppFilterChanged;

    public LoadedStats Loaded { get; }

    public InputService? Service { get; }

    public GlobalTimeScopeService TimeScope { get; }

    public AppProfileRepository Profiles { get; }

    public AppCategoryService Categories { get; }

    public TimeScopeRange Range => TimeScope.Range;

    /// <summary>全站应用上下文；默认 All，切换时经 <see cref="AppFilterChanged"/> 广播。</summary>
    public AppFilterScope SelectedAppFilter
    {
        get => _selectedAppFilter;
        set
        {
            if (_selectedAppFilter == value)
            {
                return;
            }

            _selectedAppFilter = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedAppFilter)));
            AppFilterChanged?.Invoke(this, new AppFilterChangedEventArgs(value));
        }
    }

    public IReadOnlyList<AppFilterOption> GetRecentApps(
        TimeScopeRange? range = null,
        int limit = 24)
    {
        Dictionary<string, AppFilterGroup> groups = new(StringComparer.Ordinal);

        void Accumulate(DayRecord day)
        {
            foreach (AppUsage app in day.Apps.Values)
            {
                string key = AppIdentity.NormalizeKey(app.ProcessPath);
                if (key.Length == 0)
                {
                    continue;
                }

                string displayName = AppDisplayName.FriendlyName(
                    app.AppName.Length > 0 ? app.AppName : key);
                string groupKey = displayName.ToLowerInvariant();
                if (groupKey.Length == 0)
                {
                    continue;
                }

                if (!groups.TryGetValue(groupKey, out AppFilterGroup? group))
                {
                    group = new AppFilterGroup
                    {
                        DisplayName = displayName
                    };
                    groups[groupKey] = group;
                }

                group.AppKeys.Add(key);
                group.ActiveSeconds += app.ActiveSeconds;
                group.Keys += app.Keys;
                if (day.Date > group.LastSeen)
                {
                    group.LastSeen = day.Date;
                }

                // 多版本共存时优先选择仍然存在的 exe，图标提取更可靠。
                if (string.IsNullOrWhiteSpace(group.PreferredPath)
                    || (!File.Exists(group.PreferredPath) && File.Exists(app.ProcessPath)))
                {
                    group.PreferredPath = app.ProcessPath;
                }
            }
        }

        if (range is null)
        {
            foreach (DayRecord day in Loaded.Data.History.Values)
            {
                Accumulate(day);
            }
        }
        else
        {
            // 严格按当前全局时间范围切片；绝不用全历史时长排序。
            TimeScopeRange active = range.Value;
            for (DateTime date = active.Start.Date; date <= active.End.Date; date = date.AddDays(1))
            {
                string dayKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (Loaded.Data.History.TryGetValue(dayKey, out DayRecord? day))
                {
                    Accumulate(day);
                }
            }
        }

        return groups.Values
            .Select(static group =>
            {
                string[] appKeys = group.AppKeys
                    .OrderBy(static key => key, StringComparer.Ordinal)
                    .ToArray();
                string primary = AppIdentity.NormalizeKey(
                    string.IsNullOrWhiteSpace(group.PreferredPath)
                        ? appKeys[0]
                        : group.PreferredPath);
                return new AppFilterOption(
                    primary,
                    group.DisplayName,
                    group.PreferredPath ?? appKeys[0],
                    group.LastSeen,
                    group.ActiveSeconds,
                    group.Keys,
                    appKeys);
            })
            .OrderByDescending(static option => option.ActiveSeconds)
            .ThenBy(static option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private sealed class AppFilterGroup
    {
        public string DisplayName { get; set; } = "";
        public string? PreferredPath { get; set; }
        public HashSet<string> AppKeys { get; } = new(StringComparer.Ordinal);
        public double ActiveSeconds { get; set; }
        public long Keys { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>后台查询单应用快照；All 直接返回 null，不触碰 SQLite。</summary>
    public Task<AppProfileSnapshot?> QueryAppProfileAsync(
        AppFilterScope filter,
        TimeScopeRange range,
        CancellationToken cancellationToken = default)
    {
        if (filter.Kind == AppFilterKind.All)
        {
            return Task.FromResult<AppProfileSnapshot?>(null);
        }

        IEnumerable<string>? categoryKeys = filter.Kind == AppFilterKind.Category
            ? Categories.GetAppKeys(filter.CategoryKey ?? "")
            : null;
        AppProfileQuery query = AppProfileQuery.Create(
            filter,
            categoryKeys,
            range.Start,
            range.End);
        return Profiles.QueryAsync(query, cancellationToken)!;
    }

    public TimeScope SelectedTimeScope
    {
        get => TimeScope.SelectedTimeScope;
        set => TimeScope.SelectedTimeScope = value;
    }

    /// <summary>当前激活页索引；由 ShellView 维护，页面重建时沿用。</summary>
    public int ActivePageIndex
    {
        get => _activePageIndex;
        set
        {
            if (_activePageIndex == value)
            {
                return;
            }

            _activePageIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActivePageIndex)));
        }
    }

    /// <summary>日报页的前/后翻日游标；切到本周/本月时不清空，回到今日仍可继续翻看。</summary>
    public DateTime? SelectedDay
    {
        get => _selectedDay;
        set
        {
            DateTime? date = value?.Date;
            if (_selectedDay == date)
            {
                return;
            }

            _selectedDay = date;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDay)));
        }
    }

    /// <summary>
    /// Rime 日志只读一次并缓存：切换时间范围只重新切片，不重复阻塞磁盘。
    /// 重新进入字符页时可显式刷新；仅由 UI 线程触发。
    /// </summary>
    public RimeLogStats GetRimeStats(bool refresh = false)
    {
        lock (_rimeGate)
        {
            if (refresh || _rimeStats is null)
            {
                _rimeStats = RimeLog.Load();
            }

            return _rimeStats;
        }
    }

    public void RefreshTimeScope() => TimeScope.Refresh();

    public void Dispose()
    {
        TimeScope.TimeScopeChanged -= OnTimeScopeChanged;
        Profiles.Dispose();
    }

    private void OnTimeScopeChanged(object? sender, TimeScopeChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Range)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTimeScope)));
        TimeScopeChanged?.Invoke(this, e);
    }
}

public sealed class AppFilterChangedEventArgs(AppFilterScope filter) : EventArgs
{
    public AppFilterScope Filter { get; } = filter;
}

public sealed record AppFilterOption(
    string AppKey,
    string DisplayName,
    string ProcessPath,
    DateTime LastSeen,
    double ActiveSeconds = 0,
    long Keys = 0,
    IReadOnlyList<string>? AppKeys = null);

