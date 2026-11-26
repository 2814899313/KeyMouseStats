using Kms.Storage.Migration;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

public sealed class Epoch3995FixTests
{
    [Fact]
    public void Apply_ShiftsDirtyTimestampsAndIsIdempotent()
    {
        string root = FindRepoRoot();
        string directory = Path.Combine(root, ".tmp-epoch-tests");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".db");

        try
        {
            long dirty = 63_000_000_000_000;
            CreateDatabase(path, dirty);

            using (SqliteConnection connection = new($"Data Source={path}"))
            {
                connection.Open();
                Assert.True(Epoch3995Fix.Apply(connection, path));
                Assert.Equal("1", Scalar(connection, "SELECT v FROM meta WHERE k='epoch_fix_v1'"));
                Assert.True(Convert.ToInt64(Scalar(connection, "SELECT start_ts_ms FROM events_window_session")) < Epoch3995Fix.ThresholdMs);
                Assert.True(Convert.ToInt64(Scalar(connection, "SELECT v FROM meta WHERE k='last_event_ts_ms'")) < Epoch3995Fix.ThresholdMs);

                Assert.False(Epoch3995Fix.Apply(connection, path));
            }

            Assert.True(File.Exists(path + ".bak-3995-fix"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string file in Directory.EnumerateFiles(directory, Path.GetFileName(path) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    private static void CreateDatabase(string path, long dirty)
    {
        using SqliteConnection connection = new($"Data Source={path}");
        connection.Open();

        string[] statements =
        [
            "CREATE TABLE meta (k TEXT PRIMARY KEY, v TEXT NOT NULL);",
            "CREATE TABLE events_window_session (id INTEGER PRIMARY KEY, start_ts_ms INTEGER NOT NULL, end_ts_ms INTEGER NOT NULL, day TEXT NOT NULL);",
            "CREATE TABLE events_input_burst (id INTEGER PRIMARY KEY, start_ts_ms INTEGER NOT NULL, end_ts_ms INTEGER NOT NULL, day TEXT NOT NULL);",
            "CREATE TABLE kms_import_log (imported_ms INTEGER NOT NULL);",
            "CREATE TABLE migration_log (imported_ms INTEGER NOT NULL);",
            "CREATE TABLE app_category_rule (updated_ms INTEGER NOT NULL);",
            "CREATE TABLE window_daily_agg (day TEXT NOT NULL, first_ts_ms INTEGER NOT NULL, last_ts_ms INTEGER NOT NULL);",
            "CREATE TABLE daily_summary (day TEXT NOT NULL);",
            "CREATE TABLE daily_hour_agg (day TEXT NOT NULL);",
            "CREATE TABLE daily_key_agg (day TEXT NOT NULL);",
            "CREATE TABLE daily_click_grid (day TEXT NOT NULL);",
            $"INSERT INTO meta (k, v) VALUES ('last_event_ts_ms', '{dirty}');",
            $"INSERT INTO events_window_session VALUES (1, {dirty}, {dirty}, '3995-01-01');",
            $"INSERT INTO events_input_burst VALUES (1, {dirty}, {dirty}, '3995-01-01');",
            $"INSERT INTO kms_import_log VALUES ({dirty});",
            $"INSERT INTO migration_log VALUES ({dirty});",
            $"INSERT INTO app_category_rule VALUES ({dirty});",
            $"INSERT INTO window_daily_agg VALUES ('3995-01-01', {dirty}, {dirty});",
            "INSERT INTO daily_summary VALUES ('3995-01-01');"
        ];

        foreach (string sql in statements)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static string FindRepoRoot()
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
