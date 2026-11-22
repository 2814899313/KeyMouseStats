// ============================================================================
//  tests/FlowAnalyzerTests.cs  ·  v3.0 Milestone 3 / Step 1
//  纯函数:不建窗体、不碰数据库、不读写文件。
// ============================================================================
using System;
using System.Collections.Generic;
using KeyMouseStats;

internal static class FlowAnalyzerTests
{
    private static int _checks;
    private static void Check(bool v, string n) { if (!v) throw new Exception(n); _checks++; }
    private static void Equal(long a, long e, string n) { Check(a == e, n + "(期望 " + e + ",实际 " + a + ")"); }
    private static void Near(double a, double e, double tol, string n) { Check(Math.Abs(a - e) <= tol, n + "(期望 " + e + ",实际 " + a + ")"); }

    private static void Main() { Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("FlowAnalyzerTests", Run); }

    private static FlowSessionRow S(long startSec, long durSec, string proc, int cat, long keys)
    {
        FlowSessionRow r = new FlowSessionRow();
        r.StartMs = startSec * 1000; r.EndMs = (startSec + durSec) * 1000;
        r.ProcessPath = proc; r.ProcessName = proc; r.CategoryId = cat;
        r.KeyCount = keys; r.ActiveMs = durSec * 1000;
        return r;
    }

    private static List<FlowBurstRow> Bursts(long fromSec, long toSec, long everySec, long keys, double ikiVar)
    {
        List<FlowBurstRow> list = new List<FlowBurstRow>();
        for (long t = fromSec; t < toSec; t += everySec)
        {
            FlowBurstRow b = new FlowBurstRow();
            b.StartMs = t * 1000; b.EndMs = (t + everySec) * 1000;
            b.KeyCount = keys; b.IkiVar = ikiVar; b.IkiAvgMs = 180;
            list.Add(b);
        }
        return list;
    }

    private static void Run()
    {
        NormalizeRules();
        DeepHappyPath();
        DeepNeedsTenMinutes();
        LenientRhythmAndInput();
        StickinessCategoriesAndNormal();
        LongestFocusRunCountsNormal();
        MicroCheckoutKeepsFlow();
        LongBreakInterrupts();
        UnrelatedSwitchesInterrupt();
        ScoreDecomposition();
        DegenerateInputs();
        ContextCostRules();
        ExtraCoverage();
        IdleWindowNotFragmented();
        NoOverlapDoubleCount();
        MergeAcrossGapStaysSplit();
        LongIdleActiveMsNotFragmented();
        MinuteSegmentKeysProrated();
        HasDataRequiresMeaningfulInput();
        Console.WriteLine("FlowAnalyzerTests: " + _checks + " 项断言通过");
    }

    private static void NormalizeRules()
    {
        FlowConfig cfg = new FlowConfig();
        List<FlowSessionRow> raw = new List<FlowSessionRow>();
        FlowSessionRow bad = S(0, 0, "a.exe", 0, 1);           // 无效区间
        raw.Add(bad);
        raw.Add(S(120, 60, "b.exe", 0, 5));                    // 乱序
        raw.Add(S(0, 60, "a.exe", 0, 10));
        List<FlowSessionRow> n = FlowAnalyzer.Normalize(raw, cfg);
        Equal(n.Count, 2, "无效区间应被过滤(3 条留 2 条)");
        Equal(n[0].StartMs, 0, "应按起点升序");
        Check(n[1].StartMs > n[0].StartMs, "排序稳定");

        List<FlowSessionRow> merged = new List<FlowSessionRow>();
        merged.Add(S(0, 60, "a.exe", 0, 10));
        merged.Add(S(70, 60, "a.exe", 0, 20));                 // 间隔 10s ≤20s 同应用 → 合并
        List<FlowSessionRow> m = FlowAnalyzer.Normalize(merged, cfg);
        Equal(m.Count, 1, "10s 同应用相邻应合并");
        Equal(m[0].KeyCount, 30, "合并后击键累加");
        Equal(m[0].EndMs, 130000, "合并后终点取后者");

        List<FlowSessionRow> split = new List<FlowSessionRow>();
        split.Add(S(0, 60, "a.exe", 0, 10));
        split.Add(S(70, 60, "b.exe", 0, 20));
        Equal(FlowAnalyzer.Normalize(split, cfg).Count, 2, "不同应用不合并");

        List<FlowSessionRow> far = new List<FlowSessionRow>();
        far.Add(S(0, 60, "a.exe", 0, 10));
        far.Add(S(120, 60, "a.exe", 0, 20));                   // 间隔 60s > 20s
        Equal(FlowAnalyzer.Normalize(far, cfg).Count, 2, "超过微确认容忍不合并");

        Check(FlowAnalyzer.IsWorkCategory(0), "编程是工作类");
        Check(FlowAnalyzer.IsWorkCategory(5), "办公是工作类");
        Check(FlowAnalyzer.IsWorkCategory(6), "科研是工作类");
        Check(FlowAnalyzer.IsWorkCategory(7), "写作是工作类");
        Check(!FlowAnalyzer.IsWorkCategory(8), "沟通不是工作类");
        Check(!FlowAnalyzer.IsWorkCategory(1), "游戏不是工作类");
    }

    private static void DeepHappyPath()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>(); s.Add(S(0, 1200, "code.exe", 0, 9000));
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 1200, 30, 40, 500), new FlowConfig());
        Check(r.Segments.Count >= 1, "应至少有一段");
        bool deep = false;
        for (int i = 0; i < r.Segments.Count; i++) if (r.Segments[i].State == FlowState.Deep) deep = true;
        Check(deep, "20 分钟连续专注应识别为深度心流");
        Equal(r.LongestDeepMs, 1200000, "最长心流连击 = 20 分钟");
        Near(r.FocusScore, 100, 0.1, "整天深度应得满分");
        Near(r.Parts.Quality, 1, 0.001, "质量项为 1");
    }

    private static void DeepNeedsTenMinutes()
    {
        List<FlowSessionRow> s9 = new List<FlowSessionRow>(); s9.Add(S(0, 540, "code.exe", 0, 4000));
        FlowReport r9 = FlowAnalyzer.Analyze(s9, Bursts(0, 540, 30, 40, 500), new FlowConfig());
        bool deep9 = false;
        for (int i = 0; i < r9.Segments.Count; i++) if (r9.Segments[i].State == FlowState.Deep) deep9 = true;
        Check(!deep9, "9 分钟不足以判为深度心流");
        Equal(r9.LongestDeepMs, 0, "无深度段时深度连击为 0");
        Check(r9.LongestFocusMs >= 540000, "9 分钟仍应计入最长专注");
        Check(r9.FocusScore < 100, "不足 10 分钟不应满分");

        List<FlowSessionRow> s10 = new List<FlowSessionRow>(); s10.Add(S(0, 600, "code.exe", 0, 5000));
        FlowReport r10 = FlowAnalyzer.Analyze(s10, Bursts(0, 600, 30, 40, 500), new FlowConfig());
        bool deep10 = false;
        for (int i = 0; i < r10.Segments.Count; i++) if (r10.Segments[i].State == FlowState.Deep) deep10 = true;
        Check(deep10, "10 分钟连续专注应判为深度心流");
        Equal(r10.LongestDeepMs, 600000, "深度连击 = 10 分钟");
    }

    /// <summary>宽容节奏:思考停顿不被绝对方差误杀,鼠标点击也计入有效输入。</summary>
    private static void LenientRhythmAndInput()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 1200, "code.exe", 0, 9000));
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 1200, 30, 40, 100000), new FlowConfig());
        Check(r.LongestDeepMs >= 600000, "思考停顿(高 IKI 方差)不应把深度心流判成碎片");

        List<FlowSessionRow> clicks = new List<FlowSessionRow>();
        FlowSessionRow row = S(0, 600, "code.exe", 0, 10);
        row.InputCount = 150;
        clicks.Add(row);
        List<FlowBurstRow> b = new List<FlowBurstRow>();
        FlowBurstRow burst = new FlowBurstRow();
        burst.StartMs = 0; burst.EndMs = 600000;
        burst.KeyCount = 5; burst.IkiAvgMs = 180; burst.IkiVar = 500;
        b.Add(burst);
        FlowReport cr = FlowAnalyzer.Analyze(clicks, b, new FlowConfig());
        Check(cr.LongestDeepMs >= 600000, "鼠标点击占主导 + 少量键盘应能构成深度专注");
    }

    /// <summary>0.72 粘性与 3 类别上限;48% 粘性应落常规专注。</summary>
    private static void StickinessCategoriesAndNormal()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 240, "code.exe", 0, 3000));
        s.Add(S(240, 30, "chrome.exe", 4, 300));
        s.Add(S(270, 30, "office.exe", 5, 300));
        s.Add(S(300, 240, "code.exe", 0, 3000));
        s.Add(S(540, 30, "chrome.exe", 4, 300));
        s.Add(S(570, 30, "office.exe", 5, 300));
        s.Add(S(600, 240, "code.exe", 0, 3000));
        s.Add(S(840, 30, "chrome.exe", 4, 300));
        s.Add(S(870, 30, "office.exe", 5, 300));
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 900, 30, 40, 500), new FlowConfig());
        Check(r.LongestDeepMs >= 600000, "0.8 粘性 + 3 类别应可判深度心流");

        List<FlowSessionRow> n = new List<FlowSessionRow>();
        n.Add(S(0, 144, "code.exe", 0, 2000));
        n.Add(S(144, 156, "chrome.exe", 4, 2000));
        FlowReport nr = FlowAnalyzer.Analyze(n, Bursts(0, 300, 30, 40, 500), new FlowConfig());
        bool normal = false;
        for (int i = 0; i < nr.Segments.Count; i++) if (nr.Segments[i].State == FlowState.Normal) normal = true;
        Check(normal, "48% 粘性应判常规专注");
        Equal(nr.LongestDeepMs, 0, "不足 72% 粘性不应判深度");
        Check(nr.LongestFocusMs >= 300000, "常规专注应计入最长专注");
    }

    private static FlowSegment Seg(long startSec, long durSec, FlowState state)
    {
        FlowSegment segment = new FlowSegment();
        segment.StartMs = startSec * 1000L;
        segment.EndMs = (startSec + durSec) * 1000L;
        segment.State = state;
        return segment;
    }

    /// <summary>LongestFocusMs 必须把连续的 Deep + Normal 合并计算。</summary>
    private static void LongestFocusRunCountsNormal()
    {
        List<FlowSegment> segments = new List<FlowSegment>();
        segments.Add(Seg(0, 600, FlowState.Deep));
        segments.Add(Seg(600, 300, FlowState.Normal));
        segments.Add(Seg(900, 60, FlowState.Fragmented));
        segments.Add(Seg(960, 600, FlowState.Deep));
        segments.Add(Seg(1560, 240, FlowState.Idle));
        Equal(FlowAnalyzer.LongestFocusRun(segments), 900000, "深度 + 常规应合并为最长专注");
    }

    private static void MicroCheckoutKeepsFlow()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 600, "code.exe", 0, 5000));
        s.Add(S(600, 15, "wechat.exe", 8, 20));      // 15s 微确认
        s.Add(S(615, 1185, "code.exe", 0, 9000));
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 1800, 30, 40, 500), new FlowConfig());
        Check(r.LongestDeepMs >= 900000, "微确认不应打断,连击应 ≥15 分钟");
    }

    private static void LongBreakInterrupts()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 600, "code.exe", 0, 5000));
        s.Add(S(660, 1140, "code.exe", 0, 9000));    // 中间空了 60s > 45s
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 600, 30, 40, 500), new FlowConfig());
        // 分钟桶口径:60s 空档本身没有活动,不会再生成碎片段;
        // 正确语义是「前后两段深度被切断」,而不是「空档变成碎片」。
        Check(r.Segments.Count >= 2, "中断应切出多段");
        Check(r.LongestDeepMs < 1500000, "中断必须切断深度连击");
    }

    private static void UnrelatedSwitchesInterrupt()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 300, "code.exe", 0, 3000));
        s.Add(S(300, 20, "wechat.exe", 8, 10));
        s.Add(S(320, 20, "chrome.exe", 4, 10));
        s.Add(S(340, 20, "qq.exe", 8, 10));
        s.Add(S(360, 1440, "code.exe", 0, 9000));
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 1800, 30, 40, 500), new FlowConfig());
        bool windowBroken = false;
        for (int i = 0; i < r.Segments.Count; i++)
            if (r.Segments[i].State != FlowState.Deep
                && r.Segments[i].StartMs <= 300000 && r.Segments[i].EndMs >= 360000)
                windowBroken = true;
        Check(windowBroken, "连续 ≥3 个无关窗口必须判为中断");
    }

    private static void ScoreDecomposition()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>(); s.Add(S(0, 1200, "code.exe", 0, 9000));
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 1200, 30, 40, 500), new FlowConfig());
        double expected = Math.Round(100.0 * (0.70 * r.Parts.Quality + 0.15 * r.Parts.Stickiness + 0.15 * r.Parts.Calm), 1);
        Near(r.FocusScore, expected, 0.05, "专注分必须等于四项分解的加权和");
        Check(r.Parts.DeepShare >= 0 && r.Parts.DeepShare <= 1, "DeepShare 在 [0,1]");
        Check(r.Parts.NormalShare >= 0 && r.Parts.NormalShare <= 1, "NormalShare 在 [0,1]");
        Check(r.Parts.Stickiness >= 0 && r.Parts.Stickiness <= 1, "Stickiness 在 [0,1]");
        Check(r.Parts.Calm >= 0 && r.Parts.Calm <= 1, "Calm 在 [0,1]");
        Check(r.FocusScore >= 0 && r.FocusScore <= 100, "专注分在 [0,100]");
    }

    private static void DegenerateInputs()
    {
        FlowReport empty = FlowAnalyzer.Analyze(new List<FlowSessionRow>(), new List<FlowBurstRow>(), new FlowConfig());
        Equal(empty.Segments.Count, 0, "空输入没有段");
        Near(empty.FocusScore, 0, 0.001, "空输入专注分为 0");
        Equal(empty.LongestDeepMs, 0, "空输入连击为 0");
        FlowReport nulls = FlowAnalyzer.Analyze(null, null, null);
        Check(nulls != null && nulls.FocusScore == 0, "null 输入应安全返回");
    }

    private static void ContextCostRules()
    {
        FlowConfig cfg = new FlowConfig();
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 600, "code.exe", 0, 5000));          // 主力
        s.Add(S(600, 120, "wechat.exe", 8, 30));        // 干扰 2 分钟
        s.Add(S(720, 600, "code.exe", 0, 5000));        // 切回
        List<FlowBurstRow> b = Bursts(0, 1200, 30, 10, 500);
        // 让 720s 后的第一批突发达到 ≥5 键的恢复判据
        for (int i = 0; i < b.Count; i++) if (b[i].StartMs >= 720000 && b[i].StartMs < 750000) b[i].KeyCount = 20;
        List<ContextSwitchStat> stats = ContextCost.Analyze(s, b, cfg, 5, 15);
        Equal(stats.Count, 1, "应识别到一个干扰源");
        Equal(stats[0].Count, 1, "样本数为 1");
        Check(stats[0].MedianLagSeconds >= 120, "恢复滞后至少覆盖干扰时长");
        Check(stats[0].AvgLagSeconds > 0, "平均滞后为正");
        Check(stats[0].TotalLagSeconds >= stats[0].MedianLagSeconds, "总损耗不小于中位数");
        Check(ContextCost.IsBrowser("chrome.exe"), "chrome 视为干扰源");
        Check(ContextCost.IsBrowser("MSEDGE.EXE"), "进程名不区分大小写");
        Check(!ContextCost.IsBrowser("code.exe"), "非浏览器不是干扰源");

        List<ContextSwitchStat> none = ContextCost.Analyze(new List<FlowSessionRow>(), b, cfg, 5, 15);
        Equal(none.Count, 0, "空会话没有损耗样本");
    }

    private static void ExtraCoverage()
    {
        List<FlowSessionRow> fragmented = new List<FlowSessionRow>();
        for (int i = 0; i < 40; i++) fragmented.Add(S(i * 30, 20, "app" + (i % 5) + ".exe", i % 2 == 0 ? 0 : 8, 5));
        FlowReport fr = FlowAnalyzer.Analyze(fragmented, Bursts(0, 1200, 60, 5, 500), new FlowConfig());
        Check(fr.Segments.Count > 0, "碎片化输入应有分段");
        Check(fr.FocusScore < 65, "碎片化专注分应偏低");
        Check(fr.Parts.DeepShare < 0.5, "碎片化场景深度占比应偏低");
        List<FlowSessionRow> twoDeep = new List<FlowSessionRow>();
        twoDeep.Add(S(0, 1200, "code.exe", 0, 9000));
        twoDeep.Add(S(1800, 1200, "code.exe", 0, 9000));
        FlowReport tr = FlowAnalyzer.Analyze(twoDeep, Bursts(0, 3000, 30, 40, 500), new FlowConfig());
        Check(tr.Segments.Count >= 2, "长中断应形成多段");
        Check(tr.LongestDeepMs < tr.TotalMs, "最长连击应短于总时长");
        Check(tr.LongestDeepMs >= 900000, "单段深度仍应 >=15 分钟");
        List<FlowSessionRow> deepOnly = new List<FlowSessionRow>();
        deepOnly.Add(S(0, 1200, "code.exe", 0, 9000));
        FlowReport d = FlowAnalyzer.Analyze(deepOnly, Bursts(0, 1200, 30, 40, 500), new FlowConfig());
        Check(d.FocusScore >= fr.FocusScore, "深度场景分数不应低于碎片场景");
        List<FlowSessionRow> idle = new List<FlowSessionRow>();
        idle.Add(S(0, 1200, "code.exe", 0, 0));
        FlowReport ir = FlowAnalyzer.Analyze(idle, new List<FlowBurstRow>(), new FlowConfig());
        Check(ir.FocusScore < d.FocusScore, "无输入的挂机不应与深度同分");
        Near(ir.Parts.DeepShare, 0, 0.001, "挂机不产生深度占比");
        FlowReport spam = FlowAnalyzer.Analyze(deepOnly, Bursts(0, 1200, 10, 200, 5), new FlowConfig());
        Check(spam.Parts.DeepShare < 1, "IKI 方差过低应被排除");
        Check(spam.FocusScore < 100, "连点器不应拿满分");
        FlowConfig loose = new FlowConfig();
        loose.DeepMinMinutes = 5;
        List<FlowSessionRow> s14 = new List<FlowSessionRow>();
        s14.Add(S(0, 840, "code.exe", 0, 6000));
        FlowReport lr = FlowAnalyzer.Analyze(s14, Bursts(0, 840, 30, 40, 500), loose);
        Check(lr.LongestDeepMs >= 600000, "降低门槛后 14 分钟应为深度");
        List<FlowSessionRow> multi = new List<FlowSessionRow>();
        multi.Add(S(0, 600, "code.exe", 0, 5000));
        multi.Add(S(600, 60, "wechat.exe", 8, 10));
        multi.Add(S(660, 600, "code.exe", 0, 5000));
        multi.Add(S(1260, 600, "chrome.exe", 4, 10));
        multi.Add(S(1860, 600, "code.exe", 0, 5000));
        List<ContextSwitchStat> ranked = ContextCost.Analyze(multi, Bursts(0, 3000, 30, 10, 500), new FlowConfig(), 5, 15);
        Check(ranked.Count >= 1, "多干扰源应有统计");
        for (int i = 1; i < ranked.Count; i++) Check(ranked[i - 1].TotalLagSeconds >= ranked[i].TotalLagSeconds, "排行榜按总损耗降序");
    }

    /// <summary>跨零碎窗口:中间长时间没有会话的空窗不得计入碎片时长。</summary>
    private static void IdleWindowNotFragmented()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 600, "code.exe", 0, 5000));          // 0..10min 活跃
        s.Add(S(7200, 600, "code.exe", 0, 5000));       // 2h..2h10 活跃,中间空了近两小时
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 600, 30, 40, 500), new FlowConfig());
        long total = 0;
        for (int i = 0; i < r.Segments.Count; i++) total += r.Segments[i].LengthMs;
        Check(total < 1800000, "空窗不得被算进三态总时长(实际 " + total + " ms)");
        long frag = 0;
        for (int i = 0; i < r.Segments.Count; i++)
            if (r.Segments[i].State == FlowState.Fragmented) frag += r.Segments[i].LengthMs;
        Check(frag < 1800000, "空窗不得被计为碎片化切换");
        Check(r.Segments.Count >= 1, "仍要有段输出");
    }

    /// <summary>连续 150 分钟活动:三态总时长必须严格 ≤ 活动跨度,不得因窗口重叠重复累加。</summary>
    private static void NoOverlapDoubleCount()
    {
        SessionSpan();
    }

    private static void SessionSpan()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 9000, "code.exe", 0, 80000));      // 0..150min 连续活动
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 9000, 30, 40, 500), new FlowConfig());
        long total = 0;
        for (int i = 0; i < r.Segments.Count; i++) total += r.Segments[i].LengthMs;
        Check(total <= 9000000, "三态总时长不得超过 150 分钟(实际 " + total + " ms)");
        Near(total, 9000000, 120000, "三态总时长应等于活动跨度");
        long sum = 0;
        for (int i = 0; i < r.Segments.Count; i++) sum += r.Segments[i].LengthMs;
        Equal(sum, total, "分段求和自洽");
    }

    /// <summary>同态但中间隔 2 小时:必须保持两段,不得桥接成更长连击。</summary>
    private static void MergeAcrossGapStaysSplit()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 1200, "code.exe", 0, 9000));
        s.Add(S(8400, 1200, "code.exe", 0, 9000));    // 与上一段相隔 2 小时
        List<FlowBurstRow> b = Bursts(0, 1200, 30, 40, 500);
        b.AddRange(Bursts(8400, 9600, 30, 40, 500));
        FlowReport r = FlowAnalyzer.Analyze(s, b, new FlowConfig());
        Check(r.Segments.Count >= 2, "同态段之间有 2 小时空洞时必须拆成两段");
        Equal(r.LongestDeepMs, 1200000, "空洞不得把两段深度桥接成更长连击");
        long total = 0;
        for (int i = 0; i < r.Segments.Count; i++) total += r.Segments[i].LengthMs;
        Check(total <= 2460000, "两段各 20 分钟,总活动时长不得超过 41 分钟(实际 " + total + " ms)");
    }

    /// <summary>93 分钟前台会话只有 3.6 秒有效输入:应判 Idle,不得铺成长碎片。</summary>
    private static void LongIdleActiveMsNotFragmented()
    {
        FlowSessionRow idle = S(0, 5580, "Gemini.exe", 4, 0);
        idle.ActiveMs = 3633;
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(idle);
        FlowReport r = FlowAnalyzer.Analyze(s, new List<FlowBurstRow>(), new FlowConfig());
        Check(r.IdleMs >= 5500000, "93 分钟挂机会话应归入 Idle(实际 " + r.IdleMs + " ms)");
        long frag = 0;
        for (int i = 0; i < r.Segments.Count; i++)
            if (r.Segments[i].State == FlowState.Fragmented) frag += r.Segments[i].LengthMs;
        Equal(frag, 0, "挂机不得计入 FragmentedMs");
        Near(r.Parts.DeepShare, 0, 0.001, "挂机不产生深度占比");

        // 190 分钟会话仅 32 次键鼠输入:按比例向下取整后不得摊成每个窗口 1 次。
        FlowSessionRow gemini = S(0, 11401, "Gemini.exe", 4, 3);
        gemini.InputCount = 32;
        gemini.ActiveMs = 29714;
        List<FlowSessionRow> g = new List<FlowSessionRow>();
        g.Add(gemini);
        FlowReport gr = FlowAnalyzer.Analyze(g, new List<FlowBurstRow>(), new FlowConfig());
        Check(gr.IdleMs >= 11000000, "190 分钟零星输入应主要归入 Idle(实际 " + gr.IdleMs + " ms)");
        long gFrag = 0;
        for (int i = 0; i < gr.Segments.Count; i++)
            if (gr.Segments[i].State == FlowState.Fragmented) gFrag += gr.Segments[i].LengthMs;
        Equal(gFrag, 0, "零星输入不得被取整放大成整段碎片");
    }

    /// <summary>FlowSegment.Keys 必须按 burst 与分钟桶的重叠比例累计真实击键。</summary>
    private static void MinuteSegmentKeysProrated()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>();
        s.Add(S(0, 300, "code.exe", 0, 60));
        List<FlowBurstRow> b = new List<FlowBurstRow>();
        FlowBurstRow burst = new FlowBurstRow();
        burst.StartMs = 30000; burst.EndMs = 90000;
        burst.KeyCount = 60; burst.IkiVar = 500; burst.IkiAvgMs = 180;
        b.Add(burst);
        FlowReport r = FlowAnalyzer.Analyze(s, b, new FlowConfig());
        long keys = 0;
        long listKeys = 0;
        for (int i = 0; i < r.Segments.Count; i++)
        {
            keys += r.Segments[i].Keys;
            if (r.Segments[i].MinuteKeys == null) continue;
            for (int m = 0; m < r.Segments[i].MinuteKeys.Count; m++) listKeys += r.Segments[i].MinuteKeys[m];
        }
        Equal(keys, 60, "FlowSegment.Keys 应按分钟重叠比例累计 burst 击键");
        Equal(listKeys, 60, "MinuteKeys 应保留可绘制的分钟击键序列");
    }

    /// <summary>HasData 只认有效输入;纯挂机会话不算可分析心流数据。</summary>
    private static void HasDataRequiresMeaningfulInput()
    {
        FlowSessionRow idle = S(0, 600, "idle.exe", 4, 0);
        idle.ActiveMs = 0;
        List<FlowSessionRow> sessions = new List<FlowSessionRow>();
        sessions.Add(idle);
        Check(!FlowBoardData.HasMeaningfulInput(sessions, new List<FlowBurstRow>()),
            "没有键鼠输入的会话不算可分析数据");
        sessions.Add(S(60, 60, "code.exe", 0, 10));
        Check(FlowBoardData.HasMeaningfulInput(sessions, new List<FlowBurstRow>()),
            "有击键的会话应算可分析数据");
        List<FlowBurstRow> bursts = new List<FlowBurstRow>();
        FlowBurstRow burst = new FlowBurstRow();
        burst.KeyCount = 5;
        bursts.Add(burst);
        Check(FlowBoardData.HasMeaningfulInput(new List<FlowSessionRow>(), bursts),
            "输入突发段本身也应算可分析数据");
    }
}
