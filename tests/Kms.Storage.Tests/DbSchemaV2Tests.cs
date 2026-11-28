using System.Globalization;
using Kms.Core.Apps;
using Kms.Core.Input;
using Kms.Core.Time;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

public sealed class DbSchemaV2Tests
{
    private const long T0 = 1_700_000_000_000L;

    [Fact]
    public void Ensure_UpgradesV1ToV2_Idempotently_WithoutBackfillingHistory()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        CopyFixture("v3-sqlite-normal.db", path);

        try
        {
            using (SqliteConnection connection = Open(path, SqliteOpenMode.ReadWrite))
            {
                long daysBefore = ScalarLong(connection, "SELECT COUNT(*) FROM daily_summary;");
                long keysBefore = ScalarLong(connection, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;");
                long windowsBefore = ScalarLong(connection, "SELECT COUNT(*) FROM window_daily_agg;");

                KmsSchema.Ensure(connection, "4.2.0-test");
                KmsSchema.Ensure(connection, "should-not-overwrite");

                Assert.Empty(KmsSchema.MissingTables(connection));
                Assert.Equal("3", ScalarText(connection, "SELECT v FROM meta WHERE k='schema_rev';"));
                Assert.Equal(3L, ScalarLong(connection, "PRAGMA user_version;"));
                Assert.Equal(1L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_dim WHERE app_uid=-1 AND app_key='__other__';"));
                Assert.Equal(
                    AppCategoryBuiltins.All.Count,
                    ScalarLong(connection, "SELECT COUNT(*) FROM app_category;"));
                Assert.Equal(0L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_category_rule WHERE category_key='';"));

                Assert.Equal(daysBefore, ScalarLong(connection, "SELECT COUNT(*) FROM daily_summary;"));
                Assert.Equal(keysBefore, ScalarLong(connection, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;"));
                Assert.Equal(windowsBefore, ScalarLong(connection, "SELECT COUNT(*) FROM window_daily_agg;"));

                // 历史数据不回填：新分桶表必须为空。
                Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM daily_app_hour_agg;"));
                Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM daily_app_key_agg;"));
                Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM daily_app_click_grid;"));
                Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM daily_app_metric_agg;"));

                foreach (string index in new[]
                         {
                             "ix_wda_proc_day", "ix_ews_proc_day", "ix_eib_proc_day",
                             "ix_daily_app_hour_day", "ix_daily_app_key_day",
                             "ix_daily_app_click_day", "ix_daily_app_metric_day"
                         })
                {
                    Assert.Equal(1L, ScalarLong(connection,
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='" + index + "';"));
                }
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Ensure_UpgradesV2ToV3_BackfillsRulesAndSeedsBuiltins_Idempotently()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        CopyFixture("v3-sqlite-normal.db", path);

        try
        {
            using (SqliteConnection connection = Open(path, SqliteOpenMode.ReadWrite))
            {
                // 先手工构造成 schema_rev=2：v1 数据 + v2 分桶表，但没有分类表与 category_key。
                foreach (string statement in KmsSchema.V2Statements())
                {
                    Exec(connection, statement);
                }

                Exec(connection, "UPDATE meta SET v='2' WHERE k='schema_rev';");
                Exec(connection, "PRAGMA user_version = 2;");
                long daysBefore = ScalarLong(connection, "SELECT COUNT(*) FROM daily_summary;");
                long keysBefore = ScalarLong(connection, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;");
                long rulesBefore = ScalarLong(connection, "SELECT COUNT(*) FROM app_category_rule;");
                Assert.Equal(0L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='app_category';"));
                Assert.Equal(0L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM pragma_table_info('app_category_rule') WHERE name='category_key';"));

                KmsSchema.Ensure(connection, "4.2.0-v3");
                KmsSchema.Ensure(connection, "should-not-overwrite");

                Assert.Empty(KmsSchema.MissingTables(connection));
                Assert.Equal("3", ScalarText(connection, "SELECT v FROM meta WHERE k='schema_rev';"));
                Assert.Equal(3L, ScalarLong(connection, "PRAGMA user_version;"));
                Assert.Equal(AppCategoryBuiltins.All.Count, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_category;"));
                Assert.Equal(1L, ScalarLong(connection,
                    "SELECT is_work FROM app_category WHERE category_key='coding';"));
                Assert.Equal(0L, ScalarLong(connection,
                    "SELECT is_work FROM app_category WHERE category_key='game';"));
                Assert.Equal(rulesBefore, ScalarLong(connection, "SELECT COUNT(*) FROM app_category_rule;"));
                Assert.Equal(0L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_category_rule WHERE category_key='';"));
                Assert.Equal(11L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_category_rule WHERE category_key='office' AND category_id=5;"));
                Assert.Equal(1L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ux_app_category_legacy';"));

                // 历史统计不因 v3 迁移改变。
                Assert.Equal(daysBefore, ScalarLong(connection, "SELECT COUNT(*) FROM daily_summary;"));
                Assert.Equal(keysBefore, ScalarLong(connection, "SELECT IFNULL(SUM(keys),0) FROM daily_summary;"));
                Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM daily_app_hour_agg;"));
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Ensure_MergesLegacyEntertainmentCategoryAndRules_Idempotently()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            using (SqliteConnection connection = Open(path, SqliteOpenMode.ReadWriteCreate))
            {
                KmsSchema.Ensure(connection, "4.2.0-test");
                Exec(connection,
                    "INSERT INTO app_category " +
                    "(category_key,name,color_hex,is_work,sort_order,is_builtin,legacy_id) " +
                    "VALUES ('entertainment','娱乐','#F97316',0,9,1,9);");
                Exec(connection,
                    "INSERT INTO app_category_rule " +
                    "(rule_key,category_id,category_key,source,updated_ms) " +
                    "VALUES ('app:legacy-old.exe',9,'entertainment','user_override',1)," +
                    "('app:legacy-number.exe',9,'','user_override',2);");

                KmsSchema.Ensure(connection, "4.2.0-v3");
                KmsSchema.Ensure(connection, "4.2.0-v3");

                Assert.Equal(0L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_category WHERE category_key='entertainment';"));
                Assert.Equal(1L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_category WHERE category_key='media' AND legacy_id=2;"));
                Assert.Equal(2L, ScalarLong(connection,
                    "SELECT COUNT(*) FROM app_category_rule " +
                    "WHERE category_key='media' AND category_id=2;"));
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Writer_NormalizesUnicodePathAndReusesStableAppUid()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        string day = UnixTime.ToLocal(T0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            WriteKey(path, T0, 65, @"C:\工具\Goink.EXE", "Goink.exe");
            WriteKey(path, T0 + 1_000, 66, @"c:\工具\goink.exe", "goink.exe");

            using SqliteConnection db = Open(path, SqliteOpenMode.ReadOnly);
            Assert.Equal(1L, ScalarLong(db,
                "SELECT COUNT(*) FROM app_dim WHERE app_key='c:\\工具\\goink.exe';"));
            Assert.Equal(1L, ScalarLong(db,
                "SELECT COUNT(*) FROM app_dim WHERE canonical_path='C:\\工具\\Goink.EXE';"));
            Assert.Equal(2L, ScalarLong(db,
                "SELECT SUM(cnt) FROM daily_app_key_agg WHERE app_uid<>-1;"));
            Assert.Equal(2L, ScalarLong(db,
                "SELECT SUM(keys) FROM daily_app_hour_agg WHERE day='" + day + "';"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Writer_Overflow64ConservesKeysInOtherBucket()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        string day = UnixTime.ToLocal(T0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            EventRing ring = new(256);
            using (DbWriter writer = DbWriter.Open(
                path,
                ring,
                new DbWriterOptions { RunSelfHeal = false, Diagnostic = static _ => { } }))
            {
                writer.ResolveWindow = hwnd =>
                {
                    int id = hwnd.ToInt32();
                    return new WindowTarget
                    {
                        ProcessPath = @"C:\Apps\App" + id.ToString("00", CultureInfo.InvariantCulture) + ".exe",
                        ProcessName = "App" + id.ToString("00", CultureInfo.InvariantCulture) + ".exe",
                        WindowTitle = "Window " + id.ToString(CultureInfo.InvariantCulture)
                    };
                };

                for (int i = 1; i <= 65; i++)
                {
                    Assert.True(writer.Enqueue(
                        InputEventKind.KeyDown,
                        new IntPtr(i),
                        65,
                        0,
                        0,
                        0,
                        writer.TicksForMs(T0 + i * 200L)));
                }

                Assert.Equal(65, writer.DrainOnce());
            }

            using SqliteConnection db = Open(path, SqliteOpenMode.ReadOnly);
            Assert.Equal(64L, ScalarLong(db,
                "SELECT COUNT(*) FROM app_dim WHERE app_key<>'__other__';"));
            Assert.Equal(65L, ScalarLong(db, "SELECT SUM(cnt) FROM daily_app_key_agg;"));
            Assert.Equal(64L, ScalarLong(db,
                "SELECT SUM(cnt) FROM daily_app_key_agg WHERE app_uid<>-1;"));
            Assert.Equal(1L, ScalarLong(db,
                "SELECT SUM(cnt) FROM daily_app_key_agg WHERE app_uid=-1;"));
            Assert.Equal(65L, ScalarLong(db, "SELECT SUM(cnt) FROM daily_key_agg;"));
            Assert.Equal(65L, ScalarLong(db,
                "SELECT SUM(keys) FROM daily_app_hour_agg WHERE day='" + day + "';"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Writer_RemovesAppClickCellsOlderThanThirtyDays()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, KmsSchema.DbFileName);
        try
        {
            using (SqliteConnection connection = Open(path, SqliteOpenMode.ReadWriteCreate))
            {
                KmsSchema.Ensure(connection, "test");
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO daily_app_click_grid (app_uid,day,channel,cell_x,cell_y,count) " +
                    "VALUES (-1,'2000-01-01',1,1,1,9);";
                command.ExecuteNonQuery();
            }

            WriteKey(path, T0, 65, @"C:\Apps\Code.exe", "Code.exe");

            using SqliteConnection db = Open(path, SqliteOpenMode.ReadOnly);
            Assert.Equal(0L, ScalarLong(db,
                "SELECT COUNT(*) FROM daily_app_click_grid WHERE day='2000-01-01';"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static void WriteKey(
        string path,
        long timestampMs,
        int vk,
        string processPath,
        string processName)
    {
        EventRing ring = new(64);
        using DbWriter writer = DbWriter.Open(
            path,
            ring,
            new DbWriterOptions { RunSelfHeal = false, Diagnostic = static _ => { } });
        writer.ResolveWindow = _ => new WindowTarget
        {
            ProcessPath = processPath,
            ProcessName = processName,
            WindowTitle = "title"
        };
        Assert.True(writer.Enqueue(
            InputEventKind.KeyDown,
            new IntPtr(1),
            vk,
            0,
            0,
            0,
            writer.TicksForMs(timestampMs)));
        Assert.Equal(1, writer.DrainOnce());
    }

    private static void CopyFixture(string name, string destination)
    {
        string source = Path.Combine(RepoRoot(), "tests", "Fixtures", name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
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

    private static string NewDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "kms-schema-v2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
        connection.Open();
        return connection;
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
