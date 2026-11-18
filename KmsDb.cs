// ============================================================================
//  键鼠统计 v3.0  Milestone 1  KmsDb.cs
//  SQLite 时序事件流引擎的底层封装:P/Invoke 层 + 连接 + 语句 + 事务 + 后端实现。
//
//  为什么不用 System.Data.SQLite / Microsoft.Data.Sqlite:
//   本项目用裸 csc.exe 编译、无 MSBuild/NuGet、且 CI 要求清爽版 ≤2 MiB 且零内嵌
//   托管资源。调研结论(见 docs/internal/planning/v3-milestone1-datastore.md):
//     · System.Data.SQLite 在离线环境不可获得;
//     · Microsoft.Data.Sqlite 的 netstandard2.0 门面链缺 System.Text.Encodings.Web
//       与 System.Runtime.CompilerServices.Unsafe,在本机不可解析。
//   因此直接对内嵌的 e_sqlite3.dll 做 P/Invoke。收益:
//     · 零第三方托管程序集,零门面链,天然满足 C# 5 / 裸 csc;
//     · LoadLibrary 与降级时机完全由我们掌握(见 NativeBoot.cs)。
//  引擎版本:SQLite 3.41.2(SQLitePCLRaw.lib.e_sqlite3 2.1.6,公有领域)。
//
//  连接模型(硬约定):
//     · **单写者**:一个进程只允许一个 OpenWriter 连接。SQLite 本身也只允许一个写者,
//       在应用层先收口,就不会出现 SQLITE_BUSY 重试风暴。
//     · **读写分离**:UI 查询走 OpenReader(OPEN_READONLY)。WAL 下读不阻塞写。
//     · KmsDb 不是线程安全的:连接归它的所有者线程独占,不共享。
//
//  CLEAN_EDITION 下本文件整体不参与编译(清爽版没有 SQLite)。
// ============================================================================

#if !CLEAN_EDITION

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyMouseStats
{
    /// <summary>数据库层的统一异常,带 SQL 片段与返回码,便于诊断。</summary>
    internal sealed class KmsDbException : Exception
    {
        internal KmsDbException(string message) : base(message) { }
    }

    /// <summary>e_sqlite3.dll 的原始入口。全部 Cdecl,全部 UTF-8。</summary>
    internal static class Sqlite
    {
        internal const int OK = 0;
        internal const int ROW = 100;
        internal const int DONE = 101;

        internal const int OPEN_READONLY = 0x00000001;
        internal const int OPEN_READWRITE = 0x00000002;
        internal const int OPEN_CREATE = 0x00000004;
        internal const int OPEN_NOMUTEX = 0x00008000;

        /// <summary>SQLITE_TRANSIENT:让 SQLite 自己复制一份绑定数据。</summary>
        internal static readonly IntPtr Transient = new IntPtr(-1);

        private const string Dll = "e_sqlite3.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(byte[] fileName, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close_v2(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, IntPtr errMsg);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int byteCount, out IntPtr stmt, IntPtr tail);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_reset(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_clear_bindings(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int byteCount, IntPtr destructor);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_null(IntPtr stmt, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr stmt, int column);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern double sqlite3_column_double(IntPtr stmt, int column);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes(IntPtr stmt, int column);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_type(IntPtr stmt, int column);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_last_insert_rowid(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_changes(IntPtr db);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_libversion();

        /// <summary>本机引擎版本号,例如 "3.41.2"。用于诊断与测试。</summary>
        internal static string LibVersion()
        {
            return PtrToUtf8(sqlite3_libversion());
        }

        /// <summary>UTF-8 字符串 → 带结尾 NUL 的字节数组。sqlite3 全程 UTF-8。</summary>
        internal static byte[] Utf8Z(string value)
        {
            if (value == null) value = "";
            byte[] raw = Encoding.UTF8.GetBytes(value);
            byte[] buffer = new byte[raw.Length + 1];
            Buffer.BlockCopy(raw, 0, buffer, 0, raw.Length);
            buffer[raw.Length] = 0;
            return buffer;
        }

        /// <summary>读一个不受托管 GC 管理的 UTF-8 指针(sqlite3 的返回值)。</summary>
        internal static string PtrToUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return "";
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0) length++;
            if (length == 0) return "";
            byte[] buffer = new byte[length];
            Marshal.Copy(pointer, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);
        }
    }

    /// <summary>一条预编译语句。生命周期由 using 管理,Dispose 时 finalize。</summary>
    internal sealed class KmsStmt : IDisposable
    {
        private readonly KmsDb _owner;
        private IntPtr _stmt;

        internal KmsStmt(KmsDb owner, IntPtr stmt)
        {
            _owner = owner;
            _stmt = stmt;
        }

        internal IntPtr Raw { get { return _stmt; } }

        internal void Bind(object[] args)
        {
            if (args == null || args.Length == 0) return;
            for (int i = 0; i < args.Length; i++) BindOne(i + 1, args[i]);
        }

        private void BindOne(int index, object value)
        {
            int rc;
            if (value == null)
            {
                rc = Sqlite.sqlite3_bind_null(_stmt, index);
            }
            else if (value is long || value is int || value is short || value is byte)
            {
                rc = Sqlite.sqlite3_bind_int64(_stmt, index, Convert.ToInt64(value));
            }
            else if (value is double || value is float)
            {
                rc = Sqlite.sqlite3_bind_double(_stmt, index, Convert.ToDouble(value));
            }
            else if (value is string)
            {
                rc = Sqlite.sqlite3_bind_text(_stmt, index, Sqlite.Utf8Z((string)value), -1, Sqlite.Transient);
            }
            else if (value is bool)
            {
                rc = Sqlite.sqlite3_bind_int64(_stmt, index, ((bool)value) ? 1L : 0L);
            }
            else
            {
                rc = Sqlite.sqlite3_bind_text(_stmt, index, Sqlite.Utf8Z(Convert.ToString(value)), -1, Sqlite.Transient);
            }
            if (rc != Sqlite.OK) throw _owner.Failure("绑定第 " + index + " 个参数", rc);
        }

        /// <summary>推进一行。返回 true 表示有行(ROW),false 表示结束(DONE)。</summary>
        internal bool Step()
        {
            int rc = Sqlite.sqlite3_step(_stmt);
            if (rc == Sqlite.ROW) return true;
            if (rc == Sqlite.DONE) return false;
            throw _owner.Failure("执行语句", rc);
        }

        /// <summary>执行一条不返回结果集的语句,并断言它走到 DONE。</summary>
        internal void StepDone()
        {
            if (Step()) throw new KmsDbException("语句本应不返回结果集,却返回了行");
        }

        internal long ColumnLong(int column)
        {
            return Sqlite.sqlite3_column_int64(_stmt, column);
        }

        internal double ColumnDouble(int column)
        {
            return Sqlite.sqlite3_column_double(_stmt, column);
        }

        internal string ColumnText(int column)
        {
            IntPtr pointer = Sqlite.sqlite3_column_text(_stmt, column);
            if (pointer == IntPtr.Zero) return null;
            int bytes = Sqlite.sqlite3_column_bytes(_stmt, column);
            if (bytes <= 0) return "";
            byte[] buffer = new byte[bytes];
            Marshal.Copy(pointer, buffer, 0, bytes);
            return Encoding.UTF8.GetString(buffer);
        }

        internal int ColumnCount()
        {
            return Sqlite.sqlite3_column_count(_stmt);
        }

        internal void Reset()
        {
            Sqlite.sqlite3_reset(_stmt);
            Sqlite.sqlite3_clear_bindings(_stmt);
        }

        public void Dispose()
        {
            if (_stmt == IntPtr.Zero) return;
            try { Sqlite.sqlite3_finalize(_stmt); }
            catch { }
            _stmt = IntPtr.Zero;
        }
    }

    /// <summary>一个 SQLite 连接。单写者约定见文件头。</summary>
    internal sealed class KmsDb : IDisposable
    {
        private IntPtr _db;
        private readonly bool _readOnly;
        private readonly string _path;
        private bool _inTransaction;

        private KmsDb(IntPtr db, string path, bool readOnly)
        {
            _db = db;
            _path = path;
            _readOnly = readOnly;
        }

        internal string Path { get { return _path; } }
        internal bool IsReadOnly { get { return _readOnly; } }
        internal bool InTransaction { get { return _inTransaction; } }

        /// <summary>打开(或创建)可写连接。这是全进程唯一的写者。</summary>
        internal static KmsDb OpenWriter(string path)
        {
            return Open(path, false);
        }

        /// <summary>打开只读连接,供 UI 查询使用。</summary>
        internal static KmsDb OpenReader(string path)
        {
            return Open(path, true);
        }


        // ---------------------------------------------------------------- 只读查询(Milestone 3)

        /// <summary>
        /// 按日读取窗口会话,供心流引擎消费。只读连接,按 (start_ts_ms, id) 升序 ——
        /// 与 DbWriter 的写入顺序一致,保证分析结果可复现。
        /// </summary>
        internal List<FlowSessionRow> ReadSessionsForDay(string day)
        {
            List<FlowSessionRow> rows = new List<FlowSessionRow>();
            using (KmsStmt statement = Prepare(
                "SELECT id, start_ts_ms, end_ts_ms, process_path, process_name, window_title, " +
                "key_count, category_id, active_ms FROM events_window_session " +
                "WHERE day = ? ORDER BY start_ts_ms ASC, id ASC;"))
            {
                statement.Bind(new object[] { day });
                while (statement.Step())
                {
                    FlowSessionRow row = new FlowSessionRow();
                    row.Id = statement.ColumnLong(0);
                    row.StartMs = statement.ColumnLong(1);
                    row.EndMs = statement.ColumnLong(2);
                    row.ProcessPath = statement.ColumnText(3);
                    row.ProcessName = statement.ColumnText(4);
                    row.WindowTitle = statement.ColumnText(5);
                    row.KeyCount = statement.ColumnLong(6);
                    row.CategoryId = (int)statement.ColumnLong(7);
                    row.ActiveMs = statement.ColumnLong(8);
                    rows.Add(row);
                }
            }
            return rows;
        }

        /// <summary>按日读取输入突发段,按 (start_ts_ms, id) 升序。</summary>
        internal List<FlowBurstRow> ReadBurstsForDay(string day)
        {
            List<FlowBurstRow> rows = new List<FlowBurstRow>();
            using (KmsStmt statement = Prepare(
                "SELECT id, start_ts_ms, end_ts_ms, key_count, iki_avg_ms, iki_var, " +
                "backspace_rate, switch_count, process_path, category_id FROM events_input_burst " +
                "WHERE day = ? ORDER BY start_ts_ms ASC, id ASC;"))
            {
                statement.Bind(new object[] { day });
                while (statement.Step())
                {
                    FlowBurstRow row = new FlowBurstRow();
                    row.Id = statement.ColumnLong(0);
                    row.StartMs = statement.ColumnLong(1);
                    row.EndMs = statement.ColumnLong(2);
                    row.KeyCount = statement.ColumnLong(3);
                    row.IkiAvgMs = statement.ColumnDouble(4);
                    row.IkiVar = statement.ColumnDouble(5);
                    row.BackspaceRate = statement.ColumnDouble(6);
                    row.SwitchCount = statement.ColumnLong(7);
                    row.ProcessPath = statement.ColumnText(8);
                    row.CategoryId = (int)statement.ColumnLong(9);
                    rows.Add(row);
                }
            }
            return rows;
        }

        /// <summary>
        /// 批量回填 semantic_tag('deep' | 'normal' | 'fragmented')。
        /// 幂等:只更新值确实不同的行;返回真正改动的行数。单事务提交,失败回滚。
        /// </summary>
        internal static int UpdateSessionSemanticTags(string path, IDictionary<long, string> tags)
        {
            if (string.IsNullOrEmpty(path) || tags == null || tags.Count == 0) return 0;
            int changed = 0;
            bool inTransaction = false;
            using (KmsDb db = Open(path, false))
            {
                try
                {
                    db.Execute("BEGIN;", null);
                    inTransaction = true;
                    using (KmsStmt statement = db.Prepare(
                        "UPDATE events_window_session SET semantic_tag=? WHERE id=? AND semantic_tag<>?;"))
                    {
                        foreach (KeyValuePair<long, string> pair in tags)
                        {
                            string tag = pair.Value ?? "";
                            statement.Reset();
                            statement.Bind(new object[] { tag, pair.Key, tag });
                            changed += statement.Step() ? 1 : 0;
                        }
                    }
                    db.Execute("COMMIT;", null);
                    inTransaction = false;
                }
                catch
                {
                    if (inTransaction)
                    {
                        try { db.Execute("ROLLBACK;", null); } catch { }
                    }
                    throw;
                }
            }
            return changed;
        }

        /// <summary>
        /// .kms 增量包的跨进程幂等合并。
        /// 账本 kms_import_log 是唯一的仲裁者:INSERT OR IGNORE 命中主键(已导入)→ 跳过;
        /// 只有真正插入成功(受影响行数 == 1)的记录才会写进 daily_summary / events_window_session。
        /// 因此同一个包无论导入多少次、中间重启多少次,结果都是 100% 恒等。
        /// </summary>
        internal static KmsMergeResult MergeKmsPackage(string path, string packageId, KmsPackageData package)
        {
            KmsMergeResult result = new KmsMergeResult();
            if (string.IsNullOrEmpty(path) || package == null) return result;
            string id = string.IsNullOrEmpty(packageId) ? "unknown" : packageId;
            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            using (KmsDb db = Open(path, false))
            {
                KmsSchema.Ensure(db);
                db.RunInTransaction(delegate(KmsDb tx)
                {
                    for (int i = 0; i < package.Days.Count; i++)
                    {
                        KmsDayRecord day = package.Days[i];
                        int applied = tx.Execute(
                            "INSERT OR IGNORE INTO kms_import_log (package_id, record_key, content_hash, imported_ms) VALUES (?,?,?,?);",
                            new object[] { id, day.RecordKey, day.ContentHash(), now });
                        if (applied == 1)
                        {
                            tx.Execute(
                                "INSERT OR IGNORE INTO daily_summary (day, keys, clicks, wheel, active_seconds) VALUES (?,?,?,?,?);",
                                new object[] { day.Day, day.Keys, day.Clicks, day.Wheel, (double)day.ActiveSeconds });
                            result.DaysApplied++;
                        }
                        else result.DaysSkipped++;
                    }
                    for (int i = 0; i < package.Sessions.Count; i++)
                    {
                        KmsSessionRecord session = package.Sessions[i];
                        int applied = tx.Execute(
                            "INSERT OR IGNORE INTO kms_import_log (package_id, record_key, content_hash, imported_ms) VALUES (?,?,?,?);",
                            new object[] { id, session.RecordKey, session.ContentHash(), now });
                        if (applied == 1)
                        {
                            tx.Execute(
                                "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, process_path, " +
                                "process_name, window_title, key_count, category_id, category_source, is_open) " +
                                "VALUES (?,?,?,?,?,?,?,?,'none',0);",
                                new object[]
                                {
                                    session.StartMs, session.EndMs, session.Day,
                                    session.ProcessPath ?? "", session.ProcessName ?? "",
                                    session.WindowTitle ?? "", session.KeyCount, session.CategoryId
                                });
                            result.SessionsApplied++;
                        }
                        else result.SessionsSkipped++;
                    }
                });
            }
            return result;
        }
        private static KmsDb Open(string path, bool readOnly)
        {
            if (string.IsNullOrEmpty(path)) throw new KmsDbException("数据库路径为空");
            if (!NativeBoot.TryEnsure())
                throw new KmsDbException("原生 SQLite 引擎不可用: " + NativeBoot.Diagnosis);

            if (!readOnly)
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }

            int flags = readOnly
                ? (Sqlite.OPEN_READONLY | Sqlite.OPEN_NOMUTEX)
                : (Sqlite.OPEN_READWRITE | Sqlite.OPEN_CREATE | Sqlite.OPEN_NOMUTEX);

            IntPtr handle;
            int rc = Sqlite.sqlite3_open_v2(Sqlite.Utf8Z(path), out handle, flags, IntPtr.Zero);
            if (rc != Sqlite.OK)
            {
                string message = handle != IntPtr.Zero ? Sqlite.PtrToUtf8(Sqlite.sqlite3_errmsg(handle)) : "rc=" + rc;
                if (handle != IntPtr.Zero) Sqlite.sqlite3_close_v2(handle);
                throw new KmsDbException("打开数据库失败(" + rc + "): " + message + " · path=" + path);
            }

            KmsDb connection = new KmsDb(handle, path, readOnly);
            try
            {
                connection.ApplyPragmas();
            }
            catch
            {
                connection.Dispose();
                throw;
            }
            return connection;
        }

        /// <summary>
        /// 初始化时必须显式执行的 PRAGMA 组合:
        ///   journal_mode=WAL      持久化在库头,掉电最多丢最后一批事务但库不损坏;
        ///   synchronous=FULL      每次提交都 fsync —— 拍板选 FULL,零丢失优先;
        ///   foreign_keys=ON       SQLite 默认是关的,必须每个连接显式打开;
        ///   busy_timeout=5000     写者唯一,这里只是兜底;
        ///   temp_store=MEMORY     临时表不进磁盘。
        /// </summary>
        private void ApplyPragmas()
        {
            if (!_readOnly) Execute("PRAGMA journal_mode = WAL;");
            Execute("PRAGMA synchronous = FULL;");
            Execute("PRAGMA foreign_keys = ON;");
            Execute("PRAGMA busy_timeout = 5000;");
            Execute("PRAGMA temp_store = MEMORY;");
        }

        /// <summary>执行一段(可含多条)SQL。用于 DDL 与 PRAGMA。</summary>
        internal int Execute(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return 0;
            int rc = Sqlite.sqlite3_exec(_db, Sqlite.Utf8Z(sql), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (rc != Sqlite.OK) throw Failure("执行 SQL", rc, sql);
            return Sqlite.sqlite3_changes(_db);
        }

        /// <summary>执行一条带参数的语句,返回受影响行数。</summary>
        internal int Execute(string sql, object[] args)
        {
            using (KmsStmt statement = Prepare(sql))
            {
                statement.Bind(args);
                statement.StepDone();
                return Sqlite.sqlite3_changes(_db);
            }
        }

        internal KmsStmt Prepare(string sql)
        {
            if (string.IsNullOrEmpty(sql)) throw new KmsDbException("SQL 为空");
            IntPtr statement;
            int rc = Sqlite.sqlite3_prepare_v2(_db, Sqlite.Utf8Z(sql), -1, out statement, IntPtr.Zero);
            if (rc != Sqlite.OK) throw Failure("预编译 SQL", rc, sql);
            if (statement == IntPtr.Zero) throw new KmsDbException("SQL 没有可执行内容: " + Shorten(sql));
            return new KmsStmt(this, statement);
        }

        internal long ScalarLong(string sql, object[] args)
        {
            using (KmsStmt statement = Prepare(sql))
            {
                statement.Bind(args);
                if (!statement.Step()) return 0;
                return statement.ColumnLong(0);
            }
        }

        internal string ScalarText(string sql, object[] args)
        {
            using (KmsStmt statement = Prepare(sql))
            {
                statement.Bind(args);
                if (!statement.Step()) return null;
                return statement.ColumnText(0);
            }
        }

        internal bool TableExists(string table)
        {
            long count = ScalarLong(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?",
                new object[] { table });
            return count > 0;
        }

        internal long LastInsertRowId()
        {
            return Sqlite.sqlite3_last_insert_rowid(_db);
        }

        internal string LastError()
        {
            return Sqlite.PtrToUtf8(Sqlite.sqlite3_errmsg(_db));
        }

        internal void Begin()
        {
            if (_inTransaction) throw new KmsDbException("事务已开启,不支持嵌套");
            Execute("BEGIN IMMEDIATE;");
            _inTransaction = true;
        }

        internal void Commit()
        {
            if (!_inTransaction) return;
            Execute("COMMIT;");
            _inTransaction = false;
        }

        internal void Rollback()
        {
            if (!_inTransaction) return;
            try { Execute("ROLLBACK;"); }
            catch { }
            finally { _inTransaction = false; }
        }

        /// <summary>事务提交辅助:自动 BEGIN / COMMIT / ROLLBACK。</summary>
        internal void RunInTransaction(Action<KmsDb> work)
        {
            if (work == null) throw new ArgumentNullException("work");
            Begin();
            try
            {
                work(this);
                Commit();
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        internal KmsDbException Failure(string what, int code, string sql)
        {
            string message = what + "失败(" + code + "): " + LastError();
            if (!string.IsNullOrEmpty(sql)) message = message + " · SQL=" + Shorten(sql);
            return new KmsDbException(message);
        }

        internal KmsDbException Failure(string what, int code)
        {
            return Failure(what, code, null);
        }

        private static string Shorten(string sql)
        {
            if (sql == null) return "";
            string flat = sql.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 120 ? flat : flat.Substring(0, 120) + "…";
        }

        public void Dispose()
        {
            if (_inTransaction)
            {
                try { Rollback(); }
                catch { }
            }
            if (_db == IntPtr.Zero) return;
            try { Sqlite.sqlite3_close_v2(_db); }
            catch { }
            _db = IntPtr.Zero;
        }
    }

    /// <summary>
    /// IStatsStore 的 SQLite 实现。持有单写者连接,并在初始化时落地 schema。
    /// </summary>
    internal sealed class SqliteStatsStore : IStatsStore
    {
        private KmsDb _writer;
        private string _dbPath = "";
        private string _dataRoot = "";
        private bool _available;
        private string _diagnosis = "未初始化";

        public string BackendId { get { return "sqlite"; } }
        public bool Available { get { return _available; } }
        public string Diagnosis { get { return _diagnosis; } }
        public int SchemaRev { get { return _available ? KmsSchema.Rev : 0; } }

        /// <summary>库文件完整路径。供诊断与测试开只读连接校验写入结果。</summary>
        internal string DatabasePath { get { return _dbPath; } }

        public bool Initialize(string dataRoot)
        {
            try
            {
                _dataRoot = dataRoot == null ? "" : dataRoot;
                if (_dataRoot.Length == 0) throw new KmsDbException("数据根目录为空");
                Directory.CreateDirectory(_dataRoot);
                _dbPath = System.IO.Path.Combine(_dataRoot, KmsSchema.DbFileName);
                _writer = KmsDb.OpenWriter(_dbPath);
                KmsSchema.Ensure(_writer);
                _available = true;
                _diagnosis = "SQLite 引擎 " + Sqlite.LibVersion() + " · schema_rev=" + KmsSchema.Rev + " · db=" + _dbPath;
                AppendDiagnostic(_diagnosis);
                return true;
            }
            catch (Exception error)
            {
                _available = false;
                _diagnosis = error.GetType().Name + " · " + error.Message;
                AppendDiagnostic("初始化失败: " + _diagnosis);
                return false;
            }
        }

        public void Shutdown()
        {
            if (_writer != null)
            {
                try { _writer.Dispose(); }
                catch { }
                _writer = null;
            }
            _available = false;
        }

        public bool HealthCheck(out string detail)
        {
            if (_writer == null)
            {
                detail = "连接未建立";
                return false;
            }
            try
            {
                string mode = _writer.ScalarText("PRAGMA journal_mode;", null);
                long tables = _writer.ScalarLong(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';",
                    null);
                detail = "journal_mode=" + mode + " · tables=" + tables + " · db=" + _dbPath;
                return string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase) && tables >= KmsSchema.RequiredTables.Length;
            }
            catch (Exception error)
            {
                detail = error.GetType().Name + " · " + error.Message;
                return false;
            }
        }

        public long InsertWindowSession(WindowSessionRow row)
        {
            if (row == null) return -1;
            if (_writer == null) return -1;
            const string sql =
                "INSERT INTO events_window_session (" +
                "start_ts_ms, end_ts_ms, day, process_path, process_name, window_title, " +
                "key_count, left_clicks, right_clicks, middle_clicks, x_clicks, wheel, move_px, " +
                "rime_chars, active_ms, category_id, category_source, semantic_tag, is_open" +
                ") VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?);";
            object[] args = new object[]
            {
                row.StartMs, row.EndMs, Or(row.Day, ""), Or(row.ProcessPath, ""),
                Or(row.ProcessName, ""), Or(row.WindowTitle, ""), row.KeyCount,
                row.LeftClicks, row.RightClicks, row.MiddleClicks, row.XClicks, row.Wheel,
                row.MovePx, row.RimeChars, row.ActiveMs, row.CategoryId,
                Or(row.CategorySource, "none"), Or(row.SemanticTag, ""), row.IsOpen
            };
            RunWrite(delegate { _writer.Execute(sql, args); });
            return _writer.LastInsertRowId();
        }

        public int MergeClickCells(string day, ClickCellRow[] cells)
        {
            if (_writer == null || string.IsNullOrEmpty(day) || cells == null) return 0;
            int merged = 0;
            RunWrite(delegate
            {
                _writer.Begin();
                try
                {
                    for (int i = 0; i < cells.Length; i++)
                    {
                        ClickCellRow cell = cells[i];
                        if (cell == null || cell.Count <= 0) continue;
                        // 先 UPDATE 再 INSERT,而不是 UPSERT:向上兼容 3.24 之前的引擎。
                        int changed = _writer.Execute(
                            "UPDATE daily_click_grid SET count = count + ?, screen_id = ? " +
                            "WHERE day=? AND channel=? AND cell_x=? AND cell_y=?;",
                            new object[] { cell.Count, cell.ScreenId, day, cell.Channel, cell.CellX, cell.CellY });
                        if (changed == 0)
                        {
                            _writer.Execute(
                                "INSERT INTO daily_click_grid (day, channel, cell_x, cell_y, screen_id, count) " +
                                "VALUES (?,?,?,?,?,?);",
                                new object[] { day, cell.Channel, cell.CellX, cell.CellY, cell.ScreenId, cell.Count });
                        }
                        merged++;
                    }
                    _writer.Commit();
                }
                catch
                {
                    _writer.Rollback();
                    throw;
                }
            });
            return merged;
        }

        private void RunWrite(Action write)
        {
            try { write(); }
            catch (KmsDbException) { throw; }
            catch (Exception error)
            {
                throw new KmsDbException("写入失败: " + error.GetType().Name + " · " + error.Message);
            }
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        /// <summary>把诊断追加到数据根下的 db-diagnostics.log(测试沙盒内安全)。</summary>
        private void AppendDiagnostic(string message)
        {
            if (string.IsNullOrEmpty(_dataRoot)) return;
            try
            {
                string path = System.IO.Path.Combine(_dataRoot, "db-diagnostics.log");
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // 诊断日志写不进去不是错误,不能因此影响主流程。
            }
        }
    }
}

#endif
