// ============================================================================
//  tests/DbWriterTests.cs  ·  v3.0 Milestone 1 / Step 3
//
//  验证后台写线程与状态机:
//   1. 批量事务:未 Drain 前一行都不落库,Drain 后一次性可见;
//   2. 会话切分:窗口 (process_path, window_title) 变化即收口上一段、开新段;
//   3. 空闲切分:超过 60 s 无输入同样收口;
//   4. Welford:IKI 均值/方差与手算值一致,> 15 s 的思考停顿被排除;
//   5. 悬空会话自愈:强杀留下的 is_open=1 行被收口到 meta.last_event_ts_ms;
//   6. 阴影双写:daily_summary 与 DayRecord 的五个核心标量 0 差异;
//   7. 实时时间基准:day 必须是本地今天,start_ts_ms 必须是 Unix 毫秒。
//
//  全部写入在 %TEMP% 沙盒内;窗口解析用假实现,不依赖真实前台窗口。
// ============================================================================

using System;
using System.IO;
using KeyMouseStats;

internal static class DbWriterTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + "(期望 " + expected + ",实际 " + actual + ")");
    }

    private static void Near(double actual, double expected, double tolerance, string name)
    {
        Check(Math.Abs(actual - expected) <= tolerance,
            name + "(期望 " + expected.ToString("0.###") + ",实际 " + actual.ToString("0.###") + ")");
    }

    private static void Main()
    {
        // 必须先重定向原生库释放根,否则 KmsDb.OpenWriter 会把 e_sqlite3.dll
        // 释放到真实的 %LOCALAPPDATA%\KeyMouseStats\native 下 —— 那是用户目录。
        string bootRoot = Path.Combine(Path.GetTempPath(), "KmsWriterBoot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bootRoot);
        NativeBoot.RootOverride = bootRoot;
        try
        {
            Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("DbWriterTests", Run);
        }
        finally
        {
            try { NativeBoot.ReleaseForTest(); } catch { }
            try { Directory.Delete(bootRoot, true); } catch { }
        }
    }

    // 2023-11-14 22:13:20 UTC 附近,保证落在同一个本地日。
    private const long T0 = 1700000000000L;

    private static readonly IntPtr HwndA = new IntPtr(0x101);
    private static readonly IntPtr HwndB = new IntPtr(0x202);

    private static string NewSandbox(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(), "KmsWriter-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ForegroundApp.WindowInfo Win(string path, string title)
    {
        ForegroundApp.WindowInfo info = new ForegroundApp.WindowInfo();
        info.ProcessPath = path;
        info.ProcessName = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path);
        info.WindowTitle = title;
        return info;
    }

    private static Func<IntPtr, ForegroundApp.WindowInfo> FakeResolver()
    {
        return delegate(IntPtr hwnd)
        {
            if (hwnd == HwndB) return Win(@"C:\Apps\chrome.exe", "文档 - Chrome");
            return Win(@"C:\Apps\Code.exe", "main.cs - Code");
        };
    }

    private static void Feed(DbWriter writer, EventRing ring, int kind, IntPtr hwnd, int vk, long ms)
    {
        Feed(writer, ring, kind, hwnd, vk, ms, 0, 0, 0);
    }

    private static void Feed(DbWriter writer, EventRing ring, int kind, IntPtr hwnd, int vk,
        long ms, int x, int y, int delta)
    {
        RawInputEvent item = new RawInputEvent();
        item.TimestampTicks = writer.TicksForMs(ms);
        item.Hwnd = hwnd;
        item.Kind = kind;
        item.VkOrButton = vk;
        item.X = x;
        item.Y = y;
        item.Delta = delta;
        Check(ring.TryEnqueue(ref item), "事件应能入环");
    }

    private static void Run()
    {
        BatchAndSessionSplit();
        IdleSplitsSession();
        WelfordIki();
        SelfHealAfterCrash();
        ShadowDualWriteZeroDiff();
        RealtimeTimestampBase();
        Console.WriteLine("DbWriterTests: " + _checks + " 项断言通过");
    }

    /// <summary>7. 实时写入必须使用 Unix 毫秒,day 必须是本地今天。</summary>
    private static void RealtimeTimestampBase()
    {
        string root = NewSandbox("realtime");
        EventRing ring = new EventRing(8192);
        DbWriter writer = DbWriter.Open(root, ring);
        writer.ResolveWindow = FakeResolver();
        try
        {
            long now = UnixTime.NowMs();
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, now);
            writer.DrainOnce();
            using (KmsDb reader = KmsDb.OpenReader(writer.DatabasePath))
            {
                string day = reader.ScalarText(
                    "SELECT day FROM events_window_session ORDER BY id DESC LIMIT 1;", null);
                long start = reader.ScalarLong(
                    "SELECT start_ts_ms FROM events_window_session ORDER BY id DESC LIMIT 1;", null);
                Check(day == UnixTime.ToLocal(now).ToString("yyyy-MM-dd"),
                    "实时会话 day 必须是本地今天,实际=" + day);
                Check(day == DateTime.Today.ToString("yyyy-MM-dd"),
                    "实时会话 day 必须等于 DateTime.Today,实际=" + day);
                Check(Math.Abs(start - now) <= 86400000L,
                    "start_ts_ms 必须落在当前 Unix 毫秒 ±1 天内,实际=" + start + ",now=" + now);
                Check(start < KmsSchema.EpochFixThresholdMs,
                    "start_ts_ms 不得落入 3995 年异常区间,实际=" + start);
            }
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>1 + 2. 批量事务与会话切分。</summary>
    private static void BatchAndSessionSplit()
    {
        string root = NewSandbox("split");
        EventRing ring = new EventRing(8192);
        DbWriter writer = DbWriter.Open(root, ring);
        writer.ResolveWindow = FakeResolver();
        KmsDb reader = null;
        try
        {
            // A → A → B → A:应在 B 处与回到 A 处各切一次。
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 0);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 100);
            Feed(writer, ring, InputEventKind.KeyDown, HwndB, 66, T0 + 200);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 67, T0 + 300);

            reader = KmsDb.OpenReader(writer.DatabasePath);
            Equal(reader.ScalarLong("SELECT COUNT(*) FROM events_window_session;", null), 0,
                "未 Drain 前不应有任何落库(批量事务)");
            Equal(writer.Ring.PendingCount, 4, "四条事件应还在环里");
            reader.Dispose();
            reader = null;

            Equal(writer.DrainOnce(), 4, "本批应消费四条");

            reader = KmsDb.OpenReader(writer.DatabasePath);
            Equal(reader.ScalarLong("SELECT COUNT(*) FROM events_window_session;", null), 3,
                "A → B → A 应产生三段会话");
            Equal(reader.ScalarLong("SELECT COUNT(*) FROM events_window_session WHERE is_open=1;", null), 1,
                "只有最后一段是打开的");
            Equal(reader.ScalarLong(
                "SELECT key_count FROM events_window_session WHERE is_open=0 ORDER BY id LIMIT 1;", null), 2,
                "第一段 A 应记两次击键");

            string firstPath = reader.ScalarText(
                "SELECT process_path FROM events_window_session ORDER BY id LIMIT 1;", null);
            Check(firstPath == @"C:\Apps\Code.exe", "第一段应归属 Code.exe,实际=" + firstPath);

            long openId = reader.ScalarLong(
                "SELECT CAST(v AS INTEGER) FROM meta WHERE k='open_session_id';", null);
            Check(openId > 0, "meta.open_session_id 应指向当前会话");
            Equal(reader.ScalarLong("SELECT id FROM events_window_session WHERE is_open=1;", null), openId,
                "open_session_id 必须等于那条 is_open=1 的行");
        }
        finally
        {
            if (reader != null) reader.Dispose();
            writer.Dispose();
            TryDelete(root);
        }
    }

    /// <summary>3. 空闲 60 s 切分会话。</summary>
    private static void IdleSplitsSession()
    {
        string root = NewSandbox("idle");
        EventRing ring = new EventRing(8192);
        DbWriter writer = DbWriter.Open(root, ring);
        writer.ResolveWindow = FakeResolver();
        KmsDb reader = null;
        try
        {
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 0);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 70000);
            writer.DrainOnce();

            reader = KmsDb.OpenReader(writer.DatabasePath);
            Equal(reader.ScalarLong("SELECT COUNT(*) FROM events_window_session;", null), 2,
                "超过 60 s 空闲应切成两段");
            Equal(reader.ScalarLong("SELECT COUNT(*) FROM events_window_session WHERE is_open=1;", null), 1,
                "只保留一段打开");
        }
        finally
        {
            if (reader != null) reader.Dispose();
            writer.Dispose();
            TryDelete(root);
        }
    }

    /// <summary>4. Welford 在线方差:0/500/1500/3000 → IKI 500,1000,1500。</summary>
    private static void WelfordIki()
    {
        string root = NewSandbox("welford");
        EventRing ring = new EventRing(8192);
        DbWriter writer = DbWriter.Open(root, ring);
        writer.ResolveWindow = FakeResolver();
        KmsDb reader = null;
        try
        {
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 0);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 500);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 1500);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 3000);
            // 17 s 后的这一下应触发「思考停顿」,把上一段收口。
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 20000);
            writer.DrainOnce();

            reader = KmsDb.OpenReader(writer.DatabasePath);
            Equal(reader.ScalarLong("SELECT COUNT(*) FROM events_input_burst;", null), 1,
                "只应落一段节律(停顿后的单键段太短被丢弃)");

            Equal(reader.ScalarLong("SELECT key_count FROM events_input_burst;", null), 4,
                "该段应为 4 次按键");
            Near(reader.ScalarLong("SELECT CAST(iki_avg_ms AS INTEGER) FROM events_input_burst;", null),
                1000, 1, "IKI 均值");

            double variance = Convert.ToDouble(
                reader.ScalarText("SELECT iki_var FROM events_input_burst;", null));
            Near(variance, 500000.0 / 3.0, 1.0, "Welford 方差应与手算一致");
        }
        finally
        {
            if (reader != null) reader.Dispose();
            writer.Dispose();
            TryDelete(root);
        }
    }

    /// <summary>5. 强杀留下的 is_open=1 悬空会话必须被自愈收口。</summary>
    private static void SelfHealAfterCrash()
    {
        string root = NewSandbox("heal");
        string dbPath = Path.Combine(root, KmsSchema.DbFileName);
        EventRing ring = new EventRing(1024);

        DbWriter bootstrap = DbWriter.Open(root, ring);
        bootstrap.Dispose();

        // 模拟崩溃现场:一条 is_open=1 的会话 + 最后事件时刻。
        KmsDb raw = KmsDb.OpenWriter(dbPath);
        try
        {
            raw.Execute(
                "INSERT INTO events_window_session (start_ts_ms, end_ts_ms, day, is_open) VALUES (?,?,?,1);",
                new object[] { 1000L, 1000L, "2023-11-14" });
            raw.Execute("INSERT OR REPLACE INTO meta (k, v) VALUES ('last_event_ts_ms', ?);",
                new object[] { "5000" });
        }
        finally
        {
            raw.Dispose();
        }

        // 重新打开 = 模拟进程重启,SelfHeal 在 Open 内自动执行。
        DbWriter reopened = DbWriter.Open(root, ring);
        reopened.Dispose();

        KmsDb reader = KmsDb.OpenReader(dbPath);
        try
        {
            Equal(reader.ScalarLong("SELECT COUNT(*) FROM events_window_session WHERE is_open=1;", null), 0,
                "重开后不应再有悬空会话");
            Equal(reader.ScalarLong("SELECT end_ts_ms FROM events_window_session LIMIT 1;", null), 5000,
                "end_ts_ms 应收口到 meta.last_event_ts_ms");
            Equal(reader.ScalarLong(
                "SELECT CAST(v AS INTEGER) FROM meta WHERE k='open_session_id';", null), 0,
                "open_session_id 应被清零");
        }
        finally
        {
            reader.Dispose();
            TryDelete(root);
        }
    }

    /// <summary>6. 阴影双写:daily_summary 与 DayRecord 五个核心标量 0 差异。</summary>
    private static void ShadowDualWriteZeroDiff()
    {
        string root = NewSandbox("shadow");
        EventRing ring = new EventRing(8192);
        DbWriter writer = DbWriter.Open(root, ring);
        writer.ResolveWindow = FakeResolver();
        KmsDb reader = null;
        try
        {
            // 3 次击键 + 2 左 + 1 右 + 1 中 + 1 滚轮。
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 0);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 65, T0 + 100);
            Feed(writer, ring, InputEventKind.KeyDown, HwndA, 66, T0 + 200);
            Feed(writer, ring, InputEventKind.MouseDown, HwndA, 1, T0 + 300, 100, 200, 0);
            Feed(writer, ring, InputEventKind.MouseDown, HwndA, 1, T0 + 400, 132, 232, 0);
            Feed(writer, ring, InputEventKind.MouseDown, HwndA, 2, T0 + 500, 300, 400, 0);
            Feed(writer, ring, InputEventKind.MouseDown, HwndA, 3, T0 + 600, 500, 600, 0);
            Feed(writer, ring, InputEventKind.Wheel, HwndA, 0, T0 + 700, 500, 600, 120);
            writer.DrainOnce();

            reader = KmsDb.OpenReader(writer.DatabasePath);

            DayRecord expected = new DayRecord();
            expected.Keys = 3;
            expected.Left = 2;
            expected.Right = 1;
            expected.Middle = 1;
            expected.Clicks = 4;

            string day = reader.ScalarText("SELECT day FROM daily_summary LIMIT 1;", null);
            Check(!string.IsNullOrEmpty(day), "daily_summary 应有一行");

            ShadowDayDiff diff = ShadowVerify.CompareDay(reader, day, expected);
            Check(diff.Consistent, "阴影双写应与 DayRecord 0 差异 → " + diff.Describe());

            // 热区:两次左键落在不同格子,右键/中键各自成通道。
            long cells = reader.ScalarLong("SELECT COUNT(*) FROM daily_click_grid;", null);
            Check(cells >= 3, "三通道热区应至少落 3 个格子,实际=" + cells);
            Equal(reader.ScalarLong(
                "SELECT SUM(count) FROM daily_click_grid WHERE channel=1;", null), 2, "左键通道累计");
            Equal(reader.ScalarLong(
                "SELECT SUM(count) FROM daily_click_grid WHERE channel=2;", null), 1, "右键通道累计");
            Equal(reader.ScalarLong(
                "SELECT SUM(count) FROM daily_click_grid WHERE channel=3;", null), 1, "中键通道累计");

            // window_daily_agg 同步。
            Equal(reader.ScalarLong("SELECT SUM(keys) FROM window_daily_agg;", null), 3, "窗口级击键");
            Equal(reader.ScalarLong("SELECT SUM(clicks) FROM window_daily_agg;", null), 4, "窗口级点击");

            // 小时桶同步。
            Equal(reader.ScalarLong("SELECT SUM(keys) FROM daily_hour_agg;", null), 3, "小时桶击键");
            Equal(reader.ScalarLong("SELECT SUM(clicks) FROM daily_hour_agg;", null), 4, "小时桶点击");
        }
        finally
        {
            if (reader != null) reader.Dispose();
            writer.Dispose();
            TryDelete(root);
        }
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, true); }
        catch { }
    }
}
