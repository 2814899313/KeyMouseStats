using Kms.App.Layout;
using Kms.App.Widgets;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class LayoutScenarioTests
{
    [Fact]
    public void OverviewScenarios_AreValidAndCoverAllModes()
    {
        IReadOnlyList<LayoutScenario> scenarios = LayoutScenarios.ForPage(LayoutPresets.OverviewPageId);
        Assert.True(scenarios.Count >= 3);

        foreach (LayoutScenario scenario in scenarios)
        {
            PageLayoutConfig layout = scenario.Create();
            Assert.True(LayoutValidation.TryNormalize(layout, out PageLayoutConfig normalized, out string? error),
                $"{scenario.Id}: {error}");
            Assert.False(LayoutValidation.HasOverlap(normalized.Placements), scenario.Id);
            foreach (WidgetPlacement placement in normalized.Placements)
            {
                Assert.InRange(placement.Col, 0, LayoutPresets.Columns - 1);
                Assert.InRange(placement.Row, 0, LayoutPresets.Rows - 1);
                Assert.InRange(placement.Col + placement.ColSpan, 1, LayoutPresets.Columns);
                Assert.InRange(placement.Row + placement.RowSpan, 1, LayoutPresets.Rows);
            }
        }
    }

    [Fact]
    public void MinimalPreset_UsesMinimalKpiAndLargeAreaChart()
    {
        PageLayoutConfig layout = LayoutPresets.CreateOverviewMinimal();
        Assert.Contains(layout.Placements, placement =>
            placement.ModuleId == "overview.kpi.keys" && placement.VariantId == "minimal");
        Assert.Contains(layout.Placements, placement =>
            placement.ModuleId == "overview.kpi.active" && placement.VariantId == "minimal");
        Assert.Contains(layout.Placements, placement =>
            placement.ModuleId == "hourly.distribution_chart"
            && placement.VariantId == "area"
            && placement.ColSpan == 12
            && placement.RowSpan == 3);
    }

    [Fact]
    public void GeekPreset_CombinesFocusWearRankingAndRealtimeRate()
    {
        PageLayoutConfig layout = LayoutPresets.CreateOverviewGeek();
        string[] required =
        [
            "overview.kpi.key_rate",
            "insights.focus_pulse",
            "hardware.peripheral_wear",
            "keys.ranking_list"
        ];
        foreach (string moduleId in required)
        {
            Assert.Contains(layout.Placements, placement => placement.ModuleId == moduleId);
        }
    }

    [Fact]
    public void EveryPageHasAtLeastOneValidClassicScenario()
    {
        string[] pages =
        [
            LayoutPresets.OverviewPageId,
            LayoutPresets.AppsPageId,
            LayoutPresets.TrendsPageId,
            LayoutPresets.HourlyPageId,
            LayoutPresets.KeysPageId,
            LayoutPresets.InsightsPageId,
            LayoutPresets.CharsPageId,
            LayoutPresets.DailyPageId,
            LayoutPresets.HeatmapPageId
        ];

        foreach (string page in pages)
        {
            IReadOnlyList<LayoutScenario> scenarios = LayoutScenarios.ForPage(page);
            Assert.NotEmpty(scenarios);
            foreach (LayoutScenario scenario in scenarios)
            {
                Assert.True(
                    LayoutValidation.TryNormalize(scenario.Create(), out _, out string? error),
                    $"{page}/{scenario.Id}: {error}");
            }
        }
    }
}
