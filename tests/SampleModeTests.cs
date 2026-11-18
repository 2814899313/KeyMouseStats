// ============================================================================
//  tests/SampleModeTests.cs  ·  v3.0 Milestone 2 / Step 2(3.4)
//
//  验证「示例数据预览模式」的三条硬约束:
//   1. 进入/退出只换引用,退出后真实 History / Today / Total **逐字段与引用**完全还原;
//   2. 示例期间 Store.Save / SaveAsync 绝不写盘(哪怕 PersistenceArmed 为真);
//   3. 示例期间 ShadowPipeline.Enqueue 绝不入环(退出后同一路径恢复正常)。
//  以及全新安装判空必须绕开「静态构造预置的今日空记录」。
//
//  需要原生 SQLite 资源(test.bat 的 %RESNATIVE% 分支);
//  数据目录与原生根都重定向到 %TEMP% 下的独立沙盒,退出时清理。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using KeyMouseStats;

internal static class SampleModeTests
{
    private static int _checks;
    private static string _root;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + "(期望 " + expected + ",实际 " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("SampleModeTests", Run);
    }

    private static void Run()
    {
        _root = Path.Combine(Path.GetTempPath(), "SampleModeTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        NativeBoot.RootOverride = _root;
        Store.DataDirectory = Path.Combine(_root, "data");
        Directory.CreateDirectory(Store.DataDirectory);
        try
        {
            FreshEmptyRules();
            RestoreRoundTrip();
            WritesAreBlocked();
            Console.WriteLine("SampleModeTests: " + _checks + " 项断言通过");
        }
        finally
        {
            SampleData.Exit();
            try { ShadowPipeline.Stop(); } catch { }
            try { NativeBoot.ReleaseForTest(); } catch { }
            try { Directory.Delete(_root, true); } catch { }
        }
    }

    /// <summary>1. 判空必须看内容,不能看 History.Count。</summary>
    private static void FreshEmptyRules()
    {
        Check(SampleData.IsFreshEmpty(new DayRecord[0], 0), "空集合 + 零按键 = 全新安装");
        Check(SampleData.IsFreshEmpty(null, 0), "null 集合按全新处理");

        DayRecord blank = new DayRecord();
        blank.Date = DateTime.Today;
        Check(SampleData.IsFreshEmpty(new DayRecord[] { blank }, 0),
            "只有『今天』这条空记录仍应判为全新(Store 静态构造会预置它)");

        DayRecord busy = new DayRecord();
        busy.Date = DateTime.Today.AddDays(-1);
        busy.Keys = 1;
        Check(!SampleData.IsFreshEmpty(new DayRecord[] { blank, busy }, 0), "有任何一天非空就不是全新");
        Check(!SampleData.IsFreshEmpty(new DayRecord[0], 1), "零天但累计按键非零也不算全新");

        Check(!SampleData.Active, "初始状态不应在示例模式");
    }

    /// <summary>2. 进入/退出只换引用,退出后原样还原。</summary>
    private static void RestoreRoundTrip()
    {
        Store.History.Clear();
        Counters total = new Counters();
        total.Keys = 4242;
        total.Clicks = 99;
        total.Left = 60;
        total.Wheel = 7;
        Store.Total = total;
        DayRecord today = new DayRecord();
        today.Date = DateTime.Today;
        today.Keys = 4242;
        today.Clicks = 99;
        today.Wheel = 7;
        Store.Today = today;
        Store.History[DateTime.Today] = today;
        DateTime savedDay = Store.Day;

        Dictionary<DateTime, DayRecord> savedHistory = Store.History;
        DayRecord savedToday = Store.Today;
        Counters savedTotal = Store.Total;

        SampleData.Enter();
        Check(SampleData.Active, "进入后应处于示例模式");
        Check(!ReferenceEquals(Store.History, savedHistory), "示例期间 History 应换成示例集合");
        Equal(Store.History.Count, SampleData.DayCount, "示例应恰好覆盖 14 天");
        Check(Store.Total.Keys > 0, "示例应产生按键数据");

        long channeled = 0;
        long apps = 0;
        foreach (DayRecord day in Store.History.Values)
        {
            channeled += day.ClickCellsV1.Count;
            apps += day.Apps.Count;
        }
        Check(channeled > 0, "示例应带三通道点击热区");
        Check(apps > 0, "示例应带应用使用记录");

        SampleData.Enter();   // 幂等
        Equal(Store.History.Count, SampleData.DayCount, "重复进入不应叠加");

        SampleData.Exit();
        Check(!SampleData.Active, "退出后应关闭示例模式");
        Check(ReferenceEquals(Store.History, savedHistory), "History 引用应原样还原");
        Check(ReferenceEquals(Store.Today, savedToday), "Today 引用应原样还原");
        Check(ReferenceEquals(Store.Total, savedTotal), "Total 引用应原样还原");
        Equal(Store.Total.Keys, 4242, "Total.Keys 应逐字段还原");
        Equal(Store.Total.Clicks, 99, "Total.Clicks 应逐字段还原");
        Equal(Store.Today.Keys, 4242, "Today.Keys 应逐字段还原");
        Equal(Store.History.Count, 1, "History 条目数应还原");
        Check(Store.History.ContainsKey(DateTime.Today), "历史里应仍有今天那条");
        Check(Store.Day == savedDay, "Store.Day 应还原");

        SampleData.Exit();    // 幂等
        Check(!SampleData.Active, "重复退出应无副作用");
    }

    /// <summary>3. 写盘与入环双重熔断。</summary>
    private static void WritesAreBlocked()
    {
        Store.History.Clear();
        Store.Total = new Counters();
        Store.Today = new DayRecord();
        Store.Today.Date = DateTime.Today;
        Store.History[DateTime.Today] = Store.Today;
        Store.PersistenceArmed = true;    // 刻意置真:熔断必须压过它

        Check(ShadowPipeline.Start(_root), "阴影双写应能在临时目录启动");
        EventRing ring = ShadowPipeline.Ring;
        Check(ring != null, "启动后应拿到事件环");

        int filesBefore = Directory.GetFiles(Store.DataDirectory, "*", SearchOption.AllDirectories).Length;
        long enqueuedBefore = ring.EnqueuedCount;

        SampleData.Enter();
        ShadowPipeline.Enqueue(InputEventKind.KeyDown, IntPtr.Zero, 65, 0, 0, 0, 1);
        ShadowPipeline.Enqueue(InputEventKind.MouseDown, IntPtr.Zero, 1, 10, 10, 0, 2);
        ShadowPipeline.Enqueue(InputEventKind.Wheel, IntPtr.Zero, 0, 10, 10, 0, 3);
        Store.Save();
        Store.SaveAsync();

        Equal(ring.EnqueuedCount, enqueuedBefore, "示例模式期间不得向事件环入队");
        int filesAfter = Directory.GetFiles(Store.DataDirectory, "*", SearchOption.AllDirectories).Length;
        Equal(filesAfter, filesBefore, "示例模式期间不得写出任何文件");

        SampleData.Exit();
        Store.Save();
        int filesReal = Directory.GetFiles(Store.DataDirectory, "*", SearchOption.AllDirectories).Length;
        Check(filesReal > filesAfter, "退出示例模式后 Save 应恢复正常写盘");

        long enqueuedReal = ring.EnqueuedCount;
        ShadowPipeline.Enqueue(InputEventKind.KeyDown, IntPtr.Zero, 66, 0, 0, 0, 4);
        ShadowPipeline.Enqueue(InputEventKind.KeyDown, IntPtr.Zero, 67, 0, 0, 0, 5);
        Equal(ring.EnqueuedCount, enqueuedReal + 2, "退出示例模式后入队应恢复正常");

        try { ShadowPipeline.Flush(2000); } catch { }
    }
}
