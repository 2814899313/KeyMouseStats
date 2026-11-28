using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Widgets;

internal sealed class TrendsWidgetModel : WidgetPageModel
{
    public TrendsWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        Hourly = scope.Scope == TimeScope.Today;
    }

    public AppPageContext AppContext { get; }

    public bool IsAppFiltered => !AppContext.IsAll;

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public bool Hourly { get; }

    public int Metric { get; set; }

    public string[] MetricNames => Hourly
        ? ["击键", "点击", "活跃分钟"]
        : ["击键", "点击", "滚轮", "APM"];

    public (double[] Values, string[] Labels, DateTime[] Stamps) RangeValues(int metric)
    {
        DateTime now = DateTime.Now;
        if (Hourly)
        {
            bool actualToday = Scope.End == now.Date;
            double[] values = new double[24];
            string[] labels = new string[24];
            DateTime[] stamps = new DateTime[24];
            DayRecord? day = ScopeDay();
            IReadOnlyList<AppHourlyBucket>? profileHours = AppContext.Profile?.Hours;
            for (int hour = 0; hour < 24; hour++)
            {
                stamps[hour] = Scope.Start.AddHours(hour);
                labels[hour] = hour.ToString("00", CultureInfo.InvariantCulture);
                if (actualToday && hour > now.Hour)
                {
                    values[hour] = double.NaN;
                    continue;
                }

                values[hour] = profileHours is { Count: > 0 } && hour < profileHours.Count
                    ? metric switch
                    {
                        1 => profileHours[hour].Clicks,
                        2 => profileHours[hour].ActiveSeconds / 60.0,
                        _ => profileHours[hour].Keys
                    }
                    : day is null
                        ? 0
                        : metric switch
                        {
                            1 => day.HourClicks[hour],
                            2 => day.ActiveHours[hour] / 60.0,
                            _ => day.HourKeys[hour]
                        };
            }

            return (values, labels, stamps);
        }

        int count = Scope.Days;
        double[] daily = new double[count];
        string[] dayLabels = new string[count];
        DateTime[] days = new DateTime[count];
        for (int i = 0; i < count; i++)
        {
            DateTime date = Scope.Start.AddDays(i);
            days[i] = date;
            dayLabels[i] = date.ToString("MM/dd", CultureInfo.InvariantCulture);
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (Data.History.TryGetValue(key, out DayRecord? day))
            {
                daily[i] = metric switch
                {
                    1 => day.Clicks,
                    2 => day.Wheel,
                    3 => day.PeakApm,
                    _ => day.Keys
                };
            }
        }

        return (daily, dayLabels, days);
    }

    public double PreviousTotal(int metric)
    {
        if (Hourly)
        {
            DateTime yesterday = Scope.Start.AddDays(-1);
            string key = yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!Data.History.TryGetValue(key, out DayRecord? day))
            {
                return 0;
            }

            int last = Scope.End == DateTime.Today ? Math.Min(DateTime.Now.Hour, 23) : 23;
            double total = 0;
            for (int hour = 0; hour <= last; hour++)
            {
                total += metric switch
                {
                    1 => day.HourClicks[hour],
                    2 => day.ActiveHours[hour] / 60.0,
                    _ => day.HourKeys[hour]
                };
            }

            return total;
        }

        double previous = 0;
        int count = Scope.Days;
        for (int i = 0; i < count; i++)
        {
            DateTime date = Scope.Start.AddDays(-count + i);
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!Data.History.TryGetValue(key, out DayRecord? day))
            {
                continue;
            }

            previous += metric switch
            {
                1 => day.Clicks,
                2 => day.Wheel,
                3 => day.PeakApm,
                _ => day.Keys
            };
        }

        return previous;
    }

    public DayRecord? ScopeDay()
    {
        string key = Scope.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Data.History.TryGetValue(key, out DayRecord? day) ? day : null;
    }

    public string FormatValue(double value, int metric)
    {
        if (Hourly && metric == 2)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        return value >= 1000
            ? (value / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + "k"
            : value.ToString(value < 10 ? "0.##" : "0", CultureInfo.InvariantCulture);
    }

    public static double Median(double[] values)
    {
        double[] sorted = values.Where(double.IsFinite).OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
    }

    public static string Weekday(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}

internal static class TrendWidgets
{
    private static TrendsWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("trends", () => new TrendsWidgetModel(
            context.Data,
            context.Scope,
            context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpiTotal(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        (double[] values, _, _) = model.RangeValues(0);
        double total = values.Where(double.IsFinite).Sum();
        return MicroKpi.Card(
            model.Hourly ? "今日总量" : "周期总量",
            PageKit.FormatCount((long)total),
            model.Hourly ? "今日累计击键" : $"{model.Scope.Days} 天累计击键",
            PageKit.T.Accent);
    }

    public static WidgetView CreateKpiDailyAverage(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        (double[] values, _, _) = model.RangeValues(0);
        double[] finite = values.Where(double.IsFinite).ToArray();
        double average = finite.Length == 0 ? 0 : finite.Average();
        return MicroKpi.Card(
            model.Hourly ? "小时均值" : "日均均值",
            PageKit.FormatCount((long)average),
            model.Hourly ? "按已完成小时平均" : $"按 {finite.Length} 个有记录日平均",
            PageKit.T.Green);
    }

    public static WidgetView CreateKpiPeakDay(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        (double[] values, _, DateTime[] stamps) = model.RangeValues(0);
        int index = -1;
        double peak = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (double.IsFinite(values[i]) && values[i] > peak)
            {
                peak = values[i];
                index = i;
            }
        }

        if (index < 0)
        {
            return MicroKpi.Card(model.Hourly ? "峰值时刻" : "峰值日", "--", "暂无记录", PageKit.T.Orange);
        }

        string value = model.Hourly
            ? $"{index:00}:00"
            : stamps[index].ToString("MM-dd", CultureInfo.InvariantCulture);
        return MicroKpi.Card(
            model.Hourly ? "峰值时刻" : "峰值日",
            value,
            $"{PageKit.FormatCount((long)peak)} 击键",
            PageKit.T.Orange);
    }

    public static WidgetView CreateKpiGrowth(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        (double[] values, _, _) = model.RangeValues(0);
        double total = values.Where(double.IsFinite).Sum();
        double previous = model.PreviousTotal(0);
        double delta = previous > 0 ? (total - previous) * 100.0 / previous : double.NaN;
        string value = double.IsNaN(delta)
            ? "--"
            : (delta >= 0 ? "+" : "") + delta.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        return MicroKpi.Card(
            model.Hourly ? "较昨日同期" : "较上期",
            value,
            previous > 0 ? $"上期 {PageKit.FormatCount((long)previous)}" : "暂无可比基线",
            double.IsNaN(delta) || delta >= 0 ? PageKit.T.Cyan : PageKit.T.Red);
    }

    public static WidgetView CreateSummary(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        (double[] values, _, _) = model.RangeValues(model.Metric);
        double[] finite = values.Where(double.IsFinite).ToArray();
        double total = finite.Sum();
        double average = finite.Length == 0 ? 0 : finite.Average();
        double max = finite.Length == 0 ? 0 : finite.Max();
        int maxIndex = Array.IndexOf(values, max);
        double previous = model.PreviousTotal(model.Metric);
        double delta = previous > 0 ? (total - previous) * 100.0 / previous : double.NaN;

        string peakLabel = model.Hourly
            ? maxIndex >= 0 ? $"{model.FormatValue(max, model.Metric)} · {maxIndex:00}:00" : "--"
            : maxIndex >= 0 ? $"{model.FormatValue(max, model.Metric)} · {model.Scope.Start.AddDays(maxIndex):MM-dd}" : "--";
        int active = values.Count(value => double.IsFinite(value) && value > 0);
        int elapsed = model.Hourly && model.Scope.End == DateTime.Today
            ? Math.Min(24, DateTime.Now.Hour + 1)
            : 24;
        string activeText = model.Hourly ? $"{active} / {elapsed} 小时" : $"{active} / {values.Length} 天";

        Control body = PageKit.StatStripBody(
            (model.Hourly ? "今日总量" : "区间总量", model.FormatValue(total, model.Metric), PageKit.T.Accent),
            (model.Hourly ? "每小时均值" : "日均", model.FormatValue(average, model.Metric), PageKit.T.Green),
            (model.Hourly ? "峰值时刻" : "峰值日", peakLabel, PageKit.T.Orange),
            ("较上期", double.IsNaN(delta) ? "--" : $"{(delta >= 0 ? "+" : "")}{delta:0.#}%",
                delta < 0 ? PageKit.T.Red : PageKit.T.Cyan),
            (model.Hourly ? "活跃小时" : "活跃天", activeText, PageKit.T.Purple));

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
        TrendsWidgetModel model = Resolve(context);
        TrendChartView view = new(model, context.VariantId, context.RequestReload, context.Span);
        return new WidgetView
        {
            Content = view,
            HeaderHint = context.VariantId switch
            {
                "impulse" => "脉冲柱状对比 · 突出单点峰值与当前时段",
                "cumulative" => "周期累计增长曲线 · 看整体推进速度",
                _ => model.Hourly ? "00:00 → 现在 · 今日 24 小时逐时视图" : "区间内的日值走势 · 今日尚未结束的时段不计"
            },
            HeaderTrailing = PageKit.Badge(model.Hourly ? "HOURLY" : "TREND", PageKit.T.Accent),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateDetail(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new TrendDetailView(model),
            HeaderHint = "条形按区间峰值归一 · 局部翻页",
            HeaderTrailing = PageKit.Badge(model.Hourly ? "HOUR" : "DAILY", PageKit.T.Green),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateRhythm(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new TrendRhythmView(model),
            HeaderHint = model.Hourly ? "今日 24 小时击键分布 · 用于看当前节奏" : "当前区间各星期均值 · 用于判断稳定输出还是周末冲刺",
            HeaderTrailing = PageKit.Badge(model.Hourly ? "TODAY" : "WEEK", PageKit.T.Purple),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateInsight(WidgetContext context)
    {
        TrendsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new TrendInsightView(model),
            HeaderHint = "自动从当前全局范围提取",
            HeaderTrailing = PageKit.Badge("INSIGHT", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }
}

internal sealed class TrendChartView : UserControl
{
    private readonly TrendsWidgetModel _model;
    private readonly string _variant;
    private readonly Action? _reload;
    private readonly bool _narrowSpan;
    private readonly StackPanel _selector;
    private readonly ContentControl _chartHost = new();
    private Control? _chart;

    public TrendChartView(TrendsWidgetModel model, string variant, Action? reload, WidgetSpan span)
    {
        _model = model;
        _variant = variant;
        _reload = reload;
        _narrowSpan = span.Columns <= 3;
        _selector = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        StackPanel body = new() { Spacing = 8 };
        body.Children.Add(_selector);
        body.Children.Add(_chartHost);
        Content = body;
        Rebuild();
        SizeChanged += (_, _) => ApplyMetrics();
    }

    private bool Narrow => _narrowSpan || Bounds.Width is > 0 and < 360;

    private void Rebuild()
    {
        _selector.Children.Clear();
        _selector.IsVisible = !Narrow;
        string[] names = _model.MetricNames;
        if (!Narrow)
        {
            _selector.Children.Add(PageKit.Segmented(names, _model.Metric, index =>
            {
                if (_model.Metric == index)
                {
                    return;
                }

                _model.Metric = index;
                _reload?.Invoke();
            }, 10));
        }
        (double[] values, string[] labels, DateTime[] stamps) = _model.RangeValues(_model.Metric);
        if (string.Equals(_variant, "impulse", StringComparison.Ordinal))
        {
            TrendImpulseChart impulse = new();
            impulse.SetData(values, labels, stamps, _model.Metric, _model.Hourly);
            _chart = impulse;
        }
        else if (string.Equals(_variant, "cumulative", StringComparison.Ordinal))
        {
            TrendCumulativeChart cumulative = new();
            cumulative.SetData(values, labels, _model.Metric, _model.Hourly);
            _chart = cumulative;
        }
        else
        {
            AreaTrendChart line = new();
            line.SetData(values, labels, stamps, _model.Metric, _model.Hourly);
            _chart = line;
        }

        _chart.Height = ChartHeight();
        _chartHost.Content = _chart;
    }

    private double ChartHeight() =>
        Bounds.Height is > 0 and < 210
            ? Math.Clamp(Bounds.Height - (_selector.IsVisible ? 34 : 8), 58, 220)
            : Math.Clamp(Bounds.Height - 50, 120, 420);

    private void ApplyMetrics()
    {
        bool narrow = Narrow;
        bool selectorVisible = !narrow;
        if (_selector.IsVisible != selectorVisible)
        {
            Rebuild();
            return;
        }

        if (_chart is not null)
        {
            _chart.Height = ChartHeight();
        }
    }
}

internal sealed class TrendImpulseChart : Control
{
    private double[] _values = [];
    private string[] _labels = [];
    private DateTime[] _stamps = [];
    private int _metric;
    private bool _hourly;

    public void SetData(double[] values, string[] labels, DateTime[] stamps, int metric, bool hourly)
    {
        _values = values;
        _labels = labels;
        _stamps = stamps;
        _metric = metric;
        _hourly = hourly;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        Rect plot = new(46, 12, Math.Max(1, Bounds.Width - 58), Math.Max(1, Bounds.Height - 42));
        if (_values.Length == 0)
        {
            return;
        }

        Color tone = _metric switch { 1 => theme.Green, 2 => theme.Purple, 3 => theme.Orange, _ => theme.Accent };
        double max = _metric == 2 && _hourly
            ? 60
            : Math.Max(1, _values.Where(double.IsFinite).DefaultIfEmpty(0).Max());
        int peak = -1;
        double peakValue = 0;
        for (int i = 0; i < _values.Length; i++)
        {
            if (double.IsFinite(_values[i]) && _values[i] > peakValue)
            {
                peakValue = _values[i];
                peak = i;
            }
        }

        List<BarDatum> bars = new(_values.Length);
        for (int i = 0; i < _values.Length; i++)
        {
            double value = double.IsFinite(_values[i]) ? Math.Max(0, _values[i]) : 0;
            bool current = _stamps.Length > i
                && (_hourly
                    ? _stamps[i].Hour == DateTime.Now.Hour && _stamps[i].Date == DateTime.Today
                    : _stamps[i].Date == DateTime.Today);
            Color? color = i == peak || current ? theme.Orange : PageKit.Alpha(tone, 0.78);
            string label = i < _labels.Length ? _labels[i] : "";
            bars.Add(new BarDatum(value, label, color));
        }

        BarChartGeometry geometry = BarChart.Measure(
            plot,
            bars,
            new BarChartOptions(Padding: 0, GapRatio: 0.28, IncludeZero: true, YMin: 0, YMax: max, YTickCount: 4));
        BarChart.Draw(context, geometry, theme, showBaseline: true, cornerRadius: 2);

        IBrush muted = PageKit.Brush(theme.Muted);
        for (int tick = 0; tick <= 4; tick++)
        {
            double value = max * tick / 4.0;
            double y = plot.Bottom - value / max * plot.Height;
            Kms.Render.Primitives.TextBlock.DrawText(
                context, FormatAxis(value), new Point(0, y - 6), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 42, alignment: TextAlignment.Right);
        }

        int labelStep = _hourly ? 3 : Math.Max(1, _values.Length / 7);
        for (int i = 0; i < geometry.Bars.Count; i += labelStep)
        {
            if (i >= _labels.Length)
            {
                continue;
            }

            BarGeometry bar = geometry.Bars[i];
            Kms.Render.Primitives.TextBlock.DrawText(
                context, _labels[i], new Point(bar.Slot.Center.X - 10, plot.Bottom + 10), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 30);
        }

        Kms.Render.Primitives.TextBlock.DrawText(
            context, UnitName(), new Point(plot.Right - 40, 0), 9, PageKit.Brush(tone),
            Kms.Render.Primitives.TextBlock.DefaultFamily);
    }

    private string UnitName() => _hourly
        ? _metric == 2 ? "活跃分钟" : _metric == 1 ? "点击" : "击键"
        : _metric switch { 3 => "APM", 2 => "滚轮", 1 => "点击", _ => "击键" };

    private static string FormatAxis(double value) =>
        value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}

internal sealed class TrendCumulativeChart : Control
{
    private double[] _values = [];
    private string[] _labels = [];
    private int _metric;
    private bool _hourly;

    public void SetData(double[] values, string[] labels, int metric, bool hourly)
    {
        _values = values;
        _labels = labels;
        _metric = metric;
        _hourly = hourly;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        Rect plot = new(46, 12, Math.Max(1, Bounds.Width - 58), Math.Max(1, Bounds.Height - 42));
        if (_values.Length == 0)
        {
            return;
        }

        Color tone = _metric switch { 1 => theme.Green, 2 => theme.Purple, 3 => theme.Orange, _ => theme.Accent };
        double[] cumulative = new double[_values.Length];
        double sum = 0;
        int last = -1;
        for (int i = 0; i < _values.Length; i++)
        {
            if (double.IsFinite(_values[i]))
            {
                sum += Math.Max(0, _values[i]);
                last = i;
            }

            cumulative[i] = double.IsFinite(_values[i]) ? sum : double.NaN;
        }

        if (last < 0)
        {
            return;
        }

        double max = Math.Max(1, cumulative[last]);
        double xStep = _values.Length <= 1 ? 0 : plot.Width / (_values.Length - 1);
        Point[] points = new Point[last + 1];
        for (int i = 0; i <= last; i++)
        {
            double value = double.IsFinite(cumulative[i]) ? cumulative[i] : sum;
            points[i] = new Point(
                plot.Left + xStep * i,
                plot.Bottom - Math.Clamp(value / max, 0, 1) * plot.Height);
        }

        if (points.Length >= 2)
        {
            StreamGeometry area = new();
            using (StreamGeometryContext path = area.Open())
            {
                path.BeginFigure(new Point(points[0].X, plot.Bottom), true);
                path.LineTo(points[0]);
                for (int i = 1; i < points.Length; i++)
                {
                    path.LineTo(new Point(points[i].X, points[i - 1].Y));
                    path.LineTo(points[i]);
                }

                path.LineTo(new Point(points[^1].X, plot.Bottom));
                path.EndFigure(true);
            }

            context.DrawGeometry(PageKit.Gradient(PageKit.Alpha(tone, 0.30), PageKit.Alpha(tone, 0.01), 90), null, area);

            StreamGeometry line = new();
            using (StreamGeometryContext path = line.Open())
            {
                path.BeginFigure(points[0], false);
                for (int i = 1; i < points.Length; i++)
                {
                    path.LineTo(new Point(points[i].X, points[i - 1].Y));
                    path.LineTo(points[i]);
                }

                path.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(PageKit.Brush(tone), 2), line);
            context.DrawEllipse(PageKit.Brush(theme.Orange), null, points[^1], 3.2, 3.2);
        }

        IBrush muted = PageKit.Brush(theme.Muted);
        for (int tick = 0; tick <= 4; tick++)
        {
            double value = max * tick / 4.0;
            double y = plot.Bottom - value / max * plot.Height;
            context.DrawLine(new Pen(PageKit.Brush(PageKit.Alpha(theme.Line, 0.35)), 1),
                new Point(plot.Left, y), new Point(plot.Right, y));
            Kms.Render.Primitives.TextBlock.DrawText(
                context, FormatAxis(value), new Point(0, y - 6), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 42, alignment: TextAlignment.Right);
        }

        int labelStep = _hourly ? 3 : Math.Max(1, _values.Length / 7);
        for (int i = 0; i < _values.Length; i += labelStep)
        {
            if (i >= _labels.Length)
            {
                continue;
            }

            double x = plot.Left + xStep * i;
            Kms.Render.Primitives.TextBlock.DrawText(
                context, _labels[i], new Point(x - 10, plot.Bottom + 10), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 30);
        }

        Kms.Render.Primitives.TextBlock.DrawText(
            context, "累计", new Point(plot.Right - 30, 0), 9, PageKit.Brush(tone),
            Kms.Render.Primitives.TextBlock.DefaultFamily);
    }

    private static string FormatAxis(double value) =>
        value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}

internal sealed class TrendDetailView : UserControl
{
    private readonly TrendsWidgetModel _model;
    private readonly PageKit.PagedList _list = new(5, 4);

    public TrendDetailView(TrendsWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) =>
        {
            int size = Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 42), 3, 12) : 5;
            _list.SetPageSize(size);
            Build();
        };
    }

    private void Build()
    {
        (double[] values, _, DateTime[] stamps) = _model.RangeValues(_model.Metric);
        double max = Math.Max(1, values.Where(double.IsFinite).DefaultIfEmpty(0).Max());
        List<Control> rows = [];
        if (_model.Hourly)
        {
            bool actualToday = _model.Scope.End == DateTime.Today;
            int last = actualToday ? DateTime.Now.Hour : 23;
            for (int hour = Math.Min(last, values.Length - 1); hour >= 0; hour--)
            {
                if (!double.IsFinite(values[hour]))
                {
                    continue;
                }

                double previous = hour > 0 ? values[hour - 1] : double.NaN;
                rows.Add(HourRow(hour, values[hour], values[hour] / max,
                    Delta(values[hour], previous), actualToday && hour == DateTime.Now.Hour,
                    hour == Array.IndexOf(values, max)));
            }
        }
        else
        {
            for (int i = values.Length - 1; i >= 0; i--)
            {
                DateTime date = stamps[i];
                double previous = i > 0 ? values[i - 1] : double.NaN;
                rows.Add(DayRow(date, values[i], values[i] / max,
                    Delta(values[i], previous), date.Date == DateTime.Today,
                    i == Array.IndexOf(values, max)));
            }
        }

        _list.SetItems(rows);
    }

    private static double Delta(double value, double previous) =>
        double.IsFinite(previous) && previous > 0 ? (value - previous) * 100.0 / previous : double.NaN;

    private Control HourRow(int hour, double value, double fraction, double delta, bool current, bool peak)
    {
        ThemeTokens theme = PageKit.T;
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("52,Auto,*,Auto"), ColumnSpacing = 6 };
        StackPanel hourStack = new();
        hourStack.Children.Add(PageKit.Label(current ? "当前" : $"{hour:00}:00", 10.5,
            current ? theme.Orange : theme.Text, FontWeight.SemiBold));
        hourStack.Children.Add(PageKit.Muted($"{hour:00} – {hour + 1:00}", 8));
        row.Children.Add(hourStack);
        row.Children.Add(Dot(peak, 1));
        row.Children.Add(Bar(fraction, peak, 2));
        row.Children.Add(Value(value, delta, 3));
        return row;
    }

    private Control DayRow(DateTime date, double value, double fraction, double delta, bool today, bool peak)
    {
        ThemeTokens theme = PageKit.T;
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("56,Auto,*,Auto"), ColumnSpacing = 6 };
        StackPanel dateStack = new();
        dateStack.Children.Add(PageKit.Label(today ? "今天" : date.ToString("MM-dd", CultureInfo.InvariantCulture),
            10.5, today ? theme.Accent : theme.Text, FontWeight.SemiBold));
        dateStack.Children.Add(PageKit.Muted(TrendsWidgetModel.Weekday(date), 8));
        row.Children.Add(dateStack);
        row.Children.Add(Dot(peak, 1));
        row.Children.Add(Bar(fraction, peak, 2));
        row.Children.Add(Value(value, delta, 3));
        return row;
    }

    private static Control Dot(bool peak, int column)
    {
        Border dot = new()
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = PageKit.Brush(peak ? PageKit.T.Orange : PageKit.Alpha(PageKit.T.Accent, 0.5)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dot, column);
        return dot;
    }

    private static Control Bar(double fraction, bool peak, int column)
    {
        Grid area = new() { Margin = new Thickness(2, 9, 6, 9) };
        area.Children.Add(new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.22))
        });
        area.Children.Add(new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(2, 110 * fraction),
            Background = PageKit.Gradient(PageKit.Alpha(PageKit.T.Accent, 0.45),
                peak ? PageKit.T.Orange : PageKit.T.Accent, 0)
        });
        Grid.SetColumn(area, column);
        return area;
    }

    private static Control Value(double value, double delta, int column)
    {
        StackPanel stack = new() { HorizontalAlignment = HorizontalAlignment.Right };
        stack.Children.Add(PageKit.Label(value >= 1000
                ? (value / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + "k"
                : value.ToString(value < 10 ? "0.##" : "0", CultureInfo.InvariantCulture),
            10.5, PageKit.T.Text, FontWeight.SemiBold));
        stack.Children.Add(PageKit.Label(double.IsNaN(delta) ? "--" : $"{(delta >= 0 ? "+" : "")}{delta:0.#}%",
            8.5, double.IsNaN(delta) ? PageKit.T.Muted : delta >= 0 ? PageKit.T.Green : PageKit.T.Red));
        Grid.SetColumn(stack, column);
        return stack;
    }
}

internal sealed class TrendRhythmView : UserControl
{
    private readonly TrendsWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 6 };

    public TrendRhythmView(TrendsWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        double height = Math.Clamp(Bounds.Height - (Bounds.Height is > 0 and < 120 ? 70 : 42), 30, 260);
        if (_model.Hourly)
        {
            DayRecord? today = _model.ScopeDay();
            double[] hourKeys = today?.HourKeys.Select(value => (double)value).ToArray() ?? new double[24];
            _body.Children.Add(new PageKit.HourStrip(hourKeys, PageKit.T.Accent) { Height = height });
            _body.Children.Add(PageKit.Muted("00  03  06  09  12  15  18  21", 8));
            if (Bounds.Height >= 120)
            {
                int peak = 0;
                for (int hour = 1; hour < 24; hour++)
                {
                    if (hourKeys[hour] > hourKeys[peak])
                    {
                        peak = hour;
                    }
                }

                _body.Children.Add(PageKit.StatRow("峰值小时", $"{peak:00}:00", PageKit.T.Orange));
                _body.Children.Add(PageKit.StatRow("活跃小时", $"{hourKeys.Count(value => value > 0)} / 24", PageKit.T.Purple));
            }

            return;
        }

        (double[] values, _, DateTime[] stamps) = _model.RangeValues(_model.Metric);
        double[] sums = new double[7];
        int[] counts = new int[7];
        for (int i = 0; i < values.Length && i < stamps.Length; i++)
        {
            if (!double.IsFinite(values[i]))
            {
                continue;
            }

            int day = ((int)stamps[i].DayOfWeek + 6) % 7;
            sums[day] += values[i];
            counts[day]++;
        }

        double[] averages = new double[7];
        double max = 1;
        for (int i = 0; i < 7; i++)
        {
            averages[i] = counts[i] == 0 ? 0 : sums[i] / counts[i];
            max = Math.Max(max, averages[i]);
        }

        _body.Children.Add(new WeekdayBars(averages, max) { Height = height });
        _body.Children.Add(PageKit.Legend(
            (PageKit.T.Accent, "工作日"),
            (PageKit.T.Purple, "周末")));
    }
}

internal sealed class TrendInsightView : UserControl
{
    private readonly TrendsWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 4 };

    public TrendInsightView(TrendsWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
    }

    private void Build()
    {
        var (values, _, stamps) = _model.RangeValues(_model.Metric);
        double[] finite = values.Where(double.IsFinite).ToArray();
        double total = finite.Sum();
        double average = finite.Length == 0 ? 0 : finite.Average();
        double max = finite.Length == 0 ? 0 : finite.Max();
        int maxIndex = Array.IndexOf(values, max);
        double previous = _model.PreviousTotal(_model.Metric);
        double delta = previous > 0 ? (total - previous) * 100.0 / previous : double.NaN;
        int active = values.Count(value => double.IsFinite(value) && value > 0);
        double median = TrendsWidgetModel.Median(values);
        double consistency = finite.Length == 0 ? 0 : active * 100.0 / finite.Length;

        if (_model.Hourly)
        {
            _body.Children.Add(PageKit.StatRow("活跃小时", $"{active} / {finite.Length}", PageKit.T.Accent));
            _body.Children.Add(PageKit.StatRow("峰值小时",
                maxIndex < 0 ? "--" : $"{maxIndex:00}:00 · {_model.FormatValue(max, _model.Metric)}", PageKit.T.Orange));
            _body.Children.Add(PageKit.StatRow("均值 / 中位",
                $"{_model.FormatValue(average, _model.Metric)} / {_model.FormatValue(median, _model.Metric)}", PageKit.T.Green));
            _body.Children.Add(PageKit.StatRow("较昨日同期",
                double.IsNaN(delta) ? "--" : $"{(delta >= 0 ? "+" : "")}{delta:0.#}%",
                delta >= 0 ? PageKit.T.Green : PageKit.T.Red));
            _body.Children.Add(PageKit.Divider());
            _body.Children.Add(PageKit.Muted(consistency >= 60
                ? "今日节奏已铺开：继续观察下午与晚间的高峰是否稳定。"
                : "今日记录还不多：先在常用应用里工作一会儿，逐时曲线会自动补齐。", 10));
            return;
        }

        string best = maxIndex >= 0 && maxIndex < stamps.Length
            ? $"{stamps[maxIndex]:MM-dd} · {_model.FormatValue(max, _model.Metric)}"
            : "--";
        _body.Children.Add(PageKit.StatRow("活跃天", $"{active} / {values.Length} 天", PageKit.T.Accent));
        _body.Children.Add(PageKit.StatRow("峰值日", best, PageKit.T.Orange));
        _body.Children.Add(PageKit.StatRow("日均 / 中位",
            $"{_model.FormatValue(average, _model.Metric)} / {_model.FormatValue(median, _model.Metric)}", PageKit.T.Green));
        _body.Children.Add(PageKit.StatRow("较上期",
            double.IsNaN(delta) ? "--" : $"{(delta >= 0 ? "+" : "")}{delta:0.#}%",
            delta >= 0 ? PageKit.T.Green : PageKit.T.Red));
        _body.Children.Add(PageKit.Divider());
        _body.Children.Add(PageKit.Muted(consistency >= 80
            ? "输出很稳定：大多数日子都有输入记录，适合按日均制定目标。"
            : consistency >= 40
                ? "节奏有起伏：工作日和周末差异明显，可以观察峰值日发生了什么。"
                : "记录仍偏稀疏：继续挂机几天，趋势会更有参考价值。", 10));
    }
}

internal sealed class AreaTrendChart : Control
{
    private double[] _values = [];
    private string[] _labels = [];
    private DateTime[] _stamps = [];
    private int _metric;
    private bool _hourly;

    public void SetData(double[] values, string[] labels, DateTime[] stamps, int metric, bool hourly)
    {
        _values = values;
        _labels = labels;
        _stamps = stamps;
        _metric = metric;
        _hourly = hourly;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        Rect plot = new(10, 12, Math.Max(1, Bounds.Width - 24), Math.Max(1, Bounds.Height - 42));
        if (_values.Length == 0)
        {
            return;
        }

        Color tone = MetricTone(theme);
        double max = _hourly && _metric == 2
            ? 60
            : Math.Max(1, _values.Where(double.IsFinite).DefaultIfEmpty(0).Max());
        double xStep = _values.Length <= 1 ? 0 : plot.Width / (_values.Length - 1);
        Point?[] points = new Point?[_values.Length];
        for (int i = 0; i < _values.Length; i++)
        {
            if (!double.IsFinite(_values[i]))
            {
                continue;
            }

            double x = plot.Left + xStep * i;
            double y = plot.Bottom - Math.Clamp(_values[i] / max, 0, 1) * plot.Height;
            points[i] = new Point(x, y);
        }

        IBrush grid = PageKit.Brush(PageKit.Alpha(theme.Line, 0.35));
        IBrush muted = PageKit.Brush(theme.Muted);
        for (int tick = 0; tick <= 4; tick++)
        {
            double value = max * tick / 4.0;
            double y = plot.Bottom - plot.Height * tick / 4.0;
            context.DrawLine(new Pen(grid, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            Kms.Render.Primitives.TextBlock.DrawText(
                context, FormatAxis(value), new Point(0, y - 6), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 42, alignment: TextAlignment.Right);
        }

        DrawRuns(context, points, plot, tone);

        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] is not { } point)
            {
                continue;
            }

            bool current = _stamps.Length > i
                && (_hourly
                    ? _stamps[i].Hour == DateTime.Now.Hour && _stamps[i].Date == DateTime.Today
                    : _stamps[i].Date == DateTime.Today);
            context.DrawEllipse(PageKit.Brush(current ? theme.Orange : tone), null, point,
                current ? 3.4 : 2.2, current ? 3.4 : 2.2);
        }

        int labelStep = _hourly ? 3 : Math.Max(1, _values.Length / 7);
        for (int i = 0; i < _values.Length; i += labelStep)
        {
            if (i >= _labels.Length || points[i] is not { } point)
            {
                continue;
            }

            Kms.Render.Primitives.TextBlock.DrawText(
                context, _labels[i], new Point(point.X - 10, plot.Bottom + 10), 9, muted,
                Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 30);
        }

        string unit = _hourly
            ? _metric == 2 ? "活跃分钟" : _metric == 1 ? "点击" : "击键"
            : _metric switch { 3 => "APM", 2 => "滚轮", 1 => "点击", _ => "击键" };
        Kms.Render.Primitives.TextBlock.DrawText(
            context, unit, new Point(plot.Right - 40, 0), 9, PageKit.Brush(tone),
            Kms.Render.Primitives.TextBlock.DefaultFamily);
    }

    private Color MetricTone(ThemeTokens theme) => _metric switch
    {
        1 => theme.Green,
        2 => theme.Purple,
        3 => theme.Orange,
        _ => theme.Accent
    };

    private static void DrawRuns(DrawingContext context, Point?[] points, Rect plot, Color tone)
    {
        int index = 0;
        while (index < points.Length)
        {
            while (index < points.Length && points[index] is null)
            {
                index++;
            }

            int start = index;
            while (index < points.Length && points[index] is not null)
            {
                index++;
            }

            int end = index - 1;
            if (end <= start)
            {
                continue;
            }

            List<Point> run = [];
            for (int i = start; i <= end; i++)
            {
                if (points[i] is { } point)
                {
                    run.Add(point);
                }
            }

            if (run.Count < 2)
            {
                continue;
            }

            StreamGeometry area = new();
            using (StreamGeometryContext path = area.Open())
            {
                path.BeginFigure(new Point(run[0].X, plot.Bottom), true);
                foreach (Point point in run)
                {
                    path.LineTo(point);
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
                    path.LineTo(run[i], true);
                }

                path.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(PageKit.Brush(tone), 2), line);
        }
    }

    private static string FormatAxis(double value) =>
        value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : value.ToString("0.##", CultureInfo.InvariantCulture);
}

internal sealed class WeekdayBars : Control
{
    private readonly double[] _values;
    private readonly double _max;

    public WeekdayBars(double[] values, double max)
    {
        _values = values;
        _max = Math.Max(1, max);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        string[] labels = ["一", "二", "三", "四", "五", "六", "日"];
        double gap = 16;
        double width = Math.Max(8, (Bounds.Width - gap * 6) / 7);
        for (int i = 0; i < 7; i++)
        {
            double x = i * (width + gap);
            double height = Math.Max(3, (Bounds.Height - 22) * _values[i] / _max);
            Color tone = i >= 5 ? theme.Purple : theme.Accent;
            context.FillRectangle(PageKit.Brush(PageKit.Alpha(tone, 0.22)),
                new Rect(x, 0, width, Math.Max(3, Bounds.Height - 22)), 4);
            context.FillRectangle(PageKit.Gradient(PageKit.Alpha(tone, 0.45), tone, 90),
                new Rect(x, Bounds.Height - 22 - height, width, height), 4);
            Kms.Render.Primitives.TextBlock.DrawText(
                context, labels[i], new Point(x + width / 2 - 5, Bounds.Height - 17),
                8, PageKit.Brush(theme.Muted), Kms.Render.Primitives.TextBlock.DefaultFamily, maxWidth: 12);
        }
    }
}
