// ============================================================================
//  tests/MouseWearMultiTests.cs  ·  v3.4.1 多鼠标档案/迁移/聚合
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using KeyMouseStats;

internal static class MouseWearMultiTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("MouseWearMultiTests", Run);
    }

    private static MouseProfile Add(string id, string alias, long ratedLeft, long ratedRight, long handle)
    {
        MouseProfile pending = PeripheralMouseWear.CreatePendingProfile(new IntPtr(handle));
        pending.Alias = alias;
        pending.RatedLeft = ratedLeft;
        pending.RatedRight = ratedRight;
        pending.Since = new DateTime(2026, 1, 1);
        MouseIdentity identity = new MouseIdentity();
        identity.Id = id;
        identity.InstanceKey = "MTEST\\" + id;
        identity.HardwareKey = "MTEST\\" + id;
        identity.DevicePath = "MTEST\\" + id;
        identity.FriendlyName = alias + " friendly";
        MouseProfile profile = PeripheralMouseWear.ResolvePending(pending, identity);
        profile.Alias = alias;
        profile.RatedLeft = ratedLeft;
        profile.RatedRight = ratedRight;
        profile.Since = new DateTime(2026, 1, 1);
        profile.Seeded = true;
        return profile;
    }

    private static void Clicks(MouseProfile p, DateTime day, int left, int right, int middle, int x)
    {
        for (int i = 0; i < left; i++) PeripheralMouseWear.CountClick(p, 1, day);
        for (int i = 0; i < right; i++) PeripheralMouseWear.CountClick(p, 2, day);
        for (int i = 0; i < middle; i++) PeripheralMouseWear.CountClick(p, 3, day);
        for (int i = 0; i < x; i++) PeripheralMouseWear.CountClick(p, 4, day);
    }

    private static void Run()
    {
        ProfilesAndAggregate();
        ResetIsolation();
        Persistence();
        Legacy();
        Days();
        Seeding();
        Console.WriteLine("MouseWearMultiTests: " + _checks + " 项断言通过");
    }

    private static void ProfilesAndAggregate()
    {
        PeripheralMouseWear.BeginLoad();
        MouseProfile home = Add("mouse:first", "家里", 100, 100, 0x401);
        MouseProfile office = Add("mouse:second", "公司", 100, 100, 0x402);
        Clicks(home, DateTime.Today, 60, 30, 5, 2);
        Clicks(office, DateTime.Today, 20, 10, 1, 1);
        Equal(home.Read(1), 60, "第一只鼠标左键");
        Equal(office.Read(2), 10, "第二只鼠标右键");
        MouseWearSnapshot all = MouseWearSnapshot.CaptureAggregate();
        Equal(all.WorstLeft == 0 ? 0 : 1, 1, "聚合最高左键存在");
        Check(Math.Abs(all.WorstLeft - 0.6) < 0.0001, "聚合最高左键取单设备 60%");
        Check(all.WorstLeftSource == "家里", "聚合最高左键来源");
        Equal(all.Left, 60, "聚合展示最高设备的计数");
        Equal(all.Middle, 6, "聚合中键合计");
        Check(all.Contributions != null && all.Contributions.Count == 2, "聚合设备贡献");
        MouseWearSnapshot one = MouseWearSnapshot.Capture(home.Id);
        Equal(one.Left, 60, "单设备快照左键");
        Equal(one.Days[6], 97, "单设备近 7 天最后一天");
    }

    private static void ResetIsolation()
    {
        PeripheralMouseWear.BeginLoad();
        MouseProfile first = Add("mouse:first", "家里", 100, 100, 0x411);
        MouseProfile second = Add("mouse:second", "公司", 100, 100, 0x412);
        Clicks(first, DateTime.Today, 10, 5, 0, 0);
        Clicks(second, DateTime.Today, 8, 4, 0, 0);
        PeripheralMouseWear.Reset(first.Id, new DateTime(2026, 2, 2));
        Equal(first.Read(1), 0, "重置第一只鼠标");
        Equal(second.Read(1), 8, "第二只鼠标不受影响");
        Check(first.Since == new DateTime(2026, 2, 2), "重置起始日期");
        long[] resetDays = new long[7];
        first.CopyDaysTo(resetDays);
        Equal(resetDays[6], 0, "重置清空近 7 天");
    }

    private static void Persistence()
    {
        PeripheralMouseWear.BeginLoad();
        MouseProfile first = Add("mouse:first", "键盘旁", 50000000, 60000000, 0x421);
        MouseProfile second = Add("mouse:second", "包里", 70000000, 80000000, 0x422);
        first.Since = new DateTime(2025, 5, 1);
        second.Since = new DateTime(2025, 6, 1);
        Clicks(second, DateTime.Today, 7, 3, 2, 1);
        StringBuilder sb = new StringBuilder();
        PeripheralMouseWear.Append(sb);
        List<string> headers = new List<string>();
        List<string> buttons = new List<string>();
        List<string> days = new List<string>();
        string[] lines = sb.ToString().Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("mouse_v1=")) headers.Add(lines[i].Substring(9));
            else if (lines[i].StartsWith("mouse_buttons_v1=")) buttons.Add(lines[i].Substring(17));
            else if (lines[i].StartsWith("mouse_days_v1=")) days.Add(lines[i].Substring(14));
        }
        Check(headers.Count == 2 && buttons.Count == 2 && days.Count == 2, "多鼠标行数");
        PeripheralMouseWear.BeginLoad();
        PeripheralMouseWear.LoadRaw("", 0, 0, DateTime.MinValue, "0|0|0|0", true, headers, buttons, days);
        MouseProfile restored = PeripheralMouseWear.Find("mouse:second");
        Check(restored != null, "第二只鼠标回读");
        Equal(restored.Read(1), 7, "第二只鼠标左键回读");
        Equal(restored.Read(4), 1, "第二只鼠标侧键回读");
        Check(restored.RatedLeft == 70000000 && restored.Since == new DateTime(2025, 6, 1), "第二只鼠标配置回读");
        long[] restoredDays = new long[7];
        restored.CopyDaysTo(restoredDays);
        Check(restoredDays[6] == 13, "第二只鼠标近 7 天回读");
    }

    private static void Legacy()
    {
        PeripheralMouseWear.ResetForTest();
        PeripheralWear.MouseName = "旧鼠标";
        PeripheralWear.MouseRatedLeft = 50000000;
        PeripheralWear.MouseRatedRight = 40000000;
        PeripheralWear.MouseSince = new DateTime(2024, 1, 1);
        PeripheralWear.CountClick(1);
        PeripheralWear.CountClick(2);
        PeripheralWear.CountClick(2);
        string profile = PeripheralWear.EncodeProfile();
        string mouse = PeripheralWear.EncodeMouse();
        PeripheralWear.BeginLoad();
        PeripheralWear.LoadRaw(profile, "", mouse, false, true);
        MouseProfile legacy = PeripheralMouseWear.Find(PeripheralMouseWear.LegacyId);
        Check(legacy != null && legacy.Legacy, "旧版单鼠标成为 legacy");
        Equal(legacy.Read(1), 1, "legacy 左键保留");
        Equal(legacy.Read(2), 2, "legacy 右键保留");
        MouseProfile target = Add("mouse:new", "新鼠标", 50000000, 50000000, 0x431);
        Check(PeripheralMouseWear.MergeLegacyInto(target.Id), "legacy 可归入当前鼠标");
        Equal(target.Read(1), 1, "归并后左键保留");
        Equal(target.Read(2), 2, "归并后右键保留");
        Check(PeripheralMouseWear.Find(PeripheralMouseWear.LegacyId) == null, "归并后 legacy 移除");
    }

    private static void Days()
    {
        PeripheralMouseWear.BeginLoad();
        MouseProfile home = Add("mouse:first", "家里", 100, 100, 0x441);
        Clicks(home, DateTime.Today.AddDays(-2), 4, 2, 0, 0);
        Clicks(home, DateTime.Today.AddDays(-1), 6, 3, 0, 0);
        Clicks(home, DateTime.Today, 2, 1, 0, 0);
        MouseWearSnapshot snapshot = MouseWearSnapshot.Capture(home.Id);
        Equal(snapshot.Days[6], 3, "近 7 天最后一天");
        Equal(snapshot.Days[5], 9, "近 7 天前一天");
        Equal(snapshot.Days[4], 6, "近 7 天前两天");
    }

    private static void Seeding()
    {
        PeripheralWear.BeginLoad();
        List<DayRecord> history = new List<DayRecord>();
        DayRecord day = new DayRecord();
        day.Date = new DateTime(2025, 3, 4);
        day.Left = 10; day.Right = 6; day.Middle = 2; day.XButtons = 1;
        history.Add(day);
        Counters total = new Counters();
        total.Left = 12; total.Right = 7; total.Middle = 2; total.XButtons = 1;
        PeripheralWear.LoadRaw("", "", "", false, false);
        PeripheralPrewearSeed(history, total);
        MouseProfile legacy = PeripheralMouseWear.Find(PeripheralMouseWear.LegacyId);
        Check(legacy != null && legacy.Legacy, "历史鼠标升级为 legacy");
        Equal(legacy.Read(1), 12, "历史左键取 Total 较大值");
        Equal(legacy.Read(2), 7, "历史右键取 Total 较大值");
        Check(PeripheralMouseWear.Find(PeripheralMouseWear.LegacyId) != null, "历史 legacy 保留");
    }

    private static void PeripheralPrewearSeed(List<DayRecord> history, Counters total)
    {
        PeripheralMouseWear.EnsureSeeded(history, total);
    }
}
