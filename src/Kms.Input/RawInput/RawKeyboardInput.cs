using System.Buffers.Binary;

namespace Kms.Input.RawInput;

/// <summary>
/// 一个 WM_INPUT 键盘包的纯托管快照。Flags 保留 RI_KEY_* 原始位，
/// 按下/抬起由最低位 RI_KEY_BREAK 单独解释。
/// </summary>
public readonly struct RawKeyboardData
{
    internal RawKeyboardData(IntPtr device, int virtualKey, int makeCode, uint flags, bool down)
    {
        Device = device;
        VirtualKey = virtualKey;
        MakeCode = makeCode;
        Flags = flags;
        Down = down;
    }

    /// <summary>RAWINPUTHEADER.hDevice。</summary>
    public IntPtr Device { get; }

    /// <summary>RAWKEYBOARD.VKey（虚拟键码）。</summary>
    public int VirtualKey { get; }

    /// <summary>RAWKEYBOARD.MakeCode（扫描码）。</summary>
    public int MakeCode { get; }

    /// <summary>RAWKEYBOARD.Flags 的 RI_KEY_* 原始位。</summary>
    public uint Flags { get; }

    /// <summary>true = 按下；false = 抬起（RI_KEY_BREAK 置位）。</summary>
    public bool Down { get; }

    /// <summary>RI_KEY_BREAK：这一包是抬起事件。</summary>
    public bool IsBreak => (Flags & RawKeyboardInput.BreakFlag) != 0;

    /// <summary>RI_KEY_E0：扩展键（方向键、右 Ctrl 等）的扫描码前缀。</summary>
    public bool IsExtended => (Flags & RawKeyboardInput.E0Flag) != 0;
}

/// <summary>
/// WM_INPUT 键盘包解码器。实例持有一个复用的接收缓冲区，只应在消息循环线程上调用
/// <see cref="Read"/>；纯解码静态方法可以在任意线程/测试中使用。
/// </summary>
public sealed class RawKeyboardInput
{
    /// <summary>RI_KEY_BREAK：按键抬起。</summary>
    public const uint BreakFlag = 0x0001;

    /// <summary>RI_KEY_E0：扩展键前缀。</summary>
    public const uint E0Flag = 0x0002;

    /// <summary>RI_KEY_E1：Pause/Break 一类特殊键的 E1 前缀。</summary>
    public const uint E1Flag = 0x0004;

    internal const int DataSize = 16;

    private readonly byte[] _buffer =
        new byte[RawInputNative.HeaderPrefixSize + (2 * IntPtr.Size) + DataSize];

    private volatile bool _readFailed;

    /// <summary>最近一次 GetRawInputData 是否失败（与「这个包不是键盘」区分开）。</summary>
    public bool ReadFailed => _readFailed;

    /// <summary>
    /// 读取并解码一个 WM_INPUT 包。非键盘包返回 <c>false</c>，且不影响 <see cref="ReadFailed"/>。
    /// 本方法不阻塞、不分配新对象；内部缓冲区在实例上复用。
    /// </summary>
    public bool Read(IntPtr rawInput, out RawKeyboardData data)
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
    public static bool TryDecode(ReadOnlySpan<byte> packet, out RawKeyboardData data) =>
        TryDecode(packet, IntPtr.Size, out data);

    /// <summary>
    /// 纯解码入口。packet 必须包含完整的 RAWINPUTHEADER 与 RAWKEYBOARD。
    /// 指针宽度只接受 4/8；类型、声明长度或截断任一不符都返回 <c>false</c>。
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> packet, int pointerSize, out RawKeyboardData data)
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

        if (BinaryPrimitives.ReadUInt32LittleEndian(packet) != RawInputNative.RimTypeKeyboard)
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

        ushort makeCode = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(header, 2));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(header + 2, 2));
        ushort virtualKey = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(header + 6, 2));

        data = new RawKeyboardData(
            device,
            virtualKey,
            makeCode,
            flags,
            (flags & BreakFlag) == 0);

        return true;
    }
}
