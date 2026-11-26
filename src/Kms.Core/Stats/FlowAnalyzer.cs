using Kms.Core.Model;

namespace Kms.Core.Stats;

/// <summary>
/// 心流（Deep Work）量化引擎。纯函数：不依赖 UI / 存储 / 平台类型。
///
/// <para>判定链（全部确定性，固定滑窗 + 稳定排序 + 无随机）：</para>
/// <list type="number">
///   <item>归一化：过滤无效区间，同一应用且间隔 ≤ 20s（微确认容忍）的相邻会话合并；</item>
///   <item>5 分钟滑窗 / 60s 步长，逐窗计算粘性、类别数、工作→非工作切换、输入节律、长切出、连续无关切换；</item>
///   <item>四态：深度心流 / 常规活跃 / 碎片化高频切换 / 空闲挂机；</item>
///   <item>连续满足 ≥ 10 分钟判为 Deep，不足 10 分钟的 Deep 降级为 Normal；</item>
///   <item>专注分 0-100，四项权重分解可解释。</item>
/// </list>
///
/// <para>与 v3 的口径差异只在于新增可选 <c>now</c> 裁剪；不传时与 v3 完全一致。</para>
/// </summary>
public static class FlowAnalyzer
{
    private const long MinuteMs = 60_000L;

    /// <summary>主力工作类：0 编程 / 5 办公 / 6 科研 / 7 写作。</summary>
    public static bool IsWorkCategory(int categoryId) =>
        categoryId is 0 or 5 or 6 or 7;

    /// <summary>
    /// 分析一段会话与输入突发。
    /// </summary>
    /// <param name="sessions">窗口会话；可为 null。</param>
    /// <param name="bursts">输入突发；可为 null。保持输入顺序。</param>
    /// <param name="config">阈值配置；null 使用默认值。</param>
    /// <param name="now">
    /// 当前时刻。非 null 时丢弃未来会话/突发，并把跨越当前时刻的会话/突发裁到 <paramref name="now"/>，
    /// 用于「今天还没结束」的时光机边界。null 时与 v3 行为一致。
    /// </param>
    public static FlowReport Analyze(
        IEnumerable<FlowSessionRow>? sessions,
        IEnumerable<FlowBurstRow>? bursts = null,
        FlowConfig? config = null,
        DateTimeOffset? now = null)
    {
        FlowConfig cfg = config ?? new FlowConfig();
        FlowReport report = new();
        long? nowMs = now?.ToUnixTimeMilliseconds();

        if (nowMs is not null)
        {
            foreach (FlowSessionRow? row in sessions ?? Array.Empty<FlowSessionRow>())
            {
                if (row is null || row.StartMs >= nowMs.Value)
                {
                    continue;
                }

                FlowSessionRow copy = row.Copy();
                if (copy.EndMs > nowMs.Value)
                {
                    copy.EndMs = nowMs.Value;
                }

                report.Sessions.Add(copy);
            }

            foreach (FlowBurstRow? burst in bursts ?? Array.Empty<FlowBurstRow>())
            {
                if (burst is null || burst.StartMs >= nowMs.Value)
                {
                    continue;
                }

                FlowBurstRow copy = burst.Copy();
                if (copy.EndMs > nowMs.Value)
                {
                    copy.EndMs = nowMs.Value;
                }

                report.Bursts.Add(copy);
            }
        }
        else
        {
            foreach (FlowSessionRow? row in sessions ?? Array.Empty<FlowSessionRow>())
            {
                if (row is not null)
                {
                    report.Sessions.Add(row.Copy());
                }
            }

            foreach (FlowBurstRow? burst in bursts ?? Array.Empty<FlowBurstRow>())
            {
                if (burst is not null)
                {
                    report.Bursts.Add(burst.Copy());
                }
            }
        }

        List<FlowSessionRow> normalized = NormalizeCore(report.Sessions, cfg);
        report.Normalized.AddRange(normalized);
        if (normalized.Count == 0)
        {
            return report;
        }

        long origin = normalized[0].StartMs;
        long end = normalized[normalized.Count - 1].EndMs;
        report.TotalMs = end > origin ? end - origin : 0;

        List<FlowSegment> windows = new();
        double stickinessSum = 0;
        int stickinessCount = 0;
        long stepMs = Math.Max(1, cfg.StepSeconds) * 1000L;
        long windowsMs = Math.Max(stepMs, cfg.WindowSeconds * 1000L);

        for (long w0 = origin; w0 < end; w0 += stepMs)
        {
            long w1 = w0 + windowsMs;
            WindowStat stat = Measure(normalized, report.Bursts, w0, w1, cfg);

            // 空窗（没有任何会话重叠、也没有输入）不是「碎片化」，它是没在电脑前。
            // 用原始停留判断窗口是否存在；有效停留只负责粘性与主类别核算。
            if (stat.RawDwell == 0 && stat.BurstCount == 0)
            {
                continue;
            }

            FlowState state = Classify(stat, cfg);
            FlowSegment segment = new()
            {
                StartMs = w0,
                EndMs = Math.Min(w1, end),
                State = state,
                PrimaryCategory = stat.PrimaryCategory,
                PrimaryProcess = stat.PrimaryProcess,
                Stickiness = stat.Stickiness,
                Keys = stat.Keys
            };
            windows.Add(segment);

            // 挂机窗口没有切换惩罚，也不应把粘性均值拉低。
            if (state != FlowState.Idle)
            {
                stickinessSum += stat.Stickiness;
                stickinessCount++;
            }
        }

        // 分钟桶单次归属：每个 60 秒桶只能被计入一次，彻底消除 300/60 窗口重叠重复累加。
        List<FlowSegment> merged = BuildMinuteSegments(normalized, report.Bursts, windows, end);

        // Deep 不足 10 分钟 → 降级为 Normal。
        long deepMinMs = Math.Max(1, cfg.DeepMinMinutes) * MinuteMs;
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].State == FlowState.Deep && merged[i].LengthMs < deepMinMs)
            {
                merged[i].State = FlowState.Normal;
            }
        }

        merged = MergeWindows(merged);
        report.Segments.AddRange(merged);
        ComputeScore(report, merged, stickinessCount > 0 ? stickinessSum / stickinessCount : 0, cfg);
        return report;
    }

    /// <summary>归一化：排序 + 过滤 + 同应用微确认合并。</summary>
    public static List<FlowSessionRow> Normalize(
        IEnumerable<FlowSessionRow>? sessions,
        FlowConfig? config = null) =>
        NormalizeCore(sessions, config ?? new FlowConfig());

    private static List<FlowSessionRow> NormalizeCore(
        IEnumerable<FlowSessionRow>? sessions,
        FlowConfig cfg)
    {
        List<FlowSessionRow> list = new();
        if (sessions is not null)
        {
            foreach (FlowSessionRow? row in sessions)
            {
                if (row is null || row.EndMs <= row.StartMs)
                {
                    continue;
                }

                list.Add(row.Copy());
            }
        }

        // 稳定排序：同起点按 Id，保证同一份数据永远得到同一结果。
        list.Sort(static (FlowSessionRow a, FlowSessionRow b) =>
        {
            int byStart = a.StartMs.CompareTo(b.StartMs);
            return byStart != 0 ? byStart : a.Id.CompareTo(b.Id);
        });

        long tolerance = Math.Max(0, cfg.MicroCheckoutSeconds) * 1000L;
        List<FlowSessionRow> merged = new();
        for (int i = 0; i < list.Count; i++)
        {
            FlowSessionRow current = list[i];
            if (merged.Count > 0)
            {
                FlowSessionRow last = merged[merged.Count - 1];
                long gap = current.StartMs - last.EndMs;
                bool sameApp = string.Equals(
                    last.ProcessPath ?? "",
                    current.ProcessPath ?? "",
                    StringComparison.Ordinal);
                if (sameApp && gap <= tolerance)
                {
                    if (current.EndMs > last.EndMs)
                    {
                        last.EndMs = current.EndMs;
                    }

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
        public long RawDwell;       // 窗内原始会话停留；用于判断窗口是否存在于前台
        public long Dwell;          // 按 ActiveMs 约束后的有效停留；用于粘性与主类别
        public long InputCount;     // 窗内有效键鼠输入（键盘 + 点击 + 滚轮）
        public long EffectiveInput; // max(Keys, InputCount)，用于最少输入阈值
        public int BurstCount;
        public double IkiAvg;
        public double IkiVar;
        public double IkiCv;
    }

    /// <summary>
    /// 原始前台停留 → 有效停留。用 ActiveMs / 会话跨度做比例约束，
    /// 让 93 分钟但只有 3.6 秒输入的挂机会话不再霸占整天 Dwell。
    /// </summary>
    private static long EffectiveDwell(FlowSessionRow row, long rawDwell)
    {
        if (rawDwell <= 0)
        {
            return 0;
        }

        long span = row.EndMs - row.StartMs;
        if (span <= 0)
        {
            return rawDwell;
        }

        double ratio;
        if (row.ActiveMs > 0)
        {
            ratio = row.ActiveMs * 1.0 / span;
        }
        else if (row.InputCount <= 0 && row.KeyCount <= 0)
        {
            ratio = 0;
        }
        else
        {
            ratio = 1.0; // 旧数据没有 active_ms，但确实有输入 → 不惩罚
        }

        if (ratio <= 0)
        {
            return 0;
        }

        if (ratio > 1)
        {
            ratio = 1;
        }

        long effective = (long)Math.Round(rawDwell * ratio);
        return effective < 0 ? 0 : effective;
    }

    /// <summary>会话输入总量（键盘 + 点击 + 滚轮；旧行回退 KeyCount）按窗口重叠比例折算。</summary>
    private static long ProratedInput(FlowSessionRow row, long rawDwell)
    {
        long input = row.InputCount > 0 ? row.InputCount : row.KeyCount;
        if (input <= 0 || rawDwell <= 0)
        {
            return 0;
        }

        long span = row.EndMs - row.StartMs;
        if (span <= 0)
        {
            return input;
        }

        // 向下取整：长时间会话里零星几次输入不能被摊成「每个窗口都有 1 次」。
        return (long)(input * (double)rawDwell / span);
    }

    private static WindowStat Measure(
        IReadOnlyList<FlowSessionRow> sessions,
        IReadOnlyList<FlowBurstRow> bursts,
        long w0,
        long w1,
        FlowConfig cfg)
    {
        WindowStat stat = new();
        Dictionary<string, long> dwellByProcess = new(StringComparer.Ordinal);
        Dictionary<int, long> dwellByCategory = new();
        long totalDwell = 0;
        long previousEnd = -1;
        int previousCategory = -1;
        int unrelatedRun = 0;

        for (int i = 0; i < sessions.Count; i++)
        {
            FlowSessionRow row = sessions[i];
            long from = Math.Max(row.StartMs, w0);
            long to = Math.Min(row.EndMs, w1);
            if (to <= from)
            {
                continue;
            }

            long rawDwell = to - from;
            long dwell = EffectiveDwell(row, rawDwell);
            stat.RawDwell += rawDwell;
            stat.InputCount += ProratedInput(row, rawDwell);
            totalDwell += dwell;

            // 有效停留不足 1 秒的会话视为挂机背景：不参与主类别、
            // 类别数、工作→非工作和连续无关切换的判定。
            bool structural = dwell >= 1000;
            int category = row.CategoryId;
            if (structural)
            {
                string key = row.ProcessPath ?? "";
                dwellByProcess.TryGetValue(key, out long processDwell);
                dwellByProcess[key] = processDwell + dwell;

                dwellByCategory.TryGetValue(category, out long categoryDwell);
                dwellByCategory[category] = categoryDwell + dwell;

                if (previousCategory >= 0 && previousCategory != category
                    && IsWorkCategory(previousCategory) && !IsWorkCategory(category))
                {
                    stat.WorkToNonWork++;
                }

                if (!IsWorkCategory(category))
                {
                    unrelatedRun++;
                    if (unrelatedRun > stat.MaxUnrelatedRun)
                    {
                        stat.MaxUnrelatedRun = unrelatedRun;
                    }
                }
                else
                {
                    unrelatedRun = 0;
                }
            }

            if (previousEnd >= 0 && row.StartMs - previousEnd > cfg.BreakSeconds * 1000L)
            {
                stat.LongCheckouts++;
            }

            previousEnd = Math.Max(previousEnd, row.EndMs);
            if (structural)
            {
                previousCategory = category;
            }
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
        {
            if (pair.Value > bestCategoryDwell)
            {
                bestCategoryDwell = pair.Value;
                stat.PrimaryCategory = pair.Key;
            }
        }

        double varSum = 0;
        double avgSum = 0;
        for (int i = 0; i < bursts.Count; i++)
        {
            FlowBurstRow burst = bursts[i];
            long from = Math.Max(burst.StartMs, w0);
            long to = Math.Min(burst.EndMs, w1);
            if (to <= from)
            {
                continue;
            }

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
        else
        {
            // 没有突发段时用会话击键数按窗口占比折算，避免整窗判为 0。
            for (int i = 0; i < sessions.Count; i++)
            {
                FlowSessionRow row = sessions[i];
                long span = row.EndMs - row.StartMs;
                if (span <= 0)
                {
                    continue;
                }

                long from = Math.Max(row.StartMs, w0);
                long to = Math.Min(row.EndMs, w1);
                if (to <= from)
                {
                    continue;
                }

                stat.Keys += (long)Math.Round(row.KeyCount * (to - from) * 1.0 / span);
            }
        }

        stat.EffectiveInput = stat.Keys > stat.InputCount ? stat.Keys : stat.InputCount;
        return stat;
    }

    private static FlowState Classify(WindowStat stat, FlowConfig cfg)
    {
        // 没有输入突发，且键盘/鼠标有效输入为 0：这是挂机，不是高频切换。
        if (stat.BurstCount == 0 && stat.EffectiveInput == 0)
        {
            return FlowState.Idle;
        }

        bool rhythm = stat.EffectiveInput >= cfg.MinInputPerWindow && RhythmOk(stat, cfg);
        bool qualified = stat.Stickiness >= cfg.StickinessThreshold
            && stat.DistinctCategories <= cfg.MaxCategoriesPerWindow
            && stat.WorkToNonWork <= cfg.MaxWorkToNonWork
            && stat.LongCheckouts == 0
            && stat.MaxUnrelatedRun < cfg.UnrelatedSwitchLimit
            && rhythm;
        if (qualified)
        {
            return FlowState.Deep;
        }

        if (rhythm && stat.Stickiness >= cfg.NormalStickinessThreshold)
        {
            return FlowState.Normal;
        }

        return FlowState.Fragmented;
    }

    /// <summary>输入节律：用变异系数容忍思考停顿，并保留极端方差保险。</summary>
    private static bool RhythmOk(WindowStat stat, FlowConfig cfg)
    {
        if (stat.BurstCount == 0)
        {
            return true; // 纯鼠标/阅览窗口没有 IKI 样本
        }

        if (stat.IkiVar < cfg.IkiVarMin || stat.IkiVar > cfg.IkiVarMax)
        {
            return false;
        }

        if (stat.IkiAvg <= 0)
        {
            return true;
        }

        return stat.IkiCv >= cfg.IkiCvMin && stat.IkiCv <= cfg.IkiCvMax;
    }

    /// <summary>悬停诊断：按与 <see cref="Analyze"/> 相同的窗口起点复算某分钟的 5 分钟窗口。</summary>
    public static FlowDiagnostic? DiagnoseAt(
        IEnumerable<FlowSessionRow>? sessions,
        IEnumerable<FlowBurstRow>? bursts,
        long atMs,
        FlowConfig? config = null)
    {
        FlowConfig cfg = config ?? new FlowConfig();
        return DiagnoseNormalized(Normalize(sessions, cfg), bursts, atMs, cfg);
    }

    /// <summary>已归一化列表上的悬停诊断，避免每次悬停重复排序。</summary>
    public static FlowDiagnostic? DiagnoseNormalized(
        IReadOnlyList<FlowSessionRow>? normalized,
        IEnumerable<FlowBurstRow>? bursts,
        long atMs,
        FlowConfig? config = null)
    {
        if (normalized is null || normalized.Count == 0)
        {
            return null;
        }

        FlowConfig cfg = config ?? new FlowConfig();
        long origin = normalized[0].StartMs;
        long end = normalized[normalized.Count - 1].EndMs;
        if (atMs < origin || atMs >= end)
        {
            return null;
        }

        long stepMs = Math.Max(1, cfg.StepSeconds) * 1000L;
        long windowsMs = Math.Max(stepMs, cfg.WindowSeconds * 1000L);
        long w0 = origin + (atMs - origin) / stepMs * stepMs;

        List<FlowBurstRow> burstList = new();
        if (bursts is not null)
        {
            foreach (FlowBurstRow? burst in bursts)
            {
                if (burst is not null)
                {
                    burstList.Add(burst);
                }
            }
        }

        WindowStat stat = Measure(normalized, burstList, w0, w0 + windowsMs, cfg);
        return new FlowDiagnostic
        {
            WindowStartMs = w0,
            State = Classify(stat, cfg),
            Stickiness = stat.Stickiness,
            Keys = stat.Keys,
            InputCount = stat.InputCount,
            EffectiveInput = stat.EffectiveInput,
            BurstCount = stat.BurstCount,
            IkiAvg = stat.IkiAvg,
            IkiVar = stat.IkiVar,
            IkiCv = stat.IkiCv,
            DistinctCategories = stat.DistinctCategories,
            WorkToNonWork = stat.WorkToNonWork,
            LongCheckouts = stat.LongCheckouts,
            MaxUnrelatedRun = stat.MaxUnrelatedRun,
            PrimaryCategory = stat.PrimaryCategory,
            PrimaryProcess = stat.PrimaryProcess
        };
    }

    /// <summary>
    /// 把 5 分钟滑窗的判别结果落到 60 秒分钟桶上，每个桶只计一次。
    /// 保证四态总时长严格等于「有实际活动的分钟」总时长，
    /// 不会因为相邻窗口重叠而把同一分钟算 5 次。
    /// </summary>
    private static List<FlowSegment> BuildMinuteSegments(
        IReadOnlyList<FlowSessionRow> sessions,
        IReadOnlyList<FlowBurstRow> bursts,
        List<FlowSegment> windows,
        long end)
    {
        List<FlowSegment> result = new();
        if (windows.Count == 0)
        {
            return result;
        }

        long from = windows[0].StartMs / MinuteMs * MinuteMs;
        FlowSegment? current = null;
        for (long t = from; t < end; t += MinuteMs)
        {
            long tEnd = Math.Min(t + MinuteMs, end);
            if (!HasActivity(sessions, bursts, t, tEnd))
            {
                current = null;
                continue;
            }

            int index = WindowIndex(windows, t);
            FlowSegment window = windows[index];
            long minuteKeys = BurstKeysInRange(bursts, t, tEnd);
            if (current is not null && current.State == window.State)
            {
                current.EndMs = tEnd;
                current.Keys += minuteKeys;
                current.MinuteKeys ??= new List<long>();
                current.MinuteKeys.Add(minuteKeys);
                continue;
            }

            current = new FlowSegment
            {
                StartMs = t,
                EndMs = tEnd,
                State = window.State,
                PrimaryCategory = window.PrimaryCategory,
                PrimaryProcess = window.PrimaryProcess,
                Stickiness = window.Stickiness,
                Keys = minuteKeys,
                MinuteKeys = new List<long> { minuteKeys }
            };
            result.Add(current);
        }

        return result;
    }

    /// <summary>按 burst 与分钟桶的重叠比例折算击键数，供轨道脉冲渲染。</summary>
    private static long BurstKeysInRange(
        IReadOnlyList<FlowBurstRow> bursts,
        long from,
        long to)
    {
        if (to <= from)
        {
            return 0;
        }

        long keys = 0;
        for (int i = 0; i < bursts.Count; i++)
        {
            FlowBurstRow burst = bursts[i];
            long a = Math.Max(burst.StartMs, from);
            long b = Math.Min(burst.EndMs, to);
            if (b <= a)
            {
                continue;
            }

            long span = burst.EndMs - burst.StartMs;
            if (span <= 0)
            {
                keys += burst.KeyCount;
                continue;
            }

            keys += (long)Math.Round(burst.KeyCount * (double)(b - a) / span);
        }

        return keys;
    }

    /// <summary>该分钟桶内是否有真实会话停留或输入事件。</summary>
    private static bool HasActivity(
        IReadOnlyList<FlowSessionRow> sessions,
        IReadOnlyList<FlowBurstRow> bursts,
        long from,
        long to)
    {
        for (int i = 0; i < sessions.Count; i++)
        {
            FlowSessionRow row = sessions[i];
            if (Math.Min(row.EndMs, to) > Math.Max(row.StartMs, from))
            {
                return true;
            }
        }

        for (int i = 0; i < bursts.Count; i++)
        {
            FlowBurstRow burst = bursts[i];
            if (Math.Min(burst.EndMs, to) > Math.Max(burst.StartMs, from))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>覆盖该时刻的最后一个滑窗（窗口按起点升序）。</summary>
    private static int WindowIndex(List<FlowSegment> windows, long t)
    {
        int index = 0;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].StartMs <= t)
            {
                index = i;
            }
            else
            {
                break;
            }
        }

        return index;
    }

    /// <summary>把相邻同态窗口合并成段。</summary>
    private static List<FlowSegment> MergeWindows(List<FlowSegment> windows)
    {
        List<FlowSegment> result = new();
        for (int i = 0; i < windows.Count; i++)
        {
            FlowSegment window = windows[i];
            if (result.Count > 0)
            {
                FlowSegment last = result[result.Count - 1];

                // 只有时间连续（无空洞）的同态段才允许合并，避免跨数小时空白桥接。
                if (last.State == window.State && last.EndMs >= window.StartMs)
                {
                    last.EndMs = window.EndMs;
                    last.Keys += window.Keys;
                    if (window.MinuteKeys is not null)
                    {
                        last.MinuteKeys ??= new List<long>();
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

            FlowSegment copy = new()
            {
                StartMs = window.StartMs,
                EndMs = window.EndMs,
                State = window.State,
                PrimaryCategory = window.PrimaryCategory,
                PrimaryProcess = window.PrimaryProcess,
                Stickiness = window.Stickiness,
                Keys = window.Keys
            };
            if (window.MinuteKeys is not null)
            {
                copy.MinuteKeys = new List<long>(window.MinuteKeys);
            }

            result.Add(copy);
        }

        return result;
    }

    /// <summary>深度心流 + 常规专注的最长连续时长；Fragmented / Idle / 空洞会切断。</summary>
    public static long LongestFocusRun(IReadOnlyList<FlowSegment>? segments)
    {
        if (segments is null)
        {
            return 0;
        }

        long best = 0;
        long run = 0;
        long previousEnd = -1;
        for (int i = 0; i < segments.Count; i++)
        {
            FlowSegment segment = segments[i];
            bool focus = segment.State == FlowState.Deep || segment.State == FlowState.Normal;
            if (focus)
            {
                if (previousEnd >= 0 && segment.StartMs <= previousEnd)
                {
                    run += segment.LengthMs;
                }
                else
                {
                    run = segment.LengthMs;
                }

                if (run > best)
                {
                    best = run;
                }
            }
            else
            {
                run = 0;
            }

            previousEnd = segment.EndMs;
        }

        return best;
    }

    /// <summary>是否至少存在一个有效输入会话或输入突发段。</summary>
    public static bool HasMeaningfulInput(
        IEnumerable<FlowSessionRow>? sessions,
        IEnumerable<FlowBurstRow>? bursts)
    {
        if (bursts is not null)
        {
            foreach (FlowBurstRow? burst in bursts)
            {
                if (burst is not null && burst.KeyCount > 0)
                {
                    return true;
                }
            }
        }

        if (sessions is not null)
        {
            foreach (FlowSessionRow? row in sessions)
            {
                if (row is null)
                {
                    continue;
                }

                if (row.InputCount > 0 || row.KeyCount > 0 || row.ActiveMs > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ComputeScore(
        FlowReport report,
        List<FlowSegment> segments,
        double stickiness,
        FlowConfig cfg)
    {
        long total = 0;
        long deep = 0;
        long normal = 0;
        int workSwitch = 0;
        FlowSegment? previous = null;
        report.LongestFocusMs = LongestFocusRun(segments);
        for (int i = 0; i < segments.Count; i++)
        {
            FlowSegment segment = segments[i];

            // 空闲段单独累计，不进入三态分母，也不参与切换惩罚。
            if (segment.State == FlowState.Idle)
            {
                report.IdleMs += segment.LengthMs;
                continue;
            }

            total += segment.LengthMs;
            if (segment.State == FlowState.Deep)
            {
                deep += segment.LengthMs;
            }
            else if (segment.State == FlowState.Normal)
            {
                normal += segment.LengthMs;
            }

            if (segment.LengthMs > report.LongestDeepMs && segment.State == FlowState.Deep)
            {
                report.LongestDeepMs = segment.LengthMs;
            }

            // 段之间的切换次数用作「平静度」的分子；空闲段跳过。
            if (previous is not null
                && IsWorkCategory(previous.PrimaryCategory)
                && !IsWorkCategory(segment.PrimaryCategory))
            {
                workSwitch++;
            }

            previous = segment;
        }

        double deepShare = total > 0 ? deep * 1.0 / total : 0;
        double normalShare = total > 0 ? normal * 1.0 / total : 0;
        double calm = cfg.ScoreSwitchCap > 0
            ? 1.0 - Math.Min(1.0, workSwitch * 1.0 / cfg.ScoreSwitchCap)
            : 1.0;
        if (calm < 0)
        {
            calm = 0;
        }

        report.Parts.DeepShare = Clamp01(deepShare);
        report.Parts.NormalShare = Clamp01(normalShare);
        report.Parts.Stickiness = Clamp01(stickiness);
        report.Parts.Calm = Clamp01(calm);

        // 质量项：深度心流满分、常规活跃半分（两者互斥占比，保证「整天深度」= 100）。
        double quality = Clamp01(report.Parts.DeepShare + 0.5 * report.Parts.NormalShare);
        report.Parts.Quality = quality;
        double score = 100.0 * (0.70 * quality + 0.15 * report.Parts.Stickiness + 0.15 * report.Parts.Calm);
        report.FocusScore = Math.Round(score, 1);
    }

    private static double Clamp01(double value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 1)
        {
            return 1;
        }

        return value;
    }
}

/// <summary>
/// 上下文切换损耗：从主力工作窗口切出到干扰源，再切回并恢复稳定输入所需的滞后。
/// 纯函数，无 IO / 无 UI。与 v3 <c>ContextCost</c> 同口径。
/// </summary>
public static class ContextCost
{
    /// <summary>常见浏览器进程，视为干扰源。</summary>
    public static bool IsBrowser(string? processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        switch (processName.ToLowerInvariant())
        {
            case "chrome.exe":
            case "msedge.exe":
            case "firefox.exe":
            case "brave.exe":
            case "opera.exe":
            case "vivaldi.exe":
            case "iexplore.exe":
                return true;
            default:
                return false;
        }
    }

    /// <summary>干扰源：沟通类（8）或常见浏览器。</summary>
    public static bool IsDistractor(FlowSessionRow? row)
    {
        if (row is null)
        {
            return false;
        }

        return row.CategoryId == 8 || IsBrowser(row.ProcessName);
    }

    /// <summary>
    /// 追踪「主力 → 干扰源 → 切回主力并恢复稳定输入」的滞后。
    /// 恢复判据：连续击键 ≥ <paramref name="minKeys"/> 或连续活跃 ≥ <paramref name="minActiveSeconds"/>。
    /// </summary>
    public static List<ContextSwitchStat> Analyze(
        IEnumerable<FlowSessionRow>? sessions,
        IEnumerable<FlowBurstRow>? bursts,
        FlowConfig? config = null,
        int minKeys = 5,
        int minActiveSeconds = 15)
    {
        if (minKeys <= 0)
        {
            minKeys = 5;
        }

        if (minActiveSeconds <= 0)
        {
            minActiveSeconds = 15;
        }

        List<ContextSwitchStat> result = new();
        List<FlowSessionRow> normalized = FlowAnalyzer.Normalize(sessions, config);
        if (normalized.Count < 3)
        {
            return result;
        }

        List<FlowBurstRow> burstList = new();
        if (bursts is not null)
        {
            foreach (FlowBurstRow? burst in bursts)
            {
                if (burst is not null)
                {
                    burstList.Add(burst);
                }
            }
        }

        Dictionary<string, List<double>> lags = new(StringComparer.Ordinal);
        Dictionary<string, int> categoryOf = new(StringComparer.Ordinal);

        for (int i = 0; i < normalized.Count; i++)
        {
            FlowSessionRow current = normalized[i];
            if (!FlowAnalyzer.IsWorkCategory(current.CategoryId))
            {
                continue;
            }

            // 找主力之后紧邻的干扰源。
            int distractorIndex = i + 1;
            if (distractorIndex >= normalized.Count)
            {
                break;
            }

            FlowSessionRow distractor = normalized[distractorIndex];
            if (!IsDistractor(distractor))
            {
                continue;
            }

            long outMs = distractor.StartMs;
            long recoveryMs = FindRecovery(normalized, burstList, distractorIndex + 1, minKeys, minActiveSeconds);
            if (recoveryMs <= outMs)
            {
                continue;
            }

            string key = string.IsNullOrEmpty(distractor.ProcessName) ? "未知应用" : distractor.ProcessName;
            if (!lags.TryGetValue(key, out List<double>? list))
            {
                list = new List<double>();
                lags[key] = list;
            }

            list.Add((recoveryMs - outMs) / 1000.0);
            categoryOf[key] = distractor.CategoryId;
        }

        foreach (KeyValuePair<string, List<double>> pair in lags)
        {
            List<double> values = pair.Value;
            values.Sort();
            ContextSwitchStat stat = new()
            {
                ProcessName = pair.Key
            };
            if (categoryOf.TryGetValue(pair.Key, out int category))
            {
                stat.CategoryId = category;
            }

            stat.Count = values.Count;
            double sum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }

            stat.TotalLagSeconds = sum;
            stat.AvgLagSeconds = values.Count > 0 ? sum / values.Count : 0;
            stat.MedianLagSeconds = values.Count == 0
                ? 0
                : values.Count % 2 == 1
                    ? values[values.Count / 2]
                    : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2.0;
            result.Add(stat);
        }

        result.Sort(static (ContextSwitchStat a, ContextSwitchStat b) =>
        {
            int byTotal = b.TotalLagSeconds.CompareTo(a.TotalLagSeconds);
            return byTotal != 0 ? byTotal : string.CompareOrdinal(a.ProcessName, b.ProcessName);
        });
        return result;
    }

    /// <summary>从 startIndex 起找第一段主力窗口内恢复稳定输入的时刻；找不到返回 -1。</summary>
    private static long FindRecovery(
        IReadOnlyList<FlowSessionRow> sessions,
        IReadOnlyList<FlowBurstRow> bursts,
        int startIndex,
        int minKeys,
        int minActiveSeconds)
    {
        for (int i = startIndex; i < sessions.Count; i++)
        {
            FlowSessionRow row = sessions[i];
            if (!FlowAnalyzer.IsWorkCategory(row.CategoryId))
            {
                continue;
            }

            for (int b = 0; b < bursts.Count; b++)
            {
                FlowBurstRow burst = bursts[b];
                if (burst.StartMs < row.StartMs || burst.StartMs > row.EndMs)
                {
                    continue;
                }

                if (burst.KeyCount >= minKeys)
                {
                    return burst.StartMs;
                }
            }

            long activeMs = row.ActiveMs > 0 ? row.ActiveMs : row.EndMs - row.StartMs;
            if (activeMs >= minActiveSeconds * 1000L)
            {
                return row.StartMs + minActiveSeconds * 1000L;
            }
        }

        return -1;
    }
}
