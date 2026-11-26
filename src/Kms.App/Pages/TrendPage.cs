using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>趋势对比：7/14/30 日区间、四个指标、逐日明细与工作日节奏。</summary>
public sealed class TrendPage : UserControl
{
    private readonly StatsData _data;
    private readonly DateTime _end;
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _summary = new();
    private readonly PageKit.PagedList _rows = new(5, 4);
    private readonly StackPanel _weekdays = new();
    private readonly StackPanel _insight = new() { Spacing = 4 };
    private readonly AreaTrendChart _chart;
    private Avalonia.Controls.TextBlock? _insightNote;
    private int _range = 7;
    private int _metric;

    public TrendPage(StatsData data, string title, string subtitle)
    {
        _data = data;
        _end = ResolveEnd(data);
        _chart = new AreaTrendChart { Height = 180 };

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,1.35*,1*"),
            RowSpacing = 10
        };
        root.Children.Add(PageKit.PageHeader(
            "RANGE / COMPARE",
            title,
            subtitle,
            _end.ToString("yyyy-MM-dd")));

        Grid.SetRow(_filters, 1);
        root.Children.Add(_filters);
        Grid.SetRow(_summary, 2);
        root.Children.Add(_summary);

        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.55*,1*"),
            ColumnSpacing = 14
        };
        main.Children.Add(PageKit.Section(
            "趋势曲线",
            "区间内的日值走势 · 虚线为今日未结束段",
            _chart,
            PageKit.Badge("TREND", PageKit.T.Accent)));
        Control list = PageKit.Section(
            "逐日明细",
            "条形按区间峰值归一 · 局部翻页",
            _rows,
            PageKit.Badge("DAILY", PageKit.T.Green),
            PageKit.T.Green);
        Grid.SetColumn(list, 1);
        main.Children.Add(list);
        Grid.SetRow(main, 3);
        root.Children.Add(main);

        Grid bottom = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1*,1*"),
            ColumnSpacing = 14
        };
        bottom.Children.Add(PageKit.Section(
            "工作日节奏",
            "各星期均值 · 用于判断稳定输出还是周末冲刺",
            _weekdays,
            PageKit.Badge("WEEK", PageKit.T.Purple),
            PageKit.T.Purple));
        Control insight = PageKit.Section(
            "区间洞察",
            "自动从当前区间提取",
            _insight,
            PageKit.Badge("INSIGHT", PageKit.T.Cyan),
            PageKit.T.Cyan);
        Grid.SetColumn(insight, 1);
        bottom.Children.Add(insight);
        Grid.SetRow(bottom, 4);
        root.Children.Add(bottom);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        Refresh();
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        _chart.Height = compact ? 150 : 180;
        _rows.SetPageSize(compact ? 4 : 5);
        if (_insightNote is not null)
        {
            _insightNote.IsVisible = !compact;
        }
    }

    private void Refresh()
    {
        long[] values = RangeValues(out DateTime[] dates);
        long total = values.Sum();
        double average = values.Length == 0 ? 0 : values.Average();
        long max = values.Length == 0 ? 0 : values.Max();
        int maxIndex = Array.IndexOf(values, max);
        long previousTotal = PreviousTotal();
        double delta = previousTotal > 0 ? (total - previousTotal) * 100.0 / previousTotal : double.NaN;

        BuildFilters();

        _summary.Children.Clear();
        _summary.Children.Add(PageKit.StatStrip(
            ("区间总量", PageKit.FormatCount(total), PageKit.T.Accent),
            ("日均", FormatValue(average), PageKit.T.Green),
            ("峰值日", maxIndex >= 0 && values.Length > 0 ? $"{FormatValue(max)} · {dates[maxIndex]:MM-dd}" : "--", PageKit.T.Orange),
            ("较上期", double.IsNaN(delta) ? "--" : $"{(delta >= 0 ? "+" : "")}{delta:0.#}%", delta < 0 ? PageKit.T.Red : PageKit.T.Cyan),
            ("活跃天", $"{values.Count(value => value > 0)} / {values.Length}", PageKit.T.Purple)));

        _chart.SetData(values, dates, _metric);

        List<Control> rows = [];
        long maxValue = Math.Max(1, max);
        for (int i = values.Length - 1; i >= 0; i--)
        {
            DateTime date = dates[i];
            long previous = i > 0 ? values[i - 1] : 0;
            double rowDelta = previous > 0 ? (values[i] - previous) * 100.0 / previous : double.NaN;
            rows.Add(DayRow(
                date,
                values[i],
                values[i] / (double)maxValue,
                rowDelta,
                date.Date == DateTime.Today,
                i == maxIndex));
        }

        _rows.SetItems(rows);

        BuildWeekdays(values, dates);
        BuildInsight(values, dates, total, average, max, maxIndex, delta);

        _chart.InvalidateVisual();
    }

    private void BuildFilters()
    {
        _filters.Children.Clear();
        foreach ((string label, int range) in new[] { ("近 7 天", 7), ("近 14 天", 14), ("近 30 天", 30) })
        {
            int selected = range;
            _filters.Children.Add(PageKit.Filter(label, _range == range, () =>
            {
                _range = selected;
                Refresh();
            }, PageKit.T.Accent));
        }

        _filters.Children.Add(new Border { Width = 10 });

        string[] names = ["击键", "点击", "滚轮", "APM"];
        for (int i = 0; i < names.Length; i++)
        {
            int selected = i;
            _filters.Children.Add(PageKit.Filter(names[i], _metric == i, () =>
            {
                _metric = selected;
                Refresh();
            }, PageKit.T.Green));
        }

        _filters.Children.Add(new Border { Width = 4 });
        _filters.Children.Add(PageKit.Muted(_end == DateTime.Today ? "今天尚未结束" : $"截至 {_end:MM-dd}", 10));
    }

    private void BuildWeekdays(long[] values, DateTime[] dates)
    {
        double[] sums = new double[7];
        int[] counts = new int[7];
        for (int i = 0; i < values.Length && i < dates.Length; i++)
        {
            int day = ((int)dates[i].DayOfWeek + 6) % 7;
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

        _weekdays.Children.Clear();
        _weekdays.Children.Add(new WeekdayBars(averages, max) { Height = 86 });
        _weekdays.Children.Add(PageKit.Legend(
            (PageKit.T.Accent, "工作日"),
            (PageKit.T.Purple, "周末")));
    }

    private void BuildInsight(
        long[] values,
        DateTime[] dates,
        long total,
        double average,
        long max,
        int maxIndex,
        double delta)
    {
        _insight.Children.Clear();
        int activeDays = values.Count(value => value > 0);
        string best = maxIndex >= 0 && maxIndex < dates.Length
            ? $"{dates[maxIndex]:MM-dd} · {FormatValue(max)}"
            : "--";
        double median = Median(values);
        double consistency = values.Length == 0 ? 0 : activeDays * 100.0 / values.Length;

        _insight.Children.Add(PageKit.StatRow("活跃天", $"{activeDays} / {values.Length} 天", PageKit.T.Accent));
        _insight.Children.Add(PageKit.StatRow("峰值日", best, PageKit.T.Orange));
        _insight.Children.Add(PageKit.StatRow("日均 / 中位", $"{FormatValue(average)} / {FormatValue(median)}", PageKit.T.Green));
        _insight.Children.Add(PageKit.StatRow("较上期", double.IsNaN(delta) ? "--" : $"{(delta >= 0 ? "+" : "")}{delta:0.#}%",
            delta >= 0 ? PageKit.T.Green : PageKit.T.Red));
        _insight.Children.Add(PageKit.Divider());
        _insightNote = PageKit.Muted(
            consistency >= 80
                ? "输出很稳定：大多数日子都有输入记录，适合按日均制定目标。"
                : consistency >= 40
                    ? "节奏有起伏：工作日和周末差异明显，可以观察峰值日发生了什么。"
                    : "记录仍偏稀疏：继续挂机几天，趋势会更有参考价值。", 10);
        _insight.Children.Add(_insightNote);
    }

    private Control DayRow(DateTime date, long value, double fraction, double delta, bool today, bool peak)
    {
        ThemeTokens theme = PageKit.T;
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("64,Auto,*,Auto"),
            ColumnSpacing = 8
        };

        StackPanel dateStack = new() { Spacing = 0 };
        dateStack.Children.Add(PageKit.Label(today ? "今天" : date.ToString("MM-dd", CultureInfo.InvariantCulture), 11,
            today ? theme.Accent : theme.Text, FontWeight.SemiBold));
        dateStack.Children.Add(PageKit.Muted(Weekday(date), 8));
        row.Children.Add(dateStack);

        Border dot = new()
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = PageKit.Brush(peak ? theme.Orange : PageKit.Alpha(theme.Accent, 0.5)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dot, 1);
        row.Children.Add(dot);

        Grid barArea = new() { Margin = new Thickness(2, 9, 6, 9) };
        barArea.Children.Add(new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = PageKit.Brush(PageKit.Alpha(theme.Line, 0.22))
        });
        barArea.Children.Add(new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(2, 120 * fraction),
            Background = PageKit.Gradient(PageKit.Alpha(theme.Accent, 0.45), peak ? theme.Orange : theme.Accent, 0)
        });
        Grid.SetColumn(barArea, 2);
        row.Children.Add(barArea);

        StackPanel valueStack = new() { HorizontalAlignment = HorizontalAlignment.Right, Spacing = 0 };
        valueStack.Children.Add(PageKit.Label(FormatValue(value), 11, theme.Text, FontWeight.SemiBold));
        valueStack.Children.Add(PageKit.Label(double.IsNaN(delta) ? "--" : $"{(delta >= 0 ? "+" : "")}{delta:0.#}%", 8,
            double.IsNaN(delta) ? theme.Muted : delta >= 0 ? theme.Green : theme.Red));
        Grid.SetColumn(valueStack, 3);
        row.Children.Add(valueStack);

        return row;
    }

    private long[] RangeValues(out DateTime[] dates)
    {
        int count = _range;
        dates = new DateTime[count];
        long[] values = new long[count];
        for (int i = 0; i < count; i++)
        {
            DateTime date = _end.AddDays(i - count + 1);
            dates[i] = date;
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_data.History.TryGetValue(key, out DayRecord? day))
            {
                values[i] = (long)MetricValue(day, _metric);
            }
        }

        return values;
    }

    private long PreviousTotal()
    {
        long total = 0;
        for (int i = 0; i < _range; i++)
        {
            DateTime date = _end.AddDays(-_range - i);
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_data.History.TryGetValue(key, out DayRecord? day))
            {
                total += (long)MetricValue(day, _metric);
            }
        }

        return total;
    }

    private static double MetricValue(DayRecord day, int metric) => metric switch
    {
        1 => day.Clicks,
        2 => day.Wheel,
        3 => day.PeakApm,
        _ => day.Keys
    };

    private static string MetricUnit(int metric) => metric switch
    {
        3 => "APM",
        2 => "格",
        1 => "次",
        _ => "键"
    };

    private static string FormatValue(double value) => value >= 1000
        ? (value / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + "k"
        : value.ToString(value < 10 ? "0.##" : "0", CultureInfo.InvariantCulture);

    private static double Median(long[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        long[] sorted = values.OrderBy(value => value).ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2.0 : sorted[middle];
    }

    private static string Weekday(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    private static DateTime ResolveEnd(StatsData data)
    {
        DateTime today = DateTime.Today;
        string key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (data.History.ContainsKey(key))
        {
            return today;
        }

        DateTime latest = today;
        foreach (DayRecord day in data.History.Values)
        {
            if (day.Date > latest)
            {
                latest = day.Date;
            }
        }

        return latest;
    }

    private sealed class AreaTrendChart : Control
    {
        private long[] _values = [];
        private DateTime[] _dates = [];
        private int _metric;

        public void SetData(long[] values, DateTime[] dates, int metric)
        {
            _values = values;
            _dates = dates;
            _metric = metric;
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

            Color tone = _metric switch
            {
                1 => theme.Green,
                2 => theme.Purple,
                3 => theme.Orange,
                _ => theme.Accent
            };

            double max = Math.Max(1, _values.Max());
            double min = 0;
            double xStep = _values.Length == 1 ? 0 : plot.Width / (_values.Length - 1);
            Point[] points = new Point[_values.Length];
            for (int i = 0; i < _values.Length; i++)
            {
                double x = plot.Left + xStep * i;
                double y = plot.Bottom - (_values[i] - min) / (max - min) * plot.Height;
                points[i] = new Point(x, y);
            }

            // 横向网格与刻度
            IBrush grid = PageKit.Brush(PageKit.Alpha(theme.Line, 0.35));
            IBrush muted = PageKit.Brush(theme.Muted);
            for (int tick = 0; tick <= 4; tick++)
            {
                double value = max * tick / 4.0;
                double y = plot.Bottom - plot.Height * tick / 4.0;
                context.DrawLine(new Pen(grid, 1), new Point(plot.Left, y), new Point(plot.Right, y));
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, FormatAxis(value), new Point(0, y - 6), 8, muted,
                    Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 42,
                    alignment: TextAlignment.Right);
            }

            StreamGeometry area = new();
            using (StreamGeometryContext path = area.Open())
            {
                path.BeginFigure(new Point(points[0].X, plot.Bottom), true);
                foreach (Point point in points)
                {
                    path.LineTo(point);
                }

                path.LineTo(new Point(points[^1].X, plot.Bottom));
                path.EndFigure(true);
            }

            context.DrawGeometry(PageKit.Gradient(PageKit.Alpha(tone, 0.32), PageKit.Alpha(tone, 0.01), 90), null, area);

            StreamGeometry line = new();
            using (StreamGeometryContext path = line.Open())
            {
                path.BeginFigure(points[0], false);
                for (int i = 1; i < points.Length; i++)
                {
                    path.LineTo(points[i], true);
                }

                path.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(PageKit.Brush(tone), 2), line);
            for (int i = 0; i < points.Length; i++)
            {
                bool today = _dates.Length > i && _dates[i].Date == DateTime.Today;
                context.DrawEllipse(PageKit.Brush(today ? theme.Orange : tone), null, points[i], today ? 3.4 : 2.2, today ? 3.4 : 2.2);
            }

            int labelStep = Math.Max(1, _values.Length / 7);
            for (int i = 0; i < _values.Length; i += labelStep)
            {
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, _dates[i].ToString("MM/dd", CultureInfo.InvariantCulture),
                    new Point(points[i].X - 13, plot.Bottom + 10), 8, muted,
                    Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 36);
            }

            string unit = _metric switch { 3 => "APM", 2 => "滚轮", 1 => "点击", _ => "击键" };
            Kms.Render.Primitives.TextBlock.DrawText(
                context, unit, new Point(plot.Right - 30, 0), 9, PageKit.Brush(tone),
                Kms.Render.Primitives.TextBlock.DefaultFamily);
        }

        private static string FormatAxis(double value) =>
            value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : value.ToString("0", CultureInfo.InvariantCulture);
    }

    private sealed class WeekdayBars : Control
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
                context.FillRectangle(
                    PageKit.Brush(PageKit.Alpha(tone, 0.22)),
                    new Rect(x, 0, width, Math.Max(3, Bounds.Height - 22)),
                    4);
                context.FillRectangle(
                    PageKit.Gradient(PageKit.Alpha(tone, 0.45), tone, 90),
                    new Rect(x, Bounds.Height - 22 - height, width, height),
                    4);
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, labels[i], new Point(x + width / 2 - 5, Bounds.Height - 17),
                    8, PageKit.Brush(theme.Muted), Kms.Render.Primitives.TextBlock.DefaultFamily, maxWidth: 12);
            }
        }
    }
}
