using System.Buffers.Binary;
using Kms.Input.RawInput;

namespace Kms.Input.Tests;

/// <summary>
/// WM_INPUT 纯解码回归：按钮位、滚轮、指针宽度、绝对/相对坐标与键盘按下态。
/// 这些测试不注册 Raw Input、不创建窗口，因此可以在无桌面的 CI 上运行。
/// </summary>
public sealed class RawInputDecodeTests
{
    private const long MouseDevice = 0x1234;
    private const long KeyboardDevice = 0xABCD;

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Mouse_RelativePacket_DecodesMovementButtonsAndWheel(int pointerSize)
    {
        byte[] packet = MousePacket(
            pointerSize,
            moveFlags: 0,
            buttonFlags: RawMouseInput.LeftButtonDown | RawMouseInput.VerticalWheel,
            buttonData: 120,
            dx: 3,
            dy: -4);

        Assert.True(RawMouseInput.TryDecode(packet, pointerSize, out RawMouseData data));

        Assert.Equal(new IntPtr(MouseDevice), data.Device);
        Assert.Equal(
            (ushort)(RawMouseInput.LeftButtonDown | RawMouseInput.VerticalWheel),
            data.ButtonFlags);
        Assert.Equal((short)120, data.ButtonData);
        Assert.Equal((short)120, data.WheelDelta);
        Assert.Equal(3, data.DeltaX);
        Assert.Equal(-4, data.DeltaY);
        Assert.False(data.IsAbsolute);
        Assert.True(data.HasButtonChanges);
        Assert.True(data.HasVerticalWheel);
        Assert.False(data.HasHorizontalWheel);
        Assert.Equal(1, data.ButtonId);
        Assert.Equal(5d, data.RelativeDistance);
        Assert.True(data.TryGetRelativeDelta(out int dx, out int dy));
        Assert.Equal(3, dx);
        Assert.Equal(-4, dy);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Mouse_AbsolutePacket_KeepsButtonsButNotDistance(int pointerSize)
    {
        byte[] packet = MousePacket(
            pointerSize,
            moveFlags: 1, // MOUSE_MOVE_ABSOLUTE
            buttonFlags: RawMouseInput.RightButtonDown,
            buttonData: 120,
            dx: 12345,
            dy: -6789);

        Assert.True(RawMouseInput.TryDecode(packet, pointerSize, out RawMouseData data));

        Assert.True(data.IsAbsolute);
        Assert.Equal(0d, data.RelativeDistance);
        Assert.False(data.TryGetRelativeDelta(out int dx, out int dy));
        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
        Assert.Equal(2, data.ButtonId); // 绝对坐标包仍必须能派发按钮
        Assert.Equal(12345, data.DeltaX); // 原始值保留，由调用方决定语义
        Assert.Equal(-6789, data.DeltaY);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Mouse_SideButtonsAndHorizontalWheel_AreSeparated(int pointerSize)
    {
        byte[] sideButtons = MousePacket(
            pointerSize,
            moveFlags: 0,
            buttonFlags: RawMouseInput.SideButton1Down | RawMouseInput.SideButton2Down,
            buttonData: 2,
            dx: 0,
            dy: 0);

        Assert.True(RawMouseInput.TryDecode(sideButtons, pointerSize, out RawMouseData buttons));
        Assert.Equal(4, buttons.ButtonId);
        Assert.True(RawMouseInput.IsButtonDown(buttons.ButtonFlags, 4));
        Assert.True(RawMouseInput.IsButtonDown(buttons.ButtonFlags, 5));
        Assert.False(buttons.HasVerticalWheel);
        Assert.False(buttons.HasHorizontalWheel);
        Assert.Equal((short)0, buttons.WheelDelta); // 侧键序号不能冒充滚轮增量

        byte[] horizontal = MousePacket(
            pointerSize,
            moveFlags: 0,
            buttonFlags: RawMouseInput.HorizontalWheel,
            buttonData: -120,
            dx: 0,
            dy: 0);

        Assert.True(RawMouseInput.TryDecode(horizontal, pointerSize, out RawMouseData wheel));
        Assert.True(wheel.HasHorizontalWheel);
        Assert.False(wheel.HasVerticalWheel);
        Assert.Equal((short)-120, wheel.WheelDelta);
        Assert.Equal(0, wheel.ButtonId);
    }

    [Theory]
    [InlineData(RawMouseInput.LeftButtonDown, 1)]
    [InlineData(RawMouseInput.RightButtonDown, 2)]
    [InlineData(RawMouseInput.MiddleButtonDown, 3)]
    [InlineData(RawMouseInput.SideButton1Down, 4)]
    [InlineData(RawMouseInput.SideButton2Down, 5)]
    [InlineData(RawMouseInput.LeftButtonUp, 0)]
    [InlineData(RawMouseInput.RightButtonUp, 0)]
    [InlineData(RawMouseInput.MiddleButtonUp, 0)]
    [InlineData(RawMouseInput.SideButton1Up, 0)]
    [InlineData(RawMouseInput.SideButton2Up, 0)]
    [InlineData(RawMouseInput.VerticalWheel, 0)]
    [InlineData(RawMouseInput.HorizontalWheel, 0)]
    [InlineData((ushort)0, 0)]
    public void ButtonId_MapsOnlyDownFlags(ushort flags, int expected)
    {
        Assert.Equal(expected, RawMouseInput.ButtonId(flags));
    }

    [Fact]
    public void ButtonId_UsesLowestPressedButton()
    {
        ushort flags = RawMouseInput.LeftButtonDown
            | RawMouseInput.RightButtonDown
            | RawMouseInput.MiddleButtonDown;

        Assert.Equal(1, RawMouseInput.ButtonId(flags));
        Assert.True(RawMouseInput.IsButtonDown(flags, 1));
        Assert.True(RawMouseInput.IsButtonDown(flags, 2));
        Assert.True(RawMouseInput.IsButtonDown(flags, 3));
        Assert.False(RawMouseInput.IsButtonDown(flags, 4));
        Assert.False(RawMouseInput.IsButtonDown(flags, 5));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Keyboard_KeyDown_DecodesMakeCodeAndVirtualKey(int pointerSize)
    {
        byte[] packet = KeyboardPacket(
            pointerSize,
            makeCode: 0x1E,
            flags: 0,
            virtualKey: 0x41);

        Assert.True(RawKeyboardInput.TryDecode(packet, pointerSize, out RawKeyboardData data));

        Assert.Equal(new IntPtr(KeyboardDevice), data.Device);
        Assert.Equal(0x1E, data.MakeCode);
        Assert.Equal(0x41, data.VirtualKey);
        Assert.Equal(0u, data.Flags);
        Assert.True(data.Down);
        Assert.False(data.IsBreak);
        Assert.False(data.IsExtended);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Keyboard_BreakFlag_MeansKeyUp(int pointerSize)
    {
        byte[] packet = KeyboardPacket(
            pointerSize,
            makeCode: 0x1E,
            flags: (ushort)RawKeyboardInput.BreakFlag,
            virtualKey: 0x41);

        Assert.True(RawKeyboardInput.TryDecode(packet, pointerSize, out RawKeyboardData data));

        Assert.False(data.Down);
        Assert.True(data.IsBreak);
        Assert.Equal(RawKeyboardInput.BreakFlag, data.Flags);
    }

    [Fact]
    public void Keyboard_ExtendedFlag_IsPreserved()
    {
        ushort flags = (ushort)(RawKeyboardInput.E0Flag | RawKeyboardInput.E1Flag);
        byte[] packet = KeyboardPacket(8, makeCode: 0x1D, flags: flags, virtualKey: 0xA2);

        Assert.True(RawKeyboardInput.TryDecode(packet, 8, out RawKeyboardData data));

        Assert.True(data.Down);
        Assert.True(data.IsExtended);
        Assert.Equal((uint)flags, data.Flags);
        Assert.Equal(0xA2, data.VirtualKey);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Decoders_RejectTheOtherPacketType(int pointerSize)
    {
        byte[] mouse = MousePacket(pointerSize, 0, RawMouseInput.LeftButtonDown, 0, 1, 2);
        byte[] keyboard = KeyboardPacket(pointerSize, 0x1E, 0, 0x41);

        Assert.False(RawKeyboardInput.TryDecode(mouse, pointerSize, out _));
        Assert.False(RawMouseInput.TryDecode(keyboard, pointerSize, out _));
    }

    [Fact]
    public void Mouse_RejectsTruncatedAndInvalidPackets()
    {
        byte[] packet = MousePacket(8, 0, RawMouseInput.LeftButtonDown, 0, 1, 2);
        int header = 8 + (2 * 8);

        Assert.False(RawMouseInput.TryDecode(packet.AsSpan(0, header + 23), 8, out _));
        Assert.False(RawMouseInput.TryDecode(packet, 3, out _));
        Assert.False(RawMouseInput.TryDecode(packet, 16, out _));
        Assert.False(RawMouseInput.TryDecode(ReadOnlySpan<byte>.Empty, 8, out _));

        // dwSize 比实际缓冲区大：声明长度不可信。
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length + 1);
        Assert.False(RawMouseInput.TryDecode(packet, 8, out _));

        // dwSize 小于一个完整 RAWMOUSE。
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)(header + 23));
        Assert.False(RawMouseInput.TryDecode(packet, 8, out _));
    }

    [Fact]
    public void Keyboard_RejectsTruncatedAndInvalidPackets()
    {
        byte[] packet = KeyboardPacket(8, 0x1E, 0, 0x41);
        int header = 8 + (2 * 8);

        Assert.False(RawKeyboardInput.TryDecode(packet.AsSpan(0, header + 15), 8, out _));
        Assert.False(RawKeyboardInput.TryDecode(packet, 0, out _));
        Assert.False(RawKeyboardInput.TryDecode(packet, 12, out _));
        Assert.False(RawKeyboardInput.TryDecode(ReadOnlySpan<byte>.Empty, 8, out _));

        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length + 1);
        Assert.False(RawKeyboardInput.TryDecode(packet, 8, out _));
    }

    [Fact]
    public void Host_StaysIdleWithoutStartingRawInput()
    {
        // 不调用 Start：CI 无桌面时不注册探针，这里只验证公开生命周期状态。
        using var host = new RawInputHost(new RecordingSink());

        Assert.False(host.IsRunning);
        Assert.Equal(IntPtr.Zero, host.WindowHandle);
        Assert.Equal(0, host.LastError);
        Assert.Equal(0L, host.MousePacketCount);
        Assert.Equal(0L, host.KeyboardPacketCount);
        Assert.Equal(0L, host.DeviceChangeCount);

        host.Stop();
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void Host_RejectsNullSink()
    {
        Assert.Throws<ArgumentNullException>(() => new RawInputHost(null!));
    }

    [Fact]
    public void Host_StartAfterDisposeThrows()
    {
        var host = new RawInputHost(new RecordingSink());
        host.Dispose();

        Assert.Throws<ObjectDisposedException>(() => host.Start());
    }

    private static byte[] MousePacket(
        int pointerSize,
        ushort moveFlags,
        ushort buttonFlags,
        short buttonData,
        int dx,
        int dy)
    {
        int header = 8 + (2 * pointerSize);
        var packet = new byte[header + 24];
        WriteHeader(packet, type: 0, pointerSize, MouseDevice);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(header), moveFlags);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(header + 4), buttonFlags);
        BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(header + 6), buttonData);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(header + 12), dx);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(header + 16), dy);

        return packet;
    }

    private static byte[] KeyboardPacket(
        int pointerSize,
        ushort makeCode,
        ushort flags,
        ushort virtualKey)
    {
        int header = 8 + (2 * pointerSize);
        var packet = new byte[header + 16];
        WriteHeader(packet, type: 1, pointerSize, KeyboardDevice);

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(header), makeCode);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(header + 2), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(header + 6), virtualKey);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(header + 8), 0x0100);

        return packet;
    }

    private static void WriteHeader(byte[] packet, uint type, int pointerSize, long device)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0), type);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length);

        if (pointerSize == 8)
        {
            BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(8), device);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), (int)device);
        }
    }

    private sealed class RecordingSink : IRawInputSink
    {
        public void RawMouse(IntPtr device, ushort buttonFlags, short wheelDelta, int dx, int dy)
        {
        }

        public void RawKey(IntPtr device, int virtualKey, int makeCode, uint flags, bool down)
        {
        }

        public void DeviceChanged(IntPtr device, bool removed)
        {
        }
    }
}
