// ============================================================================
//  tests/MouseIdentityTests.cs  ·  v3.4.1 RawInput 鼠标身份与路由
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using KeyMouseStats;

internal static class MouseIdentityTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("MouseIdentityTests", Run);
    }

    private static void Run()
    {
        Classes();
        HeaderRoundTrip();
        Registry();
        Console.WriteLine("MouseIdentityTests: " + _checks + " 项断言通过");
    }

    private static void Classes()
    {
        Check(MouseDeviceIdentity.IsBuiltIn(@"ACPI\PNP0C50\4&1", ""), "ACPI 触控板判内置");
        Check(MouseDeviceIdentity.IsBuiltIn(@"HID\VID_06CB&PID_CE44\1", ""), "Synaptics 判内置");
        Check(MouseDeviceIdentity.IsBuiltIn(@"HID\VID_04F3&PID_3140\1", ""), "Elan 判内置");
        Check(!MouseDeviceIdentity.IsBuiltIn(@"HID\VID_046D&PID_B034\1", ""), "罗技鼠标不误判");
        Check(MouseDeviceIdentity.IsBuiltIn(@"HID\VID_046D&PID_B034\1", "Touchpad"), "友好名触控板判内置");
        Check(!MouseDeviceIdentity.IsBuiltIn(@"HID\VID_046D&PID_B034\1", "外接鼠标"), "友好名外接鼠标不误判");
    }

    private static void HeaderRoundTrip()
    {
        MouseProfile source = new MouseProfile();
        source.Id = "mouse:dev:T";
        source.Alias = "家里 · MX Master";
        source.FriendlyName = "Logitech MX Master";
        source.DevicePath = @"HID\VID_046D&PID_B034\1";
        source.HardwareKey = @"HID\VID_046D&PID_B034";
        source.InstanceKey = @"HID\VID_046D&PID_B034\1";
        source.BuiltIn = true;
        source.RatedLeft = 80000000;
        source.RatedRight = 70000000;
        source.Since = new DateTime(2026, 3, 4);
        source.Seeded = true;
        source.LastSeenUtcTicks = 9876;
        MouseProfile copy = MouseProfile.DecodeHeader(source.EncodeHeader());
        Check(copy != null, "鼠标档案头可解码");
        Check(copy.Id == source.Id && copy.Alias == source.Alias, "鼠标 Id/别名往返");
        Check(copy.FriendlyName == source.FriendlyName && copy.HardwareKey == source.HardwareKey, "鼠标识别信息往返");
        Check(copy.BuiltIn && copy.Seeded, "鼠标标记往返");
        Check(copy.RatedLeft == source.RatedLeft && copy.RatedRight == source.RatedRight, "左右额定往返");
        Check(copy.Since == source.Since, "起始日期往返");

        source.DecodeButtons("3,4,5,6");
        Check(source.Read(1) == 3 && source.Read(2) == 4 && source.Read(3) == 5 && source.Read(4) == 6, "按键计数往返");
        source.Count(1, new DateTime(2026, 3, 4));
        source.Count(2, new DateTime(2026, 3, 5));
        string days = source.EncodeDays();
        MouseProfile dayCopy = new MouseProfile();
        dayCopy.DecodeDays(days.Substring(days.IndexOf('|') + 1));
        long[] values = new long[7];
        dayCopy.CopyDaysTo(values);
        Check(values[5] == 1 && values[6] == 1, "近 7 天点击环往返");
    }

    private static void Registry()
    {
        PeripheralMouseWear.BeginLoad();
        MouseDeviceRegistry.ResetForTest();
        IntPtr handle = new IntPtr(0x9921);
        DateTime now = DateTime.Today;
        MouseDeviceRegistry.HandleButtons(handle, 0x0001, now);
        MouseDeviceRegistry.HandleButtons(handle, 0x0004, now);
        MouseDeviceRegistry.HandleButtons(handle, 0x0002, now);   // 左键抬起不应新增
        MouseWearSnapshot snapshot = null;
        for (int i = 0; i < 200 && snapshot == null; i++)
        {
            List<MouseProfile> list = PeripheralMouseWear.SnapshotMice(false);
            if (list.Count > 0) snapshot = MouseWearSnapshot.Capture(list[0].Id);
            else Thread.Sleep(10);
        }
        Check(snapshot != null, "鼠标 RawInput 创建档案");
        Check(snapshot.Left == 1 && snapshot.Right == 1, "左右键按下各计一次,抬起不计");
        MouseDeviceRegistry.OnDeviceRemoved(handle);
        Check(PeripheralMouseWear.SnapshotMice(false).Count == 1, "拔出后档案保留");
        MouseDeviceRegistry.ResetForTest();
        PeripheralMouseWear.BeginLoad();
    }
}
