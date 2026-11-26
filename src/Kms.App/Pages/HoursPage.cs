using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>小时分布：24 小时柱、可点选小时详情、Top 小时与活动带。</summary>
public sealed class HoursPage : UserControl
{
    private readonly StatsData _data;
    private DayRecord? _record;
    private DateTime _day;
    private readonly DateTime _minDate;
    private readonly DateTime _maxDate;
    private readonly HoursChart _chart;
    private readonly StackPanel _summary = new();
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _detail = new() { Spacing = 8 };
    private readonly PageKit.PagedList _topHours = new(4, 4);
    private readonly ContentControl _dateNav = new();
    private readonly ContentControl _rhythmHost = new();
    private readonly Avalonia.Controls.TextBlock _caption;
    private int _metric;
    private int _selectedHour = -1;

    public HoursPage(StatsData data, string title, string subtitle)
    {
        _data = data;
        _record = ResolveDay(data);
        _day = _record?.Date ?? DateTime.Today;
        _minDate = data.History.Count == 0 ? _day : data.History.Values.Min(day => day.Date);
        _maxDate = data.History.Count == 0 ? _day : data.History.Values.Max(day => day.Date);
        _chart = new HoursChart { Height = 165 };
        _chart.HourSelected += hour =>
        {
            _selectedHour = hour;
            UpdateDetail();
        };
        _caption = PageKit.Muted("纵轴单位随指标切换；今天未结束的小时不计。", 10);
        _dateNav.Content = BuildDateNav();

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,1.35*,1*"),
            RowSpacing = 10
        };
        root.Children.Add(PageKit.PageHeader(
            "DAY / 24 HOURS",
            title,
            subtitle,
            null,
            _dateNav));

        Grid.SetRow(_summary, 1);
        root.Children.Add(_summary);
        Grid.SetRow(_filters, 2);
        root.Children.Add(_filters);

        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.6*,1*"),
            ColumnSpacing = 14
        };
        main.Children.Add(PageKit.Section(
            "24 小时柱状",
            "点击柱子查看该小时详情",
            new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    _chart,
                    _caption
                }
            },
            PageKit.Badge("HOUR", PageKit.T.Accent)));

        Control detail = PageKit.Section(
            "小时详情",
            "点击左侧任意小时",
            _detail,
            PageKit.Badge("DETAIL", PageKit.T.Cyan),
            PageKit.T.Cyan);
        Grid.SetColumn(detail, 1);
        main.Children.Add(detail);
        Grid.SetRow(main, 3);
        root.Children.Add(main);

        Grid bottom = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.3*,1*"),
            ColumnSpacing = 14
        };
        bottom.Children.Add(PageKit.Section(
            "Top 小时",
            "当前指标下最活跃的小时 · 局部翻页",
            _topHours,
            PageKit.Badge("RANK", PageKit.T.Green),
            PageKit.T.Green));

        Grid.SetColumn(_rhythmHost, 1);
        bottom.Children.Add(_rhythmHost);
        Grid.SetRow(bottom, 4);
        root.Children.Add(bottom);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        UpdateChart(0);
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        _chart.Height = compact ? 145 : 165;
        _topHours.SetPageSize(compact ? 2 : 3);
    }

    private Control BuildDateNav() =>
        PageKit.DateNav(_day, _minDate, _maxDate, SetDay);

    private Control BuildRhythmCard()
    {
        StackPanel rhythm = new() { Spacing = 4 };
        rhythm.Children.Add(new PageKit.HourStrip((_record?.ActiveHours ?? new double[24]).Select(value => value / 60.0).ToArray(), PageKit.T.Purple)
        {
            Height = 32
        });
        rhythm.Children.Add(PageKit.Muted("00  03  06  09  12  15  18  21", 8));
        rhythm.Children.Add(PageKit.StatRow("活跃小时数", $"{(_record?.ActiveHours ?? new double[24]).Count(value => value > 0)} / 24", PageKit.T.Purple));
        rhythm.Children.Add(PageKit.StatRow("全天活跃", PageKit.FormatDuration(_record?.ActiveSeconds ?? 0), PageKit.T.Cyan));
        return PageKit.Section(
            "活动带",
            "每格代表 1 小时 · 按活跃分钟着色",
            rhythm,
            PageKit.Badge("RHYTHM", PageKit.T.Purple),
            PageKit.T.Purple);
    }

    private void SetDay(DateTime date)
    {
        _day = date.Date;
        _record = _data.History.TryGetValue(_day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out DayRecord? day)
            ? day
            : null;
        _selectedHour = -1;
        _dateNav.Content = BuildDateNav();
        UpdateChart(_metric);
    }

    private void UpdateChart(int metric)
    {
        _metric = metric;
        _data.History.TryGetValue(_day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out DayRecord? day);
        double[] values = HourlySeries.Values(day, _day, metric, DateTime.Now);
        double max = values.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        HourlyAxis.Compute(metric, max, out double top, out double step);
        _chart.SetData(values, metric, top, step);
        _caption.Text = $"纵轴单位：{HourlyAxis.Unit(metric).Trim()}；今天未结束的小时不计。";
        _selectedHour = -1;
        UpdateFilters();
        UpdateSummary(day, values);
        UpdateTopHours(day, values);
        _rhythmHost.Content = BuildRhythmCard();
        UpdateDetail();
    }

    private void UpdateFilters()
    {
        _filters.Children.Clear();
        string[] labels = ["击键", "点击", "活跃分钟"];
        for (int i = 0; i < labels.Length; i++)
        {
            int selected = i;
            _filters.Children.Add(PageKit.Filter(labels[i], _metric == i, () => UpdateChart(selected),
                i switch { 1 => PageKit.T.Green, 2 => PageKit.T.Purple, _ => PageKit.T.Accent }));
        }

        _filters.Children.Add(new Border { Width = 8 });
        _filters.Children.Add(PageKit.Muted(
            _record is null
                ? $"{_day:yyyy-MM-dd}（无记录）"
                : _day.Date == DateTime.Today ? "今天" : $"{_day:yyyy-MM-dd}",
            10));
    }

    private void UpdateSummary(DayRecord? day, double[] values)
    {
        _summary.Children.Clear();
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

        int activeHours = (_record?.ActiveHours ?? new double[24]).Count(value => value > 0);
        _summary.Children.Add(PageKit.StatStrip(
            ("当日击键", PageKit.FormatCount(day?.Keys ?? 0), PageKit.T.Accent),
            ("点击", PageKit.FormatCount(day?.Clicks ?? 0), PageKit.T.Green),
            ("峰值小时", peak >= 0 ? $"{peak:00}:00 · {FormatValue(peakValue, _metric)}" : "--", PageKit.T.Orange),
            ("活跃跨度", $"{activeHours} 小时", PageKit.T.Purple),
            ("活跃时长", PageKit.FormatDuration(day?.ActiveSeconds ?? 0), PageKit.T.Cyan)));
    }

    private void UpdateTopHours(DayRecord? day, double[] values)
    {
        List<Control> rows = [];
        long total = 0;
        for (int hour = 0; hour < 24; hour++)
        {
            if (double.IsFinite(values[hour]))
            {
                total += (long)values[hour];
            }
        }

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
                FormatValue(value, _metric),
                total <= 0 ? 0 : value / total * 1.8,
                rank switch
                {
                    2 => PageKit.T.Orange,
                    3 => PageKit.T.Accent,
                    4 => PageKit.T.Green,
                    5 => PageKit.T.Purple,
                    _ => PageKit.T.Cyan
                },
                MetricUnit()));
        }

        if (rows.Count == 0)
        {
            rows.Add(PageKit.Empty("该指标还没有小时数据"));
        }

        _topHours.SetItems(rows);
    }

    private void UpdateDetail()
    {
        _data.History.TryGetValue(_day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out DayRecord? day);
        double[] values = HourlySeries.Values(day, _day, _metric, DateTime.Now);

        _detail.Children.Clear();
        if (_selectedHour < 0)
        {
            _detail.Children.Add(new PageKit.Ring(
                [
                    new DonutSlice(Math.Max(0, day?.Keys ?? 0), PageKit.T.Accent),
                    new DonutSlice(Math.Max(0, day?.Clicks ?? 0), PageKit.T.Green),
                    new DonutSlice(Math.Max(0, day?.Wheel ?? 0), PageKit.T.Purple)
                ],
                PageKit.FormatCount((day?.Keys ?? 0) + (day?.Clicks ?? 0) + (day?.Wheel ?? 0)),
                "全天输入") { Height = 88 });
            _detail.Children.Add(PageKit.StatRow("击键", PageKit.FormatCount(day?.Keys ?? 0), PageKit.T.Accent));
            _detail.Children.Add(PageKit.StatRow("点击", PageKit.FormatCount(day?.Clicks ?? 0), PageKit.T.Green));
            _detail.Children.Add(PageKit.StatRow("活跃", PageKit.FormatDuration(day?.ActiveSeconds ?? 0), PageKit.T.Cyan));
            _detail.Children.Add(PageKit.Muted("点击柱子查看单个小时；当前为全天构成。", 9));
            return;
        }

        int selected = Math.Clamp(_selectedHour, 0, 23);
        double value = values[selected];
        long hourKeys = day?.HourKeys[selected] ?? 0;
        long hourClicks = day?.HourClicks[selected] ?? 0;
        double activeMinutes = day?.ActiveHours[selected] ?? 0;
        double share = day is null || day.Keys <= 0 ? 0 : hourKeys * 100.0 / day.Keys;

        _detail.Children.Add(PageKit.Label($"{selected:00}:00", 30, PageKit.T.Text, FontWeight.SemiBold));
        _detail.Children.Add(PageKit.Muted(double.IsFinite(value) ? $"{MetricName()} {FormatValue(value, _metric)}" : "该小时尚未结束", 10));
        _detail.Children.Add(PageKit.Divider());
        _detail.Children.Add(PageKit.StatRow("击键", PageKit.FormatCount(hourKeys), PageKit.T.Accent));
        _detail.Children.Add(PageKit.StatRow("点击", PageKit.FormatCount(hourClicks), PageKit.T.Green));
        _detail.Children.Add(PageKit.StatRow("活跃", $"{activeMinutes:0.#} 分钟", PageKit.T.Purple));
        _detail.Children.Add(PageKit.StatRow("占全天击键", $"{share:0.#}%", PageKit.T.Cyan));
        _detail.Children.Add(PageKit.Progress("小时强度", double.IsFinite(value) ? Math.Min(1, value / Math.Max(1, _chart.Top)) : 0, PageKit.T.Orange));
    }

    private string MetricName() => _metric switch
    {
        1 => "点击",
        2 => "活跃",
        _ => "击键"
    };

    private string MetricUnit() => _metric switch
    {
        1 => "击",
        2 => "分钟",
        _ => "键"
    };

    private static string FormatValue(double value, int metric)
    {
        if (metric == 2)
        {
            return $"{value:0.#}";
        }

        return PageKit.FormatCount((long)value);
    }

    private static DayRecord? ResolveDay(StatsData data)
    {
        DateTime today = DateTime.Today;
        string key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (data.History.TryGetValue(key, out DayRecord? todayRecord))
        {
            return todayRecord;
        }

        DayRecord? latest = null;
        foreach (DayRecord day in data.History.Values)
        {
            if (latest is null || day.Date > latest.Date)
            {
                latest = day;
            }
        }

        return latest;
    }

    private sealed class HoursChart : Control
    {
        private double[] _values = new double[24];
        private int _metric;
        private int _selected = -1;
        private double _top = 1;
        private double _step = 1;
        private BarChartGeometry? _geometry;
        private Point _lastPoint;

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

                _lastPoint = e.GetPosition(this);
                foreach (BarGeometry bar in _geometry.Bars)
                {
                    if (bar.Slot.Contains(_lastPoint))
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
            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            CardGeometry card = Card.Layout(bounds, theme, 14);
            Card.Draw(context, card, theme);

            Rect plot = new(
                card.Content.X + 44,
                card.Content.Y + 12,
                Math.Max(1, card.Content.Width - 58),
                Math.Max(1, card.Content.Height - 38));

            Color barColor = _metric switch
            {
                1 => theme.Green,
                2 => theme.Purple,
                _ => theme.Accent
            };

            List<BarDatum> bars = new(24);
            for (int hour = 0; hour < 24; hour++)
            {
                double value = double.IsFinite(_values[hour]) ? _values[hour] : 0;
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
                new BarChartOptions(Padding: 0, GapRatio: 0.24, IncludeZero: true, YTickCount: 4));
            _geometry = geometry;
            BarChart.Draw(context, geometry, theme, showBaseline: true, cornerRadius: 3);

            if (_selected >= 0 && _selected < geometry.Bars.Count)
            {
                BarGeometry bar = geometry.Bars[_selected];
                context.DrawRectangle(
                    PageKit.Brush(PageKit.Alpha(theme.Orange, 0.06)),
                    null,
                    new Rect(bar.Slot.Left - 1, plot.Top, bar.Slot.Width + 2, plot.Height),
                    4);
                context.DrawLine(
                    new Pen(PageKit.Brush(PageKit.Alpha(theme.Orange, 0.6)), 1),
                    new Point(bar.Slot.Center.X, plot.Top),
                    new Point(bar.Slot.Center.X, plot.Bottom));
            }

            IBrush muted = PageKit.Brush(theme.Muted);
            for (int tick = 0; tick <= 4; tick++)
            {
                double value = _step * tick;
                double y = plot.Bottom - value / _top * plot.Height;
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, FormatAxis(value), new Point(card.Content.X, y - 7), 9, muted,
                    Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 40,
                    alignment: TextAlignment.Right);
            }

            for (int hour = 0; hour < 24; hour += 3)
            {
                BarGeometry bar = geometry.Bars[hour];
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, hour.ToString("00", CultureInfo.InvariantCulture),
                    new Point(bar.Slot.Center.X - 8, plot.Bottom + 10), 9, muted,
                    Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 22);
            }

            Kms.Render.Primitives.TextBlock.DrawText(
                context,
                _metric == 2 ? "活跃分钟" : _metric == 1 ? "点击" : "击键",
                new Point(card.Content.Right - 40, card.Content.Y), 10, PageKit.Brush(barColor),
                Kms.Render.Primitives.TextBlock.DefaultFamily);
        }

        private static string FormatAxis(double value) =>
            value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
