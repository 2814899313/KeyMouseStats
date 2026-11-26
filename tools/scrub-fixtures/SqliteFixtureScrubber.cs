using Kms.Core.Privacy;
using Microsoft.Data.Sqlite;

namespace Kms.Tools.ScrubFixtures;

/// <summary>
/// v3 SQLite 固件清洗器。
///
/// 关键约束（来自 KmsSchema / DbWriter 的实测）：
/// - <c>window_daily_agg</c> 与 <c>app_category_rule</c> 的隐私列是主键，逐行 UPDATE 会撞唯一约束；
///   这里用「重建表 + 冲突键加后缀」保持行数不变。
/// - <c>meta</c>、ID、sqlite_sequence、计数列、origin 四值一律不动。
/// - 用 Backup API 合并 WAL，最后 secure_delete + VACUUM，避免旧页残留。
/// </summary>
internal static class SqliteFixtureScrubber
{
    private static readonly (string Table, string Column)[] SumGuards =
    [
        ("daily_summary", "keys"),
        ("daily_summary", "clicks"),
        ("daily_summary", "left"),
        ("daily_summary", "right"),
        ("daily_summary", "middle"),
        ("daily_summary", "xbuttons"),
        ("daily_summary", "wheel"),
        ("daily_summary", "sessions"),
        ("daily_summary", "app_switches"),
        ("daily_summary", "peak_apm"),
        ("daily_summary", "hold_count"),
        ("daily_summary", "hold_discarded"),
        ("daily_summary", "active_seconds"),
        ("daily_summary", "idle_seconds"),
        ("daily_summary", "app_observed_seconds"),
        ("daily_summary", "move_px"),
        ("daily_summary", "move_meters"),
        ("daily_summary", "hold_total_ms"),
        ("daily_summary", "hold_max_ms"),
        ("daily_summary", "hold_active_seconds"),
        ("events_window_session", "key_count"),
        ("events_window_session", "left_clicks"),
        ("events_window_session", "right_clicks"),
        ("events_window_session", "middle_clicks"),
        ("events_window_session", "x_clicks"),
        ("events_window_session", "wheel"),
        ("events_window_session", "move_px"),
        ("events_window_session", "active_ms"),
        ("events_window_session", "rime_chars"),
        ("events_input_burst", "key_count"),
        ("events_input_burst", "backspace_cnt"),
        ("events_input_burst", "switch_count"),
        ("window_daily_agg", "keys"),
        ("window_daily_agg", "clicks"),
        ("window_daily_agg", "wheel"),
        ("window_daily_agg", "left"),
        ("window_daily_agg", "right"),
        ("window_daily_agg", "middle"),
        ("window_daily_agg", "xbuttons"),
        ("window_daily_agg", "active_seconds"),
        ("window_daily_agg", "key_g0"),
        ("window_daily_agg", "key_g1"),
        ("window_daily_agg", "key_g2"),
        ("window_daily_agg", "key_g3"),
        ("window_daily_agg", "key_g4"),
        ("window_daily_agg", "key_g5"),
        ("daily_click_grid", "count"),
        ("daily_hour_agg", "keys"),
        ("daily_hour_agg", "clicks"),
        ("daily_hour_agg", "active_seconds"),
        ("daily_key_agg", "cnt")
    ];

    public static void Scrub(string source, string destination)
    {
        PrepareDestination(destination);
        SqliteConnection.ClearAllPools();

        // WAL 库以只读方式打开时，SQLite 需要创建 -shm；Mode=ReadOnly 会直接报
        // "unable to open database file"。因此先复制到目标目录的同名临时库，
        // 用可写连接做 WAL 恢复 + Backup，再删除临时文件，原样本保持只读。
        string workDatabase = destination + ".source-work.db";
        try
        {
            CopyDatabaseFamily(source, workDatabase);

            try
            {
                using SqliteConnection sourceConnection = new($"Data Source={workDatabase}");
                using SqliteConnection destinationConnection = new($"Data Source={destination}");
                sourceConnection.Open();
                destinationConnection.Open();
                sourceConnection.BackupDatabase(destinationConnection);
            }
            catch (SqliteException exception)
            {
                throw new InvalidOperationException(
                    $"清洗 {Path.GetFileName(source)} 失败（work={workDatabase}，dest={destination}）：{exception.Message}",
                    exception);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
            {
                string path = workDatabase + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        string[] suffixes = ["-wal", "-shm"];

        using (SqliteConnection connection = new($"Data Source={destination}"))
        {
            connection.Open();
            Execute(connection, "PRAGMA foreign_keys=ON;");
            Execute(connection, "PRAGMA secure_delete=ON;");
            // SQLite 的临时文件默认落在 %TEMP%；本机沙箱对子进程的 %TEMP% 写入受限，
            // 大事务（window_daily_agg 重建）会因此报 "unable to open database file"。
            Execute(connection, "PRAGMA temp_store=MEMORY;");

            Dictionary<string, long> beforeCounts = SnapshotCounts(connection);
            Dictionary<string, double> beforeSums = SnapshotSums(connection);

            using (SqliteTransaction transaction = connection.BeginTransaction())
            {
                UniquePathMasker paths = new();
                ScrubEventSessions(connection, transaction, paths);
                ScrubInputBursts(connection, transaction, paths);
                RebuildWindowDaily(connection, transaction, paths);
                RebuildMonthArchiveApps(connection, transaction, paths);
                RebuildCategoryRules(connection, transaction, paths);
                transaction.Commit();
            }

            EnsureSameCounts(beforeCounts, SnapshotCounts(connection));
            EnsureSameSums(beforeSums, SnapshotSums(connection));

            Execute(connection, "PRAGMA journal_mode=DELETE;");
            Execute(connection, "VACUUM;");
            VerifyIntegrity(connection);
            connection.Close();
        }

        SqliteConnection.ClearAllPools();
        foreach (string suffix in suffixes)
        {
            string path = destination + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void ScrubEventSessions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UniquePathMasker paths)
    {
        List<(long Id, string Path, string Title, long CategoryId)> rows = [];

        using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                "SELECT id, process_path, window_title, category_id FROM events_window_session";

            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3)));
            }
        }

        using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            "UPDATE events_window_session SET process_path=$path, window_title=$title WHERE id=$id";
        SqliteParameter pathParameter = update.Parameters.Add("$path", SqliteType.Text);
        SqliteParameter titleParameter = update.Parameters.Add("$title", SqliteType.Text);
        SqliteParameter idParameter = update.Parameters.Add("$id", SqliteType.Integer);

        foreach ((long id, string path, string title, long categoryId) in rows)
        {
            pathParameter.Value = paths.Mask(path);
            titleParameter.Value = MaskDbTitle(title, categoryId);
            idParameter.Value = id;
            update.ExecuteNonQuery();
        }
    }

    private static void ScrubInputBursts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UniquePathMasker paths)
    {
        List<(long Id, string Path)> rows = [];

        using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT id, process_path FROM events_input_burst";

            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE events_input_burst SET process_path=$path WHERE id=$id";
        SqliteParameter pathParameter = update.Parameters.Add("$path", SqliteType.Text);
        SqliteParameter idParameter = update.Parameters.Add("$id", SqliteType.Integer);

        foreach ((long id, string path) in rows)
        {
            pathParameter.Value = paths.Mask(path);
            idParameter.Value = id;
            update.ExecuteNonQuery();
        }
    }

    private static void RebuildWindowDaily(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UniquePathMasker paths)
    {
        List<WindowRow> rows = [];

        using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT rowid, day, process_key, window_title, process_path, process_name,
                       keys, clicks, wheel, left, right, middle, xbuttons, active_seconds,
                       key_g0, key_g1, key_g2, key_g3, key_g4, key_g5,
                       first_ts_ms, last_ts_ms, category_id, category_source,
                       category_reason, semantic_tag
                FROM window_daily_agg
                """;

            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new WindowRow(
                    RowId: reader.GetInt64(0),
                    Day: reader.GetString(1),
                    WindowTitle: reader.GetString(3),
                    ProcessPath: reader.GetString(4),
                    ProcessName: reader.GetString(5),
                    Keys: reader.GetInt64(6),
                    Clicks: reader.GetInt64(7),
                    Wheel: reader.GetInt64(8),
                    Left: reader.GetInt64(9),
                    Right: reader.GetInt64(10),
                    Middle: reader.GetInt64(11),
                    XButtons: reader.GetInt64(12),
                    ActiveSeconds: reader.GetDouble(13),
                    G0: reader.GetInt64(14),
                    G1: reader.GetInt64(15),
                    G2: reader.GetInt64(16),
                    G3: reader.GetInt64(17),
                    G4: reader.GetInt64(18),
                    G5: reader.GetInt64(19),
                    FirstTsMs: reader.GetInt64(20),
                    LastTsMs: reader.GetInt64(21),
                    CategoryId: reader.GetInt64(22),
                    CategorySource: reader.GetString(23),
                    CategoryReason: reader.GetString(24),
                    SemanticTag: reader.GetString(25)));
            }
        }

        Execute(connection, transaction, "DROP TABLE IF EXISTS window_daily_agg_clean;");
        Execute(connection, transaction, """
            CREATE TABLE window_daily_agg_clean (
              day TEXT NOT NULL,
              process_key TEXT NOT NULL,
              window_title TEXT NOT NULL,
              process_path TEXT NOT NULL DEFAULT '',
              process_name TEXT NOT NULL DEFAULT '',
              keys INTEGER NOT NULL DEFAULT 0,
              clicks INTEGER NOT NULL DEFAULT 0,
              wheel INTEGER NOT NULL DEFAULT 0,
              left INTEGER NOT NULL DEFAULT 0,
              right INTEGER NOT NULL DEFAULT 0,
              middle INTEGER NOT NULL DEFAULT 0,
              xbuttons INTEGER NOT NULL DEFAULT 0,
              active_seconds REAL NOT NULL DEFAULT 0,
              key_g0 INTEGER NOT NULL DEFAULT 0,
              key_g1 INTEGER NOT NULL DEFAULT 0,
              key_g2 INTEGER NOT NULL DEFAULT 0,
              key_g3 INTEGER NOT NULL DEFAULT 0,
              key_g4 INTEGER NOT NULL DEFAULT 0,
              key_g5 INTEGER NOT NULL DEFAULT 0,
              first_ts_ms INTEGER NOT NULL DEFAULT 0,
              last_ts_ms INTEGER NOT NULL DEFAULT 0,
              category_id INTEGER NOT NULL DEFAULT -1,
              category_source TEXT NOT NULL DEFAULT 'none',
              category_reason TEXT NOT NULL DEFAULT '',
              semantic_tag TEXT NOT NULL DEFAULT '',
              PRIMARY KEY (day, process_key, window_title),
              CHECK (category_source IN ('none','default_rule','user_override','ai_cached'))
            );
            """);

        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO window_daily_agg_clean (
              day, process_key, window_title, process_path, process_name,
              keys, clicks, wheel, left, right, middle, xbuttons, active_seconds,
              key_g0, key_g1, key_g2, key_g3, key_g4, key_g5,
              first_ts_ms, last_ts_ms, category_id, category_source,
              category_reason, semantic_tag
            ) VALUES (
              $day, $key, $title, $path, $name,
              $keys, $clicks, $wheel, $left, $right, $middle, $xbuttons, $active,
              $g0, $g1, $g2, $g3, $g4, $g5,
              $first, $last, $category, $source,
              $reason, $tag
            )
            """;
        SqliteParameter[] parameters =
        [
            insert.Parameters.Add("$day", SqliteType.Text),
            insert.Parameters.Add("$key", SqliteType.Text),
            insert.Parameters.Add("$title", SqliteType.Text),
            insert.Parameters.Add("$path", SqliteType.Text),
            insert.Parameters.Add("$name", SqliteType.Text),
            insert.Parameters.Add("$keys", SqliteType.Integer),
            insert.Parameters.Add("$clicks", SqliteType.Integer),
            insert.Parameters.Add("$wheel", SqliteType.Integer),
            insert.Parameters.Add("$left", SqliteType.Integer),
            insert.Parameters.Add("$right", SqliteType.Integer),
            insert.Parameters.Add("$middle", SqliteType.Integer),
            insert.Parameters.Add("$xbuttons", SqliteType.Integer),
            insert.Parameters.Add("$active", SqliteType.Real),
            insert.Parameters.Add("$g0", SqliteType.Integer),
            insert.Parameters.Add("$g1", SqliteType.Integer),
            insert.Parameters.Add("$g2", SqliteType.Integer),
            insert.Parameters.Add("$g3", SqliteType.Integer),
            insert.Parameters.Add("$g4", SqliteType.Integer),
            insert.Parameters.Add("$g5", SqliteType.Integer),
            insert.Parameters.Add("$first", SqliteType.Integer),
            insert.Parameters.Add("$last", SqliteType.Integer),
            insert.Parameters.Add("$category", SqliteType.Integer),
            insert.Parameters.Add("$source", SqliteType.Text),
            insert.Parameters.Add("$reason", SqliteType.Text),
            insert.Parameters.Add("$tag", SqliteType.Text)
        ];

        HashSet<(string Day, string Key, string Title)> used = [];
        Dictionary<(string Day, string Key, string Title), int> titleCounts = [];
        foreach (WindowRow row in rows)
        {
            string key = paths.Mask(row.ProcessPath);
            string title = MaskDbTitle(row.WindowTitle, row.CategoryId);

            if (!PrivacySentinels.IsTitle(title))
            {
                (string Day, string Key, string Title) slot = (row.Day, key, title);
                int count = titleCounts.GetValueOrDefault(slot) + 1;
                titleCounts[slot] = count;
                if (count > 1)
                {
                    title = $"{title}~{count}";
                }
            }

            if (!used.Add((row.Day, key, title)))
            {
                throw new InvalidOperationException(
                    $"window_daily_agg 脱敏后主键仍冲突：{row.Day} / {key} / {title}");
            }

            parameters[0].Value = row.Day;
            parameters[1].Value = key;
            parameters[2].Value = title;
            parameters[3].Value = key;
            parameters[4].Value = row.ProcessName;
            parameters[5].Value = row.Keys;
            parameters[6].Value = row.Clicks;
            parameters[7].Value = row.Wheel;
            parameters[8].Value = row.Left;
            parameters[9].Value = row.Right;
            parameters[10].Value = row.Middle;
            parameters[11].Value = row.XButtons;
            parameters[12].Value = row.ActiveSeconds;
            parameters[13].Value = row.G0;
            parameters[14].Value = row.G1;
            parameters[15].Value = row.G2;
            parameters[16].Value = row.G3;
            parameters[17].Value = row.G4;
            parameters[18].Value = row.G5;
            parameters[19].Value = row.FirstTsMs;
            parameters[20].Value = row.LastTsMs;
            parameters[21].Value = row.CategoryId;
            parameters[22].Value = row.CategorySource;
            parameters[23].Value = PrivacyMask.MaskText(row.CategoryReason);
            parameters[24].Value = row.SemanticTag;
            insert.ExecuteNonQuery();
        }

        Execute(connection, transaction, "DROP TABLE window_daily_agg;");
        Execute(connection, transaction, "ALTER TABLE window_daily_agg_clean RENAME TO window_daily_agg;");
        Execute(connection, transaction, "CREATE INDEX ix_wda_day_cat ON window_daily_agg(day, category_id);");
    }

    private static void RebuildMonthArchiveApps(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UniquePathMasker paths)
    {
        List<(string Ym, string Path, long Keys)> rows = [];

        using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT ym, process_path, keys FROM month_archive_app";

            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
            }
        }

        Execute(connection, transaction, "DROP TABLE IF EXISTS month_archive_app_clean;");
        Execute(connection, transaction, """
            CREATE TABLE month_archive_app_clean (
              ym TEXT NOT NULL,
              process_path TEXT NOT NULL,
              keys INTEGER NOT NULL,
              PRIMARY KEY (ym, process_path)
            );
            """);

        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            "INSERT INTO month_archive_app_clean (ym, process_path, keys) VALUES ($ym, $path, $keys)";
        SqliteParameter ymParameter = insert.Parameters.Add("$ym", SqliteType.Text);
        SqliteParameter pathParameter = insert.Parameters.Add("$path", SqliteType.Text);
        SqliteParameter keysParameter = insert.Parameters.Add("$keys", SqliteType.Integer);

        HashSet<(string Ym, string Path)> used = [];
        foreach ((string ym, string path, long keys) in rows)
        {
            string masked = paths.Mask(path);
            if (!used.Add((ym, masked)))
            {
                throw new InvalidOperationException(
                    $"month_archive_app 脱敏后主键冲突：{ym} / {masked}");
            }

            ymParameter.Value = ym;
            pathParameter.Value = masked;
            keysParameter.Value = keys;
            insert.ExecuteNonQuery();
        }

        Execute(connection, transaction, "DROP TABLE month_archive_app;");
        Execute(connection, transaction, "ALTER TABLE month_archive_app_clean RENAME TO month_archive_app;");
    }

    private static void RebuildCategoryRules(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UniquePathMasker paths)
    {
        List<(long RowId, string Key, long CategoryId, string Source, long UpdatedMs)> rows = [];

        using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT rowid, rule_key, category_id, source, updated_ms FROM app_category_rule";

            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetString(3),
                    reader.GetInt64(4)));
            }
        }

        Execute(connection, transaction, "DROP TABLE IF EXISTS app_category_rule_clean;");
        Execute(connection, transaction, """
            CREATE TABLE app_category_rule_clean (
              rule_key TEXT PRIMARY KEY,
              category_id INTEGER NOT NULL,
              source TEXT NOT NULL DEFAULT 'user_override',
              updated_ms INTEGER NOT NULL DEFAULT 0
            );
            """);

        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO app_category_rule_clean (rule_key, category_id, source, updated_ms)
            VALUES ($key, $category, $source, $updated)
            """;
        SqliteParameter keyParameter = insert.Parameters.Add("$key", SqliteType.Text);
        SqliteParameter categoryParameter = insert.Parameters.Add("$category", SqliteType.Integer);
        SqliteParameter sourceParameter = insert.Parameters.Add("$source", SqliteType.Text);
        SqliteParameter updatedParameter = insert.Parameters.Add("$updated", SqliteType.Integer);

        HashSet<string> used = new(StringComparer.Ordinal);
        Dictionary<string, int> keyCounts = new(StringComparer.Ordinal);
        foreach ((_, string key, long categoryId, string source, long updatedMs) in rows)
        {
            string masked = MaskRuleKey(key, categoryId, paths);
            int count = keyCounts.GetValueOrDefault(masked) + 1;
            keyCounts[masked] = count;
            if (count > 1)
            {
                masked = $"{masked}~{count}";
            }

            if (!used.Add(masked))
            {
                throw new InvalidOperationException($"app_category_rule 脱敏后主键仍冲突：{masked}");
            }

            keyParameter.Value = masked;
            categoryParameter.Value = categoryId;
            sourceParameter.Value = source;
            updatedParameter.Value = updatedMs;
            insert.ExecuteNonQuery();
        }

        Execute(connection, transaction, "DROP TABLE app_category_rule;");
        Execute(connection, transaction, "ALTER TABLE app_category_rule_clean RENAME TO app_category_rule;");
    }

    private static string MaskRuleKey(string key, long categoryId, UniquePathMasker paths)
    {
        if (key.StartsWith("app:", StringComparison.Ordinal))
        {
            return "app:" + paths.Mask(key[4..]);
        }

        if (key.StartsWith("window:", StringComparison.Ordinal))
        {
            string rest = key[7..];
            int newline = rest.IndexOf('\n');
            return newline < 0
                ? "window:" + paths.Mask(rest)
                : "window:" + paths.Mask(rest[..newline]) + "\n" +
                  MaskDbTitle(rest[(newline + 1)..], categoryId);
        }

        if (key.StartsWith("title:", StringComparison.Ordinal))
        {
            string rest = key[6..];
            int newline = rest.IndexOf('\n');
            return newline < 0
                ? "title:" + paths.Mask(rest)
                : "title:" + paths.Mask(rest[..newline]) + "\n" +
                  PrivacyMask.MaskText(rest[(newline + 1)..]);
        }

        return PrivacyMask.MaskText(key);
    }

    private static string MaskDbTitle(string title, long categoryId)
    {
        string normalized = StripOrdinalSuffix(title);
        return PrivacySentinels.IsTitle(normalized)
            ? normalized
            : PrivacyMask.MaskTitle(normalized, (int)categoryId);
    }

    /// <summary>去掉清洗时追加的 <c>~n</c>，保证二次清洗不会叠加后缀。</summary>
    private static string StripOrdinalSuffix(string value)
    {
        int tilde = value.LastIndexOf('~');
        if (tilde <= 0 || tilde == value.Length - 1)
        {
            return value;
        }

        return int.TryParse(value[(tilde + 1)..], out _) ? value[..tilde] : value;
    }

    private static Dictionary<string, long> SnapshotCounts(SqliteConnection connection)
    {
        Dictionary<string, long> counts = new(StringComparer.OrdinalIgnoreCase);

        foreach (string table in ReadTables(connection))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            counts[table] = Convert.ToInt64(command.ExecuteScalar());
        }

        return counts;
    }

    private static Dictionary<string, double> SnapshotSums(SqliteConnection connection)
    {
        Dictionary<string, double> sums = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string table, string column) in SumGuards)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT TOTAL(\"{column}\") FROM \"{table}\"";
            sums[$"{table}.{column}"] = Convert.ToDouble(command.ExecuteScalar());
        }

        return sums;
    }

    private static void EnsureSameCounts(
        IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after)
    {
        foreach ((string table, long expected) in before)
        {
            if (!after.TryGetValue(table, out long actual) || actual != expected)
            {
                throw new InvalidOperationException($"清洗改变了 {table} 的行数：{expected} → {actual}。");
            }
        }
    }

    private static void EnsureSameSums(
        IReadOnlyDictionary<string, double> before,
        IReadOnlyDictionary<string, double> after)
    {
        foreach ((string field, double expected) in before)
        {
            if (!after.TryGetValue(field, out double actual) ||
                Math.Abs(expected - actual) > Math.Max(1.0, Math.Abs(expected)) * 1e-9)
            {
                throw new InvalidOperationException($"清洗改变了汇总值 {field}：{expected} → {actual}。");
            }
        }
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check";
            string result = Convert.ToString(command.ExecuteScalar()) ?? "";
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"清理后的数据库完整性检查失败：{result}");
            }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check";
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                throw new InvalidOperationException("清理后的数据库存在外键孤儿行。");
            }
        }
    }

    private static List<string> ReadTables(SqliteConnection connection)
    {
        List<string> tables = [];

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
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

    private static void PrepareDestination(string destination)
    {
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string path = destination + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void CopyDatabaseFamily(string source, string destination)
    {
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            string from = source + suffix;
            if (File.Exists(from))
            {
                File.Copy(from, destination + suffix, overwrite: true);
            }
        }
    }

    private sealed record WindowRow(
        long RowId,
        string Day,
        string WindowTitle,
        string ProcessPath,
        string ProcessName,
        long Keys,
        long Clicks,
        long Wheel,
        long Left,
        long Right,
        long Middle,
        long XButtons,
        double ActiveSeconds,
        long G0,
        long G1,
        long G2,
        long G3,
        long G4,
        long G5,
        long FirstTsMs,
        long LastTsMs,
        long CategoryId,
        string CategorySource,
        string CategoryReason,
        string SemanticTag);
}
