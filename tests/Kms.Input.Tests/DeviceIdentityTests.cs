using Kms.Core.Model;
using Kms.Input.Devices;

namespace Kms.Input.Tests;

public sealed class DeviceIdentityTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NormalizePath_MatchesV3()
    {
        string hid = KeyboardDeviceIdentity.NormalizePath(
            @"\\?\HID#VID_046D&PID_C31C&MI_00#7&1A2B3C4D&0&0000#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}");
        Assert.Equal(@"HID\VID_046D&PID_C31C&MI_00\7&1A2B3C4D&0&0000", hid);

        string acpi = KeyboardDeviceIdentity.NormalizePath(
            @"\\?\ACPI#PNP0303#4&2A1B3C4D&0&0000#{4D36E96B-E325-11CE-BFC1-08002BE10318}");
        Assert.Equal(@"ACPI\PNP0303\4&2A1B3C4D&0&0000", acpi);

        Assert.Equal("", KeyboardDeviceIdentity.NormalizePath(null));
        Assert.Equal("", KeyboardDeviceIdentity.NormalizePath("   "));
    }

    [Theory]
    [InlineData(@"HID\VID_046D&PID_C31C&MI_00\7&1A2B3C4D&0&0000", @"HID\VID_046D&PID_C31C&MI_00")]
    [InlineData(@"ACPI\PNP0303\4&2A1B3C4D&0&0000", @"ACPI\PNP0303")]
    [InlineData(@"BTHENUM\DEV_ABC\8&1", @"BTHENUM\DEV_ABC")]
    [InlineData(@"USB\VID_1234&PID_5678\5&1", @"USB\VID_1234&PID_5678\5&1")]
    public void HardwareKey_MatchesV3(string instance, string expected)
    {
        Assert.Equal(expected, KeyboardDeviceIdentity.HardwareKeyOf(instance));
    }

    [Theory]
    [InlineData(@"ACPI\PNP0303\4&1", "", true)]
    [InlineData(@"ACPI\PNP030B\4&2", "", true)]
    [InlineData(@"ACPI\PNP0320\4&3", "", true)]
    [InlineData(@"HID\INTC\1", "", true)]
    [InlineData(@"HID\VID_8087&PID_0A1E\1", "", true)]
    [InlineData(@"HID\VID_046D&PID_C31C\1", "", false)]
    [InlineData(@"BTHENUM\DEV_ABC\8&1", "", false)]
    [InlineData(@"HID\VID_046D&PID_C31C\1", "笔记本内置键盘", true)]
    [InlineData(@"HID\VID_046D&PID_C31C\1", "Internal Keyboard", true)]
    public void KeyboardBuiltIn_MatchesV3(string instance, string friendly, bool expected)
    {
        Assert.Equal(expected, KeyboardDeviceIdentity.IsBuiltIn(instance, friendly));
    }

    [Theory]
    [InlineData(@"ACPI\PNP0C50\4&1", "", true)]
    [InlineData(@"HID\VID_06CB&PID_CE44\1", "", true)]
    [InlineData(@"HID\VID_04F3&PID_3140\1", "", true)]
    [InlineData(@"HID\VID_8087&PID_0A1E\1", "", true)]
    [InlineData(@"HID\VID_046D&PID_B034\1", "", false)]
    [InlineData(@"HID\VID_046D&PID_B034\1", "Touchpad", true)]
    [InlineData(@"HID\VID_046D&PID_B034\1", "触控板", true)]
    [InlineData(@"HID\VID_046D&PID_B034\1", "外接鼠标", false)]
    public void MouseBuiltIn_MatchesV3(string instance, string friendly, bool expected)
    {
        Assert.Equal(expected, MouseDeviceIdentity.IsBuiltIn(instance, friendly));
    }

    [Fact]
    public void Fnv_MatchesV3CharacterHash()
    {
        Assert.Equal("CBF29CE484222325", KeyboardDeviceIdentity.Fnv("").ToString("X16"));
        Assert.Equal("70D62F522D9F6932", KeyboardDeviceIdentity.Fnv("KeyMouseStats").ToString("X16"));
        Assert.Equal(
            "7398B6AD81ED31E8",
            KeyboardDeviceIdentity.Fnv(@"HID\VID_046D&PID_C31C&MI_00\7&1A2B3C4D&0&0000")
                .ToString("X16"));
        Assert.NotEqual(KeyboardDeviceIdentity.Fnv("A"), KeyboardDeviceIdentity.Fnv("a"));
    }

    [Fact]
    public void Resolve_InvalidHandleNeverThrows()
    {
        Exception? error = Record.Exception(() =>
        {
            _ = KeyboardDeviceIdentity.Resolve(IntPtr.Zero);
            _ = MouseDeviceIdentity.Resolve(IntPtr.Zero);
        });

        Assert.Null(error);
    }

    [Fact]
    public void KeyboardIdentity_CreateUsesStableIdAndFallbackName()
    {
        const string path =
            @"\\?\HID#VID_046D&PID_C31C&MI_00#7&1A2B3C4D&0&0000#{884B96C3-56EF-11D1-BC8C-00A0C91405DD}";
        KeyboardIdentity identity = Assert.IsType<KeyboardIdentity>(KeyboardDeviceIdentity.Create(path, null));

        Assert.False(identity.BuiltIn);
        Assert.Equal(@"HID\VID_046D&PID_C31C&MI_00\7&1A2B3C4D&0&0000", identity.InstanceKey);
        Assert.Equal(@"HID\VID_046D&PID_C31C&MI_00", identity.HardwareKey);
        Assert.Equal("dev:7398B6AD81ED31E8", identity.Id);
        Assert.StartsWith("外接键盘 · VID_046D PID_C31C MI_00", identity.FriendlyName);

        Assert.Null(KeyboardDeviceIdentity.Create("", "ignored"));
        Assert.Null(KeyboardDeviceIdentity.Create(null, null));
    }

    [Fact]
    public void KeyboardIdentity_FriendlyNameCanPromoteBuiltIn()
    {
        KeyboardIdentity identity = Assert.IsType<KeyboardIdentity>(KeyboardDeviceIdentity.Create(
            @"HID\VID_046D&PID_C31C\1",
            "笔记本内置键盘"));

        Assert.True(identity.BuiltIn);
        Assert.Equal("笔记本内置键盘", identity.FriendlyName);
        Assert.StartsWith("builtin:", identity.Id);
    }

    [Fact]
    public void MouseIdentity_CreateUsesTrackpadHeuristic()
    {
        MouseIdentity trackpad = Assert.IsType<MouseIdentity>(
            MouseDeviceIdentity.Create(@"ACPI\PNP0C50\4&1", null));
        Assert.True(trackpad.BuiltIn);
        Assert.Equal("笔记本触控板", trackpad.FriendlyName);
        Assert.StartsWith("mouse:builtin:", trackpad.Id);

        MouseIdentity external = Assert.IsType<MouseIdentity>(
            MouseDeviceIdentity.Create(@"HID\VID_046D&PID_B034\1", null));
        Assert.False(external.BuiltIn);
        Assert.StartsWith("mouse:dev:", external.Id);
        Assert.Equal("外接鼠标 · VID_046D PID_B034", external.FriendlyName);
    }

    [Fact]
    public void KeyboardRegistry_DedupesAutoRepeatAndLateUp()
    {
        PeripheralWear wear = new();
        KeyboardDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = _ => KeyboardIdentityFor("dev:keyboard-a", @"TEST\A")
        };
        IntPtr handle = new(0x1001);

        registry.HandleRawKey(handle, 0x41, 0x1E, 0, down: false);
        Assert.Empty(wear.SnapshotKeyboards(true));

        registry.HandleRawKey(handle, 0x41, 0x1E, 0, down: true);
        registry.HandleRawKey(handle, 0x41, 0x1E, 0, down: true);
        Assert.Single(wear.SnapshotKeyboards(false));

        KeyboardProfile profile = Assert.IsType<KeyboardProfile>(wear.FindKeyboard("dev:keyboard-a"));
        Assert.Equal(1L, profile.Keys[0x41]);

        registry.HandleRawKey(handle, 0x41, 0x1E, 0, down: false);
        registry.HandleRawKey(handle, 0x41, 0x1E, 0, down: true);
        Assert.Equal(2L, profile.Keys[0x41]);
    }

    [Fact]
    public void KeyboardRegistry_RoutesByHandle()
    {
        PeripheralWear wear = new();
        IntPtr first = new(0x2001);
        IntPtr second = new(0x2002);
        KeyboardDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = handle => handle == first
                ? KeyboardIdentityFor("dev:keyboard-a", @"TEST\A")
                : KeyboardIdentityFor("dev:keyboard-b", @"TEST\B")
        };

        registry.HandleRawKey(first, 0x41, 0x1E, 0, down: true);
        registry.HandleRawKey(second, 0x42, 0x30, 0, down: true);
        registry.HandleRawKey(first, 0x42, 0x30, 0, down: true);

        KeyboardProfile a = Assert.IsType<KeyboardProfile>(wear.FindKeyboard("dev:keyboard-a"));
        KeyboardProfile b = Assert.IsType<KeyboardProfile>(wear.FindKeyboard("dev:keyboard-b"));
        Assert.Equal(1L, a.Keys[0x41]);
        Assert.Equal(1L, a.Keys[0x42]);
        Assert.Equal(0L, b.Keys[0x41]);
        Assert.Equal(1L, b.Keys[0x42]);
        Assert.Equal(2, wear.SnapshotKeyboards(false).Count);
    }

    [Fact]
    public void KeyboardRegistry_SplitsLeftRightModifiers()
    {
        PeripheralWear wear = new();
        KeyboardDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = _ => KeyboardIdentityFor("dev:modifiers", @"TEST\MOD")
        };
        IntPtr handle = new(0x3001);

        registry.HandleRawKey(handle, 16, 0x2A, 0, down: true);      // 左 Shift
        registry.HandleRawKey(handle, 16, 0x36, 0, down: true);      // 右 Shift
        registry.HandleRawKey(handle, 17, 0x1D, 0, down: true);      // 左 Ctrl
        registry.HandleRawKey(handle, 17, 0x1D, 2, down: true);      // 右 Ctrl（E0）
        registry.HandleRawKey(handle, 18, 0x38, 0, down: true);      // 左 Alt
        registry.HandleRawKey(handle, 18, 0x38, 2, down: true);      // 右 Alt（E0）

        KeyboardProfile profile = Assert.IsType<KeyboardProfile>(wear.FindKeyboard("dev:modifiers"));
        Assert.Equal(1L, profile.Keys[0xA0]);
        Assert.Equal(1L, profile.Keys[0xA1]);
        Assert.Equal(1L, profile.Keys[0xA2]);
        Assert.Equal(1L, profile.Keys[0xA3]);
        Assert.Equal(1L, profile.Keys[0xA4]);
        Assert.Equal(1L, profile.Keys[0xA5]);
    }

    [Fact]
    public void KeyboardRegistry_PhysicalBitmapDistinguishesScans()
    {
        PeripheralWear wear = new();
        KeyboardDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = _ => KeyboardIdentityFor("dev:scan", @"TEST\SCAN")
        };
        IntPtr handle = new(0x3002);

        registry.HandleRawKey(handle, 13, 0x1C, 0, down: true);      // 主区 Enter
        registry.HandleRawKey(handle, 13, 0x1C, 2, down: true);      // 小键盘 Enter（E0）

        KeyboardProfile profile = Assert.IsType<KeyboardProfile>(wear.FindKeyboard("dev:scan"));
        Assert.Equal(2L, profile.Keys[13]);
        KeyboardDeviceSlot slot = Assert.IsType<KeyboardDeviceSlot>(registry.FindSlot(handle));
        Assert.NotEqual(0L, slot.Pressed[0x1C >> 6]);
        Assert.NotEqual(0L, slot.Pressed[0x11C >> 6]);
    }

    [Fact]
    public void KeyboardRegistry_FallsBackToUnknown()
    {
        PeripheralWear wear = new();
        KeyboardDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = _ => null
        };

        registry.HandleRawKey(new IntPtr(0x4001), 0x41, 0x1E, 0, down: true);

        KeyboardProfile unknown = Assert.IsType<KeyboardProfile>(
            wear.FindKeyboard(PeripheralWear.UnknownId));
        Assert.Equal("未知键盘", unknown.Alias);
        Assert.Equal(1L, unknown.Keys[0x41]);
    }

    [Fact]
    public void KeyboardRegistry_ResolverExceptionFallsBackToUnknown()
    {
        PeripheralWear wear = new();
        KeyboardDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = _ => throw new InvalidOperationException("resolver failed")
        };

        Exception? error = Record.Exception(
            () => registry.HandleRawKey(new IntPtr(0x4002), 0x41, 0x1E, 0, down: true));

        Assert.Null(error);
        KeyboardProfile unknown = Assert.IsType<KeyboardProfile>(
            wear.FindKeyboard(PeripheralWear.UnknownId));
        Assert.Equal(1L, unknown.Keys[0x41]);
    }

    [Fact]
    public void MouseRegistry_CountsOnlyDownsAndRoutesByHandle()
    {
        PeripheralMouseWear wear = new();
        IntPtr first = new(0x5001);
        IntPtr second = new(0x5002);
        MouseDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = handle => handle == first
                ? MouseIdentityFor("mouse:dev:a", @"TEST\MA")
                : MouseIdentityFor("mouse:dev:b", @"TEST\MB")
        };

        registry.HandleButtons(first, 0x0001, Now);                  // 左按下
        registry.HandleButtons(first, 0x0004, Now);                  // 右按下
        registry.HandleButtons(first, 0x0002, Now);                  // 左抬起
        registry.HandleButtons(first, 0x0050, Now);                  // 中 + X1
        registry.HandleButtons(first, 0x0100, Now);                  // X2
        registry.HandleButtons(second, 0x0001, Now);                 // 第二只左按下
        registry.HandleButtons(second, 0x0008, Now);                 // 右抬起，不计
        IntPtr idle = new(0x5003);
        registry.HandleButtons(idle, 0x0002, Now);                   // 全新句柄的迟到抬起
        Assert.Null(registry.FindSlot(idle));

        MouseProfile a = Assert.IsType<MouseProfile>(wear.Find("mouse:dev:a"));
        MouseProfile b = Assert.IsType<MouseProfile>(wear.Find("mouse:dev:b"));
        Assert.Equal(1L, a.Left);
        Assert.Equal(1L, a.Right);
        Assert.Equal(1L, a.Middle);
        Assert.Equal(2L, a.X);
        Assert.Equal(5L, a.TotalClicks);
        Assert.Equal(1L, b.Left);
        Assert.Equal(0L, b.Right);
        Assert.Equal(2, wear.SnapshotMice(false).Count);
    }

    [Fact]
    public void MouseRegistry_FallsBackToUnknown()
    {
        PeripheralMouseWear wear = new();
        MouseDeviceRegistry registry = new(wear)
        {
            ResolveInline = true,
            Resolver = _ => null
        };
        IntPtr handle = new(0x6001);

        registry.HandleButtons(handle, 0x0001, Now);
        registry.HandleButtons(handle, 0x0004, Now);

        MouseProfile unknown = Assert.IsType<MouseProfile>(
            wear.Find(PeripheralMouseWear.UnknownId));
        Assert.Equal("未知鼠标", unknown.Alias);
        Assert.Equal(1L, unknown.Left);
        Assert.Equal(1L, unknown.Right);
    }

    [Fact]
    public void Registry_DeviceRemovalKeepsResolvedProfiles()
    {
        PeripheralWear keyboards = new();
        PeripheralMouseWear mice = new();
        KeyboardDeviceRegistry keyboardRegistry = new(keyboards)
        {
            ResolveInline = true,
            Resolver = _ => KeyboardIdentityFor("dev:keyboard", @"TEST\K")
        };
        MouseDeviceRegistry mouseRegistry = new(mice)
        {
            ResolveInline = true,
            Resolver = _ => MouseIdentityFor("mouse:dev:mouse", @"TEST\M")
        };
        IntPtr keyboardHandle = new(0x7001);
        IntPtr mouseHandle = new(0x7002);

        keyboardRegistry.HandleRawKey(keyboardHandle, 0x41, 0x1E, 0, down: true);
        mouseRegistry.HandleButtons(mouseHandle, 0x0001, Now);
        keyboardRegistry.OnDeviceRemoved(keyboardHandle);
        mouseRegistry.OnDeviceRemoved(mouseHandle);

        Assert.NotNull(keyboards.FindKeyboard("dev:keyboard"));
        Assert.NotNull(mice.Find("mouse:dev:mouse"));
        Assert.Null(keyboardRegistry.FindSlot(keyboardHandle));
        Assert.Null(mouseRegistry.FindSlot(mouseHandle));
    }

    private static KeyboardIdentity KeyboardIdentityFor(string id, string instance)
    {
        return new KeyboardIdentity
        {
            Id = id,
            InstanceKey = instance,
            HardwareKey = instance,
            DevicePath = instance,
            FriendlyName = id,
            BuiltIn = false
        };
    }

    private static MouseIdentity MouseIdentityFor(string id, string instance)
    {
        return new MouseIdentity
        {
            Id = id,
            InstanceKey = instance,
            HardwareKey = instance,
            DevicePath = instance,
            FriendlyName = id,
            BuiltIn = false
        };
    }
}
