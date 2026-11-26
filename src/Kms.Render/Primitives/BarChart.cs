using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

public sealed record BarDatum(
    double Value,
    string Label = "",
    Color? Color = null);

public sealed record BarChartOptions(
    double Padding = 4,
    double GapRatio = 0.24,
    bool IncludeZero = true,
    double? YMin = null,
    double? YMax = null,
    int YTickCount = 4)
{
    public static BarChartOptions Default { get; } = new();
}

public readonly record struct BarGeometry(
    int Index,
    Rect Bounds,
    Rect Slot,
    double Value,
    string Label,
    bool IsNegative,
    Color? Color);

public sealed record BarChartGeometry(
    Rect Bounds,
    Rect Plot,
    IReadOnlyList<BarGeometry> Bars,
    double YMin,
    double YMax,
    double BaselineY,
    IReadOnlyList<double> YTicks)
{
    public bool IsEmpty => Bars.Count == 0;
}

/// <summary>柱状图：正值向上、负值向下，零值仍有完整的命中槽位。</summary>
public static class BarChart
{
    public static BarChartGeometry Measure(
        Rect bounds,
        IReadOnlyList<BarDatum> bars,
        BarChartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bars);

        options ??= BarChartOptions.Default;
        Rect normalized = PrimitiveMath.Normalize(bounds);
        Rect plot = PrimitiveMath.Inset(normalized, options.Padding);

        double[] values = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            values[i] = bars[i].Value;
        }

        (double min, double max) = PrimitiveMath.Domain(
            values,
            options.IncludeZero,
            options.YMin,
            options.YMax);

        double baseline = PrimitiveMath.MapY(0, min, max, plot);
        if (bars.Count == 0 || plot.Width <= 0 || plot.Height <= 0)
        {
            return new BarChartGeometry(
                normalized,
                plot,
                [],
                min,
                max,
                baseline,
                PrimitiveMath.Ticks(min, max, options.YTickCount));
        }

        double gapRatio = Math.Clamp(PrimitiveMath.Finite(options.GapRatio), 0, 0.95);
        double slotWidth = plot.Width / bars.Count;
        double barWidth = Math.Max(0, slotWidth * (1 - gapRatio));
        BarGeometry[] result = new BarGeometry[bars.Count];

        for (int i = 0; i < bars.Count; i++)
        {
            BarDatum datum = bars[i];
            double valueY = PrimitiveMath.MapY(datum.Value, min, max, plot);
            double top = Math.Min(valueY, baseline);
            double height = Math.Abs(baseline - valueY);
            double x = plot.Left + i * slotWidth + (slotWidth - barWidth) / 2;

            result[i] = new BarGeometry(
                i,
                new Rect(x, top, barWidth, height),
                new Rect(plot.Left + i * slotWidth, plot.Top, slotWidth, plot.Height),
                datum.Value,
                datum.Label,
                datum.Value < 0,
                datum.Color);
        }

        return new BarChartGeometry(
            normalized,
            plot,
            result,
            min,
            max,
            baseline,
            PrimitiveMath.Ticks(min, max, options.YTickCount));
    }

    public static void Draw(
        DrawingContext context,
        BarChartGeometry geometry,
        ThemeTokens theme,
        bool showBaseline = true,
        double cornerRadius = 2)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(theme);

        if (geometry.IsEmpty)
        {
            return;
        }

        if (showBaseline &&
            geometry.BaselineY >= geometry.Plot.Top &&
            geometry.BaselineY <= geometry.Plot.Bottom)
        {
            IPen baselinePen = new Pen(new SolidColorBrush(theme.Muted), 1);
            context.DrawLine(
                baselinePen,
                new Point(geometry.Plot.Left, geometry.BaselineY),
                new Point(geometry.Plot.Right, geometry.BaselineY));
        }

        double radius = Math.Max(0, PrimitiveMath.Finite(cornerRadius));

        for (int i = 0; i < geometry.Bars.Count; i++)
        {
            BarGeometry bar = geometry.Bars[i];
            if (bar.Bounds.Width <= 0 || bar.Bounds.Height <= 0)
            {
                continue;
            }

            Color color = bar.Color ?? (bar.IsNegative ? theme.Red : theme.Accent);
            IBrush brush = new SolidColorBrush(color);
            double r = Math.Min(radius, Math.Min(bar.Bounds.Width, bar.Bounds.Height) / 2);

            if (r > 0)
            {
                context.DrawRectangle(brush, null, new RoundedRect(bar.Bounds, r, r), default);
            }
            else
            {
                context.FillRectangle(brush, bar.Bounds, 0);
            }
        }
    }

    /// <summary>柱子的整条槽位都可命中；payload 为 <see cref="BarHit"/>。</summary>
    public static void RegisterHits(HitLayer layer, BarChartGeometry geometry, string idPrefix = "bar")
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(geometry);

        for (int i = 0; i < geometry.Bars.Count; i++)
        {
            BarGeometry bar = geometry.Bars[i];
            layer.Add(
                $"{idPrefix}:{i}",
                bar.Slot,
                new BarHit(bar.Index, bar.Value, bar.Label));
        }
    }
}

public readonly record struct BarHit(int Index, double Value, string Label);
