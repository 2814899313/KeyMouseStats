using System;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 开机自启的可测部分。真正注册计划任务要走 COM 与系统状态,没法在无人值守的
/// 测试进程里断言;但这里最容易被改坏的是「两个路径是不是同一个文件」这条判断 ——
/// 它决定每次启动要不要重注册任务,判错就会出现「自启指向旧 exe」。
/// </summary>
internal static class AutoStartTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("AutoStartTests", Run);
    }

    private static void Run()
    {
        // ---- 任务名与旧的注册表值名都是外部契约,改了老用户的自启就断 ----
        Check(AutoStart.TaskName == "KeyMouseStats", "the task name is a stable contract");
        Check(AutoStart.LegacyRunValueName == "KeyMouseStatsWidget", "the legacy run value name is stable");

        // ---- 路径归一化:引号、空白、相对路径 ----
        string exe = Path.Combine(Path.GetTempPath(), "KeyMouseStats.exe");
        Check(AutoStart.NormalizePath("\"" + exe + "\"").Length > 0, "a quoted path normalizes to something");
        Check(AutoStart.NormalizePath(null) == "", "null normalizes to empty");
        Check(AutoStart.NormalizePath("   ") == "", "blank normalizes to empty");
        Check(AutoStart.NormalizePath("  \"" + exe + "\"  ") == AutoStart.NormalizePath(exe),
            "surrounding blanks and quotes are stripped");

        // ---- 同路径判定:大小写不敏感、引号无关 ----
        Check(AutoStart.SamePath(exe, exe), "the same path matches itself");
        Check(AutoStart.SamePath("\"" + exe + "\"", exe), "quotes do not break the comparison");
        Check(AutoStart.SamePath(exe.ToUpperInvariant(), exe.ToLowerInvariant()),
            "windows paths compare case-insensitively");

        // ---- 不同文件必须判为不同,否则「复制改名后重注册」永远不会发生 ----
        string other = Path.Combine(Path.GetTempPath(), "KeyMouseStats-1.9.0-r7.exe");
        Check(!AutoStart.SamePath(exe, other), "a renamed copy is a different file");
        Check(!AutoStart.SamePath(exe, ""), "an empty registered path never matches");
        Check(!AutoStart.SamePath("", ""), "two empty paths never match");
        Check(!AutoStart.SamePath(exe, null), "a null path never matches");

        // ---- 查询接口在没有任何任务时也必须安全返回,不能抛 ----
        bool enabled = AutoStart.IsEnabled();
        Check(enabled || !enabled, "querying the task state never throws");
        string registered = AutoStart.RegisteredPath();
        Check(registered != null, "querying the registered path never returns null");

        Console.WriteLine("AutoStartTests: " + checks + " checks passed");
    }
}
