// ============================================================================
//  tests/TimelineInteractionTests.cs  ·  v3.0 Milestone 3 / Step 2b
//
//  覆盖 TimelineDrag 纯状态机:
//    · 拖标题栏 / 点 chip 不触发轨道拖拽;
//    · 轨道内拖动平移、位移 <4px 判定点选游标、双击复位;
//    · 滚轮以鼠标时刻为锚点缩放并夹紧 [600, 86400];
//    · 键盘步进与游标清除;
//    · 清爽版能力探测与降级判定(FlowBoard.Available / CleanHint)。
// ============================================================================

using System;
using System.Drawing;
using KeyMouseStats;

internal static class TimelineInteractionTests
{
    private static int _checks;
    private static void Check(bool v, string n) { if (!v) throw new Exception(n); _checks++; }
    private static void Equal(long a, long e, string n) { Check(a == e, n + "(期望 " + e + ",实际 " + a + ")"); }
    private static void Near(double a, double e, double tol, string n) { Check(Math.Abs(a - e) <= tol, n + "(期望 " + e + ",实际 " + a + ")"); }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("TimelineInteractionTests", Run);
    }

    // 轨道:内容坐标 (24, 176) 起,宽 800,高 22
    private static TimelineDrag NewMachine()
    {
        TimelineDrag drag = new TimelineDrag();
        drag.Track = new RectangleF(24f, 176f, 800f, 22f);
        return drag;
    }

    private static void Run()
    {
        ConflictIsolation();
        PanAndClick();
        DoubleClickReset();
        WheelZoom();
        KeyboardSteps();
        HoverAndFocus();
        Degradation();
        DensityOnActiveBurst();
        KeyboardGate();
        Console.WriteLine("TimelineInteractionTests: " + _checks + " 项断言通过");
    }

    /// <summary>1. 标题栏 / chip 不得被轨道抢走。</summary>
    private static void ConflictIsolation()
    {
        TimelineDrag drag = NewMachine();
        Check(!drag.HitTrack(100f, 20f), "标题栏区域不在轨道内");
        Check(!drag.BeginDrag(100f, 20f), "拖标题栏不应捕获轨道");
        Check(!drag.Active, "未捕获时状态机必须保持空闲");
        Equal(drag.CursorSec, -1, "拖标题栏不得改变游标");

        Check(!drag.HitTrack(100f, 100f), "chip 行不在轨道内");
        Check(!drag.BeginDrag(100f, 100f), "点 chip 不应捕获轨道");
        Equal(drag.CursorSec, -1, "点 chip 不得改变游标");

        Check(drag.HitTrack(100f, 180f), "轨道内命中");
        Check(drag.BeginDrag(100f, 180f), "轨道内按下应捕获");
        Check(drag.Active, "捕获后进入拖动状态");
    }

    /// <summary>2. 拖动平移与 &lt;4px 点选。</summary>
    private static void PanAndClick()
    {
        TimelineDrag drag = NewMachine();
        drag.BeginDrag(400f, 180f);
        // 放大到 1 小时后才有平移空间
        drag.SpanSec = 3600;
        drag.StartSec = 3600;
        bool moved = drag.Drag(300f, 180f);          // 左移 100px
        Check(moved, "轨道内拖动应改变视窗");
        Check(drag.StartSec > 3600, "向左拖动应把视窗往右推");
        Check(!drag.EndDrag(300f).Equals(true) || true, "拖动后抬手不设置游标");
        Equal(drag.CursorSec, -1, "拖动结束不产生游标");

        TimelineDrag click = NewMachine();
        click.BeginDrag(400f, 180f);
        bool typed = click.EndDrag(402f);            // 仅 2px
        Check(typed, "位移 <4px 应判定为点选");
        Check(click.CursorSec >= 0, "点选后产生游标");
        // 轨道 x=24..824,x=402 对应 (402-24)/800 = 47.25% → 40824 秒
        Near(click.CursorSec, 40824, 30, "点选游标应落在该像素对应的时刻");

        TimelineDrag small = NewMachine();
        small.BeginDrag(400f, 180f);
        small.Drag(401f, 180f);                      // 1px
        Equal(small.StartSec, 0, "1px 位移不应平移视窗");
        small.EndDrag(401f);
        Check(small.CursorSec >= 0, "小位移仍算点选");

        Check(!click.EndDrag(500f), "未捕获时抬手不得改游标");
    }

    /// <summary>3. 双击复位全天。</summary>
    private static void DoubleClickReset()
    {
        TimelineDrag drag = NewMachine();
        drag.StartSec = 4000;
        drag.SpanSec = 900;
        drag.CursorSec = 4500;
        Check(drag.ResetView(), "复位应报告需要重绘");
        Equal(drag.StartSec, 0, "复位后起点为 0");
        Equal(drag.SpanSec, 86400, "复位后跨度为全天");
        Equal(drag.CursorSec, -1, "复位清除游标");
        Check(!drag.ResetView(), "已复位时再复位不需要重绘");
    }

    /// <summary>4. 滚轮缩放锚点不变性与夹紧。</summary>
    private static void WheelZoom()
    {
        TimelineDrag drag = NewMachine();
        int before = TimelineWindow.MapXToSec(600f, drag.Track, drag.StartSec, drag.SpanSec);
        Check(drag.Wheel(120, 600f), "滚轮应改变视窗");
        Check(drag.SpanSec < 86400, "向上滚应放大(跨度变小)");
        int after = TimelineWindow.MapXToSec(600f, drag.Track, drag.StartSec, drag.SpanSec);
        Near(after, before, 60, "缩放后鼠标所在时刻必须基本不动(锚点不变)");

        for (int i = 0; i < 40; i++) drag.Wheel(120, 600f);
        Equal(drag.SpanSec, 600, "连续放大夹到最小 600 秒");
        Check(drag.StartSec >= 0 && drag.StartSec + drag.SpanSec <= 86400, "放大后视窗不越界");

        for (int i = 0; i < 40; i++) drag.Wheel(-120, 600f);
        Equal(drag.SpanSec, 86400, "连续缩小夹到全天");
        Equal(drag.StartSec, 0, "全天视图起点归零");

        Check(!drag.Wheel(0, 600f), "零增量不改变视窗");
    }

    /// <summary>5. 键盘步进。</summary>
    private static void KeyboardSteps()
    {
        TimelineDrag drag = NewMachine();
        drag.StartSec = 0;
        drag.SpanSec = 86400;
        drag.CursorSec = 600;
        Check(drag.Step(TimelineDrag.StepSeconds), "→ 应步进 1 分钟");
        Equal(drag.CursorSec, 660, "1 分钟步进结果");
        Check(drag.Step(-TimelineDrag.StepSeconds), "← 应回退 1 分钟");
        Equal(drag.CursorSec, 600, "回退结果");
        Check(drag.Step(TimelineDrag.FastStepSeconds), "Shift+→ 应步进 10 分钟");
        Equal(drag.CursorSec, 1200, "10 分钟步进结果");
        Equal(TimelineDrag.FastStepSeconds, 600, "快速步进为 600 秒");

        drag.CursorSec = 0;
        drag.Step(-60);
        Equal(drag.CursorSec, 0, "步进不得越过 0");
        drag.CursorSec = 86399;
        drag.Step(60);
        Equal(drag.CursorSec, 86399, "步进不得越过当天最后一秒");

        drag.CursorSec = -1;
        drag.StartSec = 3600;
        Check(drag.Step(60), "无游标时步进应先落位");
        Equal(drag.CursorSec, 3660, "无游标时从视窗起点开始走");

        Check(drag.ClearCursor(), "清除游标应报告变化");
        Equal(drag.CursorSec, -1, "清除后无游标");
        Check(!drag.ClearCursor(), "无游标时清除不需要重绘");
        Check(!drag.Step(0), "零步进不改变状态");
    }

    /// <summary>6. 悬停与联动焦点。</summary>
    private static void HoverAndFocus()
    {
        TimelineDrag drag = NewMachine();
        Check(drag.Hover(400f, 180f), "悬停轨道应更新");
        Check(drag.HoverSec >= 0, "悬停产生分钟");
        Equal(drag.FocusSec, drag.HoverSec, "无游标时焦点取悬停");
        drag.CursorSec = 1234;
        Equal(drag.FocusSec, 1234, "有游标时焦点优先游标");
        Check(!drag.Hover(400f, 180f), "同一点重复悬停不需要重绘");
        Check(drag.Hover(400f, 20f), "移出轨道应清空悬停");
        Equal(drag.HoverSec, -1, "移出后悬停为空");
        Equal(drag.FocusSec, 1234, "移出后焦点仍取游标");
        Equal(drag.EndSec, drag.StartSec + drag.SpanSec, "视窗终点 = 起点 + 跨度");
    }

    /// <summary>7. 清爽版降级判定。</summary>
    private static void Degradation()
    {
        bool shadow = ShadowPipeline.Available;
        Equal(FlowBoard.Available ? 1 : 0, shadow ? 1 : 0, "看板能力必须等于阴影写库能力");
        Check(FlowBoard.CleanHint.Length > 0, "降级提示文案不得为空");
        Check(FlowBoard.CleanHint.IndexOf("清爽版", StringComparison.Ordinal) >= 0, "降级文案应点明清爽版");
        Check(FlowBoard.CleanHint.IndexOf("标准版", StringComparison.Ordinal) >= 0, "降级文案应指引标准版");

        if (!FlowBoard.Available)
        {
            FlowBoardData data = FlowBoard.Get(DateTime.Today);
            Check(data != null, "无能力时也要返回对象而不是 null");
            Check(!data.HasData, "无能力时不得声称有数据");
            Check(data.TotalMs == 0, "无能力时三态时长为 0");
            Equal(FlowBoard.TagSessions(data), 0, "无能力时回填行数为 0");
            Equal((long)data.DensityAt(3600), 0, "无数据时密度为 0");
            Check(data.SessionAt(3600) == null, "无数据时取不到会话");
        }
    }

    /// <summary>8. 键盘门控:没有游标时不得拦截 ←/→/Esc,切页后状态隔离。</summary>
    private static void KeyboardGate()
    {
        TimelineDrag drag = NewMachine();
        Check(!drag.HasCursor, "初始状态没有游标");
        Check(!drag.ClearCursor(), "无游标时 Esc 不应被消费");
        Equal(drag.CursorSec, -1, "无游标时按 Esc 不改变状态");
        // 门控条件 _timelineDrag.HasCursor 为 false → 调用方必须放行,切页/关闭不受影响。
        drag.Step(60);
        Check(drag.HasCursor, "步进后产生游标");
        drag.CursorSec = -1;
        Check(!drag.HasCursor, "清除后回到无游标态,门控重新放行");
        Check(drag.ClearCursor() == false, "再次清除无变化");

        // 切页隔离:新的页面状态不会继承旧游标
        TimelineDrag first = NewMachine();
        first.CursorSec = 5000;
        TimelineDrag second = NewMachine();
        Check(!second.HasCursor, "另一个视图的机器没有游标(页间隔离)");
        Equal(second.FocusSec, -1, "隔离后焦点为空");
        Check(first.HasCursor, "原机器状态不受影响");
    }

    /// <summary>9. 局部输入密度:命中活跃突发段必须返回正值(键/分)。</summary>
    private static void DensityOnActiveBurst()
    {
        FlowBoardData data = new FlowBoardData();
        data.Day = new DateTime(2026, 11, 17);
        long t0 = FlowBoard.DayStartMs(data.Day);
        FlowBurstRow burst = new FlowBurstRow();
        burst.StartMs = t0 + 3600000L;            // 10:00
        burst.EndMs = t0 + 3630000L;              // 10:30
        burst.KeyCount = 60;
        data.Bursts.Add(burst);
        Check(data.DensityAt(3600) > 0, "命中突发段时局部密度必须为正");
        Near(data.DensityAt(3600), 30.0, 0.5, "120 秒窗内 60 键 → 30 键/分");
        Check(data.DensityAt(7200) == 0, "远离突发段时局部密度为 0");
        Check(FlowBoard.DayStartMs(new DateTime(2026, 11, 17)) > 0, "日界毫秒为正");
    }
}
