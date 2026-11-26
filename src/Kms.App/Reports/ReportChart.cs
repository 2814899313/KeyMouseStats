using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Render.Primitives;
using Kms.Render.Theming;

namespace Kms.App.Reports;

/// <summary>报表页的轻量趋势/柱状图；数据来自 Core，不依赖主面板控件。</summary>
internal sealed class ReportChart : Control
{
    private readonly IReadOnlyList<double> _values;
    private readonly IReadOnlyList<string> _labels;
    private readonly Color _color;
    private readonly bool _line;

    public ReportChart(IReadOnlyList<double> values, IReadOnlyList<string> labels, Color color, bool line = false)
    {
        _values = values;
        _labels = labels;
        _color = color;
        _line = line;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_values.Count == 0)
        {
            return;
        }

        var theme = ThemeManager.Current;
        Rect plot = new(34, 10, Math.Max(1, Bounds.Width - 48), Math.Max(1, Bounds.Height - 34));
        IBrush muted = new SolidColorBrush(theme.Muted);

        if (_line)
        {
            LineChartGeometry geometry = LineChart.Measure(
                plot,
                [new LineSeries("value", _values, _color)],
                new LineChartOptions(Padding: 0, IncludeZero: true, YTickCount: 3));

            if (geometry.Series.Count > 0 && geometry.Series[0].Points.Count > 1)
            {
                IReadOnlyList<Point> points = geometry.Series[0].Points;
                StreamGeometry area = new();
                using (StreamGeometryContext path = area.Open())
                {
                    path.BeginFigure(new Point(points[0].X, geometry.Plot.Bottom), true);
                    foreach (Point point in points)
                    {
                        path.LineTo(point);
                    }

                    path.LineTo(new Point(points[^1].X, geometry.Plot.Bottom));
                    path.EndFigure(true);
                }

                context.DrawGeometry(
                    new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(70, _color.R, _color.G, _color.B), 0),
                            new GradientStop(Color.FromArgb(3, _color.R, _color.G, _color.B), 1)
                        }
                    },
                    null,
                    area);
            }

            LineChart.Draw(context, geometry, theme);
            DrawYAxis(context, geometry.YTicks, geometry.YMin, geometry.YMax, geometry.Plot, muted);
        }
        else
        {
            List<BarDatum> bars = new(_values.Count);
            for (int i = 0; i < _values.Count; i++)
            {
                bars.Add(new BarDatum(_values[i], i < _labels.Count ? _labels[i] : "", _color));
            }

            BarChartGeometry geometry = BarChart.Measure(
                plot,
                bars,
                new BarChartOptions(Padding: 0, GapRatio: 0.28, IncludeZero: true, YTickCount: 3));
            BarChart.Draw(context, geometry, theme, showBaseline: true, cornerRadius: 3);
            DrawYAxis(context, geometry.YTicks, geometry.YMin, geometry.YMax, geometry.Plot, muted);
        }

        int labelStep = Math.Max(1, (int)Math.Ceiling(_labels.Count / 10.0));
        for (int i = 0; i < _labels.Count && i < _values.Count; i += labelStep)
        {
            double x = _line
                ? _values.Count == 1
                    ? plot.Center.X
                    : plot.Left + plot.Width * i / (_values.Count - 1)
                : plot.Left + plot.Width * (i + 0.5) / _values.Count;
            Kms.Render.Primitives.TextBlock.DrawText(
                context,
                _labels[i],
                new Point(x - 10, plot.Bottom + 9),
                8,
                muted,
                Kms.Render.Primitives.TextBlock.NumericFamily,
                maxWidth: 30);
        }
    }

    private static void DrawYAxis(
        DrawingContext context,
        IReadOnlyList<double> ticks,
        double min,
        double max,
        Rect plot,
        IBrush muted)
    {
        for (int i = 0; i < ticks.Count; i++)
        {
            double value = ticks[i];
            double y = max <= min ? plot.Bottom : plot.Bottom - (value - min) / (max - min) * plot.Height;
            Kms.Render.Primitives.TextBlock.DrawText(
                context,
                value >= 1000 ? (value / 1000).ToString("0.#") + "k" : value.ToString("0.#"),
                new Point(0, y - 6),
                8,
                muted,
                Kms.Render.Primitives.TextBlock.NumericFamily,
                maxWidth: 32,
                alignment: TextAlignment.Right);
        }
    }
}
