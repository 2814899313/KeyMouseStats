// ============================================================================
//  tests/ClickScreenIdentityTests.cs  ·  v3.4 点击热区稳定选屏
//  纯解析器测试:设备名优先、边界兜底、重排/拔插/重启恢复。
// ============================================================================
using System;
using System.Drawing;
using KeyMouseStats;

internal static class ClickScreenIdentityTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("ClickScreenIdentityTests", Run);
    }

    private static ScreenRef[] Screens()
    {
        return new ScreenRef[]
        {
            new ScreenRef(@"\\.\DISPLAY1", new Rectangle(0, 0, 1920, 1080), true),
            new ScreenRef(@"\\.\DISPLAY2", new Rectangle(1920, 0, 2560, 1440), false),
            new ScreenRef(@"\\.\DISPLAY3", new Rectangle(-1920, 0, 1920, 1080), false)
        };
    }

    private static void Run()
    {
        DeviceFirst();
        BoundsFallback();
        PlugAndReplay();
        HashAndStore();
        Console.WriteLine("ClickScreenIdentityTests: " + _checks + " 项断言通过");
    }

    private static void DeviceFirst()
    {
        ScreenRef[] screens = Screens();
        Equal(ClickScreenIdentity.Resolve(@"\\.\DISPLAY2", Rectangle.Empty, screens), 1, "设备名精确匹配");
        Equal(ClickScreenIdentity.Resolve(@"\\.\display2", Rectangle.Empty, screens), 1, "设备名忽略大小写");
        // 重排后仍按设备名选中同一块屏,而不是旧下标。
        ScreenRef[] reordered = { screens[2], screens[0], screens[1] };
        Equal(ClickScreenIdentity.Resolve(@"\\.\DISPLAY2", Rectangle.Empty, reordered), 2, "重排后按设备名恢复");
    }

    private static void BoundsFallback()
    {
        ScreenRef[] screens = Screens();
        Equal(ClickScreenIdentity.Resolve("", new Rectangle(-1920, 0, 1920, 1080), screens), 2, "边界精确匹配");
        Equal(ClickScreenIdentity.Resolve(@"\\.\DISPLAY9", new Rectangle(1920, 0, 2560, 1440), screens), 1,
            "设备名失效时边界兜底");
        Equal(ClickScreenIdentity.Resolve("", new Rectangle(1930, 10, 2560, 1440), screens), 1,
            "宽高相同 + 最近中心点");
        Equal(ClickScreenIdentity.Resolve("", new Rectangle(10, 10, 100, 100), screens), 0, "找不到回主屏");
        ScreenRef[] noPrimary = { screens[2], screens[1] };
        Equal(ClickScreenIdentity.Resolve("", new Rectangle(1, 1, 10, 10), noPrimary), 0, "无主屏回第一块");
        Equal(ClickScreenIdentity.Resolve("", Rectangle.Empty, new ScreenRef[0]), 0, "空屏数组安全");
    }

    private static void PlugAndReplay()
    {
        ScreenRef[] screens = Screens();
        Rectangle hint = new Rectangle(1920, 0, 2560, 1440);
        Equal(ClickScreenIdentity.Resolve(@"\\.\DISPLAY2", hint, screens), 1, "拔插前选中屏幕 2");
        // 模拟屏幕 2 被拔掉:只剩屏 1/3,回退主屏;设备名仍保留在调用方。
        ScreenRef[] unplugged = { screens[0], screens[2] };
        Equal(ClickScreenIdentity.Resolve(@"\\.\DISPLAY2", hint, unplugged), 0, "拔掉后回主屏");
        // 插回后设备名重新命中,恢复到原屏。
        Equal(ClickScreenIdentity.Resolve(@"\\.\DISPLAY2", hint, screens), 1, "插回后恢复原屏");
        // 显示器换到另一个接口:DeviceName 变了,用边界恢复。
        ScreenRef[] renamed = { screens[0], new ScreenRef(@"\\.\DISPLAY9", hint, false), screens[2] };
        Equal(ClickScreenIdentity.Resolve(@"\\.\DISPLAY2", hint, renamed), 1, "设备名变化时按边界恢复");
    }

    private static void HashAndStore()
    {
        long first = ClickScreenIdentity.Hash(@"\\.\DISPLAY2", new Rectangle(1920, 0, 2560, 1440));
        long second = ClickScreenIdentity.Hash(@"\\.\DISPLAY2", new Rectangle(1920, 0, 2560, 1440));
        long other = ClickScreenIdentity.Hash(@"\\.\DISPLAY3", new Rectangle(1920, 0, 2560, 1440));
        Check(first == second, "同设备同边界哈希稳定");
        Check(first != other, "不同设备哈希不同");

        Store.Parsed parsed = Store.ParseText(
            "click_screen_v1=\\\\.\\DISPLAY2|1920|0|2560|1440\n");
        Check(parsed.HasClickScreen, "click_screen_v1 被识别");
        Check(parsed.ClickScreenDevice == @"\\.\DISPLAY2", "设备名解析");
        Check(parsed.ClickScreenBounds == new Rectangle(1920, 0, 2560, 1440), "边界解析");
        Store.Parsed invalid = Store.ParseText("click_screen_v1=\\\\.\\DISPLAY2|1|2|0|0\n");
        Check(!invalid.HasClickScreen, "零尺寸边界拒绝");
    }
}
