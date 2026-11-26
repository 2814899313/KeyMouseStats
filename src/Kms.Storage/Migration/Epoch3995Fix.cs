using Kms.Core.Time;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Migration;

/// <summary>
/// 3995 年时间基准缺陷的一次性抢救（移植自 v3 <c>KmsSchema.MigrateEpoch3995IfNeeded</c>）。
/// 幂等：成功写 <c>meta.epoch_fix_v1=1</c> 后不再执行；失败只回滚、不写标记。
/// </summary>
public static class Epoch3995Fix
{
    public const string Marker = "epoch_fix_v1";

    /// <summary>正常 Unix 毫秒约 1.7e12，3995 年数据约 6.39e13。</summary>
    public const long ThresholdMs = 60_000_000_000_000L;

    /// <summary>返回是否执行了修复。</summary>
    public static bool Apply(SqliteConnection connection, string databasePath)
    {
        if (!TableExists(connection, "meta"))
        {
            return false;
        }

        if (!TableExists(connection, "events_window_session") &&
            !TableExists(connection, "events_input_burst"))
        {
            return false;
        }

        if (string.Equals(ScalarText(connection, $"SELECT v FROM meta WHERE k='{Marker}'"), "1", StringComparison.Ordinal))
        {
            return false;
        }

        if (!HasDirtyData(connection))
        {
            // 没有异常也落一次标记，后续启动跳过全表探测。
            Execute(connection, $"INSERT OR IGNORE INTO meta (k, v) VALUES ('{Marker}', '1');");
            return false;
        }

        Backup(databasePath);

        using SqliteTransaction transaction = connection.BeginTransaction();
        FixEventTable(connection, transaction, "events_window_session");
        FixEventTable(connection, transaction, "events_input_burst");

        ShiftMetaValue(connection, transaction, "last_event_ts_ms");
        ShiftMetaValue(connection, transaction, "db_created_ms");
        ShiftMetaValue(connection, transaction, "imported_ms");

        ShiftColumn(connection, transaction, "kms_import_log", "imported_ms");
        ShiftColumn(connection, transaction, "migration_log", "imported_ms");
        ShiftColumn(connection, transaction, "app_category_rule", "updated_ms");
        ShiftColumn(connection, transaction, "window_daily_agg", "first_ts_ms");
        ShiftColumn(connection, transaction, "window_daily_agg", "last_ts_ms");

        DeleteDirtyDayRows(connection, transaction, "daily_summary");
        DeleteDirtyDayRows(connection, transaction, "daily_hour_agg");
        DeleteDirtyDayRows(connection, transaction, "daily_key_agg");
        DeleteDirtyDayRows(connection, transaction, "window_daily_agg");
        DeleteDirtyDayRows(connection, transaction, "daily_click_grid");

        Execute(
            connection,
            transaction,
            $"INSERT OR REPLACE INTO meta (k, v) VALUES ('{Marker}', '1');");

        transaction.Commit();
        return true;
    }

    internal static bool HasDirtyData(SqliteConnection connection)
    {
        foreach (string table in new[] { "events_window_session", "events_input_burst" })
        {
            if (!TableExists(connection, table))
            {
                continue;
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM {table} WHERE day >= '3900-01-01' " +
                $"OR start_ts_ms >= {ThresholdMs} OR end_ts_ms >= {ThresholdMs} LIMIT 1;";
            if (Convert.ToInt64(command.ExecuteScalar()) > 0)
            {
                return true;
            }
        }

        if (TableExists(connection, "meta"))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM meta WHERE k IN ('last_event_ts_ms','db_created_ms','imported_ms') " +
                $"AND CAST(v AS INTEGER) >= {ThresholdMs} LIMIT 1;";
            if (Convert.ToInt64(command.ExecuteScalar()) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void Backup(string databasePath)
    {
        if (string.IsNullOrEmpty(databasePath) || !File.Exists(databasePath))
        {
            return;
        }

        string backup = databasePath + ".bak-3995-fix";
        if (File.Exists(backup))
        {
            return;
        }

        CopyIfExists(databasePath + "-wal", databasePath + "-wal.bak-3995-fix");
        CopyIfExists(databasePath + "-shm", databasePath + "-shm.bak-3995-fix");
        File.Copy(databasePath, backup, overwrite: false);
    }

    private static void CopyIfExists(string source, string target)
    {
        if (File.Exists(source) && !File.Exists(target))
        {
            File.Copy(source, target, overwrite: false);
        }
    }

    private static void FixEventTable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table)
    {
        if (!TableExists(connection, table))
        {
            return;
        }

        Execute(connection, transaction,
            $"UPDATE {table} SET day = strftime('%Y-%m-%d', " +
            $"(CASE WHEN start_ts_ms >= {ThresholdMs} THEN start_ts_ms - {UnixTime.YearOneToUnixMs} " +
            $"ELSE start_ts_ms END) / 1000, 'unixepoch', 'localtime') " +
            $"WHERE day >= '3900-01-01' OR start_ts_ms >= {ThresholdMs};");

        Execute(connection, transaction,
            $"UPDATE {table} SET " +
            $"start_ts_ms = CASE WHEN start_ts_ms >= {ThresholdMs} THEN start_ts_ms - {UnixTime.YearOneToUnixMs} ELSE start_ts_ms END, " +
            $"end_ts_ms = CASE WHEN end_ts_ms >= {ThresholdMs} THEN end_ts_ms - {UnixTime.YearOneToUnixMs} ELSE end_ts_ms END " +
            $"WHERE start_ts_ms >= {ThresholdMs} OR end_ts_ms >= {ThresholdMs};");

        Execute(connection, transaction,
            $"UPDATE {table} SET end_ts_ms = start_ts_ms WHERE end_ts_ms < start_ts_ms;");
    }

    private static void ShiftMetaValue(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key)
    {
        if (!TableExists(connection, "meta"))
        {
            return;
        }

        Execute(connection, transaction,
            $"UPDATE meta SET v = CAST(CAST(v AS INTEGER) - {UnixTime.YearOneToUnixMs} AS TEXT) " +
            $"WHERE k = '{key}' AND CAST(v AS INTEGER) >= {ThresholdMs};");
    }

    private static void ShiftColumn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column)
    {
        if (!TableExists(connection, table))
        {
            return;
        }

        Execute(connection, transaction,
            $"UPDATE {table} SET {column} = {column} - {UnixTime.YearOneToUnixMs} " +
            $"WHERE {column} >= {ThresholdMs};");
    }

    private static void DeleteDirtyDayRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table)
    {
        if (TableExists(connection, table))
        {
            Execute(connection, transaction, $"DELETE FROM {table} WHERE day >= '3900-01-01';");
        }
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static string? ScalarText(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
