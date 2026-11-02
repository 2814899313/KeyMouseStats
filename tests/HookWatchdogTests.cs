using System;
using KeyMouseStats;

/// <summary>
/// 钩子看门狗的判定表。这条逻辑决定「什么时候重装钩子」——判早了会打断正常输入、
/// 判晚了就是用户遇到的「抽风:鼠标点击和滚轮突然不再统计,必须重启」。所以逐格钉死。
/// </summary>
internal static class HookWatchdogTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("HookWatchdogTests", Run);
    }

    private static void Run()
    {
        // 锁屏 / 休眠 / 断连:钩子本来就收不到输入,不能重装(会打断仍在按住的键)。
        Check(!HookWatchdog.ShouldRepair(false, true, 0, 999999), "a busy session is never repaired");
        Check(!HookWatchdog.ShouldRepair(true, true, 0, 999999), "a busy session is not repaired even when alive");

        // 句柄丢了:无条件重装。
        Check(HookWatchdog.ShouldRepair(false, false, 0, 0), "a lost hook is repaired immediately");
        Check(HookWatchdog.ShouldRepair(false, false, uint.MaxValue, 0), "a lost hook is repaired even without idle info");

        // 句柄在、刚收到过事件:不动。
        Check(!HookWatchdog.ShouldRepair(true, false, 500, 1200), "an alive hook with recent events is left alone");

        // 用户离开(系统空闲超过阈值):不能当成钩子坏了。
        Check(!HookWatchdog.ShouldRepair(true, false, HookWatchdog.SystemIdleLimitMs, 999999),
            "idle time means the user is away, not that hooks are dead");
        Check(!HookWatchdog.ShouldRepair(true, false, 60000, 999999), "a long idle period is not a stall");

        // 系统一直在收输入,我们却迟迟没记到:判定停摆,重装。
        Check(HookWatchdog.ShouldRepair(true, false, 800, HookWatchdog.StallWindowMs + 1000),
            "system input without counted events is a stall");
        Check(HookWatchdog.ShouldRepair(true, false, HookWatchdog.SystemIdleLimitMs - 1, HookWatchdog.StallWindowMs + 1),
            "boundary: just under the idle limit still counts as a stall");

        // 读不到空闲时间:只信句柄,不做停摆判断。
        Check(!HookWatchdog.ShouldRepair(true, false, uint.MaxValue, 999999), "unknown idle time never triggers a repair");

        // 阈值本身也是契约:改小了会误杀,改大了用户又要等更久。
        Check(HookWatchdog.CheckIntervalMs == 5000, "the watchdog checks every five seconds");
        Check(HookWatchdog.StallWindowMs == 30000, "a thirty second stall is the trigger");
        Check(HookWatchdog.SystemIdleLimitMs == 10000, "ten seconds of system idle means the user is away");

        Console.WriteLine("HookWatchdogTests: " + checks + " checks passed");
    }
}
