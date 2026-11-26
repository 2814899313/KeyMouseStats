using System.Text;
using Kms.Core.Model;
using Kms.Input.Devices;

namespace Kms.Input.Tests;

public sealed class PeripheralWearTests
{
    [Fact]
    public void KeyCounting_BoundsAndUnknownFallback()
    {
        PeripheralWear wear = new();
        KeyboardProfile profile = wear.PrimaryProfile;

        wear.CountKey(profile, 32);
        wear.CountKey(profile, 32);
        wear.CountKey(profile, -1);
        wear.CountKey(profile, 999);
        wear.CountKey(profile, KeyboardProfile.KeySlots);

        Assert.Equal(2L, profile.Keys[32]);
        Assert.Equal(2L, profile.TotalKeyPresses);
        Assert.Equal(2L, wear.ReadKey(profile, 32));

        wear.CountKey(null, 65);
        KeyboardProfile unknown = Assert.IsType<KeyboardProfile>(
            wear.FindKeyboard(PeripheralWear.UnknownId));
        Assert.Equal(1L, unknown.Keys[65]);
        wear.CountUnknown(65);
        Assert.Equal(2L, unknown.Keys[65]);
    }

    [Fact]
    public void TopKeys_MergesModifierNamesAndAggregates()
    {
        PeripheralWear wear = new();
        KeyboardProfile first = AddKeyboard(wear, "dev:first", "键盘一", 100, 0x201);
        KeyboardProfile second = AddKeyboard(wear, "dev:second", "键盘二", 100, 0x202);

        for (int i = 0; i < 3; i++)
        {
            wear.CountKey(first, 65);
        }

        for (int i = 0; i < 2; i++)
        {
            wear.CountKey(first, 32);
        }

        wear.CountKey(first, 160);
        wear.CountKey(first, 161);
        wear.CountKey(first, 13);

        List<KeyWear> top = wear.TopKeys(first.Id, 5);
        Assert.Equal(4, top.Count);
        Assert.Equal("A", top[0].Name);
        Assert.Equal(3L, top[0].Count);
        Assert.Equal(2L, top[1].Count);
        Assert.Contains(top, row => row.Name == "Shift" && row.Count == 2);
        Assert.Contains(top, row => row.Name == "空格" && row.Count == 2);

        for (int i = 0; i < 60; i++)
        {
            wear.CountKey(first, 32);
        }

        for (int i = 0; i < 30; i++)
        {
            wear.CountKey(second, 32);
        }

        for (int i = 0; i < 20; i++)
        {
            wear.CountKey(second, 65);
        }

        List<KeyWear> all = wear.TopKeys(PeripheralWear.AggregateId, 5);
        Assert.Equal(4, all.Count);
        Assert.Equal(92L, all[0].Count);
        Assert.Equal("空格", all[0].Name);
        Assert.Equal(23L, all[1].Count);
        Assert.Equal("A", all[1].Name);
        Assert.Equal(62L, wear.TopKeys(first.Id, 5)[0].Count);
    }

    [Fact]
    public void WearMath_MatchesV3()
    {
        Assert.Equal(0.5, PeripheralWear.Ratio(50, 100), 10);
        Assert.Equal(1.5, PeripheralWear.Ratio(150, 100), 10);
        Assert.Equal(0.5, PeripheralWear.Progress(50, 100), 10);
        Assert.Equal(1.0, PeripheralWear.Progress(150, 100), 10);
        Assert.Equal(0.0, PeripheralWear.Progress(0, 0), 10);
        Assert.Equal(1.0, PeripheralWear.Progress(1, 0), 10);
        Assert.Equal(50_000_000L, PeripheralWear.DefaultKeyboardRated);
        Assert.Equal(80_000_000L, PeripheralWear.DefaultMouseRated);
        Assert.Equal(50_000_000L, PeripheralWear.ClampRated(0, 50_000_000L));
        Assert.Equal(PeripheralWear.MaxRated, PeripheralWear.ClampRated(long.MaxValue, 1));
        Assert.Equal(50_000_000L, new KeyboardProfile().Rated);
        Assert.Equal(80_000_000L, new MouseProfile().RatedLeft);
        Assert.Equal(80_000_000L, new MouseProfile().RatedRight);
    }

    [Fact]
    public void KeyboardHeaderAndKeys_RoundTrip()
    {
        KeyboardProfile source = new()
        {
            Id = "dev:ABC",
            Alias = "我的键盘",
            FriendlyName = "USB Keyboard",
            DevicePath = @"HID\VID_1234&PID_5678\1",
            HardwareKey = @"HID\VID_1234&PID_5678",
            InstanceKey = @"HID\VID_1234&PID_5678\1",
            BuiltIn = true,
            Rated = 50_000_000,
            Since = new DateTime(2026, 2, 3),
            Seeded = true,
            LastSeenUtcTicks = 123456
        };
        source.Keys[32] = 7;
        source.Keys[65] = 3;

        KeyboardProfile copy = Assert.IsType<KeyboardProfile>(
            PeripheralWear.DecodeHeader(PeripheralWear.EncodeHeader(source)));
        Assert.Equal(source.Id, copy.Id);
        Assert.Equal(source.Alias, copy.Alias);
        Assert.Equal(source.FriendlyName, copy.FriendlyName);
        Assert.Equal(source.DevicePath, copy.DevicePath);
        Assert.Equal(source.HardwareKey, copy.HardwareKey);
        Assert.Equal(source.InstanceKey, copy.InstanceKey);
        Assert.True(copy.BuiltIn);
        Assert.True(copy.Seeded);
        Assert.Equal(source.Since, copy.Since);
        Assert.Equal(source.Rated, copy.Rated);
        Assert.Equal(source.LastSeenUtcTicks, copy.LastSeenUtcTicks);

        string keys = PeripheralWear.EncodeKeys(source);
        Assert.StartsWith("dev:ABC|", keys);
        KeyboardProfile decoded = new() { Id = "dev:ABC" };
        PeripheralWear.DecodeKeys(decoded, keys[(keys.IndexOf('|') + 1)..]);
        Assert.Equal(7L, decoded.Keys[32]);
        Assert.Equal(3L, decoded.Keys[65]);

        PeripheralWear.DecodeKeys(decoded, "32:5,999:7,-1:9,bad,65:-2");
        Assert.Equal(5L, decoded.Keys[32]);
        Assert.Equal(0L, decoded.Keys[65]);
        Assert.Null(PeripheralWear.DecodeHeader("dev:x|only-two"));
    }

    [Fact]
    public void KeyboardProfiles_LineRoundTrip()
    {
        PeripheralWear wear = new();
        KeyboardProfile first = AddKeyboard(wear, "dev:first", "键盘一", 50_000_000, 0x211);
        KeyboardProfile second = AddKeyboard(wear, "dev:second", "键盘二", 60_000_000, 0x212);
        first.Since = new DateTime(2025, 5, 1);
        second.Since = new DateTime(2025, 6, 1);
        for (int i = 0; i < 7; i++)
        {
            wear.CountKey(second, 65);
        }

        StringBuilder builder = new();
        wear.AppendProfiles(builder);
        List<string> headers = new();
        List<string> keys = new();
        foreach (string line in builder.ToString().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("keyboard_v1=", StringComparison.Ordinal))
            {
                headers.Add(line[12..]);
            }
            else if (line.StartsWith("keyboard_keys_v1=", StringComparison.Ordinal))
            {
                keys.Add(line[17..]);
            }
        }

        Assert.Equal(2, headers.Count);
        Assert.Equal(2, keys.Count);
        PeripheralWear restored = new();
        restored.LoadProfiles(headers, keys);
        KeyboardProfile secondRestored = Assert.IsType<KeyboardProfile>(
            restored.FindKeyboard("dev:second"));
        Assert.Equal(7L, secondRestored.Keys[65]);
        Assert.Equal(60_000_000L, secondRestored.Rated);
        Assert.Equal(new DateTime(2025, 6, 1), secondRestored.Since);
    }

    [Fact]
    public void KeyboardReset_IsolatesTargetProfile()
    {
        PeripheralWear wear = new();
        KeyboardProfile first = AddKeyboard(wear, "dev:first", "键盘一", 100, 0x221);
        KeyboardProfile second = AddKeyboard(wear, "dev:second", "键盘二", 100, 0x222);
        for (int i = 0; i < 10; i++)
        {
            wear.CountKey(first, 32);
            wear.CountKey(second, 32);
        }

        wear.ResetKeyboard(first.Id, new DateTime(2026, 2, 2));
        Assert.Equal(0L, first.Keys[32]);
        Assert.Equal(10L, second.Keys[32]);
        Assert.Equal(new DateTime(2026, 2, 2), first.Since);
    }

    [Fact]
    public void PendingResolution_MergesSameInstanceAndClearsPlaceholder()
    {
        PeripheralWear wear = new();
        wear.EnsureDefault();
        Assert.NotNull(wear.FindKeyboard(PeripheralWear.PrimaryId));

        KeyboardProfile first = AddKeyboard(wear, "dev:first", "键盘一", 100, 0x231);
        Assert.Null(wear.FindKeyboard(PeripheralWear.PrimaryId));

        for (int i = 0; i < 5; i++)
        {
            wear.CountKey(first, 32);
        }

        KeyboardProfile pending = wear.CreatePendingProfile(new IntPtr(0x232));
        wear.CountKey(pending, 65);
        KeyboardIdentity identity = new()
        {
            Id = "dev:first",
            InstanceKey = "TEST\\dev:first",
            HardwareKey = "TEST\\dev:first",
            DevicePath = "TEST\\dev:first",
            FriendlyName = "键盘一 friendly"
        };
        KeyboardProfile resolved = Assert.IsType<KeyboardProfile>(wear.ResolvePending(pending, identity));

        Assert.Same(first, resolved);
        Assert.Equal(5L, first.Keys[32]);
        Assert.Equal(1L, first.Keys[65]);
        Assert.Single(wear.SnapshotKeyboards(false));
        Assert.Equal("键盘一 friendly", first.FriendlyName);
        Assert.Equal("dev:first", wear.ActiveKeyboardId);
    }

    [Fact]
    public void PendingResolution_SameIdMergesWithoutHardwareKey()
    {
        PeripheralWear wear = new();
        KeyboardProfile existing = new() { Id = "dev:same", Alias = "旧档案" };
        wear.LoadProfiles([existing]);

        KeyboardProfile pending = wear.CreatePendingProfile(new IntPtr(0x248));
        wear.CountKey(pending, 65);
        KeyboardIdentity identity = new() { Id = "dev:same", FriendlyName = "新友好名" };
        KeyboardProfile resolved = Assert.IsType<KeyboardProfile>(wear.ResolvePending(pending, identity));

        Assert.Same(existing, resolved);
        Assert.Equal(1L, existing.Keys[65]);
        Assert.Equal("新友好名", existing.FriendlyName);
        Assert.Single(wear.SnapshotKeyboards(false));
    }

    [Fact]
    public void PendingResolution_NullIdentityUsesSingleUnknownProfile()
    {
        PeripheralWear wear = new();
        KeyboardProfile firstPending = wear.CreatePendingProfile(new IntPtr(0x241));
        wear.CountKey(firstPending, 65);
        KeyboardProfile firstUnknown = Assert.IsType<KeyboardProfile>(
            wear.ResolvePending(firstPending, null));
        Assert.Equal(PeripheralWear.UnknownId, firstUnknown.Id);
        Assert.Equal(1L, firstUnknown.Keys[65]);

        KeyboardProfile secondPending = wear.CreatePendingProfile(new IntPtr(0x242));
        wear.CountKey(secondPending, 65);
        KeyboardProfile secondUnknown = Assert.IsType<KeyboardProfile>(
            wear.ResolvePending(secondPending, null));
        Assert.Same(firstUnknown, secondUnknown);
        Assert.Equal(2L, firstUnknown.Keys[65]);
        Assert.Single(wear.SnapshotKeyboards(false));
    }

    [Fact]
    public void LegacyKeyboard_MergesIntoCurrent()
    {
        PeripheralWear wear = new();
        string hardware = LegacyHardware("旧键盘");
        wear.LoadLegacy(hardware, "32:9,65:4");
        KeyboardProfile legacy = Assert.IsType<KeyboardProfile>(
            wear.FindKeyboard(PeripheralWear.LegacyId));
        Assert.True(legacy.Legacy);
        Assert.Equal(9L, legacy.Keys[32]);
        Assert.Equal(4L, legacy.Keys[65]);

        KeyboardProfile target = AddKeyboard(wear, "dev:new", "新键盘", 50_000_000, 0x251);
        Assert.True(wear.MergeLegacyInto(target.Id));
        Assert.Equal(9L, target.Keys[32]);
        Assert.Equal(4L, target.Keys[65]);
        Assert.Null(wear.FindKeyboard(PeripheralWear.LegacyId));
        Assert.Equal(13L, wear.TopKeys(PeripheralWear.AggregateId, 5).Sum(row => row.Count));

        KeyboardProfile fallback = PeripheralWear.CreateLegacyKeyboard("|50000000|0", "32:1");
        Assert.Equal("旧版单键盘", fallback.Alias);
    }

    [Fact]
    public void KeyboardSeeding_FromHistoryUpgradesToLegacy()
    {
        PeripheralWear wear = new();
        List<DayRecord> history =
        [
            new DayRecord
            {
                Date = new DateTime(2025, 1, 2),
                KeyCounts = { [32] = 10, [65] = 4 }
            },
            new DayRecord
            {
                Date = new DateTime(2025, 1, 3),
                KeyCounts = { [32] = 5 }
            },
            new DayRecord
            {
                Date = new DateTime(2025, 1, 4),
                PhysicalVks = { [83] = 2 }
            }
        ];

        wear.EnsureSeeded(history, new StatsTotals { Keys = 999_999 });

        KeyboardProfile legacy = Assert.IsType<KeyboardProfile>(
            wear.FindKeyboard(PeripheralWear.LegacyId));
        Assert.True(legacy.Legacy);
        Assert.True(legacy.Seeded);
        Assert.Equal(15L, legacy.Keys[32]);
        Assert.Equal(4L, legacy.Keys[65]);
        Assert.Equal(2L, legacy.Keys[83]);
        Assert.Equal(new DateTime(2025, 1, 2), legacy.Since);
        Assert.False(wear.NeedsKeySeed);
        Assert.Equal(21L, wear.TotalKeyPressesAll);

        wear.EnsureSeeded(history, new StatsTotals());
        Assert.Equal(15L, legacy.Keys[32]);
    }

    [Fact]
    public void KeyboardSeeding_ExplicitResetNeverRevivesOldCounts()
    {
        PeripheralWear wear = new();
        List<DayRecord> history =
        [
            new DayRecord
            {
                Date = new DateTime(2025, 2, 1),
                KeyCounts = { [32] = 42 }
            }
        ];

        wear.LoadLegacy("", "-", hasKeys: true);
        Assert.False(wear.NeedsKeySeed);
        wear.EnsureSeeded(history, new StatsTotals());

        Assert.Null(wear.FindKeyboard(PeripheralWear.LegacyId));
        Assert.Equal(0L, wear.TotalKeyPressesAll);
        Assert.Empty(wear.SnapshotKeyboards(false));
    }

    [Fact]
    public void MouseSeeding_UsesMaxOfHistoryAndTotals()
    {
        PeripheralMouseWear wear = new();
        List<DayRecord> history =
        [
            new DayRecord
            {
                Date = new DateTime(2025, 3, 4),
                Left = 10,
                Right = 6,
                Middle = 2,
                XButtons = 1
            }
        ];
        StatsTotals totals = new()
        {
            Left = 12,
            Right = 7,
            Middle = 2,
            XButtons = 1
        };

        wear.LoadLegacy("", "", hasMouse: false);
        wear.EnsureSeeded(history, totals);

        MouseProfile legacy = Assert.IsType<MouseProfile>(wear.Find(PeripheralMouseWear.LegacyId));
        Assert.True(legacy.Legacy);
        Assert.Equal(12L, legacy.Left);
        Assert.Equal(7L, legacy.Right);
        Assert.Equal(2L, legacy.Middle);
        Assert.Equal(1L, legacy.X);
        Assert.Equal(new DateTime(2025, 3, 4), legacy.Since);
        Assert.False(wear.NeedsSeed);
    }

    [Fact]
    public void MouseSeeding_ZeroWearMouseWithHasMouseDoesNotSeed()
    {
        PeripheralMouseWear wear = new();
        wear.LoadLegacy("|||" + PeripheralWear.EncodeName("主力鼠标"), "0|0|0|0", hasMouse: true);
        wear.EnsureSeeded(
            [new DayRecord { Date = new DateTime(2025, 3, 4), Left = 10 }],
            new StatsTotals { Left = 10 });

        Assert.Null(wear.Find(PeripheralMouseWear.LegacyId));
        Assert.Empty(wear.SnapshotMice(false));
    }

    [Fact]
    public void LegacyLines_MalformedValuesFallBack()
    {
        PeripheralWear wear = new();
        wear.LoadProfile("bad|not-a-number|x|y|z");
        Assert.Equal(PeripheralWear.DefaultKeyboardRated, wear.KeyboardRated);
        wear.LoadKeys("32:5,999:7,-1:9,bad,65:-2");
        Assert.Equal(5L, wear.ReadKey(32));
        Assert.Equal(0L, wear.ReadKey(65));
        Assert.StartsWith("32:5", wear.EncodeKeys());

        wear.LoadProfile("|50000000|0|鼠标|80000000|80000000|1|1");
        Assert.Equal("主力键盘", wear.KeyboardName);
        Assert.True(wear.Seeded);
        Assert.Equal("not-base64!", PeripheralWear.DecodeName("not-base64!"));

        PeripheralMouseWear mice = new();
        mice.LoadButtons("4|bad|6");
        Assert.Equal(4L, mice.LeftClicks);
        Assert.Equal(0L, mice.RightClicks);
        Assert.Equal(6L, mice.MiddleClicks);
        Assert.Equal(4L, mice.Read(1));
        Assert.StartsWith("4|0|6|", mice.EncodeButtons());
    }

    [Fact]
    public void MouseHeaderButtonsAndDays_RoundTrip()
    {
        MouseProfile source = new()
        {
            Id = "mouse:dev:T",
            Alias = "家里 · MX Master",
            FriendlyName = "Logitech MX Master",
            DevicePath = @"HID\VID_046D&PID_B034\1",
            HardwareKey = @"HID\VID_046D&PID_B034",
            InstanceKey = @"HID\VID_046D&PID_B034\1",
            BuiltIn = true,
            RatedLeft = 80_000_000,
            RatedRight = 70_000_000,
            Since = new DateTime(2026, 3, 4),
            Seeded = true,
            LastSeenUtcTicks = 9876
        };

        MouseProfile copy = Assert.IsType<MouseProfile>(
            PeripheralMouseWear.DecodeHeader(PeripheralMouseWear.EncodeHeader(source)));
        Assert.Equal(source.Id, copy.Id);
        Assert.Equal(source.Alias, copy.Alias);
        Assert.Equal(source.FriendlyName, copy.FriendlyName);
        Assert.Equal(source.HardwareKey, copy.HardwareKey);
        Assert.True(copy.BuiltIn);
        Assert.True(copy.Seeded);
        Assert.Equal(source.RatedLeft, copy.RatedLeft);
        Assert.Equal(source.RatedRight, copy.RatedRight);
        Assert.Equal(source.Since, copy.Since);

        PeripheralMouseWear.DecodeButtons(source, "3,4,5,6");
        Assert.Equal(3L, PeripheralMouseWear.Read(source, 1));
        Assert.Equal(4L, PeripheralMouseWear.Read(source, 2));
        Assert.Equal(5L, PeripheralMouseWear.Read(source, 3));
        Assert.Equal(6L, PeripheralMouseWear.Read(source, 4));

        DateTime day = new(2026, 3, 4);
        source.Count(1, day);
        source.Count(2, day.AddDays(1));
        string days = PeripheralMouseWear.EncodeDays(source);
        MouseProfile dayCopy = new();
        PeripheralMouseWear.DecodeDays(dayCopy, days[(days.IndexOf('|') + 1)..]);
        long[] values = new long[MouseProfile.DaySlots];
        PeripheralMouseWear.CopyDaysTo(dayCopy, values);
        Assert.Equal(1L, values[5]);
        Assert.Equal(1L, values[6]);
    }

    [Fact]
    public void MouseSevenDayRing_ShiftsAndDropsOldest()
    {
        MouseProfile profile = new() { Id = "mouse:ring" };
        DateTime today = new(2026, 3, 10);
        for (int offset = -8; offset <= 0; offset++)
        {
            profile.Count(1, today.AddDays(offset));
        }

        Assert.Equal(today.AddDays(-6).Ticks, profile.DayStamp[0]);
        Assert.Equal(today.Ticks, profile.DayStamp[6]);
        Assert.Equal(1L, profile.DayLeft[0]);
        Assert.Equal(1L, profile.DayLeft[6]);
        Assert.Equal(1L, profile.DayLeft[1]);

        profile.Count(2, today);
        Assert.Equal(1L, profile.DayRight[6]);

        long[] values = new long[MouseProfile.DaySlots];
        PeripheralMouseWear.CopyDaysTo(profile, values);
        Assert.Equal(1L, values[0]);
        Assert.Equal(2L, values[6]);

        PeripheralMouseWear wear = new();
        wear.LoadProfiles([profile]);
        wear.Reset(profile.Id, new DateTime(2026, 3, 11));
        Assert.Equal(0L, profile.TotalClicks);
        Assert.All(profile.DayStamp, stamp => Assert.Equal(0L, stamp));
    }

    [Fact]
    public void MouseProfiles_ResetAndAggregateAreIsolated()
    {
        PeripheralMouseWear wear = new();
        MouseProfile first = AddMouse(wear, "mouse:first", "家里", 100, 100, 0x261);
        MouseProfile second = AddMouse(wear, "mouse:second", "公司", 100, 100, 0x262);
        DateTime today = new(2026, 3, 12);
        for (int i = 0; i < 10; i++)
        {
            wear.CountClick(first, 1, today);
            wear.CountClick(second, 1, today);
        }

        wear.CountClick(second, 2, today);
        wear.Reset(first.Id, new DateTime(2026, 3, 13));
        Assert.Equal(0L, first.Left);
        Assert.Equal(10L, second.Left);
        Assert.Equal(1L, second.Right);
        Assert.Equal(11L, wear.TotalClicksAll);
        Assert.Equal(new DateTime(2026, 3, 13), first.Since);

        Assert.Equal(0.1, PeripheralMouseWear.WorstLeft(second), 10);
        Assert.Equal(0.1, wear.WorstRatio(), 10);
    }

    [Fact]
    public void MouseLegacy_MergesIntoCurrent()
    {
        PeripheralMouseWear wear = new();
        wear.LoadLegacy(LegacyHardware("旧鼠标"), "1|2|3|4");
        MouseProfile legacy = Assert.IsType<MouseProfile>(
            wear.Find(PeripheralMouseWear.LegacyId));
        Assert.True(legacy.Legacy);
        Assert.Equal(1L, legacy.Left);
        Assert.Equal(2L, legacy.Right);
        Assert.Equal(3L, legacy.Middle);
        Assert.Equal(4L, legacy.X);

        MouseProfile target = AddMouse(wear, "mouse:new", "新鼠标", 50_000_000, 50_000_000, 0x271);
        Assert.True(wear.MergeLegacyInto(target.Id));
        Assert.Equal(1L, target.Left);
        Assert.Equal(2L, target.Right);
        Assert.Equal(3L, target.Middle);
        Assert.Equal(4L, target.X);
        Assert.Null(wear.Find(PeripheralMouseWear.LegacyId));

        wear.LoadLegacy("|||" + PeripheralWear.EncodeName("主力鼠标"), "0|0|0|0");
        Assert.Empty(wear.SnapshotMice(true));
        MouseProfile fallback = PeripheralMouseWear.CreateLegacyMouse("|0", "1|2|3|4");
        Assert.Equal("旧版单鼠标", fallback.Alias);
    }

    [Fact]
    public void MouseProfiles_LineRoundTrip()
    {
        PeripheralMouseWear wear = new();
        MouseProfile first = AddMouse(wear, "mouse:first", "键盘旁", 50_000_000, 60_000_000, 0x281);
        MouseProfile second = AddMouse(wear, "mouse:second", "包里", 70_000_000, 80_000_000, 0x282);
        first.Since = new DateTime(2025, 5, 1);
        second.Since = new DateTime(2025, 6, 1);
        DateTime today = new(2026, 3, 14);
        second.Count(1, today);
        second.Count(1, today);
        second.Count(4, today);

        StringBuilder builder = new();
        wear.Append(builder);
        List<string> headers = new();
        List<string> buttons = new();
        List<string> days = new();
        foreach (string line in builder.ToString().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("mouse_v1=", StringComparison.Ordinal))
            {
                headers.Add(line[9..]);
            }
            else if (line.StartsWith("mouse_buttons_v1=", StringComparison.Ordinal))
            {
                buttons.Add(line[17..]);
            }
            else if (line.StartsWith("mouse_days_v1=", StringComparison.Ordinal))
            {
                days.Add(line[14..]);
            }
        }

        Assert.Equal(2, headers.Count);
        Assert.Equal(2, buttons.Count);
        Assert.Equal(2, days.Count);

        PeripheralMouseWear restored = new();
        restored.LoadProfiles(headers, buttons, days);
        MouseProfile secondRestored = Assert.IsType<MouseProfile>(restored.Find("mouse:second"));
        Assert.Equal(2L, secondRestored.Left);
        Assert.Equal(1L, secondRestored.X);
        Assert.Equal(70_000_000L, secondRestored.RatedLeft);
        Assert.Equal(new DateTime(2025, 6, 1), secondRestored.Since);
        long[] restoredDays = new long[MouseProfile.DaySlots];
        PeripheralMouseWear.CopyDaysTo(secondRestored, restoredDays);
        Assert.Equal(3L, restoredDays[6]);
    }

    [Fact]
    public void MousePendingResolution_MergesSameInstanceAndNullIdentity()
    {
        PeripheralMouseWear wear = new();
        wear.EnsureDefault();
        MouseProfile first = AddMouse(wear, "mouse:first", "家里", 100, 100, 0x291);
        Assert.Null(wear.Find(PeripheralMouseWear.PrimaryId));

        DateTime today = new(2026, 3, 15);
        wear.CountClick(first, 1, today);
        MouseProfile pending = wear.CreatePendingProfile(new IntPtr(0x292));
        wear.CountClick(pending, 2, today);
        MouseIdentity identity = new()
        {
            Id = "mouse:first",
            InstanceKey = "MTEST\\mouse:first",
            HardwareKey = "MTEST\\mouse:first",
            DevicePath = "MTEST\\mouse:first",
            FriendlyName = "家里 friendly"
        };
        MouseProfile resolved = Assert.IsType<MouseProfile>(wear.ResolvePending(pending, identity));
        Assert.Same(first, resolved);
        Assert.Equal(1L, first.Left);
        Assert.Equal(1L, first.Right);
        Assert.Equal("家里 friendly", first.FriendlyName);
        Assert.Single(wear.SnapshotMice(false));

        MouseProfile pendingUnknown = wear.CreatePendingProfile(new IntPtr(0x293));
        wear.CountClick(pendingUnknown, 1, today);
        MouseProfile unknown = Assert.IsType<MouseProfile>(wear.ResolvePending(pendingUnknown, null));
        Assert.Equal(PeripheralMouseWear.UnknownId, unknown.Id);
        Assert.Equal(1L, unknown.Left);
    }

    [Fact]
    public void MousePendingResolution_SameIdMergesWithoutHardwareKey()
    {
        PeripheralMouseWear wear = new();
        MouseProfile existing = new() { Id = "mouse:dev:same", Alias = "旧档案" };
        wear.LoadProfiles([existing]);

        MouseProfile pending = wear.CreatePendingProfile(new IntPtr(0x298));
        DateTime day = new(2026, 3, 15);
        wear.CountClick(pending, 2, day);
        MouseIdentity identity = new() { Id = "mouse:dev:same", FriendlyName = "新友好名" };
        MouseProfile resolved = Assert.IsType<MouseProfile>(wear.ResolvePending(pending, identity));

        Assert.Same(existing, resolved);
        Assert.Equal(1L, existing.Right);
        Assert.Equal("新友好名", existing.FriendlyName);
        Assert.Single(wear.SnapshotMice(false));
    }

    private static KeyboardProfile AddKeyboard(
        PeripheralWear wear,
        string id,
        string alias,
        long rated,
        long handle)
    {
        KeyboardProfile pending = wear.CreatePendingProfile(new IntPtr(handle));
        KeyboardIdentity identity = new()
        {
            Id = id,
            InstanceKey = "TEST\\" + id,
            HardwareKey = "TEST\\" + id,
            DevicePath = "TEST\\" + id,
            FriendlyName = alias + " friendly"
        };
        KeyboardProfile profile = Assert.IsType<KeyboardProfile>(wear.ResolvePending(pending, identity));
        profile.Alias = alias;
        profile.Rated = rated;
        profile.Since = new DateTime(2026, 1, 1);
        profile.Seeded = true;
        return profile;
    }

    private static MouseProfile AddMouse(
        PeripheralMouseWear wear,
        string id,
        string alias,
        long ratedLeft,
        long ratedRight,
        long handle)
    {
        MouseProfile pending = wear.CreatePendingProfile(new IntPtr(handle));
        MouseIdentity identity = new()
        {
            Id = id,
            InstanceKey = "MTEST\\" + id,
            HardwareKey = "MTEST\\" + id,
            DevicePath = "MTEST\\" + id,
            FriendlyName = alias + " friendly"
        };
        MouseProfile profile = Assert.IsType<MouseProfile>(wear.ResolvePending(pending, identity));
        profile.Alias = alias;
        profile.RatedLeft = ratedLeft;
        profile.RatedRight = ratedRight;
        profile.Since = new DateTime(2026, 1, 1);
        profile.Seeded = true;
        return profile;
    }

    private static string LegacyHardware(string name)
    {
        return PeripheralWear.EncodeName(name) + "|50000000|"
            + new DateTime(2024, 1, 1).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "|" + PeripheralWear.EncodeName("旧鼠标") + "|80000000|80000000|"
            + new DateTime(2024, 2, 1).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "|1";
    }
}
