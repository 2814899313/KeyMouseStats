// ============================================================================
//  键鼠统计 - 按键时长与应用×键位采集  PressDuration.cs
//
//  1.7.3 新增(v11 数据格式):
//    · 按键时长:首次按下到抬起的间隔,用单调时钟测量。
//      系统自动重复不会重置起点;丢失 UP、负值与超长按一律丢弃并计入 HoldDiscarded。
//    · 应用×键位:每次击键把「前台应用 + 键位分组」累加,保存时只写当天击键最多的
//      Top N 个应用与「其他应用汇总」,避免逐键×应用的矩阵撑爆文本格式。
//
//  口径与限制(页面与文档都要说明):
//    · 按住时长不是按压力度,普通键盘没有压力感应。
//    · 丢 UP 是常态(Alt+Tab、Win 键、游戏吞键、锁屏、进程被杀),覆盖率必须如实显示。
//    · 应用归因本身有误差,叠加后误差更大。
//    · 只保存计数,不保存顺序与时间戳,不能还原输入内容。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace KeyMouseStats
{
    /// <summary>单个按键的时长统计。只保存聚合值,不保存原始样本。</summary>
    internal sealed class KeyHold
    {
        public long Count;
        public double TotalMs, MaxMs;

        public void Add(double milliseconds)
        {
            Count++;
            TotalMs += milliseconds;
            if (milliseconds > MaxMs) MaxMs = milliseconds;
        }
        public double Mean { get { return Count > 0 ? TotalMs / Count : double.NaN; } }
    }

    /// <summary>按键时长采集。所有方法只在 UI 线程(钩子回调)调用。</summary>
    internal static class HoldTracker
    {
        /// <summary>直方图档数;最后一档为「≥ 5 秒」。</summary>
        public const int BucketCount = 8;
        /// <summary>各档上界(毫秒),最后一档无上界。</summary>
        private static readonly double[] Edges = { 50, 100, 200, 500, 1000, 2000, 5000 };
        /// <summary>超过此时长视为挂机或丢失 UP,丢弃并计入丢弃数。</summary>
        public const double MaxHoldMs = 60000;
        /// <summary>每小时按键保存的逐键明细上限,超出并入「其他按键」。</summary>
        public const int KeyDetailLimit = 48;
        /// <summary>每天保存的应用数上限,超出并入「其他应用汇总」。</summary>
        public const int AppDetailLimit = 12;
        public const int OtherKey = -1;

        private struct Pending
        {
            public DayRecord Day;
            public long Ticks;
        }
        private static readonly Dictionary<int, Pending> Down = new Dictionary<int, Pending>();

        /// <summary>首次按下(自动重复不会重置起点)。</summary>
        public static void Press(DayRecord day, int vk, long ticks)
        {
            if (day == null || vk <= 0) return;
            if (Down.ContainsKey(vk)) return;
            Pending pending;
            pending.Day = day;
            pending.Ticks = ticks;
            Down[vk] = pending;
        }

        /// <summary>抬起时结算;返回是否计入了有效样本。</summary>
        public static bool Release(int vk, long ticks)
        {
            Pending pending;
            if (!Down.TryGetValue(vk, out pending)) return false;
            Down.Remove(vk);
            return Settle(pending, vk, ticks);
        }

        private static bool Settle(Pending pending, int vk, long ticks)
        {
            DayRecord day = pending.Day;
            if (day == null) return false;
            double milliseconds = (ticks - pending.Ticks) * 1000.0 / Stopwatch.Frequency;
            if (milliseconds <= 0 || milliseconds > MaxHoldMs)
            {
                day.HoldDiscarded++;
                return false;
            }
            day.HoldCount++;
            day.HoldTotalMs += milliseconds;
            if (milliseconds > day.HoldMaxMs) day.HoldMaxMs = milliseconds;
            day.HoldBuckets[BucketIndex(milliseconds)]++;
            KeyHold hold;
            if (!day.Holds.TryGetValue(vk, out hold)) { hold = new KeyHold(); day.Holds[vk] = hold; }
            hold.Add(milliseconds);
            return true;
        }

        /// <summary>把还按着的键全部丢弃(锁屏、休眠、断开、退出前调用)。</summary>
        public static int DiscardPending()
        {
            int count = 0;
            foreach (KeyValuePair<int, Pending> pending in Down)
            {
                if (pending.Value.Day != null) pending.Value.Day.HoldDiscarded++;
                count++;
            }
            Down.Clear();
            return count;
        }

        public static int PendingCount { get { return Down.Count; } }

        public static int BucketIndex(double milliseconds)
        {
            for (int i = 0; i < Edges.Length; i++)
                if (milliseconds < Edges[i]) return i;
            return BucketCount - 1;
        }

        /// <summary>第 index 档的说明文字。</summary>
        public static string BucketLabel(int index)
        {
            if (index == 0) return "< 50 ms";
            if (index >= BucketCount) return "";
            if (index == BucketCount - 1) return "≥ " + FormatSeconds(Edges[Edges.Length - 1]);
            return FormatSeconds(Edges[index - 1]) + " — " + FormatSeconds(Edges[index]);
        }

        /// <summary>坐标轴用的紧凑标签;悬停仍用完整说明。</summary>
        public static string BucketLabelShort(int index)
        {
            if (index == 0) return "<50";
            if (index < 0 || index >= BucketCount) return "";
            if (index == BucketCount - 1) return "≥5s";
            double low = Edges[index - 1], high = Edges[index];
            if (high <= 500) return low.ToString("0", CultureInfo.InvariantCulture) + "-" + high.ToString("0", CultureInfo.InvariantCulture);
            return (low / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "-" + (high / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "s";
        }
        private static string FormatSeconds(double milliseconds)
        {
            return milliseconds < 1000
                ? milliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms"
                : (milliseconds / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " s";
        }
    }

    /// <summary>按键时长与逐键明细的编码。分箱计数与逐键明细必须与总量自洽。</summary>
    internal static class HoldCodec
    {
        /// <summary>hold_v1=b1,b2,...,b8|样本数|总毫秒|最大毫秒|丢弃数</summary>
        public static string Encode(DayRecord day)
        {
            if (day == null || (day.HoldCount == 0 && day.HoldDiscarded == 0)) return null;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < day.HoldBuckets.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(day.HoldBuckets[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('|').Append(day.HoldCount.ToString(CultureInfo.InvariantCulture));
            sb.Append('|').Append(day.HoldTotalMs.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('|').Append(day.HoldMaxMs.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('|').Append(day.HoldDiscarded.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static void Load(DayRecord day, string value)
        {
            if (day == null || string.IsNullOrEmpty(value)) return;
            try
            {
                string[] parts = value.Split('|');
                if (parts.Length < 5) return;
                string[] buckets = parts[0].Split(',');
                for (int i = 0; i < buckets.Length && i < day.HoldBuckets.Length; i++)
                {
                    long count;
                    if (long.TryParse(buckets[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count > 0)
                        day.HoldBuckets[i] = count;
                }
                day.HoldCount = Math.Max(0, ParseLong(parts[1]));
                day.HoldTotalMs = ParseNumber(parts[2], 0);
                day.HoldMaxMs = ParseNumber(parts[3], 0);
                day.HoldDiscarded = Math.Max(0, ParseLong(parts[4]));
            }
            catch (FormatException) { }
        }

        private static long ParseLong(string text)
        {
            long value;
            long.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            return value;
        }

        private static double ParseNumber(string text, double fallback)
        {
            double value;
            if (double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0) return value;
            return fallback;
        }

        /// <summary>返回该日需要写入的行(可能为空数组)。</summary>
        public static string[] EncodeAll(DayRecord day)
        {
            List<string> lines = new List<string>();
            string summary = Encode(day);
            if (summary != null) lines.Add("hold_v1=" + summary);
            string keys = EncodeKeys(day);
            if (keys != null) lines.Add("hold_key_v1=" + keys);
            return lines.ToArray();
        }

        /// <summary>hold_key_v1=vk:次数:总毫秒:最大毫秒,... 按次数从多到少,超出上限并入 vk=-1。</summary>
        public static string EncodeKeys(DayRecord day)
        {
            if (day == null || day.Holds.Count == 0) return null;
            List<KeyValuePair<int, KeyHold>> list = new List<KeyValuePair<int, KeyHold>>(day.Holds);
            list.Sort(delegate(KeyValuePair<int, KeyHold> a, KeyValuePair<int, KeyHold> b)
            {
                int order = b.Value.Count.CompareTo(a.Value.Count);
                return order != 0 ? order : a.Key.CompareTo(b.Key);
            });

            KeyHold other = new KeyHold();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i >= HoldTracker.KeyDetailLimit)
                {
                    other.Count += list[i].Value.Count;
                    other.TotalMs += list[i].Value.TotalMs;
                    if (list[i].Value.MaxMs > other.MaxMs) other.MaxMs = list[i].Value.MaxMs;
                    continue;
                }
                if (sb.Length > 0) sb.Append(',');
                sb.Append(list[i].Key.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(list[i].Value.Count.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(list[i].Value.TotalMs.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(list[i].Value.MaxMs.ToString("R", CultureInfo.InvariantCulture));
            }
            if (other.Count > 0)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(HoldTracker.OtherKey.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(other.Count.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(other.TotalMs.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(other.MaxMs.ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        public static void LoadKeys(DayRecord day, string value)
        {
            if (day == null || string.IsNullOrEmpty(value)) return;
            foreach (string entry in value.Split(','))
            {
                string[] fields = entry.Split(':');
                if (fields.Length != 4) continue;
                int vk;
                long count;
                double total, max;
                if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out vk)) continue;
                if (!long.TryParse(fields[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count <= 0) continue;
                if (!double.TryParse(fields[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out total)) continue;
                if (!double.TryParse(fields[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out max)) continue;
                KeyHold hold;
                if (!day.Holds.TryGetValue(vk, out hold)) { hold = new KeyHold(); day.Holds[vk] = hold; }
                hold.Count += count;
                hold.TotalMs += total;
                if (max > hold.MaxMs) hold.MaxMs = max;
            }
        }
    }

    /// <summary>应用×键位分组的编码。只写 Top N 应用,其余并入「其他应用汇总」。</summary>
    internal static class AppKeyCodec
    {
        public static string[] EncodeAll(DayRecord day)
        {
            if (day == null) return new string[0];
            Dictionary<string, long[]> byPath = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
            long[] total = new long[1];
            foreach (AppUsage app in day.Apps.Values)
            {
                if (!app.HasKeyGroups) continue;
                string path = app.ProcessPath ?? "";
                long[] groups;
                if (!byPath.TryGetValue(path, out groups)) { groups = new long[6]; byPath[path] = groups; }
                for (int i = 0; i < 6 && i < app.KeyGroups.Length; i++) groups[i] += app.KeyGroups[i];
            }
            if (byPath.Count == 0) return new string[0];

            List<KeyValuePair<string, long[]>> list = new List<KeyValuePair<string, long[]>>(byPath);
            list.Sort(delegate(KeyValuePair<string, long[]> a, KeyValuePair<string, long[]> b)
            {
                long left = Sum(a.Value), right = Sum(b.Value);
                int order = right.CompareTo(left);
                return order != 0 ? order : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });

            List<string> lines = new List<string>();
            long[] other = new long[6];
            for (int i = 0; i < list.Count; i++)
            {
                if (i < HoldTracker.AppDetailLimit)
                    lines.Add("app_keys_v1=" + AppActivity.Encode(list[i].Key) + "|" + EncodeGroups(list[i].Value));
                else
                    for (int g = 0; g < 6; g++) other[g] += list[i].Value[g];
            }
            if (Sum(other) > 0)
                lines.Add("app_keys_v1=" + AppActivity.Encode(CrossTelemetry.Other) + "|" + EncodeGroups(other));
            return lines.ToArray();
        }

        public static void Load(DayRecord day, string value)
        {
            if (day == null || string.IsNullOrEmpty(value)) return;
            string[] fields = value.Split('|');
            if (fields.Length != 2) return;
            string path = AppActivity.Decode(fields[0]);
            string[] groups = fields[1].Split(',');
            long[] counts = new long[6];
            for (int i = 0; i < groups.Length && i < 6; i++)
            {
                long count;
                if (long.TryParse(groups[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) && count > 0)
                    counts[i] = count;
            }
            AppUsage target = null;
            foreach (AppUsage app in day.Apps.Values)
            {
                if (string.Equals(app.ProcessPath ?? "", path, StringComparison.OrdinalIgnoreCase)) { target = app; break; }
            }
            if (target == null)
            {
                target = new AppUsage();
                target.ProcessPath = path;
                target.Title = "";
                day.Apps[target.Id] = target;
            }
            target.HasKeyGroups = true;
            for (int i = 0; i < 6; i++) target.KeyGroups[i] += counts[i];
        }

        private static long Sum(long[] values)
        {
            long total = 0;
            foreach (long value in values) total += value;
            return total;
        }

        private static string EncodeGroups(long[] groups)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(groups[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
