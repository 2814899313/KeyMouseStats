// ============================================================================
//  tests/ClickChannelUiTests.cs  ·  v3.0 Milestone 2 / Step 1(3.3)
//
//  覆盖「点击热区」三键通道 UI 里所有可纯函数化的判定:
//   1. chip id 521..524 ↔ 通道 0..3 的派发与越界夹紧;
//   2. 缓存签名片段随通道变化(否则离屏位图不刷新);
//   3. 历史提示胶囊优先级高于「还没有点击位置数据」空态;
//   4. ChannelScale 在 channel=0 时与旧 HeatScale 逐位一致(不破坏 v2.4.0 视图);
//   5. 三条新色阶彼此可区分、且沿色阶单调变亮。
//
//  纯托管用例:不创建窗体、不读数据文件、不触碰任何用户目录。
// ============================================================================

using System;
using System.Drawing;
using KeyMouseStats;

internal static class ClickChannelUiTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + "(期望 " + expected + ",实际 " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("ClickChannelUiTests", Run);
    }

    private static void Run()
    {
        ChipDispatch();
        CacheSignature();
        LegacyPillPriority();
        ChannelScaleMatchesLegacy();
        ChannelScaleDistinct();
        ChannelTexts();
        Console.WriteLine("ClickChannelUiTests: " + _checks + " 项断言通过");
    }

    /// <summary>1. chip id ↔ 通道。</summary>
    private static void ChipDispatch()
    {
        Equal(ClickChannelUi.ChannelChipIds.Length, 4, "应有四档 chip");
        for (int i = 0; i < 4; i++)
        {
            Equal(ClickChannelUi.ChannelChipIds[i], 521 + i, "chip id 应为 521..524");
            Check(ClickChannelUi.IsChannelChip(521 + i), "521..524 应被识别为通道 chip");
            Equal(ClickChannelUi.ChannelOfChip(521 + i), i, "chip 应映射到通道 " + i);
            Equal(ClickChannelUi.ChipOfChannel(i), 521 + i, "通道应映射回 chip " + (521 + i));
        }
        Check(!ClickChannelUi.IsChannelChip(520), "520 不属于通道 chip");
        Check(!ClickChannelUi.IsChannelChip(525), "525 不属于通道 chip");
        Check(!ClickChannelUi.IsChannelChip(517), "517(平滑开关)不属于通道 chip");
        Check(!ClickChannelUi.IsChannelChip(530), "530(屏幕 chip)不属于通道 chip");

        Equal(ClickChannelUi.Clamp(-3), 0, "负通道夹到 0");
        Equal(ClickChannelUi.Clamp(0), 0, "0 原样");
        Equal(ClickChannelUi.Clamp(3), 3, "3 原样");
        Equal(ClickChannelUi.Clamp(9), 3, "越界通道夹到 3");
        Equal(ClickChannelUi.ChannelOfChip(999), 0, "非通道 chip 回落到全部点击");
    }

    /// <summary>2. 缓存签名必须随通道变化,否则切档后位图/聚合不刷新。</summary>
    private static void CacheSignature()
    {
        Check(string.Equals(ClickChannelUi.StampSegment(0), "C0", StringComparison.Ordinal), "通道 0 签名应为 C0");
        Check(string.Equals(ClickChannelUi.StampSegment(3), "C3", StringComparison.Ordinal), "通道 3 签名应为 C3");
        Check(string.Equals(ClickChannelUi.StampSegment(7), "C3", StringComparison.Ordinal), "越界签名夹到 C3");

        for (int a = 0; a < 4; a++)
        {
            for (int b = a + 1; b < 4; b++)
            {
                Check(!string.Equals(ClickChannelUi.StampSegment(a), ClickChannelUi.StampSegment(b), StringComparison.Ordinal),
                    "通道 " + a + " 与 " + b + " 的缓存签名必须不同");
            }
        }
    }

    /// <summary>3. 胶囊优先于空态。</summary>
    private static void LegacyPillPriority()
    {
        Check(!ClickChannelUi.ShouldShowLegacyPill(0, true), "全部点击不显示历史胶囊");
        Check(!ClickChannelUi.ShouldShowLegacyPill(0, false), "全部点击无历史也不显示");
        Check(ClickChannelUi.ShouldShowLegacyPill(1, true), "左键 + 有历史 → 显示胶囊");
        Check(ClickChannelUi.ShouldShowLegacyPill(2, true), "右键 + 有历史 → 显示胶囊");
        Check(ClickChannelUi.ShouldShowLegacyPill(3, true), "中键 + 有历史 → 显示胶囊");
        Check(!ClickChannelUi.ShouldShowLegacyPill(1, false), "左键无历史 → 不显示胶囊");

        // 核心:通道空、但有历史时,胶囊压过空态。
        Check(!ClickChannelUi.ShouldShowEmpty(1, true, 0), "有历史时不得显示「还没有点击位置数据」");
        Check(ClickChannelUi.ShouldShowEmpty(1, false, 0), "无历史且无数据时显示空态");
        Check(ClickChannelUi.ShouldShowEmpty(0, false, 0), "全部点击无数据时显示空态");
        Check(ClickChannelUi.ShouldShowEmpty(0, true, 0), "全部点击 total=0 时照常显示空态(胶囊只在单键视图)");
        Check(!ClickChannelUi.ShouldShowEmpty(1, true, 5), "有数据时不显示空态");
        Check(!ClickChannelUi.ShouldShowEmpty(1, false, 5), "有数据时不显示空态");

        Check(ClickChannelUi.LegacyPillText.Length > 0, "胶囊文案不应为空");
        Check(ClickChannelUi.LegacyPillText.IndexOf("全部点击", StringComparison.Ordinal) >= 0, "胶囊应指明「全部点击」");
    }

    /// <summary>4. channel=0 必须与旧 HeatScale 逐位一致。</summary>
    private static void ChannelScaleMatchesLegacy()
    {
        long[] maxes = new long[] { 1, 7, 1000 };
        long[] counts = new long[] { 0, 1, 2, 9, 12, 500, 1000 };
        int compared = 0;
        for (int m = 0; m < maxes.Length; m++)
        {
            for (int c = 0; c < counts.Length; c++)
            {
                Color actual = ChannelScale.For(0, counts[c], maxes[m]);
                Color expected = LegacyHeat(counts[c], maxes[m]);
                Check(actual.ToArgb() == expected.ToArgb(),
                    "通道 0 必须与旧 HeatScale 完全一致(count=" + counts[c] + ",max=" + maxes[m] + ")");
                compared++;
            }
        }
        Check(compared >= 15, "应覆盖足够多的取值组合");

        // channel 0 的 At 也必须直接委托 HeatScale。
        for (int i = 0; i <= 20; i++)
        {
            double fraction = i / 20.0;
            Check(ChannelScale.At(0, fraction).ToArgb() == HeatScale.At(fraction).ToArgb(),
                "通道 0 的 At 应逐位等于 HeatScale.At(" + fraction + ")");
        }
    }

    /// <summary>与 ClickHeat.Heat 同构的旧算法,用来钉死「零漂移」。</summary>
    private static Color LegacyHeat(long count, long max)
    {
        if (count <= 0 || max <= 0) return HeatScale.Zero;
        double fraction = Math.Log(1.0 + count) / Math.Log(1.0 + max);
        return HeatScale.At(Math.Pow(Math.Min(1, fraction), 0.85));
    }

    /// <summary>5. 三条新色阶可区分且单调变亮。</summary>
    private static void ChannelScaleDistinct()
    {
        for (int c = 1; c < 4; c++)
        {
            double low = Luma(ChannelScale.At(c, 0.0));
            double mid = Luma(ChannelScale.At(c, 0.5));
            double high = Luma(ChannelScale.At(c, 1.0));
            Check(high > low + 30, "通道 " + c + " 高端应明显亮于低端(低 " + low.ToString("0") + " / 高 " + high.ToString("0") + ")");
            Check(mid > low && high > mid, "通道 " + c + " 应单调变亮");
        }

        // 四档在同一分位上的颜色必须互不相同。
        double[] fractions = new double[] { 0.25, 0.5, 0.75 };
        for (int f = 0; f < fractions.Length; f++)
        {
            for (int a = 0; a < 4; a++)
            {
                for (int b = a + 1; b < 4; b++)
                {
                    Check(ChannelScale.At(a, fractions[f]).ToArgb() != ChannelScale.At(b, fractions[f]).ToArgb(),
                        "通道 " + a + " 与 " + b + " 在 " + fractions[f] + " 处的颜色必须不同");
                }
            }
        }

        // 名称与越界夹紧。
        Equal(ChannelScale.Names.Length, 4, "四档应有四个名字");
        Check(string.Equals(ChannelScale.Names[0], "全部点击", StringComparison.Ordinal), "0 = 全部点击");
        Check(string.Equals(ChannelScale.Names[1], "左键", StringComparison.Ordinal), "1 = 左键");
        Check(string.Equals(ChannelScale.Names[2], "右键", StringComparison.Ordinal), "2 = 右键");
        Check(string.Equals(ChannelScale.Names[3], "中键", StringComparison.Ordinal), "3 = 中键");
        Check(string.Equals(ChannelScale.NameOf(9), "全部点击", StringComparison.Ordinal), "越界名称回落全部点击");

        Equal(ChannelScale.All, 0, "All 常量");
        Equal(ChannelScale.Left, 1, "Left 常量");
        Equal(ChannelScale.Right, 2, "Right 常量");
        Equal(ChannelScale.Middle, 3, "Middle 常量");
        Equal(ChannelScale.Count, 4, "Count 常量");
    }

    /// <summary>6. 文案随通道。</summary>
    private static void ChannelTexts()
    {
        Check(string.Equals(ClickChannelUi.TotalLabel(0), "有位置的点击", StringComparison.Ordinal), "通道 0 摘要文案不变");
        Check(string.Equals(ClickChannelUi.TotalLabel(1), "左键点击数", StringComparison.Ordinal), "通道 1 摘要文案");
        Check(string.Equals(ClickChannelUi.TotalLabel(2), "右键点击数", StringComparison.Ordinal), "通道 2 摘要文案");
        Check(string.Equals(ClickChannelUi.TotalLabel(3), "中键点击数", StringComparison.Ordinal), "通道 3 摘要文案");

        Check(string.Equals(ClickChannelUi.MostHeading(0), "点击最多的区域", StringComparison.Ordinal), "通道 0 标题不变");
        Check(string.Equals(ClickChannelUi.MostHeading(1), "左键最常点击", StringComparison.Ordinal), "通道 1 标题");
        Check(string.Equals(ClickChannelUi.MostHeading(2), "右键最常点击", StringComparison.Ordinal), "通道 2 标题");
        Check(string.Equals(ClickChannelUi.MostHeading(3), "中键最常点击", StringComparison.Ordinal), "通道 3 标题");
        Check(string.Equals(ClickChannelUi.TotalLabel(-5), "有位置的点击", StringComparison.Ordinal), "越界回落全部点击");
    }

    private static double Luma(Color color)
    {
        return 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
    }
}
