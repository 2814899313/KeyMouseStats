using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.Core.Model;
using Kms.Input.Devices;
using Kms.App.Theming;
using Kms.Render.Primitives;
using Kms.Render.Tokens;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Kms.App.Pages;

/// <summary>总览：8 张紧凑 KPI、鼠标/键盘分布、累计 Top5、洞察与 CSV 导出。</summary>
public sealed class OverviewPage : UserControl
{
    private const int PeriodToday = 0;
    private const int PeriodWeek = 1;
    private const int PeriodMonth = 2;
    private const int PeriodYear = 3;

    private readonly StatsData _data;
    private readonly InputService? _service;
    private readonly DayRecord? _day;
    private readonly DateTime _today = DateTime.Today;

    private readonly TextBlock _clock = new();
    private readonly TextBlock[] _tileValues = new TextBlock[8];
    private readonly TextBlock _footer = new();
    private readonly TextBlock _insight = new();
    private readonly LiveRateSpark _liveSpark = new();
    private readonly ContentControl _distributionHost = new();
    private readonly DispatcherTimer _timer;
    private int _period;
    private bool _keyboardMode;

    public OverviewPage(
        StatsData data,
        string title,
        string subtitle,
        Action? refresh = null,
        InputService? service = null)
    {
        _data = data;
        _service = service;
        _day = ResolveDay(data);
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");

        Grid root = new()
        {
            Margin = new Thickness(22, 16, 22, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 12
        };
        root.Children.Add(BuildHeader(title));

        Control tiles = BuildTiles();
        Grid.SetRow(tiles, 1);
        root.Children.Add(tiles);

        Grid middle = new()
        {
            ColumnDefinitions = new ColumnDefinitions("0.485*,0.515*"),
            ColumnSpacing = 17
        };
        _distributionHost.Content = BuildDistribution();
        middle.Children.Add(_distributionHost);
        Control top = BuildTopKeys();
        Grid.SetColumn(top, 1);
        middle.Children.Add(top);
        Grid.SetRow(middle, 2);
        root.Children.Add(middle);

        _insight.Text = BuildInsightText();
        _insight.FontSize = 10.5;
        _insight.Foreground = PageKit.Brush(PageKit.T.Muted);
        _insight.TextTrimming = TextTrimming.CharacterEllipsis;
        Border insightBar = new()
        {
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.72)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6),
            Child = _insight
        };
        Grid.SetRow(insightBar, 3);
        root.Children.Add(insightBar);

        Control footer = BuildFooter();
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        Content = root;
        RefreshLiveValues();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshLiveValues();
        _timer.Start();
    }

    private static ThemeTokens Palette => PageKit.T;

    private Control BuildHeader(string title)
    {
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };

        TextBlock crumb = new()
        {
            Text = $"工作空间  /  {title}",
            FontSize = 9.5,
            Foreground = PageKit.Brush(PageKit.T.Muted)
        };
        header.Children.Add(crumb);

        StackPanel titleRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 30,
            FontWeight = FontWeight.Bold,
            Foreground = PageKit.Brush(PageKit.T.Text)
        });

        string state = _day is null
            ? "暂无数据"
            : _day.Date == _today ? "今日 · 累积中" : $"最近 · {_day.Date:MM-dd}";
        titleRow.Children.Add(new Border
        {
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.14)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.30)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new Ellipse { Width = 6, Height = 6, Fill = PageKit.Brush(PageKit.T.Accent) },
                    new TextBlock
                    {
                        Text = state,
                        FontSize = 10.5,
                        Foreground = PageKit.Brush(PageKit.T.Accent)
                    }
                }
            }
        });
        Grid.SetRow(titleRow, 1);
        header.Children.Add(titleRow);

        _clock.Text = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture);
        _clock.FontSize = 11;
        _clock.Foreground = PageKit.Brush(PageKit.T.Text);
        _clock.VerticalAlignment = VerticalAlignment.Center;

        Border clockPill = new()
        {
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.86)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.75)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new Ellipse { Width = 6, Height = 6, Fill = PageKit.Brush(PageKit.T.Green) },
                    _clock
                }
            }
        };
        Grid.SetColumn(clockPill, 1);
        Grid.SetRowSpan(clockPill, 2);
        header.Children.Add(clockPill);
        if (ThemeArtwork.IsArtTheme(PageKit.T.Id) || ThemeArtwork.CustomActive)
        {
            return new Border
            {
                Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.76)),
                BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.45)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(PageKit.R(10, 14)),
                Padding = new Thickness(12, 8),
                Child = header
            };
        }

        return header;
    }

    private Control BuildTiles()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowDefinitions = new RowDefinitions("88,88"),
            ColumnSpacing = 16,
            RowSpacing = 14
        };

        DayRecord? day = _day;
        string keysBaseline = Baseline(keys: true);
        string activeBaseline = Baseline(keys: false);
        long keys = day?.Keys ?? 0;
        long clicks = day?.Clicks ?? 0;
        long wheel = day?.Wheel ?? 0;
        double meters = day?.MoveMeters ?? 0;
        double active = day?.ActiveSeconds ?? 0;
        int sessions = day?.Sessions.Count ?? 0;
        long keyRate = _service?.KeysPerMin ?? 0;
        long apm = _service?.Apm ?? 0;

        (string Label, string Value, Color Color, string? Extra, bool Live, bool Apm)[] tiles =
        [
            ("今日击键", FmtCount(keys), Palette.Accent, keysBaseline, false, false),
            ("今日点击", FmtCount(clicks), Palette.Green, null, false, false),
            ("今日滚轮", FmtCount(wheel), Palette.Orange, null, false, false),
            ("鼠标路程(估算)", FmtMeters(meters), Palette.Purple, null, false, false),
            ("有效使用时长", FormatDuration(active), Palette.Green, activeBaseline, false, false),
            ("连续使用段", $"{sessions} 段", Palette.Accent, null, false, false),
            ("击键速率 / 分", keyRate.ToString("N0", CultureInfo.InvariantCulture), Palette.Cyan, null, true, false),
            ("操作速率 APM", apm.ToString("N0", CultureInfo.InvariantCulture), Palette.Red, "最近 5 分钟 · 60秒滚动值", false, true)
        ];

        for (int i = 0; i < tiles.Length; i++)
        {
            Control tile = BuildTile(tiles[i].Label, tiles[i].Value, tiles[i].Color,
                tiles[i].Extra, tiles[i].Live, tiles[i].Apm, i);
            Grid.SetColumn(tile, i % 4);
            Grid.SetRow(tile, i / 4);
            grid.Children.Add(tile);
        }

        return grid;
    }

    private Control BuildTile(
        string label,
        string value,
        Color color,
        string? extra,
        bool live,
        bool apm,
        int index)
    {
        StackPanel text = new()
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = PageKit.Brush(PageKit.T.Muted)
        });

        Grid valueRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        TextBlock number = new()
        {
            Text = value,
            FontSize = 27,
            FontWeight = FontWeight.SemiBold,
            Foreground = PageKit.Brush(PageKit.T.Text),
            VerticalAlignment = VerticalAlignment.Center
        };
        valueRow.Children.Add(number);
        if (apm)
        {
            _liveSpark.Height = 30;
            _liveSpark.Margin = new Thickness(10, 10, 0, 0);
            _liveSpark.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(_liveSpark, 1);
            valueRow.Children.Add(_liveSpark);
        }

        text.Children.Add(valueRow);
        if (!string.IsNullOrEmpty(extra))
        {
            text.Children.Add(new TextBlock
            {
                Text = extra,
                FontSize = 9.5,
                Foreground = PageKit.Brush(PageKit.Alpha(PageKit.T.Muted, 0.92)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 210
            });
        }

        if (live)
        {
            _tileValues[6] = number;
        }
        else if (apm)
        {
            _tileValues[7] = number;
        }
        else if (index < 7)
        {
            _tileValues[index] = number;
        }

        Grid content = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(text);
        Control icon = new KpiIcon(color) { Width = 32, Height = 32, Margin = new Thickness(8, 2, 0, 0) };
        Grid.SetColumn(icon, 1);
        content.Children.Add(icon);

        return new Border
        {
            Background = PageKit.CardBrush,
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.9)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PageKit.R(10, 14)),
            Padding = new Thickness(18, 10),
            Child = content
        };
    }

    private Control BuildDistribution()
    {
        StackPanel body = new() { Spacing = 8 };
        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        TextBlock title = new()
        {
            Text = "按键分布",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = PageKit.Brush(PageKit.T.Text),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(title);

        StackPanel modes = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        Button mouse = ModeButton("鼠标", !_keyboardMode, () => SetMode(false));
        Button keyboard = ModeButton("键盘", _keyboardMode, () => SetMode(true));
        modes.Children.Add(mouse);
        modes.Children.Add(keyboard);
        Grid.SetColumn(modes, 1);
        header.Children.Add(modes);
        body.Children.Add(header);

        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("62,226,*"),
            ColumnSpacing = 4
        };

        StackPanel periods = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        string[] labels = ["今日", "本周", "本月", "本年"];
        for (int i = 0; i < labels.Length; i++)
        {
            int value = i;
            Button chip = ModeButton(labels[i], _period == i, () => SetPeriod(value));
            chip.Width = 52;
            chip.Height = 30;
            periods.Children.Add(chip);
        }

        content.Children.Add(periods);

        StackPanel ringPanel = new()
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ringPanel.Children.Add(BuildRing());
        Grid.SetColumn(ringPanel, 1);
        content.Children.Add(ringPanel);

        StackPanel legend = new() { Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        foreach (Control row in BuildLegend())
        {
            legend.Children.Add(row);
        }

        Grid.SetColumn(legend, 2);
        content.Children.Add(legend);
        body.Children.Add(content);

        return new Border
        {
            Background = PageKit.CardBrush,
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.9)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PageKit.R(10, 14)),
            Padding = new Thickness(18, 12),
            Child = body
        };
    }

    private Control BuildRing()
    {
        (string[] names, long[] values) = DistributionValues();
        long total = values.Sum();
        List<DonutSlice> slices = [];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > 0)
            {
                slices.Add(new DonutSlice(values[i], SliceColor(i)));
            }
        }

        if (slices.Count == 0)
        {
            slices.Add(new DonutSlice(1, PageKit.Alpha(PageKit.T.Line, 0.35)));
        }

        PageKit.Ring ring = new(
            slices,
            total > 0 ? FmtCount(total) : "暂无数据",
            _keyboardMode ? "次已记录击键" : "次点击")
        {
            Width = 210,
            Height = 210
        };
        return ring;
    }

    private IEnumerable<Control> BuildLegend()
    {
        (string[] names, long[] values) = DistributionValues();
        long total = values.Sum();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0 && total > 0)
            {
                continue;
            }

            string pct = total > 0 ? $"{values[i] * 100.0 / total:0.0}%" : "--";
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("10,*"),
                Margin = new Thickness(0, 1, 0, 1)
            };
            row.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = PageKit.Brush(SliceColor(i)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, 0, 0)
            });
            StackPanel text = new() { Spacing = 1 };
            text.Children.Add(new TextBlock
            {
                Text = names[i],
                FontSize = 11,
                Foreground = PageKit.Brush(PageKit.T.Text),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{FmtCount(values[i])} · {pct}",
                FontSize = 9.5,
                Foreground = PageKit.Brush(PageKit.T.Muted),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            yield return row;
        }
    }

    private DateTime Anchor => (_day?.Date ?? _today).Date;

    private (string[] Names, long[] Values) DistributionValues()
    {
        DateTime start = PeriodStart(_period);
        DateTime end = Anchor;
        if (_keyboardMode)
        {
            Dictionary<int, long> counts = [];
            foreach (DayRecord day in _data.History.Values)
            {
                if (day.Date < start || day.Date > end)
                {
                    continue;
                }

                foreach ((int vk, long count) in day.KeyCounts)
                {
                    if (count > 0)
                    {
                        counts[vk] = counts.GetValueOrDefault(vk) + count;
                    }
                }
            }

            List<KeyValuePair<int, long>> rank = counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .ToList();
            const int top = 8;
            int shown = Math.Min(top, rank.Count);
            bool hasOther = rank.Count > top;
            string[] names = new string[shown + (hasOther ? 1 : 0)];
            long[] values = new long[names.Length];
            for (int i = 0; i < shown; i++)
            {
                names[i] = PeripheralWear.KeyName(rank[i].Key);
                values[i] = rank[i].Value;
            }

            if (hasOther)
            {
                names[^1] = "其他";
                values[^1] = rank.Skip(top).Sum(pair => pair.Value);
            }

            return (names, values);
        }

        long left = 0, right = 0, middle = 0, side = 0;
        foreach (DayRecord day in _data.History.Values)
        {
            if (day.Date < start || day.Date > end)
            {
                continue;
            }

            left += day.Left;
            right += day.Right;
            middle += day.Middle;
            side += day.XButtons;
        }

        return (["左键", "右键", "中键", "侧键"], [left, right, middle, side]);
    }

    private Control BuildTopKeys()
    {
        Grid body = new()
        {
            RowSpacing = 4,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        body.RowDefinitions.Add(new RowDefinition(0, GridUnitType.Auto));
        for (int i = 0; i < 5; i++)
        {
            body.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        }

        TextBlock heading = new()
        {
            Text = "最常按键 Top 5 · 累计",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = PageKit.Brush(PageKit.T.Text)
        };
        body.Children.Add(heading);

        List<KeyValuePair<int, long>> rank = AggregateKeys().Take(5).ToList();
        if (rank.Count == 0)
        {
            Control empty = PageKit.Empty("还没有键盘记录");
            Grid.SetRow(empty, 1);
            Grid.SetRowSpan(empty, 5);
            body.Children.Add(empty);
        }
        else
        {
            long max = Math.Max(1, rank[0].Value);
            for (int i = 0; i < rank.Count; i++)
            {
                Color color = RankColor(i);
                Grid row = new()
                {
                    ColumnDefinitions = new ColumnDefinitions("22,64,*,68")
                };
                row.Children.Add(new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 10,
                    Foreground = PageKit.Brush(PageKit.T.Muted),
                    VerticalAlignment = VerticalAlignment.Center
                });
                TextBlock name = new()
                {
                    Text = PeripheralWear.KeyName(rank[i].Key),
                    FontSize = 12,
                    Foreground = PageKit.Brush(PageKit.T.Text),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                PageKit.Proportion bar = new(rank[i].Value / (double)max, color)
                {
                    Height = 9,
                    Margin = new Thickness(8, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(bar, 2);
                row.Children.Add(bar);

                TextBlock value = new()
                {
                    Text = FmtCount(rank[i].Value),
                    FontSize = 11,
                    Foreground = PageKit.Brush(PageKit.T.Text),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(value, 3);
                row.Children.Add(value);
                Grid.SetRow(row, i + 1);
                body.Children.Add(row);
            }
        }

        return new Border
        {
            Background = PageKit.CardBrush,
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.9)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PageKit.R(10, 14)),
            Padding = new Thickness(18, 12),
            Child = body
        };
    }

    private Control BuildFooter()
    {
        Grid footer = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _footer.FontSize = 10.5;
        _footer.Foreground = PageKit.Brush(PageKit.T.Muted);
        _footer.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(_footer);

        Button export = new()
        {
            Content = "⇩  导出 CSV",
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(16, 7),
            CornerRadius = new CornerRadius(8),
            Background = PageKit.Gradient(PageKit.T.Accent, PageKit.T.Cyan, 0),
            Foreground = PageKit.Brush(PageKit.T.OnAccent),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        export.Click += async (_, _) => await ExportCsvAsync();
        Grid.SetColumn(export, 1);
        footer.Children.Add(export);
        return footer;
    }

    private void SetMode(bool keyboard)
    {
        if (_keyboardMode == keyboard)
        {
            return;
        }

        _keyboardMode = keyboard;
        _distributionHost.Content = BuildDistribution();
    }

    private void SetPeriod(int period)
    {
        if (_period == period)
        {
            return;
        }

        _period = period;
        _distributionHost.Content = BuildDistribution();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshLiveValues()
    {
        _clock.Text = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture);
        DayRecord? day = _day;
        if (day is not null && _day?.Date == _today)
        {
            // 文本对象由采集宿主原地更新，这里只刷新数字，不重建图表。
            if (_tileValues[0] is { } keys)
            {
                keys.Text = FmtCount(day.Keys);
            }

            if (_tileValues[1] is { } clicks)
            {
                clicks.Text = FmtCount(day.Clicks);
            }

            if (_tileValues[2] is { } wheel)
            {
                wheel.Text = FmtCount(day.Wheel);
            }

            if (_tileValues[3] is { } meters)
            {
                meters.Text = FmtMeters(day.MoveMeters);
            }

            if (_tileValues[4] is { } active)
            {
                active.Text = FormatDuration(day.ActiveSeconds);
            }

            if (_tileValues[5] is { } sessions)
            {
                sessions.Text = $"{day.Sessions.Count} 段";
            }
        }

        long keyRate = _service?.KeysPerMin ?? 0;
        long apm = _service?.Apm ?? 0;
        _tileValues[6].Text = keyRate.ToString("N0", CultureInfo.InvariantCulture);
        _tileValues[7].Text = apm.ToString("N0", CultureInfo.InvariantCulture);
        _liveSpark.Push(apm);

        ActiveSession? session = day?.Sessions.LastOrDefault();
        _footer.Text = $"本段 {FormatDuration(session?.Seconds ?? 0)}  ·  今日峰值 {day?.PeakApm ?? 0} APM  ·  " +
                       $"实时键 {keyRate:N0} / 点 {_service?.ClicksPerMin ?? 0:N0}";
    }

    private string BuildInsightText()
    {
        List<KeyValuePair<int, long>> rank = AggregateKeys();
        long totalKeys = rank.Sum(pair => pair.Value);
        string top = rank.Count == 0
            ? "--"
            : $"{PeripheralWear.KeyName(rank[0].Key)}({(totalKeys > 0 ? rank[0].Value * 100.0 / totalKeys : 0):0.0}%)";
        int peak = 0;
        if (_day is not null)
        {
            for (int hour = 1; hour < 24; hour++)
            {
                if (_day.HourKeys[hour] > _day.HourKeys[peak])
                {
                    peak = hour;
                }
            }
        }

        long daily = _data.History.Count == 0 ? 0 : _data.Totals.Keys / Math.Max(1, _data.History.Count);
        return $"洞察   最常用按键 {top}    ·    高峰时段 {peak:00}:00-{peak + 1:00}:00    ·    日均击键 {FmtCount(daily)}";
    }

    private List<KeyValuePair<int, long>> AggregateKeys()
    {
        Dictionary<int, long> counts = [];
        foreach (DayRecord day in _data.History.Values)
        {
            foreach ((int vk, long count) in day.KeyCounts)
            {
                if (count > 0)
                {
                    counts[vk] = counts.GetValueOrDefault(vk) + count;
                }
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .ToList();
    }

    private string Baseline(bool keys)
    {
        DateTime end = Anchor;
        List<double> values = [];
        foreach (DayRecord day in _data.History.Values)
        {
            if (day.Date >= end || day.Date < end.AddDays(-30))
            {
                continue;
            }

            values.Add(keys ? day.Keys : day.ActiveSeconds);
        }

        if (values.Count < 7)
        {
            return "";
        }

        values.Sort();
        double p50 = Quantile(values, 0.5);
        double p90 = Quantile(values, 0.9);
        return keys
            ? $"P50 {FmtCount((long)p50)}  ·  P90 {FmtCount((long)p90)}"
            : $"P50 {FormatDuration(p50)}  ·  P90 {FormatDuration(p90)}";
    }

    private static double Quantile(List<double> sorted, double p)
    {
        double position = (sorted.Count - 1) * p;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private DateTime PeriodStart(int period) => PeriodStart(period, Anchor);

    private static DateTime PeriodStart(int period, DateTime anchor)
    {
        DateTime today = anchor.Date;
        return period switch
        {
            PeriodWeek => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            PeriodMonth => new DateTime(today.Year, today.Month, 1),
            PeriodYear => new DateTime(today.Year, 1, 1),
            _ => today
        };
    }

    private static Button ModeButton(string text, bool active, Action onClick)
    {
        Button button = new()
        {
            Content = text,
            FontSize = 10.5,
            Padding = new Thickness(12, 5),
            CornerRadius = new CornerRadius(7),
            Background = active ? PageKit.Brush(PageKit.T.Accent) : PageKit.Brush(PageKit.T.Raised),
            Foreground = active ? PageKit.Brush(PageKit.T.OnAccent) : PageKit.Brush(PageKit.T.Muted),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Color SliceColor(int index) => index switch
    {
        0 => PageKit.T.Accent,
        1 => PageKit.T.Green,
        2 => PageKit.T.Orange,
        3 => PageKit.T.Purple,
        4 => PageKit.T.Cyan,
        5 => PageKit.T.Red,
        6 => PageKit.T.Purple,
        7 => PageKit.T.Green,
        _ => PageKit.T.Muted
    };

    private static Color RankColor(int index) => index switch
    {
        0 => PageKit.T.Purple,
        1 => PageKit.T.Cyan,
        2 => PageKit.T.Orange,
        3 => PageKit.T.Green,
        _ => PageKit.T.Accent
    };

    private static string FmtCount(long value)
    {
        if (value < 10_000)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        double v = value;
        return v < 1e8
            ? (v / 1e4).ToString("0.#", CultureInfo.InvariantCulture) + "万"
            : (v / 1e8).ToString("0.##", CultureInfo.InvariantCulture) + "亿";
    }

    private static string FmtMeters(double meters)
    {
        if (meters > 0 && meters < 1)
        {
            return (meters * 100).ToString("0.##", CultureInfo.InvariantCulture) + " cm";
        }

        if (meters < 1000)
        {
            return meters.ToString("0.#", CultureInfo.InvariantCulture) + " m";
        }

        return meters < 100_000
            ? (meters / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " km"
            : (meters / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " km";
    }

    private static string FormatDuration(double seconds)
    {
        long whole = Math.Max(0, (long)seconds);
        if (whole < 60)
        {
            return whole + " 秒";
        }

        if (whole < 3600)
        {
            return whole / 60 + " 分 " + whole % 60 + " 秒";
        }

        return whole / 3600 + " 时 " + (whole % 3600) / 60 + " 分";
    }

    private async Task ExportCsvAsync()
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "导出 CSV",
                SuggestedFileName = $"键鼠统计-{DateTime.Today:yyyyMMdd}.csv",
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });
        if (file is null)
        {
            return;
        }

        StringBuilder csv = new();
        csv.AppendLine("day,keys,clicks,left,right,middle,xbuttons,wheel,move_m,active_seconds,idle_seconds,peak_apm,sessions,hold_count,hold_total_ms");
        foreach (DayRecord day in _data.History.Values.OrderByDescending(value => value.Date))
        {
            csv.Append(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.Keys).Append(',')
                .Append(day.Clicks).Append(',')
                .Append(day.Left).Append(',')
                .Append(day.Right).Append(',')
                .Append(day.Middle).Append(',')
                .Append(day.XButtons).Append(',')
                .Append(day.Wheel).Append(',')
                .Append(day.MoveMeters.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.ActiveSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.IdleSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(day.PeakApm).Append(',')
                .Append(day.Sessions.Count).Append(',')
                .Append(day.HoldCount).Append(',')
                .Append(day.HoldTotalMs.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        await using Stream stream = await file.OpenWriteAsync();
        await using StreamWriter writer = new(stream, new UTF8Encoding(true));
        await writer.WriteAsync(csv.ToString());
    }

    private static DayRecord? ResolveDay(StatsData data)
    {
        string key = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (data.History.TryGetValue(key, out DayRecord? today))
        {
            return today;
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

    private sealed class KpiIcon : Control
    {
        private readonly Color _color;

        public KpiIcon(Color color) => _color = color;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            context.FillRectangle(
                PageKit.Brush(PageKit.Alpha(_color, 0.18)),
                bounds,
                8);
            double barWidth = 3.5;
            double gap = 3;
            double startX = bounds.Center.X - (barWidth * 2 + gap * 3) / 2 + gap / 2;
            double bottom = bounds.Bottom - 8;
            double[] heights = [6, 11, 15, 9];
            for (int i = 0; i < heights.Length; i++)
            {
                context.FillRectangle(
                    PageKit.Brush(PageKit.Alpha(_color, 0.85)),
                    new Rect(startX + i * (barWidth + gap), bottom - heights[i], barWidth, heights[i]),
                    1.5f);
            }
        }
    }

    private sealed class LiveRateSpark : Control
    {
        private readonly Queue<double> _values = new();

        public void Push(double value)
        {
            _values.Enqueue(Math.Max(0, value));
            while (_values.Count > 60)
            {
                _values.Dequeue();
            }

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_values.Count < 2)
            {
                return;
            }

            double[] values = _values.ToArray();
            double max = Math.Max(1, values.Max());
            Rect plot = new(1, 2, Math.Max(1, Bounds.Width - 2), Math.Max(1, Bounds.Height - 4));
            StreamGeometry line = new();
            using (StreamGeometryContext path = line.Open())
            {
                for (int i = 0; i < values.Length; i++)
                {
                    Point point = new(
                        plot.Left + plot.Width * i / (values.Length - 1),
                        plot.Bottom - plot.Height * values[i] / max);
                    if (i == 0)
                    {
                        path.BeginFigure(point, false);
                    }
                    else
                    {
                        path.LineTo(point);
                    }
                }

                path.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(PageKit.Brush(PageKit.T.Cyan), 1.6), line);
        }
    }
}
