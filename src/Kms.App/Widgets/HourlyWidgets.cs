using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Core.Time;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Widgets;

internal sealed class HourlyWidgetModel : WidgetPageModel
{
    public HourlyWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        StatsRange range = StatsRange.Build(Data, scope);
        Totals = AppContext.IsAll
            ? range.Totals
            : AppScopeData.BuildProfileTotals(AppContext.Profile, range.Totals);
        ActiveDays = Math.Max(1, range.ActiveDays);
    }

    public AppPageContext AppContext { get; }

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public DayRecord Totals { get; }

    public int ActiveDays { get; }

    public int Metric { get; set; }

    public int SelectedHour { get; set; } = -1;

    public double[] Values(int metric)
    {
        double[] values = new double[24];
        int nowHour = DateTime.Now.Hour;
        bool today = Scope.IsToday && Scope.End == DateTime.Today;
        for (int hour = 0; hour < 24; hour++)
        {
            if (today && hour > nowHour)
            {
                values[hour] = double.NaN;
                continue;
            }

            values[hour] = metric switch
            {
                1 => Totals.HourClicks[hour],
                2 => Totals.ActiveHours[hour] / 60.0 / ActiveDays,
                _ => Totals.HourKeys[hour]
            };
        }

        return values;
    }

    public string FormatValue(double value, int metric) =>
        metric == 2 ? value.ToString("0.#", CultureInfo.InvariantCulture) : PageKit.FormatCount((long)value);

    public string MetricName() => Metric switch { 1 => "点击", 2 => "活跃", _ => "击键" };

    public string MetricUnit() => Metric switch { 1 => "击", 2 => "分钟/天", _ => "键" };

    public (int Peak, double PeakValue) Peak(double[] values)
    {
        int peak = -1;
        double peakValue = 0;
        for (int hour = 0; hour < 24; hour++)
        {
            if (double.IsFinite(values[hour]) && values[hour] > peakValue)
            {
                peakValue = values[hour];
                peak = hour;
            }
        }

        return (peak, peakValue);
    }
}

internal static class HourlyWidgets
{
    private static HourlyWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("hourly", () => new HourlyWidgetModel(
            context.Data,
            context.Scope,
            context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpiPeakHour(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        double[] values = model.Values(0);
        (int peak, double peakValue) = model.Peak(values);
        return peak < 0
            ? MicroKpi.Card("黄金高峰", "--", "暂无小时记录", PageKit.T.Orange)
            : MicroKpi.Card("黄金高峰", $"{peak:00}:00", $"{PageKit.FormatCount((long)peakValue)} 击键", PageKit.T.Orange);
    }

    public static WidgetView CreateKpiActiveHours(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        double[] values = model.Values(0);
        int active = values.Count(value => double.IsFinite(value) && value > 0);
        return MicroKpi.Card("活跃小时", $"{active} / 24", "有击键记录的小时", PageKit.T.Cyan);
    }

    public static WidgetView CreateKpiNightRatio(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        double[] values = model.Values(0);
        double total = 0;
        double night = 0;
        for (int hour = 0; hour < 24; hour++)
        {
            if (!double.IsFinite(values[hour]))
            {
                continue;
            }

            total += values[hour];
            if (hour >= 22 || hour < 6)
            {
                night += values[hour];
            }
        }

        double ratio = total > 0 ? night / total : 0;
        return MicroKpi.Card(
            "夜间占比",
            MicroKpi.Percent(ratio),
            "22:00–06:00 击键占比",
            PageKit.T.Purple);
    }

    public static WidgetView CreateKpiOffPeakHour(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        double[] values = model.Values(0);
        int offPeak = -1;
        double lowest = double.MaxValue;
        for (int hour = 0; hour < 24; hour++)
        {
            if (!double.IsFinite(values[hour]) || values[hour] <= 0)
            {
                continue;
            }

            if (values[hour] < lowest)
            {
                lowest = values[hour];
                offPeak = hour;
            }
        }

        return offPeak < 0
            ? MicroKpi.Card("低谷时段", "--", "暂无有记录的小时", PageKit.T.Muted)
            : MicroKpi.Card("低谷时段", $"{offPeak:00}:00", $"{PageKit.FormatCount((long)lowest)} 击键", PageKit.T.Muted);
    }

    public static WidgetView CreateSummary(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        double[] values = model.Values(model.Metric);
        (int peak, double peakValue) = model.Peak(values);
        int activeHours = model.Totals.ActiveHours.Count(value => value > 0);
        Control body = PageKit.StatStripBody(
            ($"{model.Scope.Label}击键", PageKit.FormatCount(model.Totals.Keys), PageKit.T.Accent),
            ("点击", PageKit.FormatCount(model.Totals.Clicks), PageKit.T.Green),
            ("峰值小时", peak >= 0 ? $"{peak:00}:00 · {model.FormatValue(peakValue, model.Metric)}" : "--", PageKit.T.Orange),
            ("活跃跨度", $"{activeHours} 小时", PageKit.T.Purple),
            ("活跃时长", PageKit.FormatDuration(model.Totals.ActiveSeconds), PageKit.T.Cyan));
        return new WidgetView
        {
            Content = body,
            ShowHeader = false,
            ContentPadding = new Thickness(12),
            Elevated = true
        };
    }

    public static WidgetView CreateChart(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new HourlyChartView(model, context.VariantId, context.RequestReload, context.Span),
            HeaderHint = context.VariantId switch
            {
                "area" => $"平滑面积曲线 · {model.Scope.Label}聚合 · 突出波峰波谷",
                "polar" => $"24 小时极坐标雷达 · {model.Scope.Label}聚合",
                _ => $"点击柱子查看该小时详情 · {model.Scope.Label}聚合"
            },
            HeaderTrailing = PageKit.Badge(model.Scope.IsToday ? "TODAY" : model.Scope.Label, PageKit.T.Accent),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateDetail(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new HourlyDetailView(model),
            HeaderHint = "点击左侧任意小时",
            HeaderTrailing = PageKit.Badge("DETAIL", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateTopHours(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new HourlyTopView(model),
            HeaderHint = "当前指标下最活跃的小时 · 局部翻页",
            HeaderTrailing = PageKit.Badge("RANK", PageKit.T.Green),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateRhythm(WidgetContext context)
    {
        HourlyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new HourlyRhythmView(model),
            HeaderHint = $"每格代表 1 小时 · 按{model.Scope.Label}平均活跃分钟着色",
            HeaderTrailing = PageKit.Badge("RHYTHM", PageKit.T.Purple),
            ContentPadding = new Thickness(16)
        };
    }
}

internal sealed class HourlyChartView : UserControl
{
    private readonly HourlyWidgetModel _model;
    private readonly string _variant;
    private readonly Action? _reload;
    private readonly bool _compactSpan;
    private readonly ContentControl _host = new();
    private readonly Avalonia.Controls.TextBlock _caption = PageKit.Muted("", 9);
    private Control? _chart;

    public HourlyChartView(HourlyWidgetModel model, string variant, Action? reload, WidgetSpan span)
    {
        _model = model;
        _variant = variant;
        _reload = reload;
        _compactSpan = span.Rows <= 2;
        StackPanel body = new() { Spacing = 4 };
        body.Children.Add(_host);
        body.Children.Add(_caption);
        Content = body;
        Refresh();
        SizeChanged += (_, _) => ApplyMetrics();
    }

    private void Refresh()
    {
        double[] values = _model.Values(_model.Metric);
        double max = values.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        HourlyAxis.Compute(_model.Metric, max, out double top, out double step);
        if (string.Equals(_variant, "area", StringComparison.Ordinal))
        {
            HourlyAreaChart area = new();
            area.SetData(values, _model.Metric);
            _chart = area;
        }
        else if (string.Equals(_variant, "polar", StringComparison.Ordinal))
        {
            HourlyPolarChart polar = new();
            polar.SetData(values, _model.Metric, _model.Scope.IsToday && _model.Scope.End == DateTime.Today);
            _chart = polar;
        }
        else
        {
            HoursChart columns = new();
            columns.HourSelected += hour =>
            {
                _model.SelectedHour = hour;
                _reload?.Invoke();
            };
            columns.SetData(values, _model.Metric, top, step);
            if (_model.SelectedHour >= 0)
            {
                columns.SetSelected(_model.SelectedHour);
            }

            _chart = columns;
        }

        _chart.Height = CompactHeight();
        _host.Content = _chart;
        _caption.Text = $"纵轴单位：{HourlyAxis.Unit(_model.Metric).Trim()}；{_model.Scope.Caption}；今天未结束的小时不计。";
        ApplyMetrics();
    }

    private bool Compact => _compactSpan || Bounds.Width is > 0 and < 300;

    private double CompactHeight() =>
        Compact
            ? Math.Clamp(Bounds.Height - 8, 62, 220)
            : Math.Clamp(Bounds.Height - 24, 110, 480);

    private void ApplyMetrics()
    {
        if (_chart is null)
        {
            return;
        }

        _chart.Height = CompactHeight();
        _caption.IsVisible = !Compact;
    }
}

internal sealed class HourlyAreaChart : Control
{
    private double[] _values = new double[24];
    private int _metric;

    public void SetData(double[] values, int metric)
    {
        _values = values;
        _metric = metric;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        Rect plot = new(46, 8, Math.Max(1, Bounds.Width - 58), Math.Max(1, Bounds.Height - 30));
        double[] finite = _values.Where(double.IsFinite).ToArray();
        double max = _metric == 2 ? 60 : Math.Max(1, finite.DefaultIfEmpty(0).Max());
        Color tone = _metric switch { 1 => theme.Green, 2 => theme.Purple, _ => theme.Accent };
        IBrush muted = PageKit.Brush(theme.Muted);

        for (int tick = 0; tick <= 4; tick++)
        {
            double y = plot.Bottom - plot.Height * tick / 4.0;
            context.DrawLine(new Pen(PageKit.Brush(PageKit.Alpha(theme.Line, 0.35)), 1),
                new Point(plot.Left, y), new Point(plot.Right, y));
            Kms.Render.Primitives.TextBlock.DrawText(
                context, FormatAxis(max * tick / 4.0), new Point(0, y - 6), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 42, alignment: TextAlignment.Right);
        }

        double xStep = plot.Width / 23.0;
        List<Point> run = [];
        for (int hour = 0; hour < 24; hour++)
        {
            if (!double.IsFinite(_values[hour]))
            {
                DrawRun(context, run, plot, tone);
                run.Clear();
                continue;
            }

            double x = plot.Left + xStep * hour;
            double y = plot.Bottom - Math.Clamp(_values[hour] / max, 0, 1) * plot.Height;
            run.Add(new Point(x, y));
        }

        DrawRun(context, run, plot, tone);
        for (int hour = 0; hour < 24; hour += 3)
        {
            double x = plot.Left + xStep * hour;
            Kms.Render.Primitives.TextBlock.DrawText(
                context, hour.ToString("00", CultureInfo.InvariantCulture),
                new Point(x - 8, plot.Bottom + 8), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 22);
        }

        Kms.Render.Primitives.TextBlock.DrawText(
            context, _metric == 2 ? "活跃分钟" : _metric == 1 ? "点击" : "击键",
            new Point(plot.Right - 40, 0), 10, PageKit.Brush(tone),
            Kms.Render.Primitives.TextBlock.DefaultFamily);
    }

    private static void DrawRun(DrawingContext context, List<Point> run, Rect plot, Color tone)
    {
        if (run.Count < 2)
        {
            return;
        }

        StreamGeometry area = new();
        using (StreamGeometryContext path = area.Open())
        {
            path.BeginFigure(new Point(run[0].X, plot.Bottom), true);
            path.LineTo(run[0]);
            for (int i = 1; i < run.Count; i++)
            {
                Point p0 = run[Math.Max(0, i - 2)];
                Point p1 = run[i - 1];
                Point p2 = run[i];
                Point p3 = run[Math.Min(run.Count - 1, i + 1)];
                path.CubicBezierTo(
                    new Point(p1.X + (p2.X - p0.X) / 6, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, plot.Top, plot.Bottom)),
                    new Point(p2.X - (p3.X - p1.X) / 6, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, plot.Top, plot.Bottom)),
                    p2);
            }

            path.LineTo(new Point(run[^1].X, plot.Bottom));
            path.EndFigure(true);
        }

        context.DrawGeometry(PageKit.Gradient(PageKit.Alpha(tone, 0.32), PageKit.Alpha(tone, 0.01), 90), null, area);

        StreamGeometry line = new();
        using (StreamGeometryContext path = line.Open())
        {
            path.BeginFigure(run[0], false);
            for (int i = 1; i < run.Count; i++)
            {
                Point p0 = run[Math.Max(0, i - 2)];
                Point p1 = run[i - 1];
                Point p2 = run[i];
                Point p3 = run[Math.Min(run.Count - 1, i + 1)];
                path.CubicBezierTo(
                    new Point(p1.X + (p2.X - p0.X) / 6, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, plot.Top, plot.Bottom)),
                    new Point(p2.X - (p3.X - p1.X) / 6, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, plot.Top, plot.Bottom)),
                    p2);
            }

            path.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(PageKit.Brush(tone), 2), line);
    }

    private static string FormatAxis(double value) =>
        value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}

internal sealed class HourlyPolarChart : Control
{
    private double[] _values = new double[24];
    private int _metric;
    private bool _today;

    public void SetData(double[] values, int metric, bool today)
    {
        _values = values;
        _metric = metric;
        _today = today;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        double radius = Math.Max(20, Math.Min(Bounds.Width, Bounds.Height) / 2 - 30);
        Color tone = _metric switch { 1 => theme.Green, 2 => theme.Purple, _ => theme.Accent };
        IBrush muted = PageKit.Brush(theme.Muted);

        for (int ring = 1; ring <= 3; ring++)
        {
            context.DrawEllipse(null,
                new Pen(PageKit.Brush(PageKit.Alpha(theme.Line, 0.28)), 1),
                center,
                radius * ring / 3.0,
                radius * ring / 3.0);
        }

        double max = _metric == 2 ? 60 : Math.Max(1, _values.Where(double.IsFinite).DefaultIfEmpty(0).Max());
        double inner = radius * 0.24;
        double spoke = Math.Max(3, Math.PI * 2 * radius / 24 * 0.55);
        for (int hour = 0; hour < 24; hour++)
        {
            if (!double.IsFinite(_values[hour]))
            {
                continue;
            }

            double fraction = Math.Clamp(_values[hour] / max, 0, 1);
            double outer = inner + (radius - inner) * fraction;
            double angle = -90 + hour * 15.0;
            double radians = angle * Math.PI / 180;
            Point start = new(center.X + Math.Cos(radians) * inner, center.Y + Math.Sin(radians) * inner);
            Point end = new(center.X + Math.Cos(radians) * outer, center.Y + Math.Sin(radians) * outer);
            bool current = _today && hour == DateTime.Now.Hour
                           && _values[hour] > 0;
            Color color = current ? theme.Orange : PageKit.Alpha(tone, 0.35 + 0.55 * fraction);
            context.DrawLine(new Pen(PageKit.Brush(color), spoke)
            {
                LineCap = PenLineCap.Round
            }, start, end);
        }

        for (int hour = 0; hour < 24; hour += 3)
        {
            double angle = (-90 + hour * 15.0) * Math.PI / 180;
            Point label = new(
                center.X + Math.Cos(angle) * (radius + 10),
                center.Y + Math.Sin(angle) * (radius + 10));            Kms.Render.Primitives.TextBlock.DrawCentered(
                context,
                hour.ToString("00", CultureInfo.InvariantCulture),
                new Rect(label.X - 12, label.Y - 7, 24, 14),
                8.5,
                muted,
                Kms.Render.Primitives.TextBlock.NumericFamily);
        }

        Kms.Render.Primitives.TextBlock.DrawCentered(
            context,
            _metric == 2 ? "活跃分钟" : _metric == 1 ? "点击" : "击键",
            new Rect(center.X - 46, center.Y - 10, 92, 20),
            10,
            PageKit.Brush(tone),
            Kms.Render.Primitives.TextBlock.DefaultFamily,
            FontWeight.SemiBold);
    }
}

internal sealed class HourlyDetailView : UserControl
{
    private readonly HourlyWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 6 };

    public HourlyDetailView(HourlyWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        double[] values = _model.Values(_model.Metric);
        if (_model.SelectedHour < 0)
        {
            double ringHeight = Math.Clamp(Bounds.Height - 120, 70, 220);
            _body.Children.Add(new PageKit.Ring(
                [
                    new DonutSlice(Math.Max(0, _model.Totals.Keys), PageKit.T.Accent),
                    new DonutSlice(Math.Max(0, _model.Totals.Clicks), PageKit.T.Green),
                    new DonutSlice(Math.Max(0, _model.Totals.Wheel), PageKit.T.Purple)
                ],
                PageKit.FormatCount(_model.Totals.Keys + _model.Totals.Clicks + _model.Totals.Wheel),
                $"{_model.Scope.Label}输入") { Height = ringHeight });
            _body.Children.Add(PageKit.StatRow("击键", PageKit.FormatCount(_model.Totals.Keys), PageKit.T.Accent));
            _body.Children.Add(PageKit.StatRow("点击", PageKit.FormatCount(_model.Totals.Clicks), PageKit.T.Green));
            _body.Children.Add(PageKit.StatRow("活跃", PageKit.FormatDuration(_model.Totals.ActiveSeconds), PageKit.T.Cyan));
            _body.Children.Add(PageKit.Muted("点击柱子查看单个小时；当前为区间构成。", 9));
            return;
        }

        int selected = Math.Clamp(_model.SelectedHour, 0, 23);
        double value = values[selected];
        long hourKeys = _model.Totals.HourKeys[selected];
        long hourClicks = _model.Totals.HourClicks[selected];
        double activeMinutes = _model.Totals.ActiveHours[selected] / 60.0 / _model.ActiveDays;
        double share = _model.Totals.Keys <= 0 ? 0 : hourKeys * 100.0 / _model.Totals.Keys;
        double max = values.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        _body.Children.Add(PageKit.Label($"{selected:00}:00", 30, PageKit.T.Text, FontWeight.SemiBold));
        _body.Children.Add(PageKit.Muted(double.IsFinite(value)
            ? $"{_model.MetricName()} {_model.FormatValue(value, _model.Metric)}"
            : "该小时尚未结束", 10));
        _body.Children.Add(PageKit.Divider());
        _body.Children.Add(PageKit.StatRow("击键", PageKit.FormatCount(hourKeys), PageKit.T.Accent));
        _body.Children.Add(PageKit.StatRow("点击", PageKit.FormatCount(hourClicks), PageKit.T.Green));
        _body.Children.Add(PageKit.StatRow("活跃", $"{activeMinutes:0.#} 分钟/天", PageKit.T.Purple));
        _body.Children.Add(PageKit.StatRow("占区间击键", $"{share:0.#}%", PageKit.T.Cyan));
        _body.Children.Add(PageKit.Progress("小时强度",
            double.IsFinite(value) ? Math.Min(1, value / Math.Max(1, max)) : 0, PageKit.T.Orange));
    }
}

internal sealed class HourlyTopView : UserControl
{
    private readonly HourlyWidgetModel _model;
    private readonly PageKit.PagedList _list = new(3, 4);

    public HourlyTopView(HourlyWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) =>
        {
            int size = Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 46), 2, 8) : 3;
            _list.SetPageSize(size);
        };
    }

    private void Build()
    {
        double[] values = _model.Values(_model.Metric);
        long total = 0;
        for (int hour = 0; hour < 24; hour++)
        {
            if (double.IsFinite(values[hour]))
            {
                total += (long)values[hour];
            }
        }

        List<Control> rows = [];
        int rank = 1;
        foreach ((double value, int hour) in values
                     .Select((value, hour) => (value, hour))
                     .Where(item => double.IsFinite(item.value) && item.value > 0)
                     .OrderByDescending(item => item.value)
                     .Take(8))
        {
            rows.Add(PageKit.RankRow(
                rank++,
                $"{hour:00}:00 – {hour:00}:59",
                _model.FormatValue(value, _model.Metric),
                total <= 0 ? 0 : value / total * 1.8,
                PageKit.T.Cyan,
                _model.MetricUnit()));
        }

        if (rows.Count == 0)
        {
            rows.Add(PageKit.Empty("该指标还没有小时数据"));
        }

        _list.SetItems(rows);
    }
}

internal sealed class HourlyRhythmView : UserControl
{
    private readonly HourlyWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 4 };

    public HourlyRhythmView(HourlyWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        double[] values = _model.Totals.ActiveHours
            .Select(seconds => seconds / 60.0 / _model.ActiveDays)
            .ToArray();
        _body.Children.Add(new PageKit.HourStrip(values, PageKit.T.Purple)
        {
            Height = Math.Clamp(Bounds.Height - 100, 26, 180)
        });
        _body.Children.Add(PageKit.Muted("00  03  06  09  12  15  18  21", 8));
        _body.Children.Add(PageKit.StatRow("活跃小时数",
            $"{_model.Totals.ActiveHours.Count(value => value > 0)} / 24", PageKit.T.Purple));
        _body.Children.Add(PageKit.StatRow("区间活跃",
            PageKit.FormatDuration(_model.Totals.ActiveSeconds), PageKit.T.Cyan));
    }
}

internal sealed class HoursChart : Control
{
    private double[] _values = new double[24];
    private int _metric;
    private int _selected = -1;
    private double _top = 1;
    private double _step = 1;
    private BarChartGeometry? _geometry;

    public event Action<int>? HourSelected;

    public double Top => _top;

    public HoursChart()
    {
        PointerPressed += (_, e) =>
        {
            if (_geometry is null)
            {
                return;
            }

            Point point = e.GetPosition(this);
            foreach (BarGeometry bar in _geometry.Bars)
            {
                if (bar.Slot.Contains(point))
                {
                    _selected = bar.Index;
                    InvalidateVisual();
                    HourSelected?.Invoke(bar.Index);
                    e.Handled = true;
                    return;
                }
            }
        };
    }

    public void SetData(double[] values, int metric, double top, double step)
    {
        _values = values;
        _metric = metric;
        _top = top <= 0 ? 1 : top;
        _step = step <= 0 ? 1 : step;
        _selected = -1;
        InvalidateVisual();
    }

    public void SetSelected(int hour)
    {
        if (_selected == hour)
        {
            return;
        }

        _selected = hour;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        Rect plot = new(8, 8, Math.Max(1, Bounds.Width - 16), Math.Max(1, Bounds.Height - 30));
        Color barColor = _metric switch { 1 => theme.Green, 2 => theme.Purple, _ => theme.Accent };
        List<BarDatum> bars = new(24);
        for (int hour = 0; hour < 24; hour++)
        {
            double value = double.IsFinite(_values[hour]) ? _values[hour] : 0;
            if (_metric == 2)
            {
                value = Math.Min(value, HourlyAxis.MaxActiveMinutes);
            }

            Color? color = !double.IsFinite(_values[hour])
                ? null
                : hour == _selected
                    ? theme.Orange
                    : PageKit.Alpha(barColor, 0.82);
            bars.Add(new BarDatum(value, hour.ToString("00", CultureInfo.InvariantCulture), color));
        }

        BarChartGeometry geometry = BarChart.Measure(
            plot,
            bars,
            new BarChartOptions(Padding: 0, GapRatio: 0.24, IncludeZero: true, YMin: 0, YMax: _top, YTickCount: 4));
        _geometry = geometry;
        BarChart.Draw(context, geometry, theme, showBaseline: true, cornerRadius: 3);
        if (_selected >= 0 && _selected < geometry.Bars.Count)
        {
            BarGeometry bar = geometry.Bars[_selected];
            context.DrawRectangle(PageKit.Brush(PageKit.Alpha(theme.Orange, 0.06)), null,
                new Rect(bar.Slot.Left - 1, plot.Top, bar.Slot.Width + 2, plot.Height), 4);
            context.DrawLine(new Pen(PageKit.Brush(PageKit.Alpha(theme.Orange, 0.6)), 1),
                new Point(bar.Slot.Center.X, plot.Top), new Point(bar.Slot.Center.X, plot.Bottom));
        }

        IBrush muted = PageKit.Brush(theme.Muted);
        for (int tick = 0; tick <= 4; tick++)
        {
            double value = _step * tick;
            double y = plot.Bottom - value / _top * plot.Height;
            Kms.Render.Primitives.TextBlock.DrawText(
                context, FormatAxis(value), new Point(0, y - 7), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 40, alignment: TextAlignment.Right);
        }

        for (int hour = 0; hour < 24; hour += 3)
        {
            BarGeometry bar = geometry.Bars[hour];
            Kms.Render.Primitives.TextBlock.DrawText(
                context, hour.ToString("00", CultureInfo.InvariantCulture),
                new Point(bar.Slot.Center.X - 8, plot.Bottom + 8), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 22);
        }

        Kms.Render.Primitives.TextBlock.DrawText(
            context, _metric == 2 ? "活跃分钟" : _metric == 1 ? "点击" : "击键",
            new Point(plot.Right - 40, 0), 10, PageKit.Brush(barColor),
            Kms.Render.Primitives.TextBlock.DefaultFamily);
    }

    private static string FormatAxis(double value) =>
        value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}
