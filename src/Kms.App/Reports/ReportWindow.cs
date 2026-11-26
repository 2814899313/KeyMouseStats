using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Render.Theming;
using Kms.Render.Tokens;
using AvaloniaText = Avalonia.Controls.TextBlock;

namespace Kms.App.Reports;

/// <summary>M6 / T6.1–T6.2 报表窗首版：5 个纯数据页。</summary>
internal sealed class ReportWindow : Window
{
    private readonly StatsData _data;
    private readonly ThemeTokens _theme = ThemeManager.Current;
    private readonly Border _help;
    private readonly CheckBox _desensitize;
    private readonly TabControl _tabs;

    public ReportWindow(StatsData data)
    {
        _data = data;
        Title = "键鼠统计 · 报表";
        Icon = AppIcons.Load();
        Width = 940;
        Height = 640;
        Background = new SolidColorBrush(_theme.Background);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _tabs = new TabControl
        {
            Margin = new Thickness(18),
            Items =
            {
                Tab("个人指标", Personal()),
                Tab("区间回顾", Range()),
                Tab("分布", Distribution()),
                Tab("交叉", Cross()),
                Tab("按住趋势", Hold())
            }
        };

        Button week = ExportButton("导出周报海报");
        week.Click += async (_, _) => await ExportPeriod(7, "键鼠统计周报");
        Button month = ExportButton("导出月报海报");
        month.Click += async (_, _) => await ExportPeriod(30, "键鼠统计月报");
        Button battle = ExportButton("导出战报长图");
        battle.Click += async (_, _) => await ExportBattle();
        Button legacy = ExportButton("导出 v13 文本");
        legacy.Click += async (_, _) => await LegacyExporter.SaveAsync(_data, this);
        Button copy = ExportButton("复制个人摘要");
        copy.Click += async (_, _) => await CopySummary(copy);
        Button copyImage = ExportButton("复制战报图");
        copyImage.Click += async (_, _) => await CopyBattleImage(copyImage);
        Button help = ExportButton("帮助 ?");
        help.Click += (_, _) => _help!.IsVisible = true;

        CheckBox desensitize = new()
        {
            Content = "战报脱敏",
            IsChecked = false,
            FontSize = 11,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = new SolidColorBrush(_theme.Muted)
        };
        _desensitize = desensitize;

        StackPanel footer = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(20, 0, 20, 18),
            Children = { week, month, battle, legacy, copy, copyImage, help, desensitize }
        };

        _help = BuildHelp();
        _help.IsVisible = false;

        Grid root = new() { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(_tabs);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Grid.SetRowSpan(_help, 2);
        root.Children.Add(_help);
        Content = root;
    }

    /// <summary>供 golden / 诊断切换报表页。</summary>
    internal void SelectTab(int index) => _tabs.SelectedIndex = Math.Clamp(index, 0, _tabs.ItemCount - 1);

    private Button ExportButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(14, 7),
        CornerRadius = new CornerRadius(8),
        FontSize = 13,
        Background = new SolidColorBrush(_theme.Card),
        Foreground = new SolidColorBrush(_theme.Text)
    };

    private async Task ExportPeriod(int days, string title)
    {
        DateTime end = LatestDate();
        DateTime from = end.AddDays(-(days - 1));
        PeriodPosterSurface poster = new(_data.History.Values, from, end, title, _theme);
        await PosterExporter.SavePngAsync(
            poster,
            PeriodPosterSurface.PosterWidth,
            PeriodPosterSurface.PosterHeight,
            3,
            $"{title}-{end:yyyyMMdd}.png",
            this);
    }

    private async Task ExportBattle()
    {
        DateTime end = LatestDate();
        _data.History.TryGetValue(end.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), out DayRecord? day);
        BattlePosterSurface poster = new(day, _theme, _desensitize.IsChecked == true);
        await PosterExporter.SavePngAsync(
            poster,
            BattlePosterSurface.PosterWidth,
            BattlePosterSurface.MeasureHeight(day),
            3,
            $"键鼠统计战报-{end:yyyyMMdd}.png",
            this);
    }

    private TabItem Tab(string header, Control content) => new()
    {
        Header = header,
        Content = new ScrollViewer { Content = content }
    };

    private Control Personal()
    {
        int days = Math.Max(1, _data.History.Count);
        StatsTotals totals = _data.Totals;
        double active = _data.History.Values.Sum(day => day.ActiveSeconds);
        List<(string, string)> rows =
        [
            ("统计天数", days.ToString()),
            ("总击键", totals.Keys.ToString("N0")),
            ("总点击", totals.Clicks.ToString("N0")),
            ("总滚轮", totals.Wheel.ToString("N0")),
            ("总移动", $"{totals.MoveMeters:N1} m"),
            ("总活跃", FormatDuration(active)),
            ("日均击键", (totals.Keys / (double)days).ToString("N0")),
            ("日均点击", (totals.Clicks / (double)days).ToString("N0")),
            ("击键/点击比", totalRatio(totals.Keys, totals.Clicks)),
            ("每分钟活跃击键", totalRatio(totals.Keys * 60.0, active)),
            ("每分钟活跃点击", totalRatio(totals.Clicks * 60.0, active))
        ];
        return WithChart(
            TrendChart(14, day => day.Keys, "近 14 天击键"),
            Table(rows));
    }

    private Control Range()
    {
        DateTime end = LatestDate();
        List<(string, string)> rows = [];
        List<double> keysByRange = [];
        foreach (int days in new[] { 7, 30, 90 })
        {
            DateTime start = end.AddDays(-(days - 1));
            List<DayRecord> window = _data.History.Values
                .Where(day => day.Date >= start && day.Date <= end)
                .ToList();
            long keys = window.Sum(day => day.Keys);
            long clicks = window.Sum(day => day.Clicks);
            double active = window.Sum(day => day.ActiveSeconds);
            rows.Add(($"近 {days} 天", $"{window.Count} 天 · {keys:N0} 键 · {clicks:N0} 击 · {FormatDuration(active)}"));
            keysByRange.Add(keys);
        }

        return WithChart(
            new ReportChart(keysByRange, ["7 天", "30 天", "90 天"], _theme.Accent),
            Table(rows));
    }

    private Control Distribution()
    {
        List<double> keys = _data.History.Values.Select(day => (double)day.Keys).OrderBy(value => value).ToList();
        if (keys.Count == 0)
        {
            return Table([("分布", "暂无数据")]);
        }

        double mean = keys.Average();
        double median = keys[keys.Count / 2];
        double variance = keys.Sum(value => (value - mean) * (value - mean)) / Math.Max(1, keys.Count - 1);
        double min = keys[0];
        double max = keys[^1];
        int bins = 10;
        double[] histogram = new double[bins];
        string[] labels = new string[bins];
        for (int i = 0; i < bins; i++)
        {
            double low = min + (max - min) * i / bins;
            double high = min + (max - min) * (i + 1) / bins;
            histogram[i] = keys.Count(value => value >= low && (i == bins - 1 ? value <= high : value < high));
            labels[i] = low >= 1000 ? (low / 1000).ToString("0.#") + "k" : low.ToString("0");
        }

        return WithChart(
            new ReportChart(histogram, labels, _theme.Green),
            Table(
            [
                ("样本天数", keys.Count.ToString()),
                ("最小/最大", $"{keys[0]:N0} / {keys[^1]:N0}"),
                ("均值", mean.ToString("N0")),
                ("中位数", median.ToString("N0")),
                ("样本标准差", Math.Sqrt(variance).ToString("N1")),
                ("Q1 / Q3", $"{Quantile(keys, 0.25):N0} / {Quantile(keys, 0.75):N0}")
            ]));
    }

    private Control Cross()
    {
        long packets = _data.History.Values.Sum(day => day.Cross.Packets);
        long clicks = _data.History.Values.Sum(day => day.Cross.Clicks);
        long flicks = _data.History.Values.Sum(day => day.Cross.Flicks);
        long reversals = _data.History.Values.Sum(day => day.Cross.Reversals);
        double paths = _data.History.Values.Sum(day => day.Cross.PathCounts);
        return WithChart(
            new ReportChart([packets, clicks, flicks, reversals], ["包", "点击", "Flick", "反转"], _theme.Cyan),
            Table(
            [
                ("Raw Input 包", packets.ToString("N0")),
                ("Raw Input 点击", clicks.ToString("N0")),
                ("Flicks", flicks.ToString("N0")),
                ("方向反转", reversals.ToString("N0")),
                ("路径计数", paths.ToString("N0"))
            ]));
    }

    private Control Hold()
    {
        long count = _data.History.Values.Sum(day => day.HoldCount);
        double total = _data.History.Values.Sum(day => day.HoldTotalMs);
        double max = _data.History.Values.Count == 0 ? 0 : _data.History.Values.Max(day => day.HoldMaxMs);
        long discarded = _data.History.Values.Sum(day => day.HoldDiscarded);
        double[] buckets = new double[8];
        foreach (DayRecord day in _data.History.Values)
        {
            for (int i = 0; i < 8; i++)
            {
                buckets[i] += day.HoldBuckets[i];
            }
        }

        return WithChart(
            new ReportChart(buckets, ["<50", "100", "200", "500", "1s", "2s", "5s", "≥5s"], _theme.Purple),
            Table(
            [
                ("有效样本", count.ToString("N0")),
                ("总按住时长", FormatDuration(total / 1000.0)),
                ("平均按住", count == 0 ? "--" : $"{total / count:0.#} ms"),
                ("最大按住", $"{max:0.#} ms"),
                ("丢弃样本", discarded.ToString("N0"))
            ]));
    }

    private Control Table(IEnumerable<(string Label, string Value)> rows)
    {
        StackPanel stack = new() { Margin = new Thickness(20), Spacing = 8 };
        foreach ((string label, string value) in rows)
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("240,*") };
            row.Children.Add(new AvaloniaText
            {
                Text = label,
                FontSize = 13,
                Foreground = new SolidColorBrush(_theme.Muted)
            });
            AvaloniaText text = new()
            {
                Text = value,
                FontSize = 14,
                Foreground = new SolidColorBrush(_theme.Text)
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            stack.Children.Add(row);
        }

        return stack;
    }

    private Control WithChart(Control chart, Control table)
    {
        StackPanel stack = new() { Margin = new Thickness(16, 14, 16, 8), Spacing = 8 };
        chart.Height = 188;
        stack.Children.Add(chart);
        stack.Children.Add(table);
        return stack;
    }

    private Control TrendChart(int days, Func<DayRecord, double> selector, string title)
    {
        double[] values = new double[days];
        string[] labels = new string[days];
        DateTime end = LatestDate();
        for (int i = 0; i < days; i++)
        {
            DateTime date = end.AddDays(i - days + 1);
            string key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            values[i] = _data.History.TryGetValue(key, out DayRecord? day) ? selector(day) : 0;
            labels[i] = date.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        StackPanel stack = new() { Spacing = 4 };
        stack.Children.Add(new AvaloniaText
        {
            Text = title,
            FontSize = 11,
            Foreground = new SolidColorBrush(_theme.Muted)
        });
        stack.Children.Add(new ReportChart(values, labels, _theme.Accent, line: true)
        {
            Height = 150
        });
        return stack;
    }

    private Border BuildHelp()
    {
        Button close = ExportButton("关闭");
        close.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        close.Click += (_, _) => _help!.IsVisible = false;

        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(new AvaloniaText
        {
            Text = "报表说明",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(_theme.Text)
        });
        foreach (string line in new[]
                 {
                     "个人指标：全部历史总量、日均与活跃强度；不修改数据。",
                     "区间回顾：以最近有数据日为终点，比较 7/30/90 天窗口。",
                     "分布：按每日击键数做 10 档直方图；中位数比均值更抗峰值。",
                     "交叉：Raw Input 独立观测，用于校验钩子计数与方向/甩枪事件。",
                     "按住趋势：8 档按住时长；EF 无效样本会计入丢弃数。",
                     "导出：周报/月报/战报为 3× PNG；v13 文本用于回滚到 v3。",
                     "脱敏：战报长图不显示进程名，只显示「应用 01」等编号。"
                 })
        {
            content.Children.Add(new AvaloniaText
            {
                Text = "· " + line,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(_theme.Muted)
            });
        }

        content.Children.Add(close);

        Border card = new()
        {
            Width = 620,
            Background = new SolidColorBrush(_theme.Card),
            BorderBrush = new SolidColorBrush(_theme.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 18),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = content
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(185, _theme.Background.R, _theme.Background.G, _theme.Background.B)),
            Child = card
        };
    }

    private async Task CopySummary(Button button)
    {
        System.Text.StringBuilder text = new();
        text.AppendLine($"键鼠统计 · 个人摘要 · {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine($"统计天数：{_data.History.Count}");
        text.AppendLine($"总击键：{_data.Totals.Keys:N0} · 总点击：{_data.Totals.Clicks:N0} · 总滚轮：{_data.Totals.Wheel:N0}");
        text.AppendLine($"总移动：{_data.Totals.MoveMeters:N1} m · 总活跃：{FormatDuration(_data.History.Values.Sum(day => day.ActiveSeconds))}");
        text.AppendLine($"按住样本：{_data.Totals.HoldCount:N0} · 平均：{(_data.Totals.HoldCount == 0 ? "--" : $"{_data.Totals.HoldTotalMs / _data.Totals.HoldCount:0.#} ms")}");
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text.ToString());
            button.Content = "已复制";
            DispatcherTimer.RunOnce(() => button.Content = "复制个人摘要", TimeSpan.FromSeconds(2));
        }
    }

    private async Task CopyBattleImage(Button button)
    {
        DateTime end = LatestDate();
        _data.History.TryGetValue(
            end.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            out DayRecord? day);
        BattlePosterSurface poster = new(day, _theme, _desensitize.IsChecked == true);
        using RenderTargetBitmap? bitmap = PosterExporter.Render(
            poster,
            BattlePosterSurface.PosterWidth,
            BattlePosterSurface.MeasureHeight(day),
            2);
        if (bitmap is null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetBitmapAsync(bitmap);
            button.Content = "已复制战报图";
            DispatcherTimer.RunOnce(() => button.Content = "复制战报图", TimeSpan.FromSeconds(2));
        }
    }

    private DateTime LatestDate() =>
        _data.History.Count == 0 ? DateTime.Today : _data.History.Values.Max(day => day.Date);

    private static string totalRatio(double numerator, double denominator) =>
        denominator > 0 ? (numerator / denominator).ToString("0.##") : "--";

    private static double Quantile(List<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double position = (values.Count - 1) * p;
        int low = (int)Math.Floor(position);
        int high = (int)Math.Ceiling(position);
        return values[low] + (values[high] - values[low]) * (position - low);
    }

    private static string FormatDuration(double seconds) =>
        seconds < 60 ? $"{seconds:0}s"
        : seconds < 3600 ? $"{seconds / 60:0.#} 分"
        : $"{seconds / 3600:0.##} 小时";
}
