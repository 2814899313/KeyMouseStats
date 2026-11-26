using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

public readonly record struct DonutSlice(double Value, Color Color, string Label = "");

public readonly record struct DonutArc(
    int Index,
    double StartAngle,
    double SweepAngle,
    double Value,
    Color Color,
    string Label);

/// <summary>环图几何。角度单位为度，0° 指向 3 点方向，正角度在屏幕坐标中顺时针。</summary>
public sealed record DonutGeometry(
    Point Center,
    double OuterRadius,
    Rect OuterBounds,
    double InnerRadius,
    Rect InnerBounds,
    double StartAngle,
    double SweepAngle,
    double Total,
    IReadOnlyList<DonutArc> Arcs)
{
    public bool IsEmpty => Arcs.Count == 0 || Total <= 0;

    public Point PointAt(double angleDegrees, double radius)
    {
        double radians = angleDegrees * Math.PI / 180;
        return new Point(
            Center.X + Math.Cos(radians) * radius,
            Center.Y + Math.Sin(radians) * radius);
    }

    /// <summary>环形/扇区命中。圆孔与圆外返回 null；落在扇区分界时前一个扇区优先。</summary>
    public DonutHit? HitTest(Point point)
    {
        double dx = point.X - Center.X;
        double dy = point.Y - Center.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < InnerRadius || distance > OuterRadius)
        {
            return null;
        }

        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        for (int i = 0; i < Arcs.Count; i++)
        {
            DonutArc arc = Arcs[i];
            double sweep = Math.Abs(arc.SweepAngle);
            double delta = arc.SweepAngle >= 0
                ? Normalize360(angle - arc.StartAngle)
                : Normalize360(arc.StartAngle - angle);

            if (sweep >= 360 - 1e-9 || delta <= sweep + 1e-9)
            {
                return new DonutHit(arc.Index, angle, arc.Value, arc.Color, arc.Label);
            }
        }

        return null;
    }

    private static double Normalize360(double angle)
    {
        double value = angle % 360;
        return value < 0 ? value + 360 : value;
    }
}

public readonly record struct DonutHit(int Index, double Angle, double Value, Color Color, string Label);

/// <summary>环图 / 仪表盘：支持任意起始角、扫描角、中心文字与角度命中。</summary>
public static class Donut
{
    public static DonutGeometry Measure(
        Rect bounds,
        IReadOnlyList<DonutSlice> slices,
        double startAngle = -90,
        double sweepAngle = 360,
        double thicknessRatio = 0.32)
    {
        ArgumentNullException.ThrowIfNull(slices);

        Rect normalized = PrimitiveMath.Normalize(bounds);
        Point center = normalized.Center;
        double outer = Math.Min(normalized.Width, normalized.Height) / 2;
        double ratio = Math.Clamp(PrimitiveMath.Finite(thicknessRatio), 0.02, 0.98);
        double inner = outer * (1 - ratio);
        double start = double.IsFinite(startAngle) ? startAngle : -90;
        double sweep = Math.Clamp(PrimitiveMath.Finite(sweepAngle), -360, 360);

        Rect outerBounds = new(center.X - outer, center.Y - outer, outer * 2, outer * 2);
        Rect innerBounds = new(center.X - inner, center.Y - inner, inner * 2, inner * 2);

        double total = 0;
        for (int i = 0; i < slices.Count; i++)
        {
            total += Math.Max(0, PrimitiveMath.Finite(slices[i].Value));
        }

        if (total <= 0 || Math.Abs(sweep) < 1e-9 || outer <= 0)
        {
            return new DonutGeometry(
                center,
                outer,
                outerBounds,
                inner,
                innerBounds,
                start,
                sweep,
                total,
                []);
        }

        List<DonutArc> arcs = new(slices.Count);
        double cursor = start;
        int index = 0;

        for (int i = 0; i < slices.Count; i++)
        {
            DonutSlice slice = slices[i];
            double value = Math.Max(0, PrimitiveMath.Finite(slice.Value));
            if (value <= 0)
            {
                continue;
            }

            double arcSweep = sweep * value / total;
            arcs.Add(new DonutArc(index++, cursor, arcSweep, value, slice.Color, slice.Label));
            cursor += arcSweep;
        }

        return new DonutGeometry(
            center,
            outer,
            outerBounds,
            inner,
            innerBounds,
            start,
            sweep,
            total,
            arcs);
    }

    public static void Draw(
        DrawingContext context,
        DonutGeometry geometry,
        ThemeTokens theme,
        string? centerText = null,
        string? subText = null,
        double centerFontSize = 21,
        double subFontSize = 11)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(theme);

        double thickness = Math.Max(1, geometry.OuterRadius - geometry.InnerRadius);
        double radius = (geometry.OuterRadius + geometry.InnerRadius) / 2;

        if (geometry.IsEmpty)
        {
            IPen emptyPen = new Pen(new SolidColorBrush(theme.Line), thickness);
            context.DrawEllipse(null, emptyPen, geometry.Center, radius, radius);
        }
        else
        {
            for (int i = 0; i < geometry.Arcs.Count; i++)
            {
                DonutArc arc = geometry.Arcs[i];
                IBrush brush = new SolidColorBrush(arc.Color);
                IPen pen = new Pen(brush, thickness)
                {
                    LineCap = PenLineCap.Flat
                };

                if (Math.Abs(arc.SweepAngle) >= 359.999)
                {
                    context.DrawEllipse(null, pen, geometry.Center, radius, radius);
                    continue;
                }

                StreamGeometry path = BuildArc(geometry.Center, radius, arc.StartAngle, arc.SweepAngle);
                context.DrawGeometry(null, pen, path);
            }
        }

        if (!string.IsNullOrEmpty(centerText))
        {
            double mainSize = Math.Max(1, PrimitiveMath.Finite(centerFontSize));
            double subSize = Math.Max(1, PrimitiveMath.Finite(subFontSize));
            double innerWidth = Math.Max(1, geometry.InnerRadius * 2);

            Rect textBounds = string.IsNullOrEmpty(subText)
                ? new Rect(
                    geometry.Center.X - innerWidth / 2,
                    geometry.Center.Y - mainSize,
                    innerWidth,
                    mainSize * 2)
                : new Rect(
                    geometry.Center.X - innerWidth / 2,
                    geometry.Center.Y - mainSize * 0.75,
                    innerWidth,
                    mainSize * 1.2);

            TextBlock.DrawCentered(
                context,
                centerText,
                textBounds,
                mainSize,
                new SolidColorBrush(theme.Text),
                TextBlock.NumericFamily,
                maxWidth: innerWidth);

            if (!string.IsNullOrEmpty(subText))
            {
                Rect subBounds = new(
                    geometry.Center.X - innerWidth / 2,
                    textBounds.Bottom - 1,
                    innerWidth,
                    subSize * 1.5);

                TextBlock.DrawCentered(
                    context,
                    subText,
                    subBounds,
                    subSize,
                    new SolidColorBrush(theme.Muted),
                    TextBlock.DefaultFamily,
                    maxWidth: innerWidth);
            }
        }
    }

    private static StreamGeometry BuildArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        double startRadians = startAngle * Math.PI / 180;
        double endRadians = (startAngle + sweepAngle) * Math.PI / 180;
        Point start = new(
            center.X + Math.Cos(startRadians) * radius,
            center.Y + Math.Sin(startRadians) * radius);
        Point end = new(
            center.X + Math.Cos(endRadians) * radius,
            center.Y + Math.Sin(endRadians) * radius);

        StreamGeometry path = new();
        using (StreamGeometryContext pathContext = path.Open())
        {
            pathContext.BeginFigure(start, false);
            pathContext.ArcTo(
                end,
                new Size(radius, radius),
                0,
                Math.Abs(sweepAngle) > 180,
                sweepAngle >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise,
                true);
            pathContext.EndFigure(false);
        }

        return path;
    }
}
