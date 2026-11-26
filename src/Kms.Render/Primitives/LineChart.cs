using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

/// <summary>一条折线序列。PendingTail 表示最后一个点尚未结束（今天未过完），最后一段画虚线。</summary>
public sealed record LineSeries(
    string Name,
    IReadOnlyList<double> Values,
    Color Color,
    bool PendingTail = false);

public sealed record LineChartOptions(
    double Padding = 4,
    bool IncludeZero = true,
    double? YMin = null,
    double? YMax = null,
    int YTickCount = 4)
{
    public static LineChartOptions Default { get; } = new();
}

public readonly record struct LineChartSegment(Point Start, Point End, bool Dashed);

public sealed record LineSeriesGeometry(
    LineSeries Series,
    IReadOnlyList<Point> Points,
    IReadOnlyList<LineChartSegment> Segments);

public sealed record LineChartGeometry(
    Rect Bounds,
    Rect Plot,
    IReadOnlyList<LineSeriesGeometry> Series,
    double YMin,
    double YMax,
    double BaselineY,
    IReadOnlyList<double> YTicks)
{
    public bool IsEmpty
    {
        get
        {
            if (Series.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < Series.Count; i++)
            {
                if (Series[i].Points.Count > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// 折线图：空数据、单点、负值、零轴与同期裁剪（最后一段虚线）都由纯几何处理。
/// </summary>
public static class LineChart
{
    public static LineChartGeometry Measure(
        Rect bounds,
        IReadOnlyList<LineSeries> series,
        LineChartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(series);

        options ??= LineChartOptions.Default;
        Rect normalized = PrimitiveMath.Normalize(bounds);
        Rect plot = PrimitiveMath.Inset(normalized, options.Padding);

        List<double> values = [];
        for (int i = 0; i < series.Count; i++)
        {
            IReadOnlyList<double> points = series[i].Values;
            for (int j = 0; j < points.Count; j++)
            {
                values.Add(points[j]);
            }
        }

        (double min, double max) = PrimitiveMath.Domain(
            values,
            options.IncludeZero,
            options.YMin,
            options.YMax);

        double baseline = PrimitiveMath.MapY(0, min, max, plot);
        List<LineSeriesGeometry> geometries = new(series.Count);

        for (int i = 0; i < series.Count; i++)
        {
            LineSeries line = series[i];
            IReadOnlyList<double> points = line.Values;
            if (points.Count == 0)
            {
                geometries.Add(new LineSeriesGeometry(line, [], []));
                continue;
            }

            Point[] mapped = new Point[points.Count];
            double width = plot.Width;
            for (int j = 0; j < points.Count; j++)
            {
                double x = points.Count == 1
                    ? plot.Center.X
                    : plot.Left + width * j / (points.Count - 1);
                mapped[j] = new Point(x, PrimitiveMath.MapY(points[j], min, max, plot));
            }

            LineChartSegment[] segments = new LineChartSegment[Math.Max(0, points.Count - 1)];
            for (int j = 0; j < segments.Length; j++)
            {
                bool dashed = line.PendingTail && j == segments.Length - 1;
                segments[j] = new LineChartSegment(mapped[j], mapped[j + 1], dashed);
            }

            geometries.Add(new LineSeriesGeometry(line, mapped, segments));
        }

        return new LineChartGeometry(
            normalized,
            plot,
            geometries,
            min,
            max,
            baseline,
            PrimitiveMath.Ticks(min, max, options.YTickCount));
    }

    public static void Draw(
        DrawingContext context,
        LineChartGeometry geometry,
        ThemeTokens theme,
        double thickness = 1.6,
        bool showPoints = true,
        bool showBaseline = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(theme);

        if (geometry.IsEmpty)
        {
            return;
        }

        using (context.PushClip(geometry.Plot))
        {
            if (showBaseline &&
                geometry.BaselineY >= geometry.Plot.Top &&
                geometry.BaselineY <= geometry.Plot.Bottom)
            {
                IPen baselinePen = new Pen(new SolidColorBrush(theme.Line), 1);
                context.DrawLine(
                    baselinePen,
                    new Point(geometry.Plot.Left, geometry.BaselineY),
                    new Point(geometry.Plot.Right, geometry.BaselineY));
            }

            double stroke = Math.Max(0.5, PrimitiveMath.Finite(thickness));

            for (int i = 0; i < geometry.Series.Count; i++)
            {
                LineSeriesGeometry line = geometry.Series[i];
                if (line.Points.Count == 0)
                {
                    continue;
                }

                SolidColorBrush brush = new(line.Series.Color);
                IPen pen = new Pen(brush, stroke);
                IPen dashedPen = new Pen(brush, stroke)
                {
                    DashStyle = new DashStyle([3, 3], 0)
                };

                for (int j = 0; j < line.Segments.Count; j++)
                {
                    LineChartSegment segment = line.Segments[j];
                    context.DrawLine(segment.Dashed ? dashedPen : pen, segment.Start, segment.End);
                }

                if (showPoints && line.Points.Count <= 64)
                {
                    double radius = line.Points.Count == 1 ? 3 : 2;
                    for (int j = 0; j < line.Points.Count; j++)
                    {
                        context.DrawEllipse(brush, null, line.Points[j], radius, radius);
                    }
                }
            }
        }
    }

    /// <summary>把每个折线点写成命中区；payload 为 <see cref="LinePointHit"/>。</summary>
    public static void RegisterHits(
        HitLayer layer,
        LineChartGeometry geometry,
        double radius = 6,
        string idPrefix = "line")
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(geometry);

        double r = Math.Max(1, PrimitiveMath.Finite(radius));

        for (int i = 0; i < geometry.Series.Count; i++)
        {
            LineSeriesGeometry line = geometry.Series[i];
            for (int j = 0; j < line.Points.Count; j++)
            {
                Point point = line.Points[j];
                layer.Add(
                    $"{idPrefix}:{i}:{j}",
                    new Rect(point.X - r, point.Y - r, r * 2, r * 2),
                    new LinePointHit(i, j, line.Series.Values[j]));
            }
        }
    }
}

public readonly record struct LinePointHit(int SeriesIndex, int PointIndex, double Value);
