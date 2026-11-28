using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.App;
using Kms.App.Apps;
using Kms.App.Layout;
using Kms.App.Pages;
using Kms.App.Platform;
using Kms.App.Theming;
using Kms.App.Views;
using Kms.App.Widgets;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Render.Theming;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class AppFilterStep2Tests
{
    [Fact]
    public void DescriptorCapabilities_MatchPageDomain()
    {
        Assert.Equal(
            AppDataCapability.Hourly,
            WidgetRegistry.Get("hourly.distribution_chart").Descriptor.RequiredCapabilities);
        Assert.Equal(
            AppDataCapability.KeyDetail,
            WidgetRegistry.Get("keys.ranking_list").Descriptor.RequiredCapabilities);
        Assert.Equal(
            AppDataCapability.Heatmap,
            WidgetRegistry.Get("heatmap.screen_canvas").Descriptor.RequiredCapabilities);
        Assert.Equal(
            AppDataCapability.Focus,
            WidgetRegistry.Get("insights.focus_pulse").Descriptor.RequiredCapabilities);
        Assert.Equal(
            AppDataCapability.Chars,
            WidgetRegistry.Get("chars.char_cloud").Descriptor.RequiredCapabilities);
        Assert.True(WidgetRegistry.Get("hardware.peripheral_wear").Descriptor.GlobalOnly);
        Assert.True(WidgetRegistry.Get("chars.privacy_card").Descriptor.GlobalOnly);
    }

    [Fact]
    public void WidgetContext_CacheKeyIsolatedByAppFilter()
    {
        Dictionary<string, WidgetPageModel> models = new(StringComparer.Ordinal);
        StatsData data = new();
        TimeScopeRange scope = TimeScopeRange.Create(TimeScope.Today, DateTime.Today);
        WidgetContext all = Context(data, scope, AppFilterScope.All, AppDataCapabilities.AllExact, models);
        WidgetContext app = Context(
            data,
            scope,
            AppFilterScope.ByApp(@"C:\Apps\Goink.exe"),
            new AppDataCapabilities(AppDataCapability.AppTotals, AppDataCapability.All),
            models);

        CountingModel first = all.ResolveModel("probe", static () => new CountingModel("all"));
        CountingModel second = app.ResolveModel("probe", static () => new CountingModel("app"));

        Assert.NotSame(first, second);
        Assert.Equal("all", first.Name);
        Assert.Equal("app", second.Name);
        Assert.Equal(2, models.Count);
        Assert.NotEqual(all.ModelKey("probe"), app.ModelKey("probe"));
    }

    [Fact]
    public void BentoPageBuilder_GatesPendingAppCapabilityAndAllowsGlobalOnly()
    {
        GoldenHarness.EnsureAvalonia();
        StatsData data = new();
        TimeScopeRange scope = TimeScopeRange.Create(TimeScope.Today, DateTime.Today);
        WidgetContext context = Context(
            data,
            scope,
            AppFilterScope.ByApp(@"C:\Apps\Goink.exe"),
            new AppDataCapabilities(AppDataCapability.AppTotals, AppDataCapability.All),
            new Dictionary<string, WidgetPageModel>(StringComparer.Ordinal));

        PageLayoutConfig layout = new()
        {
            PageId = LayoutPresets.OverviewPageId,
            Placements =
            [
                new WidgetPlacement
                {
                    ModuleId = "overview.kpi.keys",
                    VariantId = "sparkline",
                    Col = 0,
                    Row = 0,
                    ColSpan = 3,
                    RowSpan = 1
                },
                new WidgetPlacement
                {
                    ModuleId = "hourly.summary_strip",
                    VariantId = "strip",
                    Col = 3,
                    Row = 0,
                    ColSpan = 6,
                    RowSpan = 1
                },
                new WidgetPlacement
                {
                    ModuleId = "hardware.peripheral_wear",
                    VariantId = "wear",
                    Col = 0,
                    Row = 1,
                    ColSpan = 8,
                    RowSpan = 4
                }
            ]
        };

        BentoGridPanel grid = new();
        IReadOnlyDictionary<string, BuiltWidget> built = BentoPageBuilder.Build(grid, layout, context);

        Assert.Contains("暂未采集", Texts(built["hourly.summary_strip"].View));
        Assert.DoesNotContain(
            Texts(built["hourly.summary_strip"].View),
            text => text.Contains("该维度自 v4.2.0 起按应用独立采集", StringComparison.Ordinal));
        Assert.DoesNotContain(
            Texts(built["overview.kpi.keys"].View),
            text => text.Contains("该维度自 v4.2.0 起按应用独立采集", StringComparison.Ordinal));
        Assert.True(built["hardware.peripheral_wear"].Descriptor.GlobalOnly);
        Assert.DoesNotContain(
            Texts(built["hardware.peripheral_wear"].View),
            text => text.Contains("该维度自 v4.2.0 起按应用独立采集", StringComparison.Ordinal));

        BentoGridPanel tallGrid = new();
        PageLayoutConfig tallLayout = BentoPageBuilder.Layout(
            LayoutPresets.OverviewPageId,
            BentoPageBuilder.Place("hourly.summary_strip", "strip", 0, 0, 6, 2));
        IReadOnlyDictionary<string, BuiltWidget> tall =
            BentoPageBuilder.Build(tallGrid, tallLayout, context);
        Assert.Contains(
            Texts(tall["hourly.summary_strip"].View),
            text => text.Contains("该维度自 v4.2.0 起按应用独立采集", StringComparison.Ordinal));
    }

    [Fact]
    public void Shell_AppFilterFlyout_UsesAdaptiveTilesWithoutPathText()
    {
        GoldenHarness.EnsureAvalonia();
        AppIconLoader.Enabled = false;
        if (Application.Current is { } application)
        {
            ThemeManager.Apply(application, 0);
        }

        StatsData data = GoldenFixture.LoadStats();
        DateTime fixtureDay = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(day => day.Date);
        LoadedStats loaded = new(data, "fixture", "");
        MainViewModel viewModel = new(
            loaded,
            null,
            new GlobalTimeScopeService(() => fixtureDay));
        ShellView shell = new(viewModel, GoldenLayouts.Empty);
        Window window = new()
        {
            Width = 1180,
            Height = 730,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Border? flyoutCard = null;
            StackPanel? flyoutPanel = null;
            WrapPanel? tiles = null;
            foreach (Button button in shell.GetVisualDescendants().OfType<Button>())
            {
                if (button.Flyout is Flyout flyout
                    && flyout.Content is Border border
                    && border.Child is StackPanel panel
                    && panel.Children.OfType<ScrollViewer>().FirstOrDefault()?.Content is WrapPanel wrap)
                {
                    flyoutCard = border;
                    flyoutPanel = panel;
                    tiles = wrap;
                    break;
                }
            }

            Assert.NotNull(flyoutCard);
            Assert.NotNull(flyoutPanel);
            Assert.NotNull(tiles);
            Assert.NotEmpty(tiles.Children);
            Assert.True(flyoutPanel.Width >= 399.5,
                $"应用筛选面板宽度应至少 400，实际 {flyoutPanel.Width:0.#}");

            flyoutCard.Measure(new Size(420, 600));
            flyoutCard.Arrange(new Rect(0, 0, 420, 600));
            Dispatcher.UIThread.RunJobs();

            Button[] buttons = tiles.Children.OfType<Button>().ToArray();
            Assert.NotEmpty(buttons);
            Assert.All(buttons, tile => Assert.True(tile.MinWidth >= 75.5));
            Assert.All(buttons, tile => Assert.True(tile.Width >= 75.5));
            Assert.All(buttons, tile =>
            {
                string tip = ToolTip.GetTip(tile) as string ?? "";
                Assert.False(string.IsNullOrWhiteSpace(tip));
                Assert.Contains("\n", tip, StringComparison.Ordinal);
            });
            string visible = string.Join(
                " ",
                tiles.GetVisualDescendants().OfType<TextBlock>().Select(static text => text.Text));
            Assert.DoesNotContain(":\\", visible, StringComparison.Ordinal);

            Button categoryTab = Assert.Single(
                flyoutPanel.GetVisualDescendants().OfType<Button>(),
                button => button.Content is string text && text.Contains("按工作性质", StringComparison.Ordinal));
            categoryTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            ScrollViewer categoryScroller = Assert.Single(
                flyoutPanel.Children.OfType<ScrollViewer>());
            Grid categoryGrid = Assert.IsType<Grid>(categoryScroller.Content);
            Assert.Equal(2, categoryGrid.ColumnDefinitions.Count);
            Button[] categoryCards = categoryGrid.Children.OfType<Button>().ToArray();
            Assert.True(categoryCards.Length >= 8);
            Assert.Contains(categoryCards, card => Grid.GetColumn(card) == 0);
            Assert.Contains(categoryCards, card => Grid.GetColumn(card) == 1);
            Button manage = Assert.Single(
                flyoutPanel.Children.OfType<Button>(),
                button => button.Content is string text && text.Contains("管理分类", StringComparison.Ordinal));
            Assert.True(manage.IsVisible);

            manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, viewModel.ActivePageIndex);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void GetRecentApps_FollowsActiveTimeScopeForSortAndScale()
    {
        DateTime day1 = new(2026, 11, 24);
        DateTime day2 = new(2026, 11, 25);
        StatsData data = new();
        AddApp(data, day1, @"C:\Apps\A.exe", seconds: 500, keys: 50);
        AddApp(data, day2, @"C:\Apps\A.exe", seconds: 10, keys: 1);
        AddApp(data, day1, @"C:\Apps\B.exe", seconds: 20, keys: 2);
        AddApp(data, day2, @"C:\Apps\B.exe", seconds: 200, keys: 20);
        AddApp(data, day1, @"C:\Apps\C.exe", seconds: 1000, keys: 100);

        using MainViewModel viewModel = new(
            new LoadedStats(data, "fixture", ""),
            null);
        TimeScopeRange today = new(TimeScope.Today, day2, day2);
        TimeScopeRange month = new(TimeScope.ThisMonth, new DateTime(2026, 11, 1), day2);

        IReadOnlyList<AppFilterOption> todayApps = viewModel.GetRecentApps(today, 20);
        IReadOnlyList<AppFilterOption> monthApps = viewModel.GetRecentApps(month, 20);

        Assert.Equal("B", todayApps[0].DisplayName);
        Assert.Equal(200, todayApps[0].ActiveSeconds, 6);
        Assert.DoesNotContain(todayApps, option => option.DisplayName == "C");
        Assert.Equal("C", monthApps[0].DisplayName);
        Assert.Equal(1000, monthApps[0].ActiveSeconds, 6);
        Assert.Equal("A", monthApps[1].DisplayName);
        Assert.Equal(510, monthApps[1].ActiveSeconds, 6);

        Assert.True(
            AppTileMetrics.SizeFor(todayApps[0].ActiveSeconds, todayApps[0].ActiveSeconds)
            > AppTileMetrics.SizeFor(todayApps[1].ActiveSeconds, todayApps[0].ActiveSeconds));
        Assert.Equal(
            AppTileMetrics.MaxSize,
            AppTileMetrics.SizeFor(monthApps[0].ActiveSeconds, monthApps[0].ActiveSeconds),
            6);
    }

    [Fact]
    public void CategoryUsage_UsesActiveTimeScope()
    {
        DateTime day1 = new(2026, 11, 24);
        DateTime day2 = new(2026, 11, 25);
        StatsData data = new();
        AddApp(data, day1, @"C:\Apps\Code.exe", seconds: 500, keys: 50);
        AddApp(data, day2, @"C:\Apps\Code.exe", seconds: 10, keys: 1);
        AddApp(data, day1, @"C:\Apps\WinWord.exe", seconds: 20, keys: 2);
        AddApp(data, day2, @"C:\Apps\WinWord.exe", seconds: 200, keys: 20);

        using MainViewModel viewModel = new(
            new LoadedStats(data, "fixture", ""),
            null);
        TimeScopeRange today = new(TimeScope.Today, day2, day2);
        TimeScopeRange month = new(TimeScope.ThisMonth, new DateTime(2026, 11, 1), day2);

        AppCategoryUsage todayCode = viewModel.Categories.GetUsage(
            AppCategoryBuiltins.Coding,
            today.Start,
            today.End);
        AppCategoryUsage monthCode = viewModel.Categories.GetUsage(
            AppCategoryBuiltins.Coding,
            month.Start,
            month.End);
        AppCategoryUsage todayOffice = viewModel.Categories.GetUsage(
            AppCategoryBuiltins.Office,
            today.Start,
            today.End);

        Assert.Equal(1, todayCode.AppCount);
        Assert.Equal(10, todayCode.ActiveSeconds, 6);
        Assert.Equal(1, monthCode.AppCount);
        Assert.Equal(510, monthCode.ActiveSeconds, 6);
        Assert.Equal(1, todayOffice.AppCount);
        Assert.Equal(200, todayOffice.ActiveSeconds, 6);
    }

    [Fact]
    public void Overview_ByCategory_PreservesAllElevenPlacements()
    {
        DateTime date = new(2026, 11, 24);
        StatsData data = new();
        AddApp(data, date, @"C:\Apps\Code.exe", seconds: 120, keys: 20);
        AppCategoryService categories = new(null, data, loadFromDisk: false);
        AppFilterScope filter = AppFilterScope.ByCategory(AppCategoryBuiltins.Coding);
        AppScopeSet scope = AppScopeSet.FromCategory(
            AppCategoryBuiltins.Coding,
            categories.GetAppKeys(AppCategoryBuiltins.Coding));
        AppPageContext appContext = AppPageContext.Create(filter, null, scope, categories);
        WidgetContext context = new()
        {
            Data = data,
            Scope = new TimeScopeRange(TimeScope.Today, date, date),
            AppFilter = filter,
            Capabilities = appContext.Capabilities,
            AppPage = appContext,
            VariantId = "",
            Models = new Dictionary<string, WidgetPageModel>(StringComparer.Ordinal)
        };
        PageLayoutConfig layout = LayoutPresets.CreateOverview();
        BentoGridPanel grid = new();
        IReadOnlyDictionary<string, BuiltWidget> built =
            BentoPageBuilder.Build(grid, layout, context);

        Assert.Equal(layout.Placements.Count, grid.Children.Count);
        Assert.Equal(layout.Placements.Count, built.Count);
        foreach (WidgetPlacement placement in layout.Placements)
        {
            BentoCardFrame frame = built[placement.ModuleId].Frame;
            Assert.Equal(placement.Col, BentoGridPanel.GetCol(frame));
            Assert.Equal(placement.Row, BentoGridPanel.GetRow(frame));
            Assert.Equal(placement.ColSpan, BentoGridPanel.GetColSpan(frame));
            Assert.Equal(placement.RowSpan, BentoGridPanel.GetRowSpan(frame));
            Assert.True(frame.IsVisible);
        }
    }

    private static void AddApp(
        StatsData data,
        DateTime day,
        string path,
        double seconds,
        long keys)
    {
        DayRecord record = data.GetOrCreate(day);
        AppUsage app = new()
        {
            ProcessPath = path,
            Title = "title",
            Keys = keys,
            Clicks = keys / 2,
            Wheel = 1,
            ActiveSeconds = seconds
        };
        record.Apps[app.Id] = app;
    }

    [Fact]
    public void GetRecentApps_MergesSameDisplayNameAndPrefersExistingPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "kms-app-group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string oldPath = Path.Combine(directory, "old", "ChatGPT.exe");
        string newPath = Path.Combine(directory, "new", "ChatGPT.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllBytes(newPath, []);

        try
        {
            StatsData data = new();
            DayRecord day = data.GetOrCreate(new DateTime(2026, 11, 24));
            AppUsage oldApp = new()
            {
                ProcessPath = oldPath,
                Title = "old",
                Keys = 10,
                ActiveSeconds = 60
            };
            AppUsage newApp = new()
            {
                ProcessPath = newPath,
                Title = "new",
                Keys = 20,
                ActiveSeconds = 120
            };
            day.Apps[oldApp.Id] = oldApp;
            day.Apps[newApp.Id] = newApp;

            using MainViewModel viewModel = new(
                new LoadedStats(data, "fixture", ""),
                null);
            AppFilterOption option = Assert.Single(
                viewModel.GetRecentApps(),
                item => string.Equals(
                    item.DisplayName,
                    "ChatGPT",
                    StringComparison.OrdinalIgnoreCase));

            Assert.Equal(2, option.AppKeys?.Count);
            Assert.Equal(180, option.ActiveSeconds, 6);
            Assert.Equal(30, option.Keys);
            Assert.Equal(newPath, option.ProcessPath);
            Assert.Equal(2, AppFilterScope
                .ByAppGroup(option.ProcessPath, option.AppKeys!)
                .ToScopeSet()
                .Count);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void AppScopeData_Filter_KeepsMemoryTotalsWhenBucketsArePending()
    {
        StatsData data = new();
        DayRecord day = data.GetOrCreate(new DateTime(2026, 11, 24));
        AppUsage app = new()
        {
            ProcessPath = @"C:\Apps\Goink.exe",
            Title = "document.gk",
            Keys = 99,
            Clicks = 7,
            Wheel = 1,
            ActiveSeconds = 8
        };
        day.Apps[app.Id] = app;

        AppFilterScope filter = AppFilterScope.ByApp(app.ProcessPath);
        AppProfileSnapshot profile = new()
        {
            Filter = filter,
            Scope = AppScopeSet.FromApp(app.ProcessPath),
            Start = day.Date,
            End = day.Date,
            Capabilities = new AppDataCapabilities(
                AppDataCapability.None,
                AppDataCapability.All),
            Totals = new AppTotalsSnapshot
            {
                Keys = 99,
                Clicks = 7,
                Wheel = 1,
                ActiveSeconds = 8,
                AppCount = 1
            }
        };

        StatsData filtered = AppScopeData.Filter(data, profile.Scope, profile);

        DayRecord only = Assert.Single(filtered.History).Value;
        Assert.Equal(99, only.Keys);
        Assert.Equal(7, only.Clicks);
        Assert.Equal(8, only.ActiveSeconds, 6);
        Assert.True(profile.Capabilities.IsPending(AppDataCapability.Hourly));
    }

    [Fact]
    public void AppPageContext_ScopeBeforeProfile_KeepsAppTotalsExact()
    {
        AppFilterScope filter = AppFilterScope.ByCategory(AppCategoryBuiltins.Coding);
        AppScopeSet scope = AppScopeSet.FromCategory(
            AppCategoryBuiltins.Coding,
            [@"C:\Apps\Code.exe"]);

        AppPageContext context = AppPageContext.Create(filter, null, scope);

        Assert.True(context.Capabilities.IsExact(AppDataCapability.AppTotals));
        Assert.False(context.Capabilities.IsPending(AppDataCapability.AppTotals));
        Assert.True(context.Capabilities.IsPending(AppDataCapability.Hourly));
    }

    [Fact]
    public void AppPageContext_ProfileTotals_UpgradesAppTotalsToExact()
    {
        DateTime day = new(2026, 11, 24);
        AppFilterScope filter = AppFilterScope.ByApp(@"C:\Apps\Goink.exe");
        AppProfileSnapshot profile = new()
        {
            Filter = filter,
            Scope = AppScopeSet.FromApp(@"C:\Apps\Goink.exe"),
            Start = day,
            End = day,
            Capabilities = new AppDataCapabilities(
                AppDataCapability.None,
                AppDataCapability.All),
            Totals = new AppTotalsSnapshot
            {
                Keys = 99,
                Clicks = 7,
                Wheel = 1,
                ActiveSeconds = 8,
                AppCount = 1
            }
        };

        AppPageContext context = AppPageContext.Create(filter, profile);

        Assert.True(context.Capabilities.IsExact(AppDataCapability.AppTotals));
        Assert.False(context.Capabilities.IsPending(AppDataCapability.AppTotals));
        Assert.True(context.Capabilities.IsPending(AppDataCapability.Hourly));
        Assert.True(context.Capabilities.IsPending(AppDataCapability.KeyDetail));
    }

    [Fact]
    public void BentoPageBuilder_PendingRowSpan1_UsesCompactSingleLineBadge()
    {
        GoldenHarness.EnsureAvalonia();
        StatsData data = new();
        TimeScopeRange scope = TimeScopeRange.Create(TimeScope.Today, DateTime.Today);
        WidgetContext context = Context(
            data,
            scope,
            AppFilterScope.ByApp(@"C:\Apps\Goink.exe"),
            new AppDataCapabilities(AppDataCapability.None, AppDataCapability.All),
            new Dictionary<string, WidgetPageModel>(StringComparer.Ordinal));
        PageLayoutConfig layout = BentoPageBuilder.Layout(
            LayoutPresets.OverviewPageId,
            BentoPageBuilder.Place("hourly.summary_strip", "strip", 0, 0, 3, 1));

        BentoGridPanel grid = new();
        IReadOnlyDictionary<string, BuiltWidget> built =
            BentoPageBuilder.Build(grid, layout, context);
        WidgetView view = built["hourly.summary_strip"].View;

        string[] texts = Texts(view);
        Assert.Contains("暂未采集", texts);
        Assert.DoesNotContain(texts, text => text.Contains("按应用独立采集", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text.StartsWith("筛选：", StringComparison.Ordinal));

        Border card = Assert.IsType<Border>(view.Content);
        Grid host = new()
        {
            Width = 300,
            Height = 42,
            Children = { card }
        };
        host.Measure(new Size(300, 42));
        host.Arrange(new Rect(0, 0, 300, 42));

        Assert.True(card.Bounds.Height <= 42.5);
        Assert.True(card.Bounds.Width <= 300.5);
        StackPanel body = Assert.IsType<StackPanel>(card.Child);
        TextBlock label = Assert.IsType<TextBlock>(Assert.Single(body.Children));
        Assert.True(label.Bounds.Height >= label.DesiredSize.Height - 0.5,
            $"紧凑徽标高度 {label.Bounds.Height:0.#} 小于期望 {label.DesiredSize.Height:0.#}");
    }

    [Fact]
    public void WidgetLiveBus_IsFrozenForNonAllAppScope()
    {
        WidgetLiveBus bus = new();
        ProbeWidget probe = new();
        bus.Register(probe);
        bus.IsFrozen = true;
        bus.Publish(new WidgetLiveSnapshot(1, 1, 1, 1, 1, 1, 1, 1));
        Assert.Equal(0, probe.Updates);

        bus.IsFrozen = false;
        bus.Publish(new WidgetLiveSnapshot(1, 1, 1, 1, 1, 1, 1, 1));
        Assert.Equal(1, probe.Updates);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void AppsPage_SingleAppShowsWindowTitlesAndFitsViewport(double dpi)
    {
        GoldenHarness.EnsureAvalonia();
        AppIconLoader.Enabled = false;
        if (Application.Current is { } application)
        {
            ThemeManager.Apply(application, 0);
        }

        StatsData data = new();
        DayRecord day = data.GetOrCreate(new DateTime(2026, 11, 24));
        AppUsage app = new()
        {
            ProcessPath = @"C:\Apps\Goink.exe",
            Title = "document.gk",
            Keys = 100,
            Clicks = 8,
            Wheel = 2,
            ActiveSeconds = 120
        };
        day.Apps[app.Id] = app;

        AppFilterScope filter = AppFilterScope.ByApp(app.ProcessPath);
        AppProfileSnapshot profile = new()
        {
            Filter = filter,
            Scope = AppScopeSet.FromApp(app.ProcessPath),
            Start = new DateTime(2026, 11, 24),
            End = new DateTime(2026, 11, 24),
            Capabilities = AppDataCapabilities.AllExact,
            Totals = new AppTotalsSnapshot
            {
                Keys = 100,
                Clicks = 8,
                Wheel = 2,
                ActiveSeconds = 120,
                AppCount = 1
            }
        };
        AppPageContext appContext = AppPageContext.Create(filter, profile);
        LayoutConfigService layouts = new(Path.Combine(
            Path.GetTempPath(),
            "kms-step2-layout-" + Guid.NewGuid().ToString("N")));
        AppsPage page = new(
            data,
            "应用统计",
            "前台窗口、APM 与时长占比",
            new TimeScopeRange(TimeScope.ThisMonth, new DateTime(2026, 11, 1), new DateTime(2026, 11, 24)),
            layouts,
            appContext);

        Window window = new()
        {
            Width = 928,
            Height = 730,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Content = page
        };
        try
        {
            window.SetRenderScaling(dpi / 96.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(page.GetVisualDescendants(), visual => visual is ScrollViewer);
            Assert.True(page.Bounds.Height <= 730.5);
            Assert.Contains(
                page.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "document.gk");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static WidgetContext Context(
        StatsData data,
        TimeScopeRange scope,
        AppFilterScope filter,
        AppDataCapabilities capabilities,
        Dictionary<string, WidgetPageModel> models) => new()
    {
        Data = data,
        Scope = scope,
        AppFilter = filter,
        Capabilities = capabilities,
        VariantId = "",
        Models = models
    };

    private static string[] Texts(WidgetView view)
    {
        if (view.Content is not Border border || border.Child is not StackPanel panel)
        {
            return [];
        }

        return panel.Children.OfType<TextBlock>().Select(static text => text.Text ?? "").ToArray();
    }

    private sealed class CountingModel(string name) : WidgetPageModel
    {
        public string Name { get; } = name;
    }

    private sealed class ProbeWidget : ILiveWidget
    {
        public int Updates { get; private set; }

        public void UpdateLive(in WidgetLiveSnapshot snapshot) => Updates++;
    }
}
