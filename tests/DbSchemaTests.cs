// ============================================================================
//  tests/DbSchemaTests.cs  ·  v3.0 Milestone 1 / Step 2
//
//  验证(对应任务书 Step 2 的验收项):
//   1. 五个 PRAGMA 全部生效(journal_mode / synchronous / foreign_keys /
//      busy_timeout / temp_store);
//   2. DDL 重复执行幂等(跑三次,表数与 meta 均不变);
//   3. category_source 合法性约束真的会拦下非法值(两张表都要);
//   4. 十二张表齐全、schema_rev 与 user_version 落地;
//   5. 单写者 / 只读查询连接分离;事务回滚;外键生效;热区格子累加正确。
//
//  全部写入都在 %TEMP% 沙盒内,绝不触碰用户真实存档目录。
// ============================================================================

using System;
using System.IO;
using System.Text;
using KeyMouseStats;

internal static class DbSchemaTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Expect(long actual, long expected, string name)
    {
        Check(actual == expected, name + "(期望 " + expected + ",实际 " + actual + ")");
    }

    /// <summary>断言一个操作会抛出 KmsDbException,并返回其消息用于诊断。</summary>
    private static string ExpectFailure(Action body, string name)
    {
        try
        {
            body();
        }
        catch (KmsDbException error)
        {
            _checks++;
            return error.Message;
        }
        throw new Exception(name + ":本应失败却成功了");
    }

    private static long PragmaInt(KmsDb db, string pragma)
    {
        string raw = db.ScalarText("PRAGMA " + pragma + ";", null);
        long value;
        if (long.TryParse(raw, out value)) return value;
        return long.MinValue;
    }

    private static void Main()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "KmsSchemaTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        KmsDb writer = null;
        try
        {
            NativeBoot.RootOverride = sandbox;
            Check(NativeBoot.TryEnsure(), "原生引擎应可用:" + NativeBoot.Diagnosis);

            string dbPath = Path.Combine(sandbox, KmsSchema.DbFileName);
            writer = KmsDb.OpenWriter(dbPath);
            KmsSchema.Ensure(writer);

            CheckPragmas(writer);
            CheckTables(writer);
            CheckIdempotent(writer);
            CheckConstraints(writer);
            CheckTransaction(writer);
            CheckForeignKeys(writer);
            CheckReaderSeparation(dbPath);
            CheckRoundTrip(sandbox);
            CheckClickGrid(sandbox);

            Console.WriteLine("DbSchemaTests: " + _checks + " 项断言通过");
        }
        finally
        {
            if (writer != null) { try { writer.Dispose(); } catch { } }
            try { NativeBoot.ReleaseForTest(); } catch { }
            try { Directory.Delete(sandbox, true); } catch { }
        }
    }

    /// <summary>1. PRAGMA 必须逐条生效。</summary>
    private static void CheckPragmas(KmsDb db)
    {
        string mode = db.ScalarText("PRAGMA journal_mode;", null);
        Check(string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase),
            "journal_mode 应为 wal,实际=" + mode);

        Expect(PragmaInt(db, "synchronous"), 2, "synchronous 应为 FULL(2)");
        Expect(PragmaInt(db, "foreign_keys"), 1, "foreign_keys 应为 1");
        Expect(PragmaInt(db, "busy_timeout"), 5000, "busy_timeout 应为 5000");
        Expect(PragmaInt(db, "temp_store"), 2, "temp_store 应为 MEMORY(2)");
    }

    /// <summary>4. 十二张表齐全,schema_rev / user_version 落地。</summary>
    private static void CheckTables(KmsDb db)
    {
        string[] missing = KmsSchema.MissingTables(db);
        Check(missing.Length == 0, "缺少表:" + string.Join(",", missing));

        long tables = db.ScalarLong(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", null);
        Expect(tables, KmsSchema.RequiredTables.Length, "业务表数量");

        Expect(KmsSchema.ReadRev(db), KmsSchema.Rev, "meta.schema_rev");
        Expect(PragmaInt(db, "user_version"), KmsSchema.Rev, "PRAGMA user_version");

        // ix_ews_open 必须是普通索引(不是部分索引),以保证老引擎兼容。
        string indexSql = db.ScalarText(
            "SELECT sql FROM sqlite_master WHERE type='index' AND name='ix_ews_open';", null);
        Check(!string.IsNullOrEmpty(indexSql), "ix_ews_open 索引应存在");
        Check(indexSql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) < 0,
            "ix_ews_open 不应使用部分索引,实际=" + indexSql);

        // 三通道热区表的主键顺序决定累加语义,必须精确匹配。
        string gridSql = db.ScalarText(
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='daily_click_grid';", null);
        Check(gridSql.IndexOf("PRIMARY KEY (day, channel, cell_x, cell_y)", StringComparison.Ordinal) >= 0,
            "daily_click_grid 主键应为 (day, channel, cell_x, cell_y)");
    }

    /// <summary>2. DDL 重复执行必须幂等。</summary>
    private static void CheckIdempotent(KmsDb db)
    {
        long tablesBefore = db.ScalarLong(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", null);
        long metaBefore = db.ScalarLong("SELECT COUNT(*) FROM meta;", null);
        string revBefore = db.ScalarText("SELECT v FROM meta WHERE k='schema_rev';", null);

        for (int i = 0; i < 3; i++) KmsSchema.Ensure(db);

        long tablesAfter = db.ScalarLong(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", null);
        long metaAfter = db.ScalarLong("SELECT COUNT(*) FROM meta;", null);
        string revAfter = db.ScalarText("SELECT v FROM meta WHERE k='schema_rev';", null);

        Expect(tablesAfter, tablesBefore, "重复建表后表数量");
        Expect(metaAfter, metaBefore, "重复初始化后 meta 行数");
        Check(string.Equals(revBefore, revAfter, StringComparison.Ordinal), "schema_rev 不应被改写");
    }

    /// <summary>3. category_source 的四值封闭枚举必须真的拦住非法值。</summary>
    private static void CheckConstraints(KmsDb db)
    {
        string badSession = ExpectFailure(delegate
        {
            db.Execute(
                "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, category_source) " +
                "VALUES (1, 2, '2026-01-01', 'bogus_source');", null);
        }, "events_window_session 应拒绝非法 category_source");
        Check(badSession.IndexOf("CHECK", StringComparison.OrdinalIgnoreCase) >= 0
            || badSession.IndexOf("constraint", StringComparison.OrdinalIgnoreCase) >= 0,
            "拒绝原因应来自 CHECK 约束,实际=" + badSession);

        ExpectFailure(delegate
        {
            db.Execute(
                "INSERT INTO window_daily_agg (day, process_key, window_title, category_source) " +
                "VALUES ('2026-01-01', 'k', 't', 'ai');", null);
        }, "window_daily_agg 应拒绝非四值枚举");

        // end_ts_ms < start_ts_ms 必须被拦下。
        ExpectFailure(delegate
        {
            db.Execute(
                "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day) VALUES (10, 5, '2026-01-01');", null);
        }, "会话结束时间早于开始时间应被拒绝");

        // 四种合法来源都必须放行。
        string[] allowed = new string[] { "none", "default_rule", "user_override", "ai_cached" };
        for (int i = 0; i < allowed.Length; i++)
        {
            db.Execute(
                "INSERT INTO window_daily_agg (day, process_key, window_title, category_source) VALUES (?,?,?,?);",
                new object[] { "2026-01-0" + (i + 1), "p" + i, "t" + i, allowed[i] });
        }
        Expect(db.ScalarLong("SELECT COUNT(*) FROM window_daily_agg;", null), 4, "四种合法来源应全部写入");
        db.Execute("DELETE FROM window_daily_agg;", null);
    }

    /// <summary>5c. 事务辅助方法:成功提交,异常回滚。</summary>
    private static void CheckTransaction(KmsDb db)
    {
        db.RunInTransaction(delegate(KmsDb tx)
        {
            tx.Execute("INSERT INTO month_archive (ym, days) VALUES ('2026-01', 3);", null);
        });
        Expect(db.ScalarLong("SELECT COUNT(*) FROM month_archive WHERE ym='2026-01';", null), 1, "事务提交后应可见");

        bool threw = false;
        try
        {
            db.RunInTransaction(delegate(KmsDb tx)
            {
                tx.Execute("INSERT INTO month_archive (ym, days) VALUES ('2026-02', 4);", null);
                throw new InvalidOperationException("故意失败以验证回滚");
            });
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Check(threw, "事务体抛出的异常应原样冒泡");
        Expect(db.ScalarLong("SELECT COUNT(*) FROM month_archive WHERE ym='2026-02';", null), 0, "回滚后不应留下行");
        Check(!db.InTransaction, "回滚后不应仍处于事务中");

        // 主键冲突必须报错。
        ExpectFailure(delegate
        {
            db.Execute("INSERT INTO month_archive (ym, days) VALUES ('2026-01', 9);", null);
        }, "month_archive 主键冲突应报错");

        db.Execute("DELETE FROM month_archive;", null);
    }

    /// <summary>5d. foreign_keys=ON 必须真的生效。</summary>
    private static void CheckForeignKeys(KmsDb db)
    {
        ExpectFailure(delegate
        {
            db.Execute(
                "INSERT INTO events_input_burst (session_id, start_ts_ms, end_ts_ms, day, key_count) " +
                "VALUES (999999, 1, 2, '2026-11-16', 10);", null);
        }, "外键应拦住不存在的 session_id");

        // 合法外键应通过。
        db.Execute(
            "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day) VALUES (1, 2, '2026-11-16');", null);
        long sessionId = db.LastInsertRowId();
        db.Execute(
            "INSERT INTO events_input_burst (session_id, start_ts_ms, end_ts_ms, day, key_count) " +
            "VALUES (?, 1, 2, '2026-11-16', 10);", new object[] { sessionId });
        Expect(db.ScalarLong("SELECT COUNT(*) FROM events_input_burst;", null), 1, "合法外键应写入");

        // ON DELETE SET NULL 必须生效。
        db.Execute("DELETE FROM events_window_session WHERE id=?;", new object[] { sessionId });
        Expect(db.ScalarLong("SELECT COUNT(*) FROM events_input_burst WHERE session_id IS NULL;", null), 1,
            "删除会话应把 session_id 置空");

        db.Execute("DELETE FROM events_input_burst;", null);
        db.Execute("DELETE FROM events_window_session;", null);
    }

    /// <summary>5e. 只读连接与写连接分离:读得到、写不了。</summary>
    private static void CheckReaderSeparation(string dbPath)
    {
        KmsDb reader = KmsDb.OpenReader(dbPath);
        try
        {
            Check(reader.IsReadOnly, "OpenReader 应标记为只读");
            Expect(reader.ScalarLong(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", null),
                KmsSchema.RequiredTables.Length, "只读连接应能查询表数量");

            ExpectFailure(delegate
            {
                reader.Execute("INSERT INTO meta (k, v) VALUES ('x', 'y');", null);
            }, "只读连接必须拒绝写入");
        }
        finally
        {
            reader.Dispose();
        }
    }

    /// <summary>5a. 写入 → 读回:单写者(store)+ 只读连接校验,自增 id 与列值正确。</summary>
    private static void CheckRoundTrip(string sandbox)
    {
        SqliteStatsStore store = new SqliteStatsStore();
        Check(store.Initialize(Path.Combine(sandbox, "roundtrip")),
            "SqliteStatsStore 应初始化成功:" + store.Diagnosis);

        KmsDb reader = null;
        try
        {
            reader = KmsDb.OpenReader(store.DatabasePath);
            Check(reader.IsReadOnly, "校验用的连接应为只读");
            Expect(reader.ScalarLong("SELECT COUNT(*) FROM meta;", null) >= 6 ? 1 : 0, 1, "只读连接应读到 meta");

            WindowSessionRow row = new WindowSessionRow();
            row.StartMs = 1700000000000L;
            row.EndMs = 1700000060000L;
            row.Day = "2026-11-16";
            row.ProcessPath = @"C:\Apps\Code.exe";
            row.ProcessName = "Code.exe";
            row.WindowTitle = "KeyMouseStats - Visual Studio Code";
            row.KeyCount = 1234;
            row.LeftClicks = 56;
            row.RightClicks = 7;
            row.MiddleClicks = 2;
            row.XClicks = 1;
            row.Wheel = 9;
            row.MovePx = 4567.5;
            row.RimeChars = 88;
            row.ActiveMs = 60000;
            row.CategoryId = 0;
            row.CategorySource = "default_rule";
            row.IsOpen = 0;

            long id = store.InsertWindowSession(row);
            Check(id > 0, "应返回自增 id,实际=" + id);

            string title = reader.ScalarText("SELECT window_title FROM events_window_session WHERE id=?;",
                new object[] { id });
            Check(string.Equals(title, row.WindowTitle, StringComparison.Ordinal),
                "读回的窗口标题应与写入一致,实际=" + title);

            Expect(reader.ScalarLong("SELECT key_count FROM events_window_session WHERE id=?;", new object[] { id }),
                1234, "key_count 往返");
            Expect(reader.ScalarLong("SELECT left_clicks FROM events_window_session WHERE id=?;", new object[] { id }),
                56, "left_clicks 往返");
            string source = reader.ScalarText("SELECT category_source FROM events_window_session WHERE id=?;",
                new object[] { id });
            Check(string.Equals(source, "default_rule", StringComparison.Ordinal), "category_source 往返");
            string path = reader.ScalarText("SELECT process_path FROM events_window_session WHERE id=?;",
                new object[] { id });
            Check(string.Equals(path, row.ProcessPath, StringComparison.Ordinal), "process_path 往返");
        }
        finally
        {
            if (reader != null) reader.Dispose();
            store.Shutdown();
        }
    }

    /// <summary>5b. 热区格子按 (day, 通道, x, y) 累加,不同通道互不干扰。</summary>
    private static void CheckClickGrid(string sandbox)
    {
        SqliteStatsStore store = new SqliteStatsStore();
        Check(store.Initialize(Path.Combine(sandbox, "grid")), "热区库应初始化成功:" + store.Diagnosis);

        KmsDb reader = null;
        try
        {
            reader = KmsDb.OpenReader(store.DatabasePath);

            ClickCellRow[] first = new ClickCellRow[]
            {
                MakeCell(1, 12, 34, 3),
                MakeCell(2, 12, 34, 4)
            };
            ClickCellRow[] second = new ClickCellRow[]
            {
                MakeCell(1, 12, 34, 5),
                MakeCell(1, 13, 34, 2)
            };

            Expect(store.MergeClickCells("2026-11-16", first), 2, "首次合并格子数");
            Expect(store.MergeClickCells("2026-11-16", second), 2, "第二次合并格子数");

            // 通道 1 的 (12,34) 应累加为 3+5=8;通道 2 保持 4;新增格子 (13,34)=2。
            Expect(reader.ScalarLong(
                "SELECT count FROM daily_click_grid WHERE day='2026-11-16' AND channel=1 AND cell_x=12 AND cell_y=34;",
                null), 8, "同通道同格应累加");
            Expect(reader.ScalarLong(
                "SELECT count FROM daily_click_grid WHERE day='2026-11-16' AND channel=2 AND cell_x=12 AND cell_y=34;",
                null), 4, "不同通道必须独立");
            Expect(reader.ScalarLong(
                "SELECT count FROM daily_click_grid WHERE day='2026-11-16' AND channel=1 AND cell_x=13 AND cell_y=34;",
                null), 2, "新增格子应写入");
            Expect(reader.ScalarLong("SELECT COUNT(*) FROM daily_click_grid;", null), 3, "格子去重后的行数");

            // 负坐标(多屏左侧)必须能存能取。
            store.MergeClickCells("2026-11-16", new ClickCellRow[] { MakeCell(1, -40, -12, 1) });
            Expect(reader.ScalarLong(
                "SELECT count FROM daily_click_grid WHERE cell_x=-40 AND cell_y=-12;", null), 1, "负坐标格子应可写入");
        }
        finally
        {
            if (reader != null) reader.Dispose();
            store.Shutdown();
        }
    }

    private static ClickCellRow MakeCell(int channel, int x, int y, long count)
    {
        ClickCellRow cell = new ClickCellRow();
        cell.Channel = channel;
        cell.CellX = x;
        cell.CellY = y;
        cell.ScreenId = 0;
        cell.Count = count;
        return cell;
    }
}
