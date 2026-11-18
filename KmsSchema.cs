// ============================================================================
//  键鼠统计 v3.0  Milestone 1  KmsSchema.cs
//  schema_rev = 1 的全部 DDL 与幂等初始化。
//
//  设计要点:
//   · 全部语句都带 IF NOT EXISTS,重复执行恒等 —— 迁移器与测试都依赖这一点。
//   · ix_ews_open 用普通索引(不用部分索引),兼容更老的 SQLite 引擎
//     (部分索引要 3.8.0+,WITHOUT ROWID 要 3.8.2+;本项目要留退路)。
//   · 序号约定与勘察蓝图 §2.1.2 逐字节一致,便于对照文档审阅。
//
//  CLEAN_EDITION 下本文件整体不参与编译。
// ============================================================================

#if !CLEAN_EDITION

using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
    internal static class KmsSchema
    {
        /// <summary>当前 schema 版本。任何表结构变更都必须 +1 并补迁移分支。</summary>
        internal const int Rev = 1;

        /// <summary>数据库文件名(放在数据根目录下)。</summary>
        internal const string DbFileName = "kmstats.db";

        /// <summary>13 张业务表;自检与测试按这份清单校验。</summary>
        internal static readonly string[] RequiredTables = new string[]
        {
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
        };

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

        private static readonly string[] WindowSessionIndexes = new string[]
        {
            "CREATE INDEX IF NOT EXISTS ix_ews_day ON events_window_session(day);",
            "CREATE INDEX IF NOT EXISTS ix_ews_day_proc ON events_window_session(day, process_path, window_title);",
            // 普通索引:兼容老引擎,且 is_open=1 的行极少,选择性足够。
            "CREATE INDEX IF NOT EXISTS ix_ews_open ON events_window_session(is_open);"
        };

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

        private static readonly string[] InputBurstIndexes = new string[]
        {
            "CREATE INDEX IF NOT EXISTS ix_eib_day ON events_input_burst(day);",
            "CREATE INDEX IF NOT EXISTS ix_eib_ts ON events_input_burst(start_ts_ms);"
        };

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

        private static readonly string[] WindowDailyIndexes = new string[]
        {
            "CREATE INDEX IF NOT EXISTS ix_wda_day_cat ON window_daily_agg(day, category_id);"
        };

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
            "  source TEXT NOT NULL DEFAULT 'user_override'," +
            "  updated_ms INTEGER NOT NULL DEFAULT 0" +
            ");";

        private const string MigrationLogDdl =
            "CREATE TABLE IF NOT EXISTS migration_log (" +
            "  source_kind TEXT NOT NULL," +
            "  source_id TEXT NOT NULL," +
            "  content_sha256 TEXT NOT NULL," +
            "  days_imported INTEGER NOT NULL DEFAULT 0," +
            "  imported_ms INTEGER NOT NULL," +
            "  PRIMARY KEY (source_kind, source_id)" +
            ");";

        /// <summary>按依赖顺序排列的全部 DDL。</summary>
        internal static string[] Statements()
        {
            List<string> all = new List<string>();
            all.Add(MetaDdl);
            all.Add(WindowSessionDdl);
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
            // Milestone 3 / 4.3b:.kms 增量包的幂等账本。
            // 主键 (package_id, record_key) —— 同一包无论如何重启,第二次导入都命中主键。
            all.Add(
                "CREATE TABLE IF NOT EXISTS kms_import_log (" +
                "  package_id TEXT NOT NULL," +
                "  record_key TEXT NOT NULL," +
                "  content_hash TEXT NOT NULL," +
                "  imported_ms INTEGER NOT NULL," +
                "  PRIMARY KEY (package_id, record_key)" +
                ");");
            return all.ToArray();
        }

        /// <summary>
        /// 幂等落地 schema:建表 → 建索引 → 写 meta 基线 → 设 user_version。
        /// 反复调用结果恒等。
        /// </summary>
        internal static void Ensure(KmsDb db)
        {
            if (db == null) throw new KmsDbException("Ensure 需要连接");
            string[] statements = Statements();
            for (int i = 0; i < statements.Length; i++)
            {
                if (string.IsNullOrEmpty(statements[i])) continue;
                db.Execute(statements[i]);
            }

            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            db.RunInTransaction(delegate(KmsDb tx)
            {
                tx.Execute("INSERT OR IGNORE INTO meta (k, v) VALUES ('schema_rev', ?);",
                    new object[] { Rev.ToString() });
                tx.Execute("INSERT OR IGNORE INTO meta (k, v) VALUES ('app_version', ?);",
                    new object[] { BuildInfo.Version });
                tx.Execute("INSERT OR IGNORE INTO meta (k, v) VALUES ('db_created_ms', ?);",
                    new object[] { now.ToString() });
                tx.Execute("INSERT OR IGNORE INTO meta (k, v) VALUES ('migrated_from_format', '');");
                tx.Execute("INSERT OR IGNORE INTO meta (k, v) VALUES ('last_event_ts_ms', '0');");
                tx.Execute("INSERT OR IGNORE INTO meta (k, v) VALUES ('open_session_id', '0');");
            });

            db.Execute("PRAGMA user_version = " + Rev.ToString() + ";");
        }

        /// <summary>返回缺失的表名;空数组表示 schema 完整。</summary>
        internal static string[] MissingTables(KmsDb db)
        {
            List<string> missing = new List<string>();
            for (int i = 0; i < RequiredTables.Length; i++)
            {
                if (!db.TableExists(RequiredTables[i])) missing.Add(RequiredTables[i]);
            }
            return missing.ToArray();
        }

        /// <summary>读回库里的 schema_rev;读不到返回 -1。</summary>
        internal static int ReadRev(KmsDb db)
        {
            try
            {
                string raw = db.ScalarText("SELECT v FROM meta WHERE k='schema_rev';", null);
                int value;
                if (int.TryParse(raw, out value)) return value;
                return -1;
            }
            catch
            {
                return -1;
            }
        }
    }
}

#endif
