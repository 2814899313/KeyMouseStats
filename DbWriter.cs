// ============================================================================
//  键鼠统计 v3.0  Milestone 1 / Step 3  DbWriter.cs
//  专用后台写线程 + 前台窗口会话状态机 + 节律聚合 + 阴影双写门面。
//
//  分工:
//   钩子(UI 线程) ──入环──> EventRing ──Drain──> DbWriter 线程(本文件)
//   钩子只做「GetForegroundWindow + 入环」,零跨进程调用;
//   所有 GetWindowText / OpenProcess 都在这里,带 256 条 / 1500 ms 的 LRU 缓存。
//
//  设计要点:
//   · 批量事务:512 事件或 1000 ms 触发一次 RunInTransaction,WAL + synchronous=FULL
//     下单次提交约 1–5 ms,1 Hz 频率对磁盘几乎无感。
//   · 会话状态机:(process_path, window_title) 变化或空闲超时即收口上一段、开新段;
//     新段立刻以 is_open=1 落库,崩溃后可由 SelfHeal 收口。
//   · 节律:过滤 > 15 s 的思考停顿,IKI 用 Welford 在线算法求均值与方差。
//   · 全部写入都在单事务内,异常回滚,绝不半写。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
//  CLEAN_EDITION:本文件编译为 no-op 门面(清爽版不含 SQLite)。
// ============================================================================

#if !CLEAN_EDITION

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace KeyMouseStats
{
    /// <summary>Stopwatch 时刻 ↔ 墙钟毫秒 的换算锚点。</summary>
    internal sealed class EventClock
    {
        private readonly long _anchorMs;
        private readonly long _anchorTicks;
        private readonly double _ticksPerMs;

        internal EventClock()
        {
            _anchorTicks = Stopwatch.GetTimestamp();
            _anchorMs = UnixTime.NowMs();
            _ticksPerMs = Stopwatch.Frequency / 1000.0;
        }

        internal long ToMs(long ticks)
        {
            // 四舍五入而不是截断:截断会让 ms↔ticks 往返产生 ±1 ms 的量化误差,
            // 把 IKI 的 500/1000/1500 变成 501/1000/1499,节律方差随之失真。
            return _anchorMs + (long)Math.Round((ticks - _anchorTicks) / _ticksPerMs);
        }

        /// <summary>把墙钟毫秒换回 Stopwatch 时刻。测试用它构造可控时间线。</summary>
        internal long TicksForMs(long epochMs)
        {
            return _anchorTicks + (long)Math.Round((epochMs - _anchorMs) * _ticksPerMs);
        }
    }

    /// <summary>一段正在进行的前台窗口会话。</summary>
    internal sealed class LiveSession
    {
        public long Id;
        public long StartMs;
        public long EndMs;
        public string ProcessPath = "";
        public string ProcessName = "";
        public string WindowTitle = "";
        public long KeyCount;
        public long Left;
        public long Right;
        public long Middle;
        public long XClicks;
        public long Wheel;
        public double MovePx;
        /// <summary>Milestone 3 / 2.1:连续活跃时长(相邻输入间隔 ≤ ActiveGapMs 才累加,挂机不计)。</summary>
        public long ActiveMs;
        public int CategoryId = CategoryService.UnclassifiedCategory;
        public CategorySource CategorySource = CategorySource.None;
    }

    /// <summary>一段连续输入节律。</summary>
    internal sealed class LiveBurst
    {
        public long StartMs;
        public long EndMs;
        public long Count;
        public long Backspaces;
        public long Switches;
        public double Mean;
        public double M2;
        public bool HasPrevious;
        public long PreviousKeyMs;
        public string ProcessPath = "";
    }

    /// <summary>一个批次的聚合结果。字典全部复用,避免每批都 new。</summary>
    internal sealed class WriteBatch
    {
        internal string Day = "";
        internal long Keys;
        internal long Left;
        internal long Right;
        internal long Middle;
        internal long XClicks;
        internal long Wheel;
        internal double MovePx;
        internal long FirstMs;
        internal long LastMs;

        internal readonly Dictionary<int, long> HourKeys = new Dictionary<int, long>();
        internal readonly Dictionary<int, long> HourClicks = new Dictionary<int, long>();
        internal readonly Dictionary<int, long> Vk = new Dictionary<int, long>();
        internal readonly Dictionary<long, long> Cells = new Dictionary<long, long>();
        internal readonly Dictionary<string, WindowAgg> Windows = new Dictionary<string, WindowAgg>(StringComparer.Ordinal);
        internal readonly List<LiveSession> ClosedSessions = new List<LiveSession>();
        internal readonly List<LiveBurst> Bursts = new List<LiveBurst>();

        internal long Clicks { get { return Left + Right + Middle + XClicks; } }

        internal bool IsEmpty
        {
            get
            {
                return Keys == 0 && Left == 0 && Right == 0 && Middle == 0 && XClicks == 0
                    && Wheel == 0 && MovePx <= 0 && ClosedSessions.Count == 0 && Bursts.Count == 0
                    && HourKeys.Count == 0 && HourClicks.Count == 0 && Vk.Count == 0
                    && Cells.Count == 0 && Windows.Count == 0;
            }
        }

        internal void Reset(string day)
        {
            Day = day;
            Keys = 0; Left = 0; Right = 0; Middle = 0; XClicks = 0; Wheel = 0; MovePx = 0;
            FirstMs = 0; LastMs = 0;
            HourKeys.Clear(); HourClicks.Clear(); Vk.Clear(); Cells.Clear(); Windows.Clear();
            ClosedSessions.Clear(); Bursts.Clear();
        }
    }

    /// <summary>按 (日, 进程, 窗口标题) 聚合的窗口行。</summary>
    internal sealed class WindowAgg
    {
        internal string ProcessPath = "";
        internal string ProcessName = "";
        internal string Title = "";
        internal string SemanticTag = "";
        internal long Keys;
        internal long Left;
        internal long Right;
        internal long Middle;
        internal long XClicks;
        internal long Wheel;
        internal long FirstMs;
        internal long LastMs;
        internal int CategoryId = CategoryService.UnclassifiedCategory;
        internal CategorySource CategorySource = CategorySource.None;
        internal string CategoryReason = "";

        internal long Clicks { get { return Left + Right + Middle + XClicks; } }
    }

    /// <summary>写线程本体。非线程安全:只由它自己的线程(或测试线程)调用。</summary>
    internal sealed class DbWriter
    {
        /// <summary>单批事件上限。512 × 40 B ≈ 20 KB。</summary>
        internal const int BatchSize = 512;

        /// <summary>空闲多久算「离开」,收口当前会话。</summary>
        internal const long SessionIdleMs = 60000;
        /// <summary>相邻输入间隔在此之内视为「连续活跃输出段」,累加进 active_ms。</summary>
        internal const long ActiveGapMs = 15000;

        /// <summary>超过这个间隔的击键视为思考停顿,不计入节律。</summary>
        internal const long BurstGapMs = 15000;

        /// <summary>单日窗口标题上限;超出部分塌缩到 (process_key, "") 并标记 overflow。</summary>
        internal const int WindowTitleLimitPerDay = 2000;

        private readonly KmsDb _db;
        private readonly EventRing _ring;
        private readonly EventClock _clock = new EventClock();
        private readonly RawInputEvent[] _buffer = new RawInputEvent[BatchSize];
        private readonly WriteBatch _batch = new WriteBatch();
        private readonly ManualResetEvent _signal = new ManualResetEvent(false);
        private readonly HashSet<string> _dayWindowKeys = new HashSet<string>(StringComparer.Ordinal);

        private Thread _thread;
        private volatile bool _stop;
        private int _busy;
        private int _wakeArmed;
        private string _day = "";
        private LiveSession _session;
        private LiveBurst _burst;
        private long _lastEventMs;
        private long _droppedReported;

        /// <summary>窗口解析器。测试可替换为假实现,避免依赖真实窗口。</summary>
        internal Func<IntPtr, ForegroundApp.WindowInfo> ResolveWindow = ForegroundApp.ResolveWindowByHwnd;

        private DbWriter(KmsDb db, EventRing ring)
        {
            _db = db;
            _ring = ring;
        }

        internal string DatabasePath { get { return _db.Path; } }

        internal long LastEventMs { get { return _lastEventMs; } }

        /// <summary>测试用:把墙钟毫秒换成 Stopwatch 时刻,便于构造可控时间线。</summary>
        internal long TicksForMs(long epochMs)
        {
            return _clock.TicksForMs(epochMs);
        }

        internal EventRing Ring { get { return _ring; } }

        internal static DbWriter Open(string dataRoot, EventRing ring)
        {
            if (string.IsNullOrEmpty(dataRoot)) throw new KmsDbException("数据根目录为空");
            if (ring == null) throw new KmsDbException("写线程需要环形缓冲区");
            Directory.CreateDirectory(dataRoot);
            string path = Path.Combine(dataRoot, KmsSchema.DbFileName);
            KmsDb db = KmsDb.OpenWriter(path);
            DbWriter writer;
            try
            {
                KmsSchema.Ensure(db);
                writer = new DbWriter(db, ring);
                writer.SelfHeal();
            }
            catch
            {
                db.Dispose();
                throw;
            }
            return writer;
        }

        /// <summary>
        /// 崩溃/强杀自愈:扫描 is_open=1 的残留会话,把 end_ts_ms 收口到
        /// meta.last_event_ts_ms(没有就收口到当前时间),并清空 open_session_id。
        /// </summary>
        internal int SelfHeal()
        {
            long lastMs = 0;
            string raw = _db.ScalarText("SELECT v FROM meta WHERE k='last_event_ts_ms';", null);
            if (!string.IsNullOrEmpty(raw)) long.TryParse(raw, out lastMs);

            long pivot = lastMs > 0 ? lastMs : NowMs();
            int healed = _db.Execute(
                "UPDATE events_window_session SET is_open=0, " +
                "end_ts_ms = CASE WHEN start_ts_ms > ? THEN start_ts_ms ELSE ? END WHERE is_open=1;",
                new object[] { pivot, pivot });
            _db.Execute("UPDATE meta SET v='0' WHERE k='open_session_id';", null);
            if (healed > 0) AppendDiag("自愈:收口 " + healed + " 条悬空会话至 " + pivot);
            return healed;
        }

        /// <summary>
        /// 把文本快照的 mtime 记为 meta.last_export_ms。退出时调用(写线程已 Join),
        /// 这样下次启动就不会把我们自己写的 stats.txt 误判成「外部改写」。
        /// </summary>
        internal void RecordSnapshotExport(string sourceRoot)
        {
            if (string.IsNullOrEmpty(sourceRoot)) return;
            try
            {
                string path = Path.Combine(sourceRoot, "stats.txt");
                if (!File.Exists(path)) return;
                long mtime = (long)(File.GetLastWriteTimeUtc(path)
                    - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                _db.Execute("INSERT OR REPLACE INTO meta (k, v) VALUES ('last_export_ms', ?);",
                    new object[] { mtime.ToString() });
            }
            catch
            {
            }
        }

        internal void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.BelowNormal;
            _thread.Name = "kms-dbwriter";
            _thread.Start();
        }

        private void Loop()
        {
            while (!_stop)
            {
                try { _signal.WaitOne(200); }
                catch { }
                Interlocked.Exchange(ref _wakeArmed, 0);
                if (_stop) break;
                try { DrainOnce(); }
                catch (Exception error) { AppendDiag("写线程异常:" + error.GetType().Name + " · " + error.Message); }
            }
            try { DrainOnce(); }
            catch (Exception error) { AppendDiag("收尾异常:" + error.Message); }
        }

        /// <summary>把环里的待处理事件全部消费掉。返回处理条数。</summary>
        internal int DrainOnce()
        {
            Interlocked.Exchange(ref _busy, 1);
            int total = 0;
            try
            {
                for (int guard = 0; guard < 256; guard++)
                {
                    int n = _ring.Drain(_buffer, BatchSize);
                    if (n <= 0) break;
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

        private void ReportDrops()
        {
            long dropped = _ring.DroppedCount;
            if (dropped == _droppedReported) return;
            _droppedReported = dropped;
            AppendDiag("环满丢弃累计 " + dropped + " 条(写线程跟不上,计数可能偏小)");
        }

        /// <summary>信号量唤醒写线程(由 Flush/定时器调用)。重复调用合并成一次 Set。</summary>
        internal void Wake()
        {
            if (Interlocked.Exchange(ref _wakeArmed, 1) != 0) return;
            try { _signal.Set(); } catch { }
        }

        internal bool Flush(int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
            Wake();
            for (; ; )
            {
                bool idle = _ring.PendingCount <= 0 && Volatile.Read(ref _busy) == 0;
                if (idle) return true;
                if (DateTime.UtcNow > deadline) return false;
                Thread.Sleep(2);
            }
        }

        internal void Stop()
        {
            _stop = true;
            Wake();
            Thread thread = _thread;
            if (thread != null)
            {
                try { thread.Join(3000); }
                catch { }
                _thread = null;
            }
        }

        internal void Dispose()
        {
            Stop();
            try { _signal.Close(); } catch { }
            try { _db.Dispose(); } catch { }
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
                    // 跨天:先把上一天积压的写掉,再重开批。
                    if (!_batch.IsEmpty) Commit();
                    _day = day;
                    _batch.Reset(day);
                    _dayWindowKeys.Clear();
                    CloseSession(ms);
                }
                if (_batch.Day.Length == 0) _batch.Reset(day);
                ProcessOne(item, ms);
                any = true;
            }
            if (any && !_batch.IsEmpty) Commit();
        }

        private void ProcessOne(RawInputEvent item, long ms)
        {
            if (_batch.FirstMs == 0) _batch.FirstMs = ms;
            _batch.LastMs = ms;
            _lastEventMs = ms;

            bool needsWindow = item.Kind == InputEventKind.KeyDown
                || item.Kind == InputEventKind.MouseDown
                || item.Kind == InputEventKind.Wheel;

            if (needsWindow)
            {
                ForegroundApp.WindowInfo info = ResolveWindow == null
                    ? new ForegroundApp.WindowInfo()
                    : ResolveWindow(item.Hwnd);
                if (info == null) info = new ForegroundApp.WindowInfo();

                bool idle = _session != null && ms - _session.EndMs > SessionIdleMs;
                bool changed = _session != null && !SameWindow(_session, info);
                if (_session == null || idle || changed)
                {
                    if (_session != null)
                    {
                        if (_burst != null) _burst.Switches++;
                        CloseSession(ms);
                        if (_burst != null) CloseBurst(ms);
                    }
                    OpenSession(ms, info);
                }
            }
            if (_session != null)
            {
                // 2.1:先按「距上一次事件」的间隔累加活跃时长,再推进 EndMs。
                // 新开的会话 EndMs 已被 OpenSession 设为 ms,间隔为 0,不会误累加。
                long gap = ms - _session.EndMs;
                if (gap > 0 && gap <= ActiveGapMs) _session.ActiveMs += gap;
                _session.EndMs = ms;
            }

            switch (item.Kind)
            {
                case InputEventKind.KeyDown:
                    CountKeyDown(item, ms);
                    break;
                case InputEventKind.KeyUp:
                    break;
                case InputEventKind.MouseDown:
                    CountMouseDown(item, ms);
                    break;
                case InputEventKind.Wheel:
                    CountWheel(ms);
                    break;
                case InputEventKind.Move:
                    CountMove(item);
                    break;
            }
        }

        private void CountKeyDown(RawInputEvent item, long ms)
        {
            _batch.Keys++;
            if (_session != null) _session.KeyCount++;

            int hour = HourOf(ms);
            AddCounter(_batch.HourKeys, hour, 1);
            AddCounter(_batch.Vk, item.VkOrButton, 1);

            if (_burst == null)
            {
                _burst = new LiveBurst();
                _burst.StartMs = ms;
                _burst.ProcessPath = _session == null ? "" : _session.ProcessPath;
            }
            else if (_burst.HasPrevious)
            {
                long iki = ms - _burst.PreviousKeyMs;
                if (iki > BurstGapMs)
                {
                    // 思考停顿:结束当前片段,另起一段,不把这段间隔算进 IKI。
                    CloseBurst(ms);
                    _burst = new LiveBurst();
                    _burst.StartMs = ms;
                    _burst.ProcessPath = _session == null ? "" : _session.ProcessPath;
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
            if (item.VkOrButton == 0x08) _burst.Backspaces++;

            AddWindowCounters(1, 0, 0, 0, 0, 0, ms);
        }

        private void CountMouseDown(RawInputEvent item, long ms)
        {
            long left = 0, right = 0, middle = 0, extra = 0;
            switch (item.VkOrButton)
            {
                case 1: left = 1; _batch.Left++; if (_session != null) _session.Left++; break;
                case 2: right = 1; _batch.Right++; if (_session != null) _session.Right++; break;
                case 3: middle = 1; _batch.Middle++; if (_session != null) _session.Middle++; break;
                default: extra = 1; _batch.XClicks++; if (_session != null) _session.XClicks++; break;
            }

            AddCounter(_batch.HourClicks, HourOf(ms), 1);

            int cellX, cellY;
            ClickHeat.CellOf(item.X, item.Y, out cellX, out cellY);
            int channel = item.VkOrButton >= 1 && item.VkOrButton <= 3 ? item.VkOrButton : 4;
            long cellKey = ((long)channel << 32) | (uint)ClickHeat.CellKey(cellX, cellY);
            AddCounter(_batch.Cells, cellKey, 1);

            AddWindowCounters(0, left, right, middle, extra, 0, ms);
        }

        private void CountWheel(long ms)
        {
            _batch.Wheel++;
            if (_session != null) _session.Wheel++;
            AddWindowCounters(0, 0, 0, 0, 0, 1, ms);
        }

        private void CountMove(RawInputEvent item)
        {
            double distance = item.Delta;
            if (distance <= 0) return;
            _batch.MovePx += distance;
            if (_session != null) _session.MovePx += distance;
        }

        private void AddWindowCounters(long keys, long left, long right, long middle, long extra, long wheel, long ms)
        {
            if (_session == null) return;
            WindowAgg agg = EnsureWindow(_session.ProcessPath, _session.ProcessName, _session.WindowTitle, ms);
            if (agg == null) return;
            agg.Keys += keys;
            agg.Left += left;
            agg.Right += right;
            agg.Middle += middle;
            agg.XClicks += extra;
            agg.Wheel += wheel;
            if (agg.FirstMs == 0) agg.FirstMs = ms;
            agg.LastMs = ms;
        }

        private WindowAgg EnsureWindow(string path, string name, string title, long ms)
        {
            string processKey = (path == null ? "" : path).ToLowerInvariant();
            string useTitle = title == null ? "" : title;
            string overflowTag = "";

            string candidate = processKey + "\n" + useTitle;
            if (_dayWindowKeys.Count >= WindowTitleLimitPerDay && !_dayWindowKeys.Contains(candidate))
            {
                // 超限:塌缩到该进程的 overflow 桶,保留归属但不记具体标题。
                useTitle = "";
                overflowTag = "overflow";
            }

            string key = processKey + "\n" + useTitle;
            WindowAgg agg;
            if (_batch.Windows.TryGetValue(key, out agg)) return agg;

            agg = new WindowAgg();
            agg.ProcessPath = path == null ? "" : path;
            agg.ProcessName = name == null ? "" : name;
            agg.Title = useTitle;
            agg.SemanticTag = overflowTag;
            agg.FirstMs = ms;
            agg.LastMs = ms;
            CategoryResult category = ClassifyWindow(agg.ProcessPath, agg.Title);
            agg.CategoryId = category.CategoryId;
            agg.CategorySource = category.Source;
            agg.CategoryReason = category.Reason == null ? "" : category.Reason;
            _batch.Windows[key] = agg;
            _dayWindowKeys.Add(key);
            return agg;
        }

        // ---------------------------------------------------------------- 会话

        private static bool SameWindow(LiveSession session, ForegroundApp.WindowInfo info)
        {
            if (session == null || info == null) return false;
            return string.Equals(session.ProcessPath, info.ProcessPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(session.WindowTitle, info.WindowTitle, StringComparison.Ordinal);
        }

        private void OpenSession(long ms, ForegroundApp.WindowInfo info)
        {
            _session = new LiveSession();
            _session.StartMs = ms;
            _session.EndMs = ms;
            _session.ProcessPath = info == null ? "" : info.ProcessPath;
            _session.ProcessName = info == null ? "" : info.ProcessName;
            _session.WindowTitle = info == null ? "" : info.WindowTitle;
            CategoryResult category = ClassifyWindow(_session.ProcessPath, _session.WindowTitle);
            _session.CategoryId = category.CategoryId;
            _session.CategorySource = category.Source;
        }

        /// <summary>按 (进程路径, 窗口标题) 走责任链分类。每次开新会话/新窗口行只算一次。</summary>
        private static CategoryResult ClassifyWindow(string path, string title)
        {
            AppUsage probe = new AppUsage();
            probe.ProcessPath = path == null ? "" : path;
            probe.Title = title == null ? "" : title;
            return CategoryService.Classify(probe);
        }

        private void CloseSession(long ms)
        {
            if (_session == null) return;
            _session.EndMs = ms;
            _batch.ClosedSessions.Add(_session);
            _session = null;
        }

        private void CloseBurst(long ms)
        {
            if (_burst == null) return;
            _burst.EndMs = ms;
            _batch.Bursts.Add(_burst);
            _burst = null;
        }

        // ---------------------------------------------------------------- 落库

        private void Commit()
        {
            if (_batch.IsEmpty) return;
            WriteBatch batch = _batch;
            try
            {
                _db.RunInTransaction(delegate(KmsDb db)
                {
                    WriteSessions(db, batch);
                    WriteBursts(db, batch);
                    WriteDaily(db, batch);
                    db.Execute("INSERT OR REPLACE INTO meta (k, v) VALUES ('last_event_ts_ms', ?);",
                        new object[] { _lastEventMs.ToString() });
                });
            }
            finally
            {
                batch.Reset(_day);
            }
        }

        private void WriteSessions(KmsDb db, WriteBatch batch)
        {
            for (int i = 0; i < batch.ClosedSessions.Count; i++)
            {
                LiveSession closed = batch.ClosedSessions[i];
                // 已经落过库(有自增 id)的会话必须 UPDATE 收口,否则旧行会永远停在 is_open=1。
                if (closed.Id == 0) InsertSession(db, closed, 0);
                else UpdateSession(db, closed, 0);
            }

            if (_session != null)
            {
                if (_session.Id == 0)
                {
                    long id = InsertSession(db, _session, 1);
                    _session.Id = id;
                    db.Execute("INSERT OR REPLACE INTO meta (k, v) VALUES ('open_session_id', ?);",
                        new object[] { id.ToString() });
                }
                else
                {
                    UpdateSession(db, _session, 1);
                }
            }
        }

        private void UpdateSession(KmsDb db, LiveSession session, int isOpen)
        {
            db.Execute(
                "UPDATE events_window_session SET end_ts_ms=?, key_count=?, left_clicks=?, " +
                "right_clicks=?, middle_clicks=?, x_clicks=?, wheel=?, move_px=?, active_ms=?, " +
                "category_id=?, category_source=?, is_open=? WHERE id=?;",
                new object[]
                {
                    session.EndMs, session.KeyCount, session.Left, session.Right,
                    session.Middle, session.XClicks, session.Wheel, session.MovePx, session.ActiveMs,
                    session.CategoryId, CategoryService.SourceToString(session.CategorySource),
                    isOpen, session.Id
                });
        }

        private long InsertSession(KmsDb db, LiveSession session, int isOpen)
        {
            db.Execute(
                "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, process_path, process_name, " +
                "window_title, key_count, left_clicks, right_clicks, middle_clicks, x_clicks, wheel, move_px, " +
                "active_ms, category_id, category_source, is_open) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?);",
                new object[]
                {
                    session.StartMs, session.EndMs, DayOf(session.StartMs),
                    session.ProcessPath == null ? "" : session.ProcessPath,
                    session.ProcessName == null ? "" : session.ProcessName,
                    session.WindowTitle == null ? "" : session.WindowTitle,
                    session.KeyCount, session.Left, session.Right, session.Middle,
                    session.XClicks, session.Wheel, session.MovePx, session.ActiveMs,
                    session.CategoryId, CategoryService.SourceToString(session.CategorySource), isOpen
                });
            return db.LastInsertRowId();
        }

        private void WriteBursts(KmsDb db, WriteBatch batch)
        {
            for (int i = 0; i < batch.Bursts.Count; i++)
            {
                LiveBurst burst = batch.Bursts[i];
                if (burst.Count <= 0) continue;
                double avg = burst.Count > 0 ? burst.Mean : 0;
                double variance = burst.Count > 0 ? burst.M2 / burst.Count : 0;
                double rate = burst.Count + 1 > 0 ? (double)burst.Backspaces / (burst.Count + 1) : 0;
                db.Execute(
                    "INSERT INTO events_input_burst (start_ts_ms, end_ts_ms, day, key_count, iki_avg_ms, " +
                    "iki_var, iki_p50_ms, backspace_cnt, backspace_rate, switch_count, process_path, category_id) " +
                    "VALUES (?,?,?,?,?,?,?,?,?,?,?,-1);",
                    new object[]
                    {
                        burst.StartMs, burst.EndMs, DayOf(burst.StartMs), burst.Count + 1,
                        avg, variance, 0, burst.Backspaces, rate, burst.Switches,
                        burst.ProcessPath == null ? "" : burst.ProcessPath
                    });
            }
        }

        private void WriteDaily(KmsDb db, WriteBatch batch)
        {
            if (batch.Keys > 0 || batch.Clicks > 0 || batch.Wheel > 0 || batch.MovePx > 0)
            {
                int updated = db.Execute(
                    "UPDATE daily_summary SET keys=keys+?, clicks=clicks+?, left=left+?, right=right+?, " +
                    "middle=middle+?, xbuttons=xbuttons+?, wheel=wheel+?, move_px=move_px+? WHERE day=?;",
                    new object[]
                    {
                        batch.Keys, batch.Clicks, batch.Left, batch.Right, batch.Middle,
                        batch.XClicks, batch.Wheel, batch.MovePx, batch.Day
                    });
                if (updated == 0)
                {
                    db.Execute(
                        "INSERT INTO daily_summary (day, keys, clicks, left, right, middle, xbuttons, wheel, move_px, origin) " +
                        "VALUES (?,?,?,?,?,?,?,?,?, 'shadow');",
                        new object[]
                        {
                            batch.Day, batch.Keys, batch.Clicks, batch.Left, batch.Right,
                            batch.Middle, batch.XClicks, batch.Wheel, batch.MovePx
                        });
                }
            }

            foreach (KeyValuePair<int, long> pair in batch.HourKeys)
            {
                int updated = db.Execute(
                    "UPDATE daily_hour_agg SET keys=keys+? WHERE day=? AND hour=?;",
                    new object[] { pair.Value, batch.Day, pair.Key });
                if (updated == 0)
                {
                    db.Execute("INSERT INTO daily_hour_agg (day, hour, keys) VALUES (?,?,?);",
                        new object[] { batch.Day, pair.Key, pair.Value });
                }
            }

            foreach (KeyValuePair<int, long> pair in batch.HourClicks)
            {
                int updated = db.Execute(
                    "UPDATE daily_hour_agg SET clicks=clicks+? WHERE day=? AND hour=?;",
                    new object[] { pair.Value, batch.Day, pair.Key });
                if (updated == 0)
                {
                    db.Execute("INSERT INTO daily_hour_agg (day, hour, clicks) VALUES (?,?,?);",
                        new object[] { batch.Day, pair.Key, pair.Value });
                }
            }

            foreach (KeyValuePair<int, long> pair in batch.Vk)
            {
                int updated = db.Execute(
                    "UPDATE daily_key_agg SET cnt=cnt+? WHERE day=? AND vk=?;",
                    new object[] { pair.Value, batch.Day, pair.Key });
                if (updated == 0)
                {
                    db.Execute("INSERT INTO daily_key_agg (day, vk, cnt) VALUES (?,?,?);",
                        new object[] { batch.Day, pair.Key, pair.Value });
                }
            }

            foreach (KeyValuePair<long, long> pair in batch.Cells)
            {
                int channel = (int)(pair.Key >> 32);
                int cellKey = (int)(pair.Key & 0xFFFFFFFF);
                int cellX, cellY;
                ClickHeat.SplitKey(cellKey, out cellX, out cellY);
                int updated = db.Execute(
                    "UPDATE daily_click_grid SET count=count+? WHERE day=? AND channel=? AND cell_x=? AND cell_y=?;",
                    new object[] { pair.Value, batch.Day, channel, cellX, cellY });
                if (updated == 0)
                {
                    db.Execute(
                        "INSERT INTO daily_click_grid (day, channel, cell_x, cell_y, screen_id, count) VALUES (?,?,?,?,0,?);",
                        new object[] { batch.Day, channel, cellX, cellY, pair.Value });
                }
            }

            foreach (KeyValuePair<string, WindowAgg> pair in batch.Windows)
            {
                WindowAgg agg = pair.Value;
                int updated = db.Execute(
                    "UPDATE window_daily_agg SET keys=keys+?, clicks=clicks+?, wheel=wheel+?, left=left+?, " +
                    "right=right+?, middle=middle+?, xbuttons=xbuttons+?, " +
                    "first_ts_ms=CASE WHEN first_ts_ms=0 THEN ? ELSE first_ts_ms END, last_ts_ms=? " +
                    "WHERE day=? AND process_key=? AND window_title=?;",
                    new object[]
                    {
                        agg.Keys, agg.Clicks, agg.Wheel, agg.Left, agg.Right, agg.Middle, agg.XClicks,
                        agg.FirstMs, agg.LastMs, batch.Day,
                        (agg.ProcessPath == null ? "" : agg.ProcessPath).ToLowerInvariant(), agg.Title
                    });
                if (updated == 0)
                {
                    db.Execute(
                        "INSERT INTO window_daily_agg (day, process_key, window_title, process_path, process_name, " +
                        "keys, clicks, wheel, left, right, middle, xbuttons, first_ts_ms, last_ts_ms, " +
                        "category_id, category_source, semantic_tag) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?);",
                        new object[]
                        {
                            batch.Day, (agg.ProcessPath == null ? "" : agg.ProcessPath).ToLowerInvariant(),
                            agg.Title, agg.ProcessPath, agg.ProcessName, agg.Keys, agg.Clicks, agg.Wheel,
                            agg.Left, agg.Right, agg.Middle, agg.XClicks, agg.FirstMs, agg.LastMs,
                            agg.CategoryId, CategoryService.SourceToString(agg.CategorySource),
                            agg.SemanticTag == null ? "" : agg.SemanticTag
                        });
                }
            }
        }

        // ---------------------------------------------------------------- 工具

        private static void WelfordAdd(LiveBurst burst, long sample)
        {
            double x = sample;
            double delta = x - burst.Mean;
            burst.Mean += delta / burst.Count;
            burst.M2 += delta * (x - burst.Mean);
        }

        private static void AddCounter(Dictionary<int, long> map, int key, long value)
        {
            long current;
            map.TryGetValue(key, out current);
            map[key] = current + value;
        }

        private static void AddCounter(Dictionary<long, long> map, long key, long value)
        {
            long current;
            map.TryGetValue(key, out current);
            map[key] = current + value;
        }

        private int HourOf(long ms)
        {
            return ToLocal(ms).Hour;
        }

        private string DayOf(long ms)
        {
            return ToLocal(ms).ToString("yyyy-MM-dd");
        }

        private static DateTime ToLocal(long ms)
        {
            return UnixTime.ToLocal(ms);
        }

        private static long NowMs()
        {
            return UnixTime.NowMs();
        }

        private void AppendDiag(string message)
        {
            try
            {
                string root = Path.GetDirectoryName(_db.Path);
                if (string.IsNullOrEmpty(root)) return;
                string path = Path.Combine(root, "db-writer.log");
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    /// <summary>阴影双写门面。产品代码只与本类型打交道,清爽版下全是 no-op。</summary>
    internal static class ShadowPipeline
    {
        private static readonly object Gate = new object();
        private static EventRing _ring;
        private static DbWriter _writer;
        private static string _diagnosis = "未启动";

        internal static bool Available { get { return _writer != null; } }
        internal static string Diagnosis { get { return _diagnosis; } }
        internal static EventRing Ring { get { return _ring; } }
        internal static DbWriter Writer { get { return _writer; } }

        internal static bool Start()
        {
            return Start(NativeBoot.DataRoot());
        }

        internal static bool Start(string dataRoot)
        {
            lock (Gate)
            {
                if (_writer != null) return true;
                try
                {
                    // v3.0 Step 4:在任何实时写入发生之前,先把 v2 文本存档无损迁移进库。
                    // 迁移自身幂等,且失败只降级、不抛异常。
                    MigrationOutcome migration = V2Migrator.Run(Store.DataDirectory, dataRoot);

                    // 快照桥:若 stats.txt 的 mtime 晚于 meta.last_export_ms(例如用户交替
                    // 运行过清爽版),只补库中缺失的日期,绝不更新已有行。
                    string syncReason;
                    V2Migrator.SyncDerivedSnapshotIfNewer(Store.DataDirectory, dataRoot, out syncReason);

                    EventRing ring = new EventRing(EventRing.DefaultCapacity);
                    DbWriter writer = DbWriter.Open(dataRoot, ring);
                    writer.Start();
                    _ring = ring;
                    _writer = writer;
                    _diagnosis = "阴影双写已启动 · " + writer.DatabasePath
                        + " · " + migration.Describe() + " · 快照桥:" + syncReason;
                    return true;
                }
                catch (Exception error)
                {
                    _ring = null;
                    _writer = null;
                    _diagnosis = "阴影双写未启动(降级为纯文本) · " + error.GetType().Name + " · " + error.Message;
                    AppendLog(dataRoot, _diagnosis);
                    return false;
                }
            }
        }

        /// <summary>钩子热路径唯一入口。未启动时是一次字段判断,零分配。</summary>
        internal static void Enqueue(int kind, IntPtr hwnd, int vkOrButton, int x, int y, int delta, long ticks)
        {
            // 示例预览模式:绝不把假事件写进 SQLite。
            if (SampleData.Active) return;
            EventRing ring = _ring;
            if (ring == null) return;
            RawInputEvent item = new RawInputEvent();
            item.TimestampTicks = ticks;
            item.Hwnd = hwnd;
            item.Kind = kind;
            item.VkOrButton = vkOrButton;
            item.X = x;
            item.Y = y;
            item.Delta = delta;
            ring.TryEnqueue(ref item);
            if (ring.IsBacklogged) Wake();
        }

        /// <summary>主动唤醒写线程;重复调用由 DbWriter.Wake 合并。</summary>
        internal static void Wake()
        {
            DbWriter writer = _writer;
            if (writer != null) writer.Wake();
        }

        internal static bool Flush(int timeoutMs)
        {
            DbWriter writer = _writer;
            return writer == null ? true : writer.Flush(timeoutMs);
        }

        internal static void Stop()
        {
            lock (Gate)
            {
                DbWriter writer = _writer;
                if (writer != null)
                {
                    // 退出前把文本快照的 mtime 记为 last_export_ms,否则下次启动会把
                    // 我们自己的快照误判成「外部改写」而触发一次多余的桥同步。
                    try { writer.RecordSnapshotExport(Store.DataDirectory); }
                    catch { }
                }
                if (_writer != null)
                {
                    try { _writer.Dispose(); }
                    catch { }
                }
                _writer = null;
                _ring = null;
            }
        }

        /// <summary>测试用:拆掉单例,便于用独立沙盒重建。</summary>
        internal static void ResetForTest()
        {
            Stop();
            _diagnosis = "未启动";
        }

        private static void AppendLog(string dataRoot, string message)
        {
            if (string.IsNullOrEmpty(dataRoot)) return;
            try
            {
                Directory.CreateDirectory(dataRoot);
                File.AppendAllText(Path.Combine(dataRoot, "db-diag.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    /// <summary>一天的阴影双写差异。</summary>
    internal sealed class ShadowDayDiff
    {
        internal string Day = "";
        internal long DbKeys, DbClicks, DbLeft, DbRight, DbMiddle;
        internal long StoreKeys, StoreClicks, StoreLeft, StoreRight, StoreMiddle;
        internal bool FoundInDb;

        internal bool Consistent
        {
            get
            {
                return FoundInDb
                    && DbKeys == StoreKeys && DbClicks == StoreClicks
                    && DbLeft == StoreLeft && DbRight == StoreRight && DbMiddle == StoreMiddle;
            }
        }

        internal string Describe()
        {
            if (!FoundInDb) return Day + ": 库中无该日 daily_summary 行";
            return Day + " · keys " + DbKeys + "/" + StoreKeys
                + " · clicks " + DbClicks + "/" + StoreClicks
                + " · left " + DbLeft + "/" + StoreLeft
                + " · right " + DbRight + "/" + StoreRight
                + " · middle " + DbMiddle + "/" + StoreMiddle
                + (Consistent ? " · 一致" : " · **不一致**");
        }
    }

    /// <summary>
    /// 阴影双写一致性校验:逐日比对 SQLite 与 v13 文本引擎的五个核心标量。
    /// 产品里用于诊断,测试里用作 0 差异断言。
    /// </summary>
    internal static class ShadowVerify
    {
        internal static ShadowDayDiff CompareDay(KmsDb db, string day, DayRecord record)
        {
            ShadowDayDiff diff = new ShadowDayDiff();
            diff.Day = day == null ? "" : day;
            if (record != null)
            {
                diff.StoreKeys = record.Keys;
                diff.StoreClicks = record.Clicks;
                diff.StoreLeft = record.Left;
                diff.StoreRight = record.Right;
                diff.StoreMiddle = record.Middle;
            }
            if (db == null) return diff;

            try
            {
                using (KmsStmt statement = db.Prepare(
                    "SELECT keys, clicks, left, right, middle FROM daily_summary WHERE day=?;"))
                {
                    statement.Bind(new object[] { diff.Day });
                    if (statement.Step())
                    {
                        diff.FoundInDb = true;
                        diff.DbKeys = statement.ColumnLong(0);
                        diff.DbClicks = statement.ColumnLong(1);
                        diff.DbLeft = statement.ColumnLong(2);
                        diff.DbRight = statement.ColumnLong(3);
                        diff.DbMiddle = statement.ColumnLong(4);
                    }
                }
            }
            catch
            {
            }
            return diff;
        }
    }
}

#else

using System;

namespace KeyMouseStats
{
    /// <summary>
    /// 清爽版桩:不含 SQLite,阴影双写全部 no-op。
    /// 保留同名成员,让 Program.Main 与钩子代码在两种版本下都能编译。
    /// </summary>
    internal static class ShadowPipeline
    {
        internal static bool Available { get { return false; } }
        internal static string Diagnosis { get { return "清爽版不启用 SQLite 阴影双写(无数据库引擎)"; } }
        internal static EventRing Ring { get { return null; } }

        internal static bool Start() { return false; }
        internal static bool Start(string dataRoot) { return false; }

        internal static void Enqueue(int kind, IntPtr hwnd, int vkOrButton, int x, int y, int delta, long ticks)
        {
        }

        internal static void Wake() { }

        internal static bool Flush(int timeoutMs) { return true; }
        internal static void Stop() { }
        internal static void ResetForTest() { }
    }
}

#endif
