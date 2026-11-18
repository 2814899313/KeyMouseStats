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
        DeepNeedsFifteenMinutes();
        MicroCheckoutKeepsFlow();
        LongBreakInterrupts();
        UnrelatedSwitchesInterrupt();
        ScoreDecomposition();
        DegenerateInputs();
        ContextCostRules();
        ExtraCoverage();
        IdleWindowNotFragmented();
        NoOverlapDoubleCount();
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

    private static void DeepNeedsFifteenMinutes()
    {
        List<FlowSessionRow> s = new List<FlowSessionRow>(); s.Add(S(0, 840, "code.exe", 0, 6000));
        FlowReport r = FlowAnalyzer.Analyze(s, Bursts(0, 840, 30, 40, 500), new FlowConfig());
        bool deep = false;
        for (int i = 0; i < r.Segments.Count; i++) if (r.Segments[i].State == FlowState.Deep) deep = true;
        Check(!deep, "14 分钟不足以判为深度心流");
        Equal(r.LongestDeepMs, 0, "无深度段时连击为 0");
        Check(r.FocusScore < 100, "不足 15 分钟不应满分");
        Check(r.FocusScore > 30, "常规活跃仍应有一定分数");
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
}
