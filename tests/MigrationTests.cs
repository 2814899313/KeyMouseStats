// ============================================================================
//  tests/MigrationTests.cs  ·  v3.0 Milestone 1 / Step 4
//
//  验证 v2 文本存档 → kmstats.db 的迁移:
//   1. 重复运行 3 次严格幂等(每次都把 migrated_from_format 清零再跑);
//   2. backup_v2 的 manifest.txt 逐行 sha256 / 字节数可复核,源文件哈希不变;
//   3. 坏档([day=] 却解析不出日记录)与空档都安全降级,不写库不坏文件;
//   4. format 10~13 与 <yyyy>\<yyyy-MM>.txt 月档合并导入;
//   5. 守恒断言失败 → 事务回滚,现场保留;
//   6. 历史单通道网格只进 channel=0,绝不伪造归入左键。
//
//  全部读写都在 %TEMP% 沙盒内;绝不触碰 %APPDATA%\键鼠统计 或 %LOCALAPPDATA%。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KeyMouseStats;

internal static class MigrationTests
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

    private static string Text(params string[] lines)
    {
        return string.Join("\n", lines) + "\n";
    }

    private static void Main()
    {
        string bootRoot = Path.Combine(Path.GetTempPath(), "KmsMigrateBoot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bootRoot);
        NativeBoot.RootOverride = bootRoot;
        try
        {
            Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("MigrationTests", Run);
        }
        finally
        {
            try { NativeBoot.ReleaseForTest(); } catch { }
            try { Directory.Delete(bootRoot, true); } catch { }
        }
    }

    private static void Run()
    {
        IdempotentThreeRuns();
        BackupManifestVerifiedAndSourceUntouched();
        BadArchiveAborts();
        EmptyArchiveSkips();
        FormatRangeAndMonthMerge();
        ConservationFailureRollsBack();
        SnapshotBridgeFillsMissingDaysOnly();
        Console.WriteLine("MigrationTests: " + _checks + " 项断言通过");
    }

    // ---------------------------------------------------------------- 夹具

    private static string NewSandbox(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(), "KmsMig-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SourceDir(string sandbox) { return Path.Combine(sandbox, "src"); }
    private static string DbDir(string sandbox) { return Path.Combine(sandbox, "db"); }
    private static string DbPath(string sandbox) { return Path.Combine(DbDir(sandbox), KmsSchema.DbFileName); }

    /// <summary>一份 format=13 的完整存档:两天日记录 + 单通道网格 + 逐应用 + 月档 + 规则。</summary>
    private static string FullStatsText(string format)
    {
        string editorPath = @"C:\Apps\Code.exe";
        string editorTitle = "main.cs - Code";
        return Text(
            "# 键鼠统计数据文件 v13 / 正式版 2.4.0",
            "format=" + format,
            "storage_layout=monthly-v1",
            "idle_threshold=60",
            "backup_keep=7",
            "keep_days=0",
            "art_theme=1",
            "keyboard_layout=0",
            "mouse_dpi=800",
            "all_time_peak_apm=120",
            "app_rule=" + AppActivity.Encode("app:c:\\apps\\code.exe") + "|0",
            "date=2023-11-14",
            "total_keys=130",
            "total_clicks=25",
            "total_left=18",
            "total_right=5",
            "total_middle=2",
            "archive_v1=2022-03|3|500|80|10|0|999|2.5|9|" + AppActivity.Encode(@"C:\Apps\Code.exe") + ":500|0|0|0|0|0",
            "[day=2023-11-13]",
            "keys=100",
            "clicks=20",
            "left=15",
            "right=4",
            "middle=1",
            "xbuttons=0",
            "wheel=3",
            "move=1234.5",
            "move_m=1.5",
            "hk=9:40,10:60",
            "hc=9:10,10:10",
            "ch=3:6:7,4:7:8",
            "kk=65:30,66:70",
            "app=" + AppActivity.Encode(editorPath) + "|" + AppActivity.Encode(editorTitle) + "|30|10|2|120.5",
            "active_seconds=3600",
            "[day=2023-11-14]",
            "keys=30",
            "clicks=5",
            "left=3",
            "right=1",
            "middle=1",
            "wheel=0",
            "move=100",
            "hk=11:30",
            "hc=11:5",
            "ch=5:5:2",
            "kk=67:30");
    }

    private static void WriteSource(string sandbox, string statsText)
    {
        string src = SourceDir(sandbox);
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "stats.txt"), statsText, new UTF8Encoding(false));
    }

    private static string HashOf(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    private static long CountTable(string dbPath, string table)
    {
        KmsDb db = KmsDb.OpenReader(dbPath);
        try { return db.ScalarLong("SELECT COUNT(*) FROM " + table + ";", null); }
        finally { db.Dispose(); }
    }

    private static long Scalar(string dbPath, string sql)
    {
        KmsDb db = KmsDb.OpenReader(dbPath);
        try { return db.ScalarLong(sql, null); }
        finally { db.Dispose(); }
    }

    private static string ScalarText(string dbPath, string sql)
    {
        KmsDb db = KmsDb.OpenReader(dbPath);
        try { return db.ScalarText(sql, null); }
        finally { db.Dispose(); }
    }

    private static void Execute(string dbPath, string sql)
    {
        KmsDb db = KmsDb.OpenWriter(dbPath);
        try { db.Execute(sql); }
        finally { db.Dispose(); }
    }

    private static void ResetMarker(string dbPath)
    {
        Execute(dbPath, "UPDATE meta SET v='' WHERE k='migrated_from_format';");
    }

    // ---------------------------------------------------------------- 用例

    /// <summary>1. 三次运行严格幂等。</summary>
    private static void IdempotentThreeRuns()
    {
        string sandbox = NewSandbox("idem");
        try
        {
            WriteSource(sandbox, FullStatsText("13"));

            MigrationOutcome first = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
            Check(first.Performed && !first.Failed, "首次迁移应成功 → " + first.Describe());
            Equal(first.DaysImported, 2, "应导入两天");
            Equal(first.ArchivesImported, 1, "应导入一个月档");
            Equal(first.RulesImported, 1, "应导入一条分类规则");

            string dbPath = DbPath(sandbox);
            long[] baseline = Snapshot(dbPath);

            for (int round = 2; round <= 3; round++)
            {
                ResetMarker(dbPath);
                MigrationOutcome again = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
                Check(!again.Failed, "第 " + round + " 次运行不应失败 → " + again.Describe());
                Equal(again.DaysImported, 0, "第 " + round + " 次不应重复导入日记录");

                long[] now = Snapshot(dbPath);
                for (int i = 0; i < baseline.Length; i++)
                    Equal(now[i], baseline[i], "第 " + round + " 次运行后第 " + i + " 项计数应不变");
            }

            // 幂等复跑应复用既有备份,而不是再建一份。
            string[] backups = Directory.GetDirectories(Path.Combine(SourceDir(sandbox), V2Migrator.BackupFolderName));
            Equal(backups.Length, 1, "重复运行不应产生多份备份");

            // 历史单通道网格只进 channel=0。
            Equal(Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE channel<>0;"), 0,
                "历史网格不得进入任何按键通道");
            Equal(Scalar(dbPath, "SELECT SUM(count) FROM daily_click_grid WHERE channel=0;"), 17,
                "兼容通道应承接全部历史点击(7+8+2)");
            Equal(Scalar(dbPath, "SELECT SUM(count) FROM daily_click_grid;"), 17, "网格总和守恒");

            // 累计由日+月档重建,原 total_* 只留痕。
            Equal(Scalar(dbPath, "SELECT CAST(v AS INTEGER) FROM meta WHERE k='legacy_total_keys';"), 130,
                "legacy_total_keys 应原样留痕");
            Equal(Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM daily_summary WHERE origin='migrated';"), 130,
                "日记录击键合计");
            Equal(Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM month_archive;"), 500, "月档击键合计");
        }
        finally
        {
            TryDelete(sandbox);
        }
    }

    private static long[] Snapshot(string dbPath)
    {
        return new long[]
        {
            CountTable(dbPath, "daily_summary"),
            CountTable(dbPath, "daily_hour_agg"),
            CountTable(dbPath, "daily_key_agg"),
            CountTable(dbPath, "window_daily_agg"),
            CountTable(dbPath, "daily_click_grid"),
            CountTable(dbPath, "month_archive"),
            CountTable(dbPath, "app_category_rule"),
            CountTable(dbPath, "migration_log"),
            Scalar(dbPath, "SELECT IFNULL(SUM(count),0) FROM daily_click_grid;"),
            Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;")
        };
    }

    /// <summary>2. manifest 逐行可复核;源文件未被改动。</summary>
    private static void BackupManifestVerifiedAndSourceUntouched()
    {
        string sandbox = NewSandbox("backup");
        try
        {
            WriteSource(sandbox, FullStatsText("13"));
            string statsPath = Path.Combine(SourceDir(sandbox), "stats.txt");
            string before = HashOf(statsPath);

            MigrationOutcome outcome = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
            Check(outcome.Performed, "迁移应成功 → " + outcome.Describe());
            Check(string.Equals(outcome.BackupAction, "created", StringComparison.Ordinal),
                "首次应新建备份,实际=" + outcome.BackupAction);
            Check(Directory.Exists(outcome.BackupFolder), "备份目录应存在");

            string manifestPath = Path.Combine(outcome.BackupFolder, V2Migrator.ManifestName);
            Check(File.Exists(manifestPath), "manifest.txt 应存在");

            string[] lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
            int verified = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split(new string[] { "  " }, StringSplitOptions.None);
                if (parts.Length != 3) continue;

                string sha = parts[0];
                string relative = parts[1];
                long bytes = long.Parse(parts[2]);
                string copy = Path.Combine(outcome.BackupFolder, relative);

                Check(File.Exists(copy), "备份副本应存在:" + relative);
                Equal(new FileInfo(copy).Length, bytes, "副本字节数应与 manifest 一致:" + relative);
                Check(string.Equals(HashOf(copy), sha, StringComparison.OrdinalIgnoreCase),
                    "副本 sha256 应与 manifest 一致:" + relative);
                verified++;
            }
            Check(verified >= 1, "manifest 应至少复核一个文件");
            Equal(verified, 1, "本次只有 stats.txt 一个源文件");

            // 源文件一个字节都不许动。
            Check(string.Equals(HashOf(statsPath), before, StringComparison.Ordinal),
                "迁移不得修改源 stats.txt");
        }
        finally
        {
            TryDelete(sandbox);
        }
    }

    /// <summary>3. 坏档:中止、不写库、不改文件。</summary>
    private static void BadArchiveAborts()
    {
        string sandbox = NewSandbox("bad");
        try
        {
            // [day=] 存在但日期非法 → 解析不出任何日记录。
            WriteSource(sandbox, Text("format=13", "[day=not-a-date]", "keys=1"));
            string statsPath = Path.Combine(SourceDir(sandbox), "stats.txt");
            string before = HashOf(statsPath);

            MigrationOutcome outcome = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
            Check(!outcome.Performed, "坏档不得执行迁移");
            Check(outcome.Failed, "坏档应判定为失败 → " + outcome.Describe());

            Check(string.Equals(HashOf(statsPath), before, StringComparison.Ordinal), "坏档不得被改写");
            Check(!File.Exists(DbPath(sandbox)) || CountTable(DbPath(sandbox), "daily_summary") == 0,
                "坏档不得写入任何日记录");
            Check(!Directory.Exists(Path.Combine(SourceDir(sandbox), V2Migrator.BackupFolderName)),
                "坏档不应进入备份阶段");
        }
        finally
        {
            TryDelete(sandbox);
        }
    }

    /// <summary>3b. 空档:安全跳过。</summary>
    private static void EmptyArchiveSkips()
    {
        string sandbox = NewSandbox("empty");
        try
        {
            WriteSource(sandbox, Text("format=13"));
            MigrationOutcome outcome = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
            Check(!outcome.Failed, "空档不应判为失败");
            Check(outcome.Skipped, "空档应跳过 → " + outcome.Describe());
        }
        finally
        {
            TryDelete(sandbox);
        }
    }

    /// <summary>4. format 10~13 与月档合并。</summary>
    private static void FormatRangeAndMonthMerge()
    {
        foreach (int format in new int[] { 10, 11, 12, 13 })
        {
            string sandbox = NewSandbox("fmt" + format);
            try
            {
                WriteSource(sandbox, FullStatsText(format.ToString()));

                // 月档:另一天,只存在于 2022\2022-03.txt。
                string yearDir = Path.Combine(SourceDir(sandbox), "2022");
                Directory.CreateDirectory(yearDir);
                File.WriteAllText(Path.Combine(yearDir, "2022-03.txt"), Text(
                    "format=" + format.ToString(),
                    "storage_layout=monthly-v1",
                    "[day=2022-03-05]",
                    "keys=7",
                    "clicks=2",
                    "left=2",
                    "hk=8:7",
                    "hc=8:2"), new UTF8Encoding(false));

                MigrationOutcome outcome = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
                Check(outcome.Performed, "format=" + format + " 应迁移成功 → " + outcome.Describe());
                Equal(outcome.SourceFormat, format, "source format 应为 stats.txt 的 format");
                Equal(outcome.DaysImported, 3, "format=" + format + " 应导入 3 天(2 天主档 + 1 天月档)");

                string dbPath = DbPath(sandbox);
                Equal(Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary WHERE day='2022-03-05';"), 1,
                    "月档中的日期应被并入");
                Equal(Scalar(dbPath, "SELECT keys FROM daily_summary WHERE day='2022-03-05';"), 7,
                    "月档日期击键应正确");
                Equal(Scalar(dbPath, "SELECT CAST(v AS INTEGER) FROM meta WHERE k='migrated_from_format';"),
                    format, "migrated_from_format 应记录源版本");
            }
            finally
            {
                TryDelete(sandbox);
            }
        }
    }

    /// <summary>5. 守恒断言失败 → 事务回滚,现场保留。</summary>
    private static void ConservationFailureRollsBack()
    {
        string sandbox = NewSandbox("rollback");
        try
        {
            WriteSource(sandbox, FullStatsText("13"));
            Directory.CreateDirectory(DbDir(sandbox));

            // 先落一个「毒丸」日行:origin='migrated' 但不来自源档,必然破坏断言 ①。
            KmsDb seed = KmsDb.OpenWriter(DbPath(sandbox));
            try
            {
                KmsSchema.Ensure(seed);
                seed.Execute(
                    "INSERT INTO daily_summary (day, keys, clicks, origin) VALUES ('1999-01-01', 999, 0, 'migrated');",
                    null);
            }
            finally
            {
                seed.Dispose();
            }

            MigrationOutcome outcome = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
            Check(outcome.Failed, "毒丸应触发守恒断言失败 → " + outcome.Describe());
            Check(outcome.Reason.IndexOf("守恒", StringComparison.Ordinal) >= 0,
                "失败原因应指向守恒断言,实际=" + outcome.Reason);

            string dbPath = DbPath(sandbox);
            Equal(Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary WHERE day='2023-11-13';"), 0,
                "失败事务必须回滚,不得留下源日记录");
            Equal(Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary WHERE day='2023-11-14';"), 0,
                "失败事务必须回滚(第二天)");
            Equal(Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary WHERE day='1999-01-01';"), 1,
                "事务外的毒丸行应保留(只回滚迁移自身)");
            Equal(Scalar(dbPath, "SELECT COUNT(*) FROM month_archive;"), 0, "月档也应回滚");
            string marker = ScalarText(dbPath, "SELECT v FROM meta WHERE k='migrated_from_format';");
            Check(string.IsNullOrEmpty(marker), "失败后 migrated_from_format 必须仍为空,实际=" + marker);

            // 备份必须保留,供人工排查。
            Check(Directory.Exists(Path.Combine(SourceDir(sandbox), V2Migrator.BackupFolderName)),
                "回滚后 backup_v2 现场应保留");
        }
        finally
        {
            TryDelete(sandbox);
        }
    }

    /// <summary>6. 快照桥:stats.txt 比 meta.last_export_ms 新时,只补缺失日期,绝不覆盖已有行。</summary>
    private static void SnapshotBridgeFillsMissingDaysOnly()
    {
        string sandbox = NewSandbox("bridge");
        try
        {
            WriteSource(sandbox, FullStatsText("13"));
            MigrationOutcome outcome = V2Migrator.Run(SourceDir(sandbox), DbDir(sandbox));
            Check(outcome.Performed, "先决条件:迁移应成功");

            string dbPath = DbPath(sandbox);
            string statsPath = Path.Combine(SourceDir(sandbox), "stats.txt");

            // 造出「文本被外部改写过 + 库中缺一天」的局面。
            Execute(dbPath, "UPDATE meta SET v='1' WHERE k='last_export_ms';");
            Execute(dbPath, "DELETE FROM daily_summary WHERE day='2023-11-13';");
            File.SetLastWriteTimeUtc(statsPath, DateTime.UtcNow);

            string reason;
            int added = V2Migrator.SyncDerivedSnapshotIfNewer(SourceDir(sandbox), DbDir(sandbox), out reason);
            Equal(added, 1, "只应补入缺失的那一天:" + reason);
            Equal(Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary WHERE day='2023-11-13';"), 1,
                "缺失日应被补回");
            Equal(Scalar(dbPath, "SELECT keys FROM daily_summary WHERE day='2023-11-13';"), 100,
                "补回的击键应正确");
            Check(string.Equals(ScalarText(dbPath, "SELECT origin FROM daily_summary WHERE day='2023-11-13';"),
                "snapshot-bridge", StringComparison.Ordinal), "补回的行应标记 origin=snapshot-bridge");

            // 已存在的日期不得被覆盖。
            Equal(Scalar(dbPath, "SELECT keys FROM daily_summary WHERE day='2023-11-14';"), 30,
                "已有日期不得被快照桥覆盖");

            // last_export_ms 已推进,第二次应短路。
            int again = V2Migrator.SyncDerivedSnapshotIfNewer(SourceDir(sandbox), DbDir(sandbox), out reason);
            Equal(again, 0, "第二次应短路不再补数:" + reason);
        }
        finally
        {
            TryDelete(sandbox);
        }
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, true); }
        catch { }
    }}
