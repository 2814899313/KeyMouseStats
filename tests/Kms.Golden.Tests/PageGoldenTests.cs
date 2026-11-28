using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.App.Platform;
using Kms.App.Theming;
using Kms.App.Widgets;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Text;
using Kms.Core.Time;
using Kms.Render.Theming;
using Kms.Storage.Text;

namespace Kms.Golden.Tests;

/// <summary>
/// 9 页主面板的整页基线（96 DPI，928×730，对应 1180 主窗去掉侧栏后的内容区）。
/// 基线缺失或设置 KMS_UPDATE_GOLDEN=1 时写入，人工确认后再作为回归基线。
/// </summary>
[Collection("page-golden")]
public sealed class PageGoldenTests
{
    private const int Width = 928;
    private const int Height = 730;
    private const int CompactHeight = 632;

    [Theory]
    [InlineData("page-overview")]
    [InlineData("page-trend")]
    [InlineData("page-hours")]
    [InlineData("page-keys")]
    [InlineData("page-apps")]
    [InlineData("page-insights")]
    [InlineData("page-text")]
    [InlineData("page-day")]
    [InlineData("page-heat")]
    public void Pages_MatchBaseline(string name)
    {
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        TimeScope? scope = name == "page-text"
            ? null
            : name is "page-hours" or "page-keys" or "page-apps" or "page-insights" or "page-heat"
                ? TimeScope.ThisWeek
                : TimeScope.Today;
        Control page = CreatePage(name, data, day, scope);
        GoldenHarness.AssertMatches(name, page, Width, Height, hostInWindow: true);
    }

    [Fact]
    public void Overview_Week_MatchesBaseline()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        Control page = new OverviewPage(data, "总览", "今天与最近 7 天的整体节奏",
            scope: TimeScopeRange.Create(TimeScope.ThisWeek, day), layouts: GoldenLayouts.Empty);
        GoldenHarness.AssertMatches("page-overview-week", page, Width, Height, hostInWindow: true);
    }

    [Fact]
    public void Trend_Week_MatchesBaseline()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        Control page = new TrendPage(data, "趋势", "选择区间与基线，查看变化",
            TimeScopeRange.Create(TimeScope.ThisWeek, day), GoldenLayouts.Empty);
        GoldenHarness.AssertMatches("page-trend-week", page, Width, Height, hostInWindow: true);
    }

    [Fact]
    public void Day_Week_MatchesBaseline()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        Control page = new DayPage(data, "日报", "单日摘要与可导出长图",
            TimeScopeRange.Create(TimeScope.ThisWeek, day), layouts: GoldenLayouts.Empty);
        GoldenHarness.AssertMatches("page-day-week", page, Width, Height, hostInWindow: true);
    }

    [Fact]
    public void Day_Month_MatchesBaseline()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        Control page = new DayPage(data, "日报", "单日摘要与可导出长图",
            TimeScopeRange.Create(TimeScope.ThisMonth, day), layouts: GoldenLayouts.Empty);
        GoldenHarness.AssertMatches("page-day-month", page, Width, Height, hostInWindow: true);
    }

    [Fact]
    public void Heat_Screen2_MatchesBaseline()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        HeatPage page = new(data, "点击热区",
            "手指最常落在屏幕的哪一块？位置按 32 像素网格记录，不截屏",
            TimeScopeRange.Create(TimeScope.ThisWeek, day),
            GoldenLayouts.Empty);
        page.SelectScreen(1);
        GoldenHarness.AssertMatches("page-heat-screen2", page, Width, Height, hostInWindow: true);
    }

    [Fact]
    public void Apps_Tall_MatchesBaseline()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        Control page = CreatePage("page-apps", data, day, TimeScope.ThisWeek);
        GoldenHarness.AssertMatches("page-apps-tall", page, 1500, 950, hostInWindow: true);
    }

    [Fact]
    public void Pages_Tall_RenderSmoke()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        string[] pages =
        [
            "page-overview", "page-trend", "page-hours", "page-keys", "page-apps",
            "page-insights", "page-text", "page-day", "page-heat"
        ];
        foreach (string name in pages)
        {
            Control page = CreatePage(name, data, day, TimeScope.ThisWeek);
            byte[] png = GoldenHarness.RenderHosted(page, 1500, 950);
            Assert.True(png.Length > 10_000, $"{name} tall render is empty");
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), $"{name}-tall.png"), png);
        }
    }

    [Theory]
    [InlineData(96, "page-hours-96")]
    [InlineData(144, "page-hours-144")]
    [InlineData(192, "page-hours-192")]
    public void Hours_ThreeDpi_MatchesBaseline(double dpi, string name)
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        HoursPage page = new(data, "小时分布", "24 小时内的输入与活跃时长",
            TimeScopeRange.Create(TimeScope.ThisWeek, day),
            GoldenLayouts.Empty);
        GoldenHarness.AssertMatches(name, page, Width, Height, dpi, hostInWindow: true);
    }

    [Fact]
    public void Text_Compact_MatchesBaseline()
    {
        PrepareTheme();
        RimeLogStats stats = LoadRime();
        TextPage page = new("字符内容", "Rime 上屏文字与词频", stats, RimeScope(stats), GoldenLayouts.Empty);
        GoldenHarness.AssertMatches("page-text-compact", page, Width, 650, hostInWindow: true);
    }

    [Fact]
    public void Keys_Compact_MatchesBaseline()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        KeysPage page = new(data, "按键分析", "排行、按住时长、组合键与配列",
            TimeScopeRange.Create(TimeScope.ThisWeek, day),
            GoldenLayouts.Empty);
        GoldenHarness.AssertMatches("page-keys-compact", page, Width, 650, hostInWindow: true);
    }

    [Theory]
    [InlineData("page-overview")]
    [InlineData("page-trend")]
    [InlineData("page-hours")]
    [InlineData("page-keys")]
    [InlineData("page-apps")]
    [InlineData("page-insights")]
    [InlineData("page-text")]
    [InlineData("page-day")]
    [InlineData("page-heat")]
    public void Pages_CompactFitWithoutVerticalScroll(string name)
    {
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        GoldenHarness.EnsureAvalonia();

        foreach ((int width, int height) in new[]
                 {
                     (Width, CompactHeight),
                     (720, CompactHeight),
                     (720, 712),
                     (Width, 712),
                     (Width, 880),
                     (Width, 900),
                     (Width, 950),
                     (1500, 950)
                 })
        {
            foreach (TimeScope scope in new[] { TimeScope.Today, TimeScope.ThisWeek, TimeScope.ThisMonth })
            {
                Control page = CreatePage(name, data, day, scope);
                Window window = new()
                {
                    Width = width,
                    Height = height,
                    CanResize = false,
                    WindowDecorations = WindowDecorations.None,
                    Content = page
                };

                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.DoesNotContain(Descendants(page), visual => visual is ScrollViewer);
                    Assert.True(page.Bounds.Height <= height + 0.5,
                        $"{name}/{scope}@{width}×{height} 高度 {page.Bounds.Height:0.#} 超出 {height}");
                    foreach (Visual visual in Descendants(page).OfType<Visual>())
                    {
                        if (visual is not Layoutable { IsVisible: true } layoutable)
                        {
                            continue;
                        }

                        Point? origin = visual.TranslatePoint(new Point(0, 0), page);
                        if (origin is null)
                        {
                            continue;
                        }

                        double bottom = origin.Value.Y + layoutable.Bounds.Height;
                        Assert.True(bottom <= page.Bounds.Height + 0.5,
                            $"{name}/{scope}@{width}×{height} 的 {Describe(visual)} 底部 {bottom:0.#} 超出视口 {page.Bounds.Height:0.#}");
                    }
                }
                finally
                {
                    window.Content = null;
                    window.Close();
                }
            }
        }
    }

    [Theory]
    [InlineData("page-overview", 120)]
    [InlineData("page-overview", 144)]
    [InlineData("page-overview", 192)]
    [InlineData("page-apps", 120)]
    [InlineData("page-apps", 144)]
    [InlineData("page-apps", 192)]
    [InlineData("page-trend", 120)]
    [InlineData("page-trend", 144)]
    [InlineData("page-trend", 192)]
    [InlineData("page-hours", 120)]
    [InlineData("page-hours", 144)]
    [InlineData("page-hours", 192)]
    [InlineData("page-keys", 120)]
    [InlineData("page-keys", 144)]
    [InlineData("page-keys", 192)]
    [InlineData("page-insights", 120)]
    [InlineData("page-insights", 144)]
    [InlineData("page-insights", 192)]
    [InlineData("page-text", 120)]
    [InlineData("page-text", 144)]
    [InlineData("page-text", 192)]
    [InlineData("page-day", 120)]
    [InlineData("page-day", 144)]
    [InlineData("page-day", 192)]
    [InlineData("page-heat", 120)]
    [InlineData("page-heat", 144)]
    [InlineData("page-heat", 192)]
    public void BentoPages_ThreeDpi_FitViewport(string name, double dpi)
    {
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        Control page = CreatePage(name, data, day, TimeScope.ThisWeek);
        GoldenHarness.EnsureAvalonia();
        Window window = new()
        {
            Width = Width,
            Height = Height,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Content = page
        };

        try
        {
            window.SetRenderScaling(dpi / 96.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants(), visual => visual is BentoGridPanel);
            Assert.DoesNotContain(page.GetVisualDescendants(), visual => visual is ScrollViewer);
            Assert.True(page.Bounds.Height <= Height + 0.5,
                $"{name}@{dpi} DPI 高度 {page.Bounds.Height:0.#} 超出 {Height}");
            foreach (Visual visual in page.GetVisualDescendants())
            {
                if (visual is not Layoutable { IsVisible: true } layoutable)
                {
                    continue;
                }

                Point? origin = visual.TranslatePoint(new Point(0, 0), page);
                if (origin is null)
                {
                    continue;
                }

                double bottom = origin.Value.Y + layoutable.Bounds.Height;
                Assert.True(bottom <= page.Bounds.Height + 0.5,
                    $"{name}@{dpi} DPI 的 {Describe(visual)} 底部 {bottom:0.#} 超出视口 {page.Bounds.Height:0.#}");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void BentoGridPanel_MeasuresAttachedPlacements()
    {
        BentoGridPanel panel = new() { Spacing = 0 };
        Border topLeft = Cell(0, 0, 6, 3);
        Border topRight = Cell(6, 0, 6, 3);
        Border bottom = Cell(0, 3, 12, 3);
        panel.Children.Add(topLeft);
        panel.Children.Add(topRight);
        panel.Children.Add(bottom);

        panel.Measure(new Size(120, 60));
        panel.Arrange(new Rect(0, 0, 120, 60));

        AssertRect(topLeft.Bounds, new Rect(0, 0, 60, 30));
        AssertRect(topRight.Bounds, new Rect(60, 0, 60, 30));
        AssertRect(bottom.Bounds, new Rect(0, 30, 120, 30));
    }

    [Fact]
    public void BentoGridPanel_ClampsOversizedChildInsideViewport()
    {
        BentoGridPanel panel = new() { Spacing = 0 };
        Border child = Cell(30, 30, 99, 99);
        panel.Children.Add(child);

        panel.Measure(new Size(120, 60));
        panel.Arrange(new Rect(0, 0, 120, 60));

        AssertRect(child.Bounds, new Rect(110, 50, 10, 10));
    }

    private static Border Cell(int col, int row, int colSpan, int rowSpan)
    {
        Border border = new();
        BentoGridPanel.SetCol(border, col);
        BentoGridPanel.SetRow(border, row);
        BentoGridPanel.SetColSpan(border, colSpan);
        BentoGridPanel.SetRowSpan(border, rowSpan);
        return border;
    }

    private static void AssertRect(Rect actual, Rect expected)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Width, actual.Width, 6);
        Assert.Equal(expected.Height, actual.Height, 6);
    }

    [Fact]
    public void HardwareWear_CanBuildWithoutKeysPageModel()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        Assert.True(WidgetRegistry.TryGet("hardware.peripheral_wear", out IStatWidgetModule module));
        WidgetView view = module.CreateView(new WidgetContext
        {
            Data = data,
            Scope = TimeScopeRange.Create(TimeScope.ThisWeek, day),
            VariantId = "wear"
        });
        Assert.NotNull(view.Content);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void Apps_CategoryScope_FitsViewportWithoutScroll(double dpi)
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        AppCategoryService categories = new(null, data, loadFromDisk: false);
        IReadOnlySet<string> keys = categories.GetAppKeys(AppCategoryBuiltins.Ai);
        AppPageContext context = AppPageContext.Create(
            AppFilterScope.ByCategory(AppCategoryBuiltins.Ai),
            null,
            AppScopeSet.FromCategory(AppCategoryBuiltins.Ai, keys),
            categories);
        AppsPage page = new(
            data,
            "应用统计",
            "前台窗口、APM 与时长占比",
            TimeScopeRange.Create(TimeScope.ThisWeek, day),
            GoldenLayouts.Empty,
            context);

        AssertFits(page, Width, Height, dpi);
    }

    [Fact]
    public void SecondaryPageModes_FitViewportWithoutScroll()
    {
        PrepareTheme();
        StatsData data = LoadFixture();
        DateTime day = FixtureDay(data);
        TimeScopeRange scope = TimeScopeRange.Create(TimeScope.ThisWeek, day);

        foreach ((int width, int height) in new[] { (Width, Height), (Width, CompactHeight) })
        {
            KeysPage keys = new(data, "按键排行", "排行、按住时长、组合键与配列", scope,
                GoldenLayouts.Empty);
            keys.SelectView(0);
            AssertFits(keys, width, height);
            keys.SelectView(1);
            AssertFits(keys, width, height);
            keys.SelectView(2);
            AssertFits(keys, width, height);
            keys.SelectMode(1);
            AssertFits(keys, width, height);

            TextPage text = CreateTextPage(TimeScope.ThisMonth);
            text.SelectMode(0);
            AssertFits(text, width, height);
            text.SelectMode(1);
            AssertFits(text, width, height);
            text.SelectMode(2);
            AssertFits(text, width, height);
            text.SelectMode(0);
            text.ToggleRevealed();
            AssertFits(text, width, height);
        }
    }

    private static void AssertFits(Control page, int width, int height, double dpi = 96)
    {
        Window window = new()
        {
            Width = width,
            Height = height,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Content = page
        };

        try
        {
            window.SetRenderScaling(dpi / 96.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants(), visual => visual is BentoGridPanel);
            Assert.DoesNotContain(page.GetVisualDescendants(), visual => visual is ScrollViewer);
            foreach (Visual visual in page.GetVisualDescendants())
            {
                if (visual is not Layoutable { IsVisible: true } layoutable)
                {
                    continue;
                }

                Point? origin = visual.TranslatePoint(new Point(0, 0), page);
                if (origin is null)
                {
                    continue;
                }

                double bottom = origin.Value.Y + layoutable.Bounds.Height;
                Assert.True(bottom <= page.Bounds.Height + 0.5,
                    $"{page.GetType().Name} 的 {Describe(visual)} 底部 {bottom:0.#} 超出视口 {page.Bounds.Height:0.#}");
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static Control CreatePage(string name, StatsData data, DateTime day, TimeScope? scope = null)
    {
        PrepareTheme();
        TimeScopeRange today = TimeScopeRange.Create(scope ?? TimeScope.Today, day);
        TimeScopeRange week = TimeScopeRange.Create(TimeScope.ThisWeek, day);
        TimeScopeRange month = TimeScopeRange.Create(TimeScope.ThisMonth, day);
        TimeScopeRange chosen = scope switch
        {
            TimeScope.Today => today,
            TimeScope.ThisWeek => week,
            TimeScope.ThisMonth => month,
            _ => today
        };
        return name switch
        {
            "page-overview" => new OverviewPage(data, "总览", "今天与最近 7 天的整体节奏",
                scope: chosen, layouts: GoldenLayouts.Empty),
            "page-trend" => new TrendPage(data, "趋势", "选择区间与基线，查看变化", chosen,
                GoldenLayouts.Empty),
            "page-hours" => new HoursPage(data, "时段分布", "24 小时内的输入与活跃时长", chosen,
                GoldenLayouts.Empty),
            "page-keys" => new KeysPage(data, "按键排行", "排行、按住时长、组合键与配列", chosen,
                GoldenLayouts.Empty),
            "page-apps" => new AppsPage(data, "应用统计", "前台窗口、APM 与时长占比", chosen,
                GoldenLayouts.Empty, AppsContext(data)),
            "page-insights" => new InsightsPage(data, "使用洞察", "专注段、上下文切换与时光机", chosen,
                GoldenLayouts.Empty),
            "page-text" => CreateTextPage(scope),
            "page-day" => new DayPage(data, "日报", "单日摘要与可导出长图", chosen, day,
                layouts: GoldenLayouts.Empty),
            _ => new HeatPage(data, "点击热区",
                "手指最常落在屏幕的哪一块？位置按 32 像素网格记录，不截屏", chosen,
                GoldenLayouts.Empty)
        };
    }

    private static AppPageContext AppsContext(StatsData data)
    {
        AppCategoryService categories = new(null, data, loadFromDisk: false);
        return AppPageContext.Create(
            AppFilterScope.All,
            null,
            AppScopeSet.All,
            categories);
    }

    private static TextPage CreateTextPage(TimeScope? scope = null)
    {
        RimeLogStats stats = LoadRime();
        DateTime first = stats.FirstDay ?? DateTime.Today;
        DateTime last = stats.LastDay ?? first;
        TimeScopeRange range = scope.HasValue
            ? TimeScopeRange.Create(scope.Value, last)
            : new TimeScopeRange(TimeScope.ThisMonth, first, last);
        return new TextPage("字符内容", "Rime 上屏文字与词频", stats, range, GoldenLayouts.Empty);
    }

    private static TimeScopeRange RimeScope(RimeLogStats stats)
    {
        DateTime first = stats.FirstDay ?? DateTime.Today;
        DateTime last = stats.LastDay ?? first;
        return new TimeScopeRange(TimeScope.ThisMonth, first, last);
    }

    private static RimeLogStats LoadRime()
    {
        string path = Path.Combine(GoldenFixture.RepoRoot(), "tests", "Fixtures", "rime-log-sample.csv");
        return RimeLog.Load(path, completeOnly: true);
    }

    private static IEnumerable<Visual> Descendants(Visual root) =>
        root.GetVisualDescendants();

    private static string Describe(Visual visual)
    {
        List<string> chain = [];
        Visual? current = visual;
        for (int depth = 0; current is not null && depth < 6; depth++)
        {
            string text = current is TextBlock textBlock ? $"“{textBlock.Text}”" : "";
            chain.Add($"{current.GetType().Name}{text} {current.Bounds.Width:0.#}×{current.Bounds.Height:0.#}");
            current = current.GetVisualParent();
        }

        return string.Join(" <- ", chain);
    }

    private static DateTime FixtureDay(StatsData data) =>
        data.History.Count == 0 ? DateTime.Today : data.History.Values.Max(day => day.Date);

    private static void PrepareTheme()
    {
        GoldenHarness.EnsureAvalonia();
        AppIconLoader.Enabled = false;
        DisplayLayout.Override =
        [
            new DisplayRect(0, 0, 2560, 1600),
            new DisplayRect(2560, 0, 2560, 1600)
        ];
        if (Application.Current is { } application)
        {
            ThemeManager.Apply(application, 0);
        }

        ThemeArtwork.CustomActive = false;
    }

    private static StatsData LoadFixture() => GoldenFixture.LoadStats();
}

[CollectionDefinition("page-golden", DisableParallelization = true)]
public sealed class PageGoldenCollection;
