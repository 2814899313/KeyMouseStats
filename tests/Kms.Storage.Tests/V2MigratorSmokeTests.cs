using Kms.Storage.Migration;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

public sealed class V2MigratorSmokeTests
{
    [Fact]
    public void Run_ImportsMergedTextAndIsIdempotent()
    {
        string root = FindRepoRoot();
        string sandbox = Path.Combine(root, ".tmp-migrator-smoke");
        string source = Path.Combine(sandbox, "source");
        string dbRoot = Path.Combine(sandbox, "db");
        Directory.CreateDirectory(Path.Combine(source, "2026"));
        Directory.CreateDirectory(dbRoot);

        try
        {
            File.Copy(
                Path.Combine(root, "tests", "Fixtures", "v13-text-current.txt"),
                Path.Combine(source, "stats.txt"));
            File.Copy(
                Path.Combine(root, "tests", "Fixtures", "v13-month-2026-09.txt"),
                Path.Combine(source, "2026", "2026-09.txt"));
            File.Copy(
                Path.Combine(root, "tests", "Fixtures", "v13-month-2026-10.txt"),
                Path.Combine(source, "2026", "2026-10.txt"));

            MigrationOutcome first = V2Migrator.Run(source, dbRoot);
            Assert.True(first.Performed, first.Describe());
            Assert.Equal(48, first.DaysImported);
            Assert.Equal(30, first.RulesImported);
            Assert.True(first.GridCellsImported > 0);

            string dbPath = Path.Combine(dbRoot, "kmstats.db");
            using (SqliteConnection connection = new($"Data Source={dbPath};Mode=ReadOnly"))
            {
                connection.Open();
                Assert.Equal(48L, Scalar(connection, "SELECT COUNT(*) FROM daily_summary;"));
                Assert.Equal(321_890L, Scalar(connection, "SELECT SUM(keys) FROM daily_summary;"));
                Assert.Equal(30L, Scalar(connection, "SELECT COUNT(*) FROM app_category_rule;"));
                Assert.Equal(48L, Scalar(connection, "SELECT COUNT(*) FROM daily_summary WHERE origin='migrated';"));
                Assert.Equal("1", Text(connection, "SELECT v FROM meta WHERE k='epoch_fix_v1';"));
            }

            MigrationOutcome second = V2Migrator.Run(source, dbRoot);
            Assert.True(second.Skipped, second.Describe());
            Assert.Equal("已迁移(format=13)", second.Reason);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(sandbox);
        }
    }

    [Fact]
    public void Run_BadArchiveFailsWithoutTouchingSource()
    {
        string root = FindRepoRoot();
        string sandbox = Path.Combine(root, ".tmp-migrator-bad");
        string source = Path.Combine(sandbox, "source");
        string dbRoot = Path.Combine(sandbox, "db");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dbRoot);

        try
        {
            string statsPath = Path.Combine(source, "stats.txt");
            const string bad = """
                format=13
                [day=2026-13-45]
                keys=123
                """;
            File.WriteAllText(statsPath, bad);

            MigrationOutcome outcome = V2Migrator.Run(source, dbRoot);

            Assert.True(outcome.Failed, outcome.Describe());
            Assert.Equal(bad, File.ReadAllText(statsPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(sandbox);
        }
    }

    [Fact]
    public void SyncDerivedSnapshotIfNewer_AddsOnlyMissingDays()
    {
        string root = FindRepoRoot();
        string sandbox = Path.Combine(root, ".tmp-snapshot-bridge");
        string source = Path.Combine(sandbox, "source");
        string dbRoot = Path.Combine(sandbox, "db");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dbRoot);

        try
        {
            string dbPath = Path.Combine(dbRoot, "kmstats.db");
            using (SqliteConnection connection = new($"Data Source={dbPath}"))
            {
                connection.Open();
                KmsSchema.Ensure(connection, "4.0.0");
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "UPDATE meta SET v='0' WHERE k='last_export_ms';";
                command.ExecuteNonQuery();
            }

            File.WriteAllText(
                Path.Combine(source, "stats.txt"),
                "format=13\n[day=2024-01-01]\nkeys=10\nclicks=2\nleft=2\n");

            int added = V2Migrator.SyncDerivedSnapshotIfNewer(source, dbRoot, out string reason);
            Assert.Equal(1, added);
            Assert.Contains("补入 1 天", reason);

            using (SqliteConnection connection = new($"Data Source={dbPath};Mode=ReadOnly"))
            {
                connection.Open();
                Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM daily_summary WHERE origin='snapshot-bridge';"));
                Assert.Equal(10L, Scalar(connection, "SELECT keys FROM daily_summary WHERE day='2024-01-01';"));
            }

            Assert.Equal(0, V2Migrator.SyncDerivedSnapshotIfNewer(source, dbRoot, out reason));
            Assert.Equal("文本快照未变更", reason);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(sandbox);
        }
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string? Text(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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
