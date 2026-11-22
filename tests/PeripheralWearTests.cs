// ============================================================================
//  tests/PeripheralWearTests.cs  ·  v3.4 外设硬件纪元计数器
//  只测纯内存逻辑:计数、重置隔离、编解码、首次估算 seeding、TOP5/百分比。
// ============================================================================
using System;
using System.Collections.Generic;
using KeyMouseStats;

internal static class PeripheralWearTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("PeripheralWearTests", Run);
    }

    private static void Run()
    {
        Counting();
        TopKeys();
        ResetIsolation();
        RoundTrip();
        StoreRoundTrip();
        ImportIsolation();
        Seeding();
        Malformed();
        Mathematics();
        Console.WriteLine("PeripheralWearTests: " + _checks + " 项断言通过");
    }

    private static void Counting()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.CountKey(32);
        PeripheralWear.CountKey(32);
        PeripheralWear.CountKey(-1);
        PeripheralWear.CountKey(999);
        Equal(PeripheralWear.ReadKey(32), 2, "空格计数");
        Equal(PeripheralWear.ReadKey(999), 0, "越界 vk 不计数");
        PeripheralWear.CountClick(1);
        PeripheralWear.CountClick(1);
        PeripheralWear.CountClick(2);
        PeripheralWear.CountClick(3);
        PeripheralWear.CountClick(4);
        PeripheralWear.CountClick(5);
        Equal(PeripheralWear.LeftClicks, 2, "左键计数");
        Equal(PeripheralWear.RightClicks, 1, "右键计数");
        Equal(PeripheralWear.MiddleClicks, 1, "中键计数");
        Equal(PeripheralWear.XButtons, 2, "侧键计数");
        Equal(PeripheralWear.TotalClicks, 6, "鼠标总计数");
    }

    private static void TopKeys()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.CountKey(65);
        PeripheralWear.CountKey(65);
        PeripheralWear.CountKey(65);
        PeripheralWear.CountKey(32);
        PeripheralWear.CountKey(32);
        PeripheralWear.CountKey(160);
        PeripheralWear.CountKey(161);
        PeripheralWear.CountKey(13);
        List<KeyWear> top = PeripheralWear.TopKeys(5);
        Check(top.Count == 4, "TOP5 按显示名合并左右 Shift");
        Check(top[0].Name == "A" && top[0].Count == 3, "第一名 A");
        Check(top[1].Count == 2 && top[2].Count == 2, "第二三名同为 2 次");
        Check((top[1].Name == "空格" && top[2].Name == "Shift")
            || (top[1].Name == "Shift" && top[2].Name == "空格"), "空格与左右 Shift 合并");
    }

    private static void ResetIsolation()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.CountKey(32);
        PeripheralWear.CountKey(65);
        PeripheralWear.CountClick(1);
        PeripheralWear.CountClick(2);
        Store.Total = new Counters();
        Store.Total.Keys = 123456;
        Store.Total.Left = 700;
        Store.Total.Right = 300;
        Store.History.Clear();
        DayRecord day = new DayRecord();
        day.Date = DateTime.Today;
        day.Keys = 42;
        Store.History[day.Date] = day;
        long historyCount = Store.History.Count;

        PeripheralWear.ResetKeyboard(new DateTime(2026, 11, 21, 10, 0, 0));
        Equal(PeripheralWear.ReadKey(32), 0, "换新键盘清零按键");
        Equal(PeripheralWear.LeftClicks, 1, "换新键盘不动鼠标");
        Equal(Store.Total.Keys, 123456, "换新键盘不动 Total.Keys");
        Equal(Store.Total.Left, 700, "换新键盘不动 Total.Left");
        Equal(Store.History.Count, historyCount, "换新键盘不动 History");
        PeripheralWear.CountKey(65);

        PeripheralWear.ResetMouse(new DateTime(2026, 11, 21, 11, 0, 0));
        Equal(PeripheralWear.LeftClicks, 0, "换新鼠标清零左键");
        Equal(PeripheralWear.RightClicks, 0, "换新鼠标清零右键");
        Equal(PeripheralWear.ReadKey(65), 1, "换新鼠标不动键盘");
        Equal(Store.Total.Keys, 123456, "换新鼠标不动 Total.Keys");
        Equal(Store.Total.Left, 700, "换新鼠标不动 Total.Left");
    }

    private static void RoundTrip()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.KeyboardName = "测试键盘 87";
        PeripheralWear.KeyboardRated = 50000000;
        PeripheralWear.KeyboardSince = new DateTime(2025, 5, 1);
        PeripheralWear.MouseName = "测试鼠标";
        PeripheralWear.MouseRatedLeft = 80000000;
        PeripheralWear.MouseRatedRight = 60000000;
        PeripheralWear.MouseSince = new DateTime(2025, 6, 2);
        PeripheralWear.Seeded = true;
        PeripheralWear.CountKey(32);
        PeripheralWear.CountKey(32);
        PeripheralWear.CountKey(65);
        PeripheralWear.CountClick(1);
        PeripheralWear.CountClick(2);
        PeripheralWear.CountClick(3);
        PeripheralWear.CountClick(4);

        string profile = PeripheralWear.EncodeProfile();
        string keys = PeripheralWear.EncodeKeys();
        string mouse = PeripheralWear.EncodeMouse();
        PeripheralWear.BeginLoad();
        PeripheralWear.LoadRaw(profile, keys, mouse, true, true);

        Check(PeripheralWear.KeyboardName == "测试键盘 87", "键盘名往返");
        Check(PeripheralWear.MouseName == "测试鼠标", "鼠标名往返");
        Equal(PeripheralWear.KeyboardRated, 50000000, "键盘额定往返");
        Equal(PeripheralWear.MouseRatedLeft, 80000000, "左微动额定往返");
        Equal(PeripheralWear.MouseRatedRight, 60000000, "右微动额定往返");
        Check(PeripheralWear.KeyboardSince == new DateTime(2025, 5, 1), "键盘起始往返");
        Check(PeripheralWear.MouseSince == new DateTime(2025, 6, 2), "鼠标起始往返");
        Check(PeripheralWear.Seeded, "Seeded 往返");
        Equal(PeripheralWear.ReadKey(32), 2, "按键计数往返");
        Equal(PeripheralWear.ReadKey(65), 1, "第二个按键计数往返");
        Equal(PeripheralWear.LeftClicks, 1, "左键计数往返");
        Equal(PeripheralWear.RightClicks, 1, "右键计数往返");
        Equal(PeripheralWear.MiddleClicks, 1, "中键计数往返");
        Equal(PeripheralWear.XButtons, 1, "侧键计数往返");
    }

    private static void StoreRoundTrip()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.KeyboardName = "Store 键盘";
        PeripheralWear.KeyboardRated = 50000000;
        PeripheralWear.KeyboardSince = new DateTime(2026, 2, 3);
        PeripheralWear.MouseName = "Store 鼠标";
        PeripheralWear.MouseRatedLeft = 80000000;
        PeripheralWear.MouseRatedRight = 80000000;
        PeripheralWear.MouseSince = new DateTime(2026, 2, 4);
        PeripheralWear.Seeded = true;
        PeripheralWear.CountKey(32);
        PeripheralWear.CountClick(1);
        Store.ClickScreenDevice = @"\\.\DISPLAY2";
        Store.ClickScreenBounds = new System.Drawing.Rectangle(1920, 0, 2560, 1440);
        Store.History.Clear();
        Store.Total = new Counters();
        DayRecord today = new DayRecord();
        today.Date = DateTime.Today;
        today.Keys = 1;
        Store.History[DateTime.Today] = today;

        string payload = Store.ExportPayload();
        Check(!string.IsNullOrEmpty(payload), "ExportPayload 不为空");
        Check(payload.IndexOf("hardware_v1=") >= 0, "stats.txt 含 hardware_v1");
        Check(payload.IndexOf("wear_keys_v1=") >= 0, "stats.txt 含 wear_keys_v1");
        Check(payload.IndexOf("wear_mouse_v1=") >= 0, "stats.txt 含 wear_mouse_v1");
        Check(payload.IndexOf("mouse_v1=") >= 0, "stats.txt 含 mouse_v1");
        Check(payload.IndexOf("mouse_buttons_v1=") >= 0, "stats.txt 含 mouse_buttons_v1");
        Check(payload.IndexOf("mouse_days_v1=") >= 0, "stats.txt 含 mouse_days_v1");
        Check(payload.IndexOf("click_screen_v1=") >= 0, "stats.txt 含 click_screen_v1");
        Store.Parsed parsed = Store.ParseText(payload);
        Check(!string.IsNullOrEmpty(parsed.Hardware), "hardware_v1 可回读");
        Check(parsed.HasWearKeys && parsed.HasWearMouse, "wear_* 行可回读");
        Check(parsed.MouseProfiles.Count > 0 && parsed.MouseButtons.Count > 0 && parsed.MouseDays.Count > 0,
            "mouse_* 行可回读");
        Check(parsed.HasClickScreen, "click_screen_v1 可回读");
        Check(parsed.ClickScreenDevice == @"\\.\DISPLAY2", "选屏设备名回读");
    }

    private static void ImportIsolation()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.KeyboardName = "本机键盘";
        PeripheralWear.Seeded = true;
        PeripheralWear.CountKey(32);
        Store.ClickScreenDevice = @"\\.\DISPLAY1";
        Store.ClickScreenBounds = new System.Drawing.Rectangle(0, 0, 1920, 1080);

        Store.Parsed imported = Store.ParseText(
            "hardware_v1=|80000000|0|外来鼠标|80000000|80000000|0|1\n"
            + "wear_keys_v1=65:999\nwear_mouse_v1=9|9|9|9\n"
            + "click_screen_v1=\\\\.\\DISPLAY9|0|0|3840|2160\n"
            + "[day=2026-11-20]\nkeys=1\n");
        Store.ApplyParsed(imported, true, false);
        Check(PeripheralWear.KeyboardName == "本机键盘", "导入不覆盖本机键盘名");
        Equal(PeripheralWear.ReadKey(32), 1, "导入不覆盖本机键盘纪元");
        Equal(PeripheralWear.ReadKey(65), 0, "导入不写入外来键盘计数");
        Check(Store.ClickScreenDevice == @"\\.\DISPLAY1", "导入不覆盖本机选屏身份");
    }

    private static void Seeding()
    {
        PeripheralWear.ResetForTest();
        Store.Total = new Counters();
        List<DayRecord> history = new List<DayRecord>();
        DayRecord old = new DayRecord();
        old.Date = new DateTime(2025, 1, 2);
        old.KeyCounts[32] = 10;
        old.KeyCounts[65] = 4;
        old.Left = 30;
        history.Add(old);
        DayRecord newer = new DayRecord();
        newer.Date = new DateTime(2025, 1, 3);
        newer.KeyCounts[32] = 5;
        newer.Right = 7;
        history.Add(newer);
        DayRecord physicalOnly = new DayRecord();
        physicalOnly.Date = new DateTime(2025, 1, 4);
        physicalOnly.PhysicalVks[83] = 2;
        history.Add(physicalOnly);
        Store.Total.Left = 100;
        Store.Total.Right = 50;
        Store.Total.Middle = 8;
        Store.Total.XButtons = 2;

        PeripheralWear.LoadRaw("", "", "", false, false);
        PeripheralWear.EnsureSeeded(history, Store.Total);
        Check(PeripheralWear.Seeded, "首次加载置 Seeded");
        Equal(PeripheralWear.ReadKey(32), 15, "键盘从 KeyCounts 估算");
        Equal(PeripheralWear.ReadKey(65), 4, "第二个键估算");
        Equal(PeripheralWear.ReadKey(83), 2, "仅 PhysicalVks 的旧记录也能估算");
        Equal(PeripheralWear.LeftClicks, 100, "鼠标左键继承 Total");
        Equal(PeripheralWear.RightClicks, 50, "鼠标右键继承 Total");
        Equal(PeripheralWear.MiddleClicks, 8, "鼠标中键继承 Total");
        Equal(PeripheralWear.XButtons, 2, "鼠标侧键继承 Total");
        Check(PeripheralWear.KeyboardSince == new DateTime(2025, 1, 2), "键盘起点取最早历史日");
    }

    private static void Malformed()
    {
        PeripheralWear.ResetForTest();
        PeripheralWear.LoadProfile("bad|not-a-number|x|y|z");
        Equal(PeripheralWear.KeyboardRated, PeripheralWear.DefaultKeyboardRated, "坏配置回退键盘额定");
        PeripheralWear.LoadKeys("32:5,999:7,-1:9,bad,65:-2");
        Equal(PeripheralWear.ReadKey(32), 5, "合法键位保留");
        Equal(PeripheralWear.ReadKey(65), 0, "负计数丢弃");
        PeripheralWear.LoadMouse("4|bad|6");
        Equal(PeripheralWear.LeftClicks, 4, "鼠标坏行保留第一段");
        Equal(PeripheralWear.RightClicks, 0, "鼠标坏行负值丢弃");
        PeripheralWear.LoadProfile("|50000000|0|Mouse|80000000|80000000|1|1");
        Check(PeripheralWear.KeyboardName == "主力键盘", "空名回退默认键盘名");
        Check(PeripheralWear.Seeded, "空名配置仍可读 Seeded");
    }

    private static void Mathematics()
    {
        Equal((long)(PeripheralWear.Progress(50, 100) * 100), 50, "50% 进度");
        Equal((long)(PeripheralWear.Progress(150, 100) * 100), 100, "超限进度夹到 100%");
        Equal((long)(PeripheralWear.Ratio(150, 100) * 100), 150, "Ratio 保留真实百分比");
        Equal((long)(PeripheralWear.Progress(0, 0) * 100), 0, "零额定零计数");
        Equal((long)(PeripheralWear.Progress(1, 0) * 100), 100, "零额定有计数按满格");
    }
}
