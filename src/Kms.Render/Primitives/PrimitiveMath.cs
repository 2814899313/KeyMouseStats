using Avalonia;

namespace Kms.Render.Primitives;

/// <summary>不依赖渲染后端的几何/数值小工具。</summary>
internal static class PrimitiveMath
{
    public static double Finite(double value) => double.IsFinite(value) ? value : 0;

    /// <summary>把任意 Rect 收敛成非负宽高，避免负尺寸传播到绘制层。</summary>
    public static Rect Normalize(Rect rect) =>
        new(rect.X, rect.Y, Math.Max(0, rect.Width), Math.Max(0, rect.Height));

    public static Rect Inset(Rect rect, double padding)
    {
        double p = Math.Max(0, Finite(padding));
        return Normalize(new Rect(rect.X + p, rect.Y + p, rect.Width - p * 2, rect.Height - p * 2));
    }

    /// <summary>
    /// 图表纵轴值域。默认包含 0（柱状图基线 / 折线图零轴），
    /// 空数据与常量序列会扩展成非退化区间，避免除零。
    /// </summary>
    public static (double Min, double Max) Domain(
        IReadOnlyList<double> values,
        bool includeZero,
        double? minOverride,
        double? maxOverride)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        bool any = false;

        for (int i = 0; i < values.Count; i++)
        {
            double value = values[i];
            if (!double.IsFinite(value))
            {
                continue;
            }

            any = true;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (!any)
        {
            min = 0;
            max = 1;
        }

        if (includeZero)
        {
            min = Math.Min(min, 0);
            max = Math.Max(max, 0);
        }

        if (minOverride is { } lo && double.IsFinite(lo))
        {
            min = lo;
        }

        if (maxOverride is { } hi && double.IsFinite(hi))
        {
            max = hi;
        }

        if (max - min < 1e-9)
        {
            double pad = Math.Max(1, Math.Abs(max) * 0.1);
            min -= pad;
            max += pad;
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        return (min, max);
    }

    public static double MapY(double value, double min, double max, Rect plot)
    {
        double span = max - min;
        if (Math.Abs(span) < 1e-12)
        {
            return plot.Center.Y;
        }

        double t = (Finite(value) - min) / span;
        return plot.Bottom - t * plot.Height;
    }

    public static IReadOnlyList<double> Ticks(double min, double max, int count)
    {
        int n = Math.Clamp(count, 2, 8);
        double[] ticks = new double[n];
        for (int i = 0; i < n; i++)
        {
            ticks[i] = min + (max - min) * i / (n - 1);
        }

        return ticks;
    }
}
