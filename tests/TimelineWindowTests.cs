// ============================================================================
//  tests/TimelineWindowTests.cs  ·  v3.0 Milestone 3 / Step 1
//  纯数学:缩放锚点不变性、边界夹紧、坐标往返。
// ============================================================================
using System;
using System.Drawing;
using KeyMouseStats;

internal static class TimelineWindowTests
{
    private static int _checks;
    private static void Check(bool v, string n) { if (!v) throw new Exception(n); _checks++; }
    private static void Equal(long a, long e, string n) { Check(a == e, n + "(期望 " + e + ",实际 " + a + ")"); }
    private static void Near(double a, double e, double tol, string n) { Check(Math.Abs(a - e) <= tol, n + "(期望 " + e + ",实际 " + a + ")"); }

    private static void Main() { Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("TimelineWindowTests", Run); }

    private static readonly RectangleF Track = new RectangleF(100f, 300f, 800f, 34f);

    private static void Run()
    {
        Clamps();
        Mapping();
        ZoomAnchor();
        PanBounds();
        MoreCases();
        Console.WriteLine("TimelineWindowTests: " + _checks + " 项断言通过");
    }

    private static void Clamps()
    {
        Equal(TimelineWindow.DaySeconds, 86400, "一天 86400 秒");
        Equal(TimelineWindow.MinSpanSeconds, 600, "最小跨度 600 秒");
        Equal(TimelineWindow.MaxSpanSeconds, 86400, "最大跨度 86400 秒");
        Equal(TimelineWindow.ClampSpan(1), 600, "过小跨度抬到 600");
        Equal(TimelineWindow.ClampSpan(999999), 86400, "过大跨度截到 86400");
        Equal(TimelineWindow.ClampSpan(3600), 3600, "合法跨度原样");
        Equal(TimelineWindow.ClampStart(-100, 600), 0, "起点不得为负");
        Equal(TimelineWindow.ClampStart(99999, 600), 85800, "起点不得越过 86400-span");
        Equal(TimelineWindow.ClampStart(99999, 86400), 0, "整日跨度起点只能为 0");
        Equal(TimelineWindow.Clamp(5, 0, 3), 3, "Clamp 上限");
        Equal(TimelineWindow.Clamp(-5, 0, 3), 0, "Clamp 下限");
    }

    private static void Mapping()
    {
        Near(TimelineWindow.MapSecToX(0, Track, 0, 86400), 100, 0.01, "起点映射到轨道左端");
        Near(TimelineWindow.MapSecToX(86400, Track, 0, 86400), 900, 0.01, "终点映射到轨道右端");
        Near(TimelineWindow.MapSecToX(43200, Track, 0, 86400), 500, 0.01, "正午映射到中心");

        for (int i = 0; i <= 10; i++)
        {
            int sec = i * 8640;
            float x = TimelineWindow.MapSecToX(sec, Track, 0, 86400);
            int back = TimelineWindow.MapXToSec(x, Track, 0, 86400);
            Near(back, sec, 60, "坐标往返误差应在一分钟内(秒=" + sec + ")");
        }

        Equal(TimelineWindow.MapXToSec(-5000f, Track, 0, 86400), 0, "轨道左侧夹到起点");
        Equal(TimelineWindow.MapXToSec(99999f, Track, 0, 86400), 86400, "轨道右侧夹到终点");
        Near(TimelineWindow.MapSecToX(10, new RectangleF(0, 0, 0, 0), 0, 86400), 0, 0.01, "零宽轨道退回左端");
        Equal(TimelineWindow.MapXToSec(10, new RectangleF(0, 0, 0, 0), 300, 600), 300, "零宽轨道退回起点");

        int start = 3600;
        Near(TimelineWindow.MapSecToX(3600, Track, start, 600), 100, 0.01, "视窗起点贴左端");
        Near(TimelineWindow.MapSecToX(4200, Track, start, 600), 900, 0.01, "视窗终点贴右端");
    }

    private static void ZoomAnchor()
    {
        int start, span;
        // 放大一档:86400 → 57600,锚点 36000 的相对位置应保持
        TimelineWindow.Zoom(0, 86400, 36000, 1, out start, out span);
        Equal(span, 57600, "放大一档后跨度为 57600");
        double beforeRatio = 36000.0 / 86400.0;
        double afterRatio = (36000 - start) * 1.0 / span;
        Near(afterRatio, beforeRatio, 0.001, "缩放后锚点相对位置不变");

        // 缩小一档应回到更大跨度,并夹在 86400
        TimelineWindow.Zoom(0, 57600, 36000, -1, out start, out span);
        Equal(span, 86400, "缩小到上限后夹为 86400");
        Equal(start, 0, "整日跨度起点归零");

        // 连续放大应夹在 600
        int s2, sp2;
        TimelineWindow.Zoom(0, 86400, 43200, 50, out s2, out sp2);
        Equal(sp2, 600, "连续放大夹到最小跨度");
        Check(s2 >= 0 && s2 + sp2 <= 86400, "夹紧后视窗不越界");

        // 锚点越界时先夹再缩放
        TimelineWindow.Zoom(0, 3600, 99999, 1, out start, out span);
        Check(start + span <= 86400, "越界锚点缩放后仍不越界");
    }

    private static void PanBounds()
    {
        int p1 = TimelineWindow.Pan(0, 3600, 400f, Track);
        Check(p1 >= 0, "向左拖不得为负");
        int p2 = TimelineWindow.Pan(0, 3600, -400f, Track);
        Check(p2 > 0, "向右拖应推进视窗");
        int p3 = TimelineWindow.Pan(0, 3600, -999999f, Track);
        Equal(p3, 86400 - 3600, "向右拖到底夹在 86400-span");
        int p4 = TimelineWindow.Pan(0, 86400, -400f, Track);
        Equal(p4, 0, "整日跨度无法平移");
        Equal(TimelineWindow.Pan(500, 3600, 0f, Track), 500, "零位移不改变视窗");
    }

    private static void MoreCases()
    {
        for (int i = 0; i <= 23; i++)
        {
            int sec = i * 3600;
            float x = TimelineWindow.MapSecToX(sec, Track, 0, 86400);
            int back = TimelineWindow.MapXToSec(x, Track, 0, 86400);
            Check(Math.Abs(back - sec) <= 120, "整点往返误差 <=2 分钟(时=" + i + ")");
        }
        int start, span;
        TimelineWindow.Zoom(0, 7200, 10800, 1, out start, out span);
        Equal(span, 4800, "7200 放大一档到 4800");
        Check(start >= 0 && start + span <= 86400, "放大后视窗合法");
        TimelineWindow.Zoom(0, 600, 300, -1, out start, out span);
        Equal(span, 900, "600 缩小一档到 900");
        Equal(TimelineWindow.Pan(85800, 600, -999999f, Track), 85800, "已在最右则不再平移");
        Near(TimelineWindow.MapSecToX(300, Track, 0, 600), 100.0 + 300.0 * 800.0 / 600.0, 0.01, "小跨度映射正确");
        for (int s = 600; s <= 86400; s += 8580)
            Equal(TimelineWindow.ClampSpan(s), s, "合法跨度原样保留(" + s + ")");
    }
}
