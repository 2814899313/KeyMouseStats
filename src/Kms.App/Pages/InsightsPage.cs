using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>洞察心流：Deep 分段、时光机、24 小时时间线与长期节奏。</summary>
public sealed class InsightsPage : UserControl
{
    private readonly StatsData _data;
    private readonly DayRecord? _day;
    private readonly FlowReport _flow;
    private readonly StackPanel _timeDetail = new() { Spacing = 4 };
    private readonly FlowTimeline _timeline;
    private readonly PageKit.PagedList _sessions = new(5, 4);
    private readonly WeekCalendar _weekCalendar;
    private long _selectedMs;

    public InsightsPage(StatsData data, string title, string subtitle)
    {
        _data = data;
        _day = ResolveDay(data);
        _flow = BuildFlow(_day, data);
        _selectedMs = InitialCursorMs(_flow, _day);

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,1.05*,1*"),
            RowSpacing = 10
        };
        root.Children.Add(PageKit.PageHeader(
            "FLOW / SESSIONS",
            title,
            subtitle,
            _day?.Date.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd")));

        Control summary = BuildSummary();
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.55*,1*"),
            ColumnSpacing = 14
        };
        StackPanel timelineBody = new() { Spacing = 6 };
        _timeline = new FlowTimeline(_flow, _day, _selectedMs)
        {
            Height = 112
        };
        _timeline.CursorMoved += ms =>
        {
            _selectedMs = ms;
            UpdateTimeDetail(ms);
        };
        timelineBody.Children.Add(_timeline);
        timelineBody.Children.Add(_timeDetail);
        UpdateTimeDetail(_selectedMs);
        main.Children.Add(PageKit.Section(
            "24 小时心流时间线",
            "点击/拖动时间线 → 时光机；橙=深度，蓝=常规，紫=碎片",
            timelineBody,
            PageKit.Badge("TIMEMACHINE", PageKit.T.Accent)));
        Control sessions = PageKit.Section(
            "专注段 Top 5",
            "按深度 / 常规分钟段排序 · 局部翻页",
            SessionList(),
            PageKit.Badge("FOCUS", PageKit.T.Green),
            PageKit.T.Green);
        Grid.SetColumn(sessions, 1);
        main.Children.Add(sessions);
        Grid.SetRow(main, 2);
        root.Children.Add(main);

        _weekCalendar = new WeekCalendar(_data) { Height = 130 };
        Grid bottom = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.2*,1*"),
            ColumnSpacing = 14
        };
        bottom.Children.Add(PageKit.Section(
            "作息矩阵",
            "周 × 24 小时平均活跃分钟 · 用于看长期节奏",
            _weekCalendar,
            PageKit.Badge("WEEK", PageKit.T.Purple),
            PageKit.T.Purple));
        Control insight = PageKit.Section(
            "节奏摘要",
            "从心流分段、切换和峰值自动生成",
            InsightList(),
            PageKit.Badge("INSIGHT", PageKit.T.Cyan),
            PageKit.T.Cyan);
        Grid.SetColumn(insight, 1);
        bottom.Children.Add(insight);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        _timeline.Height = compact ? 80 : 112;
        _sessions.SetPageSize(compact ? 4 : 5);
        _weekCalendar.Height = compact ? 110 : 130;
    }

    private Control BuildSummary()
    {
        double active = _day?.ActiveSeconds ?? 0;
        double idle = _day?.IdleSeconds ?? 0;
        double rate = active + idle > 0 ? active / (active + idle) * 100 : 0;

        return PageKit.StatStrip(
            ("活跃率", $"{rate:0.#}%", PageKit.T.Green),
            ("深度心流", PageKit.FormatDuration(_flow.DeepMs / 1000.0), PageKit.T.Orange),
            ("常规专注", PageKit.FormatDuration(_flow.NormalMs / 1000.0), PageKit.T.Accent),
            ("碎片化", PageKit.FormatDuration(_flow.FragmentedMs / 1000.0), PageKit.T.Purple),
            ("最长专注", PageKit.FormatDuration(_flow.LongestFocusMs / 1000.0), PageKit.T.Cyan),
            ("专注分", $"{_flow.FocusScore:0}", PageKit.T.Text));
    }

    private Control SessionList()
    {
        List<Control> rows = [];
        List<FlowSegment> top = _flow.Segments
            .Where(segment => segment.State is FlowState.Deep or FlowState.Normal)
            .OrderByDescending(segment => segment.LengthMs)
            .Take(5)
            .ToList();
        if (top.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有可用的专注分钟段"));
            _sessions.SetItems(rows);
            return _sessions;
        }

        double max = Math.Max(1, top.Max(segment => segment.LengthMs));
        int rank = 1;
        foreach (FlowSegment segment in top)
        {
            Color tone = segment.State == FlowState.Deep
                ? PageKit.T.Orange
                : rank switch
                {
                    1 => PageKit.T.Accent,
                    2 => PageKit.T.Green,
                    3 => PageKit.T.Purple,
                    _ => PageKit.T.Cyan
                };
            DateTime start = ToLocal(segment.StartMs);
            DateTime end = ToLocal(segment.EndMs);
            rows.Add(PageKit.RankRow(
                rank++,
                $"{start:HH:mm} – {end:HH:mm} · {StateName(segment.State)}",
                PageKit.FormatDuration(segment.LengthMs / 1000.0),
                segment.LengthMs / max,
                tone));
        }

        _sessions.SetItems(rows);
        return _sessions;
    }

    private Control InsightList()
    {
        StackPanel rows = new() { Spacing = 6 };
        int peakHour = 0;
        if (_day is not null)
        {
            for (int hour = 1; hour < 24; hour++)
            {
                if (_day.ActiveHours[hour] > _day.ActiveHours[peakHour])
                {
                    peakHour = hour;
                }
            }
        }

        FlowSegment? longest = _flow.Segments
            .Where(segment => segment.State is FlowState.Deep or FlowState.Normal)
            .OrderByDescending(segment => segment.LengthMs)
            .FirstOrDefault();
        rows.Children.Add(Insight("高峰时段", $"{peakHour:00}:00 – {peakHour + 1:00}:00", PageKit.T.Accent));
        rows.Children.Add(Insight("最专注", longest is null
            ? "--"
            : $"{longest.LengthMs / 60000.0:0.#} 分钟 · {StateName(longest.State)}", PageKit.T.Orange));
        rows.Children.Add(Insight("碎片占比", $"{Share(_flow.FragmentedMs, _flow.ActiveMs):0.#}%", PageKit.T.Purple));
        rows.Children.Add(Insight("上下文切换", $"{_day?.AppSwitches ?? 0:N0} 次", PageKit.T.Cyan));
        return rows;
    }

    private Control Insight(string label, string value, Color color)
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

    private void UpdateTimeDetail(long ms)
    {
        _timeDetail.Children.Clear();
        DateTime local = ToLocal(ms);
        FlowSegment? segment = _flow.SegmentAt(ms);
        FlowSessionRow? session = _flow.SessionAt(ms);
        double density = _flow.DensityAt(ms, 60);

        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        header.Children.Add(PageKit.Label($"{local:HH:mm}", 18, PageKit.T.Text, FontWeight.SemiBold));
        Border state = PageKit.Badge(StateName(_flow.StateAt(ms)), StateTone(_flow.StateAt(ms)));
        state.Margin = new Thickness(8, 0, 0, 0);
        state.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Grid.SetColumn(state, 1);
        header.Children.Add(state);
        _timeDetail.Children.Add(header);

        string appName = session is null
            ? "没有前台记录"
            : string.IsNullOrEmpty(session.ProcessName)
                ? System.IO.Path.GetFileName(session.ProcessPath)
                : session.ProcessName;
        _timeDetail.Children.Add(PageKit.Muted(
            $"前台：{appName} · 近 2 分钟 {density:0.#} 键/分 · 该段 {PageKit.FormatDuration((segment?.LengthMs ?? 0) / 1000.0)}",
            9));
    }

    private static FlowReport BuildFlow(DayRecord? day, StatsData data)
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

                rows.Add(FlowRow(id++, cursor, next, path, CategoryFor(path), day));
                cursor = next;
            }
        }

        DateTimeOffset? now = day.Date == DateTime.Today ? DateTimeOffset.Now : null;
        return FlowAnalyzer.Analyze(rows, BuildBursts(rows), null, now);
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

    private static long InitialCursorMs(FlowReport flow, DayRecord? day)
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

    private static DateTime ToLocal(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().DateTime;

    private static long ToUnixMs(DateTime local) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    private static string StateName(FlowState state) => state switch
    {
        FlowState.Deep => "深度心流",
        FlowState.Normal => "常规专注",
        FlowState.Fragmented => "碎片化",
        _ => "空闲"
    };

    private static Color StateTone(FlowState state) => state switch
    {
        FlowState.Deep => PageKit.T.Orange,
        FlowState.Normal => PageKit.T.Accent,
        FlowState.Fragmented => PageKit.T.Purple,
        _ => PageKit.T.Muted
    };

    private static double Share(long part, long total) =>
        total <= 0 ? 0 : part * 100.0 / total;

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

    private sealed class FlowTimeline : Control
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

            context.FillRectangle(PageKit.Brush(PageKit.Alpha(theme.Line, 0.16)), new Rect(plot.Left, bandTop, plot.Width, bandHeight), 8);
            context.FillRectangle(PageKit.Brush(PageKit.Alpha(theme.Cyan, 0.05)), new Rect(plot.Left, idleTop, plot.Width, 12), 6);

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
                context.FillRectangle(
                    PageKit.Gradient(PageKit.Alpha(tone, 0.55), tone, 0),
                    new Rect(x, bandTop, width, bandHeight),
                    4);
            }

            foreach (ActiveSession idle in _day.IdlePeriods)
            {
                double start = Math.Clamp((idle.Start - _day.Date).TotalHours / 24.0, 0, 1);
                double end = Math.Clamp((idle.End - _day.Date).TotalHours / 24.0, 0, 1);
                double x = plot.Left + plot.Width * start;
                double width = Math.Max(1, plot.Width * (end - start));
                context.FillRectangle(PageKit.Brush(PageKit.Alpha(theme.Muted, 0.3)), new Rect(x, idleTop + 3, width, 5), 2.5f);
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
                context, ToLocal(_cursorMs).ToString("HH:mm"), new Point(cursor - 15, 2),
                8, PageKit.Brush(theme.Text), Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 34);

            if (h >= 110)
            {
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, "Deep", new Point(plot.Left, bandTop + 4), 8, PageKit.Brush(theme.Orange),
                    Kms.Render.Primitives.TextBlock.DefaultFamily);
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, "常规", new Point(plot.Left, bandTop + bandHeight * 0.38), 8, PageKit.Brush(theme.Accent),
                    Kms.Render.Primitives.TextBlock.DefaultFamily);
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, "碎片", new Point(plot.Left, bandTop + bandHeight * 0.7), 8, PageKit.Brush(theme.Purple),
                    Kms.Render.Primitives.TextBlock.DefaultFamily);
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, "空闲", new Point(plot.Left, idleTop + 1), 8, PageKit.Brush(PageKit.Alpha(theme.Muted, 0.8)),
                    Kms.Render.Primitives.TextBlock.DefaultFamily);
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
            _cursorMs = ToUnixMs(day.AddHours(fraction * 24));
            InvalidateVisual();
            CursorMoved?.Invoke(_cursorMs);
        }

        private Rect PlotRect() => new(10, 12, Math.Max(1, Bounds.Width - 20), Math.Max(1, Bounds.Height - 34));

        private double LocalFraction(long ms) =>
            Math.Clamp(ToLocal(ms).TimeOfDay.TotalHours / 24.0, 0, 1);
    }

    private sealed class WeekCalendar : Control
    {
        private readonly StatsData _data;

        public WeekCalendar(StatsData data) => _data = data;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            ThemeTokens theme = PageKit.T;
            double[,] sums = new double[7, 24];
            int[] days = new int[7];
            foreach (DayRecord day in _data.History.Values)
            {
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
                        PageKit.Brush(fraction <= 0 ? PageKit.Alpha(theme.Line, 0.16) : PageKit.Alpha(tone, 0.12 + 0.75 * fraction)),
                        new Rect(labelWidth + hour * cellW, top + row * cellH, cellW - 1.5, cellH - 1.5),
                        2);
                }
            }
        }
    }

    private sealed class ActiveBars : Control
    {
        private readonly DayRecord? _day;

        public ActiveBars(DayRecord? day) => _day = day;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            ThemeTokens theme = PageKit.T;
            double[] values = _day?.ActiveHours ?? new double[24];
            double max = Math.Max(1, values.Max());
            double gap = 5;
            double width = Math.Max(4, (Bounds.Width - gap * 23) / 24);
            for (int hour = 0; hour < 24; hour++)
            {
                double x = hour * (width + gap);
                double height = Math.Max(2, (Bounds.Height - 22) * values[hour] / max);
                Color tone = hour switch
                {
                    >= 22 or <= 5 => theme.Purple,
                    >= 9 and <= 17 => theme.Accent,
                    _ => theme.Cyan
                };
                context.FillRectangle(PageKit.Brush(PageKit.Alpha(tone, 0.18)), new Rect(x, 0, width, Math.Max(2, Bounds.Height - 22)), 3);
                context.FillRectangle(PageKit.Gradient(PageKit.Alpha(tone, 0.45), tone, 90),
                    new Rect(x, Bounds.Height - 22 - height, width, height), 3);
                if (hour % 3 == 0)
                {
                    Kms.Render.Primitives.TextBlock.DrawText(
                        context, hour.ToString("00"), new Point(x + width / 2 - 6, Bounds.Height - 16),
                        8, PageKit.Brush(theme.Muted), Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 18);
                }
            }
        }
    }
}
