using Kms.Core.Model;

namespace Kms.Input.Devices;

/// <summary>一个 RawInput 键盘句柄对应的运行期槽位。</summary>
public sealed class KeyboardDeviceSlot
{
    public IntPtr Handle { get; set; }

    public KeyboardProfile? Profile { get; set; }

    /// <summary>1024 个物理键位的按下位图；只有首次按下才计数，自动连发不会重复。</summary>
    public long[] Pressed { get; } = new long[16];

    public long LastSeenTicks { get; set; }

    public bool Resolving { get; set; }

    internal object Gate { get; } = new();

    internal void ClearPressed()
    {
        Array.Clear(Pressed);
    }
}

/// <summary>
/// RawInput 键盘 → 每设备物理键按下位图与磨损计数。
/// 按 <c>hDevice</c> 固定槽路由；首次按键后在后台解析身份，再并入稳定档案。
/// </summary>
public sealed class KeyboardDeviceRegistry
{
    public const int Capacity = 32;

    private readonly KeyboardDeviceSlot?[] _slots = new KeyboardDeviceSlot?[Capacity];
    private readonly PeripheralWear _wear;

    public KeyboardDeviceRegistry(PeripheralWear? wear = null)
    {
        _wear = wear ?? PeripheralWear.Shared;
        Resolver = KeyboardDeviceIdentity.Resolve;
    }

    /// <summary>测试可替换；产品默认走真实 RawInput/注册表解析。</summary>
    public Func<IntPtr, KeyboardIdentity?> Resolver { get; set; }

    /// <summary>
    /// 解析完成回调；产品在 UI 线程合并 pending 档案。回调负责把结果写回
    /// <see cref="KeyboardDeviceSlot.Profile"/> 并清除 <see cref="KeyboardDeviceSlot.Resolving"/>。
    /// 为 <c>null</c> 时直接在后台原地归并。
    /// </summary>
    public Action<KeyboardDeviceSlot, KeyboardProfile, KeyboardIdentity?>? ResolveCompleted { get; set; }

    /// <summary>
    /// 测试用同步解析。产品保持 <c>false</c>，身份解析在工作线程执行，热路径不读注册表。
    /// </summary>
    public bool ResolveInline { get; set; }

    /// <summary>
    /// RawInput 键盘事件。只有物理按下首次触发才累加对应档案；
    /// 抬起只清位图；设备已拔出后迟到的抬起不建新槽。
    /// </summary>
    public void HandleRawKey(IntPtr hDevice, int virtualKey, int makeCode, uint flags, bool down)
    {
        if (virtualKey is <= 0 or >= 0xFF)
        {
            return;
        }

        KeyboardDeviceSlot? slot = FindSlot(hDevice);
        if (slot == null && !down)
        {
            return;
        }

        if (slot == null)
        {
            slot = TakeSlot(hDevice);
            if (slot == null)
            {
                if (down)
                {
                    _wear.CountKey(_wear.UnknownProfile, NormalizeVirtualKey(virtualKey, makeCode, flags));
                }

                return;
            }

            KeyboardProfile pending = _wear.CreatePendingProfile(hDevice);
            slot.Profile = pending;
            slot.Resolving = true;
            if (ResolveInline)
            {
                ResolveSlot(slot, pending, hDevice);
            }
            else
            {
                IntPtr handle = hDevice;
                WaitCallback callback = _ => ResolveSlot(slot, pending, handle);
                ThreadPool.QueueUserWorkItem(callback);
            }
        }

        slot.LastSeenTicks = Environment.TickCount64;
        int normalized = NormalizeVirtualKey(virtualKey, makeCode, flags);
        int physical = PhysicalKeyId(virtualKey, makeCode, flags);
        if (physical <= 0)
        {
            physical = 0x300 + (virtualKey & 0xFF);
        }

        if (physical < 0 || physical >= slot.Pressed.Length * 64)
        {
            return;
        }

        int word = physical >> 6;
        long mask = 1L << (physical & 63);
        lock (slot.Gate)
        {
            if (down)
            {
                if ((slot.Pressed[word] & mask) != 0)
                {
                    return;
                }

                slot.Pressed[word] |= mask;
                _wear.CountKey(slot.Profile, normalized);
            }
            else
            {
                slot.Pressed[word] &= ~mask;
            }
        }
    }

    public void OnDeviceRemoved(IntPtr hDevice)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            KeyboardDeviceSlot? slot = _slots[i];
            if (slot == null || slot.Handle != hDevice)
            {
                continue;
            }

            slot.ClearPressed();
            if (slot.Profile is { Pending: true })
            {
                _wear.ResolvePending(slot.Profile, null);
            }

            _slots[i] = null;
        }
    }

    public void ClearPressed()
    {
        foreach (KeyboardDeviceSlot? slot in _slots)
        {
            slot?.ClearPressed();
        }
    }

    public void ResetForTest()
    {
        Array.Clear(_slots);
        Resolver = KeyboardDeviceIdentity.Resolve;
        ResolveCompleted = null;
        ResolveInline = false;
    }

    public KeyboardDeviceSlot? FindSlot(IntPtr handle)
    {
        foreach (KeyboardDeviceSlot? slot in _slots)
        {
            if (slot != null && slot.Handle == handle)
            {
                return slot;
            }
        }

        return null;
    }

    public List<KeyboardDeviceSlot> SnapshotSlots()
    {
        List<KeyboardDeviceSlot> list = new();
        foreach (KeyboardDeviceSlot? slot in _slots)
        {
            if (slot != null)
            {
                list.Add(slot);
            }
        }

        return list;
    }

    /// <summary>
    /// 虚拟键归一化：左右 Shift/Ctrl/Alt 拆成 0xA0..0xA5，与 v3 ShortcutTracker.Normalize 一致。
    /// RawInput 的扩展键标志是 <c>RI_KEY_E0 = 0x02</c>。
    /// </summary>
    public static int NormalizeVirtualKey(int virtualKey, int makeCode, uint flags)
    {
        if (virtualKey == 16)
        {
            return makeCode == 0x36 ? 0xA1 : 0xA0;
        }

        if (virtualKey == 17)
        {
            return (flags & 2) != 0 ? 0xA3 : 0xA2;
        }

        if (virtualKey == 18)
        {
            return (flags & 2) != 0 ? 0xA5 : 0xA4;
        }

        return virtualKey;
    }

    /// <summary>
    /// 物理键位：扫描码低 8 位 + 扩展位；Pause 特判 0x245。
    /// 扫描码未知时返回 0，由调用方回退到虚拟键键位。
    /// </summary>
    public static int PhysicalKeyId(int virtualKey, int makeCode, uint flags)
    {
        if (makeCode == 0)
        {
            return 0;
        }

        if (virtualKey == 19)
        {
            return 0x245;
        }

        return (makeCode & 255) | ((flags & 2) != 0 ? 256 : 0);
    }

    private void ResolveSlot(KeyboardDeviceSlot slot, KeyboardProfile pending, IntPtr handle)
    {
        KeyboardIdentity? identity = null;
        try
        {
            identity = Resolver?.Invoke(handle);
        }
        catch (Exception)
        {
            // 设备解析失败退化为「未知键盘」，不能让后台任务把进程带崩。
            identity = null;
        }

        Action<KeyboardDeviceSlot, KeyboardProfile, KeyboardIdentity?>? callback = ResolveCompleted;
        if (callback != null)
        {
            callback(slot, pending, identity);
            return;
        }

        KeyboardProfile? resolved = _wear.ResolvePending(pending, identity);
        lock (slot.Gate)
        {
            if (ReferenceEquals(slot.Profile, pending))
            {
                slot.Profile = resolved ?? pending;
            }

            slot.Resolving = false;
        }
    }

    private KeyboardDeviceSlot? TakeSlot(IntPtr handle)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null)
            {
                continue;
            }

            KeyboardDeviceSlot slot = new() { Handle = handle };
            _slots[i] = slot;
            return slot;
        }

        return null;
    }
}
