// ============================================================================
//  KeyMouseStats v3.0 重写 · LayoutFlow.cs
//
//  为什么要有这个文件:
//    旧的「响应式」只是把 ElasticH 加到容器矩形高度上,容器内部仍是硬编码坐标
//    (ry = 176 + i * 37 / RectangleF(固定宽, 固定 y)),于是必然出现
//    空壳(容器长高、内容不跟)、叠字(多个元素共用同一 y 区间)、
//    截断(固定宽没测量)。这里提供三个原语,让「内容决定尺寸」成为可能。
//
//  Flow      —— 垂直游标:自上而下排版,谁占多高由内容回答
//  RowLayout —— 水平分配:固定宽 / 权重 / 最小宽,一次算完
//  TextFit   —— 测量与省略:让截断变成显式决策而不是 GDI+ 的默认行为
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Drawing;

namespace KeyMouseStats
{
    /// <summary>垂直排版游标。只做累加,不持有 Graphics。</summary>
    internal sealed class Flow
    {
        private float _x;
        private float _y;
        private float _width;
        private readonly float _originY;

        internal Flow(float x, float y, float width)
        {
            _x = x;
            _y = y;
            _width = width;
            _originY = y;
        }

        internal float X { get { return _x; } }
        internal float Y { get { return _y; } }
        internal float Width { get { return _width; } }
        /// <summary>已消耗的垂直高度。</summary>
        internal float Used { get { return _y - _originY; } }

        /// <summary>取当前行 y 并向下推进 h(含间距)。返回这一次的位置。</summary>
        internal float Next(float h)
        {
            float top = _y;
            _y += h;
            return top;
        }

        /// <summary>推进一个间距,不返回位置。</summary>
        internal void Gap(float gap)
        {
            if (gap > 0f) _y += gap;
        }

        /// <summary>距离给定底边还剩多少。</summary>
        internal float Remaining(float bottom)
        {
            float left = bottom - _y;
            return left > 0f ? left : 0f;
        }

        /// <summary>把游标移到指定 y(用于贴底对齐)。</summary>
        internal void MoveTo(float y)
        {
            _y = y;
        }
    }

    /// <summary>水平分配:固定宽优先,剩余按权重分,并保证不小于最小宽。</summary>
    internal static class RowLayout
    {
        /// <summary>
        /// 计算每列宽度。
        /// fixedWidth[i] &gt; 0 表示该列固定宽度;为 0 表示按 weight[i] 参与剩余分配。
        /// 结果保证每列 &gt;= minWidth[i],且总和 + gap 不超过 total。
        /// </summary>
        internal static float[] Distribute(float total, float gap, float[] fixedWidths, float[] weights, float[] minWidths)
        {
            if (fixedWidths == null || weights == null || minWidths == null) return new float[0];
            int count = fixedWidths.Length;
            if (weights.Length < count || minWidths.Length < count) return new float[0];

            float[] result = new float[count];
            float used = gap * Math.Max(0, count - 1);
            float weightSum = 0f;
            for (int i = 0; i < count; i++)
            {
                if (fixedWidths[i] > 0f)
                {
                    result[i] = fixedWidths[i];
                    used += fixedWidths[i];
                }
                else
                {
                    if (weights[i] > 0f) weightSum += weights[i];   // 权重 0 = 不参与弹性
                }
            }

            float rest = total - used;
            if (rest < 0f) rest = 0f;
            for (int i = 0; i < count; i++)
            {
                if (fixedWidths[i] > 0f) continue;
                float weight = weights[i] > 0f ? weights[i] : 0f;
                result[i] = (weight > 0f && weightSum > 0f) ? rest * weight / weightSum : 0f;
                if (result[i] < minWidths[i]) result[i] = minWidths[i];
            }
            return result;
        }

        /// <summary>把 total 均分给 count 列(扣掉间隙)。</summary>
        internal static float Even(float total, int count, float gap)
        {
            if (count <= 0) return 0f;
            float usable = total - gap * (count - 1);
            return usable > 0f ? usable / count : 0f;
        }
    }

    /// <summary>文本测量与省略。截断从此是显式决策,而不是 GDI+ 的默认裁切。</summary>
    internal static class TextFit
    {
        /// <summary>文本在该宽度下是否放得下。</summary>
        internal static bool Fits(Graphics g, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (g == null || font == null) return false;
            return g.MeasureString(text, font).Width <= width;
        }

        /// <summary>超宽时逐字裁剪并补省略号;放得下则原样返回。</summary>
        internal static string Ellipsis(Graphics g, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (g == null || font == null) return text;
            if (g.MeasureString(text, font).Width <= width) return text;
            const string Tail = "…";
            float tailWidth = g.MeasureString(Tail, font).Width;
            if (tailWidth >= width) return "";
            for (int length = text.Length - 1; length > 0; length--)
            {
                string head = text.Substring(0, length);
                if (g.MeasureString(head, font).Width + tailWidth <= width) return head + Tail;
            }
            return Tail;
        }

        /// <summary>在给定宽度下最多能放下多少个字符(不含省略号)。</summary>
        internal static int VisibleChars(Graphics g, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text) || g == null || font == null) return 0;
            for (int length = text.Length; length > 0; length--)
            {
                if (g.MeasureString(text.Substring(0, length), font).Width <= width) return length;
            }
            return 0;
        }

        /// <summary>单行文本的实测高度(用于让行高由字体决定,而不是写死 37)。</summary>
        internal static float LineHeight(Graphics g, Font font)
        {
            if (g == null || font == null) return 0f;
            return g.MeasureString("国Ag", font).Height;
        }
    }
}
