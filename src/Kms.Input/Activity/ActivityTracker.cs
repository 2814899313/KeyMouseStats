using System.Globalization;
using Kms.Core.Model;

namespace Kms.Input.Activity;

/// <summary>
/// 把「距最后一次输入多久」积分为活跃 / 空闲区间。逐字段移植 v3
/// <c>ActivityTime.cs</c> 的 <c>ActivityTracker</c>，但不依赖全局 <c>Store</c>：
/// 日记录通过 <see cref="Func{DateTime, DayRecord}"/> 或 <see cref="IDataContext"/> 注入。
///
/// 口径：
/// <list type="bullet">
/// <item>只积分最后一次输入仍能「保证」的区间；采样间隔超过 5 秒且无法保证时
///       记为 <see cref="SessionStartReason.ObservationGap"/>，不补计未观测时间。</item>
/// <item>壁钟与单调时钟必须成对推进；两者不一致时记为
///       <see cref="SessionStartReason.ClockChange"/>，绝不跨时钟跳变桥接。</item>
/// <item>每个整点（同时也就是本地零点）切段，跨日时新的活跃段标记
///       <see cref="SessionStartReason.NewDay"/>。</item>
/// <item><see cref="ActiveGapMilliseconds"/> 是 DbWriter 的 <c>active_ms</c> 口径；
///       本类型按 v3 原样积分保证活跃的区间，由写线程在相邻输入事件上应用 15 秒上限。</item>
/// </list>
/// 仅在采样线程上使用；本类型不是线程安全的。
/// </summary>
public sealed class ActivityTracker
{
    /// <summary>两次采样间隔超过该值且不能保证活跃时，视为观测断档。</summary>
    public const double MaximumObservationGapSeconds = 5;

    /// <summary>DbWriter 的 active_ms 口径：相邻输入间隔 ≤ 15 秒才累加。</summary>
    public const long ActiveGapMilliseconds = 15_000;

    private readonly Func<DateTime, DayRecord> _getDay;

    private bool _ready;
    private SessionStartReason _nextReason = SessionStartReason.Startup;
    private DateTime _lastWall;
    private long _lastMonotonicMs;
    private double _lastIdleSeconds;
    private ActiveSession? _session;
    private DayRecord? _sessionDay;
    private ActiveSession? _idlePeriod;
    private DayRecord? _idleDay;

    /// <summary>用「日期 → 当日记录」工厂构造。工厂通常就是数据上下文的 GetOrCreate。</summary>
    public ActivityTracker(Func<DateTime, DayRecord> getDay)
    {
        ArgumentNullException.ThrowIfNull(getDay);
        _getDay = getDay;
    }

    /// <summary>用共享数据上下文构造；缺失的日记录按 <c>yyyy-MM-dd</c> 键创建并加入 <see cref="IDataContext.History"/>。</summary>
    public ActivityTracker(IDataContext data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _getDay = date => GetOrCreate(data, date);
    }

    /// <summary>最近一次采样后是否处于活跃态。</summary>
    public bool IsActive { get; private set; }

    /// <summary>当前活跃段；空闲或刚 Reset/Break 后为 null。</summary>
    public ActiveSession? CurrentSession => IsActive ? _session : null;

    /// <summary>下一段活跃开始时写入的开始原因。</summary>
    public SessionStartReason NextSessionReason => _nextReason;

    /// <summary>
    /// 每积出一段「保证活跃」的区间时回调。参数与 v3 完全一致：
    /// 日记录、活跃段、区间起点、区间终点（终点不含）。可用于应用归因。
    /// </summary>
    public Action<DayRecord, ActiveSession, DateTime, DateTime>? OnActiveInterval { get; set; }

    /// <summary>
    /// 结束当前观测并等待下一次全新采样，不桥接停止期间。
    /// 锁屏 / 休眠 / 会话断开 / 修改阈值时应使用它；下一段会带上给定原因。
    /// </summary>
    public void Reset(SessionStartReason reason = SessionStartReason.Startup)
    {
        _nextReason = reason;
        _ready = false;
        _session = null;
        _sessionDay = null;
        _idlePeriod = null;
        _idleDay = null;
        IsActive = false;
    }

    /// <summary>
    /// 立即断开当前活跃 / 空闲段，并要求下一次采样重新建立基准。
    /// 与 <see cref="Reset"/> 的差别只在语义：采样断档、时钟跳变这类
    /// 「已经知道刚刚发生什么」的场景也可以调用，区间不会回填。
    /// </summary>
    public void Break(SessionStartReason reason)
    {
        EndSegment(reason);
        _ready = false;
        IsActive = false;
    }

    /// <summary>
    /// 切换到 <paramref name="date"/> 所在日。返回该日记录；下一次采样重新建立基准，
    /// 之后的活跃段标记为 <see cref="SessionStartReason.NewDay"/>。
    /// 正常跨零点采样本身也会由 <see cref="Add"/> 精确切日，此方法供宿主在零点主动换日。
    /// </summary>
    public DayRecord RollDay(DateTime date)
    {
        DayRecord day = _getDay(date.Date);
        EndSegment(SessionStartReason.NewDay);
        _ready = false;
        IsActive = false;
        return day;
    }

    /// <summary>取得（或由工厂创建）某日记录。</summary>
    public DayRecord GetDay(DateTime date) => _getDay(date.Date);

    /// <summary>
    /// 采样一次。<paramref name="idleMilliseconds"/> 是系统「距最后一次输入」的毫秒数，
    /// <paramref name="monotonicMilliseconds"/> 是配对的单调时钟毫秒数。
    /// 返回 true 表示本次采样被接受并完成了区间积分。
    /// </summary>
    public bool Sample(DateTime wall, long monotonicMilliseconds, double idleMilliseconds, int thresholdSeconds)
    {
        if (!double.IsFinite(idleMilliseconds) || idleMilliseconds < 0)
        {
            Reset(SessionStartReason.InputUnavailable);
            return false;
        }

        double idleSeconds = idleMilliseconds / 1000.0;
        bool active = idleSeconds < thresholdSeconds;
        if (!_ready)
        {
            // 启动 / 恢复后的第一枪只建立基准，绝不回填离线期间。
            _ready = true;
            _lastWall = wall;
            _lastMonotonicMs = monotonicMilliseconds;
            _lastIdleSeconds = idleSeconds;
            IsActive = active;
            return false;
        }

        double elapsed = (monotonicMilliseconds - _lastMonotonicMs) / 1000.0;
        if (elapsed < 1 && elapsed >= 0)
        {
            // 亚秒级 UI 定时器抖动不重复积分；保留原基准，下一枪仍从原起点算。
            return false;
        }

        DateTime previousWall = _lastWall;
        double previousIdle = _lastIdleSeconds;
        _lastWall = wall;
        _lastMonotonicMs = monotonicMilliseconds;
        _lastIdleSeconds = idleSeconds;
        IsActive = active;

        double span = (wall - previousWall).TotalSeconds;

        // 壁钟与单调时钟成对采集；任一倒退或偏差超过 1 秒都视为时钟异常。
        if (elapsed <= 0 || span <= 0 || Math.Abs(span - elapsed) > 1)
        {
            EndSegment(SessionStartReason.ClockChange);
            return false;
        }

        // 延迟的 UI 定时器不一定意味着空闲：最后一次输入仍能保证整个区间活跃时，
        // 允许较长采样间隔，但只积分这段「保证活跃」的前缀。
        bool guaranteedActive = active
            && previousIdle >= 0
            && previousIdle + Math.Max(span, elapsed) <= thresholdSeconds;
        if (elapsed > MaximumObservationGapSeconds && !guaranteedActive)
        {
            EndSegment(SessionStartReason.ObservationGap);
            return false;
        }

        double prefix = Math.Min(span, Math.Max(0, thresholdSeconds - previousIdle));
        double suffixStart = active && idleSeconds < span ? Math.Max(0, span - idleSeconds) : span;
        if (suffixStart <= prefix)
        {
            Add(previousWall, wall, true);
        }
        else
        {
            DateTime prefixEnd = Boundary(previousWall, wall, prefix, span);
            DateTime suffix = Boundary(previousWall, wall, suffixStart, span);
            Add(previousWall, prefixEnd, true);
            Add(prefixEnd, suffix, false);
            Add(suffix, wall, true);
        }

        if (!active)
        {
            _session = null;
            _sessionDay = null;
            _nextReason = SessionStartReason.Idle;
        }

        return true;
    }

    /// <summary>
    /// <see cref="Sample(DateTime, long, double, int)"/> 的 <see cref="uint"/> 重载。
    /// <see cref="uint.MaxValue"/> 是 <c>GetLastInputInfo</c> 的失败哨兵，按「输入状态不可读」处理。
    /// </summary>
    public bool Sample(DateTime wall, long monotonicMilliseconds, uint idleMilliseconds, int thresholdSeconds)
    {
        if (idleMilliseconds == uint.MaxValue)
        {
            Reset(SessionStartReason.InputUnavailable);
            return false;
        }

        return Sample(wall, monotonicMilliseconds, (double)idleMilliseconds, thresholdSeconds);
    }

    /// <summary>以秒为单位的输入空闲量（v3 测试/迁移代码的写法）。</summary>
    public bool SampleSeconds(DateTime wall, long monotonicMilliseconds, double idleSeconds, int thresholdSeconds)
        => Sample(wall, monotonicMilliseconds, idleSeconds * 1000.0, thresholdSeconds);

    private void EndSegment(SessionStartReason reason)
    {
        _nextReason = reason;
        _session = null;
        _sessionDay = null;
        _idlePeriod = null;
        _idleDay = null;
    }

    private static DateTime Boundary(DateTime start, DateTime end, double offset, double span)
    {
        // 复用采样端点，避免 AddSeconds 的毫秒舍入引入亚毫秒断点。
        if (offset <= 0)
        {
            return start;
        }

        if (offset >= span)
        {
            return end;
        }

        long ticks = (long)Math.Round(offset * TimeSpan.TicksPerSecond);
        return start.AddTicks(Math.Max(0, Math.Min((end - start).Ticks, ticks)));
    }

    private void Add(DateTime start, DateTime end, bool active)
    {
        if (end <= start)
        {
            return;
        }

        if (!active)
        {
            _session = null;
            _sessionDay = null;
            _nextReason = SessionStartReason.Idle;
        }

        while (start < end)
        {
            // 每个整点切段，本地零点自然包含在内。
            DateTime boundary = start.Date.AddHours(start.Hour + 1);
            DateTime until = end < boundary ? end : boundary;
            double seconds = (until - start).TotalSeconds;
            DayRecord day = _getDay(start.Date);

            if (active)
            {
                _idlePeriod = null;
                _idleDay = null;
                if (_session is null || !ReferenceEquals(_sessionDay, day) || _session.End != start)
                {
                    SessionStartReason reason = _session is not null && !ReferenceEquals(_sessionDay, day)
                        ? SessionStartReason.NewDay
                        : _session is not null
                            ? SessionStartReason.ObservationGap
                            : _nextReason;
                    _session = new ActiveSession { Start = start, End = start, StartReason = reason };
                    _sessionDay = day;
                    day.Sessions.Add(_session);
                }

                _session.End = until;
                _session.Seconds += seconds;
                day.ActiveSeconds += seconds;
                day.ActiveHours[start.Hour] += seconds;
                OnActiveInterval?.Invoke(day, _session, start, until);
            }
            else
            {
                if (_idlePeriod is null || !ReferenceEquals(_idleDay, day) || _idlePeriod.End != start)
                {
                    _idlePeriod = new ActiveSession { Start = start, End = start };
                    _idleDay = day;
                    day.IdlePeriods.Add(_idlePeriod);
                }

                _idlePeriod.End = until;
                _idlePeriod.Seconds += seconds;
                day.IdleSeconds += seconds;
            }

            start = until;
        }
    }

    private static DayRecord GetOrCreate(IDataContext data, DateTime date)
    {
        DateTime day = date.Date;
        string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (data.History.TryGetValue(key, out DayRecord? record))
        {
            return record;
        }

        record = new DayRecord { Date = day };
        data.History[key] = record;
        return record;
    }
}

/// <summary>
/// v3 <c>AppTelemetry</c> 的应用归因部分：把已确认的活跃区间记到
/// <see cref="DayRecord.AppObservedSeconds"/> 与 <see cref="DayRecord.Apps"/>，
/// 并按前台进程变化累计 <see cref="DayRecord.AppSwitches"/>。
///
/// 规则与 v3 一致：只有前后两次快照都指向同一进程，且区间完全落在两次采样之间、
/// 两次采样相隔不超过 5 秒时，才把这段活跃时间归给该窗口；否则只增加观测秒数，
/// 不做不可靠的归因。标题切换的首次边界不算切换成功，开机 / 空闲恢复后的首次出现
/// 也不计切换。
/// 仅在采样线程上使用；本类型不是线程安全的。
/// </summary>
public sealed class ActivityAttribution
{
    /// <summary>两次快照间隔超过该值则不能可靠归因。</summary>
    public const double MaximumSnapshotGapSeconds = 5;

    /// <summary>单日窗口标题上限，与 v3 一致；超出后塌缩到溢出标题。</summary>
    public const int DefaultAppLimit = 2_000;

    /// <summary>超过当日标题上限后的归因标题。</summary>
    public const string OverflowTitle = "其他窗口（当日标题数量已达上限）";

    private readonly Func<DateTime, DayRecord> _getDay;

    private WindowInfo _previous;
    private WindowInfo _pending;
    private bool _hasPrevious;
    private bool _hasPending;
    private DateTime _previousTime;
    private DateTime _pendingTime;
    private bool _previousActive;

    public ActivityAttribution(Func<DateTime, DayRecord> getDay)
    {
        ArgumentNullException.ThrowIfNull(getDay);
        _getDay = getDay;
    }

    public ActivityAttribution(IDataContext data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _getDay = date => GetOrCreate(data, date);
    }

    /// <summary>清空前后快照，锁屏 / 休眠 / 观测中断时调用。</summary>
    public void Reset()
    {
        _previous = WindowInfo.Empty;
        _pending = WindowInfo.Empty;
        _hasPrevious = false;
        _hasPending = false;
        _previousTime = default;
        _pendingTime = default;
        _previousActive = false;
    }

    /// <summary>当前（最近一次采样）的前台窗口快照；未采样时为空。</summary>
    public WindowInfo Current => _hasPending ? _pending : _hasPrevious ? _previous : WindowInfo.Empty;

    /// <summary>在本次采样前登记前台窗口快照。</summary>
    public void Prepare(DateTime now, WindowInfo snapshot)
    {
        _pending = snapshot;
        _pendingTime = now;
        _hasPending = true;
    }

    /// <summary>
    /// 作为 <see cref="ActivityTracker.OnActiveInterval"/> 使用。
    /// 只负责应用时长与切换次数，不参与分类 / 规则 / 聚合。
    /// </summary>
    public void Interval(DayRecord day, ActiveSession session, DateTime start, DateTime end)
    {
        ArgumentNullException.ThrowIfNull(day);
        _ = session;
        double seconds = (end - start).TotalSeconds;
        if (seconds <= 0)
        {
            return;
        }

        day.AppObservedSeconds += seconds;

        if (_hasPending
            && _hasPrevious
            && _pending.ProcessPath.Length > 0
            && string.Equals(_pending.ProcessPath, _previous.ProcessPath, StringComparison.OrdinalIgnoreCase)
            && start >= _previousTime
            && end <= _pendingTime
            && (_pendingTime - _previousTime).TotalSeconds <= MaximumSnapshotGapSeconds)
        {
            AppUsage app = EnsureRecord(day, _pending);
            if (app.ProcessPath.Length > 0)
            {
                app.ActiveSeconds += seconds;
            }
        }
    }

    /// <summary>在本次采样后推进快照，并按 v3 规则统计前台切换。</summary>
    public void Finish(DateTime now, bool accepted, bool active)
    {
        if (!_hasPrevious
            || (now - _previousTime).TotalSeconds > MaximumSnapshotGapSeconds
            || now < _previousTime)
        {
            _previous = _pending;
            _hasPrevious = _hasPending;
            _previousTime = now;
            _previousActive = false;
            return;
        }

        if (!accepted)
        {
            return;
        }

        if (active
            && _previousActive
            && _hasPending
            && _pending.ProcessPath.Length > 0
            && _previous.ProcessPath.Length > 0
            && !string.Equals(_previous.ProcessPath, _pending.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            _getDay(now.Date).AppSwitches++;
        }

        _previous = _pending;
        _hasPrevious = _hasPending;
        _previousTime = now;
        _previousActive = active;
    }

    /// <summary>按 v3 <c>AppActivity.EnsureRecord</c> 取得或创建应用记录。</summary>
    public static AppUsage EnsureRecord(DayRecord day, WindowInfo snapshot)
    {
        ArgumentNullException.ThrowIfNull(day);

        string id = AppId(snapshot);
        if (day.Apps.TryGetValue(id, out AppUsage? record))
        {
            return record;
        }

        if (day.Apps.Count >= DefaultAppLimit)
        {
            snapshot = new WindowInfo("", "", OverflowTitle);
            id = AppId(snapshot);
            if (day.Apps.TryGetValue(id, out record))
            {
                return record;
            }
        }

        record = new AppUsage { ProcessPath = snapshot.ProcessPath, Title = snapshot.WindowTitle };
        day.Apps[id] = record;
        return record;
    }

    private static string AppId(WindowInfo snapshot)
        => snapshot.ProcessPath.ToLowerInvariant() + "\n" + snapshot.WindowTitle;

    private static DayRecord GetOrCreate(IDataContext data, DateTime date)
    {
        DateTime day = date.Date;
        string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (data.History.TryGetValue(key, out DayRecord? record))
        {
            return record;
        }

        record = new DayRecord { Date = day };
        data.History[key] = record;
        return record;
    }
}
