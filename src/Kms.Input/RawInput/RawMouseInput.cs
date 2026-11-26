using System.Buffers.Binary;

namespace Kms.Input.RawInput;

/// <summary>
/// 一个 WM_INPUT 鼠标包的纯托管快照。所有字段都来自 RAWMOUSE，
/// 绝对坐标包只改变 <see cref="IsAbsolute"/>，按钮与滚轮数据仍然有效。
/// </summary>
public readonly struct RawMouseData
{
    private readonly short _buttonData;

    internal RawMouseData(
        IntPtr device,
        ushort buttonFlags,
        short buttonData,
        int deltaX,
        int deltaY,
        bool absolute)
    {
        Device = device;
        ButtonFlags = buttonFlags;
        _buttonData = buttonData;
        DeltaX = deltaX;
        DeltaY = deltaY;
        IsAbsolute = absolute;
    }

    /// <summary>RAWINPUTHEADER.hDevice；设备级磨损/档案归因使用它。</summary>
    public IntPtr Device { get; }

    /// <summary>RAWMOUSE.usButtonFlags（RI_MOUSE_*）。</summary>
    public ushort ButtonFlags { get; }

    /// <summary>RAWMOUSE.usButtonData 的原始有符号值；侧键包里是 XButton 序号。</summary>
    public short ButtonData => _buttonData;

    /// <summary>滚轮增量（±120 通常代表一格）。只有滚轮标志置位时才有意义，否则返回 0。</summary>
    public short WheelDelta =>
        (ButtonFlags & (RawMouseInput.VerticalWheel | RawMouseInput.HorizontalWheel)) != 0
            ? _buttonData
            : (short)0;

    /// <summary>RAWMOUSE.lLastX。相对模式是传感器计数，绝对模式是设备坐标。</summary>
    public int DeltaX { get; }

    /// <summary>RAWMOUSE.lLastY。语义同 <see cref="DeltaX"/>。</summary>
    public int DeltaY { get; }

    /// <summary>MOUSE_MOVE_ABSOLUTE：触控板、远程桌面与部分手写笔会走这条路径。</summary>
    public bool IsAbsolute { get; }

    /// <summary>这个包是否包含任何鼠标按钮的按下/抬起变化（不含滚轮）。</summary>
    public bool HasButtonChanges => (ButtonFlags & RawMouseInput.ButtonChangeMask) != 0;

    /// <summary>垂直滚轮标志 RI_MOUSE_WHEEL。</summary>
    public bool HasVerticalWheel => (ButtonFlags & RawMouseInput.VerticalWheel) != 0;

    /// <summary>水平滚轮标志 RI_MOUSE_HWHEEL。</summary>
    public bool HasHorizontalWheel => (ButtonFlags & RawMouseInput.HorizontalWheel) != 0;

    /// <summary>第一个按下位对应的统一按钮编号（1=左 2=右 3=中 4/5=侧）；没有按下位时为 0。</summary>
    public int ButtonId => RawMouseInput.ButtonId(ButtonFlags);

    /// <summary>
    /// 相对位移的欧氏长度（传感器计数）。绝对坐标不能按 DPI 换算成路程，因此返回 0。
    /// </summary>
    public double RelativeDistance
    {
        get
        {
            if (IsAbsolute)
            {
                return 0;
            }

            double dx = DeltaX;
            double dy = DeltaY;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }
    }

    /// <summary>
    /// 取出可用于 DPI/路程换算的相对位移。绝对坐标包返回 <c>false</c> 并输出 0，
    /// 调用方不需要自己再判一次 <see cref="IsAbsolute"/>。
    /// </summary>
    public bool TryGetRelativeDelta(out int dx, out int dy)
    {
        if (IsAbsolute)
        {
            dx = 0;
            dy = 0;
            return false;
        }

        dx = DeltaX;
        dy = DeltaY;
        return true;
    }
}

/// <summary>
/// WM_INPUT 鼠标包解码器。实例持有一个复用的接收缓冲区，只应在消息循环线程上调用
/// <see cref="Read"/>；纯解码静态方法可以在任意线程/测试中使用。
/// </summary>
public sealed class RawMouseInput
{
    public const ushort LeftButtonDown = 0x0001;
    public const ushort LeftButtonUp = 0x0002;
    public const ushort RightButtonDown = 0x0004;
    public const ushort RightButtonUp = 0x0008;
    public const ushort MiddleButtonDown = 0x0010;
    public const ushort MiddleButtonUp = 0x0020;
    public const ushort SideButton1Down = 0x0040;
    public const ushort SideButton1Up = 0x0080;
    public const ushort SideButton2Down = 0x0100;
    public const ushort SideButton2Up = 0x0200;
    public const ushort VerticalWheel = 0x0400;
    public const ushort HorizontalWheel = 0x0800;

    /// <summary>全部按钮变化位（0x0001..0x0200），不含滚轮。</summary>
    public const ushort ButtonChangeMask = 0x03FF;

    internal const int DataSize = 24;

    /// <summary>一次 WM_INPUT 通常只含一个 RAWMOUSE；留出余量避免联合包被直接拒绝。</summary>
    private const int BufferedStructures = 8;

    private readonly byte[] _buffer =
        new byte[RawInputNative.HeaderPrefixSize + (2 * IntPtr.Size) + (DataSize * BufferedStructures)];

    private volatile bool _readFailed;

    /// <summary>最近一次 GetRawInputData 是否失败（与「这个包不是鼠标」区分开）。</summary>
    public bool ReadFailed => _readFailed;

    /// <summary>
    /// 读取并解码一个 WM_INPUT 包。非鼠标包返回 <c>false</c>，且不影响 <see cref="ReadFailed"/>。
    /// 本方法不阻塞、不分配新对象；内部缓冲区在实例上复用。
    /// </summary>
    public bool Read(IntPtr rawInput, out RawMouseData data)
    {
        data = default;

        uint size = (uint)_buffer.Length;
        uint read = RawInputNative.GetRawInputData(
            rawInput,
            RawInputNative.RidInput,
            _buffer,
            ref size,
            (uint)RawInputNative.HeaderSize);

        if (read == uint.MaxValue || read == 0)
        {
            _readFailed = true;
            return false;
        }

        bool ok = TryDecode(_buffer.AsSpan(0, (int)read), IntPtr.Size, out data);
        if (ok)
        {
            _readFailed = false;
        }

        return ok;
    }

    /// <summary>按当前进程指针宽度解码一个原始 RAWINPUT 字节块。</summary>
    public static bool TryDecode(ReadOnlySpan<byte> packet, out RawMouseData data) =>
        TryDecode(packet, IntPtr.Size, out data);

    /// <summary>
    /// 纯解码入口。packet 必须包含完整的 RAWINPUTHEADER 与至少一个 RAWMOUSE。
    /// 指针宽度只接受 4/8；类型、声明长度或截断任一不符都返回 <c>false</c>。
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> packet, int pointerSize, out RawMouseData data)
    {
        data = default;

        if (pointerSize is not (4 or 8))
        {
            return false;
        }

        int header = RawInputNative.HeaderPrefixSize + (2 * pointerSize);
        if (packet.Length < header + DataSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != RawInputNative.RimTypeMouse)
        {
            return false;
        }

        uint declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4));
        if (declaredSize < (uint)(header + DataSize) || declaredSize > (uint)packet.Length)
        {
            return false;
        }

        IntPtr device = pointerSize == 8
            ? new IntPtr(BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(8, 8)))
            : new IntPtr(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8, 4)));

        ushort moveFlags = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(header, 2));
        ushort buttonFlags = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(header + 4, 2));
        short buttonData = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(header + 6, 2));
        int dx = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(header + 12, 4));
        int dy = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(header + 16, 4));

        data = new RawMouseData(
            device,
            buttonFlags,
            buttonData,
            dx,
            dy,
            (moveFlags & 0x0001) != 0);

        return true;
    }

    /// <summary>把按钮按下位映射成统一编号：左 1 / 右 2 / 中 3 / 侧4 4 / 侧5 5；无按下位为 0。</summary>
    public static int ButtonId(ushort buttonFlags)
    {
        if ((buttonFlags & LeftButtonDown) != 0)
        {
            return 1;
        }

        if ((buttonFlags & RightButtonDown) != 0)
        {
            return 2;
        }

        if ((buttonFlags & MiddleButtonDown) != 0)
        {
            return 3;
        }

        if ((buttonFlags & SideButton1Down) != 0)
        {
            return 4;
        }

        if ((buttonFlags & SideButton2Down) != 0)
        {
            return 5;
        }

        return 0;
    }

    /// <summary>该包是否包含指定按钮（1..5）的按下位；抬起位不算。</summary>
    public static bool IsButtonDown(ushort buttonFlags, int buttonId) =>
        buttonId switch
        {
            1 => (buttonFlags & LeftButtonDown) != 0,
            2 => (buttonFlags & RightButtonDown) != 0,
            3 => (buttonFlags & MiddleButtonDown) != 0,
            4 => (buttonFlags & SideButton1Down) != 0,
            5 => (buttonFlags & SideButton2Down) != 0,
            _ => false,
        };
}
