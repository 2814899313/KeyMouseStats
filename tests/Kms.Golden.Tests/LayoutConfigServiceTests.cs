using System.Text.Json;
using Kms.App.Layout;
using Kms.App.Widgets;

namespace Kms.Golden.Tests;

public sealed class LayoutConfigServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "kms-layout-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MissingFile_FallsBackToDefault()
    {
        LayoutConfigService service = new(_root);
        PageLayoutConfig layout = service.GetPageLayout(LayoutPresets.OverviewPageId);

        AssertSameLayout(LayoutConfigService.GetDefault(LayoutPresets.OverviewPageId), layout);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefault()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "layouts.json"), "{ this is not json");

        LayoutConfigService service = new(_root);
        PageLayoutConfig layout = service.GetPageLayout(LayoutPresets.AppsPageId);

        AssertSameLayout(LayoutConfigService.GetDefault(LayoutPresets.AppsPageId), layout);
    }

    [Fact]
    public void UnknownModule_FallsBackToDefault()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "layouts.json"),
            """
            {
              "schemaVersion": 1,
              "pages": [
                {
                  "pageId": "overview",
                  "placements": [
                    { "moduleId": "does.not.exist", "col": 0, "row": 0, "colSpan": 3, "rowSpan": 1 }
                  ]
                }
              ]
            }
            """);

        LayoutConfigService service = new(_root);
        PageLayoutConfig layout = service.GetPageLayout(LayoutPresets.OverviewPageId);

        AssertSameLayout(LayoutConfigService.GetDefault(LayoutPresets.OverviewPageId), layout);
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsCustomLayout()
    {
        LayoutConfigService service = new(_root);
        WidgetPlacement only = new()
        {
            ModuleId = "overview.kpi.keys",
            VariantId = "sparkline",
            Col = 0,
            Row = 0,
            ColSpan = 3,
            RowSpan = 1
        };

        await service.SavePageLayoutAsync(
            LayoutPresets.OverviewPageId,
            [only],
            TestContext.Current.CancellationToken);

        LayoutConfigService reloaded = new(_root);
        PageLayoutConfig layout = reloaded.GetPageLayout(LayoutPresets.OverviewPageId);
        Assert.Equal(LayoutPresets.OverviewPageId, layout.PageId);
        WidgetPlacement placement = Assert.Single(layout.Placements);
        Assert.Equal("overview.kpi.keys", placement.ModuleId);
        Assert.Equal("sparkline", placement.VariantId);
        Assert.Equal(3, placement.ColSpan);

        reloaded.ResetPageToDefault(LayoutPresets.OverviewPageId);
        LayoutConfigService afterReset = new(_root);
        AssertSameLayout(
            LayoutConfigService.GetDefault(LayoutPresets.OverviewPageId),
            afterReset.GetPageLayout(LayoutPresets.OverviewPageId));
    }

    [Fact]
    public void JsonContext_RoundTripsPageLayout()
    {
        PageLayoutConfig source = LayoutPresets.CreateApps();
        string json = JsonSerializer.Serialize(source, LayoutJsonContext.Default.PageLayoutConfig);
        Assert.Contains("\"pageId\": \"apps\"", json, StringComparison.Ordinal);
        Assert.Contains("\"placements\"", json, StringComparison.Ordinal);
        Assert.Contains("\"moduleId\": \"apps.ranking_list\"", json, StringComparison.Ordinal);

        PageLayoutConfig? restored = JsonSerializer.Deserialize(
            json,
            LayoutJsonContext.Default.PageLayoutConfig);
        Assert.NotNull(restored);
        AssertSameLayout(source, restored);
    }

    [Fact]
    public async Task Save_DoesNotTouchStatsTxt()
    {
        Directory.CreateDirectory(_root);
        string statsPath = Path.Combine(_root, "stats.txt");
        File.WriteAllText(statsPath, "sentinel-stats-content");

        LayoutConfigService service = new(_root);
        await service.SavePageLayoutAsync(
            LayoutPresets.AppsPageId,
            LayoutPresets.CreateApps().Placements,
            TestContext.Current.CancellationToken);

        Assert.Equal("sentinel-stats-content", File.ReadAllText(statsPath));
        Assert.True(File.Exists(Path.Combine(_root, "layouts.json")));
    }

    [Fact]
    public void DefaultPresets_HaveNoOverlapAndUseRegisteredModules()
    {
        Assert.Equal(9, LayoutConfigService.DefaultPresets.Count);
        foreach ((string pageId, PageLayoutConfig preset) in LayoutConfigService.DefaultPresets)
        {
            Assert.Equal(pageId, preset.PageId);
            Assert.NotEmpty(preset.Placements);
            Assert.False(LayoutValidation.HasOverlap(preset.Placements), $"{pageId} 存在重叠");

            Assert.True(
                LayoutValidation.TryNormalize(preset, out PageLayoutConfig normalized, out string? error),
                $"{pageId} 预设校验失败：{error}");
            Assert.Equal(preset.Placements.Count, normalized.Placements.Count);

            bool[,] occupied = new bool[LayoutPresets.Columns, LayoutPresets.Rows];
            foreach (WidgetPlacement placement in preset.Placements)
            {
                Assert.True(WidgetRegistry.TryGet(placement.ModuleId, out IStatWidgetModule module),
                    $"{pageId} 使用了未注册模块 {placement.ModuleId}");
                Assert.Contains(new WidgetSpan(placement.ColSpan, placement.RowSpan),
                    module.Descriptor.SupportedSpans);
                Assert.Contains(placement.VariantId, module.Descriptor.SupportedVariants);
                Assert.InRange(placement.Col, 0, LayoutPresets.Columns - 1);
                Assert.InRange(placement.Row, 0, LayoutPresets.Rows - 1);
                Assert.InRange(placement.Col + placement.ColSpan, 1, LayoutPresets.Columns);
                Assert.InRange(placement.Row + placement.RowSpan, 1, LayoutPresets.Rows);

                for (int x = placement.Col; x < placement.Col + placement.ColSpan; x++)
                {
                    for (int y = placement.Row; y < placement.Row + placement.RowSpan; y++)
                    {
                        Assert.False(occupied[x, y], $"{pageId} 在 ({x},{y}) 重叠");
                        occupied[x, y] = true;
                    }
                }
            }
        }
    }

    [Fact]
    public void AlternatePresets_AreValid()
    {
        PageLayoutConfig[] presets =
        [
            LayoutPresets.CreateKeysCombos(),
            LayoutPresets.CreateKeysHeat(),
            LayoutPresets.CreateKeysHealth(),
            LayoutPresets.CreateCharsCharts(),
            LayoutPresets.CreateCharsPrivacy(),
            LayoutPresets.CreateDailyPeriod()
        ];

        foreach (PageLayoutConfig preset in presets)
        {
            Assert.NotEmpty(preset.Placements);
            Assert.False(LayoutValidation.HasOverlap(preset.Placements), $"{preset.PageId} 存在重叠");
            Assert.True(
                LayoutValidation.TryNormalize(preset, out PageLayoutConfig normalized, out string? error),
                $"{preset.PageId} 次级预设校验失败：{error}");
            Assert.Equal(preset.Placements.Count, normalized.Placements.Count);
        }
    }

    [Fact]
    public void InvalidVariant_NormalizesToFirstSupportedVariant()
    {
        PageLayoutConfig config = new()
        {
            PageId = "probe",
            Placements =
            [
                new WidgetPlacement
                {
                    ModuleId = "trends.metric_selector_chart",
                    VariantId = "does-not-exist",
                    Col = 0,
                    Row = 0,
                    ColSpan = 8,
                    RowSpan = 3
                }
            ]
        };

        Assert.True(LayoutValidation.TryNormalize(config, out PageLayoutConfig normalized, out string? error), error);
        Assert.Equal("line", normalized.Placements[0].VariantId);
    }

    [Fact]
    public void AllSupportedVariants_AreAcceptedByValidation()
    {
        foreach (WidgetDescriptor descriptor in WidgetRegistry.Descriptors)
        {
            foreach (string variant in descriptor.SupportedVariants)
            {
                PageLayoutConfig config = new()
                {
                    PageId = "probe",
                    Placements =
                    [
                        new WidgetPlacement
                        {
                            ModuleId = descriptor.ModuleId,
                            VariantId = variant,
                            Col = 0,
                            Row = 0,
                            ColSpan = descriptor.DefaultSpan.Columns,
                            RowSpan = descriptor.DefaultSpan.Rows
                        }
                    ]
                };

                Assert.True(
                    LayoutValidation.TryNormalize(config, out PageLayoutConfig normalized, out string? error),
                    $"{descriptor.ModuleId}/{variant}: {error}");
                Assert.Equal(variant, normalized.Placements[0].VariantId);
            }
        }
    }

    [Fact]
    public void Registry_ContainsAllModules()
    {
        string[] expected =
        [
            "overview.kpi.keys",
            "overview.kpi.clicks",
            "overview.kpi.wheel",
            "overview.kpi.distance",
            "overview.kpi.active",
            "overview.kpi.sessions",
            "overview.kpi.key_rate",
            "overview.kpi.apm",
            "overview.distribution_ring",
            "keys.top5_bars",
            "overview.insight_bar",
            "apps.summary_strip",
            "apps.ranking_list",
            "apps.category_summary",
            "apps.key_composition",
            "trends.period_summary",
            "trends.metric_selector_chart",
            "trends.detail_list",
            "trends.rhythm_profile",
            "trends.insight_notes",
            "hourly.summary_strip",
            "hourly.distribution_chart",
            "hourly.hour_detail",
            "hourly.top_hours",
            "hourly.peak_windows",
            "keys.summary_strip",
            "keys.ranking_list",
            "keys.combo_list",
            "keys.hold_chart",
            "keys.keyboard_heatmap",
            "keys.key_group_mix",
            "hardware.peripheral_wear",
            "hardware.health_summary",
            "hardware.health_notes",
            "insights.summary_strip",
            "insights.focus_pulse",
            "insights.focus_sessions",
            "insights.year_calendar",
            "insights.rhythm_profile",
            "chars.input_summary",
            "chars.char_cloud",
            "chars.word_ranking",
            "chars.chart_board",
            "chars.privacy_card",
            "daily.briefing_card",
            "daily.timeline",
            "daily.period_trend",
            "daily.app_list",
            "daily.highlights_grid",
            "daily.key_list",
            "heatmap.summary_strip",
            "heatmap.screen_canvas",
            "heatmap.zone_stats",
            "heatmap.top_cells",
            "heatmap.channel_mix",
            "trends.kpi.total",
            "trends.kpi.daily_avg",
            "trends.kpi.peak_day",
            "trends.kpi.growth",
            "hourly.kpi.peak_hour",
            "hourly.kpi.active_hours",
            "hourly.kpi.night_ratio",
            "hourly.kpi.offpeak_hour",
            "keys.kpi.total",
            "keys.kpi.top_key",
            "keys.kpi.avg_hold",
            "keys.kpi.combo_ratio",
            "apps.kpi.count",
            "apps.kpi.top_app",
            "apps.kpi.focus_share",
            "apps.kpi.switches",
            "insights.kpi.focus_time",
            "insights.kpi.sessions",
            "insights.kpi.flow_score",
            "chars.kpi.total",
            "chars.kpi.avg_len",
            "chars.kpi.distinct",
            "chars.kpi.first_rate",
            "heatmap.kpi.clicks",
            "heatmap.kpi.channel",
            "heatmap.kpi.top_zone",
            "heatmap.kpi.coverage"
        ];

        foreach (string moduleId in expected)
        {
            Assert.True(WidgetRegistry.TryGet(moduleId, out IStatWidgetModule module), moduleId);
            Assert.Equal(moduleId, module.Descriptor.ModuleId);
            Assert.NotEmpty(module.Descriptor.SupportedSpans);
            Assert.NotEmpty(module.Descriptor.SupportedVariants);
            Assert.NotNull(module.Descriptor.Factory);
        }

        Assert.Equal(expected.Length, WidgetRegistry.Count);
    }

    [Fact]
    public void PageCatalog_IsStrictlyScopedByOwnerPageAndAllowedPages()
    {
        foreach (PageId page in Enum.GetValues<PageId>())
        {
            IReadOnlyList<WidgetDescriptor> catalog = WidgetRegistry.ForPage(page);
            Assert.NotEmpty(catalog);
            foreach (WidgetDescriptor descriptor in catalog)
            {
                Assert.True(
                    descriptor.OwnerPage == page || descriptor.AllowedPages.Contains(page),
                    $"{descriptor.ModuleId} 不应出现在 {page} 的模块目录");
            }
        }

        string[] overviewModules = WidgetRegistry.ForPage(PageId.Overview)
            .Select(static descriptor => descriptor.ModuleId)
            .ToArray();
        Assert.Contains("overview.kpi.keys", overviewModules);
        Assert.Contains("hardware.peripheral_wear", overviewModules);
        Assert.Contains("hourly.distribution_chart", overviewModules);
        Assert.DoesNotContain("trends.kpi.total", overviewModules);
        Assert.DoesNotContain("chars.kpi.total", overviewModules);

        string[] trendsModules = WidgetRegistry.ForPage(PageId.Trends)
            .Select(static descriptor => descriptor.ModuleId)
            .ToArray();
        Assert.Contains("trends.kpi.total", trendsModules);
        Assert.DoesNotContain("overview.kpi.keys", trendsModules);
        Assert.DoesNotContain("hourly.distribution_chart", trendsModules);
    }

    private static void AssertSameLayout(PageLayoutConfig expected, PageLayoutConfig actual)
    {
        Assert.Equal(expected.PageId, actual.PageId);
        Assert.Equal(expected.Placements.Count, actual.Placements.Count);
        for (int i = 0; i < expected.Placements.Count; i++)
        {
            WidgetPlacement left = expected.Placements[i];
            WidgetPlacement right = actual.Placements[i];
            Assert.Equal(left.ModuleId, right.ModuleId);
            Assert.Equal(left.VariantId, right.VariantId);
            Assert.Equal(left.Col, right.Col);
            Assert.Equal(left.Row, right.Row);
            Assert.Equal(left.ColSpan, right.ColSpan);
            Assert.Equal(left.RowSpan, right.RowSpan);
        }
    }
}
