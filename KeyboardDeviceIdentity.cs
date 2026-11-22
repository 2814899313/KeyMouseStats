// ============================================================================
//  KeyMouseStats v3.4.1  KeyboardDeviceIdentity.cs
//
//  把 RawInput 的 hDevice 解析成稳定键盘身份:
//    · GetRawInputDeviceInfo(RIDI_DEVICENAME) 取接口路径;
//    · ACPI\PNP0303 / HID\INTC / HID\VID_8087 等启发式识别内置键盘;
//    · HID\VID_xxxx&PID_xxxx / BTHENUM 识别外接键盘;
//    · 后台读取注册表 FriendlyName / DeviceDesc,热路径只命中缓存。
//
//  RawInput 键盘按 hDevice 维护物理键按下位图,只有首次按下才累加对应
//  KeyboardProfile;自动连发不会重复。低级钩子只负责全局统计。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace KeyMouseStats
{
    internal sealed class KeyboardIdentity
    {
        public string Id = "";
        public string HardwareKey = "";
        public string InstanceKey = "";
        public string DevicePath = "";
        public string FriendlyName = "";
        public bool BuiltIn;
    }

    internal static class KeyboardDeviceIdentity
    {
        private const uint RidiDeviceName = 0x20000007;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command,
            StringBuilder data, ref uint size);

        internal static KeyboardIdentity Resolve(IntPtr hDevice)
        {
            string path = GetDevicePath(hDevice);
            string instance = NormalizePath(path);
            if (instance.Length == 0) return null;

            string hardware = HardwareKeyOf(instance);
            bool builtIn = IsBuiltIn(instance, "");
            string friendly = ReadFriendlyName(instance);
            if (!string.IsNullOrEmpty(friendly) && IsBuiltIn(instance, friendly)) builtIn = true;
            if (string.IsNullOrEmpty(friendly)) friendly = FallbackName(instance, hardware, builtIn);

            KeyboardIdentity identity = new KeyboardIdentity();
            identity.DevicePath = instance;
            identity.InstanceKey = instance;
            identity.HardwareKey = hardware;
            identity.FriendlyName = friendly;
            identity.BuiltIn = builtIn;
            identity.Id = (builtIn ? "builtin:" : "dev:") + Fnv(instance).ToString("X16", CultureInfo.InvariantCulture);
            return identity;
        }

        internal static string GetDevicePath(IntPtr hDevice)
        {
            try
            {
                uint size = 0;
                GetRawInputDeviceInfo(hDevice, RidiDeviceName, null, ref size);
                if (size == 0 || size > 4096) return "";
                StringBuilder sb = new StringBuilder((int)size + 2);
                uint read = GetRawInputDeviceInfo(hDevice, RidiDeviceName, sb, ref size);
                if (read == uint.MaxValue) return "";
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string text = path.Trim();
            if (text.StartsWith(@"\\?\", StringComparison.Ordinal)) text = text.Substring(4);
            text = text.Replace('#', '\\');
            while (text.IndexOf(@"\\", StringComparison.Ordinal) >= 0)
                text = text.Replace(@"\\", @"\");
            int last = text.LastIndexOf('\\');
            if (last > 0)
            {
                string tail = text.Substring(last + 1);
                if (tail.Length >= 2 && tail[0] == '{' && tail[tail.Length - 1] == '}')
                    text = text.Substring(0, last);
            }
            return text.Trim('\\');
        }

        internal static string HardwareKeyOf(string instance)
        {
            if (string.IsNullOrEmpty(instance)) return "";
            string[] parts = instance.Split('\\');
            if (parts.Length >= 2 &&
                (string.Equals(parts[0], "ACPI", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(parts[0], "BTHENUM", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(parts[0], "BTHHFENUM", StringComparison.OrdinalIgnoreCase)))
                return (parts[0] + "\\" + parts[1]).ToUpperInvariant();
            if (parts.Length >= 2 && string.Equals(parts[0], "HID", StringComparison.OrdinalIgnoreCase))
                return (parts[0] + "\\" + parts[1]).ToUpperInvariant();
            return instance.ToUpperInvariant();
        }

        internal static bool IsBuiltIn(string instance, string friendly)
        {
            string text = ((instance ?? "") + " " + (friendly ?? "")).ToUpperInvariant();
            if (text.IndexOf("ACPI\\PNP0303", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("ACPI\\PNP030B", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("ACPI\\PNP0320", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("I8042PRT", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("HID\\INTC", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("HID\\VID_8087", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("HID\\MSHW", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("内置", StringComparison.Ordinal) >= 0) return true;
            if (text.IndexOf("INTERNAL KEYBOARD", StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string FallbackName(string instance, string hardware, bool builtIn)
        {
            if (builtIn) return "笔记本内置键盘";
            if (!string.IsNullOrEmpty(hardware))
            {
                string[] parts = hardware.Split('\\');
                if (parts.Length >= 2 && parts[1].StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
                    return "外接键盘 · " + parts[1].Replace('&', ' ');
            }
            return "外接键盘";
        }

        internal static string ReadFriendlyName(string instance)
        {
            try
            {
                string keyPath = @"SYSTEM\CurrentControlSet\Enum\" + instance;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null) return "";
                    object value = key.GetValue("FriendlyName");
                    if (value == null) value = key.GetValue("DeviceDesc");
                    string text = value as string;
                    if (string.IsNullOrEmpty(text)) return "";
                    // 资源间接字符串(@%SystemRoot%\...)不在这里展开,避免阻塞/额外依赖。
                    if (text.StartsWith("@", StringComparison.Ordinal)) return "";
                    return text.Trim();
                }
            }
            catch { return ""; }
        }

        internal static ulong Fnv(string text)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }

    internal sealed class KeyboardDeviceSlot
    {
        public IntPtr Handle;
        public KeyboardProfile Profile;
        public readonly long[] Pressed = new long[16];   // 1024 个物理键位
        public long LastSeenTicks;
        public bool Resolving;

        internal void ClearPressed()
        {
            for (int i = 0; i < Pressed.Length; i++) Pressed[i] = 0;
        }
    }

    /// <summary>RawInput 键盘 → 每设备物理键按下位图与磨损计数。</summary>
    internal static class KeyboardDeviceRegistry
    {
        private const int Capacity = 32;
        private static readonly KeyboardDeviceSlot[] Slots = new KeyboardDeviceSlot[Capacity];

        /// <summary>测试可替换;产品默认走真实 RawInput/注册表解析。</summary>
        internal static Func<IntPtr, KeyboardIdentity> Resolver = KeyboardDeviceIdentity.Resolve;
        /// <summary>解析完成回调;产品在 UI 线程 BeginInvoke 合并 pending 档案。</summary>
        internal static Action<KeyboardDeviceSlot, KeyboardProfile, KeyboardIdentity> ResolveCompleted;

        internal static void HandleRawKey(IntPtr hDevice, ushort vk, ushort scan,
            ushort flags, bool down)
        {
            if (vk == 0 || vk == 0xFF) return;
            KeyboardDeviceSlot slot = Find(hDevice);
            // 设备已拔出后迟到的抬起事件:不建新槽,避免产生幽灵档案。
            if (slot == null && !down) return;
            if (slot == null)
            {
                slot = TakeSlot(hDevice);
                if (slot == null)
                {
                    KeyboardProfile unknown = PeripheralWear.UnknownProfile;
                    if (down) PeripheralWear.CountKey(unknown, Normalize(vk, scan, flags));
                    return;
                }
                slot.Profile = PeripheralWear.CreatePendingProfile(hDevice);
                slot.Resolving = true;
                KeyboardDeviceSlot captured = slot;
                KeyboardProfile pending = slot.Profile;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    KeyboardIdentity identity = null;
                    try { Func<IntPtr, KeyboardIdentity> resolver = Resolver; identity = resolver == null ? null : resolver(hDevice); }
                    catch { }
                    Action<KeyboardDeviceSlot, KeyboardProfile, KeyboardIdentity> callback = ResolveCompleted;
                    if (callback != null) callback(captured, pending, identity);
                    else
                    {
                        KeyboardProfile resolved = PeripheralWear.ResolvePending(pending, identity);
                        if (captured.Profile == pending) captured.Profile = resolved;
                        captured.Resolving = false;
                    }
                });
            }

            slot.LastSeenTicks = Environment.TickCount;
            uint hookFlags = (flags & 2) != 0 ? 1u : 0u;
            int physical = KeyboardHeat.ScanId(vk, scan, hookFlags);
            if (physical <= 0) physical = 0x300 + (vk & 0xFF);
            if (physical < 0 || physical >= slot.Pressed.Length * 64) return;
            int word = physical >> 6;
            long mask = 1L << (physical & 63);
            if (down)
            {
                if ((slot.Pressed[word] & mask) != 0) return;
                slot.Pressed[word] |= mask;
                PeripheralWear.CountKey(slot.Profile, Normalize(vk, scan, flags));
            }
            else
            {
                slot.Pressed[word] &= ~mask;
            }
        }

        internal static void OnDeviceRemoved(IntPtr hDevice)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] == null || Slots[i].Handle != hDevice) continue;
                Slots[i].ClearPressed();
                if (Slots[i].Profile != null && Slots[i].Profile.Pending)
                    PeripheralWear.ResolvePending(Slots[i].Profile, null);
                Slots[i] = null;
            }
        }

        internal static void ClearPressed()
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i] != null) Slots[i].ClearPressed();
        }

        internal static void ResetForTest()
        {
            for (int i = 0; i < Slots.Length; i++) Slots[i] = null;
            Resolver = KeyboardDeviceIdentity.Resolve;
            ResolveCompleted = null;
        }

        private static KeyboardDeviceSlot Find(IntPtr handle)
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i] != null && Slots[i].Handle == handle) return Slots[i];
            return null;
        }

        private static KeyboardDeviceSlot TakeSlot(IntPtr handle)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] != null) continue;
                Slots[i] = new KeyboardDeviceSlot();
                Slots[i].Handle = handle;
                return Slots[i];
            }
            return null;
        }

        private static int Normalize(ushort vk, ushort scan, ushort flags)
        {
            uint hookFlags = (flags & 2) != 0 ? 1u : 0u;
            return ShortcutTracker.Normalize((int)vk, scan, hookFlags);
        }
    }
}
