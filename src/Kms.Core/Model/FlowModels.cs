namespace Kms.Core.Model;

/// <summary>心流四态。<see cref="Idle"/> 表示没有有效键鼠输入，不参与碎片化惩罚。</summary>
public enum FlowState
{
    Fragmented = 0,
    Normal = 1,
    Deep = 2,
    Idle = 3
}

/// <summary>
/// 一条窗口会话（对应 v3 <c>events_window_session</c> 的只读投影）。
/// 时间均为 UTC Unix 毫秒。
/// </summary>
public sealed class FlowSessionRow
{
    public long Id { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string ProcessPath { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public long KeyCount { get; set; }

    /// <summary>键盘 + 鼠标点击 + 滚轮的有效输入总数；旧数据缺失时为 0，分析时回退到 <see cref="KeyCount"/>。</summary>
    public long InputCount { get; set; }

    public int CategoryId { get; set; }
    public long ActiveMs { get; set; }

    public FlowSessionRow Copy() => new()
    {
        Id = Id,
        StartMs = StartMs,
        EndMs = EndMs,
        ProcessPath = ProcessPath,
        ProcessName = ProcessName,
        WindowTitle = WindowTitle,
        KeyCount = KeyCount,
        InputCount = InputCount,
        CategoryId = CategoryId,
        ActiveMs = ActiveMs
    };
}

/// <summary>
/// 一条输入突发段（对应 v3 <c>events_input_burst</c> 的只读投影）。
/// 保持输入顺序；分析口径依赖该顺序寻找「第一段恢复输入」。
/// </summary>
public sealed class FlowBurstRow
{
    public long Id { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public long KeyCount { get; set; }
    public double IkiAvgMs { get; set; }
    public double IkiVar { get; set; }
    public double BackspaceRate { get; set; }
    public long SwitchCount { get; set; }
    public string ProcessPath { get; set; } = "";
    public int CategoryId { get; set; }

    public FlowBurstRow Copy() => new()
    {
        Id = Id,
        StartMs = StartMs,
        EndMs = EndMs,
        KeyCount = KeyCount,
        IkiAvgMs = IkiAvgMs,
        IkiVar = IkiVar,
        BackspaceRate = BackspaceRate,
        SwitchCount = SwitchCount,
        ProcessPath = ProcessPath,
        CategoryId = CategoryId
    };
}

/// <summary>引擎参数。默认值即产品默认行为，测试可逐项覆盖。</summary>
public sealed class FlowConfig
{
    /// <summary>微确认容忍度：切出 ≤ 20s 且同应用会话会合并，不打断心流。</summary>
    public int MicroCheckoutSeconds { get; set; } = 20;

    /// <summary>单次切出超过此秒数即判中断。</summary>
    public int BreakSeconds { get; set; } = 45;

    /// <summary>连续切换多少个无关窗口即判中断。</summary>
    public int UnrelatedSwitchLimit { get; set; } = 3;

    /// <summary>主工作窗口停留占比门槛（深度心流）。</summary>
    public double StickinessThreshold { get; set; } = 0.72;

    /// <summary>常规专注的最低粘性；低于此值仍视为碎片切换。</summary>
    public double NormalStickinessThreshold { get; set; } = 0.45;

    /// <summary>窗内允许出现的不同类别数上限（IDE / 浏览器 / 终端协同）。</summary>
    public int MaxCategoriesPerWindow { get; set; } = 3;

    /// <summary>窗内允许的工作→非工作切换次数上限。</summary>
    public int MaxWorkToNonWork { get; set; } = 2;

    public int WindowSeconds { get; set; } = 300;
    public int StepSeconds { get; set; } = 60;

    /// <summary>连续满足判据达到此分钟数才算深度心流。产品定案为 10 分钟。</summary>
    public int DeepMinMinutes { get; set; } = 10;

    /// <summary>5 分钟窗内最少有效输入（键盘 + 鼠标点击 + 滚轮），用于兼顾调试/阅览/操作。</summary>
    public long MinInputPerWindow { get; set; } = 15;

    /// <summary>IKI 方差绝对下限，用于排除机械连点。</summary>
    public double IkiVarMin { get; set; } = 40;

    /// <summary>IKI 方差绝对上限；仅作极端值保险，节奏主要由变异系数判定。</summary>
    public double IkiVarMax { get; set; } = 50_000_000;

    /// <summary>IKI 变异系数区间：容忍思考停顿，同时排除固定间隔连点。</summary>
    public double IkiCvMin { get; set; } = 0.03;

    public double IkiCvMax { get; set; } = 6.0;

    /// <summary>专注分里「平静度」项的计算上限（每次无关切换扣分）。</summary>
    public int ScoreSwitchCap { get; set; } = 12;
}

/// <summary>一段同态时间。</summary>
public sealed class FlowSegment
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public FlowState State { get; set; }
    public int PrimaryCategory { get; set; }
    public string PrimaryProcess { get; set; } = "";
    public double Stickiness { get; set; }
    public long Keys { get; set; }

    /// <summary>最终分钟段的每分钟击键数；只有 <c>Analyze</c> 输出的分钟段非空。</summary>
    public List<long>? MinuteKeys { get; set; }

    public long LengthMs => EndMs > StartMs ? EndMs - StartMs : 0;
}

/// <summary>专注分的四项权重分解（每项 0..1）。</summary>
public sealed class FlowScoreParts
{
    public double DeepShare { get; set; }
    public double NormalShare { get; set; }
    public double Stickiness { get; set; }
    public double Calm { get; set; }

    /// <summary>质量项 = 深度占比 + 0.5 × 常规占比（0..1）。</summary>
    public double Quality { get; set; }
}

/// <summary>某个分钟背后的 5 分钟窗口诊断，供时光机悬停提示使用（纯内存）。</summary>
public sealed class FlowDiagnostic
{
    public long WindowStartMs { get; set; }
    public FlowState State { get; set; }
    public double Stickiness { get; set; }
    public long Keys { get; set; }
    public long InputCount { get; set; }
    public long EffectiveInput { get; set; }
    public int BurstCount { get; set; }
    public double IkiAvg { get; set; }
    public double IkiVar { get; set; }
    public double IkiCv { get; set; }
    public int DistinctCategories { get; set; }
    public int WorkToNonWork { get; set; }
    public int LongCheckouts { get; set; }
    public int MaxUnrelatedRun { get; set; }
    public int PrimaryCategory { get; set; }
    public string PrimaryProcess { get; set; } = "";
}

/// <summary>一个干扰源的上下文切换损耗汇总。</summary>
public sealed class ContextSwitchStat
{
    public string ProcessName { get; set; } = "";
    public int CategoryId { get; set; }
    public int Count { get; set; }
    public double MedianLagSeconds { get; set; }
    public double AvgLagSeconds { get; set; }
    public double TotalLagSeconds { get; set; }
}

/// <summary>
/// 一次心流分析的完整结果。集合按时间升序；时间均为 UTC Unix 毫秒。
/// </summary>
public sealed class FlowReport
{
    /// <summary>分钟级四态段，相邻同态且连续的分钟会合并。</summary>
    public List<FlowSegment> Segments { get; } = new();

    /// <summary>归一化后的会话（排序、过滤、同应用 ≤20s 缺口合并）。</summary>
    public List<FlowSessionRow> Normalized { get; } = new();

    /// <summary>原始会话快照（已按 <c>now</c> 裁剪掉未来部分），供时光机会话查找。</summary>
    public List<FlowSessionRow> Sessions { get; } = new();

    /// <summary>输入突发快照（保持输入顺序，已按 <c>now</c> 裁剪）。</summary>
    public List<FlowBurstRow> Bursts { get; } = new();

    public FlowScoreParts Parts { get; } = new();
    public double FocusScore { get; set; }

    /// <summary>最长连续 Deep 段。</summary>
    public long LongestDeepMs { get; set; }

    /// <summary>深度心流 + 常规专注的最长连续时长。</summary>
    public long LongestFocusMs { get; set; }

    /// <summary>首末归一化会话的跨度（含空洞）。</summary>
    public long TotalMs { get; set; }

    /// <summary>空闲段总时长；不计入三态分母与碎片化惩罚。</summary>
    public long IdleMs { get; set; }

    public long DeepMs => SumState(FlowState.Deep);
    public long NormalMs => SumState(FlowState.Normal);
    public long FragmentedMs => SumState(FlowState.Fragmented);

    /// <summary>三态总时长（Deep + Normal + Fragmented），不含 Idle 与空洞。</summary>
    public long ActiveMs => DeepMs + NormalMs + FragmentedMs;

    /// <summary>某一毫秒时刻的心流状态；落在所有段之外时返回 <see cref="FlowState.Idle"/>。</summary>
    public FlowState StateAt(long ms)
    {
        for (int i = 0; i < Segments.Count; i++)
        {
            FlowSegment segment = Segments[i];
            if (ms >= segment.StartMs && ms < segment.EndMs)
            {
                return segment.State;
            }
        }

        return FlowState.Idle;
    }

    /// <summary>某一毫秒时刻所在的分钟段；没有活动时返回 null。</summary>
    public FlowSegment? SegmentAt(long ms)
    {
        for (int i = 0; i < Segments.Count; i++)
        {
            FlowSegment segment = Segments[i];
            if (ms >= segment.StartMs && ms < segment.EndMs)
            {
                return segment;
            }
        }

        return null;
    }

    /// <summary>某一毫秒时刻所在的前台会话；同一时刻多条重叠时取最长的一条。</summary>
    public FlowSessionRow? SessionAt(long ms)
    {
        FlowSessionRow? best = null;
        for (int i = 0; i < Sessions.Count; i++)
        {
            FlowSessionRow row = Sessions[i];
            if (ms < row.StartMs || ms >= row.EndMs)
            {
                continue;
            }

            if (best is null || row.EndMs - row.StartMs > best.EndMs - best.StartMs)
            {
                best = row;
            }
        }

        return best;
    }

    /// <summary>
    /// 首条与末条非 Idle 活跃段的绝对毫秒边界；没有活跃段时返回 false。
    /// 对应 v3 <c>FlowBoardData.ActiveRange</c>（v3 返回的是日内秒偏移）。
    /// </summary>
    public bool TryGetActiveRange(out long firstMs, out long lastMs)
    {
        firstMs = -1;
        lastMs = -1;
        for (int i = 0; i < Segments.Count; i++)
        {
            FlowSegment segment = Segments[i];
            if (segment.State == FlowState.Idle || segment.LengthMs <= 0)
            {
                continue;
            }

            if (firstMs < 0 || segment.StartMs < firstMs)
            {
                firstMs = segment.StartMs;
            }

            if (lastMs < 0 || segment.EndMs > lastMs)
            {
                lastMs = segment.EndMs;
            }
        }

        return firstMs >= 0 && lastMs > firstMs;
    }

    /// <summary>
    /// 以 <paramref name="atMs"/> 为中心、半宽 <paramref name="halfWindowSeconds"/> 秒的局部输入密度（键/分）。
    /// 与 v3 时光机一致：只要突发段与窗口相交就累计整段击键，不按重叠比例折算。
    /// </summary>
    public double DensityAt(long atMs, int halfWindowSeconds = 60)
    {
        int half = Math.Max(0, halfWindowSeconds);
        if (half == 0)
        {
            return 0;
        }

        long from = atMs - half * 1000L;
        long to = atMs + half * 1000L;
        long keys = 0;
        for (int i = 0; i < Bursts.Count; i++)
        {
            FlowBurstRow burst = Bursts[i];
            long start = Math.Max(burst.StartMs, from);
            long end = Math.Min(burst.EndMs, to);
            if (end > start)
            {
                keys += burst.KeyCount;
            }
        }

        return keys / (half * 2.0);
    }

    private long SumState(FlowState state)
    {
        long total = 0;
        for (int i = 0; i < Segments.Count; i++)
        {
            if (Segments[i].State == state)
            {
                total += Segments[i].LengthMs;
            }
        }

        return total;
    }
}
