// ============================================================================
//  tests/PeripheralWearMultiTests.cs  ·  v3.4.1 多键盘档案/迁移/重置
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using KeyMouseStats;

internal static class PeripheralWearMultiTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("PeripheralWearMultiTests", Run);
    }

    private static KeyboardProfile Add(string id, string alias, long rated, long handle)
    {
        KeyboardProfile pending = PeripheralWear.CreatePendingProfile(new IntPtr(handle));
        pending.Alias = alias;
        pending.Rated = rated;
        pending.Since = new DateTime(2026, 1, 1);
        KeyboardIdentity identity = new KeyboardIdentity();
        identity.Id = id;
        identity.InstanceKey = "TEST\\" + id;
        identity.HardwareKey = "TEST\\" + id;
        identity.DevicePath = "TEST\\" + id;
        identity.FriendlyName = alias + " friendly";
        KeyboardProfile profile = PeripheralWear.ResolvePending(pending, identity);
        profile.Alias = alias;
        profile.Rated = rated;
        profile.Since = new DateTime(2026, 1, 1);
        profile.Seeded = true;
        return profile;
    }

    private static void Run()
    {
        ProfilesAndAggregate();
        ResetIsolation();
        Persistence();
        Legacy();
        FreshState();
        Console.WriteLine("PeripheralWearMultiTests: " + _checks + " 项断言通过");
    }

    private static void ProfilesAndAggregate()
    {
        PeripheralWear.BeginLoad();
        KeyboardProfile first = Add("dev:first", "键盘一", 100, 0x201);
        KeyboardProfile second = Add("dev:second", "键盘二", 100, 0x202);
        for (int i = 0; i < 60; i++) PeripheralWear.CountKey(first, 32);
        for (int i = 0; i < 30; i++) PeripheralWear.CountKey(second, 32);
        for (int i = 0; i < 20; i++) PeripheralWear.CountKey(second, 65);
        Equal(first.ReadKey(32), 60, "第一把键盘空格");
        Equal(second.ReadKey(32), 30, "第二把键盘空格");
        List<KeyWear> all = PeripheralWear.TopKeys("", 5);
        Check(all.Count == 2, "聚合 TOP 两种");
        Equal(all[0].Count, 90, "聚合空格 90");
        Equal(all[1].Count, 20, "聚合 A 20");
        Equal(PeripheralWear.TopKeys(first.Id, 5)[0].Count, 60, "单设备 TOP");
    }

    private static void ResetIsolation()
    {
        PeripheralWear.BeginLoad();
        KeyboardProfile first = Add("dev:first", "键盘一", 100, 0x211);
        KeyboardProfile second = Add("dev:second", "键盘二", 100, 0x212);
        for (int i = 0; i < 10; i++) PeripheralWear.CountKey(first, 32);
        for (int i = 0; i < 10; i++) PeripheralWear.CountKey(second, 32);
        PeripheralWear.CountClick(1);
        PeripheralWear.ResetKeyboard(first.Id, new DateTime(2026, 2, 2));
        Equal(first.ReadKey(32), 0, "重置第一把键盘");
        Equal(second.ReadKey(32), 10, "第二把键盘不受影响");
        Check(first.Since == new DateTime(2026, 2, 2), "重置起始时间");
        Equal(PeripheralWear.LeftClicks, 1, "重置键盘不影响鼠标");
        PeripheralWear.ResetMouse(new DateTime(2026, 2, 3));
        Equal(PeripheralWear.LeftClicks, 0, "重置鼠标");
    }

    private static void Persistence()
    {
        PeripheralWear.BeginLoad();
        KeyboardProfile first = Add("dev:first", "键盘一", 50000000, 0x221);
        KeyboardProfile second = Add("dev:second", "键盘二", 60000000, 0x222);
        first.Since = new DateTime(2025, 5, 1);
        second.Since = new DateTime(2025, 6, 1);
        for (int i = 0; i < 7; i++) PeripheralWear.CountKey(second, 65);
        StringBuilder sb = new StringBuilder();
        PeripheralWear.AppendProfiles(sb);
        List<string> headers = new List<string>();
        List<string> keys = new List<string>();
        string[] lines = sb.ToString().Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("keyboard_v1=")) headers.Add(lines[i].Substring(12));
            else if (lines[i].StartsWith("keyboard_keys_v1=")) keys.Add(lines[i].Substring(17));
        }
        Check(headers.Count == 2 && keys.Count == 2, "多档案行数");
        PeripheralWear.BeginLoad();
        PeripheralWear.LoadRaw("", "", "", false, false, headers, keys);
        KeyboardProfile restored = PeripheralWear.FindKeyboard("dev:second");
        Check(restored != null, "第二把键盘回读");
        Equal(restored.ReadKey(65), 7, "第二把键盘键数回读");
        Check(restored.Rated == 60000000 && restored.Since == new DateTime(2025, 6, 1), "第二把配置回读");
    }

    private static void Legacy()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.KeyboardName = "旧键盘";
        PeripheralWear.KeyboardRated = 50000000;
        PeripheralWear.KeyboardSince = new DateTime(2024, 1, 1);
        for (int i = 0; i < 9; i++) PeripheralWear.CountKey(32);
        string profile = PeripheralWear.EncodeProfile();
        string keys = PeripheralWear.EncodeKeys();
        PeripheralWear.BeginLoad();
        PeripheralWear.LoadRaw(profile, keys, "0|0|0|0", true, true);
        KeyboardProfile legacy = PeripheralWear.FindKeyboard(PeripheralWear.LegacyId);
        Check(legacy != null && legacy.Legacy, "旧版单键盘成为 legacy");
        Equal(legacy.ReadKey(32), 9, "legacy 键数保留");
        KeyboardProfile target = Add("dev:new", "新键盘", 50000000, 0x231);
        Check(PeripheralWear.MergeLegacyInto(target.Id), "legacy 可归入当前键盘");
        Equal(target.ReadKey(32), 9, "归并后键数保留");
        Check(PeripheralWear.FindKeyboard(PeripheralWear.LegacyId) == null, "归并后 legacy 移除");
        Equal(PeripheralWear.TopKeys("", 5)[0].Count, 9, "聚合仍可见旧累计");
    }

    private static void FreshState()
    {
        // 新装首次保存会写 hardware_v1 + wear_keys_v1=-,不能凭空长出 legacy 空档案。
        PeripheralWear.BeginLoad();
        PeripheralWear.LoadRaw("|50000000|0|鼠标|80000000|80000000|0|1",
            "-", "0|0|0|0", true, true);
        PeripheralWear.EnsureSeeded(new List<DayRecord>(), new Counters());
        Check(PeripheralWear.SnapshotKeyboards(false).Count == 0, "空 wear_keys 不创建 legacy");
        Check(PeripheralWear.FindKeyboard(PeripheralWear.LegacyId) == null, "无历史不创建 legacy");

        // 用户主动重置后 wear_keys_v1=-:历史总量不能再把旧计数灌回来。
        List<DayRecord> oldHistory = new List<DayRecord>();
        DayRecord oldDay = new DayRecord();
        oldDay.Date = new DateTime(2025, 2, 1);
        oldDay.KeyCounts[32] = 42;
        oldHistory.Add(oldDay);
        PeripheralWear.EnsureSeeded(oldHistory, new Counters());
        Check(PeripheralWear.TotalKeyPressesAll == 0, "重置后不按历史回填");
        Check(PeripheralWear.SnapshotKeyboards(false).Count == 0, "重置后仍无 legacy");

        // 空占位在新设备解析时被清理。
        PeripheralWear.BeginLoad();
        PeripheralWear.EnsureDefault();
        Add("dev:first", "键盘一", 100, 0x301);
        Check(PeripheralWear.FindKeyboard("primary") == null, "新设备接管后清理空占位");

        // 只有历史记录时,升级为可归并的 legacy,而不是伪装成当前键盘。
        PeripheralWear.BeginLoad();
        PeripheralWear.LoadRaw("", "", "", false, false);
        List<DayRecord> history = new List<DayRecord>();
        DayRecord day = new DayRecord();
        day.Date = new DateTime(2025, 3, 4);
        day.KeyCounts[32] = 12;
        history.Add(day);
        PeripheralWear.EnsureSeeded(history, new Counters());
        KeyboardProfile legacy = PeripheralWear.FindKeyboard(PeripheralWear.LegacyId);
        Check(legacy != null && legacy.Legacy && legacy.ReadKey(32) == 12, "历史计数升级为 legacy");
        Check(PeripheralWear.SnapshotKeyboards(false).Count == 1, "历史 legacy 正常显示");
    }
}
