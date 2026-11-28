using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Core.Time;
using Kms.Input.Devices;
using Kms.Render.Primitives;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Kms.App.Widgets;

internal enum KpiKind
{
    Keys,
    Clicks,
    Wheel,
    Distance,
    Active,
    Sessions,
    KeyRate,
    Apm
}

/// <summary>
/// 总览页的页面级聚合模型。StatsRange、基线、Top 键排名只在这里计算一次，
/// 8 张 KPI 与分布/排行/洞察模块共享同一份快照。
/// </summary>
internal sealed class OverviewWidgetModel : WidgetPageModel
{
    private readonly Dictionary<int, long> _keyCounts = [];

    public OverviewWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        InputService? service,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        Service = service;
        Range = StatsRange.Build(Data, scope);
        BaseTotals = StatsRange.Build(Data, scope.Start, scope.End.AddDays(-1)).Totals;
        TodayRecord = ResolveToday(Data);
        ProfileTotals = AppContext.IsAll
            ? Range.Totals
            : AppScopeData.BuildProfileTotals(AppContext.Profile, Range.Totals);
        Rank = ProfileTotals.KeyCounts
            .Where(static pair => pair.Value > 0)
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .ToList();
        InsightText = BuildInsightText();
    }

    public AppPageContext AppContext { get; }

    public bool IsAppFiltered => !AppContext.IsAll;

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public InputService? Service { get; }

    public StatsRange Range { get; }

    public DayRecord ProfileTotals { get; }

    public DayRecord RangeTotals => ProfileTotals;

    public DayRecord BaseTotals { get; }

    public DayRecord? TodayRecord { get; }

    public IReadOnlyList<KeyValuePair<int, long>> Rank { get; }

    public string InsightText { get; }

    public long KeysPerMinute => IsAppFiltered
        ? PerMinute(ProfileTotals.Keys)
        : Service?.KeysPerMin ?? 0;

    public long Apm => IsAppFiltered
        ? PerMinute(ProfileTotals.Keys + ProfileTotals.Clicks)
        : Service?.Apm ?? 0;

    public long ClicksPerMinute => IsAppFiltered
        ? PerMinute(ProfileTotals.Clicks)
        : Service?.ClicksPerMin ?? 0;

    public string PeakLabel => IsAppFiltered
        ? "区间峰值"
        : Scope.IsToday ? "今日峰值" : $"{Scope.Label}峰值";

    public ActiveSession? LastSession => IsAppFiltered
        ? AppContext.Profile?.Sessions.LastOrDefault()
        : TodayRecord?.Sessions.LastOrDefault();

    public (long Keys, long Clicks, long Wheel, double Meters, double ActiveSeconds, int Sessions) ScopeValues()
    {
        if (IsAppFiltered)
        {
            return (ProfileTotals.Keys, ProfileTotals.Clicks, ProfileTotals.Wheel,
                ProfileTotals.MoveMeters, ProfileTotals.ActiveSeconds, ProfileTotals.Sessions.Count);
        }

        // 真实运行时全局范围 End 就是今天，可用“历史基线 + 今日实时记录”避免每秒重聚合；
        // 若是传入的历史范围（测试/预览），直接使用已经聚合好的区间总量。
        if (Scope.End != DateTime.Today)
        {
            return (RangeTotals.Keys, RangeTotals.Clicks, RangeTotals.Wheel,
                RangeTotals.MoveMeters, RangeTotals.ActiveSeconds, RangeTotals.Sessions.Count);
        }

        long keys = Scope.IsToday ? 0 : BaseTotals.Keys;
        long clicks = Scope.IsToday ? 0 : BaseTotals.Clicks;
        long wheel = Scope.IsToday ? 0 : BaseTotals.Wheel;
        double meters = Scope.IsToday ? 0 : BaseTotals.MoveMeters;
        double active = Scope.IsToday ? 0 : BaseTotals.ActiveSeconds;
        int sessions = Scope.IsToday ? 0 : BaseTotals.Sessions.Count;
        if (TodayRecord is not null)
        {
            keys += TodayRecord.Keys;
            clicks += TodayRecord.Clicks;
            wheel += TodayRecord.Wheel;
            meters += TodayRecord.MoveMeters;
            active += TodayRecord.ActiveSeconds;
            sessions += TodayRecord.Sessions.Count;
        }

        return (keys, clicks, wheel, meters, active, sessions);
    }

    private long PerMinute(long count) =>
        ProfileTotals.ActiveSeconds > 0
            ? (long)Math.Round(count * 60.0 / ProfileTotals.ActiveSeconds)
            : 0;

    public (string[] Names, long[] Values) DistributionValues(bool keyboard)
    {
        if (keyboard)
        {
            _keyCounts.Clear();
            foreach ((int vk, long count) in RangeTotals.KeyCounts)
            {
                if (count > 0)
                {
                    _keyCounts[vk] = _keyCounts.GetValueOrDefault(vk) + count;
                }
            }

            List<KeyValuePair<int, long>> rank = _keyCounts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key)
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
                values[^1] = rank.Skip(top).Sum(static pair => pair.Value);
            }

            return (names, values);
        }

        return (["左键", "右键", "中键", "侧键"],
            [RangeTotals.Left, RangeTotals.Right, RangeTotals.Middle, RangeTotals.XButtons]);
    }

    public string BuildFooterText(long keysPerMinute, long clicksPerMinute)
    {
        ActiveSession? session = LastSession;
        return $"本段 {FormatDuration(session?.Seconds ?? 0)}  ·  {PeakLabel} {RangeTotals.PeakApm} APM  ·  " +
               $"实时键 {keysPerMinute:N0} / 点 {clicksPerMinute:N0}";
    }

    public string KeysBaselineText => ComputeBaseline(keys: true);

    public string ActiveBaselineText => ComputeBaseline(keys: false);

    private string BuildInsightText()
    {
        long totalKeys = Rank.Sum(static pair => pair.Value);
        string top = Rank.Count == 0
            ? "--"
            : $"{PeripheralWear.KeyName(Rank[0].Key)}({(totalKeys > 0 ? Rank[0].Value * 100.0 / totalKeys : 0):0.0}%)";
        int peak = 0;
        for (int hour = 1; hour < 24; hour++)
        {
            if (RangeTotals.HourKeys[hour] > RangeTotals.HourKeys[peak])
            {
                peak = hour;
            }
        }

        long daily = Range.ActiveDays <= 0 ? 0 : RangeTotals.Keys / Range.ActiveDays;
        return $"洞察   {Scope.Label}最常用按键 {top}    ·    高峰时段 {peak:00}:00-{peak + 1:00}:00    ·    " +
               $"活跃日均击键 {FormatCount(daily)}";
    }

    private string ComputeBaseline(bool keys)
    {
        DateTime end = Scope.Start;
        List<double> values = [];
        foreach (DayRecord day in Data.History.Values)
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
            ? $"P50 {FormatCount((long)p50)}  ·  P90 {FormatCount((long)p90)}"
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

    private static DayRecord? ResolveToday(StatsData data)
    {
        string key = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return data.History.TryGetValue(key, out DayRecord? today) ? today : null;
    }

    internal static string FormatCount(long value)
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

    internal static string FormatMeters(double meters)
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

    internal static string FormatDuration(double seconds)
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
}

/// <summary>总览模块工厂。所有 UI 均为代码构建，不依赖 XAML/反射。</summary>
internal static class OverviewWidgets
{
    private static OverviewWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("overview", () =>
            new OverviewWidgetModel(
                context.Data,
                context.Scope,
                context.Service,
                context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpi(WidgetContext context, KpiKind kind)
    {
        OverviewWidgetModel model = Resolve(context);
        if (string.Equals(context.VariantId, "minimal", StringComparison.Ordinal))
        {
            return new WidgetView
            {
                Content = new MinimalKpiView(model, kind, context.Live),
                ShowHeader = false,
                ContentPadding = new Thickness(12),
                Elevated = false
            };
        }

        KpiView view = new(model, kind, context.Live, context.Span.Columns);
        return new WidgetView
        {
            Content = view,
            ShowHeader = false,
            ContentPadding = new Thickness(18, 8),
            Elevated = false
        };
    }

    public static WidgetView CreateDistribution(WidgetContext context)
    {
        OverviewWidgetModel model = Resolve(context);
        DistributionView view = new(model, context.VariantId);
        return new WidgetView
        {
            Content = view,
            HeaderHint = context.Scope.Caption,
            HeaderTrailing = view.HeaderTrailing,
            ContentPadding = new Thickness(18, 12),
            Elevated = false
        };
    }

    public static WidgetView CreateTopKeys(WidgetContext context)
    {
        OverviewWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new TopKeysView(model, context.VariantId),
            HeaderHint = $"{context.Scope.Label} · Top 10",
            ContentPadding = new Thickness(18, 12),
            Elevated = false
        };
    }

    public static WidgetView CreateInsight(WidgetContext context)
    {
        OverviewWidgetModel model = Resolve(context);
        TextBlock text = PageKit.Label(
            model.InsightText,
            10.5,
            PageKit.T.Muted,
            null,
            null,
            TextTrimming.CharacterEllipsis);
        text.VerticalAlignment = VerticalAlignment.Center;

        Grid content = new()
        {
            ColumnDefinitions = new ColumnDefinitions("3,*"),
            ColumnSpacing = 10
        };
        content.Children.Add(new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.55)),
            Margin = new Thickness(0, 2)
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        return new WidgetView
        {
            Content = content,
            ShowHeader = false,
            BodyVerticalAlignment = VerticalAlignment.Center,
            ContentPadding = new Thickness(14, 6),
            Elevated = false,
            FillOpacity = 0.72,
            BorderOpacity = 0.7
        };
    }

    private static Color ColorFor(KpiKind kind) => kind switch
    {
        KpiKind.Keys => PageKit.T.Accent,
        KpiKind.Clicks => PageKit.T.Green,
        KpiKind.Wheel => PageKit.T.Orange,
        KpiKind.Distance => PageKit.T.Purple,
        KpiKind.Active => PageKit.T.Green,
        KpiKind.Sessions => PageKit.T.Accent,
        KpiKind.KeyRate => PageKit.T.Cyan,
        KpiKind.Apm => PageKit.T.Red,
        _ => PageKit.T.Accent
    };

    private static string KpiLabel(OverviewWidgetModel model, KpiKind kind) => kind switch
    {
        KpiKind.Keys => $"{model.Scope.Label}击键",
        KpiKind.Clicks => $"{model.Scope.Label}点击",
        KpiKind.Wheel => $"{model.Scope.Label}滚轮",
        KpiKind.Distance => "鼠标路程(估算)",
        KpiKind.Active => "有效使用时长",
        KpiKind.Sessions => "连续使用段",
        KpiKind.KeyRate => model.IsAppFiltered ? "区间平均击键 / 分" : "击键速率 / 分",
        KpiKind.Apm => model.IsAppFiltered ? "区间平均 APM" : "操作速率 APM",
        _ => ""
    };

    private static string KpiValue(
        KpiKind kind,
        long keys,
        long clicks,
        long wheel,
        double meters,
        double active,
        int sessions,
        OverviewWidgetModel model) => kind switch
    {
        KpiKind.Keys => OverviewWidgetModel.FormatCount(keys),
        KpiKind.Clicks => OverviewWidgetModel.FormatCount(clicks),
        KpiKind.Wheel => OverviewWidgetModel.FormatCount(wheel),
        KpiKind.Distance => OverviewWidgetModel.FormatMeters(meters),
        KpiKind.Active => OverviewWidgetModel.FormatDuration(active),
        KpiKind.Sessions => $"{sessions} 段",
        KpiKind.KeyRate => model.KeysPerMinute.ToString("N0", CultureInfo.InvariantCulture),
        KpiKind.Apm => model.Apm.ToString("N0", CultureInfo.InvariantCulture),
        _ => ""
    };

    private sealed class KpiView : UserControl, ILiveWidget
    {
        private readonly OverviewWidgetModel _model;
        private readonly KpiKind _kind;
        private readonly TextBlock _label;
        private readonly TextBlock _value;
        private readonly TextBlock? _extra;
        private readonly KpiIcon _icon;
        private readonly LiveRateSpark? _spark;

        public KpiView(
            OverviewWidgetModel model,
            KpiKind kind,
            WidgetLiveBus? live,
            int columns)
        {
            _model = model;
            _kind = kind;
            (long keys, long clicks, long wheel, double meters, double active, int sessions) = model.ScopeValues();
            Color color = ColorFor(kind);

            _label = PageKit.Label(KpiLabel(model, kind), 12, PageKit.T.Muted);
            _value = PageKit.Label(KpiValue(kind, keys, clicks, wheel, meters, active, sessions, model), 27,
                PageKit.T.Text, FontWeight.SemiBold);
            _value.VerticalAlignment = VerticalAlignment.Center;

            Grid valueRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            valueRow.Children.Add(_value);

            if (kind == KpiKind.Apm)
            {
                double sparkWidth = columns >= 4
                    ? Math.Clamp(columns * 26, 120, 160)
                    : 90;
                _spark = new LiveRateSpark
                {
                    Height = 30,
                    Width = sparkWidth,
                    Margin = new Thickness(10, 10, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(_spark, 1);
                valueRow.Children.Add(_spark);
            }

            StackPanel text = new()
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Children.Add(_label);
            text.Children.Add(valueRow);

            string? extra = KpiExtra(model, kind);
            if (!string.IsNullOrEmpty(extra))
            {
                _extra = PageKit.Label(extra, 9.5, PageKit.Alpha(PageKit.T.Muted, 0.92),
                    null, 210, TextTrimming.CharacterEllipsis);
                text.Children.Add(_extra);
            }

            Grid content = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            content.Children.Add(text);
            _icon = new KpiIcon(color) { Width = 32, Height = 32, Margin = new Thickness(8, 2, 0, 0) };
            Grid.SetColumn(_icon, 1);
            content.Children.Add(_icon);
            Content = content;

            SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
            live?.Register(this);
        }

        public void UpdateLive(in WidgetLiveSnapshot snapshot)
        {
            _value.Text = KpiValue(_kind, snapshot.Keys, snapshot.Clicks, snapshot.Wheel,
                snapshot.Meters, snapshot.ActiveSeconds, snapshot.Sessions, _model);
            _spark?.Push(snapshot.Apm);
        }

        private void ApplyDensity(double height)
        {
            bool compact = height > 0 && height < 62;
            bool tall = height >= 88;
            _value.FontSize = compact ? 22 : tall ? 31 : 27;
            _label.FontSize = compact ? 10.5 : tall ? 13 : 12;
            if (_extra is not null)
            {
                _extra.FontSize = compact ? 9 : 9.5;
                _extra.IsVisible = !compact;
            }

            _icon.Width = compact ? 26 : tall ? 36 : 32;
            _icon.Height = _icon.Width;
            if (_spark is not null)
            {
                _spark.Height = compact ? 24 : tall ? 34 : 30;
            }
        }

        private static string? KpiExtra(OverviewWidgetModel model, KpiKind kind) => kind switch
        {
            KpiKind.Keys => model.IsAppFiltered ? "所选应用区间总量" : model.KeysBaselineText,
            KpiKind.Active => model.IsAppFiltered ? "所选应用有效时长" : model.ActiveBaselineText,
            KpiKind.Apm => model.IsAppFiltered ? "区间平均 · 单应用" : "最近 5 分钟 · 60秒滚动值",
            _ => null
        };
    }

    private sealed class MinimalKpiView : UserControl, ILiveWidget
    {
        private readonly OverviewWidgetModel _model;
        private readonly KpiKind _kind;
        private readonly TextBlock _label;
        private readonly TextBlock _value;
        private readonly Border _bar;
        private readonly StackPanel _body;

        public MinimalKpiView(OverviewWidgetModel model, KpiKind kind, WidgetLiveBus? live)
        {
            _model = model;
            _kind = kind;
            Color color = ColorFor(kind);
            (long keys, long clicks, long wheel, double meters, double active, int sessions) = model.ScopeValues();
            _label = PageKit.Label(KpiLabel(model, kind), 10.5, PageKit.T.Muted);
            _value = PageKit.Label(KpiValue(kind, keys, clicks, wheel, meters, active, sessions, model),
                38, PageKit.T.Text, FontWeight.Bold);
            _value.HorizontalAlignment = HorizontalAlignment.Center;
            _value.TextAlignment = TextAlignment.Center;
            _bar = new Border
            {
                Width = 34,
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = PageKit.Brush(PageKit.Alpha(color, 0.55)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _body = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _label,
                    _value,
                    _bar
                }
            };
            Content = _body;
            ApplyDensity();
            SizeChanged += (_, _) => ApplyDensity();
            live?.Register(this);
        }

        public void UpdateLive(in WidgetLiveSnapshot snapshot)
        {
            _value.Text = KpiValue(_kind, snapshot.Keys, snapshot.Clicks, snapshot.Wheel,
                snapshot.Meters, snapshot.ActiveSeconds, snapshot.Sessions, _model);
        }

        private void ApplyDensity()
        {
            double height = Bounds.Height;
            bool tiny = height > 0 && height < 50;
            bool compact = height >= 50 && height < 72;
            _value.FontSize = tiny ? 18 : compact ? 26 : 38;
            _label.FontSize = tiny ? 8.5 : compact ? 9.5 : 10.5;
            _body.Spacing = tiny ? 0 : 2;
            _bar.IsVisible = height <= 0 || height >= 64;
        }
    }

    private sealed class DistributionView : UserControl
    {
        private readonly OverviewWidgetModel _model;
        private readonly string _variant;
        private readonly bool _bars;
        private readonly bool _treemap;
        private readonly Grid _content = new();
        private readonly Button _mouseButton;
        private readonly Button _keyboardButton;
        private bool _keyboardMode;
        private double _ringSize;

        public DistributionView(OverviewWidgetModel model, string variant)
        {
            _model = model;
            _variant = variant;
            _bars = string.Equals(variant, "bars", StringComparison.Ordinal);
            _treemap = string.Equals(variant, "treemap", StringComparison.Ordinal);
            Content = _content;

            _mouseButton = ModeButton("鼠标", !_keyboardMode, () => SetMode(false));
            _keyboardButton = ModeButton("键盘", _keyboardMode, () => SetMode(true));
            HeaderTrailing = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    _mouseButton,
                    _keyboardButton
                }
            };

            BuildContent();
            SizeChanged += (_, _) =>
            {
                if (!_bars && !_treemap)
                {
                    double next = RingSize();
                    if (Math.Abs(next - _ringSize) > 4)
                    {
                        BuildContent();
                    }
                }
            };
        }

        public Control HeaderTrailing { get; }

        private void SetMode(bool keyboard)
        {
            if (_keyboardMode == keyboard)
            {
                return;
            }

            _keyboardMode = keyboard;
            UpdateModeButtons();
            BuildContent();
        }

        private void UpdateModeButtons()
        {
            ApplyModeButton(_mouseButton, !_keyboardMode);
            ApplyModeButton(_keyboardButton, _keyboardMode);
        }

        private void BuildContent()
        {
            _content.Children.Clear();
            _content.ColumnDefinitions.Clear();
            _content.RowDefinitions.Clear();
            (string[] names, long[] values) = _model.DistributionValues(_keyboardMode);
            long total = values.Sum();

            if (_bars)
            {
                BuildBars(names, values, total);
                return;
            }

            if (_treemap)
            {
                List<TreemapItem> items = [];
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] > 0)
                    {
                        items.Add(new TreemapItem(names[i], values[i], SliceColor(i)));
                    }
                }

                Treemap treemap = new();
                treemap.SetData(items);
                _content.Children.Add(treemap);
                return;
            }

            _ringSize = RingSize();
            _content.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
            _content.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

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
                total > 0 ? OverviewWidgetModel.FormatCount(total) : "暂无数据",
                _keyboardMode ? "次已记录击键" : "次点击")
            {
                Width = _ringSize,
                Height = _ringSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _content.Children.Add(ring);

            StackPanel legend = new()
            {
                Spacing = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            foreach (Control row in BuildLegend(names, values, total))
            {
                legend.Children.Add(row);
            }

            Grid.SetColumn(legend, 1);
            _content.Children.Add(legend);
        }

        private void BuildBars(string[] names, long[] values, long total)
        {
            int count = Math.Max(1, names.Length);
            for (int i = 0; i < count; i++)
            {
                _content.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            }

            for (int i = 0; i < names.Length; i++)
            {
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("58,*,76") };
                row.Children.Add(PageKit.Label(names[i], 11, PageKit.T.Text, FontWeight.Medium, 58,
                    TextTrimming.CharacterEllipsis));
                PageKit.Proportion bar = new(total > 0 ? values[i] / (double)total : 0, SliceColor(i))
                {
                    Height = 9,
                    Margin = new Thickness(6, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);
                StackPanel value = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 0
                };
                value.Children.Add(PageKit.Label(OverviewWidgetModel.FormatCount(values[i]), 10,
                    PageKit.T.Text, FontWeight.SemiBold));
                value.Children.Add(PageKit.Label(total > 0 ? $"{values[i] * 100.0 / total:0.#}%" : "--",
                    8.5, PageKit.T.Muted));
                Grid.SetColumn(value, 2);
                row.Children.Add(value);
                Grid.SetRow(row, i);
                _content.Children.Add(row);
            }
        }

        private double RingSize()
        {
            double width = Bounds.Width > 0 ? Bounds.Width * 0.46 : 160;
            double height = Bounds.Height > 0 ? Bounds.Height - 6 : 160;
            return Math.Clamp(Math.Min(width, height), 84, 340);
        }

        private IEnumerable<Control> BuildLegend(string[] names, long[] values, long total)
        {
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
                text.Children.Add(PageKit.Label(names[i], 11, PageKit.T.Text, null, null,
                    TextTrimming.CharacterEllipsis));
                text.Children.Add(PageKit.Label(
                    $"{OverviewWidgetModel.FormatCount(values[i])} · {pct}",
                    9.5,
                    PageKit.T.Muted,
                    null,
                    null,
                    TextTrimming.CharacterEllipsis));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                yield return row;
            }
        }

        private static Button ModeButton(string text, bool active, Action onClick)
        {
            Button button = new()
            {
                Content = text,
                FontSize = 10.5,
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1)
            };
            ApplyModeButton(button, active);
            button.Click += (_, _) => onClick();
            return button;
        }

        private static void ApplyModeButton(Button button, bool active)
        {
            button.Background = active ? PageKit.Brush(PageKit.T.Accent) : PageKit.Brush(PageKit.T.Raised);
            button.Foreground = active ? PageKit.Brush(PageKit.T.OnAccent) : PageKit.Brush(PageKit.T.Muted);
            button.BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7));
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
    }

    private sealed class TopKeysView : UserControl
    {
        public TopKeysView(OverviewWidgetModel model, string variant)
        {
            List<KeyValuePair<int, long>> rank = model.Rank.Take(10).ToList();
            if (string.Equals(variant, "keycaps", StringComparison.Ordinal))
            {
                long total = Math.Max(1, model.RangeTotals.Keys);
                KeycapMatrix matrix = new();
                matrix.SetData(rank.Select((pair, index) => new KeycapItem(
                    PeripheralWear.KeyName(pair.Key),
                    pair.Value,
                    pair.Value / (double)total,
                    RankColor(index))).ToArray());
                Content = matrix;
                return;
            }

            bool list = string.Equals(variant, "list", StringComparison.Ordinal);
            Grid body = new() { RowSpacing = 2 };
            if (rank.Count == 0)
            {
                Control empty = PageKit.Empty("还没有键盘记录");
                empty.VerticalAlignment = VerticalAlignment.Center;
                body.Children.Add(empty);
                Content = body;
                return;
            }

            long max = Math.Max(1, rank[0].Value);
            for (int i = 0; i < rank.Count; i++)
            {
                body.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            }

            for (int i = 0; i < rank.Count; i++)
            {
                Color color = RankColor(i);
                Grid row = new()
                {
                    ColumnDefinitions = list
                        ? new ColumnDefinitions("22,*,68")
                        : new ColumnDefinitions("22,64,*,68")
                };
                Control rankLabel = PageKit.Label((i + 1).ToString(CultureInfo.InvariantCulture), 9.5,
                    PageKit.T.Muted);
                rankLabel.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(rankLabel);
                TextBlock name = PageKit.Label(PeripheralWear.KeyName(rank[i].Key), 11.5,
                    PageKit.T.Text, null, 60, TextTrimming.CharacterEllipsis);
                name.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(name, 1);
                row.Children.Add(name);

                if (!list)
                {
                    PageKit.Proportion bar = new(rank[i].Value / (double)max, color)
                    {
                        Height = 9,
                        Margin = new Thickness(8, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(bar, 2);
                    row.Children.Add(bar);
                }

                Control value = PageKit.Label(OverviewWidgetModel.FormatCount(rank[i].Value), 10.5,
                    PageKit.T.Text, FontWeight.SemiBold);
                value.HorizontalAlignment = HorizontalAlignment.Right;
                value.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(value, list ? 2 : 3);
                row.Children.Add(value);
                Grid.SetRow(row, i);
                body.Children.Add(row);
            }

            Content = body;
        }

        private static Color RankColor(int index) => index switch
        {
            0 => PageKit.T.Purple,
            1 => PageKit.T.Cyan,
            2 => PageKit.T.Orange,
            3 => PageKit.T.Green,
            _ => PageKit.T.Accent
        };
    }

    private sealed class KpiIcon : Control
    {
        private readonly Color _color;

        public KpiIcon(Color color) => _color = color;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            context.FillRectangle(PageKit.Brush(PageKit.Alpha(_color, 0.18)), bounds, 8);
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
