using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.App.Reports;
using Kms.Core.Text;
using Kms.Core.Time;
using Kms.Render.Tokens;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Kms.App.Widgets;

internal sealed class CharsWidgetModel : WidgetPageModel
{
    public CharsWidgetModel(
        RimeLogStats stats,
        TimeScopeRange scope,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Stats = stats;
        Scope = scope;
        Slice = stats.HasData
            ? stats.Slice(scope.Start, scope.End, AppContext.IsAll ? null : AppContext.Filter.AppKey)
            : null;
    }

    public AppPageContext AppContext { get; }

    public RimeLogStats Stats { get; }

    public TimeScopeRange Scope { get; }

    public RimeSlice? Slice { get; }

    public bool Revealed { get; set; }
}

internal static class CharsWidgets
{
    private static CharsWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("chars", () =>
            new CharsWidgetModel(
                context.Rime ?? RimeLog.Load(),
                context.Scope,
                context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpiTotal(WidgetContext context)
    {
        RimeSlice? slice = Resolve(context).Slice;
        return MicroKpi.Card(
            "上屏字数",
            PageKit.FormatCount(slice?.Chars ?? 0),
            $"{PageKit.FormatCount(slice?.Commits ?? 0)} 次上屏",
            PageKit.T.Accent);
    }

    public static WidgetView CreateKpiAverageLength(WidgetContext context)
    {
        RimeSlice? slice = Resolve(context).Slice;
        double average = slice is null || slice.Commits <= 0 ? 0 : slice.Chars / (double)slice.Commits;
        return MicroKpi.Card(
            "平均上屏长度",
            slice is null ? "--" : $"{average:0.##} 字",
            "每次上屏字符数",
            PageKit.T.Green);
    }

    public static WidgetView CreateKpiDistinct(WidgetContext context)
    {
        RimeSlice? slice = Resolve(context).Slice;
        return MicroKpi.Card(
            "去重汉字",
            PageKit.FormatCount(slice?.DistinctHan ?? 0),
            "本区间出现过的汉字",
            PageKit.T.Cyan);
    }

    public static WidgetView CreateKpiFirstRate(WidgetContext context)
    {
        RimeSlice? slice = Resolve(context).Slice;
        double rate = slice is null || slice.CandidateKnown <= 0
            ? double.NaN
            : slice.CandidateFirst * 100.0 / slice.CandidateKnown;
        return MicroKpi.Card(
            "候选首选率",
            double.IsNaN(rate) ? "--" : $"{rate:0.#}%",
            "首选候选占比",
            PageKit.T.Purple);
    }

    public static WidgetView CreateSummary(WidgetContext context)
    {
        CharsWidgetModel model = Resolve(context);
        RimeSlice? slice = model.Slice;
        string firstRate = slice is null || slice.CandidateKnown == 0
            ? "--"
            : (slice.CandidateFirst * 100.0 / slice.CandidateKnown).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        string perCommit = slice is null || slice.TextCommits == 0
            ? "--"
            : (slice.TextChars / (double)slice.TextCommits).ToString("0.##", CultureInfo.InvariantCulture);
        Control body = PageKit.StatStripBody(
            ("上屏字数", PageKit.FormatCount(slice?.Chars ?? 0), PageKit.T.Accent),
            ("上屏次数", PageKit.FormatCount(slice?.Commits ?? 0), PageKit.T.Green),
            ("去重汉字", PageKit.FormatCount(slice?.DistinctHan ?? 0), PageKit.T.Cyan),
            ("活跃天", $"{slice?.ActiveDays ?? 0}", PageKit.T.Purple),
            ("平均每次", perCommit, PageKit.T.Orange),
            ("首选率", firstRate, PageKit.T.Text));
        return new WidgetView
        {
            Content = body,
            ShowHeader = false,
            ContentPadding = new Thickness(12),
            Elevated = true
        };
    }

    public static WidgetView CreateCloud(WidgetContext context)
    {
        CharsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new CharsCloudView(model),
            HeaderHint = "默认模糊 · 字卡颜色表示频率 · 局部翻页",
            HeaderTrailing = PageKit.Badge("CHARS", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateWords(WidgetContext context)
    {
        CharsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new CharsWordsView(model),
            HeaderHint = "连续上屏的词语 · 局部翻页",
            HeaderTrailing = PageKit.Badge("PHRASES", PageKit.T.Purple),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateCharts(WidgetContext context)
    {
        CharsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new CharsChartsView(model),
            HeaderHint = "字符构成、小时分布、词长与候选效率",
            HeaderTrailing = PageKit.Badge("CHARTS", PageKit.T.Orange),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreatePrivacy(WidgetContext context)
    {
        CharsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new CharsPrivacyView(model),
            HeaderHint = "默认不渲染具体字符",
            HeaderTrailing = PageKit.Badge("PRIVATE", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }
}

internal sealed class CharsCloudView : UserControl
{
    private readonly CharsWidgetModel _model;
    private readonly PageKit.PagedList _list = new(1, 0);

    public CharsCloudView(CharsWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        RimeSlice? slice = _model.Slice;
        if (slice is null)
        {
            _list.SetItems([PageKit.Empty("暂无可解析的 Rime 记录")]);
            return;
        }

        int columns = Math.Max(1, (int)((Bounds.Width - 12) / 80));
        int rows = Math.Max(1, (int)((Bounds.Height - 12) / 76));
        int pageSize = Math.Clamp(columns * rows, 8, 48);
        _list.SetPageSize(1);

        List<KeyValuePair<int, long>> topChars = slice.CharCounts
            .OrderByDescending(pair => pair.Value)
            .Take(pageSize)
            .ToList();
        long max = Math.Max(1, topChars.Count == 0 ? 1 : topChars[0].Value);
        Color[] colors = [PageKit.T.Accent, PageKit.T.Cyan, PageKit.T.Green, PageKit.T.Purple, PageKit.T.Orange, PageKit.T.Red];
        List<Control> pages = [];
        for (int start = 0; start < topChars.Count; start += pageSize)
        {
            WrapPanel grid = new() { Orientation = Orientation.Horizontal };
            int index = start;
            foreach ((int codePoint, long count) in topChars.Skip(start).Take(pageSize))
            {
                Color tone = colors[index % colors.Length];
                string glyph = _model.Revealed ? char.ConvertFromUtf32(codePoint) : "＊";
                Border card = new()
                {
                    Width = 72,
                    Height = 64,
                    Margin = new Thickness(0, 0, 8, 8),
                    CornerRadius = new CornerRadius(12),
                    Background = PageKit.Gradient(
                        PageKit.Alpha(tone, 0.14 + 0.18 * count / max),
                        PageKit.Alpha(PageKit.T.Card, 0.8),
                        135),
                    BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.28)),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 1,
                        Margin = new Thickness(8, 5),
                        Children =
                        {
                            PageKit.Label(glyph, 22, PageKit.T.Text, FontWeight.SemiBold),
                            PageKit.Muted(PageKit.FormatCount(count), 9),
                            new PageKit.Proportion(count / (double)max, tone) { Height = 3 }
                        }
                    }
                };
                ToolTip.SetTip(card, _model.Revealed
                    ? $"{char.ConvertFromUtf32(codePoint)} · {count:N0} 次"
                    : $"已遮罩 · {count:N0} 次");
                grid.Children.Add(card);
                index++;
            }

            pages.Add(grid);
        }

        if (pages.Count == 0)
        {
            pages.Add(PageKit.Empty("Rime 日志里还没有汉字"));
        }

        _list.SetItems(pages);
    }
}

internal sealed class CharsWordsView : UserControl
{
    private readonly CharsWidgetModel _model;
    private readonly PageKit.PagedList _list = new(8, 4);

    public CharsWordsView(CharsWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) =>
        {
            int size = Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 40), 4, 20) : 8;
            _list.SetPageSize(size);
            Build();
        };
    }

    private void Build()
    {
        RimeSlice? slice = _model.Slice;
        if (slice is null)
        {
            _list.SetItems([PageKit.Empty("暂无词组数据")]);
            return;
        }

        long max = Math.Max(1, slice.PhraseCounts.Count == 0 ? 1 : slice.PhraseCounts.Values.Max());
        List<Control> rows = [];
        int rank = 1;
        foreach ((string phrase, long count) in slice.PhraseCounts
                     .OrderByDescending(pair => pair.Value)
                     .Take(24))
        {
            string text = _model.Revealed ? phrase : new string('＊', Math.Min(6, Math.Max(2, phrase.Length)));
            rows.Add(PageKit.RankRow(
                rank++,
                text,
                PageKit.FormatCount(count),
                count / (double)max,
                rank switch
                {
                    2 => PageKit.T.Purple,
                    3 => PageKit.T.Cyan,
                    4 => PageKit.T.Green,
                    5 => PageKit.T.Orange,
                    _ => PageKit.T.Accent
                }));
        }

        if (rows.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有连续上屏的词组"));
        }

        _list.SetItems(rows);
    }
}

internal sealed class CharsChartsView : UserControl
{
    private readonly CharsWidgetModel _model;
    private readonly Grid _grid = new()
    {
        ColumnDefinitions = new ColumnDefinitions("*,*"),
        RowDefinitions = new RowDefinitions("*,*"),
        ColumnSpacing = 10,
        RowSpacing = 10
    };
    private readonly TextBlock _pagerLabel;
    private readonly StackPanel _root = new() { Spacing = 6 };
    private int _page;

    public CharsChartsView(CharsWidgetModel model)
    {
        _model = model;
        _pagerLabel = PageKit.Label("1 / 1", 9.5, PageKit.T.Muted);
        StackPanel pager = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button prev = PagerButton("‹", true);
        prev.Click += (_, _) =>
        {
            if (_page > 0)
            {
                _page--;
                Build();
            }
        };
        Button next = PagerButton("›", true);
        next.Click += (_, _) =>
        {
            _page++;
            Build();
        };
        pager.Children.Add(prev);
        pager.Children.Add(_pagerLabel);
        pager.Children.Add(next);
        _root.Children.Add(_grid);
        _root.Children.Add(pager);
        Content = _root;
        Build();
    }

    private void Build()
    {
        (string Title, Control Chart)[] charts = ChartSet();
        int pageCount = Math.Max(1, (charts.Length + 3) / 4);
        _page = Math.Clamp(_page, 0, pageCount - 1);
        _pagerLabel.Text = $"{_page + 1} / {pageCount}";
        _grid.Children.Clear();
        if (charts.Length == 0)
        {
            _grid.Children.Add(PageKit.Empty("暂无可绘制的 Rime 图表"));
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            int index = _page * 4 + i;
            if (index >= charts.Length)
            {
                break;
            }

            charts[index].Chart.Height = Math.Clamp(Bounds.Height / 2 - 30, 90, 260);
            Border card = PageKit.Card(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    PageKit.Label(charts[index].Title, 11, PageKit.T.Text, FontWeight.SemiBold),
                    charts[index].Chart
                }
            }, 10, PageKit.R(9, 12));
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            _grid.Children.Add(card);
        }
    }

    private (string Title, Control Chart)[] ChartSet()
    {
        RimeSlice? slice = _model.Slice;
        if (slice is null)
        {
            return [];
        }

        string scopeLabel = _model.Scope.Label;
        List<RimeDay> days = _model.Stats.DaySeries(_model.Scope.Start, _model.Scope.End);
        List<RimeAppSummary> apps = slice.TopApps(5);
        List<(string Title, Control Chart)> charts = [];
        if (!_model.Scope.IsToday && days.Count > 1)
        {
            charts.Add(($"每日字数 · {scopeLabel}", new ReportChart(
                days.Select(day => (double)day.Chars).ToArray(),
                days.Select(day => day.Day.ToString("MM-dd")).ToArray(),
                PageKit.T.Accent, line: true)));
            charts.Add(($"每日次数 · {scopeLabel}", new ReportChart(
                days.Select(day => (double)day.Commits).ToArray(),
                days.Select(day => day.Day.ToString("MM-dd")).ToArray(),
                PageKit.T.Green, line: true)));
        }

        charts.Add(($"{scopeLabel} 24 小时字数", new ReportChart(
            slice.HourChars.Select(value => (double)value).ToArray(),
            Enumerable.Range(0, 24).Select(hour => hour.ToString("00")).ToArray(),
            PageKit.T.Cyan)));
        charts.Add(($"{scopeLabel} 24 小时上屏次数", new ReportChart(
            slice.HourCommits.Select(value => (double)value).ToArray(),
            Enumerable.Range(0, 24).Select(hour => hour.ToString("00")).ToArray(),
            PageKit.T.Purple)));
        charts.Add(("词长分布", new ReportChart(
            slice.WordLen.Select(value => (double)value).ToArray(),
            ["1", "2", "3", "4", "5", "6", "7", "8", "9+"],
            PageKit.T.Orange)));
        charts.Add(("输入构成", new ReportChart(
            slice.Composition.Select(value => (double)value).ToArray(),
            RimeSlice.CompositionNames, PageKit.T.Cyan)));
        charts.Add(("输出应用 Top 5", new ReportChart(
            apps.Select(app => (double)app.Chars).ToArray(),
            apps.Select(app =>
            {
                string name = System.IO.Path.GetFileName(app.Path);
                return name.Length > 8 ? name[..8] : name;
            }).ToArray(),
            PageKit.T.Green)));
        charts.Add(("候选效率", new ReportChart(
            [
                double.IsNaN(slice.FirstChoiceRate) ? 0 : slice.FirstChoiceRate * 100,
                double.IsNaN(slice.PagedCandidateRate) ? 0 : slice.PagedCandidateRate * 100,
                slice.CandidateCoverage * 100
            ],
            ["首选", "翻页", "覆盖"], PageKit.T.Purple)));
        return charts.ToArray();
    }

    private static Button PagerButton(string text, bool enabled) => new()
    {
        Content = text,
        Width = 24,
        Height = 20,
        Padding = new Thickness(0),
        FontSize = 12,
        CornerRadius = new CornerRadius(5),
        Opacity = enabled ? 1 : 0.25,
        Background = PageKit.Brush(Colors.Transparent),
        Foreground = PageKit.Brush(PageKit.T.Muted),
        BorderThickness = new Thickness(0)
    };
}

internal sealed class CharsPrivacyView : UserControl
{
    public CharsPrivacyView(CharsWidgetModel model)
    {
        RimeSlice? slice = model.Slice;
        StackPanel rows = new() { Spacing = 7 };
        rows.Children.Add(PageKit.StatRow("日志来源", model.Stats.Missing ? "未找到 Rime 日志" : model.Stats.Source, PageKit.T.Text));
        rows.Children.Add(PageKit.StatRow("全局范围", model.Scope.Caption, PageKit.T.Text));
        rows.Children.Add(PageKit.StatRow("日志总范围",
            model.Stats.FirstDay.HasValue && model.Stats.LastDay.HasValue
                ? $"{model.Stats.FirstDay:MM-dd} → {model.Stats.LastDay:MM-dd}"
                : "暂无时间范围",
            PageKit.T.Muted));
        rows.Children.Add(PageKit.StatRow("上屏字数", PageKit.FormatCount(slice?.Chars ?? 0), PageKit.T.Accent));
        rows.Children.Add(PageKit.StatRow("去重汉字", PageKit.FormatCount(slice?.DistinctHan ?? 0), PageKit.T.Cyan));
        rows.Children.Add(PageKit.Divider());
        rows.Children.Add(PageKit.Muted(
            model.Stats.Missing
                ? "未检测到 Rime 日志。使用 Rime 输入法后，这里会自动出现字符统计；v4 不会扫描其他输入法内容。"
                : "字符统计只来自本机 Rime 日志；默认遮罩，只有点击右上角“显示明文”才渲染具体字词。切换全局范围只重新切片，不重复读取日志。",
            10));
        Content = rows;
    }
}
