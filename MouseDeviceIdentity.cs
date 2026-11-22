// ============================================================================
//  KeyMouseStats v3.4.1  MouseDeviceIdentity.cs
//
//  鼠标 RawInput 设备身份与每设备路由:
//    · 复用键盘的路径归一化 / 注册表友好名查询,不重复阻塞热路径;
//    · ACPI / 精密触控板 VID 启发式识别内置触控板,其余按外接鼠标;
//    · hDevice → MouseProfile 固定槽缓存,首次按键后后台解析;
//    · 只统计 RI_MOUSE_* 的按下位,滚轮/移动不进入磨损。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Globalization;
using System.Threading;

namespace KeyMouseStats
{
    internal sealed class MouseIdentity
    {
        public string Id = "";
        public string HardwareKey = "";
        public string InstanceKey = "";
        public string DevicePath = "";
        public string FriendlyName = "";
        public bool BuiltIn;
    }

    internal static class MouseDeviceIdentity
    {
        internal static MouseIdentity Resolve(IntPtr hDevice)
        {
            string path = KeyboardDeviceIdentity.GetDevicePath(hDevice);
            string instance = KeyboardDeviceIdentity.NormalizePath(path);
            if (instance.Length == 0) return null;

            string hardware = KeyboardDeviceIdentity.HardwareKeyOf(instance);
            string friendly = KeyboardDeviceIdentity.ReadFriendlyName(instance);
            bool builtIn = IsBuiltIn(instance, friendly);
            if (string.IsNullOrEmpty(friendly)) friendly = FallbackName(hardware, builtIn);

            MouseIdentity identity = new MouseIdentity();
            identity.DevicePath = instance;
            identity.InstanceKey = instance;
            identity.HardwareKey = hardware;
            identity.FriendlyName = friendly;
            identity.BuiltIn = builtIn;
            identity.Id = "mouse:" + (builtIn ? "builtin:" : "dev:")
                + KeyboardDeviceIdentity.Fnv(instance).ToString("X16", CultureInfo.InvariantCulture);
            return identity;
        }

        internal static bool IsBuiltIn(string instance, string friendly)
        {
            string text = ((instance ?? "") + " " + (friendly ?? "")).ToUpperInvariant();
            if (text.IndexOf("ACPI\\", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("I8042PRT", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("HID\\INTC", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("HID\\MSHW", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("VID_06CB", StringComparison.Ordinal) >= 0) return true;   // Synaptics
            if (text.IndexOf("VID_04F3", StringComparison.Ordinal) >= 0) return true;   // Elan
            if (text.IndexOf("VID_8087", StringComparison.Ordinal) >= 0) return true;   // Intel HID
            if (text.IndexOf("TOUCHPAD", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("TOUCH PAD", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("TRACKPAD", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("触控板", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("触摸板", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string FallbackName(string hardware, bool builtIn)
        {
            if (builtIn) return "笔记本触控板";
            if (!string.IsNullOrEmpty(hardware))
            {
                string[] parts = hardware.Split('\\');
                if (parts.Length >= 2 && parts[1].StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
                    return "外接鼠标 · " + parts[1].Replace('&', ' ');
            }
            return "外接鼠标";
        }
    }

    internal sealed class MouseDeviceSlot
    {
        public IntPtr Handle;
        public MouseProfile Profile;
        public long LastSeenTicks;
        public bool Resolving;
    }

    /// <summary>RawInput 鼠标 → 每设备按下计数。</summary>
    internal static class MouseDeviceRegistry
    {
        private const int Capacity = 32;
        private static readonly MouseDeviceSlot[] Slots = new MouseDeviceSlot[Capacity];

        /// <summary>测试可替换;产品默认走真实 RawInput/注册表解析。</summary>
        internal static Func<IntPtr, MouseIdentity> Resolver = MouseDeviceIdentity.Resolve;
        internal static Action<MouseDeviceSlot, MouseProfile, MouseIdentity> ResolveCompleted;

        internal static void HandleButtons(IntPtr hDevice, ushort flags, DateTime now)
        {
            if (flags == 0) return;
            ushort downs = (ushort)(flags & 0x0155);   // 左/右/中/X1/X2 的按下位
            if (downs == 0) return;

            MouseDeviceSlot slot = Find(hDevice);
            if (slot == null)
            {
                slot = TakeSlot(hDevice);
                if (slot == null)
                {
                    Count(PeripheralMouseWear.UnknownProfile, downs, now);
                    return;
                }
                slot.Profile = PeripheralMouseWear.CreatePendingProfile(hDevice);
                slot.Resolving = true;
                MouseDeviceSlot captured = slot;
                MouseProfile pending = slot.Profile;
                IntPtr handle = hDevice;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    MouseIdentity identity = null;
                    try
                    {
                        Func<IntPtr, MouseIdentity> resolver = Resolver;
                        identity = resolver == null ? null : resolver(handle);
                    }
                    catch { }
                    Action<MouseDeviceSlot, MouseProfile, MouseIdentity> callback = ResolveCompleted;
                    if (callback != null) callback(captured, pending, identity);
                    else
                    {
                        MouseProfile resolved = PeripheralMouseWear.ResolvePending(pending, identity);
                        if (captured.Profile == pending) captured.Profile = resolved;
                        captured.Resolving = false;
                    }
                });
            }

            slot.LastSeenTicks = Environment.TickCount;
            Count(slot.Profile, downs, now);
        }

        private static void Count(MouseProfile profile, ushort downs, DateTime now)
        {
            if ((downs & 0x0001) != 0) PeripheralMouseWear.CountClick(profile, 1, now);
            if ((downs & 0x0004) != 0) PeripheralMouseWear.CountClick(profile, 2, now);
            if ((downs & 0x0010) != 0) PeripheralMouseWear.CountClick(profile, 3, now);
            if ((downs & 0x0040) != 0) PeripheralMouseWear.CountClick(profile, 4, now);
            if ((downs & 0x0100) != 0) PeripheralMouseWear.CountClick(profile, 5, now);
        }

        internal static void OnDeviceRemoved(IntPtr hDevice)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] == null || Slots[i].Handle != hDevice) continue;
                if (Slots[i].Profile != null && Slots[i].Profile.Pending)
                    PeripheralMouseWear.ResolvePending(Slots[i].Profile, null);
                Slots[i] = null;
            }
        }

        internal static void ResetForTest()
        {
            for (int i = 0; i < Slots.Length; i++) Slots[i] = null;
            Resolver = MouseDeviceIdentity.Resolve;
            ResolveCompleted = null;
        }

        private static MouseDeviceSlot Find(IntPtr handle)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i] != null && Slots[i].Handle == handle) return Slots[i];
            return null;
        }

        private static MouseDeviceSlot TakeSlot(IntPtr handle)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] != null) continue;
                Slots[i] = new MouseDeviceSlot();
                Slots[i].Handle = handle;
                return Slots[i];
            }
            return null;
        }
    }
}
