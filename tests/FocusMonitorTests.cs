// ============================================================================
//  tests/FocusMonitorTests.cs  ·  v3.3 纯内存实时心流状态机
//  只使用合成时间与合成输入,不建窗体、不读数据库、不写文件。
// ============================================================================
using System;
using KeyMouseStats;

internal static class FocusMonitorTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("FocusMonitorTests", Run);
    }

    private static FocusSnapshot S(FocusMonitor monitor, long second, long keys, string app,
        uint idleMs, bool busy)
    {
        return monitor.Sample(second * 1000L, keys, 0, app, idleMs, busy);
    }

    private static FocusSnapshot WarmDeep(FocusMonitor monitor)
    {
        FocusSnapshot snapshot = null;
        for (int i = 0; i <= 30; i++)
            snapshot = S(monitor, i, 20, @"C:\Apps\Code.exe", 0, false);
        return snapshot;
    }

    private static void Run()
    {
        DeepSnapshot();
        NormalAndFragmented();
        HysteresisAndFocusReset();
        IdleAndWindowPrune();
        Console.WriteLine("FocusMonitorTests: " + _checks + " 项断言通过");
    }

    private static void DeepSnapshot()
    {
        FocusMonitor monitor = new FocusMonitor();
        FocusSnapshot snapshot = WarmDeep(monitor);
        Check(snapshot.State == FlowState.Deep, "稳定应用 + 充足输入 + 低切换应判 Deep");
        Equal(snapshot.Input5m, 20, "5 分钟有效输入");
        Equal(snapshot.Switches5m, 0, "稳定应用无切换");
        Check(snapshot.MainAppName == "Code.exe", "主力应用名应取进程文件名");
        Check(snapshot.FocusSeconds > 10, "Deep 应累计连续专注秒数");
        Check(snapshot.Stickiness > 0.9, "单应用粘性应接近 1");

        FocusMonitor unknown = new FocusMonitor();
        FocusSnapshot unknownSnapshot = null;
        for (int i = 0; i <= 30; i++) unknownSnapshot = S(unknown, i, 20, "", 0, false);
        Check(unknownSnapshot.State == FlowState.Fragmented, "未识别应用不能成为 Deep 主力");
        Check(unknownSnapshot.MainAppName == "未识别应用", "未识别应用名称回退");
    }

    private static void NormalAndFragmented()
    {
        FocusMonitor normal = new FocusMonitor();
        FocusSnapshot snapshot = null;
        for (int i = 0; i <= 30; i++)
        {
            string app = i < 7 ? @"C:\Apps\App" + i + ".exe" : @"C:\Apps\App7.exe";
            snapshot = S(normal, i, 20, app, 0, false);
        }
        Check(snapshot.State == FlowState.Normal, "7 次切换(6<切换<=12)且有有效输入应判 Normal");
        Equal(snapshot.Switches5m, 7, "Normal 切换数");
        Check(snapshot.Stickiness >= 0.45, "Normal 粘性下限");
        Check(snapshot.FocusSeconds > 0, "Normal 也应累计专注秒数");

        FocusMonitor fragmented = new FocusMonitor();
        for (int i = 0; i <= 30; i++)
        {
            string app = i < 12 ? @"C:\Apps\F" + i + ".exe" : @"C:\Apps\F12.exe";
            snapshot = S(fragmented, i, 20, app, 0, false);
        }
        Check(snapshot.State == FlowState.Fragmented, "12 次切换应判 Fragmented");

        FocusMonitor lowInput = new FocusMonitor();
        for (int i = 0; i <= 30; i++) snapshot = S(lowInput, i, 10, @"C:\Apps\Code.exe", 0, false);
        Check(snapshot.State == FlowState.Fragmented, "有效输入不足 15 应判 Fragmented");
    }

    private static void HysteresisAndFocusReset()
    {
        FocusMonitor monitor = new FocusMonitor();
        WarmDeep(monitor);
        FocusSnapshot snapshot = null;
        for (int i = 31; i <= 35; i++) snapshot = S(monitor, i, 0, @"C:\Apps\Code.exe", 0, false);
        Check(snapshot.State == FlowState.Deep, "状态切换在 12 秒滞回内不得闪烁");
        for (int i = 36; i <= 45; i++) snapshot = S(monitor, i, 0, @"C:\Apps\Code.exe", 0, false);
        Check(snapshot.State == FlowState.Fragmented, "持续低输入超过滞回应切到 Fragmented");
        Check(snapshot.FocusSeconds == 0, "离开专注态后连续专注秒数清零");
    }

    private static void IdleAndWindowPrune()
    {
        FocusMonitor idle = new FocusMonitor();
        WarmDeep(idle);
        FocusSnapshot snapshot = S(idle, 31, 20, @"C:\Apps\Code.exe", 25000, false);
        Check(snapshot.State == FlowState.Idle, "系统空闲 >=20s 应立即判 Idle(不再叠加滞回)");
        for (int i = 32; i <= 45; i++) snapshot = S(idle, i, 20, @"C:\Apps\Code.exe", 25000, false);
        Check(snapshot.State == FlowState.Idle, "系统空闲 >=20s 应判 Idle");
        Check(snapshot.FocusSeconds == 0, "Idle 必须清零连续专注");

        FocusMonitor prune = new FocusMonitor();
        S(prune, 0, 20, @"C:\Apps\A.exe", 0, false);
        S(prune, 1, 20, @"C:\Apps\B.exe", 0, false);
        snapshot = S(prune, 301, 20, @"C:\Apps\B.exe", 0, false);
        Equal(snapshot.Switches5m, 0, "5 分钟前的切换必须滚出窗口");
        Equal(snapshot.DistinctApps, 1, "窗口内只剩一个应用");
        Check(snapshot.MainAppName == "B.exe", "窗口滚动后主力应用应更新");
    }
}
