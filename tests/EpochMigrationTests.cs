using System;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 3995 年时间基准缺陷的回归:
///   · UnixTime 基准正确;
///   · 异常库启动时自动备份并迁回;
///   · 2026 正常汇总保留、3995 脏汇总清除;
///   · 迁移严格幂等;
///   · 迁移后 FlowBoard.Get 能读出当天事件。
/// 全部写入 %TEMP% 沙盒。
/// </summary>
internal static class EpochMigrationTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    [STAThread]
    private static void Main()
    {
        string bootRoot = Path.Combine(Path.GetTempPath(), "KmsEpochBoot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bootRoot);
        NativeBoot.RootOverride = bootRoot;
        try
        {
            Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("EpochMigrationTests", Run);
        }
        finally
        {
            try { ShadowPipeline.Stop(); } catch { }
            try { NativeBoot.ReleaseForTest(); } catch { }
            try { Directory.Delete(bootRoot, true); } catch { }
        }
    }

    private static void Run()
    {
        Check(UnixTime.ToUnixMs(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)) == 0,
            "Unix epoch maps to 0");
        Check(UnixTime.ToUnixMs(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc)) == 1795132800000L,
            "2026-11-20 Unix ms");
        long now = UnixTime.NowMs();
        Check(now > 1700000000000L && now < KmsSchema.EpochFixThresholdMs,
            "UnixTime.NowMs stays in the sane Unix range");

        string sandbox = Path.Combine(Path.GetTempPath(), "KmsEpoch-" + Guid.NewGuid().ToString("N"));
        string dbRoot = Path.Combine(sandbox, "db");
        string dataRoot = Path.Combine(sandbox, "data");
        Directory.CreateDirectory(dbRoot);
        Directory.CreateDirectory(dataRoot);
        try
        {
            Store.DataDirectory = dataRoot;
            NativeBoot.RootOverride = dbRoot;
            NativeBoot.ReleaseForTest();
            string dbPath = Path.Combine(dbRoot, KmsSchema.DbFileName);
            string backupPath = dbPath + ".bak-3995-fix";
            DateTime today = DateTime.Today;
            string expectedDay = today.ToString("yyyy-MM-dd");
            long correctStart = UnixTime.ToUnixMs(today.AddHours(12).ToUniversalTime());
            long correctEnd = correctStart + 60000L;
            long abnormalStart = correctStart + UnixTime.YearOneToUnixMs;
            long abnormalEnd = correctEnd + UnixTime.YearOneToUnixMs;

            using (KmsDb db = KmsDb.OpenWriter(dbPath))
            {
                KmsSchema.Ensure(db);
            }

            using (KmsDb db = KmsDb.OpenWriter(dbPath))
            {
                db.Execute("DELETE FROM meta WHERE k=?;", new object[] { KmsSchema.EpochFixMarker });
                db.Execute("INSERT INTO events_window_session " +
                    "(start_ts_ms, end_ts_ms, day, process_path, process_name, window_title, key_count, is_open) " +
                    "VALUES (?,?,?,?,?,?,?,0);",
                    new object[] { abnormalStart, abnormalEnd, "3995-11-20", @"C:\Apps\Code.exe", "Code.exe", "main.cs", 12 });
                db.Execute("INSERT INTO events_input_burst (start_ts_ms, end_ts_ms, day, key_count) " +
                    "VALUES (?,?,?,?);",
                    new object[] { abnormalStart, abnormalEnd, "3995-11-20", 5 });
                db.Execute("UPDATE meta SET v=? WHERE k='last_event_ts_ms';", new object[] { abnormalEnd.ToString() });
                db.Execute("UPDATE meta SET v=? WHERE k='db_created_ms';", new object[] { abnormalEnd.ToString() });
                db.Execute("INSERT OR REPLACE INTO daily_summary (day, keys, clicks) VALUES (?,?,?);",
                    new object[] { "2026-11-20", 123, 4 });
                db.Execute("INSERT OR REPLACE INTO daily_summary (day, keys, clicks) VALUES (?,?,?);",
                    new object[] { "3995-11-20", 999, 9 });
                db.Execute("INSERT OR REPLACE INTO daily_hour_agg (day, hour, keys, clicks, active_seconds) " +
                    "VALUES (?,?,?,?,?);", new object[] { "3995-11-20", 9, 77, 7, 60.0 });
            }

            Check(!File.Exists(backupPath), "no backup before migration");
            DateTime backupStamp = DateTime.MinValue;
            long backupLength = 0;

            using (KmsDb db = KmsDb.OpenWriter(dbPath))
            {
                KmsSchema.Ensure(db);   // 触发备份 + 单事务迁回

                Check(File.Exists(backupPath), "migration backup created");
                Check(new FileInfo(backupPath).Length > 0, "migration backup is not empty");
                backupStamp = File.GetLastWriteTimeUtc(backupPath);
                backupLength = new FileInfo(backupPath).Length;
                Check(db.ScalarText("SELECT v FROM meta WHERE k=?;", new object[] { KmsSchema.EpochFixMarker }) == "1",
                    "epoch_fix_v1 marker written");

                Check(db.ScalarLong("SELECT COUNT(*) FROM events_window_session WHERE day=? AND start_ts_ms=? AND end_ts_ms=?;",
                    new object[] { expectedDay, correctStart, correctEnd }) == 1,
                    "session day and timestamps repaired");
                Check(db.ScalarLong("SELECT COUNT(*) FROM events_input_burst WHERE day=? AND start_ts_ms=? AND end_ts_ms=?;",
                    new object[] { expectedDay, correctStart, correctEnd }) == 1,
                    "burst day and timestamps repaired");
                Check(db.ScalarLong("SELECT COUNT(*) FROM events_window_session WHERE day >= '3900-01-01' " +
                    "OR start_ts_ms >= ? LIMIT 1;", new object[] { KmsSchema.EpochFixThresholdMs }) == 0,
                    "no abnormal session rows remain");
                Check(db.ScalarLong("SELECT COUNT(*) FROM events_input_burst WHERE day >= '3900-01-01' " +
                    "OR start_ts_ms >= ? LIMIT 1;", new object[] { KmsSchema.EpochFixThresholdMs }) == 0,
                    "no abnormal burst rows remain");
                Check(db.ScalarText("SELECT v FROM meta WHERE k='last_event_ts_ms';", null) == correctEnd.ToString(),
                    "meta last_event_ts_ms repaired");
                Check(db.ScalarText("SELECT v FROM meta WHERE k='db_created_ms';", null) == correctEnd.ToString(),
                    "meta db_created_ms repaired");
                Check(db.ScalarLong("SELECT COUNT(*) FROM daily_summary WHERE day='2026-11-20' AND keys=123;", null) == 1,
                    "correct 2026 daily summary preserved");
                Check(db.ScalarLong("SELECT COUNT(*) FROM daily_summary WHERE day >= '3900-01-01';", null) == 0,
                    "dirty 3995 daily summary removed");
                Check(db.ScalarLong("SELECT COUNT(*) FROM daily_hour_agg WHERE day >= '3900-01-01';", null) == 0,
                    "dirty 3995 hourly rows removed");
            }

            using (KmsDb db = KmsDb.OpenWriter(dbPath))
            {
                KmsSchema.Ensure(db);   // 第二次仍应为幂等
                Check(db.ScalarLong("SELECT COUNT(*) FROM events_window_session WHERE start_ts_ms=?;",
                    new object[] { correctStart }) == 1, "second Ensure does not shift twice");
                Check(db.ScalarText("SELECT v FROM meta WHERE k=?;", new object[] { KmsSchema.EpochFixMarker }) == "1",
                    "marker remains after second Ensure");
            }
            Check(new FileInfo(backupPath).Length == backupLength
                && File.GetLastWriteTimeUtc(backupPath) == backupStamp,
                "existing backup is not overwritten");

            ShadowPipeline.ResetForTest();
            Check(ShadowPipeline.Start(dbRoot), "shadow pipeline starts on repaired database: " + ShadowPipeline.Diagnosis);
            FlowBoard.Invalidate();
            FlowBoardData data = FlowBoard.Get(today);
            Check(data != null && data.HasData, "FlowBoard reads repaired today sessions");
            Check(data != null && data.Sessions != null && data.Sessions.Count == 1, "FlowBoard sees one repaired session");
            Check(data != null && data.Report != null, "FlowBoard builds a flow report");
            ShadowPipeline.Stop();

            Check(FlowBoard.NoDataHint != FlowBoard.CleanHint, "no-data hint is distinct from clean-edition hint");
            Check(FlowBoard.UnavailableHint.Length > 0, "unavailable hint exists");
            Check(FlowBoard.UnavailableHint.IndexOf("清爽版", StringComparison.Ordinal) < 0,
                "standard unavailable hint does not claim clean edition");

            Console.WriteLine("PASS: " + _checks + " epoch migration checks; backup, repair, idempotence and FlowBoard read");
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch { }
        }
    }
}
