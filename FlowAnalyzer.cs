// ============================================================================
//  KeyMouseStats v3.0 · Milestone 3 / Step 1  FlowAnalyzer.cs
//
//  心流(Deep Work)量化引擎。纯函数:不依赖 Graphics / 窗体 / 文件 / SQLite,
//  输入是普通的行对象,输出是可直接序列化验证的报告。
//
//  判定链(全部确定性,固定滑窗 + 稳定排序 + 无随机):
//    1. 归一化:过滤无效区间,同一应用且间隔 ≤ 20s(微确认容忍)的相邻会话合并;
//    2. 5 分钟滑窗 / 60s 步长,逐窗计算:粘性、类别数、工作→非工作切换、输入节律、
//       长切出、连续无关切换;
//    3. 四态:深度心流 / 常规活跃 / 碎片化高频切换 / 空闲挂机;
//    4. 连续满足 ≥ 15 分钟判为 Deep,并把不足 15 分钟的 Deep 降级为 Normal;
//    5. 专注分 0-100,四项权重分解可解释。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
#pragma warning disable 0649   // 清爽版没有 SQLite 读路径,这些只读投影字段只在完整版被赋值
    /// <summary>一条窗口会话(对应 events_window_session 的只读投影)。</summary>
    internal sealed class FlowSessionRow
    {
        public long Id;
        public long StartMs;
        public long EndMs;
        public string ProcessPath;
        public string ProcessName;
        public string WindowTitle;
        public long KeyCount;
        /// <summary>键盘 + 鼠标点击 + 滚轮的有效输入总数;旧数据缺失时回退到 KeyCount。</summary>
        public long InputCount;
        public int CategoryId;
        public long ActiveMs;

        public FlowSessionRow Copy()
        {
            FlowSessionRow row = new FlowSessionRow();
            row.Id = Id; row.StartMs = StartMs; row.EndMs = EndMs;
            row.ProcessPath = ProcessPath; row.ProcessName = ProcessName; row.WindowTitle = WindowTitle;
            row.KeyCount = KeyCount; row.InputCount = InputCount; row.CategoryId = CategoryId; row.ActiveMs = ActiveMs;
            return row;
        }
    }

    /// <summary>一条输入突发段(对应 events_input_burst 的只读投影)。</summary>
    internal sealed class FlowBurstRow
    {
        public long Id;
        public long StartMs;
        public long EndMs;
        public long KeyCount;
        public double IkiAvgMs;
        public double IkiVar;
        public double BackspaceRate;
        public long SwitchCount;
        public string ProcessPath;
        public int CategoryId;
    }

#pragma warning restore 0649
    /// <summary>四态。Idle 表示没有有效键鼠输入,不参与碎片化惩罚。</summary>
    internal enum FlowState
    {
        Fragmented = 0,
        Normal = 1,
        Deep = 2,
        Idle = 3
    }

    /// <summary>引擎参数。默认值即产品默认行为,测试可逐项覆盖。</summary>
    internal sealed class FlowConfig
    {
        /// <summary>微确认容忍度:切出 < 20s 立即切回不打断心流。</summary>
        public int MicroCheckoutSeconds = 20;
        /// <summary>单次切出超过此秒数即判中断。</summary>
        public int BreakSeconds = 45;
        /// <summary>连续切换多少个无关窗口即判中断。</summary>
        public int UnrelatedSwitchLimit = 3;
        /// <summary>主工作窗口停留占比门槛(深度心流)。</summary>
        public double StickinessThreshold = 0.72;
        /// <summary>常规专注的最低粘性;低于此值仍视为碎片切换。</summary>
        public double NormalStickinessThreshold = 0.45;
        /// <summary>窗内允许出现的不同类别数上限(IDE / 浏览器 / 终端协同)。</summary>
        public int MaxCategoriesPerWindow = 3;
        /// <summary>窗内允许的工作→非工作切换次数上限。</summary>
        public int MaxWorkToNonWork = 2;
        public int WindowSeconds = 300;
        public int StepSeconds = 60;
        /// <summary>连续满足判据达到此分钟数才算深度心流。</summary>
        public int DeepMinMinutes = 10;
        /// <summary>5 分钟窗内最少有效输入(键盘 + 鼠标点击 + 滚轮),用于兼顾调试/阅览/操作。</summary>
        public long MinInputPerWindow = 15;
        /// <summary>IKI 方差绝对下限,用于排除机械连点。</summary>
        public double IkiVarMin = 40;
        /// <summary>IKI 方差绝对上限;仅作极端值保险,节奏主要由变异系数判定。</summary>
        public double IkiVarMax = 50000000;
        /// <summary>IKI 变异系数区间:容忍思考停顿,同时排除固定间隔连点。</summary>
        public double IkiCvMin = 0.03;
        public double IkiCvMax = 6.0;
        /// <summary>专注分里「平静度」项的计算上限(每次无关切换扣分)。</summary>
        public int ScoreSwitchCap = 12;
    }

    /// <summary>一段同态时间。</summary>
    internal sealed class FlowSegment
    {
        public long StartMs;
        public long EndMs;
        public FlowState State;
        public int PrimaryCategory;
        public string PrimaryProcess;
        public double Stickiness;
        public long Keys;
        /// <summary>最终分钟段的每分钟击键数;仅 Analyze 输出的分钟段非空。</summary>
        public List<long> MinuteKeys;
        public long LengthMs { get { return EndMs > StartMs ? EndMs - StartMs : 0; } }
    }

    /// <summary>专注分的四项权重分解(每项 0..1)。</summary>
    internal sealed class FlowScoreParts
    {
        public double DeepShare;
        public double NormalShare;
        public double Stickiness;
        public double Calm;
        /// <summary>质量项 = 深度占比 + 0.5 × 常规占比(0..1)。</summary>
        public double Quality;
    }

    internal sealed class FlowReport
    {
        public List<FlowSegment> Segments = new List<FlowSegment>();
        public List<FlowSessionRow> Normalized = new List<FlowSessionRow>();
        public FlowScoreParts Parts = new FlowScoreParts();
        public double FocusScore;
        public long LongestDeepMs;
        /// <summary>深度心流 + 常规专注的最长连续时长。</summary>
        public long LongestFocusMs;
        public long TotalMs;
        /// <summary>空闲段总时长;不计入三态分母与碎片化惩罚。</summary>
        public long IdleMs;
    }

    /// <summary>某个分钟背后的 5 分钟窗口诊断,供悬停提示使用(纯内存)。</summary>
    internal sealed class FlowDiagnostic
    {
        public long WindowStartMs;
        public FlowState State;
        public double Stickiness;
        public long Keys;
        public long InputCount;
        public long EffectiveInput;
        public int BurstCount;
        public double IkiAvg;
        public double IkiVar;
        public double IkiCv;
        public int DistinctCategories;
        public int WorkToNonWork;
        public int LongCheckouts;
        public int MaxUnrelatedRun;
        public int PrimaryCategory;
        public string PrimaryProcess;
    }

    internal static class FlowAnalyzer
    {
        /// <summary>主力工作类:0 编程 / 5 办公 / 6 科研 / 7 写作。</summary>
        internal static bool IsWorkCategory(int categoryId)
        {
            return categoryId == 0 || categoryId == 5 || categoryId == 6 || categoryId == 7;
        }

        internal static FlowReport Analyze(IList<FlowSessionRow> sessions, IList<FlowBurstRow> bursts, FlowConfig config)
        {
            FlowConfig cfg = config == null ? new FlowConfig() : config;
            FlowReport report = new FlowReport();
            List<FlowSessionRow> normalized = Normalize(sessions, cfg);
            report.Normalized = normalized;
            if (normalized.Count == 0) return report;

            long origin = normalized[0].StartMs;
            long end = normalized[normalized.Count - 1].EndMs;
            report.TotalMs = end > origin ? end - origin : 0;

            List<FlowSegment> windows = new List<FlowSegment>();
            double stickinessSum = 0;
            int stickinessCount = 0;
            long stepMs = Math.Max(1, cfg.StepSeconds) * 1000L;
            long windowsMs = Math.Max(stepMs, cfg.WindowSeconds * 1000L);

            for (long w0 = origin; w0 < end; w0 += stepMs)
            {
                long w1 = w0 + windowsMs;
                WindowStat stat = Measure(normalized, bursts, w0, w1, cfg);
                // 空窗(没有任何会话重叠、也没有输入)不是「碎片化」,它是没在电脑前。
                // 用原始停留判断窗口是否存在;有效停留只负责粘性与主类别核算。
                if (stat.RawDwell == 0 && stat.BurstCount == 0) continue;
                FlowState state = Classify(stat, cfg);
                FlowSegment segment = new FlowSegment();
                segment.StartMs = w0;
                segment.EndMs = Math.Min(w1, end);
                segment.State = state;
                segment.PrimaryCategory = stat.PrimaryCategory;
                segment.PrimaryProcess = stat.PrimaryProcess;
                segment.Stickiness = stat.Stickiness;
                segment.Keys = stat.Keys;
                windows.Add(segment);
                // 挂机窗口没有切换惩罚,也不应把粘性均值拉低。
                if (state != FlowState.Idle)
                {
                    stickinessSum += stat.Stickiness;
                    stickinessCount++;
                }
            }

            // 分钟桶单次归属:每个 60 秒桶只能被计入一次,彻底消除 300/60 窗口重叠重复累加。
            List<FlowSegment> merged = BuildMinuteSegments(normalized, bursts, windows, end);
            // Deep 不足 15 分钟 → 降级为 Normal
            long deepMinMs = Math.Max(1, cfg.DeepMinMinutes) * 60000L;
            for (int i = 0; i < merged.Count; i++)
                if (merged[i].State == FlowState.Deep && merged[i].LengthMs < deepMinMs)
                    merged[i].State = FlowState.Normal;
            merged = MergeWindows(merged);

            report.Segments = merged;
            ComputeScore(report, merged, stickinessCount > 0 ? stickinessSum / stickinessCount : 0, cfg);
            return report;
        }

        /// <summary>归一化:排序 + 过滤 + 同应用微确认合并。</summary>
        internal static List<FlowSessionRow> Normalize(IList<FlowSessionRow> sessions, FlowConfig config)
        {
            FlowConfig cfg = config == null ? new FlowConfig() : config;
            List<FlowSessionRow> list = new List<FlowSessionRow>();
            if (sessions != null)
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    FlowSessionRow row = sessions[i];
                    if (row == null || row.EndMs <= row.StartMs) continue;
                    list.Add(row.Copy());
                }
            }
            // 稳定排序:同起点按 Id,保证同一份数据永远得到同一结果。
            list.Sort(delegate(FlowSessionRow a, FlowSessionRow b)
            {
                int byStart = a.StartMs.CompareTo(b.StartMs);
                if (byStart != 0) return byStart;
                return a.Id.CompareTo(b.Id);
            });

            long tolerance = Math.Max(0, cfg.MicroCheckoutSeconds) * 1000L;
            List<FlowSessionRow> merged = new List<FlowSessionRow>();
            for (int i = 0; i < list.Count; i++)
            {
                FlowSessionRow current = list[i];
                if (merged.Count > 0)
                {
                    FlowSessionRow last = merged[merged.Count - 1];
                    long gap = current.StartMs - last.EndMs;
                    bool sameApp = string.Equals(last.ProcessPath ?? "", current.ProcessPath ?? "", StringComparison.Ordinal);
                    if (sameApp && gap <= tolerance)
                    {
                        if (current.EndMs > last.EndMs) last.EndMs = current.EndMs;
                        last.KeyCount += current.KeyCount;
                        last.InputCount += current.InputCount;
                        last.ActiveMs += current.ActiveMs;
                        continue;
                    }
                }
                merged.Add(current);
            }
            return merged;
        }

        private sealed class WindowStat
        {
            public double Stickiness;
            public int PrimaryCategory = -1;
            public string PrimaryProcess = "";
            public int DistinctCategories;
            public int WorkToNonWork;
            public int LongCheckouts;
            public int MaxUnrelatedRun;
            public long Keys;
            public long RawDwell;      // 窗内原始会话停留;用于判断窗口是否存在于前台
            public long Dwell;          // 按 ActiveMs 约束后的有效停留;用于粘性与主类别
            public long InputCount;     // 窗内有效键鼠输入(键盘 + 点击 + 滚轮)
            public long EffectiveInput; // max(Keys, InputCount),用于最少输入阈值
            public int BurstCount;
            public double IkiAvg;
            public double IkiVar;
            public double IkiCv;
        }

        /// <summary>
        /// 原始前台停留 → 有效停留。用 ActiveMs / 会话跨度做比例约束,
        /// 让 93 分钟但只有 3.6 秒输入的挂机会话不再霸占整天 Dwell。
        /// </summary>
        private static long EffectiveDwell(FlowSessionRow row, long rawDwell)
        {
            if (rawDwell <= 0) return 0;
            long span = row.EndMs - row.StartMs;
            if (span <= 0) return rawDwell;
            double ratio;
            if (row.ActiveMs > 0) ratio = row.ActiveMs * 1.0 / span;
            else if (row.InputCount <= 0 && row.KeyCount <= 0) ratio = 0;
            else ratio = 1.0;   // 旧数据没有 active_ms,但确实有输入 → 不惩罚
            if (ratio <= 0) return 0;
            if (ratio > 1) ratio = 1;
            long effective = (long)Math.Round(rawDwell * ratio);
            return effective < 0 ? 0 : effective;
        }

        /// <summary>会话输入总量(键盘 + 点击 + 滚轮;旧行回退 KeyCount)按窗口重叠比例折算。</summary>
        private static long ProratedInput(FlowSessionRow row, long rawDwell)
        {
            long input = row.InputCount > 0 ? row.InputCount : row.KeyCount;
            if (input <= 0 || rawDwell <= 0) return 0;
            long span = row.EndMs - row.StartMs;
            if (span <= 0) return input;
            // 向下取整:长时间会话里零星几次输入不能被摊成「每个窗口都有 1 次」。
            return (long)(input * (double)rawDwell / span);
        }

        private static WindowStat Measure(IList<FlowSessionRow> sessions, IList<FlowBurstRow> bursts,
            long w0, long w1, FlowConfig cfg)
        {
            WindowStat stat = new WindowStat();
            Dictionary<string, long> dwellByProcess = new Dictionary<string, long>(StringComparer.Ordinal);
            Dictionary<int, long> dwellByCategory = new Dictionary<int, long>();
            long totalDwell = 0;
            long previousEnd = -1;
            int previousCategory = -1;
            int unrelatedRun = 0;

            for (int i = 0; i < sessions.Count; i++)
            {
                FlowSessionRow row = sessions[i];
                long from = Math.Max(row.StartMs, w0);
                long to = Math.Min(row.EndMs, w1);
                if (to <= from) continue;
                long rawDwell = to - from;
                long dwell = EffectiveDwell(row, rawDwell);
                stat.RawDwell += rawDwell;
                stat.InputCount += ProratedInput(row, rawDwell);
                totalDwell += dwell;

                // 有效停留不足 1 秒的会话视为挂机背景:不参与主类别、
                // 类别数、工作→非工作和连续无关切换的判定。
                bool structural = dwell >= 1000;
                int category = row.CategoryId;
                if (structural)
                {
                    string key = row.ProcessPath == null ? "" : row.ProcessPath;
                    long processDwell;
                    dwellByProcess.TryGetValue(key, out processDwell);
                    dwellByProcess[key] = processDwell + dwell;

                    long categoryDwell;
                    dwellByCategory.TryGetValue(category, out categoryDwell);
                    dwellByCategory[category] = categoryDwell + dwell;

                    if (previousCategory >= 0 && previousCategory != category)
                        if (IsWorkCategory(previousCategory) && !IsWorkCategory(category)) stat.WorkToNonWork++;

                    if (!IsWorkCategory(category))
                    {
                        unrelatedRun++;
                        if (unrelatedRun > stat.MaxUnrelatedRun) stat.MaxUnrelatedRun = unrelatedRun;
                    }
                    else unrelatedRun = 0;
                }

                if (previousEnd >= 0 && row.StartMs - previousEnd > cfg.BreakSeconds * 1000L) stat.LongCheckouts++;
                previousEnd = Math.Max(previousEnd, row.EndMs);
                if (structural) previousCategory = category;
            }

            stat.DistinctCategories = dwellByCategory.Count;
            stat.Dwell = totalDwell;
            if (totalDwell > 0)
            {
                foreach (KeyValuePair<string, long> pair in dwellByProcess)
                {
                    if (pair.Value * 1.0 / totalDwell > stat.Stickiness)
                    {
                        stat.Stickiness = pair.Value * 1.0 / totalDwell;
                        stat.PrimaryProcess = pair.Key;
                    }
                }
            }
            // 主类别取停留最久的那一类。
            long bestCategoryDwell = -1;
            foreach (KeyValuePair<int, long> pair in dwellByCategory)
                if (pair.Value > bestCategoryDwell) { bestCategoryDwell = pair.Value; stat.PrimaryCategory = pair.Key; }

            if (bursts != null)
            {
                double varSum = 0;
                double avgSum = 0;
                for (int i = 0; i < bursts.Count; i++)
                {
                    FlowBurstRow burst = bursts[i];
                    if (burst == null) continue;
                    long from = Math.Max(burst.StartMs, w0);
                    long to = Math.Min(burst.EndMs, w1);
                    if (to <= from) continue;
                    stat.BurstCount++;
                    stat.Keys += burst.KeyCount;
                    varSum += burst.IkiVar;
                    avgSum += burst.IkiAvgMs;
                }
                if (stat.BurstCount > 0)
                {
                    stat.IkiVar = varSum / stat.BurstCount;
                    stat.IkiAvg = avgSum / stat.BurstCount;
                    stat.IkiCv = stat.IkiAvg > 0
                        ? Math.Sqrt(Math.Max(0.0, stat.IkiVar)) / stat.IkiAvg
                        : 0;
                }
            }
            // 没有突发段时用会话击键数按窗口占比折算,避免整窗判为 0。
            if (stat.BurstCount == 0)
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    FlowSessionRow row = sessions[i];
                    long span = row.EndMs - row.StartMs;
                    if (span <= 0) continue;
                    long from = Math.Max(row.StartMs, w0);
                    long to = Math.Min(row.EndMs, w1);
                    if (to <= from) continue;
                    stat.Keys += (long)Math.Round(row.KeyCount * (to - from) * 1.0 / span);
                }
            }
            stat.EffectiveInput = stat.Keys > stat.InputCount ? stat.Keys : stat.InputCount;
            return stat;
        }

        private static FlowState Classify(WindowStat stat, FlowConfig cfg)
        {
            // 没有输入突发,且键盘/鼠标有效输入为 0:这是挂机,不是高频切换。
            if (stat.BurstCount == 0 && stat.EffectiveInput == 0) return FlowState.Idle;
            bool rhythm = stat.EffectiveInput >= cfg.MinInputPerWindow && RhythmOk(stat, cfg);
            bool qualified = stat.Stickiness >= cfg.StickinessThreshold
                && stat.DistinctCategories <= cfg.MaxCategoriesPerWindow
                && stat.WorkToNonWork <= cfg.MaxWorkToNonWork
                && stat.LongCheckouts == 0
                && stat.MaxUnrelatedRun < cfg.UnrelatedSwitchLimit
                && rhythm;
            if (qualified) return FlowState.Deep;
            if (rhythm && stat.Stickiness >= cfg.NormalStickinessThreshold) return FlowState.Normal;
            return FlowState.Fragmented;
        }

        /// <summary>输入节律:用变异系数容忍思考停顿,并保留极端方差保险。</summary>
        private static bool RhythmOk(WindowStat stat, FlowConfig cfg)
        {
            if (stat.BurstCount == 0) return true;   // 纯鼠标/阅览窗口没有 IKI 样本
            if (stat.IkiVar < cfg.IkiVarMin || stat.IkiVar > cfg.IkiVarMax) return false;
            if (stat.IkiAvg <= 0) return true;
            return stat.IkiCv >= cfg.IkiCvMin && stat.IkiCv <= cfg.IkiCvMax;
        }

        /// <summary>悬停诊断:按与 Analyze 相同的窗口起点复算某分钟的 5 分钟窗口。</summary>
        internal static FlowDiagnostic DiagnoseAt(IList<FlowSessionRow> sessions, IList<FlowBurstRow> bursts,
            long atMs, FlowConfig config)
        {
            FlowConfig cfg = config == null ? new FlowConfig() : config;
            return DiagnoseNormalized(Normalize(sessions, cfg), bursts, atMs, cfg);
        }

        /// <summary>已归一化列表上的悬停诊断,避免每次悬停重复排序。</summary>
        internal static FlowDiagnostic DiagnoseNormalized(IList<FlowSessionRow> normalized,
            IList<FlowBurstRow> bursts, long atMs, FlowConfig config)
        {
            if (normalized == null || normalized.Count == 0) return null;
            FlowConfig cfg = config == null ? new FlowConfig() : config;
            long origin = normalized[0].StartMs;
            long end = normalized[normalized.Count - 1].EndMs;
            if (atMs < origin || atMs >= end) return null;
            long stepMs = Math.Max(1, cfg.StepSeconds) * 1000L;
            long windowsMs = Math.Max(stepMs, cfg.WindowSeconds * 1000L);
            long w0 = origin + ((atMs - origin) / stepMs) * stepMs;
            WindowStat stat = Measure(normalized, bursts, w0, w0 + windowsMs, cfg);
            FlowDiagnostic diag = new FlowDiagnostic();
            diag.WindowStartMs = w0;
            diag.State = Classify(stat, cfg);
            diag.Stickiness = stat.Stickiness;
            diag.Keys = stat.Keys;
            diag.InputCount = stat.InputCount;
            diag.EffectiveInput = stat.EffectiveInput;
            diag.BurstCount = stat.BurstCount;
            diag.IkiAvg = stat.IkiAvg;
            diag.IkiVar = stat.IkiVar;
            diag.IkiCv = stat.IkiCv;
            diag.DistinctCategories = stat.DistinctCategories;
            diag.WorkToNonWork = stat.WorkToNonWork;
            diag.LongCheckouts = stat.LongCheckouts;
            diag.MaxUnrelatedRun = stat.MaxUnrelatedRun;
            diag.PrimaryCategory = stat.PrimaryCategory;
            diag.PrimaryProcess = stat.PrimaryProcess;
            return diag;
        }

        /// <summary>
        /// 把 5 分钟滑窗的判别结果落到 60 秒分钟桶上,每个桶只计一次。
        /// 保证四态总时长严格等于「有实际活动的分钟」总时长,
        /// 不会因为相邻窗口重叠而把同一分钟算 5 次。
        /// </summary>
        private static List<FlowSegment> BuildMinuteSegments(IList<FlowSessionRow> sessions,
            IList<FlowBurstRow> bursts, List<FlowSegment> windows, long end)
        {
            List<FlowSegment> result = new List<FlowSegment>();
            if (windows.Count == 0) return result;
            const long minuteMs = 60000L;
            long from = (windows[0].StartMs / minuteMs) * minuteMs;
            FlowSegment current = null;
            for (long t = from; t < end; t += minuteMs)
            {
                long tEnd = Math.Min(t + minuteMs, end);
                if (!HasActivity(sessions, bursts, t, tEnd)) { current = null; continue; }
                int index = WindowIndex(windows, t);
                FlowSegment window = windows[index];
                long minuteKeys = BurstKeysInRange(bursts, t, tEnd);
                if (current != null && current.State == window.State)
                {
                    current.EndMs = tEnd;
                    current.Keys += minuteKeys;
                    if (current.MinuteKeys == null) current.MinuteKeys = new List<long>();
                    current.MinuteKeys.Add(minuteKeys);
                    continue;
                }
                current = new FlowSegment();
                current.StartMs = t;
                current.EndMs = tEnd;
                current.State = window.State;
                current.PrimaryCategory = window.PrimaryCategory;
                current.PrimaryProcess = window.PrimaryProcess;
                current.Stickiness = window.Stickiness;
                current.Keys = minuteKeys;
                current.MinuteKeys = new List<long>();
                current.MinuteKeys.Add(minuteKeys);
                result.Add(current);
            }
            return result;
        }

        /// <summary>按 burst 与分钟桶的重叠比例折算击键数,供轨道脉冲渲染。</summary>
        private static long BurstKeysInRange(IList<FlowBurstRow> bursts, long from, long to)
        {
            if (bursts == null || to <= from) return 0;
            long keys = 0;
            for (int i = 0; i < bursts.Count; i++)
            {
                FlowBurstRow burst = bursts[i];
                if (burst == null) continue;
                long a = Math.Max(burst.StartMs, from);
                long b = Math.Min(burst.EndMs, to);
                if (b <= a) continue;
                long span = burst.EndMs - burst.StartMs;
                if (span <= 0) { keys += burst.KeyCount; continue; }
                keys += (long)Math.Round(burst.KeyCount * (double)(b - a) / span);
            }
            return keys;
        }

        /// <summary>该分钟桶内是否有真实会话停留或输入事件。</summary>
        private static bool HasActivity(IList<FlowSessionRow> sessions, IList<FlowBurstRow> bursts, long from, long to)
        {
            for (int i = 0; i < sessions.Count; i++)
            {
                FlowSessionRow row = sessions[i];
                if (Math.Min(row.EndMs, to) > Math.Max(row.StartMs, from)) return true;
            }
            if (bursts != null)
            {
                for (int i = 0; i < bursts.Count; i++)
                {
                    FlowBurstRow burst = bursts[i];
                    if (burst == null) continue;
                    if (Math.Min(burst.EndMs, to) > Math.Max(burst.StartMs, from)) return true;
                }
            }
            return false;
        }

        /// <summary>覆盖该时刻的最后一个滑窗(窗口按起点升序)。</summary>
        private static int WindowIndex(List<FlowSegment> windows, long t)
        {
            int index = 0;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i].StartMs <= t) index = i;
                else break;
            }
            return index;
        }

        /// <summary>把相邻同态窗口合并成段。</summary>
        private static List<FlowSegment> MergeWindows(List<FlowSegment> windows)
        {
            List<FlowSegment> result = new List<FlowSegment>();
            for (int i = 0; i < windows.Count; i++)
            {
                FlowSegment window = windows[i];
                if (result.Count > 0)
                {
                    FlowSegment last = result[result.Count - 1];
                    // 只有时间连续（无空洞）的同态段才允许合并,避免跨数小时空白桥接。
                    if (last.State == window.State && last.EndMs >= window.StartMs)
                    {
                        last.EndMs = window.EndMs;
                        last.Keys += window.Keys;
                        if (window.MinuteKeys != null)
                        {
                            if (last.MinuteKeys == null) last.MinuteKeys = new List<long>();
                            last.MinuteKeys.AddRange(window.MinuteKeys);
                        }
                        if (window.Stickiness > last.Stickiness)
                        {
                            last.Stickiness = window.Stickiness;
                            last.PrimaryCategory = window.PrimaryCategory;
                            last.PrimaryProcess = window.PrimaryProcess;
                        }
                        continue;
                    }
                }
                FlowSegment copy = new FlowSegment();
                copy.StartMs = window.StartMs;
                copy.EndMs = window.EndMs;
                copy.State = window.State;
                copy.PrimaryCategory = window.PrimaryCategory;
                copy.PrimaryProcess = window.PrimaryProcess;
                copy.Stickiness = window.Stickiness;
                copy.Keys = window.Keys;
                if (window.MinuteKeys != null) copy.MinuteKeys = new List<long>(window.MinuteKeys);
                result.Add(copy);
            }
            return result;
        }

        /// <summary>深度心流 + 常规专注的最长连续时长;Fragmented / Idle / 空洞会切断。</summary>
        internal static long LongestFocusRun(List<FlowSegment> segments)
        {
            if (segments == null) return 0;
            long best = 0;
            long run = 0;
            long previousEnd = -1;
            for (int i = 0; i < segments.Count; i++)
            {
                FlowSegment segment = segments[i];
                bool focus = segment.State == FlowState.Deep || segment.State == FlowState.Normal;
                if (focus)
                {
                    if (previousEnd >= 0 && segment.StartMs <= previousEnd) run += segment.LengthMs;
                    else run = segment.LengthMs;
                    if (run > best) best = run;
                }
                else run = 0;
                previousEnd = segment.EndMs;
            }
            return best;
        }

        private static void ComputeScore(FlowReport report, List<FlowSegment> segments, double stickiness, FlowConfig cfg)
        {
            long total = 0, deep = 0, normal = 0;
            int workSwitch = 0;
            FlowSegment previous = null;
            report.LongestFocusMs = LongestFocusRun(segments);
            for (int i = 0; i < segments.Count; i++)
            {
                FlowSegment segment = segments[i];
                // 空闲段单独累计,不进入三态分母,也不参与切换惩罚。
                if (segment.State == FlowState.Idle)
                {
                    report.IdleMs += segment.LengthMs;
                    continue;
                }
                total += segment.LengthMs;
                if (segment.State == FlowState.Deep) deep += segment.LengthMs;
                else if (segment.State == FlowState.Normal) normal += segment.LengthMs;
                if (segment.LengthMs > report.LongestDeepMs && segment.State == FlowState.Deep)
                    report.LongestDeepMs = segment.LengthMs;
                // 段之间的切换次数用作「平静度」的分子;空闲段跳过。
                if (previous != null && IsWorkCategory(previous.PrimaryCategory) && !IsWorkCategory(segment.PrimaryCategory))
                    workSwitch++;
                previous = segment;
            }

            double deepShare = total > 0 ? deep * 1.0 / total : 0;
            double normalShare = total > 0 ? normal * 1.0 / total : 0;
            double calm = cfg.ScoreSwitchCap > 0
                ? 1.0 - Math.Min(1.0, workSwitch * 1.0 / cfg.ScoreSwitchCap)
                : 1.0;
            if (calm < 0) calm = 0;

            report.Parts.DeepShare = Clamp01(deepShare);
            report.Parts.NormalShare = Clamp01(normalShare);
            report.Parts.Stickiness = Clamp01(stickiness);
            report.Parts.Calm = Clamp01(calm);
            // 质量项:深度心流满分、常规活跃半分(两者互斥占比,保证「整天深度」= 100)。
            double quality = Clamp01(report.Parts.DeepShare + 0.5 * report.Parts.NormalShare);
            report.Parts.Quality = quality;
            double score = 100.0 * (0.70 * quality + 0.15 * report.Parts.Stickiness + 0.15 * report.Parts.Calm);
            report.FocusScore = Math.Round(score, 1);
        }

        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}