using System.Data;
using System.Globalization;
using Kms.Core.Time;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Sqlite;

/// <summary>
/// SQLite <c>schema_rev = 3</c> 的 DDL 与幂等初始化。
/// v1 核心 13 表保持与 v3 <c>KmsSchema.cs</c> 逐字一致（含索引、CHECK 与
/// <c>kms_import_log</c>）；v2 增量新增应用身份与按日分桶表；v3 新增
/// 自定义工作性质分类与规则 category_key。迁移只做增量，不回填历史统计。
/// 本类型只负责建表与写 meta 基线，不包含 3995 年时间基准修复。
/// </summary>
public static class KmsSchema
{
    /// <summary>当前 schema 版本。任何表结构变更都必须 +1 并补迁移分支。</summary>
    public const int Rev = 3;

    /// <summary>数据库文件名（放在数据根目录下）。</summary>
    public const string DbFileName = "kmstats.db";

    /// <summary>v1 的 13 张业务表；读取旧库时仍必须完整。</summary>
    public static readonly string[] LegacyRequiredTables =
    [
        "meta",
        "events_window_session",
        "events_input_burst",
        "daily_click_grid",
        "daily_summary",
        "daily_hour_agg",
        "daily_key_agg",
        "window_daily_agg",
        "month_archive",
        "month_archive_app",
        "app_category_rule",
        "migration_log",
        "kms_import_log"
    ];

    /// <summary>v2 应用维度新增表。</summary>
    public static readonly string[] V2Tables =
    [
        "app_dim",
        "app_alias",
        "daily_app_hour_agg",
        "daily_app_key_agg",
        "daily_app_click_grid",
        "daily_app_metric_agg"
    ];

    /// <summary>v3 自定义工作性质分类表。</summary>
    public static readonly string[] V3Tables = ["app_category"];

    /// <summary>v3 完整业务表清单；自检与新建库按这份清单校验。</summary>
    public static readonly string[] RequiredTables =
        [.. LegacyRequiredTables, .. V2Tables, .. V3Tables];

    private const string MetaDdl =
        "CREATE TABLE IF NOT EXISTS meta (" +
        "  k TEXT PRIMARY KEY," +
        "  v TEXT NOT NULL" +
        ");";

    private const string WindowSessionDdl =
        "CREATE TABLE IF NOT EXISTS events_window_session (" +
        "  id             INTEGER PRIMARY KEY AUTOINCREMENT," +
        "  start_ts_ms    INTEGER NOT NULL," +
        "  end_ts_ms      INTEGER NOT NULL," +
        "  day            TEXT    NOT NULL," +
        "  process_path   TEXT    NOT NULL DEFAULT ''," +
        "  process_name   TEXT    NOT NULL DEFAULT ''," +
        "  window_title   TEXT    NOT NULL DEFAULT ''," +
        "  key_count      INTEGER NOT NULL DEFAULT 0," +
        "  left_clicks    INTEGER NOT NULL DEFAULT 0," +
        "  right_clicks   INTEGER NOT NULL DEFAULT 0," +
        "  middle_clicks  INTEGER NOT NULL DEFAULT 0," +
        "  x_clicks       INTEGER NOT NULL DEFAULT 0," +
        "  wheel          INTEGER NOT NULL DEFAULT 0," +
        "  move_px        REAL    NOT NULL DEFAULT 0," +
        "  rime_chars     INTEGER NOT NULL DEFAULT 0," +
        "  active_ms      INTEGER NOT NULL DEFAULT 0," +
        "  category_id    INTEGER NOT NULL DEFAULT -1," +
        "  category_source TEXT   NOT NULL DEFAULT 'none'," +
        "  semantic_tag   TEXT    NOT NULL DEFAULT ''," +
        "  is_open        INTEGER NOT NULL DEFAULT 0," +
        "  CHECK (end_ts_ms >= start_ts_ms)," +
        "  CHECK (category_source IN ('none','default_rule','user_override','ai_cached'))" +
        ");";

    private static readonly string[] WindowSessionIndexes =
    [
        "CREATE INDEX IF NOT EXISTS ix_ews_day ON events_window_session(day);",
        "CREATE INDEX IF NOT EXISTS ix_ews_day_proc ON events_window_session(day, process_path, window_title);",
        // 普通索引：兼容老引擎，且 is_open=1 的行极少，选择性足够。
        "CREATE INDEX IF NOT EXISTS ix_ews_open ON events_window_session(is_open);"
    ];

    private const string InputBurstDdl =
        "CREATE TABLE IF NOT EXISTS events_input_burst (" +
        "  id             INTEGER PRIMARY KEY AUTOINCREMENT," +
        "  session_id     INTEGER REFERENCES events_window_session(id) ON DELETE SET NULL," +
        "  start_ts_ms    INTEGER NOT NULL," +
        "  end_ts_ms      INTEGER NOT NULL," +
        "  day            TEXT    NOT NULL," +
        "  key_count      INTEGER NOT NULL," +
        "  iki_avg_ms     REAL    NOT NULL DEFAULT 0," +
        "  iki_var        REAL    NOT NULL DEFAULT 0," +
        "  iki_p50_ms     REAL    NOT NULL DEFAULT 0," +
        "  backspace_cnt  INTEGER NOT NULL DEFAULT 0," +
        "  backspace_rate REAL    NOT NULL DEFAULT 0," +
        "  switch_count   INTEGER NOT NULL DEFAULT 0," +
        "  process_path   TEXT    NOT NULL DEFAULT ''," +
        "  category_id    INTEGER NOT NULL DEFAULT -1" +
        ");";

    private static readonly string[] InputBurstIndexes =
    [
        "CREATE INDEX IF NOT EXISTS ix_eib_day ON events_input_burst(day);",
        "CREATE INDEX IF NOT EXISTS ix_eib_ts ON events_input_burst(start_ts_ms);"
    ];

    private const string ClickGridDdl =
        "CREATE TABLE IF NOT EXISTS daily_click_grid (" +
        "  day       TEXT    NOT NULL," +
        "  channel   INTEGER NOT NULL," +
        "  cell_x    INTEGER NOT NULL," +
        "  cell_y    INTEGER NOT NULL," +
        "  screen_id INTEGER NOT NULL DEFAULT 0," +
        "  count     INTEGER NOT NULL," +
        "  PRIMARY KEY (day, channel, cell_x, cell_y)" +
        ");";

    private const string DailySummaryDdl =
        "CREATE TABLE IF NOT EXISTS daily_summary (" +
        "  day TEXT PRIMARY KEY," +
        "  keys INTEGER NOT NULL DEFAULT 0," +
        "  clicks INTEGER NOT NULL DEFAULT 0," +
        "  left INTEGER NOT NULL DEFAULT 0," +
        "  right INTEGER NOT NULL DEFAULT 0," +
        "  middle INTEGER NOT NULL DEFAULT 0," +
        "  xbuttons INTEGER NOT NULL DEFAULT 0," +
        "  wheel INTEGER NOT NULL DEFAULT 0," +
        "  move_px REAL NOT NULL DEFAULT 0," +
        "  move_meters REAL NOT NULL DEFAULT 0," +
        "  active_seconds REAL NOT NULL DEFAULT 0," +
        "  idle_seconds REAL NOT NULL DEFAULT 0," +
        "  app_observed_seconds REAL NOT NULL DEFAULT 0," +
        "  app_switches INTEGER NOT NULL DEFAULT 0," +
        "  peak_apm INTEGER NOT NULL DEFAULT 0," +
        "  hold_count INTEGER NOT NULL DEFAULT 0," +
        "  hold_discarded INTEGER NOT NULL DEFAULT 0," +
        "  hold_total_ms REAL NOT NULL DEFAULT 0," +
        "  hold_max_ms REAL NOT NULL DEFAULT 0," +
        "  hold_active_seconds REAL NOT NULL DEFAULT 0," +
        "  sessions INTEGER NOT NULL DEFAULT 0," +
        "  origin TEXT NOT NULL DEFAULT 'native'" +
        ");";

    private const string DailyHourDdl =
        "CREATE TABLE IF NOT EXISTS daily_hour_agg (" +
        "  day TEXT NOT NULL," +
        "  hour INTEGER NOT NULL," +
        "  keys INTEGER NOT NULL DEFAULT 0," +
        "  clicks INTEGER NOT NULL DEFAULT 0," +
        "  active_seconds REAL NOT NULL DEFAULT 0," +
        "  PRIMARY KEY (day, hour)" +
        ");";

    private const string DailyKeyDdl =
        "CREATE TABLE IF NOT EXISTS daily_key_agg (" +
        "  day TEXT NOT NULL," +
        "  vk INTEGER NOT NULL," +
        "  cnt INTEGER NOT NULL," +
        "  PRIMARY KEY (day, vk)" +
        ");";

    private const string WindowDailyDdl =
        "CREATE TABLE IF NOT EXISTS window_daily_agg (" +
        "  day TEXT NOT NULL," +
        "  process_key TEXT NOT NULL," +
        "  window_title TEXT NOT NULL," +
        "  process_path TEXT NOT NULL DEFAULT ''," +
        "  process_name TEXT NOT NULL DEFAULT ''," +
        "  keys INTEGER NOT NULL DEFAULT 0," +
        "  clicks INTEGER NOT NULL DEFAULT 0," +
        "  wheel INTEGER NOT NULL DEFAULT 0," +
        "  left INTEGER NOT NULL DEFAULT 0," +
        "  right INTEGER NOT NULL DEFAULT 0," +
        "  middle INTEGER NOT NULL DEFAULT 0," +
        "  xbuttons INTEGER NOT NULL DEFAULT 0," +
        "  active_seconds REAL NOT NULL DEFAULT 0," +
        "  key_g0 INTEGER NOT NULL DEFAULT 0," +
        "  key_g1 INTEGER NOT NULL DEFAULT 0," +
        "  key_g2 INTEGER NOT NULL DEFAULT 0," +
        "  key_g3 INTEGER NOT NULL DEFAULT 0," +
        "  key_g4 INTEGER NOT NULL DEFAULT 0," +
        "  key_g5 INTEGER NOT NULL DEFAULT 0," +
        "  first_ts_ms INTEGER NOT NULL DEFAULT 0," +
        "  last_ts_ms INTEGER NOT NULL DEFAULT 0," +
        "  category_id INTEGER NOT NULL DEFAULT -1," +
        "  category_source TEXT NOT NULL DEFAULT 'none'," +
        "  category_reason TEXT NOT NULL DEFAULT ''," +
        "  semantic_tag TEXT NOT NULL DEFAULT ''," +
        "  PRIMARY KEY (day, process_key, window_title)," +
        "  CHECK (category_source IN ('none','default_rule','user_override','ai_cached'))" +
        ");";

    private static readonly string[] WindowDailyIndexes =
    [
        "CREATE INDEX IF NOT EXISTS ix_wda_day_cat ON window_daily_agg(day, category_id);"
    ];

    private const string MonthArchiveDdl =
        "CREATE TABLE IF NOT EXISTS month_archive (" +
        "  ym TEXT PRIMARY KEY," +
        "  days INTEGER NOT NULL DEFAULT 0," +
        "  keys INTEGER NOT NULL DEFAULT 0," +
        "  clicks INTEGER NOT NULL DEFAULT 0," +
        "  wheel INTEGER NOT NULL DEFAULT 0," +
        "  combos INTEGER NOT NULL DEFAULT 0," +
        "  active_seconds REAL NOT NULL DEFAULT 0," +
        "  move_meters REAL NOT NULL DEFAULT 0," +
        "  sessions INTEGER NOT NULL DEFAULT 0," +
        "  hold_count INTEGER NOT NULL DEFAULT 0," +
        "  hold_discarded INTEGER NOT NULL DEFAULT 0," +
        "  hold_total_ms REAL NOT NULL DEFAULT 0," +
        "  hold_max_ms REAL NOT NULL DEFAULT 0," +
        "  hold_active_seconds REAL NOT NULL DEFAULT 0" +
        ");";

    private const string MonthArchiveAppDdl =
        "CREATE TABLE IF NOT EXISTS month_archive_app (" +
        "  ym TEXT NOT NULL," +
        "  process_path TEXT NOT NULL," +
        "  keys INTEGER NOT NULL," +
        "  PRIMARY KEY (ym, process_path)" +
        ");";

    private const string CategoryRuleDdl =
        "CREATE TABLE IF NOT EXISTS app_category_rule (" +
        "  rule_key TEXT PRIMARY KEY," +
        "  category_id INTEGER NOT NULL," +
        "  category_key TEXT NOT NULL DEFAULT ''," +
        "  source TEXT NOT NULL DEFAULT 'user_override'," +
        "  updated_ms INTEGER NOT NULL DEFAULT 0" +
        ");";

    private const string AppCategoryDdl =
        "CREATE TABLE IF NOT EXISTS app_category (" +
        "  category_key TEXT PRIMARY KEY," +
        "  name TEXT NOT NULL," +
        "  color_hex TEXT NOT NULL," +
        "  is_work INTEGER NOT NULL DEFAULT 1," +
        "  sort_order INTEGER NOT NULL DEFAULT 0," +
        "  is_builtin INTEGER NOT NULL DEFAULT 0," +
        "  legacy_id INTEGER NOT NULL DEFAULT -1" +
        ");";

    private const string AppDimDdl =
        "CREATE TABLE IF NOT EXISTS app_dim (" +
        "  app_uid INTEGER PRIMARY KEY," +
        "  app_key TEXT NOT NULL UNIQUE," +
        "  display_name TEXT NOT NULL DEFAULT ''," +
        "  canonical_path TEXT NOT NULL DEFAULT ''," +
        "  first_seen_ms INTEGER NOT NULL DEFAULT 0," +
        "  last_seen_ms INTEGER NOT NULL DEFAULT 0," +
        "  flags INTEGER NOT NULL DEFAULT 0" +
        ");";

    private const string AppAliasDdl =
        "CREATE TABLE IF NOT EXISTS app_alias (" +
        "  from_app_key TEXT PRIMARY KEY," +
        "  to_app_uid INTEGER NOT NULL REFERENCES app_dim(app_uid)," +
        "  reason TEXT NOT NULL DEFAULT ''," +
        "  updated_ms INTEGER NOT NULL DEFAULT 0" +
        ");";

    private const string AppHourDdl =
        "CREATE TABLE IF NOT EXISTS daily_app_hour_agg (" +
        "  app_uid INTEGER NOT NULL," +
        "  day TEXT NOT NULL," +
        "  hour INTEGER NOT NULL," +
        "  keys INTEGER NOT NULL DEFAULT 0," +
        "  clicks INTEGER NOT NULL DEFAULT 0," +
        "  active_seconds REAL NOT NULL DEFAULT 0," +
        "  PRIMARY KEY (app_uid, day, hour)" +
        ") WITHOUT ROWID;";

    private const string AppKeyDdl =
        "CREATE TABLE IF NOT EXISTS daily_app_key_agg (" +
        "  app_uid INTEGER NOT NULL," +
        "  day TEXT NOT NULL," +
        "  vk INTEGER NOT NULL," +
        "  cnt INTEGER NOT NULL," +
        "  PRIMARY KEY (app_uid, day, vk)" +
        ") WITHOUT ROWID;";

    private const string AppClickGridDdl =
        "CREATE TABLE IF NOT EXISTS daily_app_click_grid (" +
        "  app_uid INTEGER NOT NULL," +
        "  day TEXT NOT NULL," +
        "  channel INTEGER NOT NULL," +
        "  cell_x INTEGER NOT NULL," +
        "  cell_y INTEGER NOT NULL," +
        "  count INTEGER NOT NULL," +
        "  PRIMARY KEY (app_uid, day, channel, cell_x, cell_y)" +
        ") WITHOUT ROWID;";

    private const string AppMetricDdl =
        "CREATE TABLE IF NOT EXISTS daily_app_metric_agg (" +
        "  app_uid INTEGER NOT NULL," +
        "  day TEXT NOT NULL," +
        "  move_px REAL NOT NULL DEFAULT 0," +
        "  peak_apm INTEGER NOT NULL DEFAULT 0," +
        "  sessions INTEGER NOT NULL DEFAULT 0," +
        "  switches INTEGER NOT NULL DEFAULT 0," +
        "  PRIMARY KEY (app_uid, day)" +
        ") WITHOUT ROWID;";

    private static readonly string[] V2Indexes =
    [
        "CREATE INDEX IF NOT EXISTS ix_app_dim_path ON app_dim(canonical_path);",
        "CREATE INDEX IF NOT EXISTS ix_app_alias_uid ON app_alias(to_app_uid);",
        "CREATE INDEX IF NOT EXISTS ix_wda_proc_day ON window_daily_agg(process_key, day);",
        "CREATE INDEX IF NOT EXISTS ix_ews_proc_day ON events_window_session(process_path, day);",
        "CREATE INDEX IF NOT EXISTS ix_eib_proc_day ON events_input_burst(process_path, start_ts_ms);",
        "CREATE INDEX IF NOT EXISTS ix_daily_app_hour_day ON daily_app_hour_agg(day);",
        "CREATE INDEX IF NOT EXISTS ix_daily_app_key_day ON daily_app_key_agg(day);",
        "CREATE INDEX IF NOT EXISTS ix_daily_app_click_day ON daily_app_click_grid(day);",
        "CREATE INDEX IF NOT EXISTS ix_daily_app_metric_day ON daily_app_metric_agg(day);"
    ];

    private const string MigrationLogDdl =
        "CREATE TABLE IF NOT EXISTS migration_log (" +
        "  source_kind TEXT NOT NULL," +
        "  source_id TEXT NOT NULL," +
        "  content_sha256 TEXT NOT NULL," +
        "  days_imported INTEGER NOT NULL DEFAULT 0," +
        "  imported_ms INTEGER NOT NULL," +
        "  PRIMARY KEY (source_kind, source_id)" +
        ");";

    /// <summary>v1 核心 DDL，按依赖顺序排列。</summary>
    public static string[] Statements()
    {
        List<string> all =
        [
            MetaDdl,
            WindowSessionDdl
        ];
        all.AddRange(WindowSessionIndexes);
        all.Add(InputBurstDdl);
        all.AddRange(InputBurstIndexes);
        all.Add(ClickGridDdl);
        all.Add(DailySummaryDdl);
        all.Add(DailyHourDdl);
        all.Add(DailyKeyDdl);
        all.Add(WindowDailyDdl);
        all.AddRange(WindowDailyIndexes);
        all.Add(MonthArchiveDdl);
        all.Add(MonthArchiveAppDdl);
        all.Add(CategoryRuleDdl);
        all.Add(MigrationLogDdl);
        // .kms 增量包的幂等账本。主键 (package_id, record_key) 是唯一仲裁者。
        all.Add(
            "CREATE TABLE IF NOT EXISTS kms_import_log (" +
            "  package_id TEXT NOT NULL," +
            "  record_key TEXT NOT NULL," +
            "  content_hash TEXT NOT NULL," +
            "  imported_ms INTEGER NOT NULL," +
            "  PRIMARY KEY (package_id, record_key)" +
            ");");
        return [.. all];
    }

    /// <summary>v2 按应用增量分桶与身份表 DDL；迁移只新建，不回填历史。</summary>
    public static string[] V2Statements()
    {
        List<string> all =
        [
            AppDimDdl,
            AppAliasDdl,
            AppHourDdl,
            AppKeyDdl,
            AppClickGridDdl,
            AppMetricDdl
        ];
        all.AddRange(V2Indexes);
        return [.. all];
    }

    /// <summary>v3 自定义工作性质分类 DDL；规则表的 category_key 由迁移按需 ALTER。</summary>
    public static string[] V3Statements() =>
    [
        AppCategoryDdl,
        "CREATE INDEX IF NOT EXISTS ix_app_category_sort ON app_category(sort_order, category_key);",
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_app_category_legacy ON app_category(legacy_id) WHERE legacy_id >= 0;",
        "CREATE INDEX IF NOT EXISTS ix_app_rule_category ON app_category_rule(category_key);"
    ];

    /// <summary>
    /// 幂等落地 schema：建表 → 建索引 → 写 meta 基线 → 设 <c>user_version</c>。
    /// 反复调用结果恒等。不会执行 3995 年时间基准修复。
    /// </summary>
    /// <param name="connection">已打开的连接；本方法不负责打开或关闭它。</param>
    /// <param name="appVersion">写入 <c>meta.app_version</c> 的版本号；已存在时不覆盖。</param>
    public static void Ensure(SqliteConnection connection, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("KmsSchema.Ensure 需要已打开的 SQLite 连接。");
        }

        foreach (string statement in Statements())
        {
            Execute(connection, statement);
        }

        Migration.SchemaV2Migrator.Apply(connection);
        Migration.SchemaV3Migrator.Apply(connection);

        long now = UnixTime.NowMs(SystemClock.Instance);
        using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            UpsertMeta(connection, transaction, "schema_rev", Rev.ToString(CultureInfo.InvariantCulture));
            InsertMeta(connection, transaction, "app_version", appVersion ?? "");
            InsertMeta(connection, transaction, "db_created_ms", now.ToString(CultureInfo.InvariantCulture));
            InsertMeta(connection, transaction, "migrated_from_format", "");
            InsertMeta(connection, transaction, "last_event_ts_ms", "0");
            InsertMeta(connection, transaction, "open_session_id", "0");
            transaction.Commit();
        }

        if (!Migration.Epoch3995Fix.HasDirtyData(connection))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO meta (k, v) VALUES ('epoch_fix_v1', '1');";
            command.ExecuteNonQuery();
        }

        Execute(connection, "PRAGMA user_version = " + Rev.ToString(CultureInfo.InvariantCulture) + ";");
    }

    /// <summary>返回缺失的 v3 完整表名；空数组表示 schema 完整。</summary>
    public static string[] MissingTables(SqliteConnection connection) =>
        MissingTables(connection, RequiredTables);

    /// <summary>v1 读取器兼容校验：只要求 13 张旧表存在。</summary>
    public static string[] MissingLegacyTables(SqliteConnection connection) =>
        MissingTables(connection, LegacyRequiredTables);

    private static string[] MissingTables(SqliteConnection connection, string[] tables)
    {
        ArgumentNullException.ThrowIfNull(connection);

        List<string> missing = [];
        foreach (string table in tables)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            command.Parameters.AddWithValue("$name", table);
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                missing.Add(table);
            }
        }

        return [.. missing];
    }

    private static void InsertMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO meta (k, v) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    internal static void UpsertMeta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO meta (k, v) VALUES ($key, $value) " +
            "ON CONFLICT(k) DO UPDATE SET v=excluded.v;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
