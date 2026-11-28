using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.App.Platform;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Render.Primitives;

namespace Kms.App.Widgets;

internal sealed class AppsWidgetModel : WidgetPageModel
{
    public AppsWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        Snapshot = AppSnapshot.Build(
            Data,
            scope.Start,
            scope.End,
            byWindow: !AppContext.IsAll,
            AppContext.CategoryService);
        Categories = CategorySnapshot.Build(
            Data,
            scope.Start,
            scope.End,
            AppContext.CategoryService);
    }

    public AppPageContext AppContext { get; }

    public event Action? CategoryChanged;

    public AppCategoryService? CategoryService => AppContext.CategoryService;

    public CategorySnapshot Categories { get; }

    public async Task AssignCategoryAsync(AppRow app, string categoryKey)
    {
        if (CategoryService is null)
        {
            return;
        }

        await CategoryService.SetAppCategoryAsync(app.Path, categoryKey).ConfigureAwait(true);
        CategoryChanged?.Invoke();
    }

    public async Task AddTitleRuleAsync(AppRow app, string keyword, string categoryKey)
    {
        if (CategoryService is null)
        {
            return;
        }

        await CategoryService.SetTitleRuleAsync(app.Path, keyword, categoryKey).ConfigureAwait(true);
        CategoryChanged?.Invoke();
    }

    public bool IsSingleApp => !AppContext.IsAll;

    public long Switches => AppContext.IsAll
        ? Snapshot.Switches
        : AppContext.Profile?.Totals.Switches ?? 0;

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public AppSnapshot Snapshot { get; }

    public int Sort { get; set; }
}

internal sealed class AppRow
{
    public string Name { get; init; } = "";

    public string Detail { get; init; } = "";

    public string Path { get; init; } = "";

    public double Seconds { get; set; }

    public long Keys { get; set; }

    public long Clicks { get; set; }

    public long Wheel { get; set; }

    public long[] Groups { get; } = new long[6];

    public string CategoryKey { get; set; } = AppCategoryBuiltins.Uncategorized;

    public string CategoryName { get; set; } = "未分类";

    public string CategoryColor { get; set; } = "#94A3B8";
}

internal sealed class CategoryRow
{
    public string Key { get; init; } = AppCategoryBuiltins.Uncategorized;
    public string Name { get; init; } = "未分类";
    public string ColorHex { get; init; } = "#94A3B8";
    public double Seconds { get; set; }
    public long Keys { get; set; }
    public long Clicks { get; set; }
    public long Wheel { get; set; }
}

internal sealed class CategorySnapshot
{
    public List<CategoryRow> Categories { get; } = [];

    public double AttributedSeconds { get; private set; }

    public static CategorySnapshot Build(
        StatsData data,
        DateTime start,
        DateTime end,
        AppCategoryService? categoryService)
    {
        CategorySnapshot result = new();
        Dictionary<string, CategoryRow> rows = new(StringComparer.Ordinal);
        for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!data.History.TryGetValue(key, out DayRecord? day))
            {
                continue;
            }

            foreach (AppUsage app in day.Apps.Values)
            {
                AppCategoryDefinition category = categoryService?.Resolve(app.ProcessPath, app.Title).Category
                    ?? AppCategoryBuiltins.UncategorizedCategory;
                if (!rows.TryGetValue(category.CategoryKey, out CategoryRow? row))
                {
                    row = new CategoryRow
                    {
                        Key = category.CategoryKey,
                        Name = category.Name,
                        ColorHex = category.ColorHex
                    };
                    rows[category.CategoryKey] = row;
                }

                row.Seconds += app.ActiveSeconds;
                row.Keys += app.Keys;
                row.Clicks += app.Clicks;
                row.Wheel += app.Wheel;
            }
        }

        result.Categories.AddRange(rows.Values.OrderByDescending(static row => row.Seconds));
        result.AttributedSeconds = result.Categories.Sum(static row => row.Seconds);
        return result;
    }
}

internal sealed class AppSnapshot
{
    public List<AppRow> Apps { get; } = [];

    public double ObservedSeconds { get; private set; }

    public double AttributedSeconds { get; private set; }

    public long Switches { get; private set; }

    public int Days { get; private set; }

    public static AppSnapshot Build(
        StatsData data,
        DateTime start,
        DateTime end,
        bool byWindow = false,
        AppCategoryService? categoryService = null)
    {
        AppSnapshot result = new();
        Dictionary<string, AppRow> apps = new(StringComparer.OrdinalIgnoreCase);
        for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!data.History.TryGetValue(key, out DayRecord? day))
            {
                continue;
            }

            result.Days++;
            result.ObservedSeconds += day.AppObservedSeconds;
            result.Switches += day.AppSwitches;
            foreach (AppUsage app in day.Apps.Values)
            {
                string id = byWindow
                    ? app.Id
                    : app.ProcessPath.Length > 0 ? app.ProcessPath : app.AppName;
                if (!apps.TryGetValue(id, out AppRow? row))
                {
                    AppCategoryDefinition category = categoryService?.Resolve(app.ProcessPath, app.Title).Category
                        ?? AppCategoryBuiltins.UncategorizedCategory;
                    row = new AppRow
                    {
                        Name = byWindow && app.Title.Length > 0 ? app.Title : app.AppName,
                        Detail = byWindow
                            ? app.ProcessPath.Length > 0 ? app.ProcessPath : app.AppName
                            : app.ProcessPath.Length > 0 ? app.ProcessPath : app.Title,
                        Path = app.ProcessPath.Length > 0 ? app.ProcessPath : app.AppName,
                        CategoryKey = category.CategoryKey,
                        CategoryName = category.Name,
                        CategoryColor = category.ColorHex
                    };
                    apps[id] = row;
                }

                row.Seconds += app.ActiveSeconds;
                row.Keys += app.Keys;
                row.Clicks += app.Clicks;
                row.Wheel += app.Wheel;
                for (int group = 0; group < 6 && group < app.KeyGroups.Length; group++)
                {
                    row.Groups[group] += app.KeyGroups[group];
                }
            }
        }

        result.Apps.AddRange(apps.Values.OrderByDescending(static app => app.Seconds));
        result.AttributedSeconds = result.Apps.Sum(static app => app.Seconds);
        return result;
    }
}

/// <summary>应用统计模块工厂。</summary>
internal static class AppsWidgets
{
    private static AppsWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("apps", () => new AppsWidgetModel(
            context.Data,
            context.Scope,
            context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpiCount(WidgetContext context)
    {
        AppsWidgetModel model = Resolve(context);
        AppSnapshot snapshot = model.Snapshot;
        return MicroKpi.Card(
            model.IsSingleApp ? "窗口 / 文档数" : "活跃应用数",
            snapshot.Apps.Count.ToString("N0", CultureInfo.InvariantCulture),
            $"{snapshot.Days} 天有记录",
            PageKit.T.Purple);
    }

    public static WidgetView CreateKpiTopApp(WidgetContext context)
    {
        AppsWidgetModel model = Resolve(context);
        AppSnapshot snapshot = model.Snapshot;
        AppRow? top = snapshot.Apps.FirstOrDefault();
        return top is null
            ? MicroKpi.Card(model.IsSingleApp ? "主力窗口" : "主力专注应用", "--", "暂无应用归属", PageKit.T.Green)
            : MicroKpi.Card(
                model.IsSingleApp ? "主力窗口" : "主力专注应用",
                ShortName(top.Name),
                PageKit.FormatDuration(top.Seconds),
                PageKit.T.Green);
    }

    public static WidgetView CreateKpiFocusShare(WidgetContext context)
    {
        AppSnapshot snapshot = Resolve(context).Snapshot;
        AppRow? top = snapshot.Apps.FirstOrDefault();
        double share = top is null || snapshot.AttributedSeconds <= 0
            ? 0
            : top.Seconds / snapshot.AttributedSeconds;
        return MicroKpi.Card(
            "主力占比",
            top is null ? "--" : MicroKpi.Percent(share),
            top is null ? "暂无归因" : $"{ShortName(top.Name)}",
            PageKit.T.Cyan);
    }

    public static WidgetView CreateKpiSwitches(WidgetContext context)
    {
        AppsWidgetModel model = Resolve(context);
        AppSnapshot snapshot = model.Snapshot;
        double perDay = snapshot.Days > 0 ? model.Switches / (double)snapshot.Days : 0;
        return MicroKpi.Card(
            "应用切换",
            PageKit.FormatCount(model.Switches),
            $"{perDay:0.#} 次/天",
            PageKit.T.Orange);
    }

    public static WidgetView CreateSummary(WidgetContext context)
    {
        AppsWidgetModel model = Resolve(context);
        AppSnapshot snapshot = model.Snapshot;
        double observed = snapshot.ObservedSeconds;
        double attributed = snapshot.AttributedSeconds;
        double unattributed = Math.Max(0, observed - attributed);
        Control body = PageKit.StatStripBody(
            ("前台观测", PageKit.FormatDuration(observed), PageKit.T.Cyan),
            ("已归因", PageKit.FormatDuration(attributed), PageKit.T.Green),
            ("覆盖率", $"{(observed <= 0 ? 0 : attributed / observed * 100):0.#}%", PageKit.T.Accent),
            ("未归因", PageKit.FormatDuration(unattributed), PageKit.T.Orange),
            (model.IsSingleApp ? "窗口/文档" : "应用数",
                snapshot.Apps.Count.ToString("N0", CultureInfo.InvariantCulture),
                PageKit.T.Purple),
            (model.IsSingleApp ? "主力窗口" : "Top 1", TopName(snapshot), PageKit.T.Text));
        return new WidgetView
        {
            Content = body,
            ShowHeader = false,
            ContentPadding = new Thickness(12),
            Elevated = true
        };
    }

    public static WidgetView CreateRanking(WidgetContext context)
    {
        AppsWidgetModel model = Resolve(context);
        AppsRankingView view = new(model, context.Span);
        return new WidgetView
        {
            Content = view,
            HeaderHint = "时长、击键、点击与 APM · 局部翻页",
            HeaderTrailing = PageKit.Badge("RANK", PageKit.T.Accent),
            ContentPadding = new Thickness(16),
            Elevated = true
        };
    }

    public static WidgetView CreateCategory(WidgetContext context)
    {
        AppsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new AppsCategoryView(model, context.VariantId, context.Span),
            HeaderHint = context.VariantId switch
            {
                "bars" => "工作性质横向占比 · 含百分比与色标",
                "treemap" => "工作性质矩形树状面积图",
                _ => "按用户自定义工作性质归因时长"
            },
            HeaderTrailing = PageKit.Badge("CATEGORY", PageKit.T.Cyan),
            ContentPadding = new Thickness(16),
            Elevated = true
        };
    }

    public static WidgetView CreateComposition(WidgetContext context)
    {
        AppsWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new AppsKeyCompositionView(model),
            HeaderHint = "应用六类键位分布 · 堆叠条",
            HeaderTrailing = PageKit.Badge("GROUPS", PageKit.T.Purple),
            ContentPadding = new Thickness(16),
            Elevated = true
        };
    }

    internal static string TopName(AppSnapshot snapshot) =>
        snapshot.Apps.Count == 0 ? "--" : snapshot.Apps[0].Name;

    internal static Color Palette(int index)
    {
        Color[] colors =
        [
            PageKit.T.Accent,
            PageKit.T.Cyan,
            PageKit.T.Green,
            PageKit.T.Purple,
            PageKit.T.Orange,
            PageKit.T.Red
        ];
        return colors[Math.Abs(index) % colors.Length];
    }

    internal static Color CategoryColor(string? colorHex)
    {
        try
        {
            return Color.Parse(string.IsNullOrWhiteSpace(colorHex) ? "#94A3B8" : colorHex);
        }
        catch
        {
            return PageKit.T.Muted;
        }
    }

    internal static Color ColorFor(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name)
        {
            hash = (hash ^ c) * 16777619;
        }

        Color[] colors =
        [
            PageKit.T.Accent,
            PageKit.T.Cyan,
            PageKit.T.Green,
            PageKit.T.Purple,
            PageKit.T.Orange,
            PageKit.T.Red
        ];
        return colors[hash % colors.Length];
    }

    internal static string ShortName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.Length > 10 ? trimmed[..10] : trimmed;
    }

    internal static string Initial(string name) =>
        string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();
}

internal sealed class AppsRankingView : UserControl
{
    private const string NarrowColumns = "24,*,88,56,56,78";
    private const string WideColumns = "24,2.1*,1.1*,0.8*,0.8*,1.05*";

    private readonly AppsWidgetModel _model;
    private readonly PageKit.PagedList _table = new(7, 4);
    private readonly StackPanel _tableBody = new() { Spacing = 4 };
    private string _tableColumns = NarrowColumns;
    private int _sort;
    private bool _compact;
    private bool _narrow;

    public AppsRankingView(AppsWidgetModel model, WidgetSpan span)
    {
        _model = model;
        _sort = model.Sort;
        _compact = span.Rows <= 2;
        _narrow = span.Columns <= 3;
        Content = _tableBody;
        RebuildTableStructure();
        BuildTable();
        SizeChanged += (_, _) => ApplyMetrics();
    }

    public void SetSort(int sort)
    {
        if (_sort == sort)
        {
            return;
        }

        _sort = sort;
        _model.Sort = sort;
        BuildTable();
    }

    private void ApplyMetrics()
    {
        string columns = _narrow
            ? "20,*,72"
            : Bounds.Width >= 760 ? WideColumns : NarrowColumns;
        bool rebuild = !string.Equals(columns, _tableColumns, StringComparison.Ordinal);
        _tableColumns = columns;

        int rows = _compact
            ? 3
            : Bounds.Height > 0
                ? Math.Clamp((int)((Bounds.Height - 52) / 46), 4, 18)
                : 7;
        _table.SetPageSize(rows);
        if (rebuild)
        {
            RebuildTableStructure();
            BuildTable();
        }
    }

    private void RebuildTableStructure()
    {
        _tableBody.Children.Clear();
        if (!_compact)
        {
            _tableBody.Children.Add(TableHeader());
            _tableBody.Children.Add(PageKit.Divider());
        }

        _tableBody.Children.Add(_table);
    }

    private void BuildTable()
    {
        AppSnapshot snapshot = _model.Snapshot;
        List<Control> rows = [];
        if (snapshot.Apps.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有应用归属；开始使用后会自动出现"));
            _table.SetItems(rows);
            return;
        }

        List<AppRow> ordered = _sort switch
        {
            1 => snapshot.Apps.OrderByDescending(static app => app.Keys).ToList(),
            2 => snapshot.Apps.OrderByDescending(static app => app.Clicks).ToList(),
            _ => snapshot.Apps.OrderByDescending(static app => app.Seconds).ToList()
        };

        double max = _sort switch
        {
            1 => Math.Max(1, ordered.Max(static app => app.Keys)),
            2 => Math.Max(1, ordered.Max(static app => app.Clicks)),
            _ => Math.Max(1, ordered.Max(static app => app.Seconds))
        };

        int rank = 1;
        foreach (AppRow app in ordered.Take(_compact ? 3 : 30))
        {
            double fraction = _sort switch
            {
                1 => app.Keys / max,
                2 => app.Clicks / max,
                _ => app.Seconds / max
            };
            rows.Add(Row(rank++, app, fraction));
        }

        _table.SetItems(rows);
    }

    private Control TableHeader()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions(_tableColumns),
            ColumnSpacing = 6
        };
        grid.Children.Add(PageKit.Muted("#", 8));
        AddHeader(grid, 1, "应用");
        if (_narrow)
        {
            AddHeader(grid, 2, "数值", right: true);
            return grid;
        }

        AddHeader(grid, 2, "时长");
        AddHeader(grid, 3, "击键", right: true);
        AddHeader(grid, 4, "点击", right: true);
        AddHeader(grid, 5, "APM", right: true);
        return grid;
    }

    private static void AddHeader(Grid grid, int column, string text, bool right = false)
    {
        Control label = PageKit.Muted(text, 8);
        if (right)
        {
            label.HorizontalAlignment = HorizontalAlignment.Right;
        }

        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private Control Row(int rank, AppRow app, double fraction)
    {
        Color tone = AppsWidgets.ColorFor(app.Name);
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions(_tableColumns),
            ColumnSpacing = 6,
            MinHeight = _compact ? 30 : 42
        };

        Control rankLabel = PageKit.Label(rank.ToString("00", CultureInfo.InvariantCulture), 9,
            rank <= 3 ? tone : PageKit.T.Muted, FontWeight.SemiBold);
        row.Children.Add(rankLabel);

        Grid appCell = new()
        {
            ColumnDefinitions = new ColumnDefinitions(_narrow ? "*" : "30,*"),
            ColumnSpacing = _narrow ? 0 : 8
        };
        if (!_narrow)
        {
            Control avatar = BuildAvatar(app, tone);
            appCell.Children.Add(avatar);
        }

        StackPanel names = new() { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        double nameMaxWidth = string.Equals(_tableColumns, WideColumns, StringComparison.Ordinal) ? 320 : 150;
        names.Children.Add(PageKit.Label(app.Name, _compact ? 10 : 11, PageKit.T.Text, FontWeight.SemiBold,
            nameMaxWidth, TextTrimming.CharacterEllipsis));
        if (!_compact)
        {
            names.Children.Add(PageKit.Label(app.Detail, 8, PageKit.T.Muted, null, nameMaxWidth,
                TextTrimming.CharacterEllipsis));
        }

        if (_model.CategoryService is not null)
        {
            names.Children.Add(CategoryBadge(app));
        }

        Grid.SetColumn(names, _narrow ? 0 : 1);
        appCell.Children.Add(names);
        Grid.SetColumn(appCell, 1);
        row.Children.Add(appCell);

        if (_narrow)
        {
            string value = _sort switch
            {
                1 => PageKit.FormatCount(app.Keys),
                2 => PageKit.FormatCount(app.Clicks),
                _ => PageKit.FormatDuration(app.Seconds)
            };
            AddValue(row, 2, value, tone);
            return row;
        }

        StackPanel duration = new() { Spacing = 3 };
        duration.Children.Add(PageKit.Label(PageKit.FormatDuration(app.Seconds), 10, PageKit.T.Text,
            FontWeight.SemiBold));
        duration.Children.Add(new PageKit.Proportion(fraction, tone) { Height = 4 });
        Grid.SetColumn(duration, 2);
        row.Children.Add(duration);

        if (_compact)
        {
            return row;
        }

        AddValue(row, 3, PageKit.FormatCount(app.Keys), tone);
        AddValue(row, 4, PageKit.FormatCount(app.Clicks), PageKit.T.Green);
        double apm = app.Seconds > 0 ? (app.Keys + app.Clicks) * 60.0 / app.Seconds : 0;
        double keyApm = app.Seconds > 0 ? app.Keys * 60.0 / app.Seconds : 0;
        AddValue(row, 5, $"{apm:0.#} / {keyApm:0.#}", PageKit.T.Cyan);
        return row;
    }

    private Control CategoryBadge(AppRow app)
    {
        Color tone = AppsWidgets.CategoryColor(app.CategoryColor);
        Button badge = new()
        {
            Content = PageKit.Label(
                app.CategoryName,
                7.8,
                tone,
                FontWeight.SemiBold,
                maxWidth: 142,
                trimming: TextTrimming.CharacterEllipsis),
            Padding = new Thickness(5, 1),
            Height = 17,
            MaxWidth = 158,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = PageKit.Brush(PageKit.Alpha(tone, 0.12)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.3)),
            BorderThickness = new Thickness(1)
        };
        ToolTip.SetTip(badge, "点击设置工作性质分类");
        badge.Flyout = CategoryFlyout(app);
        return badge;
    }

    private Flyout CategoryFlyout(AppRow app)
    {
        AppCategoryService? service = _model.CategoryService;
        Flyout flyout = new();
        StackPanel body = new() { Spacing = 5, Width = 230 };
        ScrollViewer list = new()
        {
            MaxHeight = 190,
            Content = new StackPanel { Spacing = 3 }
        };
        StackPanel listBody = (StackPanel)list.Content!;
        if (service is not null)
        {
            foreach (AppCategoryDefinition category in service.Categories)
            {
                Color tone = AppsWidgets.CategoryColor(category.ColorHex);
                Button item = new()
                {
                    Content = PageKit.Label(
                        category.Name + (category.IsWork ? " · 专注" : ""),
                        9.5,
                        tone,
                        FontWeight.Medium),
                    Padding = new Thickness(7, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = PageKit.Brush(PageKit.Alpha(tone, 0.07)),
                    BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.22)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6)
                };
                item.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await _model.AssignCategoryAsync(app, category.CategoryKey);
                };
                listBody.Children.Add(item);
            }
        }

        body.Children.Add(list);
        if (service is not null)
        {
            AppCategoryMatch current = service.Resolve(app.Path, "");
            body.Children.Add(PageKit.Muted(
                $"当前：{current.Category.Name} · {current.Reason}",
                8.5));
        }

        TextBox keyword = new()
        {
            PlaceholderText = "窗口标题包含…",
            FontSize = 9.5,
            Height = 25,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(5)
        };
        ComboBox categoryPicker = new()
        {
            ItemsSource = service?.Categories.Select(static category => category.Name).ToArray() ?? [],
            SelectedIndex = 0,
            FontSize = 9.5,
            Height = 25,
            CornerRadius = new CornerRadius(5)
        };
        Button addRule = new()
        {
            Content = "标题关键词 → 自动归类",
            FontSize = 9,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Accent, 0.12)),
            Foreground = PageKit.Brush(PageKit.T.Accent)
        };
        addRule.Click += async (_, _) =>
        {
            if (service is null || string.IsNullOrWhiteSpace(keyword.Text))
            {
                return;
            }

            int index = Math.Clamp(categoryPicker.SelectedIndex, 0, service.Categories.Count - 1);
            AppCategoryDefinition category = service.Categories[index];
            await _model.AddTitleRuleAsync(app, keyword.Text, category.CategoryKey);
            flyout.Hide();
        };
        body.Children.Add(PageKit.Divider());
        body.Children.Add(keyword);
        body.Children.Add(categoryPicker);
        body.Children.Add(addRule);
        flyout.Content = new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(9),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.98)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.85)),
            BorderThickness = new Thickness(1),
            Child = body
        };
        return flyout;
    }

    private static void AddValue(Grid row, int column, string value, Color tone)
    {
        Control label = PageKit.Label(value, 10, column == 3 ? tone : PageKit.T.Text, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(label, column);
        row.Children.Add(label);
    }

    private static Control BuildAvatar(AppRow app, Color tone)
    {
        Bitmap? icon = AppIconLoader.Get(app.Path);
        if (icon is not null)
        {
            Image image = new()
            {
                Source = icon,
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
            return new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                Background = PageKit.Brush(PageKit.Alpha(tone, 0.08)),
                BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.16)),
                BorderThickness = new Thickness(1),
                Child = image
            };
        }

        return new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(9),
            Background = PageKit.Brush(PageKit.Alpha(tone, 0.18)),
            Child = PageKit.Label(AppsWidgets.Initial(app.Name), 11, tone, FontWeight.Bold)
        };
    }
}

internal sealed class AppsCategoryView : UserControl
{
    private readonly AppsWidgetModel _model;
    private readonly string _variant;
    private readonly bool _compact;
    private readonly Grid _root = new();
    private double _lastHeight = -1;

    public AppsCategoryView(AppsWidgetModel model, string variant, WidgetSpan span)
    {
        _model = model;
        _variant = variant;
        _compact = span.Rows <= 2 && (span.Columns <= 3 || span.Columns >= 6);
        Content = _root;
        Build();
        SizeChanged += (_, _) =>
        {
            if (Math.Abs(Bounds.Height - _lastHeight) > 5)
            {
                Build();
            }
        };
    }

    private void Build()
    {
        _lastHeight = Bounds.Height;
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        _root.RowDefinitions.Clear();
        CategorySnapshot snapshot = _model.Categories;
        if (snapshot.Categories.Count == 0)
        {
            Control empty = PageKit.Empty("暂无分类归因数据");
            empty.VerticalAlignment = VerticalAlignment.Center;
            _root.Children.Add(empty);
            return;
        }

        List<CategoryRow> top = snapshot.Categories
            .OrderByDescending(static category => category.Seconds)
            .Take(_compact ? 3 : 5)
            .ToList();
        double total = Math.Max(0.001, snapshot.AttributedSeconds);
        double rest = Math.Max(0, total - top.Sum(static category => category.Seconds));

        if (string.Equals(_variant, "bars", StringComparison.Ordinal))
        {
            BuildBars(top, rest, total);
            return;
        }

        if (string.Equals(_variant, "treemap", StringComparison.Ordinal))
        {
            List<TreemapItem> items = top
                .Select(category => new TreemapItem(
                    category.Name,
                    category.Seconds,
                    AppsWidgets.CategoryColor(category.ColorHex)))
                .ToList();
            if (rest > 0)
            {
                items.Add(new TreemapItem("其他", rest, PageKit.T.Muted));
            }

            Treemap treemap = new();
            treemap.SetData(items);
            _root.Children.Add(treemap);
            return;
        }

        List<CategoryRow> ringTop = top.Take(3).ToList();
        double ringRest = Math.Max(0, total - ringTop.Sum(static category => category.Seconds));
        List<(Color Color, string Text)> legend = [];
        for (int i = 0; i < ringTop.Count; i++)
        {
            legend.Add((AppsWidgets.CategoryColor(ringTop[i].ColorHex),
                $"{ringTop[i].Name} {ringTop[i].Seconds / total * 100:0.#}%"));
        }

        if (ringRest > 0)
        {
            legend.Add((PageKit.T.Muted, $"其他 {ringRest / total * 100:0.#}%"));
        }

        _root.ColumnDefinitions.Add(new ColumnDefinition(0, GridUnitType.Auto));
        _root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        double ringSize = _compact
            ? Math.Clamp(Bounds.Height > 0 ? Bounds.Height - 38 : 64, 46, 92)
            : Math.Clamp(Bounds.Height > 0 ? Bounds.Height - 6 : 90, 56, 220);
        List<DonutSlice> slices = ringTop
            .Select(category => new DonutSlice(
                category.Seconds,
                AppsWidgets.CategoryColor(category.ColorHex)))
            .ToList();
        if (ringRest > 0)
        {
            slices.Add(new DonutSlice(ringRest, PageKit.T.Muted));
        }

        PageKit.Ring ring = new(slices, $"{snapshot.AttributedSeconds / 3600:0.0}h", "已归因")
        {
            Width = ringSize,
            Height = ringSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _root.Children.Add(ring);
        Control legendControl = BuildLegend(legend);
        legendControl.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(legendControl, 1);
        _root.Children.Add(legendControl);
    }

    private void BuildBars(List<CategoryRow> top, double rest, double total)
    {
        int count = top.Count + (rest > 0 ? 1 : 0);
        _root.RowDefinitions.Add(new RowDefinition(0, GridUnitType.Auto));
        for (int i = 0; i < Math.Max(1, count); i++)
        {
            _root.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        }

        _root.Children.Add(PageKit.Muted("按已归因时长占比", 8.5));
        for (int i = 0; i < count; i++)
        {
            bool other = i >= top.Count;
            string name = other ? "其他" : top[i].Name;
            double seconds = other ? rest : top[i].Seconds;
            Color color = other
                ? PageKit.T.Muted
                : AppsWidgets.CategoryColor(top[i].ColorHex);
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("*,1.2*,42"),
                ColumnSpacing = 8,
                Margin = new Thickness(0, 1)
            };
            double compactBarHeight = _compact ? 5 : 7;
            Control label = PageKit.Label(name, _compact ? 8.8 : 9.5,
                PageKit.T.Text, FontWeight.Medium, null, TextTrimming.CharacterEllipsis);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            PageKit.Proportion bar = new(total > 0 ? seconds / total : 0, color)
            {
                Height = compactBarHeight,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            Control pct = PageKit.Label(total > 0 ? $"{seconds / total * 100:0.#}%" : "--",
                9, color, FontWeight.SemiBold);
            pct.HorizontalAlignment = HorizontalAlignment.Right;
            pct.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pct, 2);
            row.Children.Add(pct);
            Grid.SetRow(row, i + 1);
            _root.Children.Add(row);
        }
    }

    private static Control BuildLegend(List<(Color Color, string Text)> items)
    {
        StackPanel panel = new() { Spacing = 4 };
        foreach ((Color color, string text) in items)
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("10,*") };
            row.Children.Add(new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = PageKit.Brush(color),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Control label = PageKit.Label(text, 9, PageKit.T.Muted, null, null,
                TextTrimming.CharacterEllipsis);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            panel.Children.Add(row);
        }

        return panel;
    }
}

internal sealed class AppsKeyCompositionView : UserControl
{
    private readonly AppsWidgetModel _model;
    private readonly Grid _root = new() { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 4 };
    private readonly Grid _rows = new() { RowSpacing = 4 };
    private readonly ContentControl _legendHost = new();
    private double _lastHeight = -1;
    private double _lastWidth = -1;

    public AppsKeyCompositionView(AppsWidgetModel model)
    {
        _model = model;
        _root.Children.Add(_rows);
        Grid.SetRow(_legendHost, 1);
        _root.Children.Add(_legendHost);
        Content = _root;
        Build();
        SizeChanged += (_, _) =>
        {
            if (Math.Abs(Bounds.Height - _lastHeight) > 5 || Math.Abs(Bounds.Width - _lastWidth) > 24)
            {
                Build();
            }
        };
    }

    private void Build()
    {
        _lastHeight = Bounds.Height;
        _lastWidth = Bounds.Width;
        _rows.Children.Clear();
        _rows.RowDefinitions.Clear();
        AppSnapshot snapshot = _model.Snapshot;
        if (snapshot.Apps.Count == 0)
        {
            Control empty = PageKit.Empty("暂无可对比的应用");
            empty.VerticalAlignment = VerticalAlignment.Center;
            _rows.Children.Add(empty);
            _legendHost.Content = null;
            return;
        }

        int count = Bounds.Height > 0
            ? Math.Clamp((int)((Bounds.Height - 44) / 24), 3, 9)
            : 5;
        List<AppRow> apps = snapshot.Apps.OrderByDescending(static app => app.Seconds).Take(count).ToList();
        for (int i = 0; i < apps.Count; i++)
        {
            _rows.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        }

        Color[] colors =
        [
            PageKit.T.Accent,
            PageKit.T.Green,
            PageKit.T.Orange,
            PageKit.T.Purple,
            PageKit.T.Cyan,
            PageKit.T.Red
        ];
        for (int i = 0; i < apps.Count; i++)
        {
            AppRow app = apps[i];
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions("0.9*,1.4*"),
                ColumnSpacing = 8
            };
            Control label = PageKit.Label(app.Name, 9.5, PageKit.T.Text, FontWeight.Medium, null,
                TextTrimming.CharacterEllipsis);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            StackedBar bar = new(app.Groups, colors)
            {
                Height = 9,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            Grid.SetRow(row, i);
            _rows.Children.Add(row);
        }

        _legendHost.Content = BuildCompactLegend(colors);
    }

    private static Control BuildCompactLegend(Color[] colors)
    {
        string[] labels = ["移动", "数字", "交互", "功能", "文字", "未归位"];
        WrapPanel panel = new();
        for (int i = 0; i < colors.Length; i++)
        {
            StackPanel item = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Margin = new Thickness(0, 0, 8, 2)
            };
            item.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = PageKit.Brush(colors[i]),
                VerticalAlignment = VerticalAlignment.Center
            });
            item.Children.Add(PageKit.Label(labels[i], 8, PageKit.T.Muted));
            panel.Children.Add(item);
        }

        return panel;
    }
}

/// <summary>像素级堆叠条：按实际 Bounds.Width 绘制，窄卡片也不会溢出。</summary>
internal sealed class StackedBar : Control
{
    private readonly long[] _values;
    private readonly Color[] _colors;

    public StackedBar(long[] values, Color[] colors)
    {
        _values = values;
        _colors = colors;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        context.FillRectangle(PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.2)), bounds, 3);
        long total = _values.Sum();
        if (total <= 0)
        {
            return;
        }

        double x = 0;
        for (int i = 0; i < _values.Length; i++)
        {
            long value = _values[i];
            if (value <= 0)
            {
                continue;
            }

            double width = Bounds.Width * value / total;
            Color color = i < _colors.Length ? _colors[i] : PageKit.T.Muted;
            context.FillRectangle(
                PageKit.Brush(color),
                new Rect(x, 0, Math.Max(1, width), Bounds.Height),
                2);
            x += width;
        }
    }
}
