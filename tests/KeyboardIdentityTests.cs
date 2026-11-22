// ============================================================================
//  tests/KeyboardIdentityTests.cs  ·  v3.4.1 RawInput 设备身份
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading;
using KeyMouseStats;

internal static class KeyboardIdentityTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(string actual, string expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("KeyboardIdentityTests", Run);
    }

    private static void Run()
    {
        Paths();
        Classes();
        HeaderRoundTrip();
        Registry();
        Console.WriteLine("KeyboardIdentityTests: " + _checks + " 项断言通过");
    }

    private static void Paths()
    {
        string hid = KeyboardDeviceIdentity.NormalizePath(
            @"\\?\HID#VID_046D&PID_C31C&MI_00#7&1A2B3C4D&0&0000#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}");
        Equal(hid, @"HID\VID_046D&PID_C31C&MI_00\7&1A2B3C4D&0&0000", "HID 路径归一化");
        string acpi = KeyboardDeviceIdentity.NormalizePath(
            @"\\?\ACPI#PNP0303#4&2A1B3C4D&0&0000#{4D36E96B-E325-11CE-BFC1-08002BE10318}");
        Equal(acpi, @"ACPI\PNP0303\4&2A1B3C4D&0&0000", "ACPI 路径归一化");
        Equal(KeyboardDeviceIdentity.HardwareKeyOf(hid), @"HID\VID_046D&PID_C31C&MI_00", "HID 硬件键");
        Equal(KeyboardDeviceIdentity.HardwareKeyOf(acpi), @"ACPI\PNP0303", "ACPI 硬件键");
        Equal(KeyboardDeviceIdentity.HardwareKeyOf(@"BTHENUM\DEV_ABC\8&1"), @"BTHENUM\DEV_ABC", "蓝牙硬件键");
    }

    private static void Classes()
    {
        Check(KeyboardDeviceIdentity.IsBuiltIn(@"ACPI\PNP0303\4&1", ""), "ACPI PNP0303 判内置");
        Check(KeyboardDeviceIdentity.IsBuiltIn(@"HID\INTC\...", ""), "HID INTC 判内置");
        Check(KeyboardDeviceIdentity.IsBuiltIn(@"HID\VID_8087&PID_0A1E\...", ""), "Intel HID 判内置");
        Check(!KeyboardDeviceIdentity.IsBuiltIn(@"HID\VID_046D&PID_C31C\...", ""), "Logitech 外接不误判");
        Check(!KeyboardDeviceIdentity.IsBuiltIn(@"BTHENUM\...", ""), "蓝牙外接不误判");
        Check(KeyboardDeviceIdentity.IsBuiltIn(@"HID\VID_046D&PID_C31C\...", "笔记本内置键盘"),
            "友好名内置提示可补充判定");
    }

    private static void HeaderRoundTrip()
    {
        KeyboardProfile source = new KeyboardProfile();
        source.Id = "dev:ABC";
        source.Alias = "我的键盘";
        source.FriendlyName = "USB Keyboard";
        source.DevicePath = @"HID\VID_1234&PID_5678\1";
        source.HardwareKey = @"HID\VID_1234&PID_5678";
        source.InstanceKey = @"HID\VID_1234&PID_5678\1";
        source.BuiltIn = true;
        source.Rated = 50000000;
        source.Since = new DateTime(2026, 2, 3);
        source.Seeded = true;
        source.LastSeenUtcTicks = 123456;
        KeyboardProfile copy = KeyboardProfile.DecodeHeader(source.EncodeHeader());
        Check(copy != null, "档案头可解码");
        Equal(copy.Id, source.Id, "Id 往返");
        Equal(copy.Alias, source.Alias, "别名往返");
        Equal(copy.FriendlyName, source.FriendlyName, "友好名往返");
        Equal(copy.HardwareKey, source.HardwareKey, "硬件键往返");
        Check(copy.BuiltIn && copy.Seeded, "标记往返");
        Check(copy.Since == source.Since, "起始时间往返");
        Equal(copy.Rated.ToString(), source.Rated.ToString(), "额定寿命往返");
    }

    private static void Registry()
    {
        PeripheralWear.BeginLoad();
        KeyboardDeviceRegistry.ResetForTest();
        IntPtr handle = new IntPtr(0x9911);
        KeyboardDeviceRegistry.HandleRawKey(handle, 0x41, 0x1E, 0, false);
        Check(PeripheralWear.SnapshotKeyboards(true).Count == 0, "迟到抬起不建幽灵档案");

        KeyboardDeviceRegistry.HandleRawKey(handle, 0x41, 0x1E, 0, true);
        KeyboardDeviceRegistry.HandleRawKey(handle, 0x41, 0x1E, 0, true);   // 自动连发
        KeyboardDeviceRegistry.HandleRawKey(handle, 0x41, 0x1E, 0, false);
        KeyboardDeviceRegistry.HandleRawKey(handle, 0x41, 0x1E, 0, true);

        KeyboardProfile profile = null;
        for (int i = 0; i < 200 && profile == null; i++)
        {
            List<KeyboardProfile> all = PeripheralWear.SnapshotKeyboards(false);
            if (all.Count > 0) profile = all[0];
            else Thread.Sleep(10);
        }
        Check(profile != null, "RawInput 设备创建档案");
        Check(profile.TotalKeyPresses == 2, "连发只算一次,抬起后重按再 +1");

        KeyboardDeviceRegistry.OnDeviceRemoved(handle);
        KeyboardDeviceRegistry.ResetForTest();
        PeripheralWear.BeginLoad();
    }
}
