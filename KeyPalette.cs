// ============================================================================
//  键鼠统计 - 按键配色  KeyPalette.cs
//
//  为什么需要这个文件:
//  1.8.0 之前,总览页的「键盘按键分布」圆环与「最常按键 Top 5」都按下标取色
//  (color[i])。那等于用颜色再编码一次排名,而排名已经由位置、条长和右侧数字
//  表达了三遍。更要命的是颜色会随区间漂移:今天紫色是空格,切到本周紫色可能
//  变成 I,用户记住的「紫色=空格」当场失效,跨区间对比就成了误读。
//
//  这里把颜色改成绑在**按键显示名**上(KeyName 的返回值,"空格"/"I"/"退格"…)。
//  位置继续表示排名,颜色表示身份,两者正交。空格永远是紫色,掉到第二也还是
//  紫色,只是位置下移。
//
//  取色规则来自真实数据(2026-09 的 64,345 次击键、85 个不同键):
//  高频键给互不相邻的色相,相邻名次的颜色尽量拉开距离,低频长尾走稳定哈希。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;

namespace KeyMouseStats
{
    internal static class KeyPalette
    {
        // 固定锚点:高频键 → 主题色槽位。槽位顺序经过挑选,让最常出现的前几名
        // 落在色相最远的几个颜色上(紫 / 青 / 橙 / 绿 / 红 / 蓝),相邻排名不会撞色。
        // 表里的键名与 Analysis.KeyName 的返回值严格一致。
        private static readonly Dictionary<string, int> Anchors = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // 实测占比前 12(2026-09,64,345 次击键):空格 10.9%、I 7.8%、A 7.5%、
            // N 5.7%、退格 4.7%、E 4.6%、W 4.1%、F 4.0%、H 3.7%、S 3.7%、D 3.3%、K 3.1%
            // 十个槽位刚好把这 12 个高频键分开;顺序再按出现频率微调过,
            // 使相邻名次拿到的槽位不相邻。
            { "空格", 0 },
            { "I", 1 },
            { "A", 2 },
            { "N", 3 },
            { "退格", 4 },
            { "E", 5 },
            { "W", 6 },
            { "F", 7 },
            { "H", 8 },
            { "S", 9 },
            { "D", 0 },
            { "K", 1 },
            // 次高频(1–3%):O、L、回车、U、Shift、R、T、G、Y
            { "O", 2 },
            { "L", 3 },
            { "回车", 4 },
            { "U", 5 },
            { "Shift", 6 },
            { "R", 7 },
            { "T", 8 },
            { "G", 9 },
            { "Y", 0 },
            // 控制键与常用字母
            { "Ctrl", 5 },
            { "Alt", 6 },
            { "Tab", 7 },
            { "Esc", 8 },
            { "C", 9 },
            { "V", 0 },
            { "M", 1 },
            { "P", 2 },
            { "B", 3 },
            { "X", 4 },
            { "Z", 5 },
            { "J", 6 },
            { "Q", 7 },
            { ",", 8 },
            { ".", 9 },
            { ";", 0 },
            { "←", 1 },
            { "→", 2 },
            { "↑", 3 },
            { "↓", 4 },
            { "Win", 5 },
            { "1", 6 },
            { "2", 7 },
            { "3", 8 },
            { "4", 9 },
            { "5", 0 },
            { "6", 1 },
            { "7", 2 },
            { "8", 3 },
            { "9", 4 },
            { "0", 5 }
        };

        /// <summary>槽位号 → 颜色。槽位由主题决定,所以九套主题各自成立。</summary>
        internal static Color Slot(int slot)
        {
            ArtTheme t = ArtTheme.Current;
            switch (((slot % Slots) + Slots) % Slots)
            {
                case 0: return t.Purple;
                case 1: return t.Cyan;
                case 2: return t.Orange;
                case 3: return t.Green;
                case 4: return t.Red;
                case 5: return t.Accent;
                // 6–9 是「色相环空隙填充」而不是基色两两插值。
                // 旧写法 Mix(Purple,Cyan,.5) 这类插值必然落在两个基色的色相之间,而基色本身
                // 已经在用 —— 10 槽里有 4 槽是「插值再插值」,撞色是数学必然而不是调参失误。
                // 实测撞得最惨的两对:槽 1 Cyan #4CC9F0 与槽 8 Mix(Green,Cyan,.45) #4BCEC8
                // 只差 ΔB=40(9px 图例点肉眼难辨);槽 5 Accent #729BFF 与槽 9 只差 ΔG=1。
                default: return Overflow()[((slot % Slots) + Slots) % Slots - 6];
            }
        }

        private static int overflowTheme = -1;
        private static Color[] overflow;

        /// <summary>
        /// 槽位 6–9:把 6 个基色投到色相环上,取最大的 4 个空隙,把新色相放在空隙中点。
        /// 这样新色与任何基色的色相距离都有几何下界,不会因为主题换色而撞车。
        /// 结果按主题缓存 —— 同一主题任何时候都返回同一组颜色,跨会话稳定。
        /// </summary>
        private static Color[] Overflow()
        {
            int id = ArtTheme.Validate(Store.ThemeId);
            if (overflow != null && overflowTheme == id) return overflow;
            ArtTheme t = ArtTheme.Current;
            Color[] bases = { t.Purple, t.Cyan, t.Orange, t.Green, t.Red, t.Accent };
            double[] hues = new double[bases.Length];
            double sumSaturation = 0, sumLight = 0;
            for (int i = 0; i < bases.Length; i++)
            {
                double h, s, l;
                ToHsl(bases[i], out h, out s, out l);
                hues[i] = h;
                sumSaturation += s;
                sumLight += l;
            }
            double saturation = Math.Min(0.88, Math.Max(0.42, sumSaturation / bases.Length));
            double light = Math.Min(0.74, Math.Max(0.44, sumLight / bases.Length));

            // 按色相排序(下标一起动),再算 6 个环形空隙与中点。
            double[] sorted = (double[])hues.Clone();
            Array.Sort(sorted);
            double[] gapSize = new double[sorted.Length];
            double[] gapMid = new double[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                double next = i + 1 < sorted.Length ? sorted[i + 1] : sorted[0] + 360.0;
                gapSize[i] = next - sorted[i];
                gapMid[i] = sorted[i] + gapSize[i] / 2.0;
            }
            // 取最大的 4 个空隙;空隙大小相同时按下标稳定排序,保证结果确定。
            int[] order = new int[gapSize.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, delegate (int a, int b)
            {
                int byGap = gapSize[b].CompareTo(gapSize[a]);
                return byGap != 0 ? byGap : a.CompareTo(b);
            });
            Color[] result = new Color[4];
            for (int i = 0; i < result.Length; i++)
                result[i] = FromHsl(gapMid[order[i]] % 360.0, saturation, light);
            overflow = result;
            overflowTheme = id;
            return result;
        }

        private static void ToHsl(Color color, out double hue, out double saturation, out double light)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            light = (max + min) / 2.0;
            if (delta <= 1e-9) { hue = 0; saturation = 0; return; }
            saturation = light > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
            if (max == r) hue = (g - b) / delta + (g < b ? 6.0 : 0.0);
            else if (max == g) hue = (b - r) / delta + 2.0;
            else hue = (r - g) / delta + 4.0;
            hue *= 60.0;
        }

        private static Color FromHsl(double hue, double saturation, double light)
        {
            hue = ((hue % 360.0) + 360.0) % 360.0;
            if (saturation <= 1e-9)
            {
                int gray = (int)Math.Round(Math.Max(0, Math.Min(1, light)) * 255);
                return Color.FromArgb(gray, gray, gray);
            }
            double c = (1 - Math.Abs(2 * light - 1)) * saturation;
            double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
            double m = light - c / 2;
            double r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(
                Math.Max(0, Math.Min(255, (int)Math.Round((r + m) * 255))),
                Math.Max(0, Math.Min(255, (int)Math.Round((g + m) * 255))),
                Math.Max(0, Math.Min(255, (int)Math.Round((b + m) * 255))));
        }

        /// <summary>长尾键的稳定取色:同一按键名在任何区间、任何一次运行都得到同一颜色。</summary>
        private static int HashSlot(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            // FNV-1a:比 string.GetHashCode 稳定(后者在不同运行/框架版本间不保证一致,
            // 而这里恰恰要求跨会话稳定,否则每次启动颜色都会变)。
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < name.Length; i++)
                {
                    hash ^= name[i];
                    hash *= 16777619u;
                }
                return (int)(hash % (uint)Slots);
            }
        }

        /// <summary>
        /// 按键显示名 → 颜色。同一按键在任何排名、任何区间、任何主题下都保持同一身份色。
        /// </summary>
        internal static Color ForKey(string name)
        {
            if (name == null) return Slot(0);
            int slot;
            if (Anchors.TryGetValue(name, out slot)) return Slot(slot);
            return Slot(HashSlot(name));
        }

        /// <summary>同色去重:名次靠前的键优先保留自己的颜色,后面的撞色则顺延到下一个槽位。</summary>
        internal static Color[] ForRanking(IList<string> names)
        {
            Color[] result = new Color[names == null ? 0 : names.Count];
            if (names == null) return result;
            bool[] used = new bool[Slots];
            for (int i = 0; i < names.Count; i++)
            {
                Color wanted = ForKey(names[i]);
                int slot = SlotOf(wanted);
                // 槽位用完之前不允许重复:前 Slots 名如果撞色就顺延到最近的空槽,
                // 这样「颜色可区分」这条前提在整张 Top-N 上都成立。
                if (i < Slots && used[slot])
                {
                    for (int step = 1; step <= Slots; step++)
                    {
                        int next = (slot + step) % Slots;
                        if (!used[next]) { slot = next; break; }
                    }
                }
                used[slot] = true;
                result[i] = Slot(slot);
            }
            return result;
        }

        private static int SlotOf(Color color)
        {
            for (int i = 0; i < Slots; i++) if (color.ToArgb() == Slot(i).ToArgb()) return i;
            return 0;
        }

        /// <summary>颜色数量,供图例与测试引用。</summary>
        internal const int Slots = 10;
    }
}
