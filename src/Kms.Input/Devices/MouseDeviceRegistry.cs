using Kms.Core.Model;

namespace Kms.Input.Devices;

/// <summary>一个 RawInput 鼠标句柄对应的运行期槽位。</summary>
public sealed class MouseDeviceSlot
{
    public IntPtr Handle { get; set; }

    public MouseProfile? Profile { get; set; }

    public long LastSeenTicks { get; set; }

    public bool Resolving { get; set; }

    internal object Gate { get; } = new();
}

/// <summary>
/// RawInput 鼠标 → 每设备按下计数。只统计 <c>RI_MOUSE_*</c> 的按下位，
/// 滚轮/移动不进入磨损；同一格 30ms 去重由点击层负责，不在这里做。
/// </summary>
public sealed class MouseDeviceRegistry
{
    public const int Capacity = 32;

    /// <summary>左/右/中/X1/X2 的按下位。</summary>
    private const ushort DownMask = 0x0155;

    private readonly MouseDeviceSlot?[] _slots = new MouseDeviceSlot?[Capacity];
    private readonly PeripheralMouseWear _wear;

    public MouseDeviceRegistry(PeripheralMouseWear? wear = null)
    {
        _wear = wear ?? PeripheralMouseWear.Shared;
        Resolver = MouseDeviceIdentity.Resolve;
    }

    /// <summary>测试可替换；产品默认走真实 RawInput/注册表解析。</summary>
    public Func<IntPtr, MouseIdentity?> Resolver { get; set; }

    /// <summary>
    /// 解析完成回调；回调负责把结果写回 <see cref="MouseDeviceSlot.Profile"/> 并清除
    /// <see cref="MouseDeviceSlot.Resolving"/>。为 <c>null</c> 时后台原地归并。
    /// </summary>
    public Action<MouseDeviceSlot, MouseProfile, MouseIdentity?>? ResolveCompleted { get; set; }

    /// <summary>测试用同步解析；产品保持 <c>false</c>。</summary>
    public bool ResolveInline { get; set; }

    /// <summary>
    /// RawInput 鼠标按钮包。flags 可同时包含多个按下位，每个按钮计一次；
    /// 抬起位（0x0002/0x0008/...）被掩掉，不产生计数。
    /// </summary>
    public void HandleButtons(IntPtr hDevice, ushort buttonFlags, DateTime now)
    {
        if (buttonFlags == 0)
        {
            return;
        }

        ushort downs = (ushort)(buttonFlags & DownMask);
        if (downs == 0)
        {
            return;
        }

        MouseDeviceSlot? slot = FindSlot(hDevice);
        if (slot == null)
        {
            slot = TakeSlot(hDevice);
            if (slot == null)
            {
                Count(_wear.UnknownProfile, downs, now);
                return;
            }

            MouseProfile pending = _wear.CreatePendingProfile(hDevice);
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
        lock (slot.Gate)
        {
            Count(slot.Profile, downs, now);
        }
    }

    public void OnDeviceRemoved(IntPtr hDevice)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            MouseDeviceSlot? slot = _slots[i];
            if (slot == null || slot.Handle != hDevice)
            {
                continue;
            }

            if (slot.Profile is { Pending: true })
            {
                _wear.ResolvePending(slot.Profile, null);
            }

            _slots[i] = null;
        }
    }

    public void ResetForTest()
    {
        Array.Clear(_slots);
        Resolver = MouseDeviceIdentity.Resolve;
        ResolveCompleted = null;
        ResolveInline = false;
    }

    public MouseDeviceSlot? FindSlot(IntPtr handle)
    {
        foreach (MouseDeviceSlot? slot in _slots)
        {
            if (slot != null && slot.Handle == handle)
            {
                return slot;
            }
        }

        return null;
    }

    public List<MouseDeviceSlot> SnapshotSlots()
    {
        List<MouseDeviceSlot> list = new();
        foreach (MouseDeviceSlot? slot in _slots)
        {
            if (slot != null)
            {
                list.Add(slot);
            }
        }

        return list;
    }

    private void ResolveSlot(MouseDeviceSlot slot, MouseProfile pending, IntPtr handle)
    {
        MouseIdentity? identity = null;
        try
        {
            identity = Resolver?.Invoke(handle);
        }
        catch (Exception)
        {
            identity = null;
        }

        Action<MouseDeviceSlot, MouseProfile, MouseIdentity?>? callback = ResolveCompleted;
        if (callback != null)
        {
            callback(slot, pending, identity);
            return;
        }

        MouseProfile? resolved = _wear.ResolvePending(pending, identity);
        lock (slot.Gate)
        {
            if (ReferenceEquals(slot.Profile, pending))
            {
                slot.Profile = resolved ?? pending;
            }

            slot.Resolving = false;
        }
    }

    private MouseDeviceSlot? TakeSlot(IntPtr handle)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null)
            {
                continue;
            }

            MouseDeviceSlot slot = new() { Handle = handle };
            _slots[i] = slot;
            return slot;
        }

        return null;
    }

    private void Count(MouseProfile? profile, ushort downs, DateTime now)
    {
        if ((downs & 0x0001) != 0)
        {
            _wear.CountClick(profile, 1, now);
        }

        if ((downs & 0x0004) != 0)
        {
            _wear.CountClick(profile, 2, now);
        }

        if ((downs & 0x0010) != 0)
        {
            _wear.CountClick(profile, 3, now);
        }

        if ((downs & 0x0040) != 0)
        {
            _wear.CountClick(profile, 4, now);
        }

        if ((downs & 0x0100) != 0)
        {
            _wear.CountClick(profile, 5, now);
        }
    }
}
