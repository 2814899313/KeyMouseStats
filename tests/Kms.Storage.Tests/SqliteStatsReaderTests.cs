using System.Globalization;
using System.Security.Cryptography;
using Kms.Core.Model;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

public sealed class SqliteStatsReaderTests
{
    [Fact]
    public void Read_NormalFixture_MapsAllThirteenTables()
    {
        string path = Fixture("v3-sqlite-normal.db");
        StatsData data = SqliteStatsReader.Read(path);

        Assert.Equal(20, KmsSchema.RequiredTables.Length);
        Assert.Equal(13, KmsSchema.LegacyRequiredTables.Length);

        using (SqliteConnection connection = Open(path, SqliteOpenMode.ReadOnly))
        {
            Assert.Empty(KmsSchema.MissingLegacyTables(connection));
        }

        Assert.Equal(48, data.History.Count);
        Assert.Equal(303401L, data.History.Values.Sum(day => day.Keys));
        Assert.Equal(167698L, data.History.Values.Sum(day => day.Clicks));
        Assert.Equal(11655, data.History.Values.Sum(day => day.Apps.Count));
        Assert.NotEmpty(data.History.Values.SelectMany(day => day.Sessions));
        Assert.Empty(data.Archives);
        Assert.Equal(30, data.AppRules.Count);

        Assert.Equal("1", data.Settings["meta:schema_rev"]);
        Assert.Equal("shadow", data.Settings["daily_origin:2026-11-22"]);
        Assert.Equal(303401L, data.Totals.Keys);
    }

    [Fact]
    public void Read_Epoch3995Fixture_DoesNotThrow()
    {
        StatsData data = SqliteStatsReader.Read(Fixture("v3-sqlite-epoch3995-before.db"));

        Assert.NotEmpty(data.History);
        Assert.Contains(
            data.History.Keys,
            static day => day.StartsWith("3995-", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WalDatabase_CopiesWithoutTouchingOriginal()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, KmsSchema.DbFileName);
            File.Copy(Fixture("v3-sqlite-normal.db"), path);

            using (SqliteConnection writer = Open(path, SqliteOpenMode.ReadWrite))
            {
                Execute(writer, "PRAGMA journal_mode = WAL;");
                Execute(writer, "PRAGMA wal_autocheckpoint = 0;");
                Execute(
                    writer,
                    "INSERT INTO daily_summary (day, keys, clicks, origin) " +
                    "VALUES ('2099-01-01', 7, 3, 'native');");

                string walPath = path + "-wal";
                Assert.True(File.Exists(walPath), "测试前置：WAL 旁文件应存在。");
                byte[] databaseBefore = HashFile(path);
                byte[] walBefore = HashFile(walPath);

                StatsData data = SqliteStatsReader.Read(path);

                Assert.Equal(49, data.History.Count);
                Assert.True(data.History.TryGetValue("2099-01-01", out DayRecord? added));
                Assert.Equal(7L, added.Keys);
                Assert.Equal(3L, added.Clicks);

                Assert.Equal(databaseBefore, HashFile(path));
                Assert.Equal(walBefore, HashFile(walPath));
                Assert.DoesNotContain(
                    Directory.GetFiles(directory),
                    file => Path.GetFileName(file).Contains(".read-", StringComparison.Ordinal));
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Read_MissingTable_ThrowsWithTableName()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, KmsSchema.DbFileName);
            using (SqliteConnection connection = Open(path, SqliteOpenMode.ReadWriteCreate))
            {
                KmsSchema.Ensure(connection, "test");
                Execute(connection, "DROP TABLE daily_key_agg;");
            }

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = SqliteStatsReader.Read(path);
            });

            Assert.Contains("daily_key_agg", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Ensure_IsIdempotent_AndWritesSchemaBaseline()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, KmsSchema.DbFileName);
            using (SqliteConnection connection = Open(path, SqliteOpenMode.ReadWriteCreate))
            {
                KmsSchema.Ensure(connection, "4.0.0-test");
                KmsSchema.Ensure(connection, "should-not-overwrite");

                Assert.Empty(KmsSchema.MissingTables(connection));
                Assert.Equal("3", ScalarText(connection, "SELECT v FROM meta WHERE k='schema_rev';"));
                Assert.Equal("4.0.0-test", ScalarText(connection, "SELECT v FROM meta WHERE k='app_version';"));
                Assert.Equal("", ScalarText(connection, "SELECT v FROM meta WHERE k='migrated_from_format';"));
                Assert.Equal("0", ScalarText(connection, "SELECT v FROM meta WHERE k='last_event_ts_ms';"));
                Assert.Equal("0", ScalarText(connection, "SELECT v FROM meta WHERE k='open_session_id';"));
                Assert.True(long.Parse(
                    ScalarText(connection, "SELECT v FROM meta WHERE k='db_created_ms';"),
                    CultureInfo.InvariantCulture) > 0);
                Assert.Equal(3L, ScalarLong(connection, "PRAGMA user_version;"));
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string Fixture(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")))
            {
                return Path.Combine(directory.FullName, "tests", "Fixtures", name);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录（KeyMouseStats.slnx）。");
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = mode,
            Pooling = false
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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

    private static byte[] HashFile(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "kms-storage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
