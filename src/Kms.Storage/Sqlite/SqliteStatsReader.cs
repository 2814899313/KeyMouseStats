using System.Globalization;
using Kms.Core.Model;
using Kms.Core.Time;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Sqlite;

/// <summary>
/// 把 v3 <c>schema_rev = 1</c> 的 SQLite 库读成 v4 的 <see cref="StatsData"/>。
///
/// 硬约束：
/// <list type="bullet">
/// <item>只读打开；WAL 库先连同 <c>-wal</c> 复制到同目录临时文件，原库与旁文件一律不改。</item>
/// <item><c>daily_summary</c> 是“哪些天存在”的唯一真源；其余按日表只补充已存在的日记录，
///       无法归日的行跳过，不凭空造 <see cref="DayRecord"/>。</item>
/// <item><c>window_daily_agg</c> 是应用维度的主来源；<c>events_window_session</c> 只填
///       <see cref="DayRecord.Sessions"/>，不重复累加键鼠计数。</item>
/// <item>缺表抛带表名的 <see cref="InvalidOperationException"/>；坏日期/坏时间戳抛
///       <see cref="InvalidDataException"/>，绝不静默修复。</item>
/// </list>
/// </summary>
public static class SqliteStatsReader
{
    private static readonly string[] ReadSuffixes = ["", "-wal", "-shm", "-journal"];

    /// <summary>只读加载指定路径的 v3 SQLite 库。</summary>
    /// <param name="path">库文件路径。不会被创建、写入或就地修改。</param>
    public static StatsData Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("数据库路径为空。", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("SQLite 数据库不存在。", fullPath);
        }

        string? temporary = null;
        try
        {
            string readPath = fullPath;
            if (NeedsWalCopy(fullPath))
            {
                temporary = CopyDatabaseFamily(fullPath);
                readPath = temporary;
            }

            using SqliteConnection connection = OpenReadOnly(readPath);
            return Load(connection);
        }
        finally
        {
            if (temporary is not null)
            {
                DeleteFamily(temporary);
            }
        }
    }

    // ---------------------------------------------------------------- 打开

    /// <summary>
    /// WAL 库以只读方式打开时 SQLite 需要创建 <c>-shm</c>，在无写权限时会直接报
    /// “unable to open database file”。因此先看文件头（或 <c>-wal</c> 旁文件）判断，
    /// 需要时复制到同目录临时库再读。
    /// </summary>
    private static bool NeedsWalCopy(string path)
    {
        if (File.Exists(path + "-wal"))
        {
            return true;
        }

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[20];
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException)
        {
            return false;
        }

        // 文件头 18/19 字节是 write/read format version；2 = WAL。
        return header[..16].SequenceEqual("SQLite format 3\0"u8) && (header[18] == 2 || header[19] == 2);
    }

    private static string CopyDatabaseFamily(string source)
    {
        string directory = Path.GetDirectoryName(source)
            ?? throw new InvalidOperationException("无法确定数据库所在目录：" + source);
        string temporary = Path.Combine(
            directory,
            "." + Path.GetFileName(source) + ".read-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            File.Copy(source, temporary, false);
            string wal = source + "-wal";
            if (File.Exists(wal))
            {
                File.Copy(wal, temporary + "-wal", false);
            }

            return temporary;
        }
        catch
        {
            DeleteFamily(temporary);
            throw;
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        };

        SqliteConnection connection = new(builder.ToString());
        try
        {
            connection.Open();
            ApplyReadPragmas(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>只读连接也需要 busy_timeout 与内存临时表（与 v3 读连接口径一致）。</summary>
    private static void ApplyReadPragmas(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA temp_store = MEMORY;";
        command.ExecuteNonQuery();
    }

    private static void DeleteFamily(string path)
    {
        foreach (string suffix in ReadSuffixes)
        {
            string candidate = path + suffix;
            try
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
            catch (IOException)
            {
                // 临时文件清理失败不应掩盖读取结果；下次系统清理会回收。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // ---------------------------------------------------------------- 装载

    private static StatsData Load(SqliteConnection connection)
    {
        string[] missing = KmsSchema.MissingTables(connection);
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "SQLite 数据库缺少必需表：" + string.Join(", ", missing));
        }

        StatsData data = new();

        ReadMeta(connection, data);
        ReadDailySummary(connection, data);
        ReadDailyHour(connection, data);
        ReadDailyKey(connection, data);
        ReadClickGrid(connection, data);
        ReadWindowDaily(connection, data);
        ReadMonthArchive(connection, data);
        ReadMonthArchiveApp(connection, data);
        ReadAppRules(connection, data);
        ReadSessions(connection, data);
        ReadLogs(connection, data);
        BuildTotals(data);

        return data;
    }

    private static void ReadMeta(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT k, v FROM meta ORDER BY rowid;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            data.Settings["meta:" + ReadText(reader, 0)] = ReadText(reader, 1);
        }
    }

    private static void ReadDailySummary(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT day, keys, clicks, left, right, middle, xbuttons, wheel, move_px, move_meters, " +
            "active_seconds, idle_seconds, app_observed_seconds, app_switches, peak_apm, " +
            "hold_count, hold_discarded, hold_total_ms, hold_max_ms, hold_active_seconds, origin " +
            "FROM daily_summary ORDER BY day;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string day = ReadText(reader, 0);
            DateTime date = ParseDay("daily_summary", day);
            DayRecord record = new()
            {
                Date = date,
                Keys = ReadInt64(reader, 1),
                Clicks = ReadInt64(reader, 2),
                Left = ReadInt64(reader, 3),
                Right = ReadInt64(reader, 4),
                Middle = ReadInt64(reader, 5),
                XButtons = ReadInt64(reader, 6),
                Wheel = ReadInt64(reader, 7),
                MovePx = ReadDouble(reader, 8),
                MoveMeters = ReadDouble(reader, 9),
                ActiveSeconds = ReadDouble(reader, 10),
                IdleSeconds = ReadDouble(reader, 11),
                AppObservedSeconds = ReadDouble(reader, 12),
                AppSwitches = ReadInt64(reader, 13),
                PeakApm = ReadInt64(reader, 14),
                HoldCount = ReadInt64(reader, 15),
                HoldDiscarded = ReadInt64(reader, 16),
                HoldTotalMs = ReadDouble(reader, 17),
                HoldMaxMs = ReadDouble(reader, 18),
                HoldActiveSeconds = ReadDouble(reader, 19)
            };

            // daily_summary.sessions 在 v3 写线程从不更新；模型也没有对应字段，直接忽略。
            data.History[day] = record;

            // 仅供差分对拍，不参与业务。
            data.Settings["daily_origin:" + day] = ReadText(reader, 20);
        }
    }

    private static void ReadDailyHour(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT day, hour, keys, clicks, active_seconds FROM daily_hour_agg ORDER BY day, hour;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string day = ReadText(reader, 0);
            if (!data.History.TryGetValue(day, out DayRecord? record))
            {
                continue;
            }

            int hour = ToInt32(ReadInt64(reader, 1), "daily_hour_agg", "hour");
            if (hour is < 0 or > 23)
            {
                throw new InvalidDataException("daily_hour_agg.hour 超出 0..23：" + hour + "（day=" + day + "）");
            }

            record.HourKeys[hour] += ReadInt64(reader, 2);
            record.HourClicks[hour] += ReadInt64(reader, 3);
            record.ActiveHours[hour] += ReadDouble(reader, 4);
        }
    }

    private static void ReadDailyKey(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT day, vk, cnt FROM daily_key_agg ORDER BY day, vk;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string day = ReadText(reader, 0);
            if (!data.History.TryGetValue(day, out DayRecord? record))
            {
                continue;
            }

            int vk = ToInt32(ReadInt64(reader, 1), "daily_key_agg", "vk");
            record.KeyCounts[vk] = record.KeyCounts.GetValueOrDefault(vk) + ReadInt64(reader, 2);
        }
    }

    private static void ReadClickGrid(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT day, channel, cell_x, cell_y, count FROM daily_click_grid " +
            "ORDER BY day, channel, cell_x, cell_y;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string day = ReadText(reader, 0);
            if (!data.History.TryGetValue(day, out DayRecord? record))
            {
                continue;
            }

            int channel = ToInt32(ReadInt64(reader, 1), "daily_click_grid", "channel");
            int cellX = ToInt32(ReadInt64(reader, 2), "daily_click_grid", "cell_x");
            int cellY = ToInt32(ReadInt64(reader, 3), "daily_click_grid", "cell_y");
            long count = ReadInt64(reader, 4);
            int cellKey = CellKey(cellX, cellY);

            // ClickCells 与旧版逐格视图一致：全通道汇总。
            record.ClickCells[cellKey] = record.ClickCells.GetValueOrDefault(cellKey) + count;

            // v3 三通道键：高 32 位通道，低 32 位 CellKey。
            long channelKey = ((long)channel << 32) | (uint)cellKey;
            record.ClickCellsV1[channelKey] = record.ClickCellsV1.GetValueOrDefault(channelKey) + count;
        }
    }

    private static void ReadWindowDaily(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT day, window_title, process_path, keys, clicks, wheel, active_seconds, " +
            "key_g0, key_g1, key_g2, key_g3, key_g4, key_g5 " +
            "FROM window_daily_agg ORDER BY day, process_key, window_title;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string day = ReadText(reader, 0);
            if (!data.History.TryGetValue(day, out DayRecord? record))
            {
                continue;
            }

            string title = ReadText(reader, 1);
            string processPath = ReadText(reader, 2);
            string key = processPath.ToLowerInvariant() + "\n" + title;

            if (!record.Apps.TryGetValue(key, out AppUsage? app))
            {
                app = new AppUsage
                {
                    ProcessPath = processPath,
                    Title = title,
                    HasKeyGroups = true
                };
                record.Apps[key] = app;
            }

            app.Keys += ReadInt64(reader, 3);
            app.Clicks += ReadInt64(reader, 4);
            app.Wheel += ReadInt64(reader, 5);
            app.ActiveSeconds += ReadDouble(reader, 6);
            app.HasKeyGroups = true;
            for (int group = 0; group < 6; group++)
            {
                app.KeyGroups[group] += ReadInt64(reader, 7 + group);
            }
        }
    }

    private static void ReadMonthArchive(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT ym, days, keys, clicks, wheel, combos, active_seconds, move_meters, sessions, " +
            "hold_count, hold_discarded, hold_total_ms, hold_max_ms, hold_active_seconds " +
            "FROM month_archive ORDER BY ym;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string ym = ReadText(reader, 0);
            DateTime month = ParseMonth("month_archive", ym);
            MonthArchive archive = new()
            {
                Year = month.Year,
                Month = month.Month,
                Days = ToInt32(ReadInt64(reader, 1), "month_archive", "days"),
                Keys = ReadInt64(reader, 2),
                Clicks = ReadInt64(reader, 3),
                Wheel = ReadInt64(reader, 4),
                Combos = ReadInt64(reader, 5),
                ActiveSeconds = ReadDouble(reader, 6),
                MoveMeters = ReadDouble(reader, 7),
                Sessions = ToInt32(ReadInt64(reader, 8), "month_archive", "sessions"),
                HoldCount = ReadInt64(reader, 9),
                HoldDiscarded = ReadInt64(reader, 10),
                HoldTotalMs = ReadDouble(reader, 11),
                HoldMaxMs = ReadDouble(reader, 12),
                HoldActiveSeconds = ReadDouble(reader, 13)
            };

            data.Archives[archive.Key] = archive;
        }
    }

    private static void ReadMonthArchiveApp(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT ym, process_path, keys FROM month_archive_app ORDER BY ym, process_path;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string ym = ReadText(reader, 0);
            if (!data.Archives.TryGetValue(ym, out MonthArchive? archive))
            {
                // 孤立的归档应用行无法归属，跳过而不是凭空造月档。
                continue;
            }

            string processPath = ReadText(reader, 1);
            archive.AppKeys[processPath] =
                archive.AppKeys.GetValueOrDefault(processPath) + ReadInt64(reader, 2);
        }
    }

    private static void ReadAppRules(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT rule_key, category_id FROM app_category_rule ORDER BY rowid;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            data.AppRules.Add(new AppRule(
                ReadText(reader, 0),
                ToInt32(ReadInt64(reader, 1), "app_category_rule", "category_id")));
        }
    }

    private static void ReadSessions(SqliteConnection connection, StatsData data)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, start_ts_ms, end_ts_ms, day, active_ms FROM events_window_session " +
            "ORDER BY day, start_ts_ms, id;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            long id = ReadInt64(reader, 0);
            string day = ReadText(reader, 3);
            if (!data.History.TryGetValue(day, out DayRecord? record))
            {
                // 无法归到 daily_summary 已声明的日期：跳过，避免历史天数被事件表撑大。
                continue;
            }

            long startMs = ReadInt64(reader, 1);
            long endMs = ReadInt64(reader, 2);
            long activeMs = ReadInt64(reader, 4);
            if (activeMs < 0)
            {
                throw new InvalidDataException(
                    "events_window_session.active_ms 为负（id=" + id.ToString(CultureInfo.InvariantCulture) + "）。");
            }

            record.Sessions.Add(new ActiveSession
            {
                Start = ToLocal(startMs, "events_window_session", id),
                End = ToLocal(endMs, "events_window_session", id),
                StartReason = SessionStartReason.Legacy,
                Seconds = activeMs / 1000.0
            });
        }
    }

    /// <summary>
    /// 迁移账本/导入账本没有对应的领域模型。行数少的 <c>migration_log</c> 逐行放进
    /// <see cref="StatsData.Settings"/>；可能很大的 <c>kms_import_log</c> 只记录行数。
    /// </summary>
    private static void ReadLogs(SqliteConnection connection, StatsData data)
    {
        int migrationRows = 0;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT source_kind, source_id, content_sha256, days_imported, imported_ms " +
                "FROM migration_log ORDER BY rowid;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = "migration_log:" + ReadText(reader, 0) + ":" + ReadText(reader, 1);
                data.Settings[key] =
                    ReadText(reader, 2) + "|" +
                    ReadInt64(reader, 3).ToString(CultureInfo.InvariantCulture) + "|" +
                    ReadInt64(reader, 4).ToString(CultureInfo.InvariantCulture);
                migrationRows++;
            }
        }

        data.Settings["migration_log.rows"] = migrationRows.ToString(CultureInfo.InvariantCulture);
        data.Settings["kms_import_log.rows"] = CountRows(connection, "kms_import_log")
            .ToString(CultureInfo.InvariantCulture);
    }

    private static long CountRows(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- 汇总

    /// <summary>与 v3 <c>RebuildTotalFromHistory</c> 一致：日记录 + 月归档。</summary>
    private static void BuildTotals(StatsData data)
    {
        StatsTotals totals = data.Totals;
        foreach (DayRecord day in data.History.Values)
        {
            totals.Keys += day.Keys;
            totals.Clicks += day.Clicks;
            totals.Left += day.Left;
            totals.Right += day.Right;
            totals.Middle += day.Middle;
            totals.XButtons += day.XButtons;
            totals.Wheel += day.Wheel;
            totals.HWheel += day.HWheel;
            totals.MovePx += day.MovePx;
            totals.MoveMeters += day.MoveMeters;
            totals.HoldCount += day.HoldCount;
            totals.HoldDiscarded += day.HoldDiscarded;
            totals.HoldTotalMs += day.HoldTotalMs;
            totals.HoldActiveSeconds += day.HoldActiveSeconds;
            totals.HoldMaxMs = Math.Max(totals.HoldMaxMs, day.HoldMaxMs);
        }

        foreach (MonthArchive archive in data.Archives.Values)
        {
            totals.Keys += archive.Keys;
            totals.Clicks += archive.Clicks;
            totals.Wheel += archive.Wheel;
            totals.HWheel += archive.HWheel;
            totals.MoveMeters += archive.MoveMeters;
            totals.HoldCount += archive.HoldCount;
            totals.HoldDiscarded += archive.HoldDiscarded;
            totals.HoldTotalMs += archive.HoldTotalMs;
            totals.HoldActiveSeconds += archive.HoldActiveSeconds;
            totals.HoldMaxMs = Math.Max(totals.HoldMaxMs, archive.HoldMaxMs);
        }
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>与 v3 <c>ClickHeat.CellKey</c> 完全一致：高 16 位 X、低 16 位 Y，各偏移 32768。</summary>
    private static int CellKey(int cellX, int cellY)
    {
        int x = Math.Max(-32768, Math.Min(32767, cellX)) + 32768;
        int y = Math.Max(-32768, Math.Min(32767, cellY)) + 32768;
        return (x << 16) | y;
    }

    private static DateTime ParseDay(string table, string value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime date))
        {
            throw new InvalidDataException(table + ".day 不是 yyyy-MM-dd：" + value);
        }

        return date;
    }

    private static DateTime ParseMonth(string table, string value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime month))
        {
            throw new InvalidDataException(table + ".ym 不是 yyyy-MM：" + value);
        }

        return month;
    }

    private static DateTime ToLocal(long unixMs, string table, long id)
    {
        try
        {
            return UnixTime.ToLocal(unixMs);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                table + " 的时间戳超出 DateTime 范围：id=" +
                id.ToString(CultureInfo.InvariantCulture) + "，ms=" +
                unixMs.ToString(CultureInfo.InvariantCulture),
                exception);
        }
    }

    private static int ToInt32(long value, string table, string column)
    {
        try
        {
            return checked((int)value);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                table + "." + column + " 超出 Int32 范围：" +
                value.ToString(CultureInfo.InvariantCulture),
                exception);
        }
    }

    private static string ReadText(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);

    private static long ReadInt64(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0L : reader.GetInt64(ordinal);

    private static double ReadDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0d : reader.GetDouble(ordinal);
}
