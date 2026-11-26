using Kms.Core.Time;
using Kms.Storage.Package;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Sqlite;

/// <summary>
/// <c>.kms</c> 增量包导入：<c>kms_import_log</c> 是唯一仲裁者。
/// 同一包重复导入时，账本主键命中，第二次影响行数必须为 0。
/// </summary>
public static class KmsPackageImporter
{
    public static KmsMergeResult Import(string databasePath, string packageId, KmsPackageData package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("数据库路径为空。", nameof(databasePath));
        }

        string id = string.IsNullOrEmpty(packageId) ? "unknown" : packageId;
        long now = UnixTime.NowMs(SystemClock.Instance);
        KmsMergeResult result = new();

        using SqliteConnection connection = new($"Data Source={Path.GetFullPath(databasePath)}");
        connection.Open();
        ApplyPragmas(connection);
        Migration.Epoch3995Fix.Apply(connection, Path.GetFullPath(databasePath));
        KmsSchema.Ensure(connection, "4.0.0");

        using SqliteTransaction transaction = connection.BeginTransaction();

        foreach (KmsDayRecord day in package.Days)
        {
            int applied = Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO kms_import_log (package_id, record_key, content_hash, imported_ms) " +
                "VALUES ($id, $key, $hash, $now);",
                ("$id", id),
                ("$key", day.RecordKey),
                ("$hash", day.ContentHash()),
                ("$now", now));

            if (applied == 1)
            {
                Execute(
                    connection,
                    transaction,
                    "INSERT OR IGNORE INTO daily_summary (day, keys, clicks, wheel, active_seconds) " +
                    "VALUES ($day, $keys, $clicks, $wheel, $active);",
                    ("$day", day.Day),
                    ("$keys", day.Keys),
                    ("$clicks", day.Clicks),
                    ("$wheel", day.Wheel),
                    ("$active", (double)day.ActiveSeconds));
                result.DaysApplied++;
            }
            else
            {
                result.DaysSkipped++;
            }
        }

        foreach (KmsSessionRecord session in package.Sessions)
        {
            int applied = Execute(
                connection,
                transaction,
                "INSERT OR IGNORE INTO kms_import_log (package_id, record_key, content_hash, imported_ms) " +
                "VALUES ($id, $key, $hash, $now);",
                ("$id", id),
                ("$key", session.RecordKey),
                ("$hash", session.ContentHash()),
                ("$now", now));

            if (applied == 1)
            {
                Execute(
                    connection,
                    transaction,
                    "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, process_path, " +
                    "process_name, window_title, key_count, category_id, category_source, is_open) " +
                    "VALUES ($start, $end, $day, $path, $name, $title, $keys, $category, 'none', 0);",
                    ("$start", session.StartMs),
                    ("$end", session.EndMs),
                    ("$day", session.Day),
                    ("$path", session.ProcessPath ?? ""),
                    ("$name", session.ProcessName ?? ""),
                    ("$title", session.WindowTitle ?? ""),
                    ("$keys", session.KeyCount),
                    ("$category", session.CategoryId));
                result.SessionsApplied++;
            }
            else
            {
                result.SessionsSkipped++;
            }
        }

        transaction.Commit();
        return result;
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        foreach (string pragma in new[]
                 {
                     "PRAGMA journal_mode=WAL;",
                     "PRAGMA synchronous=FULL;",
                     "PRAGMA foreign_keys=ON;",
                     "PRAGMA busy_timeout=5000;"
                 })
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = pragma;
            command.ExecuteNonQuery();
        }
    }

    private static int Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteNonQuery();
    }
}
