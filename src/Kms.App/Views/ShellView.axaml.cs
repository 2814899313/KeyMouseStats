using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.App.Pages;
using Kms.App.Theming;
using Kms.Core.Model;
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
        ("◎", "点击热区", "点击位置与三通道分布")
    ];

    private readonly InputService? _service;
    private readonly LoadedStats _loaded;
    private readonly Button[] _navButtons = new Button[Pages.Length];
    private readonly TextBlock[] _navIcons = new TextBlock[Pages.Length];
    private readonly TextBlock[] _navTitles = new TextBlock[Pages.Length];
    private readonly DispatcherTimer _liveTimer;
    private Bitmap? _backdrop;
    private string? _customThemePath;
    private string _customThemeName = "自定义皮肤";
    private int _current;
    private DateTime? _selectedDay;

    public ShellView() : this((InputService?)null)
    {
    }

    public ShellView(InputService? service) : this(service?.Loaded ?? StatsLoader.Load(), service)
    {
    }

    internal ShellView(LoadedStats loaded, InputService? service = null)
    {
        InitializeComponent();

        _service = service;
        _loaded = loaded;

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

        BuildNavigation();
        BuildThemeSwatches();
        ShowPage(0);
        UpdateStatus();

        KeyDown += OnKeyDown;
        PointerPressed += (_, _) => Focus();

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += (_, _) => UpdateStatus();
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
        _loaded.Data.Settings["theme_id"] = themeId.ToString(CultureInfo.InvariantCulture);
        _loaded.Data.Settings["art_theme"] = themeId.ToString(CultureInfo.InvariantCulture);
        _customThemePath = null;
        _customThemeName = "自定义皮肤";
        _loaded.Data.Settings.Remove("custom_theme_path");
        _loaded.Data.Settings.Remove("custom_theme_name");
        BuildThemeSwatches();
        UpdateNavColors();
        ShowPage(_current);
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

    private void ShowPage(int index)
    {
        if (index < 0 || index >= Pages.Length)
        {
            return;
        }

        _current = index;
        ThemeArtwork.CustomActive = !string.IsNullOrWhiteSpace(_customThemePath);
        UpdateNavColors();

        PageHost.Content = index switch
        {
            0 => new OverviewPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle, service: _service),
            1 => new TrendPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle),
            2 => new HoursPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle),
            3 => new KeysPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle),
            4 => new AppsPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle),
            5 => new InsightsPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle),
            6 => new TextPage(Pages[index].Title, Pages[index].Subtitle),
            7 => new DayPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle, _selectedDay, date =>
            {
                _selectedDay = date;
                ShowPage(7);
            }),
            8 => new HeatPage(_loaded.Data, Pages[index].Title, Pages[index].Subtitle),
            _ => new PlaceholderPage(Pages[index].Title, Pages[index].Subtitle)
        };
        UpdateBackdrop(index);
    }

    private void UpdateStatus()
    {
        bool live = _service is { } service
            && service.Status.StartsWith("实时采集", StringComparison.Ordinal);
        ThemeTokens theme = ThemeManager.Current;
        LiveDot.Fill = new SolidColorBrush(live ? theme.Green : theme.Red);
        string version = typeof(App).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0] ?? "4.0.0";
        Footer.Text = $"{version} · 本地记录 {_loaded.Data.History.Count} 天";
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _liveTimer.Stop();
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
