using System.Globalization;
using Kms.Core.Model;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Sqlite;

/// <summary>
/// 把文本主存档里的派生聚合同步到 SQLite 镜像。
///
/// <see cref="DbWriter"/> 只写事件级计数（keys/clicks/…），v3 的 13 表本身没有
/// 置信的实时 hold/active 写入路径；v4 以文本为主存档，这里把
/// 活跃时长、峰值 APM、按住时长等**绝对值**补进已有的 <c>daily_summary</c> 行。
///
/// 只 UPDATE、不 INSERT：历史日是否存在于镜像由迁移决定；
/// 不碰 keys/clicks 等增量列，避免与写线程重复计数。
/// </summary>
public static class DbAggregateSync
{
    /// <summary>返回成功更新的日行数；库文件不存在或没有行时返回 0。</summary>
    public static int Sync(string dbPath, StatsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!File.Exists(dbPath) || data.History.Count == 0)
        {
            return 0;
        }

        using SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());

        connection.Open();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=3000;";
        pragma.ExecuteNonQuery();

        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE daily_summary SET active_seconds=$active, idle_seconds=$idle, " +
            "app_observed_seconds=$observed, app_switches=$switches, peak_apm=$peak, " +
            "hold_count=$hold, hold_discarded=$discarded, hold_total_ms=$total, " +
            "hold_max_ms=$max, hold_active_seconds=$holdActive, move_meters=$moveMeters, " +
            "sessions=$sessions WHERE day=$day;";

        SqliteParameter day = command.Parameters.Add("$day", SqliteType.Text);
        SqliteParameter active = command.Parameters.Add("$active", SqliteType.Real);
        SqliteParameter idle = command.Parameters.Add("$idle", SqliteType.Real);
        SqliteParameter observed = command.Parameters.Add("$observed", SqliteType.Real);
        SqliteParameter switches = command.Parameters.Add("$switches", SqliteType.Integer);
        SqliteParameter peak = command.Parameters.Add("$peak", SqliteType.Integer);
        SqliteParameter hold = command.Parameters.Add("$hold", SqliteType.Integer);
        SqliteParameter discarded = command.Parameters.Add("$discarded", SqliteType.Integer);
        SqliteParameter total = command.Parameters.Add("$total", SqliteType.Real);
        SqliteParameter max = command.Parameters.Add("$max", SqliteType.Real);
        SqliteParameter holdActive = command.Parameters.Add("$holdActive", SqliteType.Real);
        SqliteParameter moveMeters = command.Parameters.Add("$moveMeters", SqliteType.Real);
        SqliteParameter sessions = command.Parameters.Add("$sessions", SqliteType.Integer);

        using SqliteCommand deleteHours = connection.CreateCommand();
        deleteHours.Transaction = transaction;
        deleteHours.CommandText = "DELETE FROM daily_hour_agg WHERE day=$day;";
        SqliteParameter deleteHoursDay = deleteHours.Parameters.Add("$day", SqliteType.Text);

        using SqliteCommand insertHour = connection.CreateCommand();
        insertHour.Transaction = transaction;
        insertHour.CommandText =
            "INSERT INTO daily_hour_agg (day, hour, keys, clicks, active_seconds) " +
            "VALUES ($day,$hour,$keys,$clicks,$active);";
        SqliteParameter hourDay = insertHour.Parameters.Add("$day", SqliteType.Text);
        SqliteParameter hour = insertHour.Parameters.Add("$hour", SqliteType.Integer);
        SqliteParameter hourKeys = insertHour.Parameters.Add("$keys", SqliteType.Integer);
        SqliteParameter hourClicks = insertHour.Parameters.Add("$clicks", SqliteType.Integer);
        SqliteParameter hourActive = insertHour.Parameters.Add("$active", SqliteType.Real);

        using SqliteCommand deleteKeys = connection.CreateCommand();
        deleteKeys.Transaction = transaction;
        deleteKeys.CommandText = "DELETE FROM daily_key_agg WHERE day=$day;";
        SqliteParameter deleteKeysDay = deleteKeys.Parameters.Add("$day", SqliteType.Text);

        using SqliteCommand insertKey = connection.CreateCommand();
        insertKey.Transaction = transaction;
        insertKey.CommandText = "INSERT INTO daily_key_agg (day, vk, cnt) VALUES ($day,$vk,$cnt);";
        SqliteParameter keyDay = insertKey.Parameters.Add("$day", SqliteType.Text);
        SqliteParameter keyVk = insertKey.Parameters.Add("$vk", SqliteType.Integer);
        SqliteParameter keyCount = insertKey.Parameters.Add("$cnt", SqliteType.Integer);

        using SqliteCommand updateApp = connection.CreateCommand();
        updateApp.Transaction = transaction;
        updateApp.CommandText =
            "UPDATE window_daily_agg SET keys=$keys, clicks=$clicks, wheel=$wheel, " +
            "active_seconds=$active, key_g0=$g0, key_g1=$g1, key_g2=$g2, " +
            "key_g3=$g3, key_g4=$g4, key_g5=$g5 " +
            "WHERE day=$day AND process_key=$process AND window_title=$title;";
        SqliteParameter appDay = updateApp.Parameters.Add("$day", SqliteType.Text);
        SqliteParameter appProcess = updateApp.Parameters.Add("$process", SqliteType.Text);
        SqliteParameter appTitle = updateApp.Parameters.Add("$title", SqliteType.Text);
        SqliteParameter appKeys = updateApp.Parameters.Add("$keys", SqliteType.Integer);
        SqliteParameter appClicks = updateApp.Parameters.Add("$clicks", SqliteType.Integer);
        SqliteParameter appWheel = updateApp.Parameters.Add("$wheel", SqliteType.Integer);
        SqliteParameter appActive = updateApp.Parameters.Add("$active", SqliteType.Real);
        SqliteParameter[] appGroups =
        [
            updateApp.Parameters.Add("$g0", SqliteType.Integer),
            updateApp.Parameters.Add("$g1", SqliteType.Integer),
            updateApp.Parameters.Add("$g2", SqliteType.Integer),
            updateApp.Parameters.Add("$g3", SqliteType.Integer),
            updateApp.Parameters.Add("$g4", SqliteType.Integer),
            updateApp.Parameters.Add("$g5", SqliteType.Integer)
        ];

        int updated = 0;
        foreach ((string key, DayRecord record) in data.History)
        {
            day.Value = key;
            active.Value = record.ActiveSeconds;
            idle.Value = record.IdleSeconds;
            observed.Value = record.AppObservedSeconds;
            switches.Value = record.AppSwitches;
            peak.Value = record.PeakApm;
            hold.Value = record.HoldCount;
            discarded.Value = record.HoldDiscarded;
            total.Value = record.HoldTotalMs;
            max.Value = record.HoldMaxMs;
            holdActive.Value = record.HoldActiveSeconds;
            moveMeters.Value = record.MoveMeters;
            sessions.Value = record.Sessions.Count;
            updated += command.ExecuteNonQuery();

            SyncHours(record, key, deleteHours, deleteHoursDay, insertHour,
                hourDay, hour, hourKeys, hourClicks, hourActive);
            SyncKeys(record, key, deleteKeys, deleteKeysDay, insertKey, keyDay, keyVk, keyCount);
            SyncApps(record, key, updateApp, appDay, appProcess, appTitle,
                appKeys, appClicks, appWheel, appActive, appGroups);
        }

        transaction.Commit();
        return updated;
    }

    private static void SyncHours(
        DayRecord record,
        string day,
        SqliteCommand delete,
        SqliteParameter deleteDay,
        SqliteCommand insert,
        SqliteParameter hourDay,
        SqliteParameter hour,
        SqliteParameter keys,
        SqliteParameter clicks,
        SqliteParameter active)
    {
        deleteDay.Value = day;
        delete.ExecuteNonQuery();
        for (int h = 0; h < 24; h++)
        {
            long keyCount = record.HourKeys[h];
            long clickCount = record.HourClicks[h];
            double activeSeconds = record.ActiveHours[h];
            if (keyCount == 0 && clickCount == 0 && activeSeconds <= 0)
            {
                continue;
            }

            hourDay.Value = day;
            hour.Value = h;
            keys.Value = keyCount;
            clicks.Value = clickCount;
            active.Value = activeSeconds;
            insert.ExecuteNonQuery();
        }
    }

    private static void SyncKeys(
        DayRecord record,
        string day,
        SqliteCommand delete,
        SqliteParameter deleteDay,
        SqliteCommand insert,
        SqliteParameter keyDay,
        SqliteParameter keyVk,
        SqliteParameter keyCount)
    {
        deleteDay.Value = day;
        delete.ExecuteNonQuery();
        foreach ((int vk, long count) in record.KeyCounts)
        {
            if (count <= 0)
            {
                continue;
            }

            keyDay.Value = day;
            keyVk.Value = vk;
            keyCount.Value = count;
            insert.ExecuteNonQuery();
        }
    }

    private static void SyncApps(
        DayRecord record,
        string day,
        SqliteCommand update,
        SqliteParameter appDay,
        SqliteParameter process,
        SqliteParameter title,
        SqliteParameter keys,
        SqliteParameter clicks,
        SqliteParameter wheel,
        SqliteParameter active,
        SqliteParameter[] groups)
    {
        foreach (AppUsage app in record.Apps.Values)
        {
            appDay.Value = day;
            process.Value = app.ProcessPath.ToLowerInvariant();
            title.Value = app.Title;
            keys.Value = app.Keys;
            clicks.Value = app.Clicks;
            wheel.Value = app.Wheel;
            active.Value = app.ActiveSeconds;
            for (int i = 0; i < 6; i++)
            {
                groups[i].Value = app.KeyGroups[i];
            }

            update.ExecuteNonQuery();
        }
    }

    /// <summary>诊断文本：同步失败时写入 save-error.log。</summary>
    public static string Describe(Exception error) =>
        error.GetType().Name + " · " + error.Message + " · " +
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
