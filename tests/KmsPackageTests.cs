// tests/KmsPackageTests.cs · v3.0 M3/S3 —— .kms 往返、篡改拒绝、重复导入恒等
using System;
using System.Collections.Generic;
using KeyMouseStats;

internal static class KmsPackageTests
{
    private static int _checks;
    private static void Check(bool v, string n) { if (!v) throw new Exception(n); _checks++; }
    private static void Equal(long a, long e, string n) { Check(a == e, n + "(期望 " + e + ",实际 " + a + ")"); }
    private static void Same(string a, string e, string n) { Check(string.Equals(a, e, StringComparison.Ordinal), n + "(期望「" + e + "」实际「" + a + "」)"); }

    private static void Main() { Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("KmsPackageTests", Run); }

    private static KmsPackageData Sample()
    {
        KmsPackageData d = new KmsPackageData();
        d.DeviceId = "PC-A";
        d.DayRange = "2026-11-15..2026-11-16";
        d.Days.Add(new KmsDayRecord { Day = "2026-11-15", Keys = 7680, Clicks = 1210, Wheel = 402, ActiveSeconds = 3600 });
        d.Days.Add(new KmsDayRecord { Day = "2026-11-16", Keys = 5000, Clicks = 800, Wheel = 120, ActiveSeconds = 2400 });
        d.Sessions.Add(new KmsSessionRecord { Day = "2026-11-15", StartMs = 1000, EndMs = 2000, KeyCount = 40, CategoryId = 0, ProcessPath = "code.exe", ProcessName = "code", WindowTitle = "秘密项目\t主函数" });
        d.Sessions.Add(new KmsSessionRecord { Day = "2026-11-15", StartMs = 3000, EndMs = 4000, KeyCount = 10, CategoryId = 8, ProcessPath = "wechat.exe", ProcessName = "wechat", WindowTitle = "项目群" });
        return d;
    }

    private static void Run()
    {
        Format();
        RoundTrip();
        TamperRejected();
        Idempotent();
        Encoding();        SqliteLedger();
        Console.WriteLine("KmsPackageTests: " + _checks + " 项断言通过");
    }

    private static void Format()
    {
        string text = KmsPackage.Export(Sample());
        Check(text.StartsWith("KMS1\n", StringComparison.Ordinal), "以 KMS1 魔数开头");
        Check(text.IndexOf("version=1", StringComparison.Ordinal) > 0, "含 version=1");
        Check(text.IndexOf("device_id=PC-A", StringComparison.Ordinal) > 0, "含 device_id");
        Check(text.IndexOf("day_range=2026-11-15..2026-11-16", StringComparison.Ordinal) > 0, "含 day_range");
        Check(text.IndexOf("checksum=", StringComparison.Ordinal) > 0, "含 checksum");
        Check(text.IndexOf("---payload---", StringComparison.Ordinal) > 0, "含 payload 分隔符");
        Check(KmsPackage.Hash("abc").Length == 64, "SHA256 为 64 位十六进制");
    }

    private static void RoundTrip()
    {
        KmsPackageData source = Sample();
        KmsPackageData back = KmsPackage.Import(KmsPackage.Export(source));
        Equal(back.Days.Count, 2, "日汇总条数往返一致");
        Equal(back.Sessions.Count, 2, "会话条数往返一致");
        Equal(back.Days[0].Keys, 7680, "击键往返一致");
        Equal(back.Days[1].ActiveSeconds, 2400, "活跃秒往返一致");
        Same(back.DeviceId, "PC-A", "设备号往返一致");
        Same(back.DayRange, "2026-11-15..2026-11-16", "日期范围往返一致");
        Same(back.Sessions[0].WindowTitle, "秘密项目\t主函数", "含制表符的标题往返一致");
        Equal(back.Sessions[0].CategoryId, 0, "类别往返一致");
        Same(back.Days[0].RecordKey, source.Days[0].RecordKey, "记录键稳定");
    }

    private static void TamperRejected()
    {
        string text = KmsPackage.Export(Sample());
        string tampered = text.Replace("7680", "9999");
        bool threw = false;
        try { KmsPackage.Import(tampered); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "篡改载荷必须被校验和拒绝");

        threw = false;
        try { KmsPackage.Import("NOTKMS\nversion=1\n---payload---\n"); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "魔数不符必须拒绝");

        threw = false;
        try { KmsPackage.Import(""); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "空包必须拒绝");

        threw = false;
        try { KmsPackage.Import("KMS1\nversion=1\n---payload---\n"); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "缺校验和必须拒绝");

        threw = false;
        try { KmsPackage.Import("KMS1\nversion=2\n---payload---\n"); } catch (InvalidOperationException) { threw = true; }
        Check(threw, "不支持的版本必须拒绝");
    }

    private static void Idempotent()
    {
        KmsPackageData package = KmsPackage.Import(KmsPackage.Export(Sample()));
        Dictionary<string, string> ledger = new Dictionary<string, string>();
        List<KmsDayRecord> days = new List<KmsDayRecord>();
        List<KmsSessionRecord> sessions = new List<KmsSessionRecord>();

        KmsMergeResult first = KmsPackage.Merge(package, ledger, days, sessions);
        Equal(first.DaysApplied, 2, "首次导入应用 2 天");
        Equal(first.SessionsApplied, 2, "首次导入应用 2 段会话");
        Check(first.Changed, "首次导入应有变更");

        long keys = days[0].Keys, active = days[0].ActiveSeconds;
        int dayCount = days.Count, sessionCount = sessions.Count;

        KmsMergeResult second = KmsPackage.Merge(package, ledger, days, sessions);
        Equal(second.DaysApplied, 0, "重复导入不再应用任何日");
        Equal(second.SessionsApplied, 0, "重复导入不再应用任何会话");
        Check(!second.Changed, "重复导入必须无变更");
        Equal(days.Count, dayCount, "重复导入后日数不变");
        Equal(sessions.Count, sessionCount, "重复导入后会话数不变");
        Equal(days[0].Keys, keys, "重复导入后击键不变(不翻倍)");
        Equal(days[0].ActiveSeconds, active, "重复导入后活跃秒不变");
        Equal(ledger.Count, 4, "账本记录 4 条");

        Dictionary<string, string> fresh = new Dictionary<string, string>();
        KmsMergeResult third = KmsPackage.Merge(package, fresh, null, null);
        Equal(third.DaysApplied, 2, "新账本重新应用(校验账本确实生效)");
    }

    private static void Encoding()
    {
        Same(KmsPackage.Escape("a\tb\nc\\d"), "a\\tb\\nc\\\\d", "转义覆盖制表符/换行/反斜杠");
        Same(KmsPackage.Unescape("a\\tb\\nc\\\\d"), "a\tb\nc\\d", "反转义还原");
        Same(KmsPackage.Unescape(KmsPackage.Escape("普通文本")), "普通文本", "无特殊字符原样往返");
        KmsPackageData d = new KmsPackageData();
        d.Days.Add(new KmsDayRecord { Day = "2026-01-01", Keys = 1 });
        Same(KmsPackage.Fingerprint("code.exe"), KmsPackage.Fingerprint("code.exe"), "指纹确定");
        Check(KmsPackage.Fingerprint("code.exe") != KmsPackage.Fingerprint("wechat.exe"), "不同路径指纹不同");
    }

    /// <summary>持久化账本:跨连接(模拟重启)重复导入,库内计数与内容必须恒等。</summary>
    private static void SqliteLedger()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KmsLedger-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        string dbPath = System.IO.Path.Combine(dir, "kmstats.db");
        try
        {
            KmsPackageData pkg = KmsPackage.Import(KmsPackage.Export(Sample()));
            // 原生库必须释放到沙盒,绝不触碰 %LOCALAPPDATA%\KeyMouseStats。
            NativeBoot.RootOverride = System.IO.Path.Combine(dir, "native");
            // 该用例需要原生 SQLite;测试二进制若未链入原生资源,则只验证「优雅降级」而不假绿。
            if (!NativeBoot.TryEnsure())
            {
                bool threw = false;
                try { KmsDb.MergeKmsPackage(dbPath, "pkg-A", pkg); }
                catch (KmsDbException) { threw = true; }
                Check(threw, "无原生引擎时必须抛 KmsDbException 而不是崩溃");
                return;
            }
            KmsMergeResult first = KmsDb.MergeKmsPackage(dbPath, "pkg-A", pkg);
            Equal(first.DaysApplied, 2, "首次导入应用 2 天");
            Equal(first.SessionsApplied, 2, "首次导入应用 2 段会话");

            long keysBefore, sessionsBefore, ledgerBefore;
            using (KmsDb db = KmsDb.OpenReader(dbPath))
            {
                keysBefore = db.ScalarLong("SELECT COUNT(*) FROM daily_summary;", null);
                sessionsBefore = db.ScalarLong("SELECT COUNT(*) FROM events_window_session;", null);
                ledgerBefore = db.ScalarLong("SELECT COUNT(*) FROM kms_import_log;", null);
            }
            Equal(keysBefore, 2, "库内日汇总 2 行");
            Equal(sessionsBefore, 2, "库内会话 2 行");
            Equal(ledgerBefore, 4, "账本 4 条");

            // 新连接(相当于软件重启后再导入同一个包)
            KmsMergeResult second = KmsDb.MergeKmsPackage(dbPath, "pkg-A", pkg);
            Equal(second.DaysApplied, 0, "重启后重复导入不应用任何日");
            Equal(second.SessionsApplied, 0, "重启后重复导入不应用任何会话");
            Equal(second.DaysSkipped, 2, "跳过 2 天");
            Equal(second.SessionsSkipped, 2, "跳过 2 段");

            using (KmsDb db = KmsDb.OpenReader(dbPath))
            {
                Equal(db.ScalarLong("SELECT COUNT(*) FROM daily_summary;", null), keysBefore, "日汇总行数恒等");
                Equal(db.ScalarLong("SELECT COUNT(*) FROM events_window_session;", null), sessionsBefore, "会话行数恒等");
                Equal(db.ScalarLong("SELECT COUNT(*) FROM kms_import_log;", null), ledgerBefore, "账本行数恒等");
                Equal(db.ScalarLong("SELECT keys FROM daily_summary WHERE day='2026-11-15';", null), 7680, "击键未被累加翻倍");
            }
        }
        finally
        {
            NativeBoot.RootOverride = null;
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }
}
