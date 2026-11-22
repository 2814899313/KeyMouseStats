// ============================================================================
//  KeyMouseStats v3.4.1  PeripheralWear.cs
//
//  外设硬件纪元计数器。与 Store.Total / History 完全独立。
//  v3.4.1 起支持多键盘档案:
//    · 每把键盘独立 Id / Alias / FriendlyName / Rated / Since / long[256];
//    · RawInput 每设备位图去重后计数;低级钩子只负责全局统计;
//    · keyboard_v1 / keyboard_keys_v1 持久化,hardware_v1 / wear_keys_v1 继续
//      镜像主键盘,保证旧版可读;
//    · 旧版单键盘自动成为 legacy 档案,可一键归入当前键盘。
//
//  热路径只做 Interlocked 计数与固定槽查找,不做字符串/注册表/磁盘 I/O。
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace KeyMouseStats
{
    internal sealed class KeyWear
    {
        public int Vk;
        public string Name;
        public long Count;
    }

    /// <summary>一把键盘的独立磨损档案。</summary>
    internal sealed class KeyboardProfile
    {
        internal const int KeySlots = 256;

        public string Id = "";
        public string Alias = "";
        public string FriendlyName = "";
        public string DevicePath = "";
        public string HardwareKey = "";
        public string InstanceKey = "";
        public bool BuiltIn;
        public bool Legacy;
        public bool Pending;
        public long Rated = PeripheralWear.DefaultKeyboardRated;
        public DateTime Since = DateTime.MinValue;
        public bool Seeded;
        public long LastSeenUtcTicks;
        public readonly long[] Keys = new long[KeySlots];

        internal void CountKey(int vk)
        {
            if (vk < 0 || vk >= KeySlots) return;
            Interlocked.Increment(ref Keys[vk]);
        }

        internal long ReadKey(int vk)
        {
            return vk >= 0 && vk < KeySlots ? Interlocked.Read(ref Keys[vk]) : 0;
        }

        internal long TotalKeyPresses
        {
            get
            {
                long total = 0;
                for (int i = 0; i < KeySlots; i++) total += Interlocked.Read(ref Keys[i]);
                return total;
            }
        }

        internal void Reset(DateTime now)
        {
            for (int i = 0; i < KeySlots; i++) Interlocked.Exchange(ref Keys[i], 0);
            Since = now;
        }

        internal string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Alias)) return Alias;
                if (!string.IsNullOrEmpty(FriendlyName)) return FriendlyName;
                if (BuiltIn) return "笔记本内置键盘";
                if (Legacy) return "旧版单键盘";
                return "未命名键盘";
            }
        }

        internal string EncodeHeader()
        {
            int flags = (BuiltIn ? 1 : 0) | (Legacy ? 2 : 0) | (Pending ? 4 : 0);
            return Id + "|" + PeripheralWear.EncodeName(Alias) + "|"
                + PeripheralWear.EncodeName(FriendlyName) + "|"
                + PeripheralWear.EncodeName(DevicePath) + "|"
                + PeripheralWear.EncodeName(HardwareKey) + "|"
                + PeripheralWear.EncodeName(InstanceKey) + "|"
                + flags.ToString(CultureInfo.InvariantCulture) + "|"
                + Rated.ToString(CultureInfo.InvariantCulture) + "|"
                + PeripheralWear.TimeText(Since) + "|"
                + (Seeded ? "1" : "0") + "|"
                + LastSeenUtcTicks.ToString(CultureInfo.InvariantCulture);
        }

        internal string EncodeKeys()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Id).Append('|');
            for (int vk = 0; vk < KeySlots; vk++)
            {
                long value = Interlocked.Read(ref Keys[vk]);
                if (value <= 0) continue;
                if (sb.Length > Id.Length + 1) sb.Append(',');
                sb.Append(vk.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(value.ToString(CultureInfo.InvariantCulture));
            }
            if (sb.Length == Id.Length + 1) sb.Append('-');
            return sb.ToString();
        }

        internal static KeyboardProfile DecodeHeader(string text)
        {
            string[] p = (text ?? "").Split('|');
            if (p.Length < 11) return null;
            string id = p[0] ?? "";
            if (id.Length == 0) return null;
            KeyboardProfile profile = new KeyboardProfile();
            profile.Id = id;
            profile.Alias = PeripheralWear.DecodeName(p[1]);
            profile.FriendlyName = PeripheralWear.DecodeName(p[2]);
            profile.DevicePath = PeripheralWear.DecodeName(p[3]);
            profile.HardwareKey = PeripheralWear.DecodeName(p[4]);
            profile.InstanceKey = PeripheralWear.DecodeName(p[5]);
            int flags = (int)PeripheralWear.ParseLong(p[6]);
            profile.BuiltIn = (flags & 1) != 0;
            profile.Legacy = (flags & 2) != 0;
            profile.Pending = (flags & 4) != 0;
            profile.Rated = PeripheralWear.ClampRated(PeripheralWear.ParseLong(p[7]), PeripheralWear.DefaultKeyboardRated);
            profile.Since = PeripheralWear.ParseTime(p[8]);
            profile.Seeded = p[9] == "1";
            profile.LastSeenUtcTicks = PeripheralWear.ParseLong(p[10]);
            return profile;
        }

        internal void DecodeKeys(string text)
        {
            for (int i = 0; i < KeySlots; i++) Interlocked.Exchange(ref Keys[i], 0);
            if (string.IsNullOrEmpty(text) || text == "-") return;
            string[] parts = text.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                int colon = parts[i].IndexOf(':');
                if (colon <= 0) continue;
                int vk = (int)PeripheralWear.ParseLong(parts[i].Substring(0, colon));
                long value = PeripheralWear.ParseLong(parts[i].Substring(colon + 1));
                if (vk >= 0 && vk < KeySlots && value > 0)
                    Interlocked.Exchange(ref Keys[vk], value);
            }
        }
    }

    internal sealed class KeyDeviceWorst
    {
        public string DeviceName;
        public string KeyName;
        public double Ratio;
        public long Count;
    }

    /// <summary>打开健康视图/构造快照时抓取的一次内存快照,OnPaint 只读它。</summary>
    internal sealed class WearSnapshot
    {
        public string KeyboardId = "";
        public string KeyboardName;
        public string KeyboardFriendly;
        public bool KeyboardBuiltIn;
        public bool KeyboardLegacy;
        public bool KeyboardPending;
        public long KeyboardRated;
        public DateTime KeyboardSince;
        public string MouseName;
        public long MouseRatedLeft;
        public long MouseRatedRight;
        public DateTime MouseSince;
        public long LeftClicks, RightClicks, MiddleClicks, XButtons;
        public long TotalKeyPresses, TotalClicks;
        public List<KeyWear> TopKeys;
        public string KeyboardWorstKey;
        public long KeyboardWorstCount;
        public double KeyboardWorstRatio;
        public string KeyboardWorstDevice;
        public List<KeyDeviceWorst> KeyboardDeviceWorsts;

        internal static WearSnapshot Capture()
        {
            KeyboardProfile primary = PeripheralWear.PrimaryProfile;
            return CaptureKeyboard(primary == null ? "" : primary.Id);
        }

        internal static WearSnapshot CaptureKeyboard(string id)
        {
            WearSnapshot snapshot = new WearSnapshot();
            KeyboardProfile profile = PeripheralWear.FindKeyboard(id);
            bool aggregate = profile == null;
            snapshot.KeyboardId = aggregate ? "" : profile.Id;
            snapshot.KeyboardName = aggregate ? "全部键盘" : profile.DisplayName;
            snapshot.KeyboardFriendly = aggregate ? "" : profile.FriendlyName;
            snapshot.KeyboardBuiltIn = !aggregate && profile.BuiltIn;
            snapshot.KeyboardLegacy = !aggregate && profile.Legacy;
            snapshot.KeyboardPending = !aggregate && profile.Pending;
            snapshot.KeyboardRated = aggregate ? 0 : profile.Rated;
            snapshot.KeyboardSince = aggregate ? DateTime.MinValue : profile.Since;
            snapshot.MouseName = PeripheralMouseWear.MouseName ?? "";
            snapshot.MouseRatedLeft = PeripheralMouseWear.MouseRatedLeft;
            snapshot.MouseRatedRight = PeripheralMouseWear.MouseRatedRight;
            snapshot.MouseSince = PeripheralMouseWear.MouseSince;
            snapshot.LeftClicks = PeripheralMouseWear.LeftClicks;
            snapshot.RightClicks = PeripheralMouseWear.RightClicks;
            snapshot.MiddleClicks = PeripheralMouseWear.MiddleClicks;
            snapshot.XButtons = PeripheralMouseWear.XButtons;
            snapshot.TotalKeyPresses = aggregate ? PeripheralWear.TotalKeyPressesAll : profile.TotalKeyPresses;
            snapshot.TotalClicks = PeripheralMouseWear.TotalClicksAll;
            snapshot.TopKeys = PeripheralWear.TopKeys(id, 5);
            if (!aggregate && profile != null)
            {
                if (snapshot.TopKeys.Count > 0)
                {
                    KeyWear first = snapshot.TopKeys[0];
                    snapshot.KeyboardWorstKey = first.Name;
                    snapshot.KeyboardWorstCount = first.Count;
                    snapshot.KeyboardWorstRatio = PeripheralWear.Ratio(first.Count, profile.Rated);
                }
                snapshot.KeyboardWorstDevice = profile.DisplayName;
            }
            else
            {
                List<KeyboardProfile> devices = PeripheralWear.SnapshotKeyboards(false);
                List<KeyDeviceWorst> rows = new List<KeyDeviceWorst>();
                for (int d = 0; d < devices.Count; d++)
                {
                    KeyboardProfile p = devices[d];
                    int bestVk = -1;
                    long bestCount = 0;
                    for (int vk = 0; vk < PeripheralWear.KeySlots; vk++)
                    {
                        long value = Interlocked.Read(ref p.Keys[vk]);
                        if (value > bestCount) { bestCount = value; bestVk = vk; }
                    }
                    if (bestVk < 0 || bestCount <= 0) continue;
                    KeyDeviceWorst row = new KeyDeviceWorst();
                    row.DeviceName = p.DisplayName;
                    row.KeyName = Analysis.KeyName(bestVk);
                    if (string.IsNullOrEmpty(row.KeyName)) row.KeyName = "`";
                    row.Count = bestCount;
                    row.Ratio = PeripheralWear.Ratio(bestCount, p.Rated);
                    rows.Add(row);
                    if (row.Ratio > snapshot.KeyboardWorstRatio)
                    {
                        snapshot.KeyboardWorstRatio = row.Ratio;
                        snapshot.KeyboardWorstKey = row.KeyName;
                        snapshot.KeyboardWorstCount = row.Count;
                        snapshot.KeyboardWorstDevice = p.DisplayName;
                    }
                }
                rows.Sort(delegate(KeyDeviceWorst a, KeyDeviceWorst b) { return b.Ratio.CompareTo(a.Ratio); });
                snapshot.KeyboardDeviceWorsts = rows;
            }
            return snapshot;
        }
    }

    internal static class PeripheralWear
    {
        internal const int KeySlots = 256;
        internal const long DefaultKeyboardRated = 50000000L;
        internal const long DefaultMouseRated = 80000000L;
        internal const long MaxRated = 10000000000L;
        internal const string LegacyId = "legacy";
        internal const string UnknownId = "unknown";
        internal const string AggregateId = "*";

        private static readonly object Gate = new object();
        private static readonly List<KeyboardProfile> Keyboards = new List<KeyboardProfile>();
        private static string _activeKeyboardId = "";

        private static bool _loaded;
        private static bool _needKeySeed = true;

        public static string MouseName
        {
            get { return PeripheralMouseWear.MouseName; }
            set { PeripheralMouseWear.MouseName = value; }
        }
        public static long MouseRatedLeft
        {
            get { return PeripheralMouseWear.MouseRatedLeft; }
            set { PeripheralMouseWear.MouseRatedLeft = value; }
        }
        public static long MouseRatedRight
        {
            get { return PeripheralMouseWear.MouseRatedRight; }
            set { PeripheralMouseWear.MouseRatedRight = value; }
        }
        public static DateTime MouseSince
        {
            get { return PeripheralMouseWear.MouseSince; }
            set { PeripheralMouseWear.MouseSince = value; }
        }

        // ---------------------------------------------------------------- 主档案兼容

        internal static KeyboardProfile PrimaryProfile
        {
            get
            {
                lock (Gate)
                {
                    KeyboardProfile found = FindPrimaryLocked();
                    return found ?? EnsureDefaultLocked();
                }
            }
        }

        /// <summary>只查找,不创建;用于加载/序列化时避免产生空占位档案。</summary>
        private static KeyboardProfile FindPrimaryLocked()
        {
            KeyboardProfile active = FindKeyboardLocked(_activeKeyboardId);
            if (active != null && !active.Pending && !active.Legacy) return active;
            for (int i = 0; i < Keyboards.Count; i++)
                if (!Keyboards[i].Pending && !Keyboards[i].Legacy) return Keyboards[i];
            for (int i = 0; i < Keyboards.Count; i++)
                if (!Keyboards[i].Pending) return Keyboards[i];
            return null;
        }

        /// <summary>新装/仅鼠标数据时的空占位:不显示、不落盘,也不阻止真实设备接管。</summary>
        internal static bool IsPlaceholder(KeyboardProfile p)
        {
            return p != null && !p.Pending && !p.Legacy && p.Id == "primary"
                && string.Equals(p.Alias ?? "", "主力键盘", StringComparison.Ordinal)
                && string.IsNullOrEmpty(p.FriendlyName) && p.TotalKeyPresses == 0;
        }

        public static string KeyboardName
        {
            get { KeyboardProfile p = PrimaryProfile; return p == null ? "" : p.DisplayName; }
            set { KeyboardProfile p = PrimaryProfile; if (p != null) { p.Alias = value ?? ""; p.Pending = false; p.Seeded = true; } }
        }

        public static long KeyboardRated
        {
            get { KeyboardProfile p = PrimaryProfile; return p == null ? DefaultKeyboardRated : p.Rated; }
            set { KeyboardProfile p = PrimaryProfile; if (p != null) p.Rated = ClampRated(value, DefaultKeyboardRated); }
        }

        public static DateTime KeyboardSince
        {
            get { KeyboardProfile p = PrimaryProfile; return p == null ? DateTime.MinValue : p.Since; }
            set { KeyboardProfile p = PrimaryProfile; if (p != null) p.Since = value; }
        }

        public static bool Seeded
        {
            get { KeyboardProfile p = PrimaryProfile; return p != null && p.Seeded; }
            set { KeyboardProfile p = PrimaryProfile; if (p != null) p.Seeded = value; }
        }

        internal static KeyboardProfile FindKeyboard(string id)
        {
            if (string.IsNullOrEmpty(id) || id == AggregateId) return null;
            lock (Gate) return FindKeyboardLocked(id);
        }

        private static KeyboardProfile FindKeyboardLocked(string id)
        {
            for (int i = 0; i < Keyboards.Count; i++)
                if (string.Equals(Keyboards[i].Id, id, StringComparison.Ordinal)) return Keyboards[i];
            return null;
        }

        internal static List<KeyboardProfile> SnapshotKeyboards(bool includePending)
        {
            lock (Gate)
            {
                List<KeyboardProfile> list = new List<KeyboardProfile>();
                for (int i = 0; i < Keyboards.Count; i++)
                {
                    KeyboardProfile p = Keyboards[i];
                    if (!includePending && p.Pending) continue;
                    if (IsPlaceholder(p)) continue;
                    list.Add(p);
                }
                return list;
            }
        }

        internal static string ActiveKeyboardId { get { lock (Gate) return _activeKeyboardId; } }

        internal static void SetActiveProfile(KeyboardProfile profile)
        {
            if (profile == null) return;
            lock (Gate) { _activeKeyboardId = profile.Id; profile.LastSeenUtcTicks = DateTime.UtcNow.Ticks; }
        }

        // ---------------------------------------------------------------- 计数

        /// <summary>兼容旧 API:计入主档案。</summary>
        internal static void CountKey(int vk)
        {
            KeyboardProfile p = PrimaryProfile;
            if (p != null) p.CountKey(vk);
        }

        internal static void CountKey(KeyboardProfile profile, int vk)
        {
            if (profile == null) profile = UnknownProfile;
            if (profile != null) profile.CountKey(vk);
        }

        internal static void CountUnknown(int vk)
        {
            KeyboardProfile p = UnknownProfile;
            if (p != null) p.CountKey(vk);
        }

        internal static void CountClick(int button)
        {
            PeripheralMouseWear.CountClick(button);
        }

        internal static long ReadKey(int vk)
        {
            KeyboardProfile p = PrimaryProfile;
            return p == null ? 0 : p.ReadKey(vk);
        }

        public static long LeftClicks { get { return PeripheralMouseWear.LeftClicks; } }
        public static long RightClicks { get { return PeripheralMouseWear.RightClicks; } }
        public static long MiddleClicks { get { return PeripheralMouseWear.MiddleClicks; } }
        public static long XButtons { get { return PeripheralMouseWear.XButtons; } }
        public static long TotalClicks { get { return PeripheralMouseWear.TotalClicksAll; } }

        public static long TotalKeyPresses
        {
            get { KeyboardProfile p = PrimaryProfile; return p == null ? 0 : p.TotalKeyPresses; }
        }

        internal static long TotalKeyPressesAll
        {
            get
            {
                long total = 0;
                List<KeyboardProfile> list = SnapshotKeyboards(false);
                for (int i = 0; i < list.Count; i++) total += list[i].TotalKeyPresses;
                return total;
            }
        }

        public static bool HasAnyData
        {
            get { return TotalKeyPressesAll > 0 || TotalClicks > 0; }
        }

        // ---------------------------------------------------------------- 生命周期

        internal static void BeginLoad()
        {
            lock (Gate)
            {
                Keyboards.Clear();
                _activeKeyboardId = "";
            }
            PeripheralMouseWear.BeginLoad();
            _needKeySeed = true;
            _loaded = false;
        }

        internal static void LoadRaw(string hardware, string wearKeys, string wearMouse,
            bool hasKeys, bool hasMouse)
        {
            LoadRaw(hardware, wearKeys, wearMouse, hasKeys, hasMouse, null, null, null, null, null);
        }

        internal static void LoadRaw(string hardware, string wearKeys, string wearMouse,
            bool hasKeys, bool hasMouse, IList<string> keyboardHeaders, IList<string> keyboardKeys)
        {
            LoadRaw(hardware, wearKeys, wearMouse, hasKeys, hasMouse, keyboardHeaders, keyboardKeys,
                null, null, null);
        }

        internal static void LoadRaw(string hardware, string wearKeys, string wearMouse,
            bool hasKeys, bool hasMouse, IList<string> keyboardHeaders, IList<string> keyboardKeys,
            IList<string> mouseHeaders, IList<string> mouseButtons, IList<string> mouseDays)
        {
            BeginLoad();
            string[] hp = (hardware ?? "").Split('|');
            bool multi = keyboardHeaders != null && keyboardHeaders.Count > 0;
            bool legacyKeys = !multi && hasKeys && !string.IsNullOrEmpty(wearKeys) && wearKeys != "-";
            if (legacyKeys) EnsureDefault();
            if (!string.IsNullOrEmpty(hardware)) LoadProfile(hardware);
            if (multi)
            {
                LoadProfiles(keyboardHeaders, keyboardKeys);
                _needKeySeed = false;
            }
            else if (legacyKeys)
            {
                KeyboardProfile legacy = PrimaryProfile;
                legacy.Id = LegacyId;
                legacy.Legacy = true;
                legacy.HardwareKey = "";
                legacy.InstanceKey = "";
                if (string.IsNullOrEmpty(legacy.Alias) || legacy.Alias == "主力键盘")
                    legacy.Alias = "旧版单键盘";
                legacy.DecodeKeys(wearKeys);
                _needKeySeed = false;
            }
            else
            {
                // 只有鼠标数据 / 首次保存的空文件:不创建 legacy 空档案。
                // hasKeys 且 "-" 表示用户明确重置过,绝不能再从历史回填("复活"旧计数)。
                _needKeySeed = !hasKeys;
            }

            string mouseName = hp.Length > 3 ? DecodeName(hp[3]) : "";
            long mouseRatedLeft = hp.Length > 4 ? ClampRated(ParseLong(hp[4]), DefaultMouseRated) : DefaultMouseRated;
            long mouseRatedRight = hp.Length > 5 ? ClampRated(ParseLong(hp[5]), DefaultMouseRated) : DefaultMouseRated;
            DateTime mouseSince = hp.Length > 6 ? ParseTime(hp[6]) : DateTime.MinValue;
            PeripheralMouseWear.LoadRaw(mouseName, mouseRatedLeft, mouseRatedRight, mouseSince,
                wearMouse, hasMouse, mouseHeaders, mouseButtons, mouseDays);

            _loaded = true;
        }

        public static void ResetForTest()
        {
            BeginLoad();
            lock (Gate)
            {
                if (Keyboards.Count == 0) EnsureDefaultLocked();
            }
            PeripheralMouseWear.ResetForTest();
        }

        // ---------------------------------------------------------------- 档案

        private static KeyboardProfile EnsureDefaultLocked()
        {
            KeyboardProfile p = new KeyboardProfile();
            p.Id = "primary";
            p.Alias = "主力键盘";
            p.Rated = DefaultKeyboardRated;
            p.Since = DateTime.MinValue;
            Keyboards.Add(p);
            return p;
        }

        internal static KeyboardProfile EnsureDefault()
        {
            lock (Gate)
            {
                if (Keyboards.Count == 0) return EnsureDefaultLocked();
                return PrimaryProfile;
            }
        }

        internal static KeyboardProfile UnknownProfile
        {
            get
            {
                lock (Gate)
                {
                    KeyboardProfile p = FindKeyboardLocked(UnknownId);
                    if (p != null) return p;
                    p = new KeyboardProfile();
                    p.Id = UnknownId;
                    p.Alias = "未知键盘";
                    p.FriendlyName = "RawInput 不可用时的回退档案";
                    p.Seeded = true;
                    p.Pending = false;
                    Keyboards.Add(p);
                    return p;
                }
            }
        }

        /// <summary>RawInput 首次出现:先建 pending 档案,后台解析后再归并。</summary>
        internal static KeyboardProfile CreatePendingProfile(IntPtr handle)
        {
            KeyboardProfile p = new KeyboardProfile();
            p.Id = "pending:" + handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
            p.Alias = "识别中…";
            p.Pending = true;
            p.Seeded = true;
            p.Since = DateTime.Today;
            lock (Gate) Keyboards.Add(p);
            return p;
        }

        internal static KeyboardProfile ResolvePending(KeyboardProfile pending, KeyboardIdentity identity)
        {
            if (pending == null) return null;
            lock (Gate)
            {
                if (!pending.Pending) return pending;
                KeyboardProfile blank = FindKeyboardLocked("primary");
                if (blank != null && blank != pending && IsPlaceholder(blank)) Keyboards.Remove(blank);
                if (identity == null)
                {
                    // 已存在 unknown 档案时并入,避免出现两个同 Id 档案。
                    KeyboardProfile unknown = FindKeyboardLocked(UnknownId);
                    if (unknown != null && unknown != pending)
                    {
                        for (int i = 0; i < KeySlots; i++)
                        {
                            long add = Interlocked.Exchange(ref pending.Keys[i], 0);
                            if (add > 0) Interlocked.Add(ref unknown.Keys[i], add);
                        }
                        Keyboards.Remove(pending);
                        return unknown;
                    }
                    pending.Id = UnknownId;
                    pending.Alias = "未知键盘";
                    pending.FriendlyName = "";
                    pending.Pending = false;
                    _activeKeyboardId = pending.Id;
                    return pending;
                }

                KeyboardProfile target = null;
                for (int i = 0; i < Keyboards.Count; i++)
                {
                    KeyboardProfile p = Keyboards[i];
                    if (p == pending || p.Pending) continue;
                    if (!string.IsNullOrEmpty(identity.InstanceKey)
                        && string.Equals(p.InstanceKey, identity.InstanceKey, StringComparison.OrdinalIgnoreCase))
                    {
                        target = p;
                        break;
                    }
                }
                if (target == null && !string.IsNullOrEmpty(identity.HardwareKey))
                {
                    int matches = 0;
                    KeyboardProfile only = null;
                    for (int i = 0; i < Keyboards.Count; i++)
                    {
                        KeyboardProfile p = Keyboards[i];
                        if (p == pending || p.Pending) continue;
                        if (string.Equals(p.HardwareKey, identity.HardwareKey, StringComparison.OrdinalIgnoreCase))
                        {
                            matches++;
                            only = p;
                        }
                    }
                    if (matches == 1) target = only;
                }

                if (target == null)
                {
                    target = pending;
                    pending.Id = identity.Id;
                    pending.Pending = false;
                    pending.Alias = "";
                }
                else
                {
                    for (int i = 0; i < KeySlots; i++)
                    {
                        long add = Interlocked.Exchange(ref pending.Keys[i], 0);
                        if (add > 0) Interlocked.Add(ref target.Keys[i], add);
                    }
                    Keyboards.Remove(pending);
                }
                target.FriendlyName = identity.FriendlyName ?? "";
                target.DevicePath = identity.DevicePath ?? "";
                target.HardwareKey = identity.HardwareKey ?? "";
                target.InstanceKey = identity.InstanceKey ?? "";
                target.BuiltIn = identity.BuiltIn;
                target.Legacy = false;
                target.Seeded = true;
                target.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
                _activeKeyboardId = target.Id;
                return target;
            }
        }

        internal static bool MergeLegacyInto(string targetId)
        {
            lock (Gate)
            {
                KeyboardProfile legacy = FindKeyboardLocked(LegacyId);
                KeyboardProfile target = FindKeyboardLocked(targetId);
                if (legacy == null || target == null || legacy == target) return false;
                for (int i = 0; i < KeySlots; i++)
                {
                    long add = Interlocked.Exchange(ref legacy.Keys[i], 0);
                    if (add > 0) Interlocked.Add(ref target.Keys[i], add);
                }
                if (target.Since == DateTime.MinValue) target.Since = legacy.Since;
                Keyboards.Remove(legacy);
                return true;
            }
        }

        // ---------------------------------------------------------------- 重置

        public static void ResetKeyboard(DateTime now)
        {
            KeyboardProfile p = PrimaryProfile;
            if (p != null) p.Reset(now);
        }

        public static void ResetKeyboard(string profileId, DateTime now)
        {
            KeyboardProfile p = FindKeyboard(profileId);
            if (p != null) p.Reset(now);
        }

        public static void ResetMouse(DateTime now)
        {
            PeripheralMouseWear.Reset(now);
        }

        public static void ResetMouse(string profileId, DateTime now)
        {
            PeripheralMouseWear.Reset(profileId, now);
        }

        // ---------------------------------------------------------------- TOP5 / 状态

        internal static List<KeyWear> TopKeys(int count)
        {
            KeyboardProfile p = PrimaryProfile;
            return TopKeys(p == null ? "" : p.Id, count);
        }

        internal static List<KeyWear> TopKeys(string profileId, int count)
        {
            long[] counts = new long[KeySlots];
            if (string.IsNullOrEmpty(profileId) || profileId == AggregateId)
            {
                List<KeyboardProfile> list = SnapshotKeyboards(false);
                for (int i = 0; i < list.Count; i++)
                    for (int vk = 0; vk < KeySlots; vk++)
                        counts[vk] += Interlocked.Read(ref list[i].Keys[vk]);
            }
            else
            {
                KeyboardProfile profile = FindKeyboard(profileId);
                if (profile != null)
                    for (int vk = 0; vk < KeySlots; vk++)
                        counts[vk] = Interlocked.Read(ref profile.Keys[vk]);
            }

            Dictionary<string, KeyWear> grouped = new Dictionary<string, KeyWear>(StringComparer.Ordinal);
            for (int vk = 0; vk < KeySlots; vk++)
            {
                long value = counts[vk];
                if (value <= 0) continue;
                string name = Analysis.KeyName(vk);
                if (string.IsNullOrEmpty(name)) name = "`";
                KeyWear row;
                if (!grouped.TryGetValue(name, out row))
                {
                    row = new KeyWear();
                    row.Vk = vk;
                    row.Name = name;
                    grouped[name] = row;
                }
                row.Count += value;
            }
            List<KeyWear> result = new List<KeyWear>(grouped.Values);
            result.Sort(delegate(KeyWear a, KeyWear b)
            {
                int order = b.Count.CompareTo(a.Count);
                return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
            });
            if (count < 0) count = 0;
            if (result.Count > count) result.RemoveRange(count, result.Count - count);
            return result;
        }

        public static double Progress(long count, long rated)
        {
            double ratio = Ratio(count, rated);
            if (ratio < 0) return 0;
            return ratio > 1 ? 1 : ratio;
        }

        public static double Ratio(long count, long rated)
        {
            if (count <= 0) return 0;
            if (rated <= 0) return 1;
            return count / (double)rated;
        }

        internal static double WorstRatio()
        {
            double worst = PeripheralMouseWear.WorstRatio();
            List<KeyboardProfile> list = SnapshotKeyboards(false);
            for (int i = 0; i < list.Count; i++)
            {
                KeyboardProfile p = list[i];
                for (int vk = 0; vk < KeySlots; vk++)
                {
                    long value = Interlocked.Read(ref p.Keys[vk]);
                    if (value <= 0) continue;
                    double ratio = Ratio(value, p.Rated);
                    if (ratio > worst) worst = ratio;
                }
            }
            return worst;
        }

        // ---------------------------------------------------------------- 编解码

        internal static string EncodeProfile()
        {
            KeyboardProfile p;
            lock (Gate) p = FindPrimaryLocked();
            string keyboardName = p == null ? "" : EncodeName(p.DisplayName);
            long rated = p == null ? DefaultKeyboardRated : p.Rated;
            long since = p == null ? 0 : p.Since.Ticks;
            string seeded = p != null && p.Seeded ? "1" : "0";
            return keyboardName + "|"
                + rated.ToString(CultureInfo.InvariantCulture) + "|"
                + since.ToString(CultureInfo.InvariantCulture) + "|"
                + EncodeName(MouseName) + "|"
                + MouseRatedLeft.ToString(CultureInfo.InvariantCulture) + "|"
                + MouseRatedRight.ToString(CultureInfo.InvariantCulture) + "|"
                + TimeText(MouseSince) + "|"
                + seeded;
        }

        internal static string EncodeKeys()
        {
            KeyboardProfile p;
            lock (Gate) p = FindPrimaryLocked();
            if (p == null) return "-";
            StringBuilder sb = new StringBuilder();
            for (int vk = 0; vk < KeySlots; vk++)
            {
                long value = Interlocked.Read(ref p.Keys[vk]);
                if (value <= 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(vk.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        internal static void AppendProfiles(StringBuilder sb)
        {
            List<KeyboardProfile> list = SnapshotKeyboards(false);
            for (int i = 0; i < list.Count; i++)
            {
                sb.AppendLine("keyboard_v1=" + list[i].EncodeHeader());
                sb.AppendLine("keyboard_keys_v1=" + list[i].EncodeKeys());
            }
        }

        internal static void LoadProfile(string text)
        {
            string[] parts = (text ?? "").Split('|');
            if (parts.Length < 8) return;
            KeyboardProfile p;
            lock (Gate) p = FindPrimaryLocked();
            string name = DecodeName(parts[0]);
            if (p != null)
            {
                if (!string.IsNullOrEmpty(name)) p.Alias = name;
                p.Rated = ClampRated(ParseLong(parts[1]), DefaultKeyboardRated);
                p.Since = ParseTime(parts[2]);
                p.Seeded = parts[7] == "1";
            }
        }

        internal static void LoadKeys(string text)
        {
            KeyboardProfile p = PrimaryProfile;
            if (p != null) p.DecodeKeys(text);
        }

        internal static void LoadProfiles(IList<string> headers, IList<string> keyLines)
        {
            lock (Gate) Keyboards.Clear();
            if (headers != null)
            {
                for (int i = 0; i < headers.Count; i++)
                {
                    KeyboardProfile p = KeyboardProfile.DecodeHeader(headers[i]);
                    if (p == null) continue;
                    lock (Gate)
                    {
                        if (FindKeyboardLocked(p.Id) != null) continue;
                        Keyboards.Add(p);
                    }
                }
            }
            if (keyLines != null)
            {
                for (int i = 0; i < keyLines.Count; i++)
                {
                    string line = keyLines[i] ?? "";
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    string id = line.Substring(0, bar);
                    KeyboardProfile p = FindKeyboard(id);
                    if (p != null) p.DecodeKeys(line.Substring(bar + 1));
                }
            }
            lock (Gate)
            {
                if (Keyboards.Count == 0) EnsureDefaultLocked();
                if (string.IsNullOrEmpty(_activeKeyboardId) || FindKeyboardLocked(_activeKeyboardId) == null)
                {
                    for (int i = 0; i < Keyboards.Count; i++)
                        if (!Keyboards[i].Pending) { _activeKeyboardId = Keyboards[i].Id; break; }
                }
            }
        }

        internal static string EncodeMouse()
        {
            return PeripheralMouseWear.EncodeButtons();
        }

        internal static void LoadMouse(string text)
        {
            PeripheralMouseWear.LoadButtons(text);
        }

        internal static void AppendMice(StringBuilder sb)
        {
            PeripheralMouseWear.Append(sb);
        }

        internal static void EnsureSeeded(IEnumerable<DayRecord> history, Counters total)
        {
            if (_loaded && !_needKeySeed && !PeripheralMouseWear.NeedsSeed)
                return;

            long[] keySeed = new long[KeySlots];
            DateTime oldest = DateTime.MinValue;
            if (history != null)
            {
                foreach (DayRecord day in history)
                {
                    if (day == null) continue;
                    if (oldest == DateTime.MinValue || day.Date < oldest) oldest = day.Date;
                    if (day.KeyCounts != null && day.KeyCounts.Count > 0)
                    {
                        foreach (KeyValuePair<int, long> pair in day.KeyCounts)
                            if (pair.Key >= 0 && pair.Key < KeySlots && pair.Value > 0)
                                keySeed[pair.Key] += pair.Value;
                    }
                    else if (day.PhysicalVks != null)
                    {
                        foreach (KeyValuePair<int, long> pair in day.PhysicalVks)
                            if (pair.Key >= 0 && pair.Key < KeySlots && pair.Value > 0)
                                keySeed[pair.Key] += pair.Value;
                    }
                }
            }

            DateTime since = oldest == DateTime.MinValue ? DateTime.Today : oldest;
            KeyboardProfile seedTarget = null;
            if (_needKeySeed)
            {
                lock (Gate)
                {
                    seedTarget = FindKeyboardLocked("primary");
                    if (seedTarget == null && Keyboards.Count == 0) seedTarget = EnsureDefaultLocked();
                }
                if (seedTarget != null)
                {
                    long seeded = 0;
                    for (int i = 0; i < KeySlots; i++)
                    {
                        if (keySeed[i] <= 0) continue;
                        Interlocked.Exchange(ref seedTarget.Keys[i], keySeed[i]);
                        seeded += keySeed[i];
                    }
                    if (seeded > 0)
                    {
                        if (seedTarget.Since == DateTime.MinValue) seedTarget.Since = since;
                        seedTarget.Seeded = true;
                        // 只有真实历史计数才升级为 legacy,空占位继续隐藏。
                        if (seedTarget.Id == "primary")
                        {
                            seedTarget.Id = LegacyId;
                            seedTarget.Legacy = true;
                            if (string.IsNullOrEmpty(seedTarget.Alias) || seedTarget.Alias == "主力键盘")
                                seedTarget.Alias = "旧版单键盘";
                        }
                    }
                }
            }
            if (seedTarget != null && seedTarget.Since == DateTime.MinValue) seedTarget.Since = since;
            PeripheralMouseWear.EnsureSeeded(history, total);
            _needKeySeed = false;
            _loaded = true;
        }

        // ---------------------------------------------------------------- 内部

        internal static long ClampRated(long value, long fallback)
        {
            if (value <= 0) return fallback;
            return value > MaxRated ? MaxRated : value;
        }

        internal static long ParseLong(string text)
        {
            long value;
            long.TryParse((text ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            return value;
        }

        internal static string TimeText(DateTime value)
        {
            return value == DateTime.MinValue ? "0" : value.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        internal static DateTime ParseTime(string text)
        {
            long ticks = ParseLong(text);
            if (ticks <= 0) return DateTime.MinValue;
            try { return new DateTime(ticks); }
            catch { return DateTime.MinValue; }
        }

        internal static string EncodeName(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }
            catch { return ""; }
        }

        internal static string DecodeName(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(text)); }
            catch { return text; }
        }
    }
}
