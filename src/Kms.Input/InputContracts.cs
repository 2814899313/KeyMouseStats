namespace Kms.Input;

/// <summary>
/// 低级钩子的最小输出接缝。实现方负责把事件并入当日统计/写线程，
/// 钩子自身只做廉价字段读取，绝不跨进程解析窗口标题。
/// </summary>
public interface IInputSink
{
    void Key(int virtualKey, bool down, long ticks);

    /// <summary>
    /// 带扫描码与标志位的键盘事件。默认退化为 <see cref="Key"/>；
    /// 需要区分左右修饰键/物理键位的实现应覆盖它。
    /// </summary>
    void KeyEx(int virtualKey, int scanCode, uint flags, bool down, long ticks) =>
        Key(virtualKey, down, ticks);

    void MouseButton(int button, bool down, int x, int y, long ticks);

    void Wheel(bool horizontal, int delta, long ticks);

    void Move(int x, int y, long ticks);
}

/// <summary>Raw Input 输出接缝：设备级路由（磨损、每设备按下位图）。</summary>
public interface IRawInputSink
{
    void RawMouse(IntPtr device, ushort buttonFlags, short wheelDelta, int dx, int dy);

    void RawKey(IntPtr device, int virtualKey, int makeCode, uint flags, bool down);

    void DeviceChanged(IntPtr device, bool removed);
}

public static class InputMessages
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSEHWHEEL = 0x020E;

    public const int WM_INPUT = 0x00FF;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;

    /// <summary>统一鼠标按钮编号：1=左 2=右 3=中 4=XButton1 5=XButton2。</summary>
    public static int MouseButtonId(int message, uint mouseData)
    {
        if (message is WM_LBUTTONDOWN or WM_LBUTTONUP)
        {
            return 1;
        }

        if (message is WM_RBUTTONDOWN or WM_RBUTTONUP)
        {
            return 2;
        }

        if (message is WM_MBUTTONDOWN or WM_MBUTTONUP)
        {
            return 3;
        }

        if (message is WM_XBUTTONDOWN or WM_XBUTTONUP)
        {
            int x = (int)((mouseData >> 16) & 0xFFFF);
            return x is >= 1 and <= 2 ? 3 + x : 0;
        }

        return 0;
    }
}
