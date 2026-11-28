using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kms.App.Layout;
using Kms.App.Pages;
using Kms.App.Widgets;
using Kms.Core.Model;
using Kms.Core.Text;
using Kms.Core.Time;
using Kms.Render.Theming;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class WidgetVariantRenderTests
{
    [Theory]
    [InlineData("overview", "overview.distribution_ring", "ring", 6, 3)]
    [InlineData("overview", "overview.distribution_ring", "bars", 6, 3)]
    [InlineData("overview", "overview.distribution_ring", "treemap", 6, 3)]
    [InlineData("overview", "overview.kpi.keys", "sparkline", 3, 1)]
    [InlineData("overview", "overview.kpi.keys", "minimal", 3, 1)]
    [InlineData("overview", "keys.top5_bars", "bars", 6, 3)]
    [InlineData("overview", "keys.top5_bars", "keycaps", 6, 3)]
    [InlineData("apps", "apps.category_summary", "ring", 4, 2)]
    [InlineData("apps", "apps.category_summary", "bars", 4, 2)]
    [InlineData("apps", "apps.category_summary", "treemap", 4, 2)]
    [InlineData("hourly", "hourly.distribution_chart", "columns", 8, 3)]
    [InlineData("hourly", "hourly.distribution_chart", "area", 8, 3)]
    [InlineData("hourly", "hourly.distribution_chart", "polar", 8, 3)]
    [InlineData("trend", "trends.metric_selector_chart", "line", 8, 3)]
    [InlineData("trend", "trends.metric_selector_chart", "impulse", 8, 3)]
    [InlineData("trend", "trends.metric_selector_chart", "cumulative", 8, 3)]
    [InlineData("keys", "keys.ranking_list", "bars", 8, 5)]
    [InlineData("keys", "keys.ranking_list", "keycaps", 8, 5)]
    [InlineData("apps", "apps.ranking_list", "list", 6, 2)]
    [InlineData("apps", "apps.ranking_list", "list", 3, 2)]
    [InlineData("apps", "apps.category_summary", "ring", 6, 2)]
    [InlineData("apps", "apps.category_summary", "bars", 3, 2)]
    [InlineData("hourly", "hourly.distribution_chart", "columns", 6, 2)]
    [InlineData("hourly", "hourly.distribution_chart", "area", 3, 2)]
    [InlineData("trend", "trends.metric_selector_chart", "line", 6, 2)]
    [InlineData("trend", "trends.metric_selector_chart", "impulse", 3, 2)]
    [InlineData("keys", "keys.ranking_list", "bars", 6, 2)]
    [InlineData("keys", "keys.ranking_list", "list", 3, 2)]
    [InlineData("keys", "keys.combo_list", "list", 6, 2)]
    [InlineData("keys", "keys.combo_list", "list", 3, 2)]
    [InlineData("heat", "heatmap.zone_stats", "zones", 6, 2)]
    [InlineData("heat", "heatmap.zone_stats", "zones", 3, 2)]
    public async Task Variants_RenderAtStandardAnd150PercentWithoutOverflow(
        string pageId,
        string moduleId,
        string variant,
        int colSpan,
        int rowSpan)
    {
        GoldenHarness.EnsureAvalonia();
        string root = Path.Combine(Path.GetTempPath(), "kms-variant-" + Guid.NewGuid().ToString("N"));
        try
        {
            LayoutConfigService layouts = new(root);
            await layouts.SavePageLayoutAsync(
                pageId,
                [
                    new WidgetPlacement
                    {
                        ModuleId = moduleId,
                        VariantId = variant,
                        Col = 0,
                        Row = 0,
                        ColSpan = colSpan,
                        RowSpan = rowSpan
                    }
                ],
                TestContext.Current.CancellationToken);

            foreach (double dpi in new[] { 96d, 144d })
            {
                Control page = CreatePage(pageId, layouts);
                Window window = new()
                {
                    Width = 928,
                    Height = 730,
                    CanResize = false,
                    WindowDecorations = WindowDecorations.None,
                    Background = new Avalonia.Media.SolidColorBrush(ThemeManager.Current.Background),
                    Content = page
                };

                try
                {
                    window.SetRenderScaling(dpi / 96.0);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Contains(page.GetVisualDescendants(), visual => visual is BentoGridPanel);
                    Assert.DoesNotContain(page.GetVisualDescendants(), visual => visual is ScrollViewer);
                    BentoGridPanel grid = page.GetVisualDescendants().OfType<BentoGridPanel>().Single();
                    foreach (Visual visual in grid.GetVisualDescendants())
                    {
                        if (visual is not Layoutable { IsVisible: true } layoutable)
                        {
                            continue;
                        }

                        Point? origin = visual.TranslatePoint(new Point(0, 0), grid);
                        if (origin is null)
                        {
                            continue;
                        }

                        Assert.True(origin.Value.X >= -0.5 && origin.Value.Y >= -0.5,
                            $"{moduleId}/{variant}@{dpi} 的 {visual.GetType().Name} 超出栅格左上边界");
                        Assert.True(origin.Value.X + layoutable.Bounds.Width <= grid.Bounds.Width + 0.5,
                            $"{moduleId}/{variant}@{dpi} 的 {visual.GetType().Name} 横向超出栅格");
                        Assert.True(origin.Value.Y + layoutable.Bounds.Height <= grid.Bounds.Height + 0.5,
                            $"{moduleId}/{variant}@{dpi} 的 {visual.GetType().Name} 纵向超出栅格");
                    }
                    Assert.True(page.Bounds.Height <= 730.5,
                        $"{moduleId}/{variant}@{dpi} 页面高度 {page.Bounds.Height:0.#} 超出视口");
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
                            $"{moduleId}/{variant}@{dpi} 的 {visual.GetType().Name} 底部 {bottom:0.#} 超出视口");
                    }

                    var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0);
                    if (Environment.GetEnvironmentVariable("KMS_DUMP_VARIANTS") == "1")
                    {
                        using MemoryStream stream = new();
                        frame.Save(stream, PngBitmapEncoderOptions.Default);
                        File.WriteAllBytes(
                            Path.Combine(Path.GetTempPath(),
                                $"kms-variant-{moduleId.Replace('.', '_')}-{variant}-{(int)dpi}.png"),
                            stream.ToArray());
                    }
                }
                finally
                {
                    window.Content = null;
                    window.Close();
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task MicroKpiCards_RenderAt96And144DpiWithoutOverflow()
    {
        GoldenHarness.EnsureAvalonia();
        PageId[] pages =
        [
            PageId.Trends, PageId.Hourly, PageId.Keys, PageId.Apps,
            PageId.Insights, PageId.Chars, PageId.Heatmap
        ];

        foreach (PageId page in pages)
        {
            WidgetDescriptor[] micros = WidgetRegistry.ForPage(page)
                .Where(descriptor => descriptor.ModuleId.Contains(".kpi.", StringComparison.Ordinal))
                .Take(4)
                .ToArray();
            Assert.NotEmpty(micros);

            foreach (WidgetSpan span in new[] { new WidgetSpan(3, 1), new WidgetSpan(3, 2) })
            {
                string root = Path.Combine(Path.GetTempPath(), "kms-micro-" + Guid.NewGuid().ToString("N"));
                try
                {
                    LayoutConfigService layouts = new(root);
                    await layouts.SavePageLayoutAsync(
                        page.ToId(),
                        micros.Select((descriptor, index) => new WidgetPlacement
                        {
                            ModuleId = descriptor.ModuleId,
                            VariantId = descriptor.SupportedVariants[0],
                            Col = index * 3,
                            Row = 0,
                            ColSpan = span.Columns,
                            RowSpan = span.Rows
                        }).ToArray(),
                        TestContext.Current.CancellationToken);

                    foreach (double dpi in new[] { 96d, 144d })
                    {
                        Control host = CreatePage(page.ToId(), layouts);
                        Window window = new()
                        {
                            Width = 928,
                            Height = 730,
                            CanResize = false,
                            WindowDecorations = WindowDecorations.None,
                            Background = new Avalonia.Media.SolidColorBrush(ThemeManager.Current.Background),
                            Content = host
                        };

                        try
                        {
                            window.SetRenderScaling(dpi / 96.0);
                            window.Show();
                            Dispatcher.UIThread.RunJobs();
                            Assert.Contains(host.GetVisualDescendants(), visual => visual is BentoGridPanel);
                            Assert.DoesNotContain(host.GetVisualDescendants(), visual => visual is ScrollViewer);
                            BentoGridPanel grid = host.GetVisualDescendants().OfType<BentoGridPanel>().Single();
                            foreach (Visual visual in grid.GetVisualDescendants())
                            {
                                if (visual is not Layoutable { IsVisible: true } layoutable)
                                {
                                    continue;
                                }

                                Point? origin = visual.TranslatePoint(new Point(0, 0), grid);
                                if (origin is null)
                                {
                                    continue;
                                }

                                Assert.True(origin.Value.X >= -0.5 && origin.Value.Y >= -0.5,
                                    $"{page}/{span}@{dpi} 的 {visual.GetType().Name} 超出栅格左上边界");
                                Assert.True(origin.Value.X + layoutable.Bounds.Width <= grid.Bounds.Width + 0.5,
                                    $"{page}/{span}@{dpi} 的 {visual.GetType().Name} 横向超出栅格");
                                Assert.True(origin.Value.Y + layoutable.Bounds.Height <= grid.Bounds.Height + 0.5,
                                    $"{page}/{span}@{dpi} 的 {visual.GetType().Name} 纵向超出栅格");
                            }

                            Assert.True(host.Bounds.Height <= 730.5,
                                $"{page}/{span}@{dpi} 页面高度 {host.Bounds.Height:0.#} 超出视口");
                        }
                        finally
                        {
                            window.Content = null;
                            window.Close();
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(root))
                        {
                            Directory.Delete(root, recursive: true);
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
    }

    [Fact]
    public void VariantSelector_IsLockedByDefaultUntilEditMode()
    {
        BentoCardFrame frame = new();
        frame.AttachVariantSelector(["ring", "bars", "treemap"], "ring", _ => { });
        Assert.False(frame.ShowVariantSelector);
        Assert.Equal("ring", frame.CurrentVariant);
        frame.ShowVariantSelector = true;
        Assert.True(frame.ShowVariantSelector);
    }

    [Fact]
    public void VariantSelector_InvokesCallbackWithoutRecreatingPage()
    {
        BentoCardFrame frame = new()
        {
            Title = "测试",
            Body = new Border()
        };
        string? selected = null;
        frame.AttachVariantSelector(["ring", "bars"], "ring", value => selected = value);
        frame.ShowVariantSelector = true;
        Window window = new()
        {
            Width = 240,
            Height = 120,
            WindowDecorations = WindowDecorations.None,
            Content = frame
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ComboBox combo = frame.GetVisualDescendants().OfType<ComboBox>().Single();
            combo.SelectedItem = "bars";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("bars", selected);
            Assert.Equal("bars", frame.CurrentVariant);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static Control CreatePage(string pageId, LayoutConfigService layouts)
    {
        StatsData data = GoldenFixture.LoadStats();
        DateTime day = data.History.Count == 0
            ? DateTime.Today
            : data.History.Values.Max(record => record.Date);
        TimeScopeRange scope = TimeScopeRange.Create(TimeScope.ThisWeek, day);
        return pageId switch
        {
            "overview" => new OverviewPage(data, "总览", "今天与最近 7 天的整体节奏",
                scope: scope, layouts: layouts),
            "apps" => new AppsPage(data, "应用统计", "前台窗口、APM 与时长占比", scope, layouts),
            "hourly" => new HoursPage(data, "时段分布", "24 小时内的输入与活跃时长", scope, layouts),
            "trend" => new TrendPage(data, "趋势", "选择区间与基线，查看变化", scope, layouts),
            "keys" => new KeysPage(data, "按键排行", "排行、按住时长、组合键与配列", scope, layouts),
            "insights" => new InsightsPage(data, "使用洞察", "专注段、上下文切换与时光机", scope, layouts),
            "chars" => new TextPage("字符内容", "Rime 上屏文字与词频", new RimeLogStats(), scope, layouts, data),
            "daily" => new DayPage(data, "日报", "单日摘要与可导出长图", scope, layouts: layouts),
            "heat" => new HeatPage(data, "点击热区", "手指最常落在屏幕的哪一块？", scope, layouts),
            _ => throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "未知页面")
        };
    }
}
