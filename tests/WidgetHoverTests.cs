// ============================================================================
//  tests/WidgetHoverTests.cs  ·  v3.0 Milestone 2 / Step 2(3.4)
//
//  验证悬浮窗顶部微控件的纯函数几何:
//   1. 四个分页圆点在分隔线中段等距排布,⤢ 展开图标贴在右端;
//   2. 命中判定返回 1..4 / 5 / 0,命中区之间不重叠;
//   3. ShouldArmDrag 是「命中即不武装拖拽」的唯一判据(命中微控件时为 false,
//      从而既不会拖窗、也不会顺带翻页);
//   4. 缩放系数 s 变化时命中区同步缩放(高 DPI 下仍能点中)。
//
//  纯几何用例:不创建窗体、不装钩子、不读写任何文件。
// ============================================================================

using System;
using System.Drawing;
using KeyMouseStats;

internal static class WidgetHoverTests
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
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("WidgetHoverTests", Run);
    }

    private static void Run()
    {
        DotGeometry();
        HitGeometry();
        NoOverlap();
        DragArming();
        ScaleAwareness();
        Console.WriteLine("WidgetHoverTests: " + _checks + " 项断言通过");
    }

    /// <summary>小部件默认客户区:248 × 208。</summary>
    private static readonly Size Client = new Size(248, 208);

    // 1. 几何排布
    private static void DotGeometry()
    {
        Equal(WidgetChrome.DotCount, 4, "应有四个分页圆点");
        Equal(WidgetChrome.None, 0, "None 常量");
        Equal(WidgetChrome.Expand, 5, "Expand 常量");
        Check(WidgetChrome.DividerY == 36f, "分隔线在 y=36(与既有绘制一致)");

        // 四个圆点应严格左到右递增,且中心落在分隔线上。
        float previous = float.MinValue;
        for (int i = 0; i < WidgetChrome.DotCount; i++)
        {
            RectangleF rect = WidgetChrome.DotRect(Client, 1f, i);
            Check(rect.X > previous, "第 " + i + " 个圆点应在第 " + (i - 1) + " 个右侧");
            previous = rect.X;
            Check(rect.Contains(rect.X + rect.Width / 2f, 36f), "圆点命中区应覆盖分隔线 y=36");
        }

        // 整体以窗口水平中点为轴。
        RectangleF first = WidgetChrome.DotRect(Client, 1f, 0);
        RectangleF last = WidgetChrome.DotRect(Client, 1f, 3);
        float centerFirst = first.X + first.Width / 2f;
        float centerLast = last.X + last.Width / 2f;
        Check(Math.Abs((centerFirst + centerLast) / 2f - Client.Width / 2f) < 0.01f,
            "四个圆点应关于窗口水平中点对称");

        // ⤢ 贴在右端。
        RectangleF expand = WidgetChrome.ExpandRect(Client, 1f);
        Check(expand.Right <= Client.Width, "展开图标不应超出右边界");
        Check(expand.Right > Client.Width - 30, "展开图标应贴近右端");
    }

    // 2. 命中判定
    private static void HitGeometry()
    {
        for (int i = 0; i < WidgetChrome.DotCount; i++)
        {
            RectangleF rect = WidgetChrome.DotRect(Client, 1f, i);
            int cx = (int)Math.Round(rect.X + rect.Width / 2f);
            int cy = (int)Math.Round(rect.Y + rect.Height / 2f);
            Equal(WidgetChrome.HitHoverChrome(Client, 1f, new Point(cx, cy)), i + 1,
                "第 " + i + " 个圆点的中心应命中 " + (i + 1));
        }

        RectangleF box = WidgetChrome.ExpandRect(Client, 1f);
        Equal(WidgetChrome.HitHoverChrome(Client, 1f,
            new Point((int)Math.Round(box.X + box.Width / 2f), (int)Math.Round(box.Y + box.Height / 2f))),
            WidgetChrome.Expand, "⤢ 中心应命中展开");

        // 非命中区:标题处、数值区、左下角。
        Equal(WidgetChrome.HitHoverChrome(Client, 1f, new Point(20, 12)), WidgetChrome.None, "标题区不命中");
        Equal(WidgetChrome.HitHoverChrome(Client, 1f, new Point(120, 80)), WidgetChrome.None, "数值区不命中");
        Equal(WidgetChrome.HitHoverChrome(Client, 1f, new Point(4, 200)), WidgetChrome.None, "左下角不命中");
        Equal(WidgetChrome.HitHoverChrome(Client, 1f, new Point(0, 0)), WidgetChrome.None, "左上角不命中");
    }

    // 3. 命中区不重叠(否则点击会落到错误的控件上)
    private static void NoOverlap()
    {
        RectangleF expand = WidgetChrome.ExpandRect(Client, 1f);
        for (int i = 0; i < WidgetChrome.DotCount; i++)
        {
            RectangleF dot = WidgetChrome.DotRect(Client, 1f, i);
            Check(!dot.IntersectsWith(expand), "圆点 " + i + " 不得与 ⤢ 重叠");
            for (int j = i + 1; j < WidgetChrome.DotCount; j++)
                Check(!dot.IntersectsWith(WidgetChrome.DotRect(Client, 1f, j)),
                    "圆点 " + i + " 不得与圆点 " + j + " 重叠");
        }
    }

    // 4. 命中即不武装拖拽
    private static void DragArming()
    {
        Check(WidgetChrome.ShouldArmDrag(WidgetChrome.None), "空白处应允许武装拖拽");
        for (int id = 1; id <= WidgetChrome.DotCount; id++)
            Check(!WidgetChrome.ShouldArmDrag(id), "命中圆点 " + id + " 时不得武装拖拽");
        Check(!WidgetChrome.ShouldArmDrag(WidgetChrome.Expand), "命中 ⤢ 时不得武装拖拽");

        // 与命中判定串起来:每个圆点中心都必须走「不武装」分支。
        for (int i = 0; i < WidgetChrome.DotCount; i++)
        {
            RectangleF rect = WidgetChrome.DotRect(Client, 1f, i);
            Point center = new Point((int)Math.Round(rect.X + rect.Width / 2f),
                                     (int)Math.Round(rect.Y + rect.Height / 2f));
            Check(!WidgetChrome.ShouldArmDrag(WidgetChrome.HitHoverChrome(Client, 1f, center)),
                "圆点 " + i + " 上按下不应进入拖拽");
        }
        Check(WidgetChrome.ShouldArmDrag(WidgetChrome.HitHoverChrome(Client, 1f, new Point(120, 80))),
            "空白处按下应进入拖拽(保留原有手感)");
    }

    // 5. 缩放感知
    private static void ScaleAwareness()
    {
        for (int i = 0; i < WidgetChrome.DotCount; i++)
        {
            RectangleF baseRect = WidgetChrome.DotRect(Client, 1f, i);
            RectangleF scaled = WidgetChrome.DotRect(new Size(372, 312), 1.5f, i);
            Check(scaled.Width > baseRect.Width, "放大后命中区应同步变大");
            Point center = new Point((int)Math.Round(scaled.X + scaled.Width / 2f),
                                     (int)Math.Round(scaled.Y + scaled.Height / 2f));
            Equal(WidgetChrome.HitHoverChrome(new Size(372, 312), 1.5f, center), i + 1,
                "1.5 倍缩放下仍应命中第 " + i + " 个圆点");
        }

        // 非法缩放不应崩溃或返回错误命中。
        Equal(WidgetChrome.HitHoverChrome(Client, 0f, new Point(120, 80)), WidgetChrome.None,
            "s=0 时按 1 倍处理且不命中");
        Check(WidgetChrome.DotRect(Client, -1f, 0).Width > 0, "负缩放应按 1 倍兜底");
    }
}
