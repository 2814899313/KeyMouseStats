using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kms.Core.Model;
using Kms.Storage.Migration;
using Kms.Storage.Sqlite;
using Kms.Storage.Text;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

/// <summary>
/// v2 文本 → SQLite 迁移的端到端测试。全部读写都在仓库根 <c>.tmp-migrator-tests/</c> 下，
/// 不触碰真实用户数据目录。
/// </summary>
public sealed class V2MigratorTests
{
    // ---------------------------------------------------------------- 真实固件

    [Fact]
    public void Run_RealFixture_Imports48DaysAndConserves()
    {
        string sandbox = NewSandbox("real");
        try
        {
            string source = Path.Combine(sandbox, "src");
            string dbRoot = Path.Combine(sandbox, "db");
            Directory.CreateDirectory(source);

            string fixtures = Path.Combine(RepoRoot(), "tests", "Fixtures");
            string statsPath = Path.Combine(source, "stats.txt");
            File.Copy(Path.Combine(fixtures, "v13-text-current.txt"), statsPath);
            File.Copy(statsPath, statsPath + ".bak");

            string year = Path.Combine(source, "2026");
            Directory.CreateDirectory(year);
            File.Copy(
                Path.Combine(fixtures, "v13-month-2026-09.txt"),
                Path.Combine(year, "2026-09.txt"));
            File.Copy(
                Path.Combine(fixtures, "v13-month-2026-10.txt"),
                Path.Combine(year, "2026-10.txt"));

            string statsBefore = HashFile(statsPath);
            string[] sourceFiles =
            [
                statsPath,
                statsPath + ".bak",
                Path.Combine(year, "2026-09.txt"),
                Path.Combine(year, "2026-10.txt")
            ];
            string[] sourceHashes = [.. sourceFiles.Select(HashFile)];

            MigrationOutcome outcome = V2Migrator.Run(source, dbRoot);
            Assert.False(outcome.Failed, outcome.Describe());
            Assert.True(outcome.Performed, outcome.Describe());
            Assert.Equal(13, outcome.SourceFormat);
            Assert.Equal(48, outcome.DaysImported);
            Assert.Equal(0, outcome.ArchivesImported);
            Assert.Equal(30, outcome.RulesImported);
            Assert.True(outcome.GridCellsImported > 0, outcome.Describe());

            string dbPath = Path.Combine(dbRoot, KmsSchema.DbFileName);
            Assert.True(File.Exists(dbPath));

            // 48 天 = stats.txt 22 天 + 2026-09 月档 21 天 + 2026-10 月档 5 天。
            // 实际日合计 321890；303401 来自 tests/Fixtures/v3-sqlite-normal.db（另一份 SQLite 快照）。
            Assert.Equal(48L, Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary;"));
            Assert.Equal(48L, Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary WHERE origin='migrated';"));
            Assert.Equal(321890L, Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;"));
            Assert.Equal(
                321890L,
                Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM daily_summary WHERE origin='migrated';"));

            // 4 组守恒：日击键 / 日点击 / 网格 / 小时桶 / 月档（月档真实固件为空）。
            long dayClicks = Scalar(dbPath, "SELECT IFNULL(SUM(clicks),0) FROM daily_summary;");
            Assert.True(dayClicks > 0);
            Assert.Equal(dayClicks, Scalar(dbPath, "SELECT IFNULL(SUM(clicks),0) FROM daily_hour_agg;"));
            Assert.Equal(
                Scalar(dbPath, "SELECT IFNULL(SUM(count),0) FROM daily_click_grid;"),
                FlatCellTotal(source, fixtures));
            Assert.Equal(
                321890L,
                Scalar(
                    dbPath,
                    "SELECT IFNULL(SUM(a.keys),0) FROM daily_hour_agg a " +
                    "JOIN daily_summary d ON d.day=a.day WHERE d.origin='migrated';"));
            Assert.Equal(0L, Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM month_archive;"));

            // 其它数据路径都应有内容，且窗口击键不得超过日总数。
            Assert.True(Scalar(dbPath, "SELECT COUNT(*) FROM daily_hour_agg;") > 0);
            Assert.True(Scalar(dbPath, "SELECT COUNT(*) FROM daily_key_agg;") > 0);
            Assert.True(Scalar(dbPath, "SELECT COUNT(*) FROM window_daily_agg;") > 0);
            Assert.True(Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid;") > 0);
            Assert.Equal(30L, Scalar(dbPath, "SELECT COUNT(*) FROM app_category_rule;"));
            long windowKeys = Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM window_daily_agg;");
            Assert.True(windowKeys > 0);
            Assert.True(windowKeys <= 321890L, "窗口击键不应超过日总数(实际 " + windowKeys + ")");

            // 真实 v13 档同时含旧单通道 ch 与新三通道 chv1。
            Assert.True(Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE channel=0;") > 0);
            Assert.True(Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE channel<>0;") > 0);

            // meta 留痕。
            Assert.Equal("13", Text(dbPath, "SELECT v FROM meta WHERE k='migrated_from_format';"));
            Assert.Equal("48", Text(dbPath, "SELECT v FROM meta WHERE k='migrated_days';"));
            Assert.True(
                long.Parse(Text(dbPath, "SELECT v FROM meta WHERE k='migrated_at_ms';"), CultureInfo.InvariantCulture) > 0);
            Assert.Equal("321890", Text(dbPath, "SELECT v FROM meta WHERE k='legacy_total_keys';"));
            Assert.Equal(
                "0",
                Text(dbPath, "SELECT v FROM meta WHERE k='legacy_total_delta';"));

            // migration_log：stats / stats.bak / 两个月档，主键去重。
            Assert.Equal(4L, Scalar(dbPath, "SELECT COUNT(*) FROM migration_log;"));
            Assert.Equal(
                2L,
                Scalar(dbPath, "SELECT COUNT(*) FROM migration_log WHERE source_kind='v2-stats';"));
            Assert.Equal(
                2L,
                Scalar(dbPath, "SELECT COUNT(*) FROM migration_log WHERE source_kind='v2-month';"));

            // 备份 manifest 可逐行复核，源文件一个字节都没动。
            Assert.Equal("created", outcome.BackupAction);
            Assert.True(Directory.Exists(outcome.BackupFolder));
            VerifyManifest(outcome.BackupFolder, expectedEntries: 4);
            Assert.Equal(statsBefore, HashFile(statsPath));
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                Assert.Equal(sourceHashes[i], HashFile(sourceFiles[i]));
            }
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    [Fact]
    public void Run_SecondRunIsIdempotent()
    {
        string sandbox = NewSandbox("idem");
        try
        {
            string source = Path.Combine(sandbox, "src");
            string dbRoot = Path.Combine(sandbox, "db");
            Directory.CreateDirectory(source);
            File.WriteAllText(
                Path.Combine(source, "stats.txt"),
                SyntheticText(includeArchive: true),
                new UTF8Encoding(false));

            MigrationOutcome first = V2Migrator.Run(source, dbRoot);
            Assert.True(first.Performed, first.Describe());
            Assert.Equal(2, first.DaysImported);

            string dbPath = Path.Combine(dbRoot, KmsSchema.DbFileName);
            long[] baseline = Snapshot(dbPath);
            string[] backups = Directory.GetDirectories(Path.Combine(source, V2Migrator.BackupFolderName));
            Assert.Single(backups);

            MigrationOutcome second = V2Migrator.Run(source, dbRoot);
            Assert.False(second.Failed, second.Describe());
            Assert.True(second.Skipped, second.Describe());
            Assert.Contains("已迁移", second.Reason, StringComparison.Ordinal);
            Assert.Equal(baseline, Snapshot(dbPath));
            Assert.Equal("2", Text(dbPath, "SELECT v FROM meta WHERE k='migrated_days';"));
            Assert.Single(Directory.GetDirectories(Path.Combine(source, V2Migrator.BackupFolderName)));

            // 把门禁标记清掉后再跑一次：靠 migration_log 主键与逐日 INSERT OR IGNORE 做到 0 行变化。
            Execute(dbPath, "UPDATE meta SET v='' WHERE k='migrated_from_format';");
            MigrationOutcome third = V2Migrator.Run(source, dbRoot);
            Assert.False(third.Failed, third.Describe());
            Assert.True(third.Performed, third.Describe());
            Assert.Equal(0, third.DaysImported);
            Assert.Equal(0, third.ArchivesImported);
            Assert.Equal(0, third.RulesImported);
            Assert.Equal("reused", third.BackupAction);
            Assert.Equal(baseline, Snapshot(dbPath));
            Assert.Single(Directory.GetDirectories(Path.Combine(source, V2Migrator.BackupFolderName)));
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    // ---------------------------------------------------------------- 合成固件

    [Fact]
    public void Run_SyntheticArchive_ImportsAllPathsAndConserves()
    {
        string sandbox = NewSandbox("synth");
        try
        {
            string source = Path.Combine(sandbox, "src");
            string dbRoot = Path.Combine(sandbox, "db");
            Directory.CreateDirectory(source);
            File.WriteAllText(
                Path.Combine(source, "stats.txt"),
                SyntheticText(includeArchive: true),
                new UTF8Encoding(false));

            MigrationOutcome outcome = V2Migrator.Run(source, dbRoot);
            Assert.False(outcome.Failed, outcome.Describe());
            Assert.True(outcome.Performed, outcome.Describe());
            Assert.Equal(13, outcome.SourceFormat);
            Assert.Equal(2, outcome.DaysImported);
            Assert.Equal(1, outcome.ArchivesImported);
            Assert.Equal(1, outcome.RulesImported);
            Assert.Equal(3, outcome.GridCellsImported);

            string dbPath = Path.Combine(dbRoot, KmsSchema.DbFileName);
            Assert.Equal(130L, Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;"));
            Assert.Equal(25L, Scalar(dbPath, "SELECT IFNULL(SUM(clicks),0) FROM daily_summary;"));

            // 小时桶 / 网格与日总数同源。
            Assert.Equal(130L, Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM daily_hour_agg;"));
            Assert.Equal(25L, Scalar(dbPath, "SELECT IFNULL(SUM(clicks),0) FROM daily_hour_agg;"));
            Assert.Equal(17L, Scalar(dbPath, "SELECT IFNULL(SUM(count),0) FROM daily_click_grid;"));
            Assert.Equal(0L, Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE channel<>0;"));
            Assert.Equal(130L, Scalar(dbPath, "SELECT IFNULL(SUM(cnt),0) FROM daily_key_agg;"));

            // 窗口维度：两条应用合计 100 + 一条 30，正好等于日击键。
            Assert.Equal(3L, Scalar(dbPath, "SELECT COUNT(*) FROM window_daily_agg;"));
            Assert.Equal(130L, Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM window_daily_agg;"));
            Assert.Equal(25L, Scalar(dbPath, "SELECT IFNULL(SUM(clicks),0) FROM window_daily_agg;"));

            // 月档与月档应用。
            Assert.Equal(1L, Scalar(dbPath, "SELECT COUNT(*) FROM month_archive;"));
            Assert.Equal(500L, Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM month_archive;"));
            Assert.Equal(1L, Scalar(dbPath, "SELECT COUNT(*) FROM month_archive_app;"));
            Assert.Equal(500L, Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM month_archive_app;"));

            // 分类规则与 meta。
            Assert.Equal(1L, Scalar(dbPath, "SELECT COUNT(*) FROM app_category_rule;"));
            Assert.Equal(0L, Scalar(dbPath, "SELECT category_id FROM app_category_rule LIMIT 1;"));
            Assert.Equal("1", Text(dbPath, "SELECT v FROM meta WHERE k='theme_id';"));
            Assert.Equal("60", Text(dbPath, "SELECT v FROM meta WHERE k='idle_threshold';"));
            Assert.Equal("800", Text(dbPath, "SELECT v FROM meta WHERE k='mouse_dpi';"));
            Assert.Equal("0", Text(dbPath, "SELECT v FROM meta WHERE k='keep_days';"));
            Assert.Equal("130", Text(dbPath, "SELECT v FROM meta WHERE k='legacy_total_keys';"));
            Assert.Equal("-500", Text(dbPath, "SELECT v FROM meta WHERE k='legacy_total_delta';"));
            Assert.Equal(1L, Scalar(dbPath, "SELECT COUNT(*) FROM migration_log;"));
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    [Fact]
    public void Run_ChanneledGridUsesChannelsAndFallsBackOnMismatch()
    {
        string sandbox = NewSandbox("grid");
        try
        {
            string source = Path.Combine(sandbox, "src");
            string dbRoot = Path.Combine(sandbox, "db");
            Directory.CreateDirectory(source);
            File.WriteAllText(
                Path.Combine(source, "stats.txt"),
                Text(
                    "format=13",
                    "[day=2023-12-01]",
                    "keys=5",
                    "clicks=4",
                    "hk=10:5",
                    "hc=10:4",
                    "ch=10:10:4",
                    "chv1=1:10:10:2,2:10:10:2",
                    "[day=2023-12-02]",
                    "keys=3",
                    "clicks=2",
                    "hk=10:3",
                    "hc=10:2",
                    "ch=11:11:2",
                    "chv1=1:11:11:1"),
                new UTF8Encoding(false));

            MigrationOutcome outcome = V2Migrator.Run(source, dbRoot);
            Assert.True(outcome.Performed, outcome.Describe());
            Assert.Equal(3, outcome.GridCellsImported);

            string dbPath = Path.Combine(dbRoot, KmsSchema.DbFileName);
            Assert.Equal(6L, Scalar(dbPath, "SELECT IFNULL(SUM(count),0) FROM daily_click_grid;"));
            Assert.Equal(4L, Scalar(dbPath, "SELECT IFNULL(SUM(count),0) FROM daily_click_grid WHERE day='2023-12-01';"));
            Assert.Equal(
                2L,
                Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE day='2023-12-01' AND channel IN (1,2);"));
            Assert.Equal(
                0L,
                Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE day='2023-12-01' AND channel=0;"));
            Assert.Equal(
                1L,
                Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE day='2023-12-02' AND channel=0;"));
            Assert.Equal(
                0L,
                Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid WHERE day='2023-12-02' AND channel<>0;"));
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    // ---------------------------------------------------------------- 边界

    [Fact]
    public void Run_BadArchiveFailsWithoutTouchingSourceOrDatabase()
    {
        string sandbox = NewSandbox("bad");
        try
        {
            string source = Path.Combine(sandbox, "src");
            string dbRoot = Path.Combine(sandbox, "db");
            Directory.CreateDirectory(source);
            string statsPath = Path.Combine(source, "stats.txt");
            File.WriteAllText(
                statsPath,
                Text("format=13", "[day=not-a-date]", "keys=1"),
                new UTF8Encoding(false));
            string before = HashFile(statsPath);

            MigrationOutcome outcome = V2Migrator.Run(source, dbRoot);
            Assert.True(outcome.Failed, outcome.Describe());
            Assert.False(outcome.Performed);
            Assert.Contains("解析", outcome.Reason, StringComparison.Ordinal);

            Assert.Equal(before, HashFile(statsPath));
            Assert.False(
                File.Exists(Path.Combine(dbRoot, KmsSchema.DbFileName)),
                "坏档不应写入数据库");
            Assert.False(
                Directory.Exists(Path.Combine(source, V2Migrator.BackupFolderName)),
                "坏档不应进入备份阶段");
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    [Fact]
    public void Run_EmptyArchiveSkipsWithoutCreatingDatabase()
    {
        string sandbox = NewSandbox("empty");
        try
        {
            string source = Path.Combine(sandbox, "src");
            string dbRoot = Path.Combine(sandbox, "db");
            Directory.CreateDirectory(source);
            File.WriteAllText(
                Path.Combine(source, "stats.txt"),
                Text("format=13"),
                new UTF8Encoding(false));

            MigrationOutcome outcome = V2Migrator.Run(source, dbRoot);
            Assert.False(outcome.Failed, outcome.Describe());
            Assert.True(outcome.Skipped, outcome.Describe());
            Assert.False(Directory.Exists(dbRoot), "空档不应建库");
            Assert.False(Directory.Exists(Path.Combine(source, V2Migrator.BackupFolderName)));
        }
        finally
        {
            Cleanup(sandbox);
        }
    }

    // ---------------------------------------------------------------- 固件

    /// <summary>两天日记录 + 单通道网格 + 窗口应用 + 月档 + 分类规则。</summary>
    private static string SyntheticText(bool includeArchive)
    {
        string editorPath = @"C:\Apps\Code.exe";
        List<string> lines =
        [
            "# 键鼠统计数据文件 v13 / 合成测试档",
            "format=13",
            "storage_layout=monthly-v1",
            "shortcut_model_v1=2|0.5|2",
            "idle_threshold=60",
            "backup_keep=7",
            "wellbeing_v1=",
            "keep_days=0",
            "motion_v1=2|1|0",
            "text_refresh_v1=15",
            "window_layout_v1=0|0|0",
            "click_screen_v1=",
            "daily_report_v1=",
            "art_theme=1",
            "keyboard_layout=0",
            "all_time_peak_apm=120",
            "mouse_dpi=800",
            "app_rule=" + Encode(@"app:c:\apps\code.exe") + "|0",
            "date=2023-11-14",
            "total_keys=130",
            "total_clicks=25",
            "total_left=18",
            "total_right=5",
            "total_middle=2"
        ];

        if (includeArchive)
        {
            lines.Add(
                "archive_v1=2022-03|3|500|80|10|0|999|2.5|9|"
                + Encode(editorPath) + ":500|0|0|0|0|0");
        }

        lines.AddRange(
        [
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
            "app=" + Encode(editorPath) + "|" + Encode("main.cs - Code") + "|60|10|2|120.5",
            "app=" + Encode(@"C:\Apps\Terminal.exe") + "|" + Encode("pwsh") + "|40|10|1|60",
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
            "kk=67:30",
            "app=" + Encode(editorPath) + "|" + Encode("notes.txt - Code") + "|30|5|0|900"
        ]);

        return Text([.. lines]);
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    // ---------------------------------------------------------------- 查询

    private static long FlatCellTotal(string sourceRoot, string fixtures)
    {
        StatsData data = TextStatsCodec.Load(
            File.ReadAllText(Path.Combine(sourceRoot, "stats.txt")));
        foreach (string month in new[] { "2026-09.txt", "2026-10.txt" })
        {
            StatsData parsed = TextStatsCodec.Load(
                File.ReadAllText(Path.Combine(fixtures, "v13-month-" + month)));
            foreach (KeyValuePair<string, Kms.Core.Model.DayRecord> pair in parsed.History)
            {
                data.History.TryAdd(pair.Key, pair.Value);
            }
        }

        long total = 0;
        foreach (Kms.Core.Model.DayRecord day in data.History.Values)
        {
            foreach (long count in day.ClickCells.Values)
            {
                if (count > 0)
                {
                    total += count;
                }
            }
        }

        return total;
    }

    private static long[] Snapshot(string dbPath) =>
    [
        Scalar(dbPath, "SELECT COUNT(*) FROM daily_summary;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM daily_hour_agg;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM daily_key_agg;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM window_daily_agg;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM daily_click_grid;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM month_archive;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM month_archive_app;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM app_category_rule;"),
        Scalar(dbPath, "SELECT COUNT(*) FROM migration_log;"),
        Scalar(dbPath, "SELECT IFNULL(SUM(count),0) FROM daily_click_grid;"),
        Scalar(dbPath, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;")
    ];

    private static long Scalar(string dbPath, string sql)
    {
        using SqliteConnection connection = Open(dbPath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? 0
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static string Text(string dbPath, string sql)
    {
        using SqliteConnection connection = Open(dbPath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? ""
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private static void Execute(string dbPath, string sql)
    {
        using SqliteConnection connection = Open(dbPath);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string dbPath)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }

    // ---------------------------------------------------------------- 工具

    private static void VerifyManifest(string backupFolder, int expectedEntries)
    {
        string manifestPath = Path.Combine(backupFolder, V2Migrator.ManifestName);
        Assert.True(File.Exists(manifestPath), "manifest.txt 应存在");

        string[] lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
        Assert.Contains(lines, line => line.StartsWith("# fingerprint=", StringComparison.Ordinal));

        int verified = 0;
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split(["  "], StringSplitOptions.None);
            Assert.Equal(3, parts.Length);
            string relative = parts[1];
            long bytes = long.Parse(parts[2], CultureInfo.InvariantCulture);
            string copy = Path.Combine(backupFolder, relative.Replace('\\', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(copy), "备份副本应存在:" + relative);
            Assert.Equal(bytes, new FileInfo(copy).Length);
            Assert.Equal(parts[0], HashFile(copy), ignoreCase: true);
            verified++;
        }

        Assert.Equal(expectedEntries, verified);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        StringBuilder sb = new(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string Text(params string[] lines) =>
        string.Join("\n", lines) + "\n";

    private static string NewSandbox(string tag)
    {
        string directory = Path.Combine(
            RepoRoot(),
            ".tmp-migrator-tests",
            tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Cleanup(string directory)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(directory, true);
        }
        catch
        {
            // 测试清理失败不改变断言结果。
        }
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录。");
    }
}
