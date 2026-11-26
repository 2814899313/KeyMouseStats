using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Reports;
using Kms.Core.Text;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>字符内容：Rime 上屏统计、字/词组排行、隐私开关与来源说明。</summary>
public sealed class TextPage : UserControl
{
    private readonly ThemeTokens _theme = PageKit.T;
    private readonly RimeSlice? _slice;
    private readonly RimeLogStats _stats;
    private readonly PageKit.PagedList _chars = new(1, 0);
    private readonly PageKit.PagedList _phrases = new(12, 3);
    private readonly Button _toggle;
    private readonly ContentControl _modeHost = new();
    private readonly ContentControl _contentHost = new();
    private int _mode;
    private int _chartPage;
    private int _charPageSize = 24;
    private int _phrasePageSize = 12;
    private double _chartHeight = 190;
    private bool _revealed;

    public TextPage(string title, string subtitle)
    {
        _stats = RimeLog.Load();
        _slice = _stats.HasData && _stats.FirstDay.HasValue && _stats.LastDay.HasValue
            ? _stats.Slice(_stats.FirstDay.Value, _stats.LastDay.Value)
            : null;

        _toggle = new Button
        {
            Content = "显示明文",
            Padding = new Thickness(13, 6),
            CornerRadius = new CornerRadius(20),
            FontSize = 11,
            Background = PageKit.Brush(PageKit.Alpha(_theme.Accent, 0.12)),
            Foreground = PageKit.Brush(_theme.Accent),
            BorderBrush = PageKit.Brush(PageKit.Alpha(_theme.Accent, 0.4)),
            BorderThickness = new Thickness(1)
        };
        _toggle.Click += (_, _) =>
        {
            _revealed = !_revealed;
            UpdateToggle();
            FillLists();
        };

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _modeHost, _toggle }
        };
        _modeHost.Content = ModeSelector();
        root.Children.Add(PageKit.PageHeader(
            "TEXT / PRIVATE BY DEFAULT",
            title,
            subtitle,
            _stats.Missing ? "未找到 Rime 日志" : FormatRange(),
            actions));

        Control summary = BuildSummary();
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        Grid.SetRow(_contentHost, 2);
        root.Children.Add(_contentHost);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        UpdateToggle();
        ShowMode(0);
        FillLists();
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        int chars = compact ? 15 : 24;
        int phrases = compact ? 8 : 12;
        double chartHeight = compact ? 130 : 190;
        bool chartChanged = Math.Abs(chartHeight - _chartHeight) > 0.5;
        _chartHeight = chartHeight;
        if (chars == _charPageSize && phrases == _phrasePageSize && !chartChanged)
        {
            return;
        }

        _charPageSize = chars;
        _phrasePageSize = phrases;
        _phrases.SetPageSize(phrases);
        if (_contentHost.Content is not null)
        {
            if (_mode == 0)
            {
                FillLists();
            }
            else if (_mode == 1 && chartChanged)
            {
                ShowMode(1);
            }
        }
    }

    private Control ModeSelector() => PageKit.Segmented(
        ["字词", "图表", "隐私"],
        _mode,
        ShowMode);

    private void ShowMode(int mode)
    {
        _mode = mode;
        _modeHost.Content = ModeSelector();
        if (_contentHost.Content is not null)
        {
            foreach (Control control in new Control[] { _chars, _phrases })
            {
                if (control.Parent is Panel panel)
                {
                    panel.Children.Remove(control);
                }
            }
        }

        _contentHost.Content = mode switch
        {
            1 => ChartPage(),
            2 => PrivacyPage(),
            _ => WordsPage()
        };
    }

    private Control WordsPage()
    {
        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.25*,1*"),
            ColumnSpacing = 14
        };
        main.Children.Add(PageKit.Section(
            "高频字 Top 24",
            "默认模糊 · 字卡颜色表示频率 · 局部翻页",
            _chars,
            PageKit.Badge("CHARS", _theme.Cyan),
            _theme.Cyan));
        Control phrases = PageKit.Section(
            "高频词组 Top 16",
            "连续上屏的词语 · 局部翻页",
            _phrases,
            PageKit.Badge("PHRASES", _theme.Purple),
            _theme.Purple);
        Grid.SetColumn(phrases, 1);
        main.Children.Add(phrases);
        return main;
    }

    private Control ChartPage()
    {
        (string Title, Control Chart)[] charts = ChartSet();
        if (charts.Length == 0)
        {
            return PageKit.Empty("暂无可绘制的 Rime 图表");
        }

        int pageCount = Math.Max(1, (charts.Length + 3) / 4);
        _chartPage = Math.Clamp(_chartPage, 0, pageCount - 1);
        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 8
        };

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10,
            RowDefinitions = new RowDefinitions("*,*"),
            RowSpacing = 10
        };
        for (int i = 0; i < 4; i++)
        {
            int index = _chartPage * 4 + i;
            if (index >= charts.Length)
            {
                break;
            }

            Border card = PageKit.Card(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    PageKit.Label(charts[index].Title, 11, PageKit.T.Text, FontWeight.SemiBold),
                    charts[index].Chart
                }
            }, 10, PageKit.R(9, 12));
            charts[index].Chart.Height = _chartHeight;
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            grid.Children.Add(card);
        }

        root.Children.Add(grid);

        StackPanel pager = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Button prev = PagerButton("‹", _chartPage > 0);
        prev.Click += (_, _) =>
        {
            if (_chartPage > 0)
            {
                _chartPage--;
                ShowMode(1);
            }
        };
        Button next = PagerButton("›", _chartPage < pageCount - 1);
        next.Click += (_, _) =>
        {
            if (_chartPage < pageCount - 1)
            {
                _chartPage++;
                ShowMode(1);
            }
        };
        pager.Children.Add(prev);
        pager.Children.Add(PageKit.Label($"{_chartPage + 1} / {pageCount}", 9.5, PageKit.T.Muted));
        pager.Children.Add(next);
        Grid.SetRow(pager, 1);
        root.Children.Add(pager);
        return root;
    }

    private static Button PagerButton(string text, bool enabled) => new()
    {
        Content = text,
        Width = 26,
        Height = 22,
        Padding = new Thickness(0),
        FontSize = 13,
        CornerRadius = new CornerRadius(5),
        Opacity = enabled ? 1 : 0.25,
        Background = PageKit.Brush(Colors.Transparent),
        Foreground = PageKit.Brush(PageKit.T.Muted),
        BorderThickness = new Thickness(0)
    };

    private Control PrivacyPage() => PageKit.Card(new StackPanel
    {
        Spacing = 8,
        Children =
        {
            PageKit.Label("数据来源与隐私", 16, PageKit.T.Text, FontWeight.SemiBold),
            PageKit.Muted("只有在这页显式切换后才显示明文；默认不渲染具体字符。", 10),
            BuildPrivacy()
        }
    });

    private Control BuildSummary()
    {
        string firstRate = _slice is null || _slice.CandidateKnown == 0
            ? "--"
            : (_slice.CandidateFirst * 100.0 / _slice.CandidateKnown).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        string perCommit = _slice is null || _slice.TextCommits == 0
            ? "--"
            : (_slice.TextChars / (double)_slice.TextCommits).ToString("0.##", CultureInfo.InvariantCulture);

        return PageKit.StatStrip(
            ("上屏字数", PageKit.FormatCount(_slice?.Chars ?? 0), _theme.Accent),
            ("上屏次数", PageKit.FormatCount(_slice?.Commits ?? 0), _theme.Green),
            ("去重汉字", PageKit.FormatCount(_slice?.DistinctHan ?? 0), _theme.Cyan),
            ("活跃天", $"{_slice?.ActiveDays ?? 0}", _theme.Purple),
            ("平均每次", perCommit, _theme.Orange),
            ("首选率", firstRate, _theme.Text));
    }

    private string FormatRange() =>
        _stats.FirstDay.HasValue && _stats.LastDay.HasValue
            ? $"{_stats.FirstDay:MM-dd} → {_stats.LastDay:MM-dd}"
            : "暂无时间范围";

    private (string Title, Control Chart)[] ChartSet()
    {
        if (_slice is null)
        {
            return [];
        }

        DateTime end = _slice.To.Date;
        DateTime start = end.AddDays(-29);
        List<RimeDay> days = _stats.DaySeries(start, end);
        double[] dayChars = days.Select(day => (double)day.Chars).ToArray();
        double[] dayCommits = days.Select(day => (double)day.Commits).ToArray();
        string[] dayLabels = days.Select(day => day.Day.ToString("MM-dd")).ToArray();

        List<RimeAppSummary> apps = _slice.TopApps(5);
        return
        [
            ("每日字数 · 30 天", new ReportChart(dayChars, dayLabels, _theme.Accent, line: true)),
            ("每日次数 · 30 天", new ReportChart(dayCommits, dayLabels, _theme.Green, line: true)),
            ("24 小时字数", new ReportChart(
                _slice.HourChars.Select(value => (double)value).ToArray(),
                Enumerable.Range(0, 24).Select(hour => hour.ToString("00")).ToArray(),
                _theme.Cyan)),
            ("24 小时上屏次数", new ReportChart(
                _slice.HourCommits.Select(value => (double)value).ToArray(),
                Enumerable.Range(0, 24).Select(hour => hour.ToString("00")).ToArray(),
                _theme.Purple)),
            ("词长分布", new ReportChart(
                _slice.WordLen.Select(value => (double)value).ToArray(),
                ["1", "2", "3", "4", "5", "6", "7", "8", "9+"],
                _theme.Orange)),
            ("输入构成", new ReportChart(
                _slice.Composition.Select(value => (double)value).ToArray(),
                RimeSlice.CompositionNames,
                _theme.Cyan)),
            ("输出应用 Top 5", new ReportChart(
                apps.Select(app => (double)app.Chars).ToArray(),
                apps.Select(app =>
                {
                    string name = System.IO.Path.GetFileName(app.Path);
                    return name.Length > 8 ? name[..8] : name;
                }).ToArray(),
                _theme.Green)),
            ("候选效率", new ReportChart(
                [
                    double.IsNaN(_slice.FirstChoiceRate) ? 0 : _slice.FirstChoiceRate * 100,
                    double.IsNaN(_slice.PagedCandidateRate) ? 0 : _slice.PagedCandidateRate * 100,
                    _slice.CandidateCoverage * 100
                ],
                ["首选", "翻页", "覆盖"],
                _theme.Purple))
        ];
    }

    private Control BuildPrivacy()
    {
        StackPanel rows = new() { Spacing = 7 };
        rows.Children.Add(PageKit.StatRow("日志来源", _stats.Missing ? "未找到 Rime 日志" : _stats.Source, _theme.Text));
        rows.Children.Add(PageKit.StatRow("解析范围", FormatRange(), _theme.Text));
        rows.Children.Add(PageKit.StatRow("上屏字数", PageKit.FormatCount(_slice?.Chars ?? 0), _theme.Accent));
        rows.Children.Add(PageKit.StatRow("去重汉字", PageKit.FormatCount(_slice?.DistinctHan ?? 0), _theme.Cyan));
        rows.Children.Add(PageKit.Divider());
        rows.Children.Add(PageKit.Muted(
            _stats.Missing
                ? "未检测到 Rime 日志。使用 Rime 输入法后，这里会自动出现字符统计；v4 不会扫描其他输入法内容。"
                : "字符统计只来自本机 Rime 日志；默认遮罩，只有点击右上角“显示明文”才渲染具体字词。", 10));
        return rows;
    }

    private void UpdateToggle()
    {
        _toggle.Content = _revealed ? "隐藏明文" : "显示明文";
        _toggle.Background = PageKit.Brush(PageKit.Alpha(_revealed ? _theme.Orange : _theme.Accent, 0.12));
        _toggle.Foreground = PageKit.Brush(_revealed ? _theme.Orange : _theme.Accent);
        _toggle.BorderBrush = PageKit.Brush(PageKit.Alpha(_revealed ? _theme.Orange : _theme.Accent, 0.4));
    }

    private void FillLists()
    {
        if (_slice is null)
        {
            _chars.SetItems([PageKit.Empty("暂无可解析的 Rime 记录")]);
            _phrases.SetItems([PageKit.Empty("暂无词组数据")]);
            return;
        }

        List<KeyValuePair<int, long>> topChars = _slice.CharCounts
            .OrderByDescending(pair => pair.Value)
            .Take(24)
            .ToList();
        long max = Math.Max(1, topChars.Count == 0 ? 1 : topChars[0].Value);

        Color[] colors = [_theme.Accent, _theme.Cyan, _theme.Green, _theme.Purple, _theme.Orange, _theme.Red];
        List<Control> charPages = [];
        for (int pageStart = 0; pageStart < topChars.Count; pageStart += _charPageSize)
        {
            WrapPanel grid = new() { Orientation = Orientation.Horizontal };
            int index = pageStart;
            foreach ((int codePoint, long count) in topChars.Skip(pageStart).Take(_charPageSize))
            {
                Color tone = colors[index % colors.Length];
                string glyph = _revealed ? char.ConvertFromUtf32(codePoint) : "＊";
                Border card = new()
                {
                    Width = 72,
                    Height = 64,
                    Margin = new Thickness(0, 0, 8, 8),
                    CornerRadius = new CornerRadius(12),
                    Background = PageKit.Gradient(PageKit.Alpha(tone, 0.14 + 0.18 * count / max), PageKit.Alpha(_theme.Card, 0.8), 135),
                    BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.28)),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 1,
                        Margin = new Thickness(8, 5),
                        Children =
                        {
                            PageKit.Label(glyph, 22, _theme.Text, FontWeight.SemiBold),
                            PageKit.Muted(PageKit.FormatCount(count), 9),
                            new PageKit.Proportion(count / (double)max, tone) { Height = 3 }
                        }
                    }
                };
                ToolTip.SetTip(card, _revealed
                    ? $"{char.ConvertFromUtf32(codePoint)} · {count:N0} 次"
                    : $"已遮罩 · {count:N0} 次");
                grid.Children.Add(card);
                index++;
            }

            charPages.Add(grid);
        }

        if (charPages.Count == 0)
        {
            charPages.Add(PageKit.Empty("Rime 日志里还没有汉字"));
        }

        _chars.SetItems(charPages);

        long phraseMax = Math.Max(1, _slice.PhraseCounts.Count == 0 ? 1 : _slice.PhraseCounts.Values.Max());
        List<Control> phrases = [];
        int rank = 1;
        foreach ((string phrase, long count) in _slice.PhraseCounts
                     .OrderByDescending(pair => pair.Value)
                     .Take(16))
        {
            string text = _revealed ? phrase : new string('＊', Math.Min(6, Math.Max(2, phrase.Length)));
            phrases.Add(PageKit.RankRow(
                rank++,
                text,
                PageKit.FormatCount(count),
                count / (double)phraseMax,
                rank switch
                {
                    2 => _theme.Purple,
                    3 => _theme.Cyan,
                    4 => _theme.Green,
                    5 => _theme.Orange,
                    _ => _theme.Accent
                }));
        }

        if (phrases.Count == 0)
        {
            phrases.Add(PageKit.Empty("还没有连续上屏的词组"));
        }

        _phrases.SetItems(phrases);
    }
}
