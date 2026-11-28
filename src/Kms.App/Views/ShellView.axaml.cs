using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Pages;
using Kms.App.Platform;
using Kms.App.Theming;
using Kms.App.Widgets;
using Kms.Core.Apps;
using Kms.Storage.Apps;
using Kms.Core;
using Kms.Core.Time;
using Kms.Render.Theming;
using Kms.Render.Tokens;

namespace Kms.App.Views;

public sealed partial class ShellView : UserControl
{
    private static readonly (string Icon, string Title, string Subtitle)[] Pages =
    [
        ("▦", "总览", "今天与最近 7 天的整体节奏"),
        ("↗", "趋势", "选择区间与基线，查看变化"),
        ("◔", "时段分布", "24 小时内的输入与活跃时长"),
        ("▤", "按键排行", "排行、按住时长、组合键与配列"),
        ("▣", "应用统计", "前台窗口、APM 与时长占比"),
        ("◇", "使用洞察", "专注段、上下文切换与时光机"),
        ("字", "字符内容", "Rime 上屏文字与词频"),
        ("▤", "日报", "单日摘要与可导出长图"),
        ("◎", "点击热区", "手指最常落在屏幕的哪一块？位置按 32 像素网格记录，不截屏")
    ];

    private readonly InputService? _service;
    private readonly LoadedStats _loaded;
    private readonly MainViewModel _viewModel;
    private readonly LayoutConfigService _layouts;
    private readonly Button[] _navButtons = new Button[Pages.Length];
    private readonly TextBlock[] _navIcons = new TextBlock[Pages.Length];
    private readonly TextBlock[] _navTitles = new TextBlock[Pages.Length];
    private readonly DispatcherTimer _liveTimer;
    private readonly ScaleTransform _pageScale = new(1, 1);
    private readonly LayoutTransformControl _pageLayout;
    private AppProfileSnapshot? _appProfile;
    private CancellationTokenSource? _appProfileCts;
    private int _appProfileVersion;
    private readonly AppIconCacheService _appIcons = new();
    private Button? _appFilterButton;
    private Button? _appFilterClear;
    private Border? _appFilterCard;
    private TextBlock? _appFilterLabel;
    private Bitmap? _backdrop;
    private string? _customThemePath;
    private string _customThemeName = "自定义皮肤";
    private int _current;
    private TimeScope _lastScope;
    private bool _compactSidebar;

    public ShellView() : this((InputService?)null)
    {
    }

    public ShellView(InputService? service)
        : this(new MainViewModel(service?.Loaded ?? StatsLoader.Load(), service))
    {
    }

    internal ShellView(LoadedStats loaded, InputService? service = null, LayoutConfigService? layouts = null)
        : this(new MainViewModel(loaded, service), layouts)
    {
    }

    public ShellView(MainViewModel viewModel, LayoutConfigService? layouts = null)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _loaded = viewModel.Loaded;
        _service = viewModel.Service;
        _layouts = layouts ?? LayoutConfigService.Default;
        _lastScope = viewModel.SelectedTimeScope;

        int themeId = int.TryParse(
            _loaded.Data.Settings.GetValueOrDefault("theme_id")
                ?? _loaded.Data.Settings.GetValueOrDefault("art_theme"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int savedTheme)
            ? savedTheme
            : 0;
        _loaded.Data.Settings["theme_id"] = ThemeCatalog.Validate(themeId).ToString(CultureInfo.InvariantCulture);

        if (Application.Current is { } application)
        {
            ThemeManager.Apply(application, ThemeCatalog.Validate(themeId));
            ApplyThemeVariant(application);
        }

        _customThemePath = _loaded.Data.Settings.GetValueOrDefault("custom_theme_path");
        if (string.IsNullOrWhiteSpace(_customThemePath) || !System.IO.File.Exists(_customThemePath))
        {
            if (ThemeArtwork.TryFindV3Custom(out string migrated, out string migratedName))
            {
                _customThemePath = migrated;
                _customThemeName = migratedName;
                _loaded.Data.Settings["custom_theme_path"] = migrated;
                _loaded.Data.Settings["custom_theme_name"] = migratedName;
            }
            else
            {
                _customThemePath = null;
            }
        }
        else
        {
            _customThemeName = _loaded.Data.Settings.GetValueOrDefault("custom_theme_name")
                ?? System.IO.Path.GetFileNameWithoutExtension(_customThemePath);
        }

        ApplyCustomPalette();

        _pageLayout = new LayoutTransformControl
        {
            LayoutTransform = _pageScale
        };
        PageHost.Content = _pageLayout;
        BuildNavigation();
        BuildScopeSelector();
        BuildAppFilter();
        BuildThemeSwatches();
        ShowPage(0);
        UpdateStatus();

        KeyDown += OnKeyDown;
        PointerPressed += (_, _) => Focus();
        SizeChanged += (_, _) =>
        {
            ApplySidebarDensity(Bounds.Height);
            ApplyPageScale();
        };
        _viewModel.TimeScopeChanged += OnTimeScopeChanged;
        _viewModel.AppFilterChanged += OnAppFilterChanged;
        _viewModel.Categories.Changed += OnCategoriesChanged;

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += (_, _) =>
        {
            _viewModel.RefreshTimeScope();
            UpdateStatus();
        };
        _liveTimer.Start();
    }

    private void BuildNavigation()
    {
        for (int i = 0; i < Pages.Length; i++)
        {
            int index = i;
            ThemeTokens theme = ThemeManager.Current;

            TextBlock icon = new()
            {
                Text = Pages[i].Icon,
                FontSize = 14,
                Width = 22,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(theme.Muted),
                FontFamily = new FontFamily("Segoe UI Symbol, Microsoft YaHei UI, Segoe UI")
            };

            TextBlock title = new()
            {
                Text = Pages[i].Title,
                FontSize = 13,
                FontWeight = FontWeight.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(theme.Muted)
            };

            Grid content = new()
            {
                ColumnDefinitions = new ColumnDefinitions("24,*"),
                ColumnSpacing = 10
            };
            content.Children.Add(icon);
            Grid.SetColumn(title, 1);
            content.Children.Add(title);

            Button button = new() { Content = content };
            button.Classes.Add("nav");
            button.Click += (_, _) => ShowPage(index);
            NavPanel.Children.Add(button);

            _navButtons[i] = button;
            _navIcons[i] = icon;
            _navTitles[i] = title;
        }
    }

    private void BuildScopeSelector()
    {
        ScopePanel.Children.Clear();
        (TimeScope Scope, string Text)[] options =
        [
            (TimeScope.Today, "今日"),
            (TimeScope.ThisWeek, "本周"),
            (TimeScope.ThisMonth, "本月")
        ];

        for (int i = 0; i < options.Length; i++)
        {
            TimeScope scope = options[i].Scope;
            Button button = new()
            {
                Content = options[i].Text,
                FontSize = _compactSidebar ? 9.5 : 10.5,
                FontWeight = _viewModel.SelectedTimeScope == scope ? FontWeight.SemiBold : FontWeight.Normal
            };
            button.Classes.Add("scope");
            button.Classes.Set("active", _viewModel.SelectedTimeScope == scope);
            ToolTip.SetTip(button, scope switch
            {
                TimeScope.ThisWeek => "本周（周一起）",
                TimeScope.ThisMonth => "本月（1 日起）",
                _ => "今日 24 小时"
            });
            button.Click += (_, _) => _viewModel.SelectedTimeScope = scope;
            Grid.SetColumn(button, i);
            ScopePanel.Children.Add(button);
        }

        ScopeRange.Text = _viewModel.Range.Caption;
        ToolTip.SetTip(ScopeRange, $"{_viewModel.Range.Start:yyyy-MM-dd} → {_viewModel.Range.End:yyyy-MM-dd}");
    }

    private void BuildAppFilter()
    {
        AppFilterHost.Children.Clear();
        Border card = new()
        {
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.72)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = _compactSidebar ? new Thickness(5, 3) : new Thickness(7, 6)
        };
        _appFilterCard = card;
        StackPanel body = new() { Spacing = 4 };
        _appFilterLabel = PageKit.Muted("应用上下文", 8.5);
        _appFilterLabel.IsVisible = !_compactSidebar;
        body.Children.Add(_appFilterLabel);
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 4
        };

        _appFilterButton = new Button
        {
            FontSize = 10,
            Height = _compactSidebar ? 22 : 25,
            Padding = new Thickness(7, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.7)),
            Foreground = PageKit.Brush(PageKit.T.Text),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
            BorderThickness = new Thickness(1)
        };
        _appFilterButton.Flyout = CreateAppFilterFlyout();
        row.Children.Add(_appFilterButton);

        _appFilterClear = new Button
        {
            Content = "×",
            FontSize = 13,
            Width = _compactSidebar ? 22 : 25,
            Height = _compactSidebar ? 22 : 25,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.7)),
            Foreground = PageKit.Brush(PageKit.T.Muted),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
            BorderThickness = new Thickness(1)
        };
        ToolTip.SetTip(_appFilterClear, "清除应用筛选，回到全部应用");
        _appFilterClear.Click += (_, _) => _viewModel.SelectedAppFilter = AppFilterScope.All;
        Grid.SetColumn(_appFilterClear, 1);
        row.Children.Add(_appFilterClear);
        body.Children.Add(row);
        card.Child = body;
        AppFilterHost.Children.Add(card);
        UpdateAppFilterChrome();
    }

    private Flyout CreateAppFilterFlyout()
    {
        Flyout flyout = new() { Content = BuildAppFilterFlyout() };
        flyout.Opening += (_, _) => flyout.Content = BuildAppFilterFlyout();
        return flyout;
    }

    private Control BuildAppFilterFlyout()
    {
        IReadOnlyList<AppFilterOption> options = _viewModel.GetRecentApps(
            _viewModel.Range,
            40);
        AppCategoryService categories = _viewModel.Categories;
        double maxSeconds = options.Count == 0
            ? 1
            : options.Max(static option => option.ActiveSeconds);
        double windowWidth = Bounds.Width > 0 ? Bounds.Width : 1180;
        double panelWidth = Math.Min(400, Math.Max(260, windowWidth - 48));
        double maxListHeight = Math.Clamp(
            (Bounds.Height > 0 ? Bounds.Height : 720) - 210,
            220,
            520);

        WrapPanel tiles = new() { Orientation = Orientation.Horizontal };
        Grid categoryList = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            RowSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        TextBox search = new()
        {
            PlaceholderText = "搜索应用…",
            FontSize = 10.5,
            Height = 28,
            Padding = new Thickness(8, 0),
            CornerRadius = new CornerRadius(6),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.7)),
            Foreground = PageKit.Brush(PageKit.T.Text),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.75)),
            BorderThickness = new Thickness(1)
        };
        ScrollViewer scroller = new()
        {
            MaxHeight = maxListHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = tiles
        };

        string mode = "app";
        Button appTab = SegmentButton("按应用", true);
        Button categoryTab = SegmentButton("按工作性质", false);
        Grid segments = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 3
        };
        segments.Children.Add(appTab);
        Grid.SetColumn(categoryTab, 1);
        segments.Children.Add(categoryTab);

        void RenderApps(string query)
        {
            tiles.Children.Clear();
            if (options.Count == 0)
            {
                tiles.Children.Add(PageKit.Muted("当前时间范围暂无应用记录", 9.5));
                return;
            }

            IEnumerable<AppFilterOption> filtered = options;
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(option =>
                    option.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || option.ProcessPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (option.AppKeys?.Any(key =>
                        key.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false));
            }

            AppFilterOption[] shown = filtered.Take(40).ToArray();
            if (shown.Length == 0)
            {
                tiles.Children.Add(PageKit.Muted("没有匹配的应用", 9.5));
                return;
            }

            foreach (AppFilterOption option in shown)
            {
                tiles.Children.Add(BuildAppTile(option, maxSeconds, panelWidth));
            }
        }

        void RenderCategories()
        {
            categoryList.Children.Clear();
            categoryList.RowDefinitions.Clear();
            IReadOnlyList<AppCategoryDefinition> all = categories.Categories;
            if (all.Count == 0)
            {
                categoryList.Children.Add(PageKit.Muted("暂无分类", 9.5));
                return;
            }

            for (int i = 0; i < all.Count; i++)
            {
                AppCategoryDefinition category = all[i];
                AppCategoryUsage usage = categories.GetUsage(
                    category.CategoryKey,
                    _viewModel.Range.Start,
                    _viewModel.Range.End);
                int row = i / 2;
                if (row >= categoryList.RowDefinitions.Count)
                {
                    categoryList.RowDefinitions.Add(new RowDefinition(0, GridUnitType.Auto));
                }

                Button card = BuildCategoryCard(category, usage);
                Grid.SetColumn(card, i % 2);
                Grid.SetRow(card, row);
                categoryList.Children.Add(card);
            }
        }

        Button manage = new()
        {
            Content = "⚙  管理分类与自动归类规则",
            FontSize = 9.5,
            Height = 27,
            IsVisible = false,
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.68)),
            Foreground = PageKit.Brush(PageKit.T.Muted),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.7)),
            BorderThickness = new Thickness(1)
        };
        manage.Click += (_, _) => OpenCategoryManagerFromFilter();

        void RenderCurrent()
        {
            search.IsVisible = mode == "app";
            manage.IsVisible = mode == "category";
            if (mode == "app")
            {
                scroller.Content = tiles;
                RenderApps(search.Text ?? "");
            }
            else
            {
                scroller.Content = categoryList;
                RenderCategories();
            }
        }

        void SetMode(string next)
        {
            mode = next;
            StyleSegment(appTab, mode == "app");
            StyleSegment(categoryTab, mode == "category");
            RenderCurrent();
        }

        appTab.Click += (_, _) => SetMode("app");
        categoryTab.Click += (_, _) => SetMode("category");
        search.TextChanged += (_, _) => RenderApps(search.Text ?? "");

        StackPanel panel = new()
        {
            Spacing = 9,
            Width = panelWidth,
            Children =
            {
                segments,
                search,
                scroller,
                manage
            }
        };
        RenderCurrent();
        return new Border
        {
            Padding = new Thickness(10, 12),
            CornerRadius = new CornerRadius(10),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Card, 0.98)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.9)),
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }

    private Control BuildAppTile(AppFilterOption option, double maxSeconds, double panelWidth)
    {
        double size = AppTileMetrics.SizeFor(option.ActiveSeconds, maxSeconds);
        double iconSize = AppTileMetrics.IconSizeFor(size);
        // 400px 面板内一行 4 个磁贴：76px 下限保证短名/时长有完整横向空间。
        double tileWidth = Math.Max(76, Math.Min(panelWidth / 3.6, size + 24));
        Color tone = AppsWidgets.ColorFor(option.DisplayName);
        ContentControl iconHost = new()
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        AppIconPresenter.SetFallback(iconHost, option.DisplayName, size, tone);
        _ = AppIconPresenter.ApplyAsync(
            iconHost,
            option.ProcessPath,
            iconSize,
            size,
            tone,
            option.DisplayName,
            _appIcons);

        StackPanel body = new()
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                iconHost,
                Centered(PageKit.Label(
                    ShortAppName(option.DisplayName),
                    8.4,
                    PageKit.T.Text,
                    FontWeight.SemiBold,
                    tileWidth - 8,
                    TextTrimming.CharacterEllipsis)),
                Centered(PageKit.Label(
                    FormatCompactDuration(option.ActiveSeconds),
                    8.0,
                    PageKit.T.Muted,
                    null,
                    tileWidth - 8,
                    TextTrimming.CharacterEllipsis))
            }
        };

        Button tile = new()
        {
            Width = tileWidth,
            MinWidth = 76,
            Padding = new Thickness(3),
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.42)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.55)),
            BorderThickness = new Thickness(1),
            Content = body
        };
        ToolTip.SetTip(
            tile,
            option.DisplayName
            + "\n" + option.ProcessPath
            + (option.AppKeys is { Count: > 1 }
                ? $"\n合并 {option.AppKeys.Count} 个历史路径"
                : "")
            + "\n" + PageKit.FormatDuration(option.ActiveSeconds));
        tile.Click += (_, _) =>
        {
            _appFilterButton?.Flyout?.Hide();
            IReadOnlyList<string> appKeys = option.AppKeys is { Count: > 0 }
                ? option.AppKeys
                : new[] { option.AppKey };
            _viewModel.SelectedAppFilter = AppFilterScope.ByAppGroup(
                string.IsNullOrWhiteSpace(option.ProcessPath)
                    ? option.AppKey
                    : option.ProcessPath,
                appKeys);
        };
        return tile;
    }

    private Button BuildCategoryCard(
        AppCategoryDefinition category,
        AppCategoryUsage usage)
    {
        Color tone = AppsWidgets.CategoryColor(category.ColorHex);
        Grid header = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 5
        };
        Border dot = new()
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = PageKit.Brush(tone),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(dot);

        TextBlock name = PageKit.Label(
            category.Name,
            9.6,
            PageKit.T.Text,
            FontWeight.SemiBold,
            maxWidth: 118,
            trimming: TextTrimming.CharacterEllipsis);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        header.Children.Add(name);

        if (category.IsWork)
        {
            Border badge = new()
            {
                Padding = new Thickness(3, 0),
                CornerRadius = new CornerRadius(4),
                Background = PageKit.Brush(PageKit.Alpha(tone, 0.14)),
                Child = PageKit.Label("专注", 7.2, tone, FontWeight.SemiBold)
            };
            badge.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(badge, 2);
            header.Children.Add(badge);
        }

        StackPanel body = new()
        {
            Spacing = 3,
            Children =
            {
                header,
                PageKit.Label(
                    $"{usage.AppCount} 个应用 · {FormatCompactDuration(usage.ActiveSeconds)}",
                    8.2,
                    PageKit.T.Muted,
                    maxWidth: 150,
                    trimming: TextTrimming.CharacterEllipsis)
            }
        };

        Button card = new()
        {
            Padding = new Thickness(7),
            MinHeight = 48,
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Raised, 0.42)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.55)),
            BorderThickness = new Thickness(1),
            Content = body
        };
        ToolTip.SetTip(card, $"{category.Name} · {usage.AppCount} 个应用 · {PageKit.FormatDuration(usage.ActiveSeconds)}");
        card.Click += (_, _) =>
        {
            _appFilterButton?.Flyout?.Hide();
            _viewModel.SelectedAppFilter = AppFilterScope.ByCategory(category.CategoryKey);
        };
        return card;
    }

    private void OpenCategoryManagerFromFilter()
    {
        _appFilterButton?.Flyout?.Hide();
        ShowPage(4);
        if (_pageLayout.Child is AppsPage apps)
        {
            apps.OpenCategoryManager();
        }
    }

    private static Control Centered(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        return control;
    }

    private static string ShortAppName(string name)
    {
        string text = (name ?? "").Trim();
        return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? text[..^4]
            : text;
    }

    private static string FormatCompactDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return "0s";
        }

        if (seconds >= 3600)
        {
            return $"{seconds / 3600:0.#}h";
        }

        return seconds >= 60
            ? $"{seconds / 60:0}m"
            : $"{seconds:0}s";
    }



    private static Button SegmentButton(string text, bool selected)
    {
        Button button = new()
        {
            Content = text,
            FontSize = 9.8,
            Height = 25,
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        StyleSegment(button, selected);
        return button;
    }

    private static void StyleSegment(Button button, bool selected)
    {
        button.Background = PageKit.Brush(PageKit.Alpha(
            selected ? PageKit.T.Accent : PageKit.T.Raised,
            selected ? 0.2 : 0.5));
        button.Foreground = PageKit.Brush(selected ? PageKit.T.Accent : PageKit.T.Muted);
        button.BorderBrush = PageKit.Brush(PageKit.Alpha(
            selected ? PageKit.T.Accent : PageKit.T.Line,
            selected ? 0.42 : 0.4));
        button.BorderThickness = new Thickness(1);
    }

    private void UpdateAppFilterChrome()
    {
        if (_appFilterButton is null)
        {
            return;
        }

        AppFilterScope filter = _viewModel.SelectedAppFilter;
        string text = "全部应用  ▾";
        string tip = "当前显示全部应用";
        switch (filter.Kind)
        {
            case AppFilterKind.App:
                string appName = AppDisplayName.FriendlyName(
                    AppIdentity.DisplayName(filter.AppKey));
                text = appName + "  ▾";
                tip = "当前仅显示 " + appName;
                break;
            case AppFilterKind.Category:
                AppCategoryDefinition? category = _viewModel.Categories.Find(filter.CategoryKey ?? "");
                AppCategoryUsage usage = _viewModel.Categories.GetUsage(
                    filter.CategoryKey ?? "",
                    _viewModel.Range.Start,
                    _viewModel.Range.End);
                text = (category?.Name ?? "工作性质") + "  ▾";
                tip = $"当前按工作性质「{category?.Name ?? "未知"}」聚合，当前范围 {usage.AppCount} 个应用";
                break;
        }

        _appFilterButton.Content = BuildAppFilterButtonContent(filter, text);
        ToolTip.SetTip(_appFilterButton, tip);
        if (_appFilterClear is not null)
        {
            _appFilterClear.IsVisible = filter.Kind != AppFilterKind.All;
        }
    }

    private Control BuildAppFilterButtonContent(AppFilterScope filter, string text)
    {
        TextBlock label = PageKit.Label(
            text,
            10,
            PageKit.T.Text,
            maxWidth: 142,
            trimming: TextTrimming.CharacterEllipsis);
        label.VerticalAlignment = VerticalAlignment.Center;
        if (filter.Kind != AppFilterKind.App)
        {
            return label;
        }

        string name = AppDisplayName.FriendlyName(
            AppIdentity.DisplayName(filter.AppKey));
        Color tone = AppsWidgets.ColorFor(name);
        ContentControl iconHost = new()
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        AppIconPresenter.SetFallback(iconHost, name, 16, tone);
        _ = AppIconPresenter.ApplyAsync(
            iconHost,
            filter.AppKey ?? "",
            11,
            16,
            tone,
            name,
            _appIcons);

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                iconHost,
                label
            }
        };
        return row;
    }

    private void ApplySidebarDensity(double height)
    {
        bool compact = height > 0 && height < 700;
        if (compact == _compactSidebar)
        {
            return;
        }

        _compactSidebar = compact;
        TopPanel.Spacing = compact ? 6 : 10;
        NavPanel.Spacing = compact ? 2 : 4;
        ShortcutHints.IsVisible = !compact;
        if (_appFilterCard is not null)
        {
            _appFilterCard.Padding = compact ? new Thickness(5, 3) : new Thickness(7, 6);
        }

        if (_appFilterLabel is not null)
        {
            _appFilterLabel.IsVisible = !compact;
        }

        if (_appFilterButton is not null)
        {
            _appFilterButton.Height = compact ? 22 : 25;
        }

        if (_appFilterClear is not null)
        {
            _appFilterClear.Height = compact ? 22 : 25;
            _appFilterClear.Width = compact ? 22 : 25;
        }
        for (int i = 0; i < _navButtons.Length; i++)
        {
            _navButtons[i].Padding = compact ? new Thickness(9, 6) : new Thickness(11, 9);
            _navTitles[i].FontSize = compact ? 12 : 13;
        }

        BuildScopeSelector();
    }

    /// <summary>
    /// 高视口/全屏统一放大整页：小窗保持 1:1 紧凑，大窗把字体、图标、图表一起放大，
    /// 而不是只增加行数导致字号仍然按 9-10px 绘制。
    /// </summary>
    private void ApplyPageScale()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        double raw = Math.Min(Bounds.Width / 1500.0, Bounds.Height / 880.0);
        double scale = 1.0;
        if (Bounds.Width >= 1366 && Bounds.Height >= 800)
        {
            scale = Math.Clamp(raw, 1.1, 1.35);
        }
        if (Math.Abs(_pageScale.ScaleX - scale) < 0.001
            && Math.Abs(_pageScale.ScaleY - scale) < 0.001)
        {
            return;
        }

        _pageScale.ScaleX = scale;
        _pageScale.ScaleY = scale;
    }

    private void OnTimeScopeChanged(object? sender, TimeScopeChangedEventArgs e)
    {
        bool cameFromPeriod = _lastScope != TimeScope.Today;
        _lastScope = e.Range.Scope;

        // 从本周/本月切回今日时回到今日日报；标签页之间切换仍保留日报翻日游标。
        if (cameFromPeriod && e.Range.Scope == TimeScope.Today)
        {
            _viewModel.SelectedDay = null;
        }

        BuildScopeSelector();
        if (_appFilterButton?.Flyout is Flyout flyout)
        {
            flyout.Content = BuildAppFilterFlyout();
        }

        BeginAppProfileRefresh();
        UpdateStatus();
    }

    private void OnAppFilterChanged(object? sender, AppFilterChangedEventArgs e)
    {
        UpdateAppFilterChrome();
        BeginAppProfileRefresh();
    }

    private void OnCategoriesChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnCategoriesChanged);
            return;
        }

        UpdateAppFilterChrome();
        if (_appFilterButton is not null)
        {
            _appFilterButton.Flyout = CreateAppFilterFlyout();
        }

        AppFilterScope filter = _viewModel.SelectedAppFilter;
        if (filter.Kind == AppFilterKind.Category
            && _viewModel.Categories.Find(filter.CategoryKey ?? "") is null)
        {
            _viewModel.SelectedAppFilter = AppFilterScope.All;
            return;
        }

        if (filter.Kind == AppFilterKind.Category)
        {
            BeginAppProfileRefresh();
        }
    }

    /// <summary>
    /// 过滤/时间范围变化时先在能力 Pending 状态重建页面，再后台取新快照替换。
    /// 绝不在 UI 线程同步打开 SQLite。
    /// </summary>
    private void BeginAppProfileRefresh()
    {
        _appProfileVersion++;
        int version = _appProfileVersion;
        _appProfileCts?.Cancel();
        _appProfileCts = null;
        _appProfile = null;

        ShowPage(_current, refreshRime: false);
        AppFilterScope filter = _viewModel.SelectedAppFilter;
        if (filter.Kind == AppFilterKind.All)
        {
            return;
        }

        CancellationTokenSource cts = new();
        _appProfileCts = cts;
        _ = LoadAppProfileAsync(version, filter, cts);
    }

    private async Task LoadAppProfileAsync(
        int version,
        AppFilterScope filter,
        CancellationTokenSource cts)
    {
        try
        {
            AppProfileSnapshot? profile = await _viewModel.QueryAppProfileAsync(
                filter,
                _viewModel.Range,
                cts.Token);
            if (version != _appProfileVersion || cts.IsCancellationRequested)
            {
                return;
            }

            _appProfile = profile;
            ShowPage(_current, refreshRime: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (version == _appProfileVersion)
            {
                _appProfile = null;
                ShowPage(_current, refreshRime: false);
            }
        }
        finally
        {
            if (ReferenceEquals(_appProfileCts, cts))
            {
                _appProfileCts = null;
            }

            cts.Dispose();
        }
    }

    private void BuildThemeSwatches()
    {
        ThemePanel.Children.Clear();
        ThemeTokens current = ThemeManager.Current;
        bool custom = !string.IsNullOrWhiteSpace(_customThemePath)
            && System.IO.File.Exists(_customThemePath);
        ThemeLabel.Text = custom ? $"外观 · {_customThemeName}" : $"外观 · {ShortThemeName(current)}";

        foreach (ThemeTokens theme in ThemeCatalog.All)
        {
            Button swatch = new()
            {
                Background = new SolidColorBrush(theme.Accent),
                BorderBrush = new SolidColorBrush(
                    theme.Id == current.Id ? current.Text : PageKit.Alpha(current.Line, 0.8)),
                BorderThickness = new Thickness(theme.Id == current.Id ? 2 : 1),
                CornerRadius = new CornerRadius(9),
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 4)
            };
            swatch.Classes.Add("swatch");
            ToolTip.SetTip(swatch, theme.Name);
            int id = theme.Id;
            swatch.Click += (_, _) => ApplyTheme(id);
            ThemePanel.Children.Add(swatch);
        }

        Button customSwatch = new()
        {
            Content = "+",
            FontSize = 11,
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(PageKit.Alpha(current.Text, 0.08)),
            BorderBrush = new SolidColorBrush(custom ? current.Accent : PageKit.Alpha(current.Line, 0.9)),
            BorderThickness = new Thickness(custom ? 2 : 1),
            Foreground = new SolidColorBrush(custom ? current.Accent : current.Muted),
            Margin = new Thickness(0, 0, 4, 4)
        };
        customSwatch.Classes.Add("swatch");
        ToolTip.SetTip(customSwatch, "自定义皮肤壁纸");
        customSwatch.Click += (_, _) => OpenThemeGallery();
        ThemePanel.Children.Add(customSwatch);
    }

    private static string ShortThemeName(ThemeTokens theme) => theme.Id switch
    {
        2 => "灵光森林",
        5 => "星愿织梦",
        6 => "战术终端",
        7 => "档案",
        8 => "方块世界",
        9 => "幻彩牌桌",
        10 => "碧血丹心",
        11 => "长途之路",
        12 => "琴心剑魄",
        _ => theme.Name
    };

    private void ApplyTheme(int themeId)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        ThemeManager.Apply(application, themeId);
        ApplyThemeVariant(application);
        _loaded.Data.Settings["theme_id"] = themeId.ToString(CultureInfo.InvariantCulture);
        _loaded.Data.Settings["art_theme"] = themeId.ToString(CultureInfo.InvariantCulture);
        _customThemePath = null;
        _customThemeName = "自定义皮肤";
        _loaded.Data.Settings.Remove("custom_theme_path");
        _loaded.Data.Settings.Remove("custom_theme_name");
        BuildThemeSwatches();
        BuildScopeSelector();
        BuildAppFilter();
        UpdateNavColors();
        ShowPage(_current);
    }

    private static void ApplyThemeVariant(Application application)
    {
        Color background = ThemeManager.Current.Background;
        double luminance = (0.2126 * background.R
            + 0.7152 * background.G
            + 0.0722 * background.B) / 255.0;
        application.RequestedThemeVariant = luminance >= 0.55
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private void UpdateNavColors()
    {
        ThemeTokens theme = ThemeManager.Current;
        for (int i = 0; i < _navButtons.Length; i++)
        {
            bool active = i == _current;
            Color ink = active ? theme.OnAccent : PageKit.Alpha(theme.Text, 0.86);
            _navIcons[i].Foreground = new SolidColorBrush(ink);
            _navTitles[i].Foreground = new SolidColorBrush(ink);
            _navButtons[i].Classes.Set("active", active);
        }
    }

    /// <summary>供诊断/基准入口切换页面；正常交互仍走侧栏与快捷键。</summary>
    public void SelectPage(int index) => ShowPage(index);

    public int PageCount => Pages.Length;

    private void ShowPage(int index, bool refreshRime = true)
    {
        if (index < 0 || index >= Pages.Length)
        {
            return;
        }

        _current = index;
        _viewModel.ActivePageIndex = index;
        ThemeArtwork.CustomActive = !string.IsNullOrWhiteSpace(_customThemePath);
        UpdateNavColors();

        TimeScopeRange scope = _viewModel.Range;
        AppFilterScope filter = _viewModel.SelectedAppFilter;
        AppScopeSet appScope = ResolveAppScope(filter);
        AppPageContext appContext = AppPageContext.Create(
            filter,
            _appProfile,
            appScope,
            _viewModel.Categories);
        string subtitle = PageSubtitle(index, filter, appScope);
        Control page = index switch
        {
            0 => new OverviewPage(_loaded.Data, Pages[index].Title, subtitle, scope: scope, service: _service, layouts: _layouts, appContext: appContext),
            1 => new TrendPage(_loaded.Data, Pages[index].Title, subtitle, scope, _layouts, appContext),
            2 => new HoursPage(_loaded.Data, Pages[index].Title, subtitle, scope, _layouts, appContext),
            3 => new KeysPage(_loaded.Data, Pages[index].Title, subtitle, scope, _layouts, appContext),
            4 => new AppsPage(_loaded.Data, Pages[index].Title, subtitle, scope, _layouts, appContext),
            5 => new InsightsPage(_loaded.Data, Pages[index].Title, subtitle, scope, _layouts, appContext),
            6 => new TextPage(Pages[index].Title, subtitle, _viewModel.GetRimeStats(refreshRime), scope, _layouts, _loaded.Data, appContext),
            7 => new DayPage(_loaded.Data, Pages[index].Title, subtitle, scope,
                _viewModel.SelectedDay, date =>
                {
                    _viewModel.SelectedDay = date;
                    ShowPage(7);
                }, _layouts, appContext),
            8 => new HeatPage(_loaded.Data, Pages[index].Title, subtitle, scope, _layouts, appContext),
            _ => new PlaceholderPage(Pages[index].Title, subtitle)
        };
        _pageLayout.Child = null;
        _pageLayout.Child = page;
        ApplyPageScale();
        UpdateBackdrop(index);
    }

    private string PageSubtitle(int index, AppFilterScope filter, AppScopeSet appScope)
    {
        string subtitle = Pages[index].Subtitle;
        return filter.Kind switch
        {
            AppFilterKind.Category => subtitle
                + " · 工作性质："
                + (_viewModel.Categories.Find(filter.CategoryKey ?? "")?.Name ?? "未知")
                + $"（{appScope.Count} 个应用）",
            AppFilterKind.App => subtitle
                + " · 应用："
                + AppDisplayName.FriendlyName(AppIdentity.DisplayName(filter.AppKey)),
            _ => subtitle
        };
    }

    private AppScopeSet ResolveAppScope(AppFilterScope filter)
    {
        return filter.Kind switch
        {
            AppFilterKind.Category => AppScopeSet.FromCategory(
                filter.CategoryKey ?? "",
                _viewModel.Categories.GetAppKeys(filter.CategoryKey ?? "")),
            AppFilterKind.App => filter.ToScopeSet(),
            _ => AppScopeSet.All
        };
    }

    private void UpdateStatus()
    {
        bool live = _service is { } service
            && service.Status.StartsWith("实时采集", StringComparison.Ordinal);
        ThemeTokens theme = ThemeManager.Current;
        LiveDot.Fill = new SolidColorBrush(live ? theme.Green : theme.Red);
        string version = string.IsNullOrWhiteSpace(BuildInfo.Version) ? "4.2.0" : BuildInfo.Version;
        Footer.Text = $"{version} · {_viewModel.Range.Label} · 本地记录 {_loaded.Data.History.Count} 天";
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _liveTimer.Stop();
        _viewModel.TimeScopeChanged -= OnTimeScopeChanged;
        _viewModel.AppFilterChanged -= OnAppFilterChanged;
        _viewModel.Categories.Changed -= OnCategoriesChanged;
        _appProfileCts?.Cancel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ShowPage(0);
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        int index = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => -1
        };

        if (index >= 0)
        {
            ShowPage(index);
            e.Handled = true;
        }
    }
}
