// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 2b  FlowBoard.cs
//
//  「使用洞察」心流看板的数据层:
//    · 标准版:经 KmsDb 只读查询当日事件流 → FlowAnalyzer / ContextCost → 内存按日缓存;
//    · 清爽版:ShadowPipeline.Available == false,返回空数据 + 提示文案,绝不查库。
//
//  能力探测只认 ShadowPipeline.Available —— 这条消费链路在 Step 2a 之前是断的。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace KeyMouseStats
{
#pragma warning disable 0649   // 清爽版没有读路径,这些字段只在标准版被赋值
    internal sealed class FlowBoardData
    {
        public DateTime Day = DateTime.MinValue;
        public bool HasData;
        public FlowReport Report;
        public List<ContextSwitchStat> Costs = new List<ContextSwitchStat>();
        public List<FlowSessionRow> Sessions = new List<FlowSessionRow>();
        public List<FlowBurstRow> Bursts = new List<FlowBurstRow>();
        public long DeepMs;
        public long NormalMs;
        public long FragmentedMs;
        /// <summary>空闲挂机时长;不进入 TotalMs,也不参与碎片化惩罚。</summary>
        public long IdleMs;

        /// <summary>是否至少存在一个有效输入会话或输入突发段。</summary>
        internal static bool HasMeaningfulInput(IList<FlowSessionRow> sessions, IList<FlowBurstRow> bursts)
        {
            if (bursts != null)
            {
                for (int i = 0; i < bursts.Count; i++)
                {
                    FlowBurstRow burst = bursts[i];
                    if (burst != null && burst.KeyCount > 0) return true;
                }
            }
            if (sessions != null)
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    FlowSessionRow row = sessions[i];
                    if (row == null) continue;
                    if (row.InputCount > 0 || row.KeyCount > 0 || row.ActiveMs > 0) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 本地日期 → 当日 00:00:00 的 UTC Unix 毫秒。
        /// 事件表的 start_ts_ms / end_ts_ms 是 UTC Unix 毫秒(UnixTime),
        /// 绝不能用 DateTime.Ticks(公元 1 年起的本地刻度)直接当毫秒比,否则时间基准完全不同。
        /// </summary>
        internal static long DayStartMs(DateTime day)
        {
            DateTime local = day.Date;
            return (long)(local.ToUniversalTime()
                - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        /// <summary>当日某毫秒时刻的心流状态;无数据时返回 Idle。</summary>
        public FlowState StateAt(long ms)
        {
            if (Report != null)
            {
                List<FlowSegment> segments = Report.Segments;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (ms >= segments[i].StartMs && ms < segments[i].EndMs) return segments[i].State;
                }
            }
            return FlowState.Idle;
        }

        /// <summary>当日首条与末条非 Idle 活跃段的秒偏移;没有活跃段时返回 false。</summary>
        internal bool ActiveRange(out int firstSec, out int lastSec)
        {
            firstSec = -1;
            lastSec = -1;
            if (Report == null) return false;
            long dayStart = DayStartMs(Day);
            List<FlowSegment> segments = Report.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                FlowSegment segment = segments[i];
                if (segment.State == FlowState.Idle || segment.LengthMs <= 0) continue;
                int from = (int)((segment.StartMs - dayStart) / 1000L);
                int to = (int)((segment.EndMs - dayStart) / 1000L);
                if (from < 0) from = 0;
                if (to > TimelineWindow.DaySeconds) to = TimelineWindow.DaySeconds;
                if (to <= from) continue;
                if (firstSec < 0 || from < firstSec) firstSec = from;
                if (lastSec < 0 || to > lastSec) lastSec = to;
            }
            return firstSec >= 0 && lastSec > firstSec;
        }

        /// <summary>当日第 second 秒所在的前台会话;找不到返回 null。</summary>
        public FlowSessionRow SessionAt(int second)
        {
            long dayStart = DayStartMs(Day);
            long target = dayStart + second * 1000L;
            FlowSessionRow best = null;
            for (int i = 0; i < Sessions.Count; i++)
            {
                FlowSessionRow row = Sessions[i];
                if (target >= row.StartMs && target < row.EndMs)
                {
                    if (best == null || row.EndMs - row.StartMs > best.EndMs - best.StartMs) best = row;
                }
            }
            return best;
        }

        /// <summary>某分钟前后 60 秒的局部输入密度(键/分)。</summary>
        public double DensityAt(int second)
        {
            long dayStart = DayStartMs(Day);
            long from = dayStart + (second - 60) * 1000L;
            long to = dayStart + (second + 60) * 1000L;
            long keys = 0;
            for (int i = 0; i < Bursts.Count; i++)
            {
                FlowBurstRow burst = Bursts[i];
                long s = Math.Max(burst.StartMs, from);
                long e = Math.Min(burst.EndMs, to);
                if (e > s) keys += burst.KeyCount;
            }
            return keys / 2.0;   // 120 秒窗口 → 每分钟
        }

        /// <summary>当日总时长(毫秒),用于三态比例条。</summary>
        public long TotalMs
        {
            get { return DeepMs + NormalMs + FragmentedMs; }
        }
    }

#pragma warning restore 0649
    internal static class FlowBoard
    {
        internal const string CleanHint = "当前为清爽版(不含内置事件数据库),使用标准版可解锁深度心流与分钟级轨迹时光机";
        internal const string NoDataHint = "当日暂无可分析的事件流，继续使用后将自动生成分钟级轨迹";
#if CLEAN_EDITION
        internal const string UnavailableHint = CleanHint;
#else
        internal const string UnavailableHint = "事件数据库未就绪，请检查运行日志";
#endif

        private const int CacheLimit = 6;
        private const int TodayTtlSeconds = 30;

        private static readonly Dictionary<DateTime, FlowBoardData> Cache = new Dictionary<DateTime, FlowBoardData>();
        private static readonly Dictionary<DateTime, DateTime> Stamp = new Dictionary<DateTime, DateTime>();
        private static readonly object Gate = new object();

        /// <summary>本地日期 → 当日 00:00:00 的 UTC Unix 毫秒(转发到数据类的同一实现,避免两处漂移)。</summary>
        internal static long DayStartMs(DateTime day)
        {
            return FlowBoardData.DayStartMs(day);
        }

        /// <summary>能力探测:唯一判据是阴影写库是否真的可用。</summary>
        internal static bool Available
        {
            get
            {
                try { return ShadowPipeline.Available; }
                catch { return false; }
            }
        }

        internal static string Diagnosis
        {
            get
            {
                try { return ShadowPipeline.Diagnosis; }
                catch { return CleanHint; }
            }
        }

#if !CLEAN_EDITION
        /// <summary>事件库路径。清爽版没有 KmsSchema/KmsDb,整体不参与编译。</summary>
        internal static string DatabasePath
        {
            get { return Path.Combine(NativeBoot.DataRoot(), KmsSchema.DbFileName); }
        }
#endif

        internal static void Invalidate()
        {
            lock (Gate) { Cache.Clear(); Stamp.Clear(); }
        }

        /// <summary>取某日看板数据;无能力或读失败时返回 HasData=false 的空对象(不抛)。</summary>
        internal static FlowBoardData Get(DateTime day)
        {
            DateTime key = day.Date;
            if (!Available) return new FlowBoardData();
            lock (Gate)
            {
                FlowBoardData cached;
                if (Cache.TryGetValue(key, out cached))
                {
                    DateTime stamp;
                    bool stale = key == DateTime.Today
                        && Stamp.TryGetValue(key, out stamp)
                        && (DateTime.Now - stamp).TotalSeconds > TodayTtlSeconds;
                    if (!stale) return cached;
                }
            }
            FlowBoardData data = Load(key);
            lock (Gate)
            {
                if (Cache.Count >= CacheLimit) { Cache.Clear(); Stamp.Clear(); }
                Cache[key] = data;
                Stamp[key] = DateTime.Now;
            }
            return data;
        }

        private static FlowBoardData Load(DateTime day)
        {
            FlowBoardData data = new FlowBoardData();
            data.Day = day;
            try
            {
#if !CLEAN_EDITION
                string path = DatabasePath;
                if (!File.Exists(path)) return data;
                string dayKey = day.ToString("yyyy-MM-dd");
                using (KmsDb db = KmsDb.OpenReader(path))
                {
                    data.Sessions = db.ReadSessionsForDay(dayKey);
                    data.Bursts = db.ReadBurstsForDay(dayKey);
                }
                FlowReport report = FlowAnalyzer.Analyze(data.Sessions, data.Bursts, new FlowConfig());
                data.Report = report;
                for (int i = 0; i < report.Segments.Count; i++)
                {
                    FlowSegment segment = report.Segments[i];
                    if (segment.State == FlowState.Deep) data.DeepMs += segment.LengthMs;
                    else if (segment.State == FlowState.Normal) data.NormalMs += segment.LengthMs;
                    else if (segment.State == FlowState.Idle) data.IdleMs += segment.LengthMs;
                    else data.FragmentedMs += segment.LengthMs;
                }
                data.Costs = ContextCost.Analyze(data.Sessions, data.Bursts, new FlowConfig(), 5, 15);
                data.HasData = FlowBoardData.HasMeaningfulInput(data.Sessions, data.Bursts);
#endif
            }
            catch
            {
                data.HasData = false;
            }
            return data;
        }

        /// <summary>
        /// 把四态标签回填到 events_window_session.semantic_tag。幂等,幂等性由 SQL 的
        /// `AND semantic_tag&lt;&gt;?` 保证。返回真正改动的行数。
        /// </summary>
        internal static int TagSessions(FlowBoardData data)
        {
            if (data == null || !data.HasData || data.Report == null) return 0;
            try
            {
#if !CLEAN_EDITION
                string path = DatabasePath;
                if (!File.Exists(path)) return 0;
                Dictionary<long, string> tags = new Dictionary<long, string>();
                for (int i = 0; i < data.Sessions.Count; i++)
                {
                    FlowSessionRow row = data.Sessions[i];
                    long mid = row.StartMs + (row.EndMs - row.StartMs) / 2;
                    tags[row.Id] = TagOf(data.StateAt(mid));
                }
                return KmsDb.UpdateSessionSemanticTags(path, tags);
#else
                return 0;
#endif
            }
            catch { return 0; }
        }

        internal static string TagOf(FlowState state)
        {
            if (state == FlowState.Deep) return "deep";
            if (state == FlowState.Normal) return "normal";
            if (state == FlowState.Idle) return "idle";
            return "fragmented";
        }
    }
}
