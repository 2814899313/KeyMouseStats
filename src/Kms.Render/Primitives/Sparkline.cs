using Avalonia;
using Avalonia.Media;

namespace Kms.Render.Primitives;

public sealed record SparklineGeometry(
    Rect Bounds,
    IReadOnlyList<Point> Points,
    double Min,
    double Max,
    double BaselineY,
    bool IsFlat);

/// <summary>迷你趋势线（HUD 5 分钟条）。空数据与单点都返回可安全绘制的几何。</summary>
public static class Sparkline
{
    public static SparklineGeometry Measure(
        Rect bounds,
        IReadOnlyList<double> values,
        double padding = 1,
        bool includeZero = false)
    {
        ArgumentNullException.ThrowIfNull(values);

        Rect normalized = PrimitiveMath.Normalize(bounds);
        Rect plot = PrimitiveMath.Inset(normalized, padding);
        (double min, double max) = PrimitiveMath.Domain(values, includeZero, null, null);

        Point[] points = new Point[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            double x = values.Count == 1
                ? plot.Center.X
                : plot.Left + plot.Width * i / (values.Count - 1);
            points[i] = new Point(x, PrimitiveMath.MapY(values[i], min, max, plot));
        }

        bool flat = true;
        for (int i = 1; i < values.Count; i++)
        {
            if (Math.Abs(PrimitiveMath.Finite(values[i]) - PrimitiveMath.Finite(values[0])) > 1e-9)
            {
                flat = false;
                break;
            }
        }

        return new SparklineGeometry(
            normalized,
            points,
            min,
            max,
            PrimitiveMath.MapY(0, min, max, plot),
            flat);
    }

    public static void Draw(
        DrawingContext context,
        SparklineGeometry geometry,
        Color color,
        double thickness = 1.4)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.Points.Count == 0)
        {
            return;
        }

        IBrush brush = new SolidColorBrush(color);
        double stroke = Math.Max(0.5, PrimitiveMath.Finite(thickness));

        if (geometry.Points.Count == 1)
        {
            context.DrawEllipse(brush, null, geometry.Points[0], stroke, stroke);
            return;
        }

        StreamGeometry path = new();
        using (StreamGeometryContext pathContext = path.Open())
        {
            pathContext.BeginFigure(geometry.Points[0], false);
            for (int i = 1; i < geometry.Points.Count; i++)
            {
                pathContext.LineTo(geometry.Points[i], true);
            }

            pathContext.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(brush, stroke), path);
    }
}
