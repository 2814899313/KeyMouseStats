using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.App.Reports;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Core.Time;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Widgets;

internal sealed class DailyWidgetModel : WidgetPageModel
{
    public DailyWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        DateTime selectedDate,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        PeriodMode = scope.Scope != TimeScope.Today;
        SelectedDate = selectedDate.Date;
        Day = ResolveExact(Data, SelectedDate);
        Range = StatsRange.Build(Data, scope);
        Totals = AppContext.IsAll
            ? Range.Totals
            : AppScopeData.BuildProfileTotals(AppContext.Profile, Range.Totals);
        ActiveDays = Range.ActiveDays;
    }

    public AppPageContext AppContext { get; }

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public bool PeriodMode { get; }

    public DateTime SelectedDate { get; }

    public DayRecord? Day { get; }

    public StatsRange Range { get; }

    public DayRecord Totals { get; }

    public int ActiveDays { get; }

    public IReadOnlyDictionary<int, long> KeyCounts => !AppContext.IsAll
        ? Totals.KeyCounts
        : PeriodMode
            ? Totals.KeyCounts
            : Day?.KeyCounts ?? new Dictionary<int, long>();

    public static DayRecord? ResolveExact(StatsData data, DateTime date) =>
        data.History.TryGetValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out DayRecord? record)
            ? record
            : null;

    public static DateTime ResolveDate(StatsData data)
    {
        DateTime today = DateTime.Today;
        if (data.History.ContainsKey(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
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

        return latest?.Date ?? today;
    }

    public static string Weekday(DateTime date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };
}

internal static class DailyWidgets
{
    private static DailyWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("daily", () => new DailyWidgetModel(
            context.Data,
            context.Scope,
            DailyWidgetModel.ResolveDate(context.Data),
            context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateBriefing(WidgetContext context)
    {
        DailyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new DailyHeroView(model),
            ShowHeader = false,
            ContentPadding = new Thickness(0),
            FillOpacity = 0,
            BorderOpacity = 0,
            Elevated = false
        };
    }

    public static WidgetView CreateTimeline(WidgetContext context)
    {
        DailyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new DailyTimelineView(model),
            HeaderHint = "活跃段与空闲段 · 今天未结束",
            HeaderTrailing = PageKit.Badge("24H", PageKit.T.Accent),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreatePeriodTrend(WidgetContext context)
    {
        DailyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new DailyPeriodChartView(model),
            HeaderHint = "按日汇总击键 · 范围由全局时间维度决定",
            HeaderTrailing = PageKit.Badge(model.Scope.Scope == TimeScope.ThisWeek ? "WEEK" : "MONTH", PageKit.T.Accent),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateApps(WidgetContext context)
    {
        DailyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new DailyAppListView(model),
            HeaderHint = "按活跃时长排序 · 局部翻页",
            HeaderTrailing = PageKit.Badge("APPS", PageKit.T.Green),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateHighlights(WidgetContext context)
    {
        DailyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new DailyHighlightsView(model),
            HeaderHint = model.PeriodMode ? "当前全局范围值得记住的数字" : "单日值得记住的数字",
            HeaderTrailing = PageKit.Badge("HIGHLIGHT", PageKit.T.Orange),
            ContentPadding = new Thickness(12)
        };
    }

    public static WidgetView CreateKeys(WidgetContext context)
    {
        DailyWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new DailyKeyListView(model),
            HeaderHint = model.PeriodMode ? "当前范围最常用的键 · 局部翻页" : "当天最常用的键 · 局部翻页",
            HeaderTrailing = PageKit.Badge("KEYS", PageKit.T.Purple),
            ContentPadding = new Thickness(12)
        };
    }
}

internal sealed class DailyHeroView : UserControl
{
    public DailyHeroView(DailyWidgetModel model)
    {
        ThemeTokens theme = PageKit.T;
        long total = model.PeriodMode
            ? model.Totals.Keys + model.Totals.Clicks + model.Totals.Wheel
            : (model.Day?.Keys ?? 0) + (model.Day?.Clicks ?? 0) + (model.Day?.Wheel ?? 0);
        string topApp = model.PeriodMode
            ? model.Totals.Apps.Count == 0
                ? "暂无应用"
                : PageKit.AggregateApps(model.Totals.Apps.Values).FirstOrDefault()?.AppName ?? "暂无应用"
            : model.Day?.Apps.Values is null
                ? "暂无应用"
                : PageKit.AggregateApps(model.Day.Apps.Values).FirstOrDefault()?.AppName ?? "暂无应用";
        double averageActive = model.ActiveDays == 0 ? 0 : model.Totals.ActiveSeconds / model.ActiveDays;
        double active = model.PeriodMode ? model.Totals.ActiveSeconds : model.Day?.ActiveSeconds ?? 0;

        Grid hero = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.45*,1*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 18,
            RowSpacing = 8
        };

        StackPanel left = new() { Spacing = 5 };
        left.Children.Add(PageKit.Badge(
            model.PeriodMode
                ? model.Scope.Scope == TimeScope.ThisWeek ? "本周汇总" : "本月汇总"
                : model.Day is null ? "无记录" : DailyWidgetModel.Weekday(model.SelectedDate),
            theme.Orange));
        left.Children.Add(PageKit.Label(
            model.PeriodMode ? model.Scope.RangeText : model.SelectedDate.ToString("yyyy.MM.dd"),
            24, theme.Text, FontWeight.SemiBold));
        StackPanel valueRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        valueRow.Children.Add(PageKit.Label(
            PageKit.FormatCount(model.PeriodMode ? model.Totals.Keys : model.Day?.Keys ?? 0),
            32, theme.Text, FontWeight.Bold));
        valueRow.Children.Add(new StackPanel
        {
            Spacing = 1,
            Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                PageKit.Label("次击键", 13, theme.Text, FontWeight.SemiBold),
                PageKit.Muted(model.PeriodMode
                    ? $"{model.ActiveDays} 个活跃天 · 日均 {PageKit.FormatDuration(averageActive)}"
                    : $"{PageKit.FormatDuration(active)} 活跃", 9)
            }
        });
        left.Children.Add(valueRow);
        left.Children.Add(PageKit.Label(
            model.PeriodMode
                ? $"本{(model.Scope.Scope == TimeScope.ThisWeek ? "周" : "月")}最常停在「{topApp}」"
                : $"今天最常停在「{topApp}」",
            11, theme.Cyan, FontWeight.Medium, 330, TextTrimming.CharacterEllipsis));
        left.Children.Add(PageKit.Muted(model.PeriodMode
            ? $"{model.Totals.Sessions.Count} 个活跃段 · 峰值 {model.Totals.PeakApm} APM · {model.Totals.AppSwitches:N0} 次应用切换"
            : model.Day is null
                ? "还没有当天记录"
                : $"{model.Day.Sessions.Count} 个活跃段 · 峰值 {model.Day.PeakApm} APM · {model.Day.AppSwitches} 次应用切换", 9));
        hero.Children.Add(left);

        StackPanel right = new() { Spacing = 4 };
        right.Children.Add(new PageKit.Ring(
            [
                new DonutSlice(model.PeriodMode ? model.Totals.Keys : model.Day?.Keys ?? 0, theme.Accent),
                new DonutSlice(model.PeriodMode ? model.Totals.Clicks : model.Day?.Clicks ?? 0, theme.Green),
                new DonutSlice(model.PeriodMode ? model.Totals.Wheel : model.Day?.Wheel ?? 0, theme.Purple)
            ],
            PageKit.FormatCount(total),
            model.PeriodMode ? "区间输入" : "输入事件") { Height = 94 });
        right.Children.Add(PageKit.Legend(
            (theme.Accent, "击键"),
            (theme.Green, "点击"),
            (theme.Purple, "滚轮")));
        Grid.SetColumn(right, 1);
        hero.Children.Add(right);

        (string Label, string Value, Color Tone)[] metrics = model.PeriodMode
            ?
            [
                ("击键", PageKit.FormatCount(model.Totals.Keys), theme.Accent),
                ("点击", PageKit.FormatCount(model.Totals.Clicks), theme.Green),
                ("滚轮", PageKit.FormatCount(model.Totals.Wheel), theme.Purple),
                ("移动", PageKit.FormatMeters(model.Totals.MoveMeters), theme.Cyan),
                ("活跃", PageKit.FormatDuration(model.Totals.ActiveSeconds), theme.Orange),
                ("日均活跃", PageKit.FormatDuration(averageActive), theme.Muted)
            ]
            :
            [
                ("击键", PageKit.FormatCount(model.Day?.Keys ?? 0), theme.Accent),
                ("点击", PageKit.FormatCount(model.Day?.Clicks ?? 0), theme.Green),
                ("滚轮", PageKit.FormatCount(model.Day?.Wheel ?? 0), theme.Purple),
                ("移动", PageKit.FormatMeters(model.Day?.MoveMeters ?? 0), theme.Cyan),
                ("活跃", PageKit.FormatDuration(model.Day?.ActiveSeconds ?? 0), theme.Orange),
                ("空闲", PageKit.FormatDuration(model.Day?.IdleSeconds ?? 0), theme.Muted)
            ];
        Control strip = MetricStrip(metrics);
        Grid.SetRow(strip, 1);
        Grid.SetColumnSpan(strip, 2);
        hero.Children.Add(strip);

        Content = new Border
        {
            Background = PageKit.Gradient(PageKit.Alpha(theme.Orange, 0.18),
                PageKit.Alpha(theme.Card, 0.94), 120),
            BorderBrush = PageKit.Brush(PageKit.Alpha(theme.Orange, 0.32)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PageKit.R(12, 18)),
            Padding = new Thickness(16, 12),
            BoxShadow = PageKit.Shadow(28, 12, 0.18),
            Child = hero
        };
    }

    private static Control MetricStrip((string Label, string Value, Color Tone)[] metrics)
    {
        Grid strip = new() { ColumnSpacing = 8 };
        for (int i = 0; i < metrics.Length; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            StackPanel cell = new()
            {
                Children =
                {
                    PageKit.Label(metrics[i].Label, 9, PageKit.T.Muted),
                    PageKit.Label(metrics[i].Value, 12.5, metrics[i].Tone, FontWeight.SemiBold)
                }
            };
            Grid.SetColumn(cell, i);
            strip.Children.Add(cell);
        }

        return strip;
    }
}

internal sealed class DailyTimelineView : UserControl
{
    private readonly DayTimeline _timeline;

    public DailyTimelineView(DailyWidgetModel model)
    {
        _timeline = new DayTimeline(model.Day);
        Content = _timeline;
        SizeChanged += (_, _) => _timeline.Height = Math.Clamp(Bounds.Height, 72, 220);
    }
}

internal sealed class DailyPeriodChartView : UserControl
{
    private readonly ReportChart _chart;

    public DailyPeriodChartView(DailyWidgetModel model)
    {
        DateTime[] days = model.Scope.DaySequence().ToArray();
        double[] values = days.Select(day => model.Data.History.TryGetValue(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out DayRecord? record)
                ? (double)record.Keys
                : 0).ToArray();
        _chart = new ReportChart(values,
            days.Select(day => day.ToString("MM-dd", CultureInfo.InvariantCulture)).ToArray(),
            PageKit.T.Accent, line: true);
        Content = _chart;
        SizeChanged += (_, _) => _chart.Height = Math.Clamp(Bounds.Height, 80, 260);
    }
}

internal sealed class DailyAppListView : UserControl
{
    private readonly DailyWidgetModel _model;
    private readonly PageKit.PagedList _list = new(3, 4);

    public DailyAppListView(DailyWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) =>
        {
            int size = Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 42), 2, 8) : 3;
            _list.SetPageSize(size);
        };
    }

    private void Build()
    {
        IReadOnlyList<AppUsage> apps = _model.PeriodMode
            ? PageKit.AggregateApps(_model.Totals.Apps.Values)
            : _model.Day is null ? [] : PageKit.AggregateApps(_model.Day.Apps.Values);
        List<Control> rows = [];
        if (apps.Count == 0)
        {
            rows.Add(PageKit.Empty("暂无应用数据"));
            _list.SetItems(rows);
            return;
        }

        double max = Math.Max(1, apps.Max(app => app.ActiveSeconds));
        int rank = 1;
        foreach (AppUsage app in apps.Take(10))
        {
            rows.Add(PageKit.RankRow(
                rank++,
                app.AppName,
                PageKit.FormatDuration(app.ActiveSeconds),
                app.ActiveSeconds / max,
                rank switch
                {
                    2 => PageKit.T.Green,
                    3 => PageKit.T.Accent,
                    4 => PageKit.T.Purple,
                    5 => PageKit.T.Cyan,
                    _ => PageKit.T.Orange
                },
                $"{PageKit.FormatCount(app.Keys)} 键 · {PageKit.FormatCount(app.Clicks)} 击"));
        }

        _list.SetItems(rows);
    }
}

internal sealed class DailyHighlightsView : UserControl
{
    public DailyHighlightsView(DailyWidgetModel model)
    {
        (string Label, string Value, Color Tone)[] items;
        if (model.PeriodMode)
        {
            DayRecord? best = model.Range.Days.OrderByDescending(day => day.Keys).FirstOrDefault();
            double averageHold = model.Totals.HoldCount == 0 ? 0 : model.Totals.HoldTotalMs / model.Totals.HoldCount;
            items =
            [
                ("活跃天", $"{model.ActiveDays} / {model.Range.CalendarDays} 天", PageKit.T.Green),
                ("最强一天", best is null ? "--" : $"{best.Date:MM-dd} · {PageKit.FormatCount(best.Keys)}", PageKit.T.Orange),
                ("峰值 APM", model.Totals.PeakApm.ToString("N0"), PageKit.T.Accent),
                ("应用切换", $"{model.Totals.AppSwitches:N0} 次", PageKit.T.Purple),
                ("按住样本", $"{model.Totals.HoldCount:N0} 次", PageKit.T.Cyan),
                ("平均按住", $"{averageHold:0} ms", PageKit.T.Text)
            ];
        }
        else
        {
            double longest = model.Day?.Sessions.Count > 0 ? model.Day.Sessions.Max(session => session.Seconds) : 0;
            int peakHour = 0;
            if (model.Day is not null)
            {
                for (int hour = 1; hour < 24; hour++)
                {
                    if (model.Day.HourKeys[hour] > model.Day.HourKeys[peakHour])
                    {
                        peakHour = hour;
                    }
                }
            }

            double averageHold = model.Day is null || model.Day.HoldCount == 0 ? 0 : model.Day.HoldTotalMs / model.Day.HoldCount;
            items =
            [
                ("最长专注", PageKit.FormatDuration(longest), PageKit.T.Green),
                ("峰值 APM", (model.Day?.PeakApm ?? 0).ToString("N0"), PageKit.T.Orange),
                ("最活跃小时", $"{peakHour:00}:00", PageKit.T.Accent),
                ("应用切换", $"{model.Day?.AppSwitches ?? 0:N0} 次", PageKit.T.Purple),
                ("按住样本", $"{model.Day?.HoldCount ?? 0:N0} 次", PageKit.T.Cyan),
                ("平均按住", $"{averageHold:0} ms", PageKit.T.Text)
            ];
        }

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 18,
            RowSpacing = 2
        };
        for (int i = 0; i < items.Length; i++)
        {
            Control row = PageKit.StatRow(items[i].Label, items[i].Value, items[i].Tone);
            Grid.SetColumn(row, i % 2);
            Grid.SetRow(row, i / 2);
            grid.Children.Add(row);
        }

        StackPanel body = new()
        {
            Spacing = 6,
            Children =
            {
                grid,
                PageKit.Muted(model.PeriodMode
                    ? "周报/月报按全局范围自动汇总；导出的日报海报适合留档或分享。"
                    : "导出按钮会生成 4320×5400 的日报海报，适合留档或分享。", 9)
            }
        };
        Content = body;
    }
}

internal sealed class DailyKeyListView : UserControl
{
    private readonly DailyWidgetModel _model;
    private readonly PageKit.PagedList _list = new(3, 4);

    public DailyKeyListView(DailyWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) =>
        {
            int size = Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 34), 2, 10) : 3;
            _list.SetPageSize(size);
        };
    }

    private void Build()
    {
        IReadOnlyDictionary<int, long> counts = _model.KeyCounts;
        List<Control> rows = [];
        if (counts.Count == 0)
        {
            rows.Add(PageKit.Empty("暂无按键数据"));
            _list.SetItems(rows);
            return;
        }

        long max = Math.Max(1, counts.Values.Max());
        int rank = 1;
        foreach ((int vk, long count) in counts.OrderByDescending(pair => pair.Value).Take(12))
        {
            rows.Add(PageKit.RankRow(
                rank++,
                Kms.Input.Devices.PeripheralWear.KeyName(vk),
                PageKit.FormatCount(count),
                count / (double)max,
                rank switch
                {
                    2 => PageKit.T.Purple,
                    3 => PageKit.T.Accent,
                    4 => PageKit.T.Green,
                    _ => PageKit.T.Cyan
                },
                keycap: true));
        }

        _list.SetItems(rows);
    }
}

internal sealed class DayTimeline : Control
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
                9, muted, Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 18);
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
