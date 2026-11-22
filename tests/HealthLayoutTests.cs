// ============================================================================
//  tests/HealthLayoutTests.cs  ·  v3.4.1 现代外设健康布局
// ============================================================================
using System;
using System.Drawing;
using KeyMouseStats;

internal static class HealthLayoutTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Near(float actual, float expected, string name)
    {
        Check(Math.Abs(actual - expected) < 0.6f, name + " (expected " + expected + ", got " + actual + ")");
    }
    private static void Inside(RectangleF inner, RectangleF outer, string name)
    {
        Check(inner.X >= outer.X - 0.6f && inner.Y >= outer.Y - 0.6f
            && inner.Right <= outer.Right + 0.6f && inner.Bottom <= outer.Bottom + 0.6f, name);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("HealthLayoutTests", Run);
    }

    private static void Run()
    {
        CheckLayout(482f, false, "100%");
        CheckLayout(430f, false, "150% / 短窗口");
        CheckLayout(404f, true, "175% / 紧凑");
        CheckLayout(360f, true, "极短窗口");
        Console.WriteLine("HealthLayoutTests: " + _checks + " 项断言通过");
    }

    private static void CheckLayout(float cardH, bool compact, string label)
    {
        HealthLayoutResult layout = HealthLayout.Compute(832f, 24f, 104f, cardH);
        Near(layout.KeyboardCard.X, 24f, label + " 左卡 X");
        Near(layout.KeyboardCard.Width, 408f, label + " 单卡宽");
        Near(layout.MouseCard.X, 24f + 408f + 16f, label + " 右卡 X");
        Near(layout.KeyboardCard.Height, cardH, label + " 卡高");
        Check(layout.Compact == compact, label + " 紧凑标记");

        Inside(layout.KeyboardPills, layout.KeyboardCard, label + " 键盘胶囊在卡内");
        Inside(layout.MousePills, layout.MouseCard, label + " 鼠标胶囊在卡内");
        Inside(layout.KeyboardHero, layout.KeyboardCard, label + " 键盘主数字在卡内");
        Inside(layout.KeyboardList, layout.KeyboardCard, label + " 键盘列表在卡内");
        Inside(layout.KeyboardMeta, layout.KeyboardCard, label + " 键盘 footer 在卡内");
        Inside(layout.MouseMetrics, layout.MouseCard, label + " 鼠标双数字在卡内");
        Inside(layout.MouseStatus, layout.MouseCard, label + " 鼠标状态在卡内");
        Inside(layout.MouseStats, layout.MouseCard, label + " 鼠标中/侧键在卡内");
        Inside(layout.MouseSpark, layout.MouseCard, label + " 鼠标火花线在卡内");
        Inside(layout.MouseMeta, layout.MouseCard, label + " 鼠标 footer 在卡内");
        Inside(layout.KeyboardEdit, layout.KeyboardCard, label + " 键盘编辑浮层在卡内");
        Inside(layout.MouseEdit, layout.MouseCard, label + " 鼠标编辑浮层在卡内");

        Check(layout.KeyboardPills.Bottom <= layout.KeyboardHero.Y, label + " 键盘胶囊不压主数字");
        Check(layout.KeyboardHero.Bottom <= layout.KeyboardList.Y, label + " 键盘主数字不压列表");
        Check(layout.KeyboardList.Bottom <= layout.KeyboardMeta.Y, label + " 键盘列表不压 footer");
        Check(layout.MouseMetrics.Bottom <= layout.MouseStatus.Y, label + " 鼠标双数字不压状态");
        Check(layout.MouseStatus.Bottom <= layout.MouseStats.Y, label + " 状态不压中/侧键");
        Check(layout.MouseStats.Bottom <= layout.MouseSpark.Y, label + " 中/侧键不压火花线");
        Check(layout.MouseSpark.Bottom <= layout.MouseMeta.Y, label + " 火花线不压 footer");
        Check(layout.ListRowHeight >= 24f, label + " TOP 行高下限");
        Check(layout.MouseMetricWidth > 120f, label + " 双数字列宽");
        Check(layout.KeyboardEdit.Width >= 280f && layout.KeyboardEdit.Height >= 180f, label + " 编辑浮层尺寸");
    }
}
