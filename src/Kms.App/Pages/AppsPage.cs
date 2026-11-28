using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Widgets;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.App.Pages;

/// <summary>
/// 应用统计：全局范围聚合、时长构成、排序表、键位组分布。
/// 4.1.0 起由 BentoGridPanel + LayoutConfigService 声明式驱动，内容区零滚动。
/// </summary>
public sealed class AppsPage : UserControl
{
    public const string PageId = LayoutPresets.AppsPageId;

    private readonly StatsData _data;
    private readonly TimeScopeRange _scope;
    private readonly AppPageContext _appContext;
    private AppsWidgetModel _model;
    private readonly LayoutConfigService _layouts;
    private readonly Dictionary<string, WidgetPageModel> _models = new(StringComparer.Ordinal);
    private readonly BentoPageEditor _editor;
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Grid _root;
    private Button? _categoryManagerButton;
    private AppsRankingView? _ranking;
    private bool _compact;
    private bool _tall;

    public AppsPage(
        StatsData data,
        string title,
        string subtitle,
        TimeScopeRange? scope = null,
        LayoutConfigService? layouts = null,
        AppPageContext? appContext = null)
    {
        _data = data;
        _scope = scope ?? TimeScopeRange.Create(TimeScope.Today, DateTime.Now);
        _appContext = appContext ?? AppPageContext.All;
        _model = new AppsWidgetModel(data, _scope, _appContext);
        _model.CategoryChanged += ReloadApps;
        _layouts = layouts ?? LayoutConfigService.Default;
        _editor = new BentoPageEditor(PageId, _layouts, CreateContext);
        _editor.Rebuilt += SyncRanking;

        _root = new Grid
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 10,
            ClipToBounds = true
        };
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (_appContext.CategoryService is not null)
        {
            actions.Children.Add(CreateCategoryManagerButton());
        }

        actions.Children.Add(_editor.CreateEntryButton());
        _root.Children.Add(PageKit.PageHeader(
            "APPS / ATTRIBUTION",
            title,
            subtitle,
            _scope.Caption,
            actions));

        Grid.SetRow(_filters, 1);
        _root.Children.Add(_filters);

        Grid.SetRow(_editor, 2);
        _root.Children.Add(_editor);

        Content = _root;
        SyncRanking();
        BuildFilters();
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
    }

    private WidgetContext CreateContext() => new()
    {
        Data = _data,
        Scope = _scope,
        AppFilter = _appContext.Filter,
        Capabilities = _appContext.Capabilities,
        Profile = _appContext.Profile,
        AppPage = _appContext,
        VariantId = "",
        Model = _model,
        Models = _models
    };

    private Button CreateCategoryManagerButton()
    {
        Button button = new()
        {
            Content = "管理分类 / 规则",
            FontSize = 10.5,
            Height = 27,
            Padding = new Thickness(11, 0),
            CornerRadius = new CornerRadius(8),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.65)),
            Foreground = PageKit.Brush(PageKit.T.Muted),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
            BorderThickness = new Thickness(1)
        };
        button.Flyout = new Flyout { Content = BuildCategoryManager() };
        _categoryManagerButton = button;
        return button;
    }

    internal void OpenCategoryManager()
    {
        if (_categoryManagerButton?.Flyout is not null)
        {
            _categoryManagerButton.Flyout.ShowAt(_categoryManagerButton);
        }
    }

    private Control BuildCategoryManager()
    {
        AppCategoryService service = _appContext.CategoryService!;
        ComboBox picker = new()
        {
            FontSize = 10,
            Height = 27,
            ItemsSource = service.Categories.Select(static category => category.Name).ToArray(),
            SelectedIndex = 0
        };
        TextBox name = new()
        {
            FontSize = 10,
            Height = 26,
            PlaceholderText = "分类名称",
            Padding = new Thickness(6, 0)
        };
        TextBox color = new()
        {
            FontSize = 10,
            Height = 26,
            PlaceholderText = "#8B5CF6",
            Padding = new Thickness(6, 0)
        };
        CheckBox work = new()
        {
            Content = "计入专注工作类",
            FontSize = 10,
            Height = 24
        };
        NumericUpDown sort = new()
        {
            FontSize = 10,
            Height = 26,
            Minimum = 0,
            Maximum = 999,
            Increment = 1,
            FormatString = "0",
            Width = 96
        };
        Button save = SmallAction("保存修改", PageKit.T.Accent);
        Button create = SmallAction("新建分类", PageKit.T.Green);
        Button delete = SmallAction("删除自定义", PageKit.T.Red);
        StackPanel edit = new() { Spacing = 5 };

        static string CleanName(string? value, string fallback)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0)
            {
                text = fallback;
            }

            return text.Length > 24 ? text[..24] : text;
        }

        int IndexOfKey(string categoryKey)
        {
            for (int i = 0; i < service.Categories.Count; i++)
            {
                if (string.Equals(
                    service.Categories[i].CategoryKey,
                    categoryKey,
                    StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        void RefreshPicker(int selected = 0)
        {
            picker.ItemsSource = service.Categories.Select(static category => category.Name).ToArray();
            picker.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, service.Categories.Count - 1));
        }

        void Fill()
        {
            int index = Math.Clamp(picker.SelectedIndex, 0, service.Categories.Count - 1);
            AppCategoryDefinition category = service.Categories[index];
            name.Text = category.Name;
            color.Text = category.ColorHex;
            work.IsChecked = category.IsWork;
            sort.Value = category.SortOrder;
            delete.IsEnabled = !category.IsBuiltin;
        }

        picker.SelectionChanged += (_, _) => Fill();
        save.Click += async (_, _) =>
        {
            int index = Math.Clamp(picker.SelectedIndex, 0, service.Categories.Count - 1);
            AppCategoryDefinition category = service.Categories[index];
            await service.UpdateCategoryAsync(
                category.CategoryKey,
                CleanName(name.Text, category.Name),
                color.Text ?? category.ColorHex,
                work.IsChecked == true,
                (int)(sort.Value ?? category.SortOrder));
            ReloadApps();
            RefreshPicker(IndexOfKey(category.CategoryKey));
        };
        create.Click += async (_, _) =>
        {
            AppCategoryDefinition category = await service.CreateCategoryAsync(
                CleanName(name.Text, "新分类"),
                color.Text ?? "#8B5CF6",
                work.IsChecked == true,
                (int)(sort.Value ?? service.Categories.Count));
            ReloadApps();
            RefreshPicker(IndexOfKey(category.CategoryKey));
        };
        delete.Click += async (_, _) =>
        {
            int index = Math.Clamp(picker.SelectedIndex, 0, service.Categories.Count - 1);
            if (await service.DeleteCategoryAsync(service.Categories[index].CategoryKey))
            {
                ReloadApps();
                RefreshPicker(0);
            }
        };

        WrapPanel palette = new() { Orientation = Orientation.Horizontal };
        foreach (string hex in new[] { "#8B5CF6", "#EC4899", "#06B6D4", "#22C55E", "#F59E0B", "#EF4444", "#64748B" })
        {
            string selected = hex;
            Button swatch = new()
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                CornerRadius = new CornerRadius(9),
                Background = PageKit.Brush(AppsWidgets.CategoryColor(hex)),
                BorderThickness = new Thickness(0)
            };
            swatch.Click += (_, _) => color.Text = selected;
            palette.Children.Add(swatch);
        }

        edit.Children.Add(PageKit.Muted("选择分类后编辑；内置分类可改名/配色，自定义分类可删除。", 9));
        edit.Children.Add(picker);
        edit.Children.Add(name);
        edit.Children.Add(color);
        edit.Children.Add(palette);
        StackPanel sortRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7
        };
        sortRow.Children.Add(PageKit.Muted("排序", 9));
        sortRow.Children.Add(sort);
        edit.Children.Add(sortRow);
        edit.Children.Add(work);
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
        actions.Children.Add(save);
        actions.Children.Add(create);
        actions.Children.Add(delete);
        edit.Children.Add(actions);
        Fill();

        return new Border
        {
            Width = 292,
            MaxHeight = 360,
            Padding = new Thickness(9),
            CornerRadius = new CornerRadius(10),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.98)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.9)),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = edit }
        };
    }

    private static Button SmallAction(string text, Avalonia.Media.Color tone) => new()
    {
        Content = text,
        FontSize = 9,
        Height = 25,
        Padding = new Thickness(7, 0),
        CornerRadius = new CornerRadius(5),
        Background = PageKit.Brush(PageKit.Alpha(tone, 0.12)),
        Foreground = PageKit.Brush(tone),
        BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.3)),
        BorderThickness = new Thickness(1)
    };

    private void ReloadApps()
    {
        _model.CategoryChanged -= ReloadApps;
        _model = new AppsWidgetModel(_data, _scope, _appContext);
        _model.CategoryChanged += ReloadApps;
        _editor.Refresh();
        SyncRanking();
        BuildFilters();
    }

    private void SyncRanking()
    {
        _ranking = _editor.Built.TryGetValue("apps.ranking_list", out BuiltWidget? built)
                   && built.View.Content is AppsRankingView ranking
            ? ranking
            : null;
        _ranking?.SetSort(_model.Sort);
    }

    private void BuildFilters()
    {
        _filters.Children.Clear();
        foreach ((string label, int sort) in new[] { ("按时长", 0), ("按击键", 1), ("按点击", 2) })
        {
            int selected = sort;
            _filters.Children.Add(PageKit.Filter(label, _model.Sort == sort, () =>
            {
                _model.Sort = selected;
                _ranking?.SetSort(selected);
                BuildFilters();
            }, PageKit.T.Green));
        }

        _filters.Children.Add(new Border { Width = 4 });
        _filters.Children.Add(PageKit.Muted(
            _model.IsSingleApp
                ? $"{_scope.Caption} · 单应用窗口/文档明细 · {_model.Snapshot.Apps.Count} 项"
                : $"{_scope.Caption} · {_model.Snapshot.Days} 天有记录 · {_model.Snapshot.Apps.Count} 个应用",
            10));
    }

    private void ApplyDensity(double height)
    {
        bool compact = PageKit.IsCompact(height);
        bool tall = PageKit.IsTall(height);
        if (_compact == compact && _tall == tall)
        {
            return;
        }

        _compact = compact;
        _tall = tall;
        _root.Margin = compact
            ? new Thickness(16, 12, 16, 8)
            : tall
                ? new Thickness(26, 22, 26, 16)
                : new Thickness(20, 16, 20, 12);
        _root.RowSpacing = compact ? 8 : tall ? 14 : 10;
        _editor.Spacing = compact ? 8 : tall ? 14 : 12;
    }
}
