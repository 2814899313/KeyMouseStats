using System.Diagnostics;
using System.Globalization;
using Kms.Core.Time;
using Kms.Core.Input;
using Kms.Storage.Text;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Sqlite;

/// <summary>
/// 专用后台写线程 + 前台窗口会话状态机 + 节律聚合。
/// 1:1 移植自 v3 <c>DbWriter.cs</c> 的写线程部分，驱动换成 <see cref="SqliteConnection"/>。
///
/// 分工：
/// 钩子（UI 线程）──入环──&gt; <see cref="EventRing"/> ──Drain──&gt; 本类型自己的写线程。
/// 钩子只做「GetForegroundWindow + 入环」；所有窗口解析都在写线程，通过
/// <see cref="ResolveWindow"/> 注入。
///
/// 设计要点：
/// <list type="bullet">
/// <item>批量事务：512 事件或 1 s 触发一次，WAL + synchronous=FULL。</item>
/// <item>会话状态机：(process_path, window_title) 变化或空闲 60 s 即收口上一段、开新段；
///       新段立刻以 is_open=1 落库，崩溃后由 <see cref="SelfHeal"/> 收口。</item>
/// <item>节律：过滤 &gt; 15 s 的思考停顿，IKI 用 Welford 在线算法求均值与方差。</item>
/// <item>全部写入都在单事务内，异常回滚，绝不半写。</item>
/// </list>
/// </summary>
public sealed class DbWriter : IDisposable
{
    /// <summary>单批事件上限。512 × 约 40 B ≈ 20 KB。</summary>
    public const int BatchSize = 512;

    /// <summary>空闲多久算「离开」，收口当前会话。</summary>
    public const long SessionIdleMs = 60_000;

    /// <summary>相邻输入间隔在此之内视为「连续活跃输出段」，累加进 active_ms。</summary>
    public const long ActiveGapMs = 15_000;

    /// <summary>超过这个间隔的击键视为思考停顿，不计入节律。</summary>
    public const long BurstGapMs = 15_000;

    /// <summary>单日窗口标题上限；超出部分塌缩到 (process_key, "") 并标记 overflow。</summary>
    public const int WindowTitleLimitPerDay = 2_000;

    private static readonly string[] Pragmas =
    [
        "PRAGMA journal_mode = WAL;",
        "PRAGMA synchronous = FULL;",
        "PRAGMA foreign_keys = ON;",
        "PRAGMA busy_timeout = 5000;",
        "PRAGMA temp_store = MEMORY;"
    ];

    private readonly SqliteConnection _connection;
    private readonly string _databasePath;
    private readonly EventRing _ring;
    private readonly DbWriterOptions _options;
    private readonly EventClock _clock;
    private AppBucketWriter? _appBuckets;
    private readonly RawInputEvent[] _buffer = new RawInputEvent[BatchSize];
    private readonly WriteBatch _batch = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly HashSet<string> _dayWindowKeys = new(StringComparer.Ordinal);

    private Thread? _thread;
    private volatile bool _stop;
    private int _busy;
    private int _wakeArmed;
    private bool _disposed;
    private string _day = "";
    private LiveSession? _session;
    private LiveBurst? _burst;
    private long _lastEventMs;
    private long _droppedReported;

    private DbWriter(
        SqliteConnection connection,
        string databasePath,
        EventRing ring,
        DbWriterOptions options)
    {
        _connection = connection;
        _databasePath = databasePath;
        _ring = ring;
        _options = options;
        _clock = new EventClock(options.Clock);
    }

    public string DatabasePath => _databasePath;

    public long LastEventMs => _lastEventMs;

    public EventRing Ring => _ring;

    /// <summary>
    /// 前台窗口解析器。运行在写线程上，宿主可注入带缓存的实现。
    /// 为空时所有会话都落到空进程/空标题。
    /// </summary>
    public Func<IntPtr, WindowTarget?>? ResolveWindow { get; set; }

    /// <summary>
    /// 窗口分类器。为空时统一写「未分类」（category_id=4, source=none）。
    /// v3 的分类责任链尚未移植，v4 由宿主在启动时注入。
    /// </summary>
    public Func<string, string, WindowCategory>? ClassifyWindow { get; set; }

    /// <summary>测试用：把墙钟毫秒换成 Stopwatch 时刻，便于构造可控时间线。</summary>
    public long TicksForMs(long epochMs) => _clock.TicksForMs(epochMs);

    /// <summary>
    /// 打开写线程。只建连、建表、自愈，不启动线程；宿主随后调用 <see cref="Start"/>。
    /// </summary>
    /// <param name="dbPath">数据库文件路径（<c>.db</c>）；父目录不存在时会创建。</param>
    /// <param name="ring">钩子写入的环形缓冲区。</param>
    public static DbWriter Open(string dbPath, EventRing ring) => Open(dbPath, ring, null);

    /// <summary>带选项的打开重载；测试可关闭自愈或注入假时钟。</summary>
    public static DbWriter Open(string dbPath, EventRing ring, DbWriterOptions? options)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            throw new ArgumentException("数据库路径为空。", nameof(dbPath));
        }

        ArgumentNullException.ThrowIfNull(ring);
        options ??= new DbWriterOptions();

        string fullPath = Path.GetFullPath(dbPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SqliteConnection connection = new(CreateConnectionString(fullPath));
        try
        {
            connection.Open();
            ApplyPragmas(connection);
            Migration.Epoch3995Fix.Apply(connection, fullPath);
            KmsSchema.Ensure(connection, options.AppVersion);

            DbWriter writer = new(connection, fullPath, ring, options);
            writer._appBuckets = new AppBucketWriter(connection);
            if (options.RunSelfHeal)
            {
                writer.SelfHeal();
            }

            return writer;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 崩溃/强杀自愈：扫描 is_open=1 的残留会话，把 end_ts_ms 收口到
    /// meta.last_event_ts_ms（没有就收口到当前时间），并清空 open_session_id。
    /// </summary>
    /// <returns>收口的会话行数。</returns>
    public int SelfHeal()
    {
        long lastMs = 0;
        string? raw = ScalarText(
            _connection,
            null,
            "SELECT v FROM meta WHERE k='last_event_ts_ms';");

        if (!string.IsNullOrEmpty(raw))
        {
            _ = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out lastMs);
        }

        long pivot = lastMs > 0 ? lastMs : UnixTime.NowMs(_options.Clock);

        int healed = Execute(
            _connection,
            null,
            "UPDATE events_window_session SET is_open=0, " +
            "end_ts_ms = CASE WHEN start_ts_ms > $p0 THEN start_ts_ms ELSE $p0 END WHERE is_open=1;",
            pivot);

        Execute(_connection, null, "UPDATE meta SET v='0' WHERE k='open_session_id';");

        if (healed > 0)
        {
            AppendDiag("自愈:收口 " + healed.ToString(CultureInfo.InvariantCulture)
                + " 条悬空会话至 " + pivot.ToString(CultureInfo.InvariantCulture));
        }

        return healed;
    }

    /// <summary>启动后台写线程。重复调用幂等。</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            return;
        }

        Thread thread = new(Loop)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "kms-dbwriter"
        };

        _thread = thread;
        thread.Start();
    }

    /// <summary>停止写线程。重复调用幂等。</summary>
    public void Stop()
    {
        _stop = true;
        Wake();

        Thread? thread = _thread;
        if (thread is not null)
        {
            try
            {
                thread.Join(3000);
            }
            catch
            {
                // 停机路径不允许把异常抛给调用方。
            }

            _thread = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;

        try
        {
            _signal.Dispose();
        }
        catch
        {
        }

        try
        {
            _appBuckets?.Dispose();
        }
        catch
        {
        }

        try
        {
            _connection.Dispose();
        }
        catch
        {
        }
    }

    /// <summary>钩子热路径入口：未启动/环满时返回 false，绝不阻塞。</summary>
    public bool Enqueue(in RawInputEvent item)
    {
        bool accepted = _ring.TryEnqueue(in item);
        if (_ring.IsBacklogged)
        {
            Wake();
        }

        return accepted;
    }

    /// <summary>
    /// 字段形式的入队重载，参数顺序与 v3 <c>ShadowPipeline.Enqueue</c> 一致，
    /// 便于钩子代码原样迁移。
    /// </summary>
    public bool Enqueue(
        int kind,
        IntPtr hwnd,
        int vkOrButton,
        int x,
        int y,
        int delta,
        long timestampTicks)
    {
        RawInputEvent item = new(timestampTicks, hwnd, kind, vkOrButton, x, y, delta);
        return Enqueue(in item);
    }

    /// <summary>信号量唤醒写线程；重复调用合并成一次 Set。</summary>
    public void Wake()
    {
        if (Interlocked.Exchange(ref _wakeArmed, 1) != 0)
        {
            return;
        }

        try
        {
            _signal.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>等待环排空且当前批次落库。超时返回 false。</summary>
    public bool Flush(int timeoutMs)
    {
        Wake();

        double ticksPerMs = Stopwatch.Frequency / 1000.0;
        long deadline = Stopwatch.GetTimestamp()
            + (long)(Math.Max(0, timeoutMs) * ticksPerMs);

        while (true)
        {
            if (_ring.PendingCount <= 0 && Volatile.Read(ref _busy) == 0)
            {
                return true;
            }

            if (Stopwatch.GetTimestamp() > deadline)
            {
                return false;
            }

            Thread.Sleep(2);
        }
    }

    /// <summary>
    /// 把环里的待处理事件全部消费掉。返回处理条数。
    /// 运行期只由写线程调用；测试可在 <see cref="Start"/> 之前手动排空。
    /// </summary>
    public int DrainOnce()
    {
        Interlocked.Exchange(ref _busy, 1);
        int total = 0;
        try
        {
            for (int guard = 0; guard < 256; guard++)
            {
                int n = _ring.Drain(_buffer, BatchSize);
                if (n <= 0)
                {
                    break;
                }

                Apply(_buffer, n);
                total += n;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }

        ReportDrops();
        return total;
    }

    // ---------------------------------------------------------------- 线程

    private void Loop()
    {
        while (!_stop)
        {
            try
            {
                _signal.WaitOne(200);
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            Interlocked.Exchange(ref _wakeArmed, 0);
            if (_stop)
            {
                break;
            }

            try
            {
                DrainOnce();
            }
            catch (Exception error)
            {
                AppendDiag("写线程异常:" + error.GetType().Name + " · " + error.Message);
            }
        }

        try
        {
            DrainOnce();
        }
        catch (Exception error)
        {
            AppendDiag("收尾异常:" + error.Message);
        }
    }

    private void ReportDrops()
    {
        long dropped = _ring.DroppedCount;
        if (dropped == _droppedReported)
        {
            return;
        }

        _droppedReported = dropped;
        AppendDiag("环满丢弃累计 " + dropped.ToString(CultureInfo.InvariantCulture)
            + " 条(写线程跟不上,计数可能偏小)");
    }

    // ---------------------------------------------------------------- 事件处理

    private void Apply(RawInputEvent[] events, int count)
    {
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            RawInputEvent item = events[i];
            long ms = _clock.ToMs(item.TimestampTicks);
            string day = DayOf(ms);

            if (!string.Equals(_day, day, StringComparison.Ordinal))
            {
                // 跨天：先把上一天积压的写掉，再重开批。
                if (!_batch.IsEmpty)
                {
                    Commit();
                }

                _day = day;
                _batch.Reset(day);
                _dayWindowKeys.Clear();
                CloseSession(ms);
            }

            if (_batch.Day.Length == 0)
            {
                _batch.Reset(day);
            }

            ProcessOne(item, ms);
            any = true;
        }

        if (any && !_batch.IsEmpty)
        {
            Commit();
        }
    }

    private void ProcessOne(RawInputEvent item, long ms)
    {
        if (_batch.FirstMs == 0)
        {
            _batch.FirstMs = ms;
        }

        _batch.LastMs = ms;
        _lastEventMs = ms;

        bool needsWindow = item.Kind is InputEventKind.KeyDown
            or InputEventKind.MouseDown
            or InputEventKind.Wheel;
        bool appChanged = false;

        if (needsWindow)
        {
            WindowTarget info = ResolveWindow?.Invoke(item.Hwnd) ?? WindowTarget.Empty;

            bool idle = _session is not null && ms - _session.EndMs > SessionIdleMs;
            bool changed = _session is not null && !SameWindow(_session, info);
            if (_session is null || idle || changed)
            {
                if (_session is not null)
                {
                    if (_burst is not null)
                    {
                        _burst.Switches++;
                    }

                    CloseSession(ms);

                    if (_burst is not null)
                    {
                        CloseBurst(ms);
                    }
                }

                OpenSession(ms, info);
                appChanged = changed;
            }
        }

        if (_session is not null)
        {
            // 先按「距上一次事件」的间隔累加活跃时长，再推进 EndMs。
            // 新开的会话 EndMs 已被 OpenSession 设为 ms，间隔为 0，不会误累加。
            long gap = ms - _session.EndMs;
            if (gap > 0 && gap <= ActiveGapMs)
            {
                _session.ActiveMs += gap;
                _appBuckets?.AddActive(_session.AppUid, ms, gap);
            }

            _session.EndMs = ms;
        }

        if (appChanged)
        {
            _appBuckets?.RecordSwitch(_session?.AppUid ?? Kms.Core.Apps.AppIdentity.OtherAppUid, ms);
        }

        int appUid = _session?.AppUid ?? Kms.Core.Apps.AppIdentity.OtherAppUid;
        switch (item.Kind)
        {
            case InputEventKind.KeyDown:
                CountKeyDown(item, ms, appUid);
                break;
            case InputEventKind.MouseDown:
                CountMouseDown(item, ms, appUid);
                break;
            case InputEventKind.Wheel:
                CountWheel(ms);
                break;
            case InputEventKind.Move:
                CountMove(item, ms, appUid);
                break;
            default:
                // KeyUp 与未知类型不计数（v3 同口径）。
                break;
        }
    }

    private void CountKeyDown(RawInputEvent item, long ms, int appUid)
    {
        _batch.Keys++;
        if (_session is not null)
        {
            _session.KeyCount++;
        }

        int hour = HourOf(ms);
        AddCounter(_batch.HourKeys, hour, 1);
        AddCounter(_batch.Vk, item.VkOrButton, 1);
        _appBuckets?.AddKey(appUid, item.VkOrButton, ms);

        if (_burst is null)
        {
            _burst = new LiveBurst
            {
                StartMs = ms,
                ProcessPath = _session is null ? "" : _session.ProcessPath
            };
        }
        else if (_burst.HasPrevious)
        {
            long iki = ms - _burst.PreviousKeyMs;
            if (iki > BurstGapMs)
            {
                // 思考停顿：结束当前片段，另起一段，不把这段间隔算进 IKI。
                CloseBurst(ms);
                _burst = new LiveBurst
                {
                    StartMs = ms,
                    ProcessPath = _session is null ? "" : _session.ProcessPath
                };
            }
            else if (iki > 0)
            {
                _burst.Count++;
                WelfordAdd(_burst, iki);
            }
        }

        _burst.HasPrevious = true;
        _burst.PreviousKeyMs = ms;
        _burst.EndMs = ms;
        if (item.VkOrButton == 0x08)
        {
            _burst.Backspaces++;
        }

        AddWindowCounters(1, 0, 0, 0, 0, 0, ms);
    }

    private void CountMouseDown(RawInputEvent item, long ms, int appUid)
    {
        long left = 0;
        long right = 0;
        long middle = 0;
        long extra = 0;

        switch (item.VkOrButton)
        {
            case 1:
                left = 1;
                _batch.Left++;
                if (_session is not null)
                {
                    _session.Left++;
                }

                break;
            case 2:
                right = 1;
                _batch.Right++;
                if (_session is not null)
                {
                    _session.Right++;
                }

                break;
            case 3:
                middle = 1;
                _batch.Middle++;
                if (_session is not null)
                {
                    _session.Middle++;
                }

                break;
            default:
                extra = 1;
                _batch.XClicks++;
                if (_session is not null)
                {
                    _session.XClicks++;
                }

                break;
        }

        AddCounter(_batch.HourClicks, HourOf(ms), 1);

        // 屏幕坐标 → 32 px 格子；通道 1/2/3 = 左/右/中，其余键位归 4。
        int cellX = (int)Math.Floor(item.X / 32.0);
        int cellY = (int)Math.Floor(item.Y / 32.0);
        int cellKey = V13Codecs.ClickCellKey(cellX, cellY);
        int channel = item.VkOrButton is >= 1 and <= 3 ? item.VkOrButton : 4;
        long channelKey = V13Codecs.ClickChannelKey(channel, cellKey);
        AddCounter(_batch.Cells, channelKey, 1);
        _appBuckets?.AddClick(appUid, channel, cellX, cellY, ms);

        AddWindowCounters(0, left, right, middle, extra, 0, ms);
    }

    /// <summary>
    /// 滚轮只记「事件次数」。输入层的 <c>WheelAccumulator</c> 按 120 结算并跨包保留余数，
    /// 但库存口径是原始事件条数——这里绝不按 <c>Delta</c> 折算。
    /// </summary>
    private void CountWheel(long ms)
    {
        _batch.Wheel++;
        if (_session is not null)
        {
            _session.Wheel++;
        }

        AddWindowCounters(0, 0, 0, 0, 0, 1, ms);
    }

    private void CountMove(RawInputEvent item, long ms, int appUid)
    {
        double distance = item.Delta;
        if (distance <= 0)
        {
            return;
        }

        _batch.MovePx += distance;
        if (_session is not null)
        {
            _session.MovePx += distance;
        }

        _appBuckets?.AddMove(appUid, distance, ms);
    }

    private void AddWindowCounters(
        long keys,
        long left,
        long right,
        long middle,
        long extra,
        long wheel,
        long ms)
    {
        if (_session is null)
        {
            return;
        }

        WindowAgg agg = EnsureWindow(
            _session.ProcessPath,
            _session.ProcessName,
            _session.WindowTitle,
            ms);

        agg.Keys += keys;
        agg.Left += left;
        agg.Right += right;
        agg.Middle += middle;
        agg.XClicks += extra;
        agg.Wheel += wheel;

        if (agg.FirstMs == 0)
        {
            agg.FirstMs = ms;
        }

        agg.LastMs = ms;
    }

    private WindowAgg EnsureWindow(string path, string name, string title, long ms)
    {
        string processKey = (path ?? "").ToLowerInvariant();
        string useTitle = title ?? "";
        string overflowTag = "";

        string candidate = processKey + "\n" + useTitle;
        if (_dayWindowKeys.Count >= WindowTitleLimitPerDay && !_dayWindowKeys.Contains(candidate))
        {
            // 超限：塌缩到该进程的 overflow 桶，保留归属但不记具体标题。
            useTitle = "";
            overflowTag = "overflow";
        }

        string key = processKey + "\n" + useTitle;
        if (_batch.Windows.TryGetValue(key, out WindowAgg? agg))
        {
            return agg;
        }

        agg = new WindowAgg
        {
            ProcessPath = path ?? "",
            ProcessName = name ?? "",
            Title = useTitle,
            SemanticTag = overflowTag,
            FirstMs = ms,
            LastMs = ms
        };

        WindowCategory category = ClassifyWindow?.Invoke(agg.ProcessPath, agg.Title)
            ?? WindowCategory.Unclassified;
        agg.CategoryId = category.CategoryId;
        agg.CategorySource = CategorySources.Normalize(category.Source);

        _batch.Windows[key] = agg;
        _dayWindowKeys.Add(key);
        return agg;
    }

    // ---------------------------------------------------------------- 会话

    private static bool SameWindow(LiveSession session, WindowTarget info) =>
        string.Equals(session.ProcessPath, info.ProcessPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(session.WindowTitle, info.WindowTitle, StringComparison.Ordinal);

    private void OpenSession(long ms, WindowTarget info)
    {
        _session = new LiveSession
        {
            StartMs = ms,
            EndMs = ms,
            ProcessPath = info.ProcessPath ?? "",
            ProcessName = info.ProcessName ?? "",
            WindowTitle = info.WindowTitle ?? ""
        };

        WindowCategory category = ClassifyWindow?.Invoke(_session.ProcessPath, _session.WindowTitle)
            ?? WindowCategory.Unclassified;
        _session.CategoryId = category.CategoryId;
        _session.CategorySource = CategorySources.Normalize(category.Source);
        _session.AppUid = _appBuckets?.Resolve(_session.ProcessPath, _session.ProcessName, ms)
            ?? Kms.Core.Apps.AppIdentity.OtherAppUid;
    }

    private void CloseSession(long ms)
    {
        if (_session is null)
        {
            return;
        }

        _session.EndMs = ms;
        _appBuckets?.RecordSessionClosed(_session.AppUid, _session.StartMs);
        _batch.ClosedSessions.Add(_session);
        _session = null;
    }

    private void CloseBurst(long ms)
    {
        if (_burst is null)
        {
            return;
        }

        _burst.EndMs = ms;
        _batch.Bursts.Add(_burst);
        _burst = null;
    }

    // ---------------------------------------------------------------- 落库

    private void Commit()
    {
        if (_batch.IsEmpty)
        {
            return;
        }

        WriteBatch batch = _batch;
        try
        {
            using SqliteTransaction transaction = _connection.BeginTransaction();
            WriteSessions(transaction, batch);
            WriteBursts(transaction, batch);
            WriteDaily(transaction, batch);
            _appBuckets?.Flush(transaction, batch.Day);

            Execute(
                _connection,
                transaction,
                "INSERT OR REPLACE INTO meta (k, v) VALUES ('last_event_ts_ms', $p0);",
                _lastEventMs.ToString(CultureInfo.InvariantCulture));

            transaction.Commit();
        }
        finally
        {
            batch.Reset(_day);
        }
    }

    private void WriteSessions(SqliteTransaction transaction, WriteBatch batch)
    {
        for (int i = 0; i < batch.ClosedSessions.Count; i++)
        {
            LiveSession closed = batch.ClosedSessions[i];
            // 已经落过库（有自增 id）的会话必须 UPDATE 收口，否则旧行会永远停在 is_open=1。
            if (closed.Id == 0)
            {
                InsertSession(transaction, closed, 0);
            }
            else
            {
                UpdateSession(transaction, closed, 0);
            }
        }

        if (_session is not null)
        {
            if (_session.Id == 0)
            {
                long id = InsertSession(transaction, _session, 1);
                _session.Id = id;
                Execute(
                    _connection,
                    transaction,
                    "INSERT OR REPLACE INTO meta (k, v) VALUES ('open_session_id', $p0);",
                    id.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                UpdateSession(transaction, _session, 1);
            }
        }
    }

    private void UpdateSession(SqliteTransaction transaction, LiveSession session, int isOpen)
    {
        Execute(
            _connection,
            transaction,
            "UPDATE events_window_session SET end_ts_ms=$p0, key_count=$p1, left_clicks=$p2, " +
            "right_clicks=$p3, middle_clicks=$p4, x_clicks=$p5, wheel=$p6, move_px=$p7, active_ms=$p8, " +
            "category_id=$p9, category_source=$p10, is_open=$p11 WHERE id=$p12;",
            session.EndMs,
            session.KeyCount,
            session.Left,
            session.Right,
            session.Middle,
            session.XClicks,
            session.Wheel,
            session.MovePx,
            session.ActiveMs,
            session.CategoryId,
            session.CategorySource,
            isOpen,
            session.Id);
    }

    private long InsertSession(SqliteTransaction transaction, LiveSession session, int isOpen)
    {
        Execute(
            _connection,
            transaction,
            "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, process_path, process_name, " +
            "window_title, key_count, left_clicks, right_clicks, middle_clicks, x_clicks, wheel, move_px, " +
            "active_ms, category_id, category_source, is_open) " +
            "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,$p15,$p16);",
            session.StartMs,
            session.EndMs,
            DayOf(session.StartMs),
            session.ProcessPath ?? "",
            session.ProcessName ?? "",
            session.WindowTitle ?? "",
            session.KeyCount,
            session.Left,
            session.Right,
            session.Middle,
            session.XClicks,
            session.Wheel,
            session.MovePx,
            session.ActiveMs,
            session.CategoryId,
            session.CategorySource,
            isOpen);

        return ScalarLong(_connection, transaction, "SELECT last_insert_rowid();");
    }

    private void WriteBursts(SqliteTransaction transaction, WriteBatch batch)
    {
        for (int i = 0; i < batch.Bursts.Count; i++)
        {
            LiveBurst burst = batch.Bursts[i];
            if (burst.Count <= 0)
            {
                continue;
            }

            // 三个分母各自独立：key_count=Count+1、variance=M2/Count、rate=backspaces/(Count+1)。
            double avg = burst.Mean;
            double variance = burst.M2 / burst.Count;
            double rate = (double)burst.Backspaces / (burst.Count + 1);

            Execute(
                _connection,
                transaction,
                "INSERT INTO events_input_burst (start_ts_ms, end_ts_ms, day, key_count, iki_avg_ms, " +
                "iki_var, iki_p50_ms, backspace_cnt, backspace_rate, switch_count, process_path, category_id) " +
                "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,-1);",
                burst.StartMs,
                burst.EndMs,
                DayOf(burst.StartMs),
                burst.Count + 1,
                avg,
                variance,
                0,
                burst.Backspaces,
                rate,
                burst.Switches,
                burst.ProcessPath ?? "");
        }
    }

    private void WriteDaily(SqliteTransaction transaction, WriteBatch batch)
    {
        if (batch.Keys > 0 || batch.Clicks > 0 || batch.Wheel > 0 || batch.MovePx > 0)
        {
            // v3 口径：只更新计数列，绝不动 active_seconds / sessions / peak_apm / hold_*。
            int updated = Execute(
                _connection,
                transaction,
                "UPDATE daily_summary SET keys=keys+$p0, clicks=clicks+$p1, left=left+$p2, right=right+$p3, " +
                "middle=middle+$p4, xbuttons=xbuttons+$p5, wheel=wheel+$p6, move_px=move_px+$p7 WHERE day=$p8;",
                batch.Keys,
                batch.Clicks,
                batch.Left,
                batch.Right,
                batch.Middle,
                batch.XClicks,
                batch.Wheel,
                batch.MovePx,
                batch.Day);

            if (updated == 0)
            {
                Execute(
                    _connection,
                    transaction,
                    "INSERT INTO daily_summary (day, keys, clicks, left, right, middle, xbuttons, wheel, move_px, origin) " +
                    "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,'shadow');",
                    batch.Day,
                    batch.Keys,
                    batch.Clicks,
                    batch.Left,
                    batch.Right,
                    batch.Middle,
                    batch.XClicks,
                    batch.Wheel,
                    batch.MovePx);
            }
        }

        foreach (KeyValuePair<int, long> pair in batch.HourKeys)
        {
            int updated = Execute(
                _connection,
                transaction,
                "UPDATE daily_hour_agg SET keys=keys+$p0 WHERE day=$p1 AND hour=$p2;",
                pair.Value,
                batch.Day,
                pair.Key);

            if (updated == 0)
            {
                Execute(
                    _connection,
                    transaction,
                    "INSERT INTO daily_hour_agg (day, hour, keys) VALUES ($p0,$p1,$p2);",
                    batch.Day,
                    pair.Key,
                    pair.Value);
            }
        }

        foreach (KeyValuePair<int, long> pair in batch.HourClicks)
        {
            int updated = Execute(
                _connection,
                transaction,
                "UPDATE daily_hour_agg SET clicks=clicks+$p0 WHERE day=$p1 AND hour=$p2;",
                pair.Value,
                batch.Day,
                pair.Key);

            if (updated == 0)
            {
                Execute(
                    _connection,
                    transaction,
                    "INSERT INTO daily_hour_agg (day, hour, clicks) VALUES ($p0,$p1,$p2);",
                    batch.Day,
                    pair.Key,
                    pair.Value);
            }
        }

        foreach (KeyValuePair<int, long> pair in batch.Vk)
        {
            int updated = Execute(
                _connection,
                transaction,
                "UPDATE daily_key_agg SET cnt=cnt+$p0 WHERE day=$p1 AND vk=$p2;",
                pair.Value,
                batch.Day,
                pair.Key);

            if (updated == 0)
            {
                Execute(
                    _connection,
                    transaction,
                    "INSERT INTO daily_key_agg (day, vk, cnt) VALUES ($p0,$p1,$p2);",
                    batch.Day,
                    pair.Key,
                    pair.Value);
            }
        }

        foreach (KeyValuePair<long, long> pair in batch.Cells)
        {
            int channel = V13Codecs.ClickChannelOfKey(pair.Key);
            int cellKey = V13Codecs.ClickCellKeyOfChannelKey(pair.Key);
            V13Codecs.SplitClickCellKey(cellKey, out int cellX, out int cellY);

            int updated = Execute(
                _connection,
                transaction,
                "UPDATE daily_click_grid SET count=count+$p0 WHERE day=$p1 AND channel=$p2 AND cell_x=$p3 AND cell_y=$p4;",
                pair.Value,
                batch.Day,
                channel,
                cellX,
                cellY);

            if (updated == 0)
            {
                Execute(
                    _connection,
                    transaction,
                    "INSERT INTO daily_click_grid (day, channel, cell_x, cell_y, screen_id, count) " +
                    "VALUES ($p0,$p1,$p2,$p3,0,$p4);",
                    batch.Day,
                    channel,
                    cellX,
                    cellY,
                    pair.Value);
            }
        }

        foreach (KeyValuePair<string, WindowAgg> pair in batch.Windows)
        {
            WindowAgg agg = pair.Value;
            string processKey = (agg.ProcessPath ?? "").ToLowerInvariant();

            int updated = Execute(
                _connection,
                transaction,
                "UPDATE window_daily_agg SET keys=keys+$p0, clicks=clicks+$p1, wheel=wheel+$p2, left=left+$p3, " +
                "right=right+$p4, middle=middle+$p5, xbuttons=xbuttons+$p6, " +
                "first_ts_ms=CASE WHEN first_ts_ms=0 THEN $p7 ELSE first_ts_ms END, last_ts_ms=$p8 " +
                "WHERE day=$p9 AND process_key=$p10 AND window_title=$p11;",
                agg.Keys,
                agg.Clicks,
                agg.Wheel,
                agg.Left,
                agg.Right,
                agg.Middle,
                agg.XClicks,
                agg.FirstMs,
                agg.LastMs,
                batch.Day,
                processKey,
                agg.Title);

            if (updated == 0)
            {
                Execute(
                    _connection,
                    transaction,
                    "INSERT INTO window_daily_agg (day, process_key, window_title, process_path, process_name, " +
                    "keys, clicks, wheel, left, right, middle, xbuttons, first_ts_ms, last_ts_ms, " +
                    "category_id, category_source, semantic_tag) " +
                    "VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10,$p11,$p12,$p13,$p14,$p15,$p16);",
                    batch.Day,
                    processKey,
                    agg.Title,
                    agg.ProcessPath,
                    agg.ProcessName,
                    agg.Keys,
                    agg.Clicks,
                    agg.Wheel,
                    agg.Left,
                    agg.Right,
                    agg.Middle,
                    agg.XClicks,
                    agg.FirstMs,
                    agg.LastMs,
                    agg.CategoryId,
                    agg.CategorySource,
                    agg.SemanticTag ?? "");
            }
        }
    }

    // ---------------------------------------------------------------- SQL

    private static string CreateConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();

    private static void ApplyPragmas(SqliteConnection connection)
    {
        foreach (string pragma in Pragmas)
        {
            Execute(connection, null, pragma);
        }
    }

    private static int Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params object?[] values)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue(
                "$p" + i.ToString(CultureInfo.InvariantCulture),
                values[i] ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }

    private static object? Scalar(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params object?[] values)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue(
                "$p" + i.ToString(CultureInfo.InvariantCulture),
                values[i] ?? DBNull.Value);
        }

        return command.ExecuteScalar();
    }

    private static string? ScalarText(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params object?[] values) =>
        Scalar(connection, transaction, sql, values) as string;

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params object?[] values) =>
        Convert.ToInt64(
            Scalar(connection, transaction, sql, values),
            CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- 工具

    private static void WelfordAdd(LiveBurst burst, long sample)
    {
        double x = sample;
        double delta = x - burst.Mean;
        burst.Mean += delta / burst.Count;
        burst.M2 += delta * (x - burst.Mean);
    }

    private static void AddCounter<TKey>(Dictionary<TKey, long> map, TKey key, long value)
        where TKey : notnull
    {
        map.TryGetValue(key, out long current);
        map[key] = current + value;
    }

    private static int HourOf(long ms) => UnixTime.ToLocal(ms).Hour;

    private static string DayOf(long ms) =>
        UnixTime.ToLocal(ms).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void AppendDiag(string message)
    {
        Action<string>? sink = _options.Diagnostic;
        if (sink is not null)
        {
            try
            {
                sink(message);
            }
            catch
            {
            }

            return;
        }

        try
        {
            string? root = Path.GetDirectoryName(_databasePath);
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            string stamp = UnixTime
                .ToLocal(UnixTime.NowMs(_options.Clock))
                .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            File.AppendAllText(
                Path.Combine(root, "db-writer.log"),
                stamp + "  " + message + Environment.NewLine);
        }
        catch
        {
        }
    }
}
