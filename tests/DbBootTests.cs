// ============================================================================
//  tests/DbBootTests.cs  ·  v3.0 Milestone 1 / Step 1
//
//  验证三件事(对应任务书 Step 1 的验收项):
//   1. 版本探测:内嵌引擎能被释放并报出真实的 SQLite 版本;
//   2. 原生库释放的幂等性:重复启动不重写文件、不重复解压;
//   3. CI 硬闸门:KeyMouseStats.Clean.exe ≤ 2 MiB 且内嵌托管资源数为 0。
//
//  另外验证降级路径:释放目录不可用时必须**优雅失败**并回落到文本后端,绝不崩溃。
//
//  安全约束:全部写入都在 %TEMP% 沙盒内;NativeBoot.RootOverride 在第一步就重定向,
//  绝不会触碰 %LOCALAPPDATA%\KeyMouseStats。
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using System.Text;
using KeyMouseStats;

internal static class DbBootTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Main()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "KmsBootTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        int code = 0;
        try
        {
            RunSuccessPath(sandbox);
            RunCleanEditionGate();
            RunFallbackPath(sandbox);
            Console.WriteLine("DbBootTests: " + _checks + " 项断言通过");
        }
        catch (Exception error)
        {
            // CLR 在某些区域设置下无法把中文异常打印到控制台(会输出「Exception.ToString() 失败」),
            // 因此失败详情一律落 UTF-8 文件,控制台只留一行 ASCII 线索。
            string path = Path.Combine(Path.GetTempPath(), "db-boot-failure.log");
            try { File.WriteAllText(path, error.ToString(), Encoding.UTF8); }
            catch { }
            Console.WriteLine("DbBootTests FAILED after " + _checks + " checks -> " + path);
            code = 1;
        }
        finally
        {
            try { NativeBoot.ReleaseForTest(); } catch { }
            try { Directory.Delete(sandbox, true); } catch { }
        }
        if (code != 0) Environment.Exit(code);
    }

    /// <summary>正常路径:释放 → 装载 → 版本探测 → 幂等复用。</summary>
    private static void RunSuccessPath(string sandbox)
    {
        NativeBoot.RootOverride = sandbox;
        NativeBoot.ReleaseForTest();

        Check(NativeBoot.TryEnsure(), "原生引擎应能装载:" + NativeBoot.Diagnosis);
        Check(NativeBoot.Available, "Available 应为 true");

        string directory = NativeBoot.NativeDirectory();
        Check(directory.StartsWith(sandbox, StringComparison.OrdinalIgnoreCase),
            "释放目录应在沙盒内,实际=" + directory);
        Check(directory.IndexOf(NativeBoot.NativeVersion, StringComparison.Ordinal) >= 0,
            "释放目录应包含引擎版本段,实际=" + directory);
        Check(directory.IndexOf(NativeBoot.ArchName(), StringComparison.OrdinalIgnoreCase) >= 0,
            "释放目录应包含架构段,实际=" + directory);

        string file = NativeBoot.NativeFilePath;
        Check(File.Exists(file), "原生库应已释放到磁盘:" + file);
        Check(string.Equals(Path.GetFileName(file), "e_sqlite3.dll", StringComparison.OrdinalIgnoreCase),
            "释放出的文件名必须是 e_sqlite3.dll,DllImport 才能命中已装载模块");
        long size = new FileInfo(file).Length;
        Check(size > 100000, "原生库体积异常,实际=" + size);

        // 版本探测:必须能问出真实版本号。
        string version = Sqlite.LibVersion();
        Check(!string.IsNullOrEmpty(version), "应能读到 SQLite 版本号");
        Check(version.StartsWith("3.", StringComparison.Ordinal), "SQLite 主版本应为 3,实际=" + version);
        Console.WriteLine("  引擎版本 = " + version + " · 架构 = " + NativeBoot.ArchName() + " · 释放字节 = " + size);

        // 幂等性 A:同一进程内重复调用,不重装、不重写。
        IntPtr firstHandle = NativeBoot.Handle;
        Check(NativeBoot.TryEnsure(), "重复 TryEnsure 应继续为 true");
        Check(NativeBoot.Handle == firstHandle, "重复调用不应换句柄");

        // 幂等性 B:模拟进程重启(卸载+复位)后再次装载,应「复用」而不是「重写」。
        DateTime stampBefore = File.GetLastWriteTimeUtc(file);
        NativeBoot.ReleaseForTest();
        Check(NativeBoot.TryEnsure(), "复位后应能再次装载");
        Check(string.Equals(NativeBoot.LastAction, "reused", StringComparison.Ordinal),
            "第二次释放应复用已存在文件,实际动作为 " + NativeBoot.LastAction);
        Check(File.GetLastWriteTimeUtc(file) == stampBefore, "幂等复用不得改写文件时间戳");

        // 与 StatsStoreFactory 接通:应选到 sqlite 后端。
        StatsStoreFactory.ResetForTest();
        IStatsStore store = StatsStoreFactory.Open(Path.Combine(sandbox, "store"));
        Check(string.Equals(store.BackendId, "sqlite", StringComparison.Ordinal),
            "引擎可用时应选 sqlite 后端,实际=" + store.BackendId);
        string detail;
        Check(store.HealthCheck(out detail), "sqlite 后端自检应通过:" + detail);
        Check(store.SchemaRev == KmsSchema.Rev, "SchemaRev 应等于当前版本");
        store.Shutdown();
        StatsStoreFactory.ResetForTest();
    }

    /// <summary>CI 硬闸门:清爽版体积与资源数。</summary>
    private static void RunCleanEditionGate()
    {
        string clean = Path.Combine(Environment.CurrentDirectory, "KeyMouseStats.Clean.exe");
        if (!File.Exists(clean))
        {
            // CI 先跑 test.bat 再跑 build-clean.bat,首次运行时产物还不存在。
            Console.WriteLine("  [SKIP] 未找到 KeyMouseStats.Clean.exe,跳过清爽版闸门(先 build 后再跑即可验证)");
            return;
        }

        long bytes = new FileInfo(clean).Length;
        Check(bytes <= 2L * 1024 * 1024, "清爽版必须 ≤ 2 MiB,实际=" + bytes + " B");

        // 用 Assembly.Load(byte[]) 而不是 LoadFile:后者会因「远程位置/CAS」策略对
        // 某些本地路径直接抛 NotSupportedException,而这里只需要读资源清单。
        Assembly assembly = Assembly.Load(File.ReadAllBytes(clean));
        string[] resources = assembly.GetManifestResourceNames();
        Check(resources.Length == 0,
            "清爽版内嵌托管资源数必须为 0,实际=" + resources.Length
            + (resources.Length > 0 ? "(" + string.Join(",", resources) + ")" : ""));

        Console.WriteLine("  清爽版 = " + bytes + " B(" + (bytes / 1024.0).ToString("0.0") + " KiB)· 内嵌资源 0 项");
    }

    /// <summary>降级路径:释放目录不可用时,必须返回 false 并让工厂回落到文本后端。</summary>
    private static void RunFallbackPath(string sandbox)
    {
        // 造一个「父级是文件」的路径:CreateDirectory 必然失败。
        string blocker = Path.Combine(sandbox, "blocker");
        File.WriteAllText(blocker, "not a directory");
        string unusable = Path.Combine(blocker, "child");

        NativeBoot.RootOverride = unusable;
        NativeBoot.ReleaseForTest();

        bool loaded = NativeBoot.TryEnsure();
        Check(!loaded, "释放目录不可用时必须优雅失败,不能抛异常也不能假装成功");
        Check(!string.IsNullOrEmpty(NativeBoot.Diagnosis), "失败时必须给出诊断说明");
        Check(!NativeBoot.Available, "失败后 Available 应为 false");

        StatsStoreFactory.ResetForTest();
        IStatsStore fallback = StatsStoreFactory.Open(Path.Combine(sandbox, "store-fallback"));
        Check(string.Equals(fallback.BackendId, "text", StringComparison.Ordinal),
            "降级后必须选文本后端,实际=" + fallback.BackendId);
        Check(fallback.Available, "文本后端自身必须可用(它是降级底线)");
        Check(fallback.Diagnosis.Length > 0, "降级原因应可读");
        string detail;
        Check(fallback.HealthCheck(out detail), "文本后端自检应通过");
        fallback.Shutdown();
        StatsStoreFactory.ResetForTest();
    }
}
