using System;
using KeyMouseStats;

/// <summary>
/// 原始输入解码。鼠标的点击/滚轮现在完全由这条路计数 —— 它不经过低级钩子,
/// 不会被 LowLevelHooksTimeout 摘掉。解错一个位,用户看到的就是「点了没反应」。
/// </summary>
internal static class RawInputTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("RawInputTests", Run);
    }

    private static void Put(byte[] bytes, int offset, int value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
    }

    private static void PutShort(byte[] bytes, int offset, short value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 2);
    }

    private static void PutUShort(byte[] bytes, int offset, ushort value)
    {
        Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 2);
    }

    private static void PutPtr(byte[] bytes, int offset, IntPtr value, int pointerSize)
    {
        if (pointerSize == 8)
            Array.Copy(BitConverter.GetBytes(value.ToInt64()), 0, bytes, offset, 8);
        else
            Array.Copy(BitConverter.GetBytes(value.ToInt32()), 0, bytes, offset, 4);
    }

    private static void Run()
    {
        foreach (int pointerSize in new[] { 4, 8 })
        {
            int header = 8 + 2 * pointerSize;

            // ---- 鼠标包:按钮位在 +4,滚轮增量在 +6 ----
            byte[] mouse = new byte[header + 24];
            Put(mouse, 0, 0);                 // RIM_TYPEMOUSE
            Put(mouse, 4, mouse.Length);      // dwSize
            PutUShort(mouse, header + 4, 0x0001);   // 左键按下
            PutShort(mouse, header + 6, 120);       // 滚轮一格

            ushort flags;
            short wheel;
            Check(RawMouseInput.DecodeButtons(mouse, pointerSize, out flags, out wheel), "a mouse packet decodes");
            Check((flags & 0x0001) != 0, "the left button flag is read");
            Check((flags & 0x0004) == 0, "the right button flag stays clear");
            Check(wheel == 120, "the wheel delta is read");

            // 侧键 + 横滚
            PutUShort(mouse, header + 4, 0x0140);   // 侧键4(0x0040) + 侧键5(0x0100)
            PutShort(mouse, header + 6, -120);
            Check(RawMouseInput.DecodeButtons(mouse, pointerSize, out flags, out wheel), "a side-button packet decodes");
            Check((flags & 0x0040) != 0 && (flags & 0x0100) != 0, "both side buttons are visible");
            Check(wheel == -120, "a negative wheel delta is preserved");

            // ---- 绝对坐标包:位移不参与 DPI 换算,但按钮/滚轮仍必须解码 ----
            PutUShort(mouse, header, 1);            // MOUSE_MOVE_ABSOLUTE
            PutUShort(mouse, header + 4, 0x0001);   // 左键按下
            PutShort(mouse, header + 6, 120);
            double absoluteCounts;
            bool absolute;
            ushort absoluteFlags;
            short absoluteWheel;
            Check(RawMouseInput.DecodeAll(mouse, mouse.Length, pointerSize, out absoluteCounts,
                out absolute, out absoluteFlags, out absoluteWheel),
                "an absolute packet must still decode buttons/wheel");
            Check(absolute && absoluteCounts == 0, "absolute movement is not used for distance");
            Check((absoluteFlags & 0x0001) != 0 && absoluteWheel == 120,
                "absolute packet keeps its button and wheel data");
            PutUShort(mouse, header, 0);

            // ---- 键盘包:类型 1,RI_KEY_BREAK 在 flags 的最低位 ----
            byte[] key = new byte[header + 16];
            Put(key, 0, 1);                    // RIM_TYPEKEYBOARD
            Put(key, 4, key.Length);
            PutPtr(key, 8, new IntPtr(0x1234), pointerSize);
            PutUShort(key, header, 0x1E);      // MakeCode = A
            PutUShort(key, header + 2, 0);     // 按下
            PutUShort(key, header + 6, 0x41);  // VK_A
            Put(key, header + 8, 0x0100);      // WM_KEYDOWN
            Put(key, header + 12, 0x55AA);     // ExtraInformation

            RawKeyData full;
            Check(RawKeyInput.DecodeFull(key, key.Length, pointerSize, out full), "a full keyboard packet decodes");
            Check(full.Device == new IntPtr(0x1234), "hDevice is read from offset 8");
            Check(full.MakeCode == 0x1E && full.VKey == 0x41 && full.Message == 0x0100,
                "make code, vkey and message are read");
            Check(full.ExtraInformation == 0x55AA && full.Down, "extra info and down state are read");

            ushort vk;
            bool down;
            Check(RawKeyInput.Decode(key, key.Length, pointerSize, out vk, out down), "a keyboard packet decodes");
            Check(vk == 0x41, "the virtual key is read");
            Check(down, "flags 0 means a key down");

            PutUShort(key, header + 2, 1);     // RI_KEY_BREAK
            Check(RawKeyInput.Decode(key, key.Length, pointerSize, out vk, out down), "a key-up packet decodes");
            Check(!down, "RI_KEY_BREAK means key up");

            // ---- 两种包不能互相误判 ----
            Check(!RawMouseInput.DecodeButtons(key, pointerSize, out flags, out wheel), "a keyboard packet is not a mouse packet");
            Check(!RawKeyInput.Decode(mouse, mouse.Length, pointerSize, out vk, out down), "a mouse packet is not a keyboard packet");
        }

        // 指针宽度非法 / 包太短:一律拒绝,不能让调用方拿到半截数据。
        ushort f; short w2;
        Check(!RawMouseInput.DecodeButtons(new byte[8], 3, out f, out w2), "an invalid pointer size is rejected");
        Check(!RawMouseInput.DecodeButtons(new byte[8], 8, out f, out w2), "a short packet is rejected");
        Check(!RawMouseInput.DecodeButtons(null, 8, out f, out w2), "a null packet is rejected");
        ushort v; bool d;
        Check(!RawKeyInput.Decode(new byte[8], 8, 8, out v, out d), "a short keyboard packet is rejected");
        Check(!RawKeyInput.Decode(null, 0, 8, out v, out d), "a null keyboard packet is rejected");

        // ---- WHEEL_DELTA 累计:余数跨包保留,正负方向都能结算 ----
        WheelAccumulator wheelAccumulator = new WheelAccumulator();
        Check(wheelAccumulator.Add(60) == 0 && wheelAccumulator.Remainder == 60,
            "a half notch stays in the remainder");
        Check(wheelAccumulator.Add(60) == 1 && wheelAccumulator.Remainder == 0,
            "two half notches settle one standard scroll");
        Check(wheelAccumulator.Add(-30) == 0 && wheelAccumulator.Remainder == -30,
            "negative delta accumulates its sign");
        Check(wheelAccumulator.Add(-90) == -1 && wheelAccumulator.Remainder == 0,
            "negative remainder settles one negative scroll");
        Check(wheelAccumulator.Add(240) == 2, "a 240 delta settles two scrolls");
        WheelAccumulator horizontal = new WheelAccumulator();
        Check(horizontal.Add(120) == 1 && wheelAccumulator.Remainder == 0,
            "vertical and horizontal accumulators stay independent");

        Console.WriteLine("RawInputTests: " + checks + " checks passed");
    }
}
