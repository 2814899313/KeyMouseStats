using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
    /// <summary>
    /// 应用 × 输入效率(APM)与使用时长占比的纯计算。页面与测试共用:
    /// 口径只在这里定义一次,避免「同一个 APM 两个地方两种算法」。
    /// </summary>
    internal static class AppFlow
    {
        /// <summary>参与 APM 排名的最短前台活跃时长(秒)。样本太短会把 APM 放大到没有意义。</summary>
        public const double MinApmSeconds = 60;

        internal sealed class Item
        {
            public string Name, Detail, ProcessPath;
            public long Keys, Clicks, Wheel;
            public double Seconds;
            public int Category;
            public bool Legacy;
            /// <summary>APM 口径:每分钟的击键 + 点击,和应用无关的输入不参与。</summary>
            public double Inputs { get { return Keys + Clicks; } }
            public double Apm { get { return Seconds >= 1 ? Inputs * 60.0 / Seconds : 0; } }
        }

        internal sealed class Totals
        {
            public long Inputs;
            public double Seconds;
            public double Apm;
            /// <summary>有前台活跃观测的应用数。</summary>
            public int Apps;
        }

        internal sealed class Slice
        {
            public string Name;
            public double Seconds;
            public long Inputs;
            public double Apm;
            public double Share;
            public bool Other;
            public int Category = -1;
        }

        /// <summary>把应用统计行转成可计算条目;Legacy 行(启用应用统计前)保留但标记。</summary>
        public static List<Item> Build(IEnumerable<AppActivity.Row> rows)
        {
            List<Item> items = new List<Item>();
            if (rows == null) return items;
            foreach (AppActivity.Row row in rows)
            {
                if (row == null) continue;
                items.Add(new Item
                {
                    Name = row.Name ?? "",
                    Detail = row.Detail ?? "",
                    ProcessPath = row.ProcessPath ?? "",
                    Keys = row.Keys,
                    Clicks = row.Clicks,
                    Wheel = row.Wheel,
                    Seconds = row.ActiveSeconds,
                    Category = row.Category,
                    Legacy = row.Legacy
                });
            }
            return items;
        }

        /// <summary>全局 APM = 全部有观测应用的(击键+点击) ÷ 活跃时长 × 60。旧数据没有时长,不参与。</summary>
        public static Totals Sum(IEnumerable<Item> items)
        {
            Totals totals = new Totals();
            if (items == null) return totals;
            foreach (Item item in items)
            {
                if (item == null || item.Legacy || item.Seconds < 1) continue;
                totals.Inputs += (long)item.Inputs;
                totals.Seconds += item.Seconds;
                totals.Apps++;
            }
            totals.Apm = totals.Seconds >= 1 ? totals.Inputs * 60.0 / totals.Seconds : 0;
            return totals;
        }

        /// <summary>
        /// 按 APM 从高到低排名。活跃时长不足 minSeconds 的条目被排除并计入 excluded;
        /// APM 相同的按输入量、再按时长、最后按名字排序,保证页面顺序稳定。
        /// </summary>
        public static List<Item> RankByApm(IEnumerable<Item> items, double minSeconds, out int excluded)
        {
            List<Item> ranked = new List<Item>();
            excluded = 0;
            if (items == null) return ranked;
            foreach (Item item in items)
            {
                if (item == null || item.Legacy) continue;
                if (item.Seconds < minSeconds) { excluded++; continue; }
                ranked.Add(item);
            }
            ranked.Sort(delegate(Item a, Item b)
            {
                int order = b.Apm.CompareTo(a.Apm);
                if (order == 0) order = b.Inputs.CompareTo(a.Inputs);
                if (order == 0) order = b.Seconds.CompareTo(a.Seconds);
                if (order == 0) order = string.CompareOrdinal(a.Name, b.Name);
                return order;
            });
            return ranked;
        }

        /// <summary>
        /// 使用时长占比:前 top 个应用各自一片,其余合并成「其他 N 个」。
        /// 返回的 Share 是相对全部有观测时长的比例,四舍五入到绘制精度由调用方决定。
        /// </summary>
        public static List<Slice> Slices(IEnumerable<Item> items, int top, out double totalSeconds, out int merged)
        {
            List<Slice> slices = new List<Slice>();
            totalSeconds = 0;
            merged = 0;
            List<Item> list = new List<Item>();
            if (items != null)
            {
                foreach (Item item in items)
                {
                    if (item == null || item.Legacy || item.Seconds < 1) continue;
                    list.Add(item);
                    totalSeconds += item.Seconds;
                }
            }
            if (list.Count == 0) return slices;
            list.Sort(delegate(Item a, Item b)
            {
                int order = b.Seconds.CompareTo(a.Seconds);
                if (order == 0) order = b.Inputs.CompareTo(a.Inputs);
                if (order == 0) order = string.CompareOrdinal(a.Name, b.Name);
                return order;
            });
            int keep = Math.Max(1, Math.Min(top, list.Count));
            for (int i = 0; i < keep; i++)
            {
                Slice slice = new Slice
                {
                    Name = list[i].Name,
                    Seconds = list[i].Seconds,
                    Inputs = (long)list[i].Inputs,
                    Apm = list[i].Apm,
                    Category = list[i].Category,
                    Share = totalSeconds > 0 ? list[i].Seconds / totalSeconds : 0
                };
                slices.Add(slice);
            }
            if (list.Count > keep)
            {
                Slice other = new Slice { Name = "其他 " + (list.Count - keep) + " 个应用", Other = true, Apm = 0 };
                for (int i = keep; i < list.Count; i++)
                {
                    other.Seconds += list[i].Seconds;
                    other.Inputs += (long)list[i].Inputs;
                }
                other.Share = totalSeconds > 0 ? other.Seconds / totalSeconds : 0;
                other.Apm = other.Seconds >= 1 ? other.Inputs * 60.0 / other.Seconds : 0;
                slices.Add(other);
                merged = list.Count - keep;
            }
            return slices;
        }

        /// <summary>页面显示用的 APM 文本:整数为主,小于 10 保留一位小数。</summary>
        public static string ApmText(double apm)
        {
            if (apm <= 0) return "--";
            return apm < 10
                ? apm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                : apm.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>单个应用统计行的 APM;没有前台时长时为 0。</summary>
        public static double ApmOf(AppActivity.Row row)
        {
            if (row == null || row.ActiveSeconds < 1) return 0;
            return (row.Keys + row.Clicks) * 60.0 / row.ActiveSeconds;
        }
    }
}
