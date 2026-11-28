using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Core.Time;
using Kms.Render.Tokens;

namespace Kms.App.Widgets;

internal sealed class InsightsWidgetModel : WidgetPageModel
{
    public InsightsWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        Analysis = InsightsAnalysis.Build(Data, scope, AppContext.CategoryService);
    }

    public AppPageContext AppContext { get; }

    public bool IsAppFiltered => !AppContext.IsAll;

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public InsightsAnalysis Analysis { get; }
}

internal static class InsightsWidgets
{
    private static InsightsWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("insights", () => new InsightsWidgetModel(
            context.Data,
            context.Scope,
            context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpiFocusTime(WidgetContext context)
    {
        InsightsAnalysis analysis = Resolve(context).Analysis;
        return MicroKpi.Card(
            "深度专注",
            MicroKpi.DurationMs(analysis.DeepMs),
            $"常规 {MicroKpi.DurationMs(analysis.NormalMs)}",
            PageKit.T.Orange);
    }

    public static WidgetView CreateKpiSessions(WidgetContext context)
    {
        InsightsAnalysis analysis = Resolve(context).Analysis;
        double average = analysis.FocusSegments.Count > 0
            ? analysis.FocusSegments.Average(static segment => segment.LengthMs / 60000.0)
            : 0;
        return MicroKpi.Card(
            "有效专注段",
            analysis.FocusSegments.Count.ToString("N0", CultureInfo.InvariantCulture),
            $"平均 {average:0.#} 分钟",
            PageKit.T.Green);
    }

    public static WidgetView CreateKpiFlowScore(WidgetContext context)
    {
        InsightsAnalysis analysis = Resolve(context).Analysis;
        return MicroKpi.Card(
            "心流节律",
            $"{analysis.FocusScore:0.#}",
            "0–100 · 越高越稳定",
            PageKit.T.Cyan);
    }

    public static WidgetView CreateSummary(WidgetContext context)
    {
        InsightsWidgetModel model = Resolve(context);
        InsightsAnalysis analysis = model.Analysis;
        double rate = analysis.ActiveSeconds + analysis.IdleSeconds > 0
            ? analysis.ActiveSeconds / (analysis.ActiveSeconds + analysis.IdleSeconds) * 100
            : 0;
        Control body = PageKit.StatStripBody(
            ("活跃率", $"{rate:0.#}%", PageKit.T.Green),
            ("深度心流", PageKit.FormatDuration(analysis.DeepMs / 1000.0), PageKit.T.Orange),
            ("常规专注", PageKit.FormatDuration(analysis.NormalMs / 1000.0), PageKit.T.Accent),
            ("碎片化", PageKit.FormatDuration(analysis.FragmentedMs / 1000.0), PageKit.T.Purple),
            ("最长专注", PageKit.FormatDuration(analysis.LongestFocusMs / 1000.0), PageKit.T.Cyan),
            ("专注分", $"{analysis.FocusScore:0}", PageKit.T.Text));
        return new WidgetView
        {
            Content = body,
            ShowHeader = false,
            ContentPadding = new Thickness(12),
            Elevated = true
        };
    }

    public static WidgetView CreateFocusPulse(WidgetContext context)
    {
        InsightsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new FocusPulseView(model),
            HeaderHint = model.Analysis.DetailDay is null
                ? "当前范围没有可用会话"
                : model.Scope.IsToday
                    ? "点击/拖动时间线 → 时光机；橙=深度，蓝=常规，紫=碎片"
                    : $"时间线取最近活跃日 {model.Analysis.DetailDay.Date:MM-dd} · 点击/拖动查看时光机",
            HeaderTrailing = PageKit.Badge("TIMEMACHINE", PageKit.T.Accent),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateSessions(WidgetContext context)
    {
        InsightsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new FocusSessionsView(model),
            HeaderHint = model.Scope.IsToday
                ? "按深度 / 常规分钟段排序 · 局部翻页"
                : $"{model.Scope.Caption} · 跨日专注段汇总",
            HeaderTrailing = PageKit.Badge("FOCUS", PageKit.T.Green),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateCalendar(WidgetContext context)
    {
        InsightsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new InsightsCalendarView(model),
            HeaderHint = model.Scope.IsToday
                ? "今日 24 小时活跃分钟"
                : $"{model.Scope.Caption} 周 × 24 小时平均活跃分钟",
            HeaderTrailing = PageKit.Badge(model.Scope.IsToday ? "TODAY" : "WEEK", PageKit.T.Purple),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateRhythm(WidgetContext context)
    {
        InsightsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new InsightsRhythmView(model),
            HeaderHint = "从当前全局范围的心流分段、切换和峰值自动生成",
            HeaderTrailing = PageKit.Badge("INSIGHT", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }
}

internal sealed class FocusPulseView : UserControl
{
    private readonly InsightsWidgetModel _model;
    private readonly FlowTimeline _timeline;
    private readonly StackPanel _detail = new() { Spacing = 4 };
    private long _selectedMs;

    public FocusPulseView(InsightsWidgetModel model)
    {
        _model = model;
        _selectedMs = InsightsAnalysis.InitialCursorMs(model.Analysis.DetailFlow, model.Analysis.DetailDay);
        _timeline = new FlowTimeline(model.Analysis.DetailFlow, model.Analysis.DetailDay, _selectedMs);
        _timeline.CursorMoved += ms =>
        {
            _selectedMs = ms;
            UpdateDetail(ms);
        };
        StackPanel body = new() { Spacing = 6 };
        body.Children.Add(_timeline);
        body.Children.Add(_detail);
        Content = body;
        UpdateDetail(_selectedMs);
        SizeChanged += (_, _) =>
        {
            _timeline.Height = Math.Clamp(Bounds.Height - 68, 76, 260);
        };
    }

    private void UpdateDetail(long ms)
    {
        _detail.Children.Clear();
        DateTime local = InsightsAnalysis.ToLocal(ms);
        FlowSegment? segment = _model.Analysis.DetailFlow.SegmentAt(ms);
        FlowSessionRow? session = _model.Analysis.DetailFlow.SessionAt(ms);
        double density = _model.Analysis.DetailFlow.DensityAt(ms, 60);

        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        header.Children.Add(PageKit.Label($"{local:HH:mm}", 18, PageKit.T.Text, FontWeight.SemiBold));
        Border state = PageKit.Badge(InsightsAnalysis.StateName(_model.Analysis.DetailFlow.StateAt(ms)),
            InsightsAnalysis.StateTone(_model.Analysis.DetailFlow.StateAt(ms)));
        state.Margin = new Thickness(8, 0, 0, 0);
        state.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(state, 1);
        header.Children.Add(state);
        _detail.Children.Add(header);

        string appName = session is null
            ? "没有前台记录"
            : string.IsNullOrEmpty(session.ProcessName)
                ? System.IO.Path.GetFileName(session.ProcessPath)
                : session.ProcessName;
        _detail.Children.Add(PageKit.Muted(
            $"前台：{appName} · 近 2 分钟 {density:0.#} 键/分 · 该段 {PageKit.FormatDuration((segment?.LengthMs ?? 0) / 1000.0)}",
            9));
    }
}

internal sealed class FocusSessionsView : UserControl
{
    private readonly InsightsWidgetModel _model;
    private readonly PageKit.PagedList _list = new(5, 4);

    public FocusSessionsView(InsightsWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) =>
        {
            int size = Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 38), 3, 12) : 5;
            _list.SetPageSize(size);
        };
    }

    private void Build()
    {
        List<Control> rows = [];
        List<FlowSegment> top = _model.Analysis.FocusSegments
            .OrderByDescending(segment => segment.LengthMs)
            .Take(12)
            .ToList();
        if (top.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有可用的专注分钟段"));
            _list.SetItems(rows);
            return;
        }

        double max = Math.Max(1, top.Max(segment => segment.LengthMs));
        int rank = 1;
        foreach (FlowSegment segment in top)
        {
            Color tone = rank switch
            {
                1 => PageKit.T.Accent,
                2 => PageKit.T.Green,
                3 => PageKit.T.Purple,
                _ => PageKit.T.Cyan
            };
            if (segment.State == FlowState.Deep)
            {
                tone = PageKit.T.Orange;
            }

            DateTime start = InsightsAnalysis.ToLocal(segment.StartMs);
            DateTime end = InsightsAnalysis.ToLocal(segment.EndMs);
            string prefix = _model.Scope.IsToday ? "" : $"{start:MM-dd} ";
            rows.Add(PageKit.RankRow(
                rank++,
                $"{prefix}{start:HH:mm} – {end:HH:mm} · {InsightsAnalysis.StateName(segment.State)}",
                PageKit.FormatDuration(segment.LengthMs / 1000.0),
                segment.LengthMs / max,
                tone));
        }

        _list.SetItems(rows);
    }
}

internal sealed class InsightsRhythmView : UserControl
{
    private readonly InsightsWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 6 };

    public InsightsRhythmView(InsightsWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        InsightsAnalysis analysis = _model.Analysis;
        FlowSegment? longest = analysis.FocusSegments.OrderByDescending(segment => segment.LengthMs).FirstOrDefault();
        _body.Children.Add(Insight("高峰时段", $"{analysis.PeakHour:00}:00 – {analysis.PeakHour + 1:00}:00", PageKit.T.Accent));
        _body.Children.Add(Insight("最专注", longest is null
            ? "--"
            : $"{longest.LengthMs / 60000.0:0.#} 分钟 · {InsightsAnalysis.StateName(longest.State)}", PageKit.T.Orange));
        if (Bounds.Height is 0 or >= 120)
        {
            _body.Children.Add(Insight("碎片占比",
                $"{InsightsAnalysis.Share(analysis.FragmentedMs, analysis.ActiveMs):0.#}%", PageKit.T.Purple));
            _body.Children.Add(Insight("上下文切换", $"{analysis.AppSwitches:N0} 次", PageKit.T.Cyan));
        }
    }

    private static Control Insight(string label, string value, Color color)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = PageKit.Brush(PageKit.Alpha(color, 0.14)),
            Margin = new Thickness(0, 0, 8, 0),
            Child = PageKit.Label("◆", 8, color)
        });
        StackPanel text = new() { Spacing = 1 };
        text.Children.Add(PageKit.Muted(label, 9));
        text.Children.Add(PageKit.Label(value, 11, PageKit.T.Text, FontWeight.SemiBold, 210, TextTrimming.CharacterEllipsis));
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }
}

internal sealed class InsightsCalendarView : UserControl
{
    private readonly InsightsWidgetModel _model;
    private readonly WeekCalendar _calendar;

    public InsightsCalendarView(InsightsWidgetModel model)
    {
        _model = model;
        _calendar = new WeekCalendar(model.Data, model.Scope);
        Content = _calendar;
        SizeChanged += (_, _) => _calendar.Height = Math.Clamp(Bounds.Height, 90, 320);
    }
}

internal sealed class InsightsAnalysis
{
    public long DeepMs { get; private set; }
    public long NormalMs { get; private set; }
    public long FragmentedMs { get; private set; }
    public long LongestFocusMs { get; private set; }
    public double ActiveSeconds { get; private set; }
    public double IdleSeconds { get; private set; }
    public long AppSwitches { get; private set; }
    public double FocusScore { get; private set; }
    public int PeakHour { get; private set; }
    public List<FlowSegment> FocusSegments { get; } = [];
    public DayRecord? DetailDay { get; private set; }
    public FlowReport DetailFlow { get; private set; } = new();

    public long ActiveMs => DeepMs + NormalMs + FragmentedMs;

    public static InsightsAnalysis Build(
        StatsData data,
        TimeScopeRange scope,
        AppCategoryService? categories = null)
    {
        InsightsAnalysis result = new();
        StatsRange range = StatsRange.Build(data, scope);
        double scoreWeighted = 0;
        double scoreWeight = 0;

        foreach (DayRecord day in range.Days)
        {
            FlowReport flow = BuildFlow(day, data, categories);
            result.DeepMs += flow.DeepMs;
            result.NormalMs += flow.NormalMs;
            result.FragmentedMs += flow.FragmentedMs;
            result.LongestFocusMs = Math.Max(result.LongestFocusMs, flow.LongestFocusMs);
            result.ActiveSeconds += day.ActiveSeconds;
            result.IdleSeconds += day.IdleSeconds;
            result.AppSwitches += day.AppSwitches;

            double weight = flow.ActiveMs;
            if (weight > 0)
            {
                scoreWeighted += flow.FocusScore * weight;
                scoreWeight += weight;
            }

            foreach (FlowSegment segment in flow.Segments)
            {
                if (segment.State is FlowState.Deep or FlowState.Normal && segment.LengthMs > 0)
                {
                    result.FocusSegments.Add(segment);
                }
            }

            if (day.Sessions.Count > 0)
            {
                result.DetailDay = day;
                result.DetailFlow = flow;
            }
        }

        result.FocusScore = scoreWeight > 0 ? Math.Round(scoreWeighted / scoreWeight, 1) : 0;
        int peak = 0;
        for (int hour = 1; hour < 24; hour++)
        {
            if (range.Totals.HourKeys[hour] > range.Totals.HourKeys[peak])
            {
                peak = hour;
            }
        }

        result.PeakHour = peak;
        return result;
    }

    public static string StateName(FlowState state) => state switch
    {
        FlowState.Deep => "深度心流",
        FlowState.Normal => "常规专注",
        FlowState.Fragmented => "碎片化",
        _ => "空闲"
    };

    public static Color StateTone(FlowState state) => state switch
    {
        FlowState.Deep => PageKit.T.Orange,
        FlowState.Normal => PageKit.T.Accent,
        FlowState.Fragmented => PageKit.T.Purple,
        _ => PageKit.T.Muted
    };

    public static double Share(long part, long total) => total <= 0 ? 0 : part * 100.0 / total;

    public static DateTime ToLocal(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().DateTime;

    public static long ToUnixMs(DateTime local) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    public static long InitialCursorMs(FlowReport flow, DayRecord? day)
    {
        if (flow.TryGetActiveRange(out long first, out long last))
        {
            if (day?.Date == DateTime.Today)
            {
                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                return Math.Clamp(now, first, Math.Max(first, last - 1));
            }

            return Math.Max(first, last - 1);
        }

        return ToUnixMs((day?.Date ?? DateTime.Today).AddHours(12));
    }

    private static FlowReport BuildFlow(
        DayRecord? day,
        StatsData data,
        AppCategoryService? categories)
    {
        if (day is null || day.Sessions.Count == 0)
        {
            return new FlowReport();
        }

        List<FlowSessionRow> rows = [];
        long id = 1;
        foreach (ActiveSession session in day.Sessions)
        {
            DateTime start = session.Start;
            DateTime end = session.End;
            double spanMs = (end - start).TotalMilliseconds;
            if (spanMs <= 0)
            {
                continue;
            }

            List<KeyValuePair<string, double>> apps = session.AppSeconds
                .Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .ToList();
            if (apps.Count == 0)
            {
                rows.Add(FlowRow(id++, start, end, "", 5, day));
                continue;
            }

            double total = apps.Sum(pair => pair.Value);
            DateTime cursor = start;
            foreach ((string path, double seconds) in apps)
            {
                DateTime next = cursor.AddMilliseconds(spanMs * seconds / total);
                if (next > end)
                {
                    next = end;
                }

                int resolved = categories?.ResolveApp(path).LegacyId ?? CategoryFor(path);
                // 写入端已按窗口标题归类时，优先保留会话上的精确分类。
                int category = resolved == AppCategoryBuiltins.UncategorizedCategory.LegacyId
                    && session.CategoryId >= 0
                        ? session.CategoryId
                        : resolved;
                rows.Add(FlowRow(id++, cursor, next, path, category, day));
                cursor = next;
            }
        }

        DateTimeOffset? now = day.Date == DateTime.Today ? DateTimeOffset.Now : null;
        return FlowAnalyzer.Analyze(
            rows,
            BuildBursts(rows),
            null,
            now,
            categories is null ? null : categories.IsWork);
    }

    private static List<FlowBurstRow> BuildBursts(List<FlowSessionRow> rows)
    {
        List<FlowBurstRow> bursts = [];
        foreach (FlowSessionRow row in rows)
        {
            long span = row.EndMs - row.StartMs;
            if (span <= 0 || row.KeyCount <= 0)
            {
                continue;
            }

            long bucket = 60_000;
            long index = 0;
            for (long start = row.StartMs; start < row.EndMs; start += bucket)
            {
                long end = Math.Min(start + bucket, row.EndMs);
                long count = Math.Max(1, (long)Math.Round(row.KeyCount * (end - start) / (double)span));
                bursts.Add(new FlowBurstRow
                {
                    Id = row.Id * 1000 + index++,
                    StartMs = start,
                    EndMs = end,
                    KeyCount = count,
                    IkiAvgMs = 120,
                    IkiVar = 1800,
                    BackspaceRate = 0.02,
                    CategoryId = row.CategoryId,
                    ProcessPath = row.ProcessPath
                });
            }
        }

        return bursts;
    }

    private static FlowSessionRow FlowRow(
        long id,
        DateTime start,
        DateTime end,
        string path,
        int category,
        DayRecord day)
    {
        double seconds = Math.Max(0, (end - start).TotalSeconds);
        double keysPerSecond = day.ActiveSeconds > 0 ? day.Keys / day.ActiveSeconds : 0;
        long keys = (long)Math.Round(keysPerSecond * seconds);
        return new FlowSessionRow
        {
            Id = id,
            StartMs = ToUnixMs(start),
            EndMs = ToUnixMs(end),
            ProcessPath = path,
            ProcessName = System.IO.Path.GetFileName(path),
            KeyCount = keys,
            InputCount = keys,
            CategoryId = category,
            ActiveMs = (long)(seconds * 1000)
        };
    }

    private static int CategoryFor(string path)
    {
        string name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return name switch
        {
            "code.exe" or "code - insiders.exe" or "devenv.exe" or "rider64.exe" or "idea64.exe"
                or "pycharm64.exe" or "sublime_text.exe" or "notepad++.exe" or "windowsterminal.exe"
                or "wt.exe" or "powershell.exe" or "cmd.exe" or "keymousestats.exe" => 0,
            "winword.exe" or "excel.exe" or "powerpnt.exe" or "outlook.exe" or "onenote.exe"
                or "notion.exe" or "obsidian.exe" or "wps.exe" => 5,
            "matlab.exe" or "rstudio.exe" or "zotero.exe" or "acrobat.exe" or "acrord32.exe" => 6,
            "typora.exe" or "scrivener.exe" => 7,
            "chrome.exe" or "msedge.exe" or "firefox.exe" or "brave.exe" => 1,
            "steam.exe" or "discord.exe" or "qq.exe" or "wechat.exe" => 2,
            _ => 3
        };
    }
}

internal sealed class FlowTimeline : Control
{
    private readonly FlowReport _flow;
    private readonly DayRecord? _day;
    private long _cursorMs;

    public FlowTimeline(FlowReport flow, DayRecord? day, long cursorMs)
    {
        _flow = flow;
        _day = day;
        _cursorMs = cursorMs;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, e) =>
        {
            SetCursor(e.GetPosition(this));
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                SetCursor(e.GetPosition(this));
                e.Handled = true;
            }
        };
    }

    public event Action<long>? CursorMoved;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        Rect plot = PlotRect();
        IBrush muted = PageKit.Brush(theme.Muted);
        double h = Math.Max(72, Bounds.Height);
        double bandTop = 20;
        double bandHeight = Math.Clamp(h * 0.34, 24, 52);
        double idleTop = bandTop + bandHeight + 6;
        double axisTop = h - 14;
        double lineBottom = axisTop - 2;

        context.FillRectangle(PageKit.Brush(PageKit.Alpha(theme.Line, 0.16)),
            new Rect(plot.Left, bandTop, plot.Width, bandHeight), 8);
        context.FillRectangle(PageKit.Brush(PageKit.Alpha(theme.Cyan, 0.05)),
            new Rect(plot.Left, idleTop, plot.Width, 12), 6);

        for (int hour = 0; hour <= 24; hour += 3)
        {
            double x = plot.Left + plot.Width * hour / 24.0;
            context.DrawLine(new Pen(PageKit.Brush(PageKit.Alpha(theme.Line, 0.5)), 1),
                new Point(x, 20), new Point(x, lineBottom));
            Kms.Render.Primitives.TextBlock.DrawText(
                context, hour == 24 ? "24" : hour.ToString("00"), new Point(x - 7, axisTop),
                8, muted, Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 18);
        }

        if (_day is null)
        {
            Kms.Render.Primitives.TextBlock.DrawText(
                context, "暂无会话", new Point(plot.Left, bandTop + 12), 12, muted,
                Kms.Render.Primitives.TextBlock.DefaultFamily);
            return;
        }

        foreach (FlowSegment segment in _flow.Segments)
        {
            if (segment.State == FlowState.Idle || segment.LengthMs <= 0)
            {
                continue;
            }

            double start = LocalFraction(segment.StartMs);
            double end = LocalFraction(segment.EndMs);
            double x = plot.Left + plot.Width * start;
            double width = Math.Max(2, plot.Width * (end - start));
            Color tone = segment.State switch
            {
                FlowState.Deep => theme.Orange,
                FlowState.Normal => theme.Accent,
                _ => theme.Purple
            };
            context.FillRectangle(PageKit.Gradient(PageKit.Alpha(tone, 0.55), tone, 0),
                new Rect(x, bandTop, width, bandHeight), 4);
        }

        foreach (ActiveSession idle in _day.IdlePeriods)
        {
            double start = Math.Clamp((idle.Start - _day.Date).TotalHours / 24.0, 0, 1);
            double end = Math.Clamp((idle.End - _day.Date).TotalHours / 24.0, 0, 1);
            double x = plot.Left + plot.Width * start;
            double width = Math.Max(1, plot.Width * (end - start));
            context.FillRectangle(PageKit.Brush(PageKit.Alpha(theme.Muted, 0.3)),
                new Rect(x, idleTop + 3, width, 5), 2.5f);
        }

        DateTime now = DateTime.Now;
        if (_day.Date == now.Date)
        {
            double x = plot.Left + plot.Width * Math.Clamp(now.TimeOfDay.TotalHours / 24.0, 0, 1);
            context.DrawLine(new Pen(PageKit.Brush(PageKit.Alpha(theme.Orange, 0.6)), 1, new DashStyle([3, 3], 0)),
                new Point(x, 14), new Point(x, lineBottom));
        }

        double cursor = plot.Left + plot.Width * LocalFraction(_cursorMs);
        context.DrawLine(new Pen(PageKit.Brush(theme.Text), 1.5),
            new Point(cursor, 16), new Point(cursor, lineBottom));
        context.FillRectangle(PageKit.Brush(theme.Text), new Rect(cursor - 2, 18, 4, 4), 2);
        Kms.Render.Primitives.TextBlock.DrawText(
            context, InsightsAnalysis.ToLocal(_cursorMs).ToString("HH:mm"), new Point(cursor - 15, 2),
            8, PageKit.Brush(theme.Text), Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 34);

        if (h >= 110)
        {
            Kms.Render.Primitives.TextBlock.DrawText(context, "Deep", new Point(plot.Left, bandTop + 4),
                8, PageKit.Brush(theme.Orange), Kms.Render.Primitives.TextBlock.DefaultFamily);
            Kms.Render.Primitives.TextBlock.DrawText(context, "常规", new Point(plot.Left, bandTop + bandHeight * 0.38),
                8, PageKit.Brush(theme.Accent), Kms.Render.Primitives.TextBlock.DefaultFamily);
            Kms.Render.Primitives.TextBlock.DrawText(context, "碎片", new Point(plot.Left, bandTop + bandHeight * 0.7),
                8, PageKit.Brush(theme.Purple), Kms.Render.Primitives.TextBlock.DefaultFamily);
            Kms.Render.Primitives.TextBlock.DrawText(context, "空闲", new Point(plot.Left, idleTop + 1),
                8, PageKit.Brush(PageKit.Alpha(theme.Muted, 0.8)), Kms.Render.Primitives.TextBlock.DefaultFamily);
        }
    }

    private void SetCursor(Point point)
    {
        Rect plot = PlotRect();
        if (plot.Width <= 0)
        {
            return;
        }

        double fraction = Math.Clamp((point.X - plot.Left) / plot.Width, 0, 1);
        DateTime day = _day?.Date ?? DateTime.Today;
        _cursorMs = InsightsAnalysis.ToUnixMs(day.AddHours(fraction * 24));
        InvalidateVisual();
        CursorMoved?.Invoke(_cursorMs);
    }

    private Rect PlotRect() => new(10, 12, Math.Max(1, Bounds.Width - 20), Math.Max(1, Bounds.Height - 34));

    private double LocalFraction(long ms) =>
        Math.Clamp(InsightsAnalysis.ToLocal(ms).TimeOfDay.TotalHours / 24.0, 0, 1);
}

internal sealed class WeekCalendar : Control
{
    private readonly StatsData _data;
    private readonly TimeScopeRange _scope;

    public WeekCalendar(StatsData data, TimeScopeRange scope)
    {
        _data = data;
        _scope = scope;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        ThemeTokens theme = PageKit.T;
        double[,] sums = new double[7, 24];
        int[] days = new int[7];
        foreach (DayRecord day in _data.History.Values)
        {
            if (!_scope.Contains(day.Date))
            {
                continue;
            }

            int index = ((int)day.Date.DayOfWeek + 6) % 7;
            days[index]++;
            for (int hour = 0; hour < 24; hour++)
            {
                sums[index, hour] += day.ActiveHours[hour];
            }
        }

        double max = 1;
        for (int row = 0; row < 7; row++)
        {
            for (int hour = 0; hour < 24; hour++)
            {
                if (days[row] > 0)
                {
                    sums[row, hour] /= days[row];
                    max = Math.Max(max, sums[row, hour]);
                }
            }
        }

        double labelWidth = 28;
        double top = 16;
        double cellW = Math.Max(4, (Bounds.Width - labelWidth - 4) / 24);
        double cellH = Math.Max(5, (Bounds.Height - top - 18) / 7);
        string[] labels = ["一", "二", "三", "四", "五", "六", "日"];
        IBrush muted = PageKit.Brush(theme.Muted);

        for (int hour = 0; hour < 24; hour += 3)
        {
            Kms.Render.Primitives.TextBlock.DrawText(
                context, hour.ToString("00"), new Point(labelWidth + hour * cellW, 0),
                8, muted, Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 18);
        }

        for (int row = 0; row < 7; row++)
        {
            Kms.Render.Primitives.TextBlock.DrawText(
                context, labels[row], new Point(2, top + row * cellH + cellH / 2 - 5),
                8, muted, Kms.Render.Primitives.TextBlock.DefaultFamily, maxWidth: 20);

            for (int hour = 0; hour < 24; hour++)
            {
                double value = days[row] == 0 ? 0 : sums[row, hour];
                double fraction = Math.Clamp(value / max, 0, 1);
                Color tone = row >= 5 ? theme.Purple : theme.Accent;
                context.FillRectangle(
                    PageKit.Brush(fraction <= 0
                        ? PageKit.Alpha(theme.Line, 0.16)
                        : PageKit.Alpha(tone, 0.12 + 0.75 * fraction)),
                    new Rect(labelWidth + hour * cellW, top + row * cellH, cellW - 1.5, cellH - 1.5),
                    2);
            }
        }
    }
}
