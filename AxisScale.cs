using System;

namespace KeyMouseStats
{
    /// <summary>
    /// 坐标轴整数刻度。原来的做法是「最大值 ÷ 4」直接当步长,于是出现 3.61k / 7.22k 这种刻度;
    /// 这里先把步长对齐到 1/2/2.5/3/4/5/6/8/10 × 10ⁿ,再取整倍数作为顶刻度。
    /// </summary>
    internal static class AxisScale
    {
        private static readonly double[] Fractions = { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };

        /// <summary>给数据最大值挑一组整齐的刻度。top 一定 ≥ rawMax,并且 top = step × divisions。</summary>
        public static void Nice(double rawMax, int divisions, out double top, out double step)
        {
            Nice(rawMax, divisions, true, out top, out step);
        }

        /// <summary>
        /// integerTicks=true 用于击键/点击/滚轮这类整数指标:步长至少为 1,不出现 0.8 这种刻度。
        /// 鼠标路程用米作单位,允许小数步长。
        /// </summary>
        public static void Nice(double rawMax, int divisions, bool integerTicks, out double top, out double step)
        {
            if (divisions < 1) divisions = 1;
            if (double.IsNaN(rawMax) || double.IsInfinity(rawMax) || rawMax <= 0)
            {
                step = 1;
                top = divisions;
                return;
            }
            // 顶部留 5% 余量,避免折线贴住顶边。
            double rough = rawMax * 1.05 / divisions;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double normalized = rough / magnitude;
            double fraction = 10;
            for (int i = 0; i < Fractions.Length; i++)
            {
                if (normalized <= Fractions[i] + 1e-9)
                {
                    fraction = Fractions[i];
                    break;
                }
            }
            step = fraction * magnitude;
            if (integerTicks && step < 1) step = 1;
            top = step * divisions;
        }

        /// <summary>把刻度值格式化成坐标轴短标签;整千/整百万不显示多余小数。</summary>
        public static string Label(double value)
        {
            return Analysis.FmtAxis(value);
        }
    }
}
