using System.Globalization;
using Kms.Core.Time;
using Kms.Core.Input;
using Kms.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Kms.Storage.Tests;

/// <summary>
/// v4 <see cref="DbWriter"/> 与 v3 写线程的口径对拍：
/// 3 个会话、2 个突发段、滚轮事件计数、窗口标题、点击网格、13 张表、重复 Stop 幂等。
/// </summary>
public sealed class DbWriterParityTests
{
    // 2023-11-14 22:13:20 UTC；本地日由系统时区决定，测试里动态计算。
    private const long T0 = 1_700_000_000_000L;

    private static readonly IntPtr HwndCode = new(0x101);
    private static readonly IntPtr HwndChrome = new(0x202);
    private static readonly IntPtr HwndNotepad = new(0x303);

    [Fact]
    public void Writer_PreservesSessionBurstWheelAndDailySemantics()
    {
        string directory = NewDatabaseDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        string expectedDay = UnixTime.ToLocal(T0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        try
        {
            EventRing ring = new(8192);
            DbWriter writer = DbWriter.Open(
                dbPath,
                ring,
                new DbWriterOptions
                {
                    RunSelfHeal = false,
                    Diagnostic = static _ => { }
                });

            try
            {
                writer.ResolveWindow = ResolveWindow;

                // 入口冒烟：字段形式的 Enqueue 与 Ring 连通，随后取走，不污染时间线。
                Assert.True(writer.Enqueue(
                    InputEventKind.KeyDown,
                    HwndCode,
                    65,
                    0,
                    0,
                    0,
                    writer.TicksForMs(T0)));
                Assert.Equal(1, ring.PendingCount);
                Assert.True(ring.TryDequeue(out RawInputEvent probe));
                Assert.Equal(65, probe.VkOrButton);

                // 会话 1：Code，3 键（含 1 个退格）+ 左 2 / 右 1 / 滚轮 1。
                Feed(writer, ring, T0 + 0, InputEventKind.KeyDown, HwndCode, 65);
                Feed(writer, ring, T0 + 500, InputEventKind.KeyDown, HwndCode, 65);
                Feed(writer, ring, T0 + 1_500, InputEventKind.KeyDown, HwndCode, 0x08);
                Feed(writer, ring, T0 + 2_000, InputEventKind.MouseDown, HwndCode, 1, 100, 200);
                Feed(writer, ring, T0 + 2_100, InputEventKind.MouseDown, HwndCode, 1, 132, 232);
                Feed(writer, ring, T0 + 2_200, InputEventKind.MouseDown, HwndCode, 2, 300, 400);
                // v3 库存的是事件次数：两个 60 的增量仍是 2 次，而不是 120 结算后的 1 次。
                Feed(writer, ring, T0 + 2_300, InputEventKind.Wheel, HwndCode, 0, 500, 600, 60);

                // 会话 2：Chrome，3 键；空闲 60.7 s 后切到会话 3。
                Feed(writer, ring, T0 + 3_000, InputEventKind.KeyDown, HwndChrome, 65);
                Feed(writer, ring, T0 + 3_600, InputEventKind.KeyDown, HwndChrome, 65);
                Feed(writer, ring, T0 + 4_300, InputEventKind.KeyDown, HwndChrome, 65);

                // 会话 3：Notepad，1 键 + 滚轮 + 中键。
                Feed(writer, ring, T0 + 65_000, InputEventKind.KeyDown, HwndNotepad, 65);
                Feed(writer, ring, T0 + 65_200, InputEventKind.Wheel, HwndNotepad, 0, 500, 600, 60);
                Feed(writer, ring, T0 + 65_400, InputEventKind.MouseDown, HwndNotepad, 3, 500, 600);

                Assert.Equal(13, writer.DrainOnce());
                Assert.Equal(0, ring.PendingCount);

                // 线程路径也走一遍，并验证重复 Stop 幂等。
                writer.Start();
                Assert.True(writer.Flush(2_000));
                writer.Stop();
                writer.Stop();
            }
            finally
            {
                writer.Dispose();
                writer.Dispose();
                writer.Stop();
            }

            using SqliteConnection db = OpenDatabase(dbPath);

            // 13 张表齐全。
            Assert.Equal(13, KmsSchema.RequiredTables.Length);
            Assert.Empty(KmsSchema.MissingTables(db));

            // ---------------- 会话
            Assert.Equal(3, Long(db, "SELECT COUNT(*) FROM events_window_session;"));
            Assert.Equal(1, Long(db, "SELECT COUNT(*) FROM events_window_session WHERE is_open=1;"));
            Assert.Equal(0, Long(db, "SELECT COUNT(*) FROM events_window_session WHERE day<>'" + expectedDay + "';"));

            Assert.Equal(T0, Long(db, "SELECT start_ts_ms FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal(T0 + 3_000, Long(db, "SELECT end_ts_ms FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal(3, Long(db, "SELECT key_count FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal(2, Long(db, "SELECT left_clicks FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal(1, Long(db, "SELECT right_clicks FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal(0, Long(db, "SELECT middle_clicks FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal(1, Long(db, "SELECT wheel FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal(2_300, Long(db, "SELECT active_ms FROM events_window_session WHERE process_name='Code.exe';"));
            Assert.Equal("main.cs - Code", Text(db, "SELECT window_title FROM events_window_session WHERE process_name='Code.exe';"));

            Assert.Equal(T0 + 3_000, Long(db, "SELECT start_ts_ms FROM events_window_session WHERE process_name='chrome.exe';"));
            Assert.Equal(T0 + 65_000, Long(db, "SELECT end_ts_ms FROM events_window_session WHERE process_name='chrome.exe';"));
            Assert.Equal(3, Long(db, "SELECT key_count FROM events_window_session WHERE process_name='chrome.exe';"));
            Assert.Equal(1_300, Long(db, "SELECT active_ms FROM events_window_session WHERE process_name='chrome.exe';"));
            Assert.Equal(0, Long(db, "SELECT is_open FROM events_window_session WHERE process_name='chrome.exe';"));

            Assert.Equal(T0 + 65_000, Long(db, "SELECT start_ts_ms FROM events_window_session WHERE process_name='notepad.exe';"));
            Assert.Equal(T0 + 65_400, Long(db, "SELECT end_ts_ms FROM events_window_session WHERE process_name='notepad.exe';"));
            Assert.Equal(1, Long(db, "SELECT key_count FROM events_window_session WHERE process_name='notepad.exe';"));
            Assert.Equal(1, Long(db, "SELECT middle_clicks FROM events_window_session WHERE process_name='notepad.exe';"));
            Assert.Equal(1, Long(db, "SELECT wheel FROM events_window_session WHERE process_name='notepad.exe';"));
            Assert.Equal(400, Long(db, "SELECT active_ms FROM events_window_session WHERE process_name='notepad.exe';"));
            Assert.Equal(1, Long(db, "SELECT is_open FROM events_window_session WHERE process_name='notepad.exe';"));

            long openId = Long(db, "SELECT id FROM events_window_session WHERE is_open=1;");
            Assert.Equal(openId, Long(db, "SELECT CAST(v AS INTEGER) FROM meta WHERE k='open_session_id';"));
            Assert.Equal(T0 + 65_400, Long(db, "SELECT CAST(v AS INTEGER) FROM meta WHERE k='last_event_ts_ms';"));

            // ---------------- 突发段：只有 Count>0 的两段落库
            Assert.Equal(2, Long(db, "SELECT COUNT(*) FROM events_input_burst;"));

            // v3 不写 session_id，category_id 固定 -1。
            Assert.Equal(0, Long(db, "SELECT COUNT(*) FROM events_input_burst WHERE session_id IS NOT NULL;"));
            Assert.Equal(2, Long(db, "SELECT COUNT(*) FROM events_input_burst WHERE category_id=-1;"));

            Assert.Equal(3, Long(db, "SELECT key_count FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"));
            Assert.Equal(750.0, Real(db, "SELECT iki_avg_ms FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"), 6);
            Assert.Equal(62_500.0, Real(db, "SELECT iki_var FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"), 6);
            Assert.Equal(1, Long(db, "SELECT backspace_cnt FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"));
            Assert.Equal(1.0 / 3.0, Real(db, "SELECT backspace_rate FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"), 9);
            Assert.Equal(1, Long(db, "SELECT switch_count FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"));
            Assert.Equal(T0 + 3_000, Long(db, "SELECT end_ts_ms FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"));
            Assert.Equal(@"C:\Apps\Code.exe", Text(db, "SELECT process_path FROM events_input_burst WHERE start_ts_ms=" + T0 + ";"));

            long burst2 = T0 + 3_000;
            Assert.Equal(3, Long(db, "SELECT key_count FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"));
            Assert.Equal(650.0, Real(db, "SELECT iki_avg_ms FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"), 6);
            Assert.Equal(2_500.0, Real(db, "SELECT iki_var FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"), 6);
            Assert.Equal(0, Long(db, "SELECT backspace_cnt FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"));
            Assert.Equal(0.0, Real(db, "SELECT backspace_rate FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"), 9);
            Assert.Equal(1, Long(db, "SELECT switch_count FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"));
            Assert.Equal(T0 + 65_000, Long(db, "SELECT end_ts_ms FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"));
            Assert.Equal(@"C:\Apps\chrome.exe", Text(db, "SELECT process_path FROM events_input_burst WHERE start_ts_ms=" + burst2 + ";"));

            // ---------------- 日汇总：v3 口径 active_seconds 恒为 0
            Assert.Equal(7, Long(db, "SELECT keys FROM daily_summary;"));
            Assert.Equal(4, Long(db, "SELECT clicks FROM daily_summary;"));
            Assert.Equal(2, Long(db, "SELECT left FROM daily_summary;"));
            Assert.Equal(1, Long(db, "SELECT right FROM daily_summary;"));
            Assert.Equal(1, Long(db, "SELECT middle FROM daily_summary;"));
            Assert.Equal(2, Long(db, "SELECT wheel FROM daily_summary;"));
            Assert.Equal(0.0, Real(db, "SELECT active_seconds FROM daily_summary;"), 6);
            Assert.Equal(0, Long(db, "SELECT sessions FROM daily_summary;"));
            Assert.Equal(0, Long(db, "SELECT peak_apm FROM daily_summary;"));
            Assert.Equal(0, Long(db, "SELECT hold_count FROM daily_summary;"));
            Assert.Equal(0.0, Real(db, "SELECT hold_total_ms FROM daily_summary;"), 6);
            Assert.Equal(0.0, Real(db, "SELECT hold_max_ms FROM daily_summary;"), 6);
            Assert.Equal("shadow", Text(db, "SELECT origin FROM daily_summary;"));
            Assert.Equal(expectedDay, Text(db, "SELECT day FROM daily_summary;"));

            // ---------------- 窗口聚合：process_key 必须是 lower(path)
            Assert.Equal(3, Long(db, "SELECT COUNT(*) FROM window_daily_agg;"));
            Assert.Equal(0, Long(db, "SELECT COUNT(*) FROM window_daily_agg WHERE process_key<>lower(process_path);"));
            Assert.Equal("c:\\apps\\code.exe", Text(db, "SELECT process_key FROM window_daily_agg WHERE process_name='Code.exe';"));
            Assert.Equal(@"C:\Apps\Code.exe", Text(db, "SELECT process_path FROM window_daily_agg WHERE process_name='Code.exe';"));
            Assert.Equal(3, Long(db, "SELECT keys FROM window_daily_agg WHERE process_name='Code.exe';"));
            Assert.Equal(3, Long(db, "SELECT clicks FROM window_daily_agg WHERE process_name='Code.exe';"));
            Assert.Equal(1, Long(db, "SELECT wheel FROM window_daily_agg WHERE process_name='Code.exe';"));
            Assert.Equal(T0, Long(db, "SELECT first_ts_ms FROM window_daily_agg WHERE process_name='Code.exe';"));
            Assert.Equal(T0 + 2_300, Long(db, "SELECT last_ts_ms FROM window_daily_agg WHERE process_name='Code.exe';"));

            // ---------------- 点击网格：32 px 一格，三通道
            Assert.Equal(4, Long(db, "SELECT COUNT(*) FROM daily_click_grid;"));
            Assert.Equal(2, Long(db, "SELECT SUM(count) FROM daily_click_grid WHERE channel=1;"));
            Assert.Equal(1, Long(db, "SELECT SUM(count) FROM daily_click_grid WHERE channel=2;"));
            Assert.Equal(1, Long(db, "SELECT SUM(count) FROM daily_click_grid WHERE channel=3;"));
            Assert.Equal(0, Long(db, "SELECT COUNT(*) FROM daily_click_grid WHERE screen_id<>0;"));
            Assert.Equal(1, Long(db, "SELECT count FROM daily_click_grid WHERE channel=1 AND cell_x=3 AND cell_y=6;"));
            Assert.Equal(1, Long(db, "SELECT count FROM daily_click_grid WHERE channel=1 AND cell_x=4 AND cell_y=7;"));

            // ---------------- 小时 / 按键桶
            Assert.Equal(7, Long(db, "SELECT SUM(keys) FROM daily_hour_agg;"));
            Assert.Equal(4, Long(db, "SELECT SUM(clicks) FROM daily_hour_agg;"));
            Assert.Equal(7, Long(db, "SELECT SUM(cnt) FROM daily_key_agg;"));
            Assert.Equal(6, Long(db, "SELECT cnt FROM daily_key_agg WHERE vk=65;"));
            Assert.Equal(1, Long(db, "SELECT cnt FROM daily_key_agg WHERE vk=8;"));
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public void EventRing_DropsWhenFullWithoutBlocking()
    {
        EventRing ring = new(4);
        Assert.Equal(4, ring.Capacity);

        for (int i = 0; i < 4; i++)
        {
            RawInputEvent item = new(i, HwndCode, InputEventKind.KeyDown, i, 0, 0, 0);
            Assert.True(ring.TryEnqueue(in item));
        }

        RawInputEvent overflow = new(99, HwndCode, InputEventKind.KeyDown, 99, 0, 0, 0);
        Assert.False(ring.TryEnqueue(in overflow));
        Assert.Equal(1, ring.DroppedCount);
        Assert.Equal(4, ring.PendingCount);

        for (int i = 0; i < 4; i++)
        {
            Assert.True(ring.TryDequeue(out RawInputEvent item));
            Assert.Equal(i, item.VkOrButton);
        }

        Assert.False(ring.TryDequeue(out _));
        Assert.Equal(0, ring.PendingCount);
    }

    [Fact]
    public void ActiveMs_IncludesExactlyFifteenSeconds_ButExcludesLongerGaps()
    {
        string directory = NewDatabaseDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);

        try
        {
            EventRing ring = new(256);
            DbWriter writer = DbWriter.Open(
                dbPath,
                ring,
                new DbWriterOptions
                {
                    RunSelfHeal = false,
                    Diagnostic = static _ => { }
                });

            try
            {
                writer.ResolveWindow = static _ => new WindowTarget
                {
                    ProcessPath = @"C:\Apps\Code.exe",
                    ProcessName = "Code.exe",
                    WindowTitle = "main.cs - Code"
                };

                Feed(writer, ring, T0, InputEventKind.KeyDown, HwndCode, 65);
                Feed(writer, ring, T0 + 15_000, InputEventKind.KeyDown, HwndCode, 65);
                Feed(writer, ring, T0 + 30_001, InputEventKind.KeyDown, HwndCode, 65);

                Assert.Equal(3, writer.DrainOnce());
            }
            finally
            {
                writer.Dispose();
            }

            using SqliteConnection db = OpenDatabase(dbPath);
            Assert.Equal(1, Long(db, "SELECT COUNT(*) FROM events_window_session;"));
            Assert.Equal(15_000, Long(db, "SELECT active_ms FROM events_window_session;"));
            Assert.Equal(T0 + 30_001, Long(db, "SELECT end_ts_ms FROM events_window_session;"));
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public void SelfHeal_PrefersLastEventTime_AndFallsBackToInjectedClock()
    {
        string directory = NewDatabaseDirectory();
        string dbPath = Path.Combine(directory, KmsSchema.DbFileName);
        EventRing ring = new(1024);

        try
        {
            using (DbWriter bootstrap = DbWriter.Open(
                dbPath,
                ring,
                new DbWriterOptions { RunSelfHeal = false, Diagnostic = static _ => { } }))
            {
            }

            using (SqliteConnection raw = OpenDatabase(dbPath))
            {
                Exec(raw,
                    "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, is_open) " +
                    "VALUES (1000, 1000, '2023-11-14', 1);");
                Exec(raw, "INSERT OR REPLACE INTO meta (k, v) VALUES ('last_event_ts_ms', '5000');");
            }

            long firstPivot = T0 + 111_000;
            using (DbWriter healed = DbWriter.Open(
                dbPath,
                ring,
                new DbWriterOptions
                {
                    RunSelfHeal = true,
                    Clock = new FixedClock(firstPivot),
                    Diagnostic = static _ => { }
                }))
            {
            }

            using (SqliteConnection db = OpenDatabase(dbPath))
            {
                Assert.Equal(0, Long(db, "SELECT COUNT(*) FROM events_window_session WHERE is_open=1;"));
                Assert.Equal(5_000, Long(db, "SELECT end_ts_ms FROM events_window_session WHERE start_ts_ms=1000;"));
                Assert.Equal(0, Long(db, "SELECT CAST(v AS INTEGER) FROM meta WHERE k='open_session_id';"));
            }

            // 没有 last_event_ts_ms 时收口到注入的假时钟。
            using (SqliteConnection raw = OpenDatabase(dbPath))
            {
                Exec(raw,
                    "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, is_open) " +
                    "VALUES (2000, 2000, '2023-11-14', 1);");
                Exec(raw, "INSERT OR REPLACE INTO meta (k, v) VALUES ('last_event_ts_ms', '0');");
            }

            long secondPivot = T0 + 222_000;
            using (DbWriter healed = DbWriter.Open(
                dbPath,
                ring,
                new DbWriterOptions
                {
                    RunSelfHeal = true,
                    Clock = new FixedClock(secondPivot),
                    Diagnostic = static _ => { }
                }))
            {
            }

            using (SqliteConnection db = OpenDatabase(dbPath))
            {
                Assert.Equal(0, Long(db, "SELECT COUNT(*) FROM events_window_session WHERE is_open=1;"));
                Assert.Equal(secondPivot, Long(db, "SELECT end_ts_ms FROM events_window_session WHERE start_ts_ms=2000;"));
                Assert.Equal(5_000, Long(db, "SELECT end_ts_ms FROM events_window_session WHERE start_ts_ms=1000;"));
            }
        }
        finally
        {
            TryDelete(directory);
        }
    }

    // ---------------------------------------------------------------- 事件工厂

    private static WindowTarget? ResolveWindow(IntPtr hwnd)
    {
        if (hwnd == HwndChrome)
        {
            return new WindowTarget
            {
                ProcessPath = @"C:\Apps\chrome.exe",
                ProcessName = "chrome.exe",
                WindowTitle = "文档 - Chrome"
            };
        }

        if (hwnd == HwndNotepad)
        {
            return new WindowTarget
            {
                ProcessPath = @"C:\Apps\Notepad.exe",
                ProcessName = "notepad.exe",
                WindowTitle = "notes.txt - Notepad"
            };
        }

        return new WindowTarget
        {
            ProcessPath = @"C:\Apps\Code.exe",
            ProcessName = "Code.exe",
            WindowTitle = "main.cs - Code"
        };
    }

    private static void Feed(
        DbWriter writer,
        EventRing ring,
        long epochMs,
        int kind,
        IntPtr hwnd,
        int vkOrButton,
        int x = 0,
        int y = 0,
        int delta = 0)
    {
        RawInputEvent item = Event(writer, epochMs, kind, hwnd, vkOrButton, x, y, delta);
        Assert.True(ring.TryEnqueue(in item));
    }

    private static RawInputEvent Event(
        DbWriter writer,
        long epochMs,
        int kind,
        IntPtr hwnd,
        int vkOrButton,
        int x,
        int y,
        int delta) =>
        new(writer.TicksForMs(epochMs), hwnd, kind, vkOrButton, x, y, delta);

    // ---------------------------------------------------------------- 沙盒 / 读写

    private static string NewDatabaseDirectory()
    {
        string directory = Path.Combine(
            FindRepoRoot(),
            ".tmp-dbwriter-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KeyMouseStats.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("找不到仓库根目录（KeyMouseStats.slnx）。");
    }

    private static SqliteConnection OpenDatabase(string path)
    {
        SqliteConnection connection = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());

        connection.Open();
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Long(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static double Real(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string? Text(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static void TryDelete(string directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }

                string? parent = Path.GetDirectoryName(directory);
                if (parent is not null
                    && Directory.Exists(parent)
                    && Directory.GetFileSystemEntries(parent).Length == 0)
                {
                    Directory.Delete(parent);
                }

                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(long unixMs)
        {
            UtcNow = DateTime.UnixEpoch.AddMilliseconds(unixMs);
        }

        public DateTime UtcNow { get; }

        public long MonotonicTicks => 0;
    }
}
