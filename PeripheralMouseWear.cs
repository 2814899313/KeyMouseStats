// ============================================================================
//  KeyMouseStats v3.4.1  PeripheralMouseWear.cs
//
//  多鼠标磨损档案:
//    · 每只鼠标独立 Id / Alias / FriendlyName / RatedLeft / RatedRight /
//      Since / 左/右/中/侧键计数 / 近 7 天点击环;
//    · RawInput 按 hDevice 路由;低级钩子只在原始输入不可用时回退 unknown;
//    · mouse_v1 / mouse_buttons_v1 / mouse_days_v1 持久化,
//      hardware_v1 / wear_mouse_v1 继续镜像主鼠标,format=13 不变;
//    · 旧版单鼠标保留为 legacy 档案,可一键归入当前鼠标。
//
//  热路径只做 Interlocked 计数与 7 槽日期查找,不做 I/O。
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace KeyMouseStats
{
    internal sealed class MouseProfile
    {
        internal const int DaySlots = 7;

        public string Id = "";
        public string Alias = "";
        public string FriendlyName = "";
        public string DevicePath = "";
        public string HardwareKey = "";
        public string InstanceKey = "";
        public bool BuiltIn;
        public bool Legacy;
        public bool Pending;
        public bool Seeded;
        public long RatedLeft = PeripheralWear.DefaultMouseRated;
        public long RatedRight = PeripheralWear.DefaultMouseRated;
        public DateTime Since = DateTime.MinValue;
        public long LastSeenUtcTicks;

        public long Left;
        public long Right;
        public long Middle;
        public long X;

        public readonly long[] DayStamp = new long[DaySlots];
        public readonly long[] DayLeft = new long[DaySlots];
        public readonly long[] DayRight = new long[DaySlots];
        public readonly long[] DayMiddle = new long[DaySlots];
        public readonly long[] DayX = new long[DaySlots];

        internal void Count(int button, DateTime now)
        {
            if (button == 1) Interlocked.Increment(ref Left);
            else if (button == 2) Interlocked.Increment(ref Right);
            else if (button == 3) Interlocked.Increment(ref Middle);
            else if (button >= 4) Interlocked.Increment(ref X);
            RecordDay(button, now.Date);
        }

        private void RecordDay(int button, DateTime day)
        {
            long stamp = day.Ticks;
            int index = -1;
            for (int i = 0; i < DaySlots; i++)
            {
                if (DayStamp[i] == stamp) { index = i; break; }
            }
            if (index < 0)
            {
                // 日期环:整体左移,最旧一天被挤掉。
                for (int i = 0; i < DaySlots - 1; i++)
                {
                    DayStamp[i] = DayStamp[i + 1];
                    DayLeft[i] = DayLeft[i + 1];
                    DayRight[i] = DayRight[i + 1];
                    DayMiddle[i] = DayMiddle[i + 1];
                    DayX[i] = DayX[i + 1];
                }
                index = DaySlots - 1;
                DayStamp[index] = stamp;
                DayLeft[index] = 0;
                DayRight[index] = 0;
                DayMiddle[index] = 0;
                DayX[index] = 0;
            }
            if (button == 1) DayLeft[index]++;
            else if (button == 2) DayRight[index]++;
            else if (button == 3) DayMiddle[index]++;
            else if (button >= 4) DayX[index]++;
        }

        internal long TotalClicks
        {
            get { return Interlocked.Read(ref Left) + Interlocked.Read(ref Right) + Interlocked.Read(ref Middle) + Interlocked.Read(ref X); }
        }

        internal long Read(int button)
        {
            if (button == 1) return Interlocked.Read(ref Left);
            if (button == 2) return Interlocked.Read(ref Right);
            if (button == 3) return Interlocked.Read(ref Middle);
            return Interlocked.Read(ref X);
        }

        internal void Reset(DateTime now)
        {
            Interlocked.Exchange(ref Left, 0);
            Interlocked.Exchange(ref Right, 0);
            Interlocked.Exchange(ref Middle, 0);
            Interlocked.Exchange(ref X, 0);
            for (int i = 0; i < DaySlots; i++)
            {
                DayStamp[i] = 0;
                DayLeft[i] = 0;
                DayRight[i] = 0;
                DayMiddle[i] = 0;
                DayX[i] = 0;
            }
            Since = now;
        }

        internal void CopyDaysTo(long[] target)
        {
            for (int i = 0; i < DaySlots; i++) target[i] = DayLeft[i] + DayRight[i] + DayMiddle[i] + DayX[i];
        }

        internal double WorstLeft { get { return PeripheralWear.Ratio(Interlocked.Read(ref Left), RatedLeft); } }
        internal double WorstRight { get { return PeripheralWear.Ratio(Interlocked.Read(ref Right), RatedRight); } }

        internal string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Alias)) return Alias;
                if (!string.IsNullOrEmpty(FriendlyName)) return FriendlyName;
                if (BuiltIn) return "笔记本触控板";
                if (Legacy) return "旧版单鼠标";
                return "未命名鼠标";
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
                + RatedLeft.ToString(CultureInfo.InvariantCulture) + "|"
                + RatedRight.ToString(CultureInfo.InvariantCulture) + "|"
                + PeripheralWear.TimeText(Since) + "|"
                + (Seeded ? "1" : "0") + "|"
                + LastSeenUtcTicks.ToString(CultureInfo.InvariantCulture);
        }

        internal string EncodeButtons()
        {
            return Id + "|" + Interlocked.Read(ref Left).ToString(CultureInfo.InvariantCulture) + ","
                + Interlocked.Read(ref Right).ToString(CultureInfo.InvariantCulture) + ","
                + Interlocked.Read(ref Middle).ToString(CultureInfo.InvariantCulture) + ","
                + Interlocked.Read(ref X).ToString(CultureInfo.InvariantCulture);
        }

        internal string EncodeDays()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Id).Append('|');
            bool any = false;
            for (int i = 0; i < DaySlots; i++)
            {
                if (DayStamp[i] <= 0) continue;
                if (any) sb.Append(';');
                sb.Append(DayStamp[i].ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(DayLeft[i].ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(DayRight[i].ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(DayMiddle[i].ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(DayX[i].ToString(CultureInfo.InvariantCulture));
                any = true;
            }
            if (!any) sb.Append('-');
            return sb.ToString();
        }

        internal static MouseProfile DecodeHeader(string text)
        {
            string[] p = (text ?? "").Split('|');
            if (p.Length < 12) return null;
            string id = p[0] ?? "";
            if (id.Length == 0) return null;
            MouseProfile profile = new MouseProfile();
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
            profile.RatedLeft = PeripheralWear.ClampRated(PeripheralWear.ParseLong(p[7]), PeripheralWear.DefaultMouseRated);
            profile.RatedRight = PeripheralWear.ClampRated(PeripheralWear.ParseLong(p[8]), PeripheralWear.DefaultMouseRated);
            profile.Since = PeripheralWear.ParseTime(p[9]);
            profile.Seeded = p[10] == "1";
            profile.LastSeenUtcTicks = PeripheralWear.ParseLong(p[11]);
            return profile;
        }

        internal void DecodeButtons(string text)
        {
            Interlocked.Exchange(ref Left, 0);
            Interlocked.Exchange(ref Right, 0);
            Interlocked.Exchange(ref Middle, 0);
            Interlocked.Exchange(ref X, 0);
            string[] p = (text ?? "").Split(',');
            if (p.Length > 0) Interlocked.Exchange(ref Left, Math.Max(0, PeripheralWear.ParseLong(p[0])));
            if (p.Length > 1) Interlocked.Exchange(ref Right, Math.Max(0, PeripheralWear.ParseLong(p[1])));
            if (p.Length > 2) Interlocked.Exchange(ref Middle, Math.Max(0, PeripheralWear.ParseLong(p[2])));
            if (p.Length > 3) Interlocked.Exchange(ref X, Math.Max(0, PeripheralWear.ParseLong(p[3])));
        }

        /// <summary>旧版 wear_mouse_v1 仍使用竖线分隔。</summary>
        internal void DecodeLegacyButtons(string text)
        {
            Interlocked.Exchange(ref Left, 0);
            Interlocked.Exchange(ref Right, 0);
            Interlocked.Exchange(ref Middle, 0);
            Interlocked.Exchange(ref X, 0);
            string[] p = (text ?? "").Split('|');
            if (p.Length > 0) Interlocked.Exchange(ref Left, Math.Max(0, PeripheralWear.ParseLong(p[0])));
            if (p.Length > 1) Interlocked.Exchange(ref Right, Math.Max(0, PeripheralWear.ParseLong(p[1])));
            if (p.Length > 2) Interlocked.Exchange(ref Middle, Math.Max(0, PeripheralWear.ParseLong(p[2])));
            if (p.Length > 3) Interlocked.Exchange(ref X, Math.Max(0, PeripheralWear.ParseLong(p[3])));
        }

        internal void DecodeDays(string text)
        {
            for (int i = 0; i < DaySlots; i++)
            {
                DayStamp[i] = 0; DayLeft[i] = 0; DayRight[i] = 0; DayMiddle[i] = 0; DayX[i] = 0;
            }
            if (string.IsNullOrEmpty(text) || text == "-") return;
            string[] parts = text.Split(';');
            int valid = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                int colon = parts[i].IndexOf(':');
                if (colon <= 0) continue;
                long stamp = PeripheralWear.ParseLong(parts[i].Substring(0, colon));
                if (stamp <= 0) continue;
                valid++;
            }
            int start = Math.Max(0, DaySlots - valid);
            int slot = start;
            for (int i = 0; i < parts.Length && slot < DaySlots; i++)
            {
                int colon = parts[i].IndexOf(':');
                if (colon <= 0) continue;
                long stamp = PeripheralWear.ParseLong(parts[i].Substring(0, colon));
                if (stamp <= 0) continue;
                string[] values = parts[i].Substring(colon + 1).Split(',');
                if (values.Length < 4) continue;
                DayStamp[slot] = stamp;
                DayLeft[slot] = Math.Max(0, PeripheralWear.ParseLong(values[0]));
                DayRight[slot] = Math.Max(0, PeripheralWear.ParseLong(values[1]));
                DayMiddle[slot] = Math.Max(0, PeripheralWear.ParseLong(values[2]));
                DayX[slot] = Math.Max(0, PeripheralWear.ParseLong(values[3]));
                slot++;
            }
        }
    }

    internal sealed class MouseContribution
    {
        public string Name;
        public long Left;
        public long Right;
        public long Total;
    }

    /// <summary>健康视图的一次内存快照,OnPaint 只读它。</summary>
    internal sealed class MouseWearSnapshot
    {
        public string Id = "";
        public string Name;
        public string Friendly;
        public string Path;
        public bool BuiltIn;
        public bool Legacy;
        public bool Pending;
        public bool Aggregate;
        public long RatedLeft;
        public long RatedRight;
        public long Left;
        public long Middle;
        public long Right;
        public long X;
        public DateTime Since;
        public readonly long[] Days = new long[MouseProfile.DaySlots];
        public double WorstLeft;
        public double WorstRight;
        public double Worst;
        public string WorstLeftSource;
        public string WorstRightSource;
        public List<MouseContribution> Contributions;

        internal static MouseWearSnapshot Capture(string id)
        {
            MouseProfile profile = PeripheralMouseWear.Find(id);
            if (profile == null) return CaptureAggregate();
            MouseWearSnapshot snapshot = new MouseWearSnapshot();
            snapshot.Id = profile.Id;
            snapshot.Name = profile.DisplayName;
            snapshot.Friendly = profile.FriendlyName ?? "";
            snapshot.Path = profile.DevicePath ?? "";
            snapshot.BuiltIn = profile.BuiltIn;
            snapshot.Legacy = profile.Legacy;
            snapshot.Pending = profile.Pending;
            snapshot.RatedLeft = profile.RatedLeft;
            snapshot.RatedRight = profile.RatedRight;
            snapshot.Left = Interlocked.Read(ref profile.Left);
            snapshot.Right = Interlocked.Read(ref profile.Right);
            snapshot.Middle = Interlocked.Read(ref profile.Middle);
            snapshot.X = Interlocked.Read(ref profile.X);
            snapshot.Since = profile.Since;
            snapshot.WorstLeft = profile.WorstLeft;
            snapshot.WorstRight = profile.WorstRight;
            snapshot.Worst = Math.Max(snapshot.WorstLeft, snapshot.WorstRight);
            snapshot.WorstLeftSource = snapshot.Name;
            snapshot.WorstRightSource = snapshot.Name;
            profile.CopyDaysTo(snapshot.Days);
            return snapshot;
        }

        internal static MouseWearSnapshot CaptureAggregate()
        {
            MouseWearSnapshot snapshot = new MouseWearSnapshot();
            snapshot.Id = "";
            snapshot.Name = "全部鼠标";
            snapshot.Aggregate = true;
            List<MouseProfile> list = PeripheralMouseWear.SnapshotMice(false);
            List<MouseContribution> contributions = new List<MouseContribution>();
            long worstLeftCount = 0, worstLeftRated = 0, worstRightCount = 0, worstRightRated = 0;
            for (int i = 0; i < list.Count; i++)
            {
                MouseProfile p = list[i];
                long left = Interlocked.Read(ref p.Left);
                long right = Interlocked.Read(ref p.Right);
                snapshot.Middle += Interlocked.Read(ref p.Middle);
                snapshot.X += Interlocked.Read(ref p.X);
                MouseContribution row = new MouseContribution();
                row.Name = p.DisplayName;
                row.Left = left;
                row.Right = right;
                row.Total = left + right;
                contributions.Add(row);

                double rl = PeripheralWear.Ratio(left, p.RatedLeft);
                double rr = PeripheralWear.Ratio(right, p.RatedRight);
                if (rl > snapshot.WorstLeft)
                {
                    snapshot.WorstLeft = rl;
                    snapshot.WorstLeftSource = p.DisplayName;
                    worstLeftCount = left;
                    worstLeftRated = p.RatedLeft;
                }
                if (rr > snapshot.WorstRight)
                {
                    snapshot.WorstRight = rr;
                    snapshot.WorstRightSource = p.DisplayName;
                    worstRightCount = right;
                    worstRightRated = p.RatedRight;
                }
            }
            snapshot.Left = worstLeftCount;
            snapshot.RatedLeft = worstLeftRated;
            snapshot.Right = worstRightCount;
            snapshot.RatedRight = worstRightRated;
            snapshot.Worst = Math.Max(snapshot.WorstLeft, snapshot.WorstRight);
            contributions.Sort(delegate(MouseContribution a, MouseContribution b) { return b.Total.CompareTo(a.Total); });
            snapshot.Contributions = contributions;
            return snapshot;
        }
    }

    internal static class PeripheralMouseWear
    {
        internal const string PrimaryId = "mprimary";
        internal const string LegacyId = "mlegacy";
        internal const string UnknownId = "munknown";

        private static readonly object Gate = new object();
        private static readonly List<MouseProfile> Mice = new List<MouseProfile>();
        private static string _activeId = "";
        private static bool _loaded;
        private static bool _needSeed = true;

        internal static bool NeedsSeed { get { return !_loaded || _needSeed; } }

        internal static MouseProfile PrimaryMouse
        {
            get
            {
                lock (Gate)
                {
                    MouseProfile found = FindPrimaryLocked();
                    return found ?? EnsureDefaultLocked();
                }
            }
        }

        private static MouseProfile FindPrimaryLocked()
        {
            MouseProfile active = FindLocked(_activeId);
            if (active != null && !active.Pending && !active.Legacy && !IsPlaceholder(active)) return active;
            for (int i = 0; i < Mice.Count; i++)
                if (!Mice[i].Pending && !Mice[i].Legacy && !IsPlaceholder(Mice[i])) return Mice[i];
            for (int i = 0; i < Mice.Count; i++)
                if (!Mice[i].Pending && !IsPlaceholder(Mice[i])) return Mice[i];
            return null;
        }

        private static MouseProfile EnsureDefaultLocked()
        {
            MouseProfile p = new MouseProfile();
            p.Id = PrimaryId;
            p.Alias = "主力鼠标";
            p.RatedLeft = PeripheralWear.DefaultMouseRated;
            p.RatedRight = PeripheralWear.DefaultMouseRated;
            Mice.Add(p);
            return p;
        }

        internal static bool IsPlaceholder(MouseProfile p)
        {
            return p != null && !p.Pending && !p.Legacy && p.Id == PrimaryId
                && string.Equals(p.Alias ?? "", "主力鼠标", StringComparison.Ordinal)
                && string.IsNullOrEmpty(p.FriendlyName) && p.TotalClicks == 0;
        }

        internal static MouseProfile Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (Gate) return FindLocked(id);
        }

        private static MouseProfile FindLocked(string id)
        {
            for (int i = 0; i < Mice.Count; i++)
                if (string.Equals(Mice[i].Id, id, StringComparison.Ordinal)) return Mice[i];
            return null;
        }

        internal static List<MouseProfile> SnapshotMice(bool includePending)
        {
            lock (Gate)
            {
                List<MouseProfile> list = new List<MouseProfile>();
                for (int i = 0; i < Mice.Count; i++)
                {
                    MouseProfile p = Mice[i];
                    if (!includePending && p.Pending) continue;
                    if (IsPlaceholder(p)) continue;
                    list.Add(p);
                }
                return list;
            }
        }

        internal static string MouseName
        {
            get { MouseProfile p = PrimaryMouse; return p == null ? "主力鼠标" : p.DisplayName; }
            set { MouseProfile p = PrimaryMouse; if (p != null) { p.Alias = value ?? ""; p.Pending = false; p.Seeded = true; } }
        }

        internal static long MouseRatedLeft
        {
            get { MouseProfile p = PrimaryMouse; return p == null ? PeripheralWear.DefaultMouseRated : p.RatedLeft; }
            set { MouseProfile p = PrimaryMouse; if (p != null) p.RatedLeft = PeripheralWear.ClampRated(value, PeripheralWear.DefaultMouseRated); }
        }

        internal static long MouseRatedRight
        {
            get { MouseProfile p = PrimaryMouse; return p == null ? PeripheralWear.DefaultMouseRated : p.RatedRight; }
            set { MouseProfile p = PrimaryMouse; if (p != null) p.RatedRight = PeripheralWear.ClampRated(value, PeripheralWear.DefaultMouseRated); }
        }

        internal static DateTime MouseSince
        {
            get { MouseProfile p = PrimaryMouse; return p == null ? DateTime.MinValue : p.Since; }
            set { MouseProfile p = PrimaryMouse; if (p != null) p.Since = value; }
        }

        internal static bool Seeded
        {
            get { MouseProfile p = PrimaryMouse; return p != null && p.Seeded; }
            set { MouseProfile p = PrimaryMouse; if (p != null) p.Seeded = value; }
        }

        internal static long LeftClicks { get { MouseProfile p = PrimaryMouse; return p == null ? 0 : Interlocked.Read(ref p.Left); } }
        internal static long RightClicks { get { MouseProfile p = PrimaryMouse; return p == null ? 0 : Interlocked.Read(ref p.Right); } }
        internal static long MiddleClicks { get { MouseProfile p = PrimaryMouse; return p == null ? 0 : Interlocked.Read(ref p.Middle); } }
        internal static long XButtons { get { MouseProfile p = PrimaryMouse; return p == null ? 0 : Interlocked.Read(ref p.X); } }

        internal static long TotalClicksAll
        {
            get
            {
                long total = 0;
                List<MouseProfile> list = SnapshotMice(false);
                for (int i = 0; i < list.Count; i++) total += list[i].TotalClicks;
                return total;
            }
        }

        internal static MouseProfile CreatePendingProfile(IntPtr handle)
        {
            MouseProfile p = new MouseProfile();
            p.Id = "mouse-pending:" + handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
            p.Alias = "识别中…";
            p.Pending = true;
            p.Seeded = true;
            p.Since = DateTime.Today;
            lock (Gate) Mice.Add(p);
            return p;
        }

        internal static MouseProfile ResolvePending(MouseProfile pending, MouseIdentity identity)
        {
            if (pending == null) return null;
            lock (Gate)
            {
                if (!pending.Pending) return pending;
                MouseProfile blank = FindLocked(PrimaryId);
                if (blank != null && blank != pending && IsPlaceholder(blank)) Mice.Remove(blank);

                if (identity == null)
                {
                    MouseProfile unknown = FindLocked(UnknownId);
                    if (unknown != null && unknown != pending)
                    {
                        AddClicks(pending, unknown);
                        Mice.Remove(pending);
                        return unknown;
                    }
                    pending.Id = UnknownId;
                    pending.Alias = "未知鼠标";
                    pending.FriendlyName = "RawInput 不可用时的回退档案";
                    pending.Pending = false;
                    _activeId = pending.Id;
                    return pending;
                }

                MouseProfile target = null;
                for (int i = 0; i < Mice.Count; i++)
                {
                    MouseProfile p = Mice[i];
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
                    MouseProfile only = null;
                    for (int i = 0; i < Mice.Count; i++)
                    {
                        MouseProfile p = Mice[i];
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
                    MouseProfile sameId = FindLocked(identity.Id);
                    if (sameId != null && sameId != pending)
                    {
                        target = sameId;
                        AddClicks(pending, target);
                        Mice.Remove(pending);
                    }
                    else
                    {
                        target = pending;
                        pending.Id = identity.Id;
                        pending.Pending = false;
                        pending.Alias = "";
                    }
                }
                else
                {
                    AddClicks(pending, target);
                    Mice.Remove(pending);
                }
                target.FriendlyName = identity.FriendlyName ?? "";
                target.DevicePath = identity.DevicePath ?? "";
                target.HardwareKey = identity.HardwareKey ?? "";
                target.InstanceKey = identity.InstanceKey ?? "";
                target.BuiltIn = identity.BuiltIn;
                target.Legacy = false;
                target.Seeded = true;
                target.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
                _activeId = target.Id;
                return target;
            }
        }

        private static void AddClicks(MouseProfile from, MouseProfile to)
        {
            Interlocked.Add(ref to.Left, Interlocked.Exchange(ref from.Left, 0));
            Interlocked.Add(ref to.Right, Interlocked.Exchange(ref from.Right, 0));
            Interlocked.Add(ref to.Middle, Interlocked.Exchange(ref from.Middle, 0));
            Interlocked.Add(ref to.X, Interlocked.Exchange(ref from.X, 0));
            for (int i = 0; i < MouseProfile.DaySlots; i++)
            {
                to.DayLeft[i] += from.DayLeft[i];
                to.DayRight[i] += from.DayRight[i];
                to.DayMiddle[i] += from.DayMiddle[i];
                to.DayX[i] += from.DayX[i];
                if (to.DayStamp[i] == 0) to.DayStamp[i] = from.DayStamp[i];
            }
        }

        internal static MouseProfile UnknownProfile
        {
            get
            {
                lock (Gate)
                {
                    MouseProfile p = FindLocked(UnknownId);
                    if (p != null) return p;
                    p = new MouseProfile();
                    p.Id = UnknownId;
                    p.Alias = "未知鼠标";
                    p.FriendlyName = "RawInput 不可用时的回退档案";
                    p.Seeded = true;
                    Mice.Add(p);
                    return p;
                }
            }
        }

        internal static void CountClick(MouseProfile profile, int button, DateTime now)
        {
            if (profile == null) profile = UnknownProfile;
            if (profile != null) profile.Count(button, now);
        }

        internal static void CountClick(int button)
        {
            MouseProfile p = PrimaryMouse;
            if (p != null) p.Count(button, DateTime.Now);
        }

        internal static void CountUnknown(int button, DateTime now)
        {
            MouseProfile p = UnknownProfile;
            if (p != null) p.Count(button, now);
        }

        internal static void Reset(DateTime now)
        {
            MouseProfile p = PrimaryMouse;
            if (p != null) p.Reset(now);
        }

        internal static void Reset(string id, DateTime now)
        {
            MouseProfile p = Find(id);
            if (p != null) p.Reset(now);
        }

        internal static bool MergeLegacyInto(string targetId)
        {
            lock (Gate)
            {
                MouseProfile legacy = FindLocked(LegacyId);
                MouseProfile target = FindLocked(targetId);
                if (legacy == null || target == null || legacy == target) return false;
                AddClicks(legacy, target);
                if (target.Since == DateTime.MinValue) target.Since = legacy.Since;
                Mice.Remove(legacy);
                return true;
            }
        }

        internal static void BeginLoad()
        {
            lock (Gate)
            {
                Mice.Clear();
                _activeId = "";
            }
            _needSeed = true;
            _loaded = false;
        }

        internal static void ResetForTest()
        {
            BeginLoad();
            lock (Gate) EnsureDefaultLocked();
        }

        internal static void LoadRaw(string mouseName, long ratedLeft, long ratedRight, DateTime since,
            string wearMouse, bool hasMouse, IList<string> headers, IList<string> buttons, IList<string> days)
        {
            BeginLoad();
            bool multi = headers != null && headers.Count > 0;
            bool legacyData = !multi && hasMouse && wearMouse != null
                && (wearMouse != "0|0|0|0" || (!string.IsNullOrEmpty(mouseName) && mouseName != "主力鼠标"));
            if (multi)
            {
                LoadProfiles(headers, buttons, days);
                _needSeed = false;
            }
            else if (legacyData)
            {
                MouseProfile p = EnsureDefaultLocked();
                if (!string.IsNullOrEmpty(mouseName)) p.Alias = mouseName;
                p.RatedLeft = PeripheralWear.ClampRated(ratedLeft, PeripheralWear.DefaultMouseRated);
                p.RatedRight = PeripheralWear.ClampRated(ratedRight, PeripheralWear.DefaultMouseRated);
                p.Since = since;
                p.DecodeLegacyButtons(wearMouse);
                p.Id = LegacyId;
                p.Legacy = true;
                p.Seeded = true;
                if (string.IsNullOrEmpty(p.Alias) || p.Alias == "主力鼠标") p.Alias = "旧版单鼠标";
                _needSeed = false;
            }
            else
            {
                _needSeed = !hasMouse || string.IsNullOrEmpty(wearMouse);
            }
            _loaded = true;
        }

        internal static void LoadProfiles(IList<string> headers, IList<string> buttons, IList<string> days)
        {
            lock (Gate) Mice.Clear();
            if (headers != null)
            {
                for (int i = 0; i < headers.Count; i++)
                {
                    MouseProfile p = MouseProfile.DecodeHeader(headers[i]);
                    if (p == null) continue;
                    lock (Gate)
                    {
                        if (FindLocked(p.Id) != null) continue;
                        Mice.Add(p);
                    }
                }
            }
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    string line = buttons[i] ?? "";
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    MouseProfile p = Find(line.Substring(0, bar));
                    if (p != null) p.DecodeButtons(line.Substring(bar + 1));
                }
            }
            if (days != null)
            {
                for (int i = 0; i < days.Count; i++)
                {
                    string line = days[i] ?? "";
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    MouseProfile p = Find(line.Substring(0, bar));
                    if (p != null) p.DecodeDays(line.Substring(bar + 1));
                }
            }
            lock (Gate)
            {
                if (string.IsNullOrEmpty(_activeId) || FindLocked(_activeId) == null)
                {
                    for (int i = 0; i < Mice.Count; i++)
                        if (!Mice[i].Pending) { _activeId = Mice[i].Id; break; }
                }
            }
        }

        internal static void Append(StringBuilder sb)
        {
            List<MouseProfile> list = SnapshotMice(false);
            for (int i = 0; i < list.Count; i++)
            {
                sb.AppendLine("mouse_v1=" + list[i].EncodeHeader());
                sb.AppendLine("mouse_buttons_v1=" + list[i].EncodeButtons());
                sb.AppendLine("mouse_days_v1=" + list[i].EncodeDays());
            }
        }

        internal static string EncodeButtons()
        {
            MouseProfile p;
            lock (Gate) p = FindPrimaryLocked();
            if (p == null) return "0|0|0|0";
            return Interlocked.Read(ref p.Left).ToString(CultureInfo.InvariantCulture) + "|"
                + Interlocked.Read(ref p.Right).ToString(CultureInfo.InvariantCulture) + "|"
                + Interlocked.Read(ref p.Middle).ToString(CultureInfo.InvariantCulture) + "|"
                + Interlocked.Read(ref p.X).ToString(CultureInfo.InvariantCulture);
        }

        internal static void LoadButtons(string text)
        {
            MouseProfile p = PrimaryMouse;
            if (p != null) p.DecodeLegacyButtons(text);
        }

        internal static void ApplySettings(string name, long ratedLeft, long ratedRight, DateTime since)
        {
            MouseProfile p = PrimaryMouse;
            if (p == null) return;
            if (!string.IsNullOrEmpty(name)) p.Alias = name;
            p.RatedLeft = PeripheralWear.ClampRated(ratedLeft, PeripheralWear.DefaultMouseRated);
            p.RatedRight = PeripheralWear.ClampRated(ratedRight, PeripheralWear.DefaultMouseRated);
            p.Since = since;
        }

        internal static void EnsureSeeded(IEnumerable<DayRecord> history, Counters total)
        {
            if (_loaded && !_needSeed) return;
            long left = 0, right = 0, middle = 0, x = 0;
            DateTime oldest = DateTime.MinValue;
            if (history != null)
            {
                foreach (DayRecord day in history)
                {
                    if (day == null) continue;
                    if (oldest == DateTime.MinValue || day.Date < oldest) oldest = day.Date;
                    left += day.Left; right += day.Right; middle += day.Middle; x += day.XButtons;
                }
            }
            bool totalHasButtons = total != null
                && (total.Left > 0 || total.Right > 0 || total.Middle > 0 || total.XButtons > 0);
            long seedLeft = totalHasButtons && total.Left > left ? total.Left : left;
            long seedRight = totalHasButtons && total.Right > right ? total.Right : right;
            long seedMiddle = totalHasButtons && total.Middle > middle ? total.Middle : middle;
            long seedX = totalHasButtons && total.XButtons > x ? total.XButtons : x;
            if (seedLeft + seedRight + seedMiddle + seedX > 0)
            {
                MouseProfile p = new MouseProfile();
                p.Id = LegacyId;
                p.Alias = "旧版单鼠标";
                p.Legacy = true;
                p.Seeded = true;
                p.Since = oldest == DateTime.MinValue ? DateTime.Today : oldest;
                Interlocked.Exchange(ref p.Left, seedLeft);
                Interlocked.Exchange(ref p.Right, seedRight);
                Interlocked.Exchange(ref p.Middle, seedMiddle);
                Interlocked.Exchange(ref p.X, seedX);
                lock (Gate) Mice.Add(p);
            }
            _needSeed = false;
            _loaded = true;
        }

        internal static double WorstRatio()
        {
            double worst = 0;
            List<MouseProfile> list = SnapshotMice(false);
            for (int i = 0; i < list.Count; i++)
            {
                worst = Math.Max(worst, list[i].WorstLeft);
                worst = Math.Max(worst, list[i].WorstRight);
            }
            return worst;
        }
    }
}
