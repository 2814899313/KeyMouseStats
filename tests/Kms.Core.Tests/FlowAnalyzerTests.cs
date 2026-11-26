using Kms.Core.Model;
using Kms.Core.Stats;

namespace Kms.Core.Tests;

/// <summary>
/// v3 <c>tests/FlowAnalyzerTests.cs</c> 的 v4 对拍。
/// 纯函数测试：不建窗体、不碰数据库、不读写文件。
/// </summary>
public sealed class FlowAnalyzerTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 11, 23, 9, 55, 0, TimeSpan.FromHours(8));

    private static FlowSessionRow S(long startSec, long durSec, string proc, int cat, long keys) => new()
    {
        StartMs = startSec * 1000,
        EndMs = (startSec + durSec) * 1000,
        ProcessPath = proc,
        ProcessName = proc,
        CategoryId = cat,
        KeyCount = keys,
        ActiveMs = durSec * 1000
    };

    private static FlowSessionRow SAt(DateTimeOffset start, long durSec, string proc, int cat, long keys) => new()
    {
        StartMs = start.ToUnixTimeMilliseconds(),
        EndMs = start.AddSeconds(durSec).ToUnixTimeMilliseconds(),
        ProcessPath = proc,
        ProcessName = proc,
        CategoryId = cat,
        KeyCount = keys,
        ActiveMs = durSec * 1000
    };

    private static List<FlowBurstRow> Bursts(long fromSec, long toSec, long everySec, long keys, double ikiVar)
    {
        List<FlowBurstRow> list = new();
        for (long t = fromSec; t < toSec; t += everySec)
        {
            list.Add(new FlowBurstRow
            {
                StartMs = t * 1000,
                EndMs = (t + everySec) * 1000,
                KeyCount = keys,
                IkiVar = ikiVar,
                IkiAvgMs = 180
            });
        }

        return list;
    }

    private static List<FlowBurstRow> BurstsAt(DateTimeOffset start, int count, int everySec, long keys, double ikiVar)
    {
        List<FlowBurstRow> list = new();
        DateTimeOffset from = start;
        for (int i = 0; i < count; i++)
        {
            list.Add(new FlowBurstRow
            {
                StartMs = from.ToUnixTimeMilliseconds(),
                EndMs = from.AddSeconds(everySec).ToUnixTimeMilliseconds(),
                KeyCount = keys,
                IkiVar = ikiVar,
                IkiAvgMs = 180
            });
            from = from.AddSeconds(everySec);
        }

        return list;
    }

    private static FlowSegment Seg(long startSec, long durSec, FlowState state) => new()
    {
        StartMs = startSec * 1000L,
        EndMs = (startSec + durSec) * 1000L,
        State = state
    };

    private static void Near(double expected, double actual, double tolerance) =>
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"期望 {expected}，实际 {actual}");

    private static bool HasState(FlowReport report, FlowState state)
    {
        for (int i = 0; i < report.Segments.Count; i++)
        {
            if (report.Segments[i].State == state)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void NormalizeRules()
    {
        FlowConfig cfg = new();
        List<FlowSessionRow> raw = new()
        {
            S(0, 0, "a.exe", 0, 1),        // 无效区间
            S(120, 60, "b.exe", 0, 5),     // 乱序
            S(0, 60, "a.exe", 0, 10)
        };
        List<FlowSessionRow> normalized = FlowAnalyzer.Normalize(raw, cfg);
        Assert.Equal(2, normalized.Count);
        Assert.Equal(0, normalized[0].StartMs);
        Assert.True(normalized[1].StartMs > normalized[0].StartMs);

        List<FlowSessionRow> mergedInput = new()
        {
            S(0, 60, "a.exe", 0, 10),
            S(70, 60, "a.exe", 0, 20)
        };
        List<FlowSessionRow> merged = FlowAnalyzer.Normalize(mergedInput, cfg);
        Assert.Single(merged);
        Assert.Equal(30, merged[0].KeyCount);
        Assert.Equal(130_000, merged[0].EndMs);
        Assert.Equal(120_000, merged[0].ActiveMs);
        Assert.Equal(60_000, mergedInput[0].EndMs);   // 输入行不被就地修改

        List<FlowSessionRow> sameAppAtBoundary = new()
        {
            S(0, 60, "a.exe", 0, 10),
            S(80, 60, "a.exe", 0, 20)      // 间隔正好 20s
        };
        Assert.Single(FlowAnalyzer.Normalize(sameAppAtBoundary, cfg));

        List<FlowSessionRow> differentApp = new()
        {
            S(0, 60, "a.exe", 0, 10),
            S(70, 60, "b.exe", 0, 20)
        };
        Assert.Equal(2, FlowAnalyzer.Normalize(differentApp, cfg).Count);

        List<FlowSessionRow> tooFar = new()
        {
            S(0, 60, "a.exe", 0, 10),
            S(120, 60, "a.exe", 0, 20)     // 间隔 60s > 20s
        };
        Assert.Equal(2, FlowAnalyzer.Normalize(tooFar, cfg).Count);

        Assert.True(FlowAnalyzer.IsWorkCategory(0));
        Assert.True(FlowAnalyzer.IsWorkCategory(5));
        Assert.True(FlowAnalyzer.IsWorkCategory(6));
        Assert.True(FlowAnalyzer.IsWorkCategory(7));
        Assert.False(FlowAnalyzer.IsWorkCategory(8));
        Assert.False(FlowAnalyzer.IsWorkCategory(1));
    }

    [Fact]
    public void DeepHappyPath()
    {
        List<FlowSessionRow> sessions = new() { S(0, 1200, "code.exe", 0, 9000) };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 1200, 30, 40, 500), new FlowConfig());

        Assert.True(report.Segments.Count >= 1);
        Assert.True(HasState(report, FlowState.Deep));
        Assert.Equal(1_200_000, report.LongestDeepMs);
        Assert.Equal(1_200_000, report.DeepMs);
        Assert.Equal(100.0, report.FocusScore);
        Assert.Equal(1.0, report.Parts.Quality);
        Assert.Equal(1.0, report.Parts.DeepShare);
        Assert.Equal(1.0, report.Parts.Stickiness);
        Assert.Equal(1.0, report.Parts.Calm);
    }

    [Fact]
    public void DeepNeedsTenMinutes()
    {
        List<FlowSessionRow> nine = new() { S(0, 540, "code.exe", 0, 4000) };
        FlowReport r9 = FlowAnalyzer.Analyze(nine, Bursts(0, 540, 30, 40, 500), new FlowConfig());
        Assert.False(HasState(r9, FlowState.Deep));
        Assert.Equal(0, r9.LongestDeepMs);
        Assert.Equal(0, r9.DeepMs);
        Assert.Equal(540_000, r9.NormalMs);
        Assert.True(r9.LongestFocusMs >= 540_000);
        Assert.True(r9.FocusScore < 100);

        List<FlowSessionRow> ten = new() { S(0, 600, "code.exe", 0, 5000) };
        FlowReport r10 = FlowAnalyzer.Analyze(ten, Bursts(0, 600, 30, 40, 500), new FlowConfig());
        Assert.True(HasState(r10, FlowState.Deep));
        Assert.Equal(600_000, r10.LongestDeepMs);
        Assert.Equal(600_000, r10.DeepMs);
    }

    [Fact]
    public void LenientRhythmAndInput()
    {
        List<FlowSessionRow> sessions = new() { S(0, 1200, "code.exe", 0, 9000) };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 1200, 30, 40, 100_000), new FlowConfig());
        Assert.True(report.LongestDeepMs >= 600_000, "思考停顿不应把深度心流判成碎片");

        List<FlowSessionRow> clicks = new()
        {
            new FlowSessionRow
            {
                StartMs = 0,
                EndMs = 600_000,
                ProcessPath = "code.exe",
                ProcessName = "code.exe",
                CategoryId = 0,
                KeyCount = 10,
                InputCount = 150,
                ActiveMs = 600_000
            }
        };
        List<FlowBurstRow> bursts = new()
        {
            new FlowBurstRow
            {
                StartMs = 0,
                EndMs = 600_000,
                KeyCount = 5,
                IkiAvgMs = 180,
                IkiVar = 500
            }
        };
        FlowReport clickReport = FlowAnalyzer.Analyze(clicks, bursts, new FlowConfig());
        Assert.True(clickReport.LongestDeepMs >= 600_000, "鼠标点击占主导 + 少量键盘应能构成深度专注");
    }

    [Fact]
    public void StickinessCategoriesAndNormal()
    {
        List<FlowSessionRow> sessions = new()
        {
            S(0, 240, "code.exe", 0, 3000),
            S(240, 30, "chrome.exe", 4, 300),
            S(270, 30, "office.exe", 5, 300),
            S(300, 240, "code.exe", 0, 3000),
            S(540, 30, "chrome.exe", 4, 300),
            S(570, 30, "office.exe", 5, 300),
            S(600, 240, "code.exe", 0, 3000),
            S(840, 30, "chrome.exe", 4, 300),
            S(870, 30, "office.exe", 5, 300)
        };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 900, 30, 40, 500), new FlowConfig());
        Assert.True(report.LongestDeepMs >= 600_000, "0.8 粘性 + 3 类别应可判深度心流");

        List<FlowSessionRow> normalInput = new()
        {
            S(0, 144, "code.exe", 0, 2000),
            S(144, 156, "chrome.exe", 4, 2000)
        };
        FlowReport normal = FlowAnalyzer.Analyze(normalInput, Bursts(0, 300, 30, 40, 500), new FlowConfig());
        Assert.True(HasState(normal, FlowState.Normal), "48% 粘性应判常规专注");
        Assert.Equal(0, normal.LongestDeepMs);
        Assert.Equal(0, normal.DeepMs);
        Assert.True(normal.LongestFocusMs >= 300_000, "常规专注应计入最长专注");
    }

    [Fact]
    public void LongestFocusRunCountsNormal()
    {
        List<FlowSegment> segments = new()
        {
            Seg(0, 600, FlowState.Deep),
            Seg(600, 300, FlowState.Normal),
            Seg(900, 60, FlowState.Fragmented),
            Seg(960, 600, FlowState.Deep),
            Seg(1560, 240, FlowState.Idle)
        };
        Assert.Equal(900_000, FlowAnalyzer.LongestFocusRun(segments));
        Assert.Equal(0, FlowAnalyzer.LongestFocusRun(null));
    }

    [Fact]
    public void MicroCheckoutKeepsFlow()
    {
        List<FlowSessionRow> sessions = new()
        {
            S(0, 600, "code.exe", 0, 5000),
            S(600, 15, "wechat.exe", 8, 20),    // 15s 微确认
            S(615, 1185, "code.exe", 0, 9000)
        };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 1800, 30, 40, 500), new FlowConfig());
        Assert.True(report.LongestDeepMs >= 900_000, "微确认不应打断");
        Assert.Equal(1_800_000, report.DeepMs);
    }

    [Fact]
    public void LongBreakInterrupts()
    {
        List<FlowSessionRow> sessions = new()
        {
            S(0, 600, "code.exe", 0, 5000),
            S(660, 1140, "code.exe", 0, 9000)   // 中间空了 60s > 45s
        };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 600, 30, 40, 500), new FlowConfig());

        // 分钟桶口径：60s 空档本身没有活动，不会再生成碎片段；正确语义是「前后两段深度被切断」。
        Assert.True(report.Segments.Count >= 2);
        Assert.True(report.LongestDeepMs < 1_500_000);
        Assert.Equal(1_740_000, report.ActiveMs);
        Assert.Equal(0, report.FragmentedMs);
    }

    [Fact]
    public void UnrelatedSwitchesInterrupt()
    {
        List<FlowSessionRow> sessions = new()
        {
            S(0, 300, "code.exe", 0, 3000),
            S(300, 20, "wechat.exe", 8, 10),
            S(320, 20, "chrome.exe", 4, 10),
            S(340, 20, "qq.exe", 8, 10),
            S(360, 1440, "code.exe", 0, 9000)
        };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 1800, 30, 40, 500), new FlowConfig());
        bool broken = false;
        for (int i = 0; i < report.Segments.Count; i++)
        {
            FlowSegment segment = report.Segments[i];
            if (segment.State != FlowState.Deep
                && segment.StartMs <= 300_000
                && segment.EndMs >= 360_000)
            {
                broken = true;
            }
        }

        Assert.True(broken, "连续 ≥3 个无关窗口必须判为中断");
        Assert.True(report.NormalMs > 0);
    }

    [Fact]
    public void ScoreDecomposition()
    {
        List<FlowSessionRow> sessions = new() { S(0, 1200, "code.exe", 0, 9000) };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 1200, 30, 40, 500), new FlowConfig());
        double expected = Math.Round(
            100.0 * (0.70 * report.Parts.Quality + 0.15 * report.Parts.Stickiness + 0.15 * report.Parts.Calm),
            1);
        Near(expected, report.FocusScore, 0.05);
        Assert.InRange(report.Parts.DeepShare, 0, 1);
        Assert.InRange(report.Parts.NormalShare, 0, 1);
        Assert.InRange(report.Parts.Stickiness, 0, 1);
        Assert.InRange(report.Parts.Calm, 0, 1);
        Assert.InRange(report.FocusScore, 0, 100);
    }

    [Fact]
    public void DegenerateInputs()
    {
        FlowReport empty = FlowAnalyzer.Analyze(new List<FlowSessionRow>(), new List<FlowBurstRow>(), new FlowConfig());
        Assert.Empty(empty.Segments);
        Assert.Equal(0.0, empty.FocusScore);
        Assert.Equal(0, empty.LongestDeepMs);
        Assert.Equal(0, empty.IdleMs);
        Assert.Equal(0, empty.ActiveMs);
        Assert.Equal(FlowState.Idle, empty.StateAt(0));

        FlowReport nulls = FlowAnalyzer.Analyze(null, null, null);
        Assert.NotNull(nulls);
        Assert.Equal(0.0, nulls.FocusScore);
        Assert.Empty(nulls.Sessions);
        Assert.False(FlowAnalyzer.HasMeaningfulInput(null, null));
    }

    [Fact]
    public void ContextCostRules()
    {
        FlowConfig cfg = new();
        List<FlowSessionRow> sessions = new()
        {
            S(0, 600, "code.exe", 0, 5000),       // 主力
            S(600, 120, "wechat.exe", 8, 30),     // 干扰 2 分钟
            S(720, 600, "code.exe", 0, 5000)      // 切回
        };
        List<FlowBurstRow> bursts = Bursts(0, 1200, 30, 10, 500);
        List<ContextSwitchStat> stats = ContextCost.Analyze(sessions, bursts, cfg, 5, 15);
        Assert.Single(stats);
        Assert.Equal(1, stats[0].Count);
        Assert.True(stats[0].MedianLagSeconds >= 120);
        Assert.True(stats[0].AvgLagSeconds > 0);
        Assert.True(stats[0].TotalLagSeconds >= stats[0].MedianLagSeconds);
        Assert.True(ContextCost.IsBrowser("chrome.exe"));
        Assert.True(ContextCost.IsBrowser("MSEDGE.EXE"));
        Assert.False(ContextCost.IsBrowser("code.exe"));
        Assert.False(ContextCost.IsBrowser(null));
        Assert.True(ContextCost.IsDistractor(new FlowSessionRow { CategoryId = 8, ProcessName = "wechat.exe" }));
        Assert.True(ContextCost.IsDistractor(new FlowSessionRow { CategoryId = 4, ProcessName = "firefox.exe" }));
        Assert.False(ContextCost.IsDistractor(new FlowSessionRow { CategoryId = 0, ProcessName = "code.exe" }));
        Assert.False(ContextCost.IsDistractor(null));

        List<ContextSwitchStat> none = ContextCost.Analyze(new List<FlowSessionRow>(), bursts, cfg, 5, 15);
        Assert.Empty(none);

        // 没有突发段时按 ActiveMs 恢复：660s 切回，15s 后判为恢复。
        List<FlowSessionRow> activeSessions = new()
        {
            S(0, 600, "code.exe", 0, 5000),
            S(600, 60, "wechat.exe", 8, 30),
            S(660, 340, "code.exe", 0, 5000)
        };
        List<ContextSwitchStat> activeStats = ContextCost.Analyze(
            activeSessions,
            new List<FlowBurstRow>(),
            cfg,
            5,
            15);
        Assert.Single(activeStats);
        Assert.Equal(1, activeStats[0].Count);
        Assert.Equal(75.0, activeStats[0].MedianLagSeconds);
        Assert.Equal(75.0, activeStats[0].AvgLagSeconds);
    }

    [Fact]
    public void ExtraCoverage()
    {
        List<FlowSessionRow> fragmented = new();
        for (int i = 0; i < 40; i++)
        {
            fragmented.Add(S(i * 30, 20, "app" + (i % 5) + ".exe", i % 2 == 0 ? 0 : 8, 5));
        }

        FlowReport fr = FlowAnalyzer.Analyze(fragmented, Bursts(0, 1200, 60, 5, 500), new FlowConfig());
        Assert.NotEmpty(fr.Segments);
        Assert.True(fr.FocusScore < 65);
        Assert.True(fr.Parts.DeepShare < 0.5);
        Assert.True(fr.FragmentedMs > 0);

        List<FlowSessionRow> twoDeep = new()
        {
            S(0, 1200, "code.exe", 0, 9000),
            S(1800, 1200, "code.exe", 0, 9000)
        };
        FlowReport tr = FlowAnalyzer.Analyze(twoDeep, Bursts(0, 3000, 30, 40, 500), new FlowConfig());
        Assert.True(tr.Segments.Count >= 2);
        Assert.True(tr.LongestDeepMs < tr.TotalMs);
        Assert.True(tr.LongestDeepMs >= 900_000);

        List<FlowSessionRow> deepOnly = new() { S(0, 1200, "code.exe", 0, 9000) };
        FlowReport deep = FlowAnalyzer.Analyze(deepOnly, Bursts(0, 1200, 30, 40, 500), new FlowConfig());
        Assert.True(deep.FocusScore >= fr.FocusScore);

        List<FlowSessionRow> idle = new() { S(0, 1200, "code.exe", 0, 0) };
        FlowReport ir = FlowAnalyzer.Analyze(idle, new List<FlowBurstRow>(), new FlowConfig());
        Assert.True(ir.FocusScore < deep.FocusScore);
        Assert.Equal(0, ir.Parts.DeepShare);
        Assert.Equal(0, ir.ActiveMs);
        Assert.True(ir.IdleMs >= 1_200_000);

        FlowReport spam = FlowAnalyzer.Analyze(deepOnly, Bursts(0, 1200, 10, 200, 5), new FlowConfig());
        Assert.True(spam.Parts.DeepShare < 1, "IKI 方差过低应被排除");
        Assert.True(spam.FocusScore < 100, "连点器不应拿满分");

        FlowConfig loose = new() { DeepMinMinutes = 5 };
        List<FlowSessionRow> fourteen = new() { S(0, 840, "code.exe", 0, 6000) };
        FlowReport lr = FlowAnalyzer.Analyze(fourteen, Bursts(0, 840, 30, 40, 500), loose);
        Assert.True(lr.LongestDeepMs >= 600_000);

        List<FlowSessionRow> multi = new()
        {
            S(0, 600, "code.exe", 0, 5000),
            S(600, 60, "wechat.exe", 8, 10),
            S(660, 600, "code.exe", 0, 5000),
            S(1260, 600, "chrome.exe", 4, 10),
            S(1860, 600, "code.exe", 0, 5000)
        };
        List<ContextSwitchStat> ranked = ContextCost.Analyze(multi, Bursts(0, 3000, 30, 10, 500), new FlowConfig(), 5, 15);
        Assert.True(ranked.Count >= 1);
        for (int i = 1; i < ranked.Count; i++)
        {
            Assert.True(ranked[i - 1].TotalLagSeconds >= ranked[i].TotalLagSeconds);
        }
    }

    [Fact]
    public void IdleWindowNotFragmented()
    {
        List<FlowSessionRow> sessions = new()
        {
            S(0, 600, "code.exe", 0, 5000),
            S(7200, 600, "code.exe", 0, 5000)
        };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 600, 30, 40, 500), new FlowConfig());

        long total = 0;
        for (int i = 0; i < report.Segments.Count; i++)
        {
            total += report.Segments[i].LengthMs;
        }

        Assert.True(total < 1_800_000, "空窗不得被算进三态总时长");
        Assert.Equal(1_200_000, report.ActiveMs);
        Assert.Equal(0, report.FragmentedMs);
        Assert.Equal(7_800_000, report.TotalMs);
        Assert.True(report.Segments.Count >= 1);
    }

    [Fact]
    public void NoOverlapDoubleCount()
    {
        List<FlowSessionRow> sessions = new() { S(0, 9000, "code.exe", 0, 80000) };
        FlowReport report = FlowAnalyzer.Analyze(sessions, Bursts(0, 9000, 30, 40, 500), new FlowConfig());

        long total = 0;
        for (int i = 0; i < report.Segments.Count; i++)
        {
            total += report.Segments[i].LengthMs;
        }

        Assert.True(total <= 9_000_000);
        Near(9_000_000, total, 120_000);
        Assert.Equal(total, report.ActiveMs);
        Assert.Equal(9_000_000, report.DeepMs);
    }

    [Fact]
    public void MergeAcrossGapStaysSplit()
    {
        List<FlowSessionRow> sessions = new()
        {
            S(0, 1200, "code.exe", 0, 9000),
            S(8400, 1200, "code.exe", 0, 9000)
        };
        List<FlowBurstRow> bursts = Bursts(0, 1200, 30, 40, 500);
        bursts.AddRange(Bursts(8400, 9600, 30, 40, 500));
        FlowReport report = FlowAnalyzer.Analyze(sessions, bursts, new FlowConfig());

        Assert.True(report.Segments.Count >= 2, "同态段之间有 2 小时空洞时必须拆成两段");
        Assert.Equal(1_200_000, report.LongestDeepMs);
        Assert.Equal(2_400_000, report.ActiveMs);
        Assert.True(report.ActiveMs <= 2_460_000);
    }

    [Fact]
    public void LongIdleActiveMsNotFragmented()
    {
        FlowSessionRow idle = S(0, 5580, "Gemini.exe", 4, 0);
        idle.ActiveMs = 3633;
        List<FlowSessionRow> sessions = new() { idle };
        FlowReport report = FlowAnalyzer.Analyze(sessions, new List<FlowBurstRow>(), new FlowConfig());

        Assert.True(report.IdleMs >= 5_500_000);
        Assert.Equal(0, report.FragmentedMs);
        Assert.Equal(0, report.Parts.DeepShare);
        Assert.Equal(0, report.ActiveMs);

        FlowSessionRow gemini = S(0, 11401, "Gemini.exe", 4, 3);
        gemini.InputCount = 32;
        gemini.ActiveMs = 29714;
        List<FlowSessionRow> sparse = new() { gemini };
        FlowReport sparseReport = FlowAnalyzer.Analyze(sparse, new List<FlowBurstRow>(), new FlowConfig());
        Assert.True(sparseReport.IdleMs >= 11_000_000);
        Assert.Equal(0, sparseReport.FragmentedMs);
        Assert.Equal(0, sparseReport.ActiveMs);
    }

    [Fact]
    public void MinuteSegmentKeysProrated()
    {
        List<FlowSessionRow> sessions = new() { S(0, 300, "code.exe", 0, 60) };
        List<FlowBurstRow> bursts = new()
        {
            new FlowBurstRow
            {
                StartMs = 30_000,
                EndMs = 90_000,
                KeyCount = 60,
                IkiVar = 500,
                IkiAvgMs = 180
            }
        };
        FlowReport report = FlowAnalyzer.Analyze(sessions, bursts, new FlowConfig());

        long keys = 0;
        long listKeys = 0;
        long minuteCount = 0;
        for (int i = 0; i < report.Segments.Count; i++)
        {
            keys += report.Segments[i].Keys;
            List<long>? minuteKeys = report.Segments[i].MinuteKeys;
            if (minuteKeys is null)
            {
                continue;
            }

            minuteCount += minuteKeys.Count;
            for (int m = 0; m < minuteKeys.Count; m++)
            {
                listKeys += minuteKeys[m];
            }
        }

        Assert.Equal(60, keys);
        Assert.Equal(60, listKeys);
        Assert.Equal(5, minuteCount);
    }

    [Fact]
    public void HasDataRequiresMeaningfulInput()
    {
        FlowSessionRow idle = S(0, 600, "idle.exe", 4, 0);
        idle.ActiveMs = 0;
        List<FlowSessionRow> sessions = new() { idle };
        Assert.False(FlowAnalyzer.HasMeaningfulInput(sessions, new List<FlowBurstRow>()));

        sessions.Add(S(60, 60, "code.exe", 0, 10));
        Assert.True(FlowAnalyzer.HasMeaningfulInput(sessions, new List<FlowBurstRow>()));

        List<FlowBurstRow> bursts = new() { new FlowBurstRow { KeyCount = 5 } };
        Assert.True(FlowAnalyzer.HasMeaningfulInput(new List<FlowSessionRow>(), bursts));

        List<FlowBurstRow> emptyBursts = new() { new FlowBurstRow { KeyCount = 0 } };
        Assert.False(FlowAnalyzer.HasMeaningfulInput(new List<FlowSessionRow>(), emptyBursts));
    }

    [Fact]
    public void NowCutoffTrimsTodayAndKeepsPastData()
    {
        FlowSessionRow session = SAt(BaseTime, 1200, "code.exe", 0, 9000);
        List<FlowSessionRow> sessions = new() { session };
        List<FlowBurstRow> bursts = BurstsAt(BaseTime, 40, 30, 40, 500);

        FlowReport partial = FlowAnalyzer.Analyze(
            sessions,
            bursts,
            new FlowConfig(),
            BaseTime.AddMinutes(5));
        Assert.Single(partial.Normalized);
        Assert.Equal(300_000, partial.TotalMs);
        Assert.Equal(300_000, partial.NormalMs);
        Assert.Equal(0, partial.DeepMs);
        Assert.Equal(BaseTime.AddMinutes(20).ToUnixTimeMilliseconds(), session.EndMs);   // now 裁剪不改写输入行
        Assert.Equal(FlowState.Normal, partial.StateAt(BaseTime.ToUnixTimeMilliseconds()));
        Assert.Equal(FlowState.Normal, partial.StateAt(BaseTime.AddMinutes(5).ToUnixTimeMilliseconds() - 1));
        Assert.Equal(FlowState.Idle, partial.StateAt(BaseTime.AddMinutes(5).ToUnixTimeMilliseconds()));

        FlowReport atStart = FlowAnalyzer.Analyze(sessions, bursts, new FlowConfig(), BaseTime);
        Assert.Empty(atStart.Segments);
        Assert.Equal(0, atStart.TotalMs);

        FlowReport full = FlowAnalyzer.Analyze(sessions, bursts, new FlowConfig());
        Assert.Equal(1_200_000, full.DeepMs);
        Assert.Equal(1_200_000, full.LongestDeepMs);

        FlowReport before = FlowAnalyzer.Analyze(sessions, bursts, new FlowConfig(), BaseTime.AddMinutes(-1));
        Assert.Empty(before.Segments);

        FlowReport after = FlowAnalyzer.Analyze(sessions, bursts, new FlowConfig(), BaseTime.AddHours(1));
        Assert.Equal(1_200_000, after.DeepMs);
    }

    [Fact]
    public void CrossHourAndCrossDayKeepBucketsContinuous()
    {
        FlowSessionRow crossHour = SAt(BaseTime, 1200, "code.exe", 0, 9000);
        FlowReport hourReport = FlowAnalyzer.Analyze(
            new[] { crossHour },
            BurstsAt(BaseTime, 40, 30, 40, 500),
            new FlowConfig());
        Assert.Single(hourReport.Segments);
        Assert.Equal(1_200_000, hourReport.DeepMs);
        Assert.Equal(1_200_000, hourReport.LongestDeepMs);
        Assert.Equal(20, hourReport.Segments[0].MinuteKeys?.Count);
        Assert.Equal(FlowState.Deep, hourReport.StateAt(BaseTime.AddMinutes(10).ToUnixTimeMilliseconds()));
        Assert.Equal(FlowState.Deep, hourReport.Segments[0].State);

        DateTimeOffset justBeforeMidnight = new DateTimeOffset(
            2026, 11, 22, 23, 50, 0, TimeSpan.FromHours(8));
        FlowSessionRow crossDay = SAt(justBeforeMidnight, 1200, "code.exe", 0, 9000);
        FlowReport dayReport = FlowAnalyzer.Analyze(
            new[] { crossDay },
            BurstsAt(justBeforeMidnight, 40, 30, 40, 500),
            new FlowConfig());
        Assert.Single(dayReport.Segments);
        Assert.Equal(20, dayReport.Segments[0].MinuteKeys?.Count);
        Assert.Equal(1_200_000, dayReport.DeepMs);
        Assert.Equal(FlowState.Deep, dayReport.StateAt(justBeforeMidnight.AddMinutes(10).ToUnixTimeMilliseconds()));
        Assert.Equal(FlowState.Idle, dayReport.StateAt(justBeforeMidnight.AddMinutes(20).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void TimeMachineBoundaries()
    {
        FlowSessionRow session = SAt(BaseTime, 1200, "code.exe", 0, 9000);
        FlowReport report = FlowAnalyzer.Analyze(
            new[] { session },
            BurstsAt(BaseTime, 40, 30, 40, 500),
            new FlowConfig());

        long start = BaseTime.ToUnixTimeMilliseconds();
        long end = BaseTime.AddMinutes(20).ToUnixTimeMilliseconds();
        Assert.Equal(FlowState.Deep, report.StateAt(start));
        Assert.Equal(FlowState.Deep, report.StateAt(end - 1));
        Assert.Equal(FlowState.Idle, report.StateAt(start - 1));
        Assert.Equal(FlowState.Idle, report.StateAt(end));
        Assert.Same(report.Segments[0], report.SegmentAt(start));
        Assert.NotNull(report.SegmentAt(end - 1));
        Assert.Null(report.SegmentAt(end));
        Assert.Null(report.SegmentAt(start - 1));
        Assert.NotNull(report.SessionAt(start));
        Assert.Null(report.SessionAt(end));
        Assert.Null(report.SessionAt(start - 1));
        Assert.True(report.TryGetActiveRange(out long firstMs, out long lastMs));
        Assert.Equal(start, firstMs);
        Assert.Equal(end, lastMs);
        Near(1.0, report.DensityAt(start + 1000), 0.0001);

        FlowDiagnostic? first = FlowAnalyzer.DiagnoseNormalized(
            report.Normalized,
            report.Bursts,
            start,
            new FlowConfig());
        Assert.NotNull(first);
        Assert.Equal(start, first.WindowStartMs);
        Assert.Equal(FlowState.Deep, first.State);
        Assert.Equal(1.0, first.Stickiness);

        Assert.Null(FlowAnalyzer.DiagnoseNormalized(report.Normalized, report.Bursts, start - 1, new FlowConfig()));
        Assert.Null(FlowAnalyzer.DiagnoseNormalized(report.Normalized, report.Bursts, end, new FlowConfig()));
        Assert.Null(FlowAnalyzer.DiagnoseAt(new List<FlowSessionRow>(), null, start, new FlowConfig()));
    }

    [Fact]
    public void NonFiniteRhythmValuesStayFinite()
    {
        List<FlowSessionRow> sessions = new() { S(0, 600, "code.exe", 0, 5000) };
        List<FlowBurstRow> nanBursts = new()
        {
            new FlowBurstRow
            {
                StartMs = 0,
                EndMs = 600_000,
                KeyCount = 100,
                IkiAvgMs = double.NaN,
                IkiVar = double.NaN
            }
        };
        FlowReport report = FlowAnalyzer.Analyze(sessions, nanBursts, new FlowConfig());
        Assert.True(double.IsFinite(report.FocusScore));
        Assert.True(double.IsFinite(report.Parts.Quality));
        Assert.Equal(0, report.DeepMs);
        Assert.True(report.FragmentedMs > 0);
        Assert.Equal(0, report.Parts.DeepShare);

        List<FlowBurstRow> zeroAvg = new()
        {
            new FlowBurstRow
            {
                StartMs = 0,
                EndMs = 600_000,
                KeyCount = 100,
                IkiAvgMs = 0,
                IkiVar = 500
            }
        };
        FlowReport zeroReport = FlowAnalyzer.Analyze(sessions, zeroAvg, new FlowConfig());
        Assert.True(zeroReport.DeepMs > 0);
        Assert.True(double.IsFinite(zeroReport.FocusScore));
    }
}
