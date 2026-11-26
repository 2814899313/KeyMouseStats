using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>日报：单日封面、六指标、会话时间线、应用与高光，附海报导出。</summary>
public sealed class DayPage : UserControl
{
    private readonly StatsData _data;
    private readonly DayRecord? _day;
    private readonly DateTime _selectedDate;
    private readonly ThemeTokens _theme = PageKit.T;
    private readonly PageKit.PagedList _appList = new(3, 4);
    private readonly PageKit.PagedList _keyList = new(3, 4);
    private readonly DayTimeline _timeline;
    private Avalonia.Controls.TextBlock? _highlightsNote;

    public DayPage(StatsData data, string title, string subtitle, DateTime? selected = null, Action<DateTime>? onDate = null)
    {
        _data = data;
        _day = selected.HasValue ? ResolveExact(data, selected.Value) : ResolveDay(data);
        _selectedDate = (selected ?? _day?.Date ?? DateTime.Today).Date;
        DateTime minDate = data.History.Count == 0 ? _selectedDate : data.History.Values.Min(day => day.Date);
        DateTime maxDate = data.History.Count == 0 ? _selectedDate : data.History.Values.Max(day => day.Date);

        Button export = new()
        {
            Content = "导出日报海报  ↗",
            Padding = new Thickness(14, 7),
            CornerRadius = new CornerRadius(20),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Background = PageKit.Gradient(_theme.Accent, _theme.Cyan, 0),
            Foreground = PageKit.Brush(_theme.OnAccent)
        };
        export.Click += async (_, _) =>
        {
            string name = $"键鼠统计日报-{_selectedDate:yyyyMMdd}.png";
            await Reports.PosterExporter.SavePngAsync(
                new Reports.DailyPosterSurface(_day, _theme),
                Reports.DailyPosterSurface.PosterWidth,
                Reports.DailyPosterSurface.PosterHeight,
                3,
                name,
                TopLevel.GetTopLevel(this));
        };

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                PageKit.DateNav(_selectedDate, minDate, maxDate, onDate ?? (_ => { })),
                export
            }
        };

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,1.15*,1*"),
            RowSpacing = 10
        };
        root.Children.Add(PageKit.PageHeader(
            "DAY / REPORT",
            title,
            _day is null ? subtitle : $"{subtitle} · {Weekday(_selectedDate)}",
            null,
            actions));

        Control hero = Hero();
        Grid.SetRow(hero, 1);
        root.Children.Add(hero);

        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.5*,1*"),
            ColumnSpacing = 14
        };
        _timeline = new DayTimeline(_day) { Height = 110 };
        main.Children.Add(PageKit.Section(
            "会话时间线",
            "活跃段与空闲段 · 今天未结束",
            _timeline,
            PageKit.Badge("24H", _theme.Accent)));
        Control apps = PageKit.Section(
            "应用 Top 6",
            "按活跃时长排序 · 局部翻页",
            AppList(),
            PageKit.Badge("APPS", _theme.Green),
            _theme.Green);
        Grid.SetColumn(apps, 1);
        main.Children.Add(apps);
        Grid.SetRow(main, 2);
        root.Children.Add(main);

        Grid bottom = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1*,1*"),
            ColumnSpacing = 14
        };
        bottom.Children.Add(PageKit.Section(
            "今日高光",
            "单日值得记住的数字",
            Highlights(),
            PageKit.Badge("HIGHLIGHT", _theme.Orange),
            _theme.Orange,
            padding: 12));
        Control keys = PageKit.Section(
            "按键 Top 8",
            "当天最常用的键 · 局部翻页",
            KeyList(),
            PageKit.Badge("KEYS", _theme.Purple),
            _theme.Purple,
            padding: 12);
        Grid.SetColumn(keys, 1);
        bottom.Children.Add(keys);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        ApplyDensity(0);
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        bool tall = height >= 880;
        int apps = tall ? 6 : height >= 790 ? 5 : compact ? 2 : 3;
        int keys = tall ? 8 : height >= 790 ? 5 : compact ? 2 : 3;
        _appList.SetPageSize(apps);
        _keyList.SetPageSize(keys);
        _timeline.Height = compact ? 80 : 110;
        if (_highlightsNote is not null)
        {
            _highlightsNote.IsVisible = !compact;
        }
    }

    private Control Hero()
    {
        double active = _day?.ActiveSeconds ?? 0;
        long total = (_day?.Keys ?? 0) + (_day?.Clicks ?? 0) + (_day?.Wheel ?? 0);
        string topApp = _day?.Apps.Values is null
            ? "暂无应用"
            : PageKit.AggregateApps(_day.Apps.Values).FirstOrDefault()?.AppName ?? "暂无应用";

        Grid hero = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.45*,1*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 18,
            RowSpacing = 10
        };

        StackPanel left = new() { Spacing = 6 };
        left.Children.Add(PageKit.Badge(_day is null ? "无记录" : Weekday(_selectedDate), _theme.Orange));
        left.Children.Add(PageKit.Label(_selectedDate.ToString("yyyy.MM.dd"),
            24, _theme.Text, FontWeight.SemiBold));
        StackPanel valueRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        valueRow.Children.Add(PageKit.Label(PageKit.FormatCount(_day?.Keys ?? 0), 34, _theme.Text, FontWeight.Bold));
        valueRow.Children.Add(new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                PageKit.Label("次击键", 13, _theme.Text, FontWeight.SemiBold),
                PageKit.Muted($"{PageKit.FormatDuration(active)} 活跃", 9)
            }
        });
        left.Children.Add(valueRow);
        left.Children.Add(PageKit.Label($"今天最常停在「{topApp}」", 11, _theme.Cyan, FontWeight.Medium, 330, TextTrimming.CharacterEllipsis));
        left.Children.Add(PageKit.Muted(_day is null
            ? "还没有当天记录"
            : $"{_day.Sessions.Count} 个活跃段 · 峰值 {_day.PeakApm} APM · {_day.AppSwitches} 次应用切换", 9));

        Grid.SetColumn(left, 0);
        hero.Children.Add(left);

        StackPanel right = new() { Spacing = 4 };
        right.Children.Add(new PageKit.Ring(
            [
                new DonutSlice(_day?.Keys ?? 0, _theme.Accent),
                new DonutSlice(_day?.Clicks ?? 0, _theme.Green),
                new DonutSlice(_day?.Wheel ?? 0, _theme.Purple)
            ],
            PageKit.FormatCount(total),
            "输入事件") { Height = 96 });
        right.Children.Add(PageKit.Legend(
            (_theme.Accent, "击键"),
            (_theme.Green, "点击"),
            (_theme.Purple, "滚轮")));
        Grid.SetColumn(right, 1);
        hero.Children.Add(right);

        (string Label, string Value, Color Tone)[] metrics =
        [
            ("击键", PageKit.FormatCount(_day?.Keys ?? 0), _theme.Accent),
            ("点击", PageKit.FormatCount(_day?.Clicks ?? 0), _theme.Green),
            ("滚轮", PageKit.FormatCount(_day?.Wheel ?? 0), _theme.Purple),
            ("移动", PageKit.FormatMeters(_day?.MoveMeters ?? 0), _theme.Cyan),
            ("活跃", PageKit.FormatDuration(_day?.ActiveSeconds ?? 0), _theme.Orange),
            ("空闲", PageKit.FormatDuration(_day?.IdleSeconds ?? 0), _theme.Muted)
        ];
        Grid strip = new() { ColumnSpacing = 10 };
        for (int i = 0; i < metrics.Length; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            StackPanel cell = new()
            {
                Spacing = 0,
                Children =
                {
                    PageKit.Label(metrics[i].Label, 9, PageKit.T.Muted),
                    PageKit.Label(metrics[i].Value, 13, metrics[i].Tone, FontWeight.SemiBold)
                }
            };
            Grid.SetColumn(cell, i);
            strip.Children.Add(cell);
        }

        Grid.SetRow(strip, 1);
        Grid.SetColumnSpan(strip, 2);
        hero.Children.Add(strip);

        return new Border
        {
            Background = PageKit.Gradient(PageKit.Alpha(_theme.Orange, 0.18), PageKit.Alpha(_theme.Card, 0.94), 120),
            BorderBrush = PageKit.Brush(PageKit.Alpha(_theme.Orange, 0.32)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PageKit.R(12, 18)),
            Padding = new Thickness(16, 12),
            BoxShadow = PageKit.Shadow(38, 18, 0.24),
            Child = hero
        };
    }

    private Control CompactMetrics()
    {
        (string Label, string Value)[] items =
        [
            ("击键", PageKit.FormatCount(_day?.Keys ?? 0)),
            ("点击", PageKit.FormatCount(_day?.Clicks ?? 0)),
            ("滚轮", PageKit.FormatCount(_day?.Wheel ?? 0)),
            ("移动", PageKit.FormatMeters(_day?.MoveMeters ?? 0)),
            ("活跃", PageKit.FormatDuration(_day?.ActiveSeconds ?? 0)),
            ("空闲", PageKit.FormatDuration(_day?.IdleSeconds ?? 0))
        ];

        Grid grid = new() { ColumnSpacing = 10 };
        for (int i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Border card = PageKit.Card(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    PageKit.Muted(items[i].Label, 9),
                    PageKit.Label(items[i].Value, 20, _theme.Text, FontWeight.SemiBold)
                }
            }, 12, PageKit.R(9, 14));
            Grid.SetColumn(card, i);
            grid.Children.Add(card);
        }

        return grid;
    }

    private Control AppList()
    {
        List<Control> rows = [];
        if (_day is null || _day.Apps.Count == 0)
        {
            rows.Add(PageKit.Empty("暂无应用数据"));
            _appList.SetItems(rows);
            return _appList;
        }

        double max = Math.Max(1, PageKit.AggregateApps(_day.Apps.Values).Max(app => app.ActiveSeconds));
        int rank = 1;
        foreach (AppUsage app in PageKit.AggregateApps(_day.Apps.Values).Take(6))
        {
            rows.Add(PageKit.RankRow(
                rank++,
                app.AppName,
                PageKit.FormatDuration(app.ActiveSeconds),
                app.ActiveSeconds / max,
                rank switch
                {
                    2 => _theme.Green,
                    3 => _theme.Accent,
                    4 => _theme.Purple,
                    5 => _theme.Cyan,
                    _ => _theme.Orange
                },
                $"{PageKit.FormatCount(app.Keys)} 键 · {PageKit.FormatCount(app.Clicks)} 击"));
        }

        _appList.SetItems(rows);
        return _appList;
    }

    private Control Highlights()
    {
        double longest = _day?.Sessions.Count > 0 ? _day.Sessions.Max(session => session.Seconds) : 0;
        int peakHour = 0;
        if (_day is not null)
        {
            for (int hour = 1; hour < 24; hour++)
            {
                if (_day.HourKeys[hour] > _day.HourKeys[peakHour])
                {
                    peakHour = hour;
                }
            }
        }

        double averageHold = _day is null || _day.HoldCount == 0 ? 0 : _day.HoldTotalMs / _day.HoldCount;
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 18,
            RowSpacing = 2
        };
        (string Label, string Value, Color Tone)[] items =
        [
            ("最长专注", PageKit.FormatDuration(longest), _theme.Green),
            ("峰值 APM", (_day?.PeakApm ?? 0).ToString("N0"), _theme.Orange),
            ("最活跃小时", $"{peakHour:00}:00", _theme.Accent),
            ("应用切换", $"{_day?.AppSwitches ?? 0:N0} 次", _theme.Purple),
            ("按住样本", $"{_day?.HoldCount ?? 0:N0} 次", _theme.Cyan),
            ("平均按住", $"{averageHold:0} ms", _theme.Text)
        ];
        for (int i = 0; i < items.Length; i++)
        {
            Control row = PageKit.StatRow(items[i].Label, items[i].Value, items[i].Tone);
            Grid.SetColumn(row, i % 2);
            Grid.SetRow(row, i / 2);
            grid.Children.Add(row);
        }

        _highlightsNote = PageKit.Muted("导出按钮会生成 4320×5400 的日报海报，适合留档或分享。", 9);
        StackPanel body = new()
        {
            Spacing = 8,
            Children =
            {
                grid,
                _highlightsNote
            }
        };
        return body;
    }

    private Control KeyList()
    {
        List<Control> rows = [];
        if (_day is null || _day.KeyCounts.Count == 0)
        {
            rows.Add(PageKit.Empty("暂无按键数据"));
            _keyList.SetItems(rows);
            return _keyList;
        }

        long max = Math.Max(1, _day.KeyCounts.Values.Max());
        int rank = 1;
        foreach ((int vk, long count) in _day.KeyCounts.OrderByDescending(pair => pair.Value).Take(8))
        {
            rows.Add(PageKit.RankRow(
                rank++,
                Kms.Input.Devices.PeripheralWear.KeyName(vk),
                PageKit.FormatCount(count),
                count / (double)max,
                rank switch
                {
                    2 => _theme.Purple,
                    3 => _theme.Accent,
                    4 => _theme.Green,
                    _ => _theme.Cyan
                },
                keycap: true));
        }

        _keyList.SetItems(rows);
        return _keyList;
    }

    private static string Weekday(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };

    private static DayRecord? ResolveExact(StatsData data, DateTime date) =>
        data.History.TryGetValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out DayRecord? record)
            ? record
            : null;

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

    private sealed class DayTimeline : Control
    {
        private readonly DayRecord? _day;

        public DayTimeline(DayRecord? day) => _day = day;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            ThemeTokens theme = PageKit.T;
            Rect plot = new(8, 10, Math.Max(1, Bounds.Width - 16), Math.Max(1, Bounds.Height - 30));
            context.FillRectangle(PageKit.Brush(PageKit.Alpha(theme.Line, 0.18)), new Rect(plot.Left, 34, plot.Width, 48), 7);

            if (_day is not null)
            {
                foreach (ActiveSession session in _day.Sessions)
                {
                    double start = Math.Clamp((session.Start - _day.Date).TotalHours / 24.0, 0, 1);
                    double end = Math.Clamp((session.End - _day.Date).TotalHours / 24.0, 0, 1);
                    double x = plot.Left + plot.Width * start;
                    double width = Math.Max(2, plot.Width * (end - start));
                    Color tone = session.Seconds >= 1200 ? theme.Orange : session.Seconds >= 300 ? theme.Accent : theme.Cyan;
                    context.FillRectangle(PageKit.Gradient(PageKit.Alpha(tone, 0.45), tone, 0),
                        new Rect(x, 34, width, 48), 4);
                }
            }

            IBrush muted = PageKit.Brush(theme.Muted);
            for (int hour = 0; hour <= 24; hour += 3)
            {
                double x = plot.Left + plot.Width * hour / 24.0;
                context.DrawLine(new Pen(PageKit.Brush(PageKit.Alpha(theme.Line, 0.45)), 1),
                    new Point(x, 28), new Point(x, 88));
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, hour == 24 ? "24" : hour.ToString("00"), new Point(x - 7, 94),
                    8, muted, Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 18);
            }

            if (_day is not null && _day.Date == DateTime.Today)
            {
                double x = plot.Left + plot.Width * Math.Clamp(DateTime.Now.TimeOfDay.TotalHours / 24.0, 0, 1);
                context.DrawLine(new Pen(PageKit.Brush(PageKit.Alpha(theme.Orange, 0.7)), 1, new DashStyle([3, 3], 0)),
                    new Point(x, 26), new Point(x, 88));
            }

            Kms.Render.Primitives.TextBlock.DrawText(
                context, "活跃", new Point(plot.Left, 38), 8, PageKit.Brush(theme.Accent),
                Kms.Render.Primitives.TextBlock.DefaultFamily);
        }
    }
}
