// ============================================================================
//  tests/FocusBreakTests.cs  ·  v3.3 心流保护型微休息状态机
//  纯内存:不建窗体、不读数据库、不写文件。
// ============================================================================
using System;
using KeyMouseStats;

internal static class FocusBreakTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("FocusBreakTests", Run);
    }

    private static FocusSnapshot Snap(FlowState state, double idleSeconds, bool busy)
    {
        return new FocusSnapshot(state, 0, @"C:\Apps\Code.exe", "Code.exe", 1.0, 0, 20,
            idleSeconds, busy, 1, 0);
    }

    private static DateTime ReachPending(FocusBreak monitor, DateTime now)
    {
        monitor.Update(now, Snap(FlowState.Normal, 0, false), false);
        for (int i = 1; i <= 45; i++)
        {
            now = now.AddMinutes(1);
            monitor.Update(now, Snap(FlowState.Normal, 0, false), false);
        }
        Check(monitor.Pending, "45 分钟活跃后应进入 Pending");
        return now;
    }

    private static void Run()
    {
        PendingAndReady();
        DeepSuppression();
        DndSuppression();
        SnoozeAndMute();
        IdleResetsActive();
        DndMapping();
        Console.WriteLine("FocusBreakTests: " + _checks + " 项断言通过");
    }

    private static void PendingAndReady()
    {
        FocusBreak monitor = new FocusBreak();
        DateTime now = new DateTime(2026, 11, 21, 10, 0, 0);
        now = ReachPending(monitor, now);
        FocusBreakStatus status = monitor.Update(now.AddSeconds(18), Snap(FlowState.Normal, 18, false), false);
        Check(status == FocusBreakStatus.Ready, "退出心流 + 空闲 18s 应触发 Ready");
        Check(!monitor.Pending, "Ready 后应重置 Pending");
        Check(monitor.ActiveSeconds == 0, "Ready 后应重置本轮累计");
        Check(monitor.LastPromptAt == now.AddSeconds(18), "Ready 记录提醒时间");
    }

    private static void DeepSuppression()
    {
        FocusBreak monitor = new FocusBreak();
        DateTime now = new DateTime(2026, 11, 21, 11, 0, 0);
        now = ReachPending(monitor, now);
        Check(monitor.Update(now.AddSeconds(1), Snap(FlowState.Deep, 0, false), false)
            == FocusBreakStatus.Pending, "Deep 中必须保持 Pending");
        Check(monitor.Update(now.AddSeconds(18), Snap(FlowState.Deep, 18, false), false)
            == FocusBreakStatus.Pending, "即使空闲，Deep 也不得弹窗");
        Check(monitor.Update(now.AddSeconds(19), Snap(FlowState.Normal, 18, false), false)
            == FocusBreakStatus.Ready, "退出 Deep 且空闲后应 Ready");
    }

    private static void DndSuppression()
    {
        FocusBreak monitor = new FocusBreak();
        DateTime now = new DateTime(2026, 11, 21, 12, 0, 0);
        now = ReachPending(monitor, now);
        Check(monitor.Update(now.AddSeconds(18), Snap(FlowState.Normal, 18, false), true)
            == FocusBreakStatus.Pending, "全屏/演示/会议 DND 中不得 Ready");
        Check(monitor.Update(now.AddSeconds(19), Snap(FlowState.Normal, 18, false), false)
            == FocusBreakStatus.Ready, "DND 解除后应 Ready");
    }

    private static void SnoozeAndMute()
    {
        FocusBreak monitor = new FocusBreak();
        DateTime now = new DateTime(2026, 11, 21, 13, 0, 0);
        now = ReachPending(monitor, now);
        monitor.Snooze(now);
        Check(monitor.IsSnoozed(now.AddMinutes(9)), "延后 10 分钟内应保持静默");
        Check(monitor.Update(now.AddMinutes(9), Snap(FlowState.Normal, 18, false), false)
            == FocusBreakStatus.Pending, "Snooze 期间不得 Ready");
        Check(monitor.Update(now.AddMinutes(10).AddSeconds(1), Snap(FlowState.Normal, 18, false), false)
            == FocusBreakStatus.Ready, "Snooze 到期后应 Ready");

        FocusBreak muted = new FocusBreak();
        DateTime mutedNow = new DateTime(2026, 11, 21, 14, 0, 0);
        mutedNow = ReachPending(muted, mutedNow);
        muted.MuteToday(mutedNow);
        Check(muted.IsMuted(mutedNow), "今日静音应按日期生效");
        Check(muted.Update(mutedNow.AddSeconds(18), Snap(FlowState.Normal, 18, false), false)
            == FocusBreakStatus.Pending, "今日静音期间不得 Ready");

        DateTime nextDay = mutedNow.AddDays(1);
        Check(muted.Update(nextDay, Snap(FlowState.Normal, 0, false), false) == FocusBreakStatus.None,
            "跨日后今日静音失效并重置本轮");
        for (int i = 1; i <= 45; i++)
        {
            nextDay = nextDay.AddMinutes(1);
            muted.Update(nextDay, Snap(FlowState.Normal, 0, false), false);
        }
        Check(muted.Update(nextDay.AddSeconds(18), Snap(FlowState.Normal, 18, false), false)
            == FocusBreakStatus.Ready, "次日重新累计后可再次提醒");
    }

    private static void IdleResetsActive()
    {
        FocusBreak monitor = new FocusBreak();
        DateTime now = new DateTime(2026, 11, 21, 15, 0, 0);
        monitor.Update(now, Snap(FlowState.Normal, 0, false), false);
        for (int i = 0; i < 5; i++)
        {
            now = now.AddMinutes(4);
            monitor.Update(now, Snap(FlowState.Normal, 0, false), false);
        }
        Check(monitor.ActiveSeconds > 1000, "非空闲活跃应累计");
        monitor.Update(now.AddMinutes(1), Snap(FlowState.Normal, 18, false), false);
        Check(monitor.ActiveSeconds == 0, "自然空闲达到缓冲应重置累计");
        Check(!monitor.Pending, "未达阈值时空闲不应进入 Pending");
    }

    private static void DndMapping()
    {
        Check(FocusDnd.ShouldSuppress(ShellNotification.QunsQuietTime, false, false),
            "QunsQuietTime 应抑制");
        Check(FocusDnd.ShouldSuppress(ShellNotification.QunsBusy, false, false),
            "QunsBusy 应抑制");
        Check(FocusDnd.ShouldSuppress(ShellNotification.QunsRunningD3dFullScreen, false, false),
            "全屏 D3D 应抑制");
        Check(FocusDnd.ShouldSuppress(ShellNotification.QunsPresentationMode, false, false),
            "演示模式应抑制");
        Check(FocusDnd.ShouldSuppress(ShellNotification.QunsAcceptsNotifications, true, false),
            "几何全屏兜底应抑制");
        Check(FocusDnd.ShouldSuppress(-1, false, true), "会议进程探测应抑制");
        Check(!FocusDnd.ShouldSuppress(ShellNotification.QunsAcceptsNotifications, false, false),
            "普通可通知状态不抑制");
    }
}
