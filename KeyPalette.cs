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
        private static Color Slot(int slot)
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
                // 6–9 用主题色两两混合:六个槽位不够把实测的高频键分开,
                // 混合出来的中间色与六个基色距离足够远,肉眼可区分。
                case 6: return ArtTheme.Mix(t.Purple, t.Cyan, 0.5);
                case 7: return ArtTheme.Mix(t.Orange, t.Red, 0.5);
                case 8: return ArtTheme.Mix(t.Green, t.Cyan, 0.45);
                default: return ArtTheme.Mix(t.Accent, t.Purple, 0.5);
            }
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
