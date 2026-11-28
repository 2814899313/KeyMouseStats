namespace Kms.App.Layout;

/// <summary>
/// 4.1.1 九页官方预设。坐标是 12×6 栅格中的逻辑位置：
/// 总览 = 8 张 3×1 KPI + 6×3 分布/排行 + 12×1 洞察；
/// 应用统计 = 12×1 指标条 + 8×5 排行 + 右侧 4×2 / 4×3 两张卡片；
/// 其余七页同样按 12×6 均分栅格编排，并提供按键/字符/日报的次级模式预设。
/// </summary>
public static class LayoutPresets
{
    public const string OverviewPageId = PageIds.Overview;
    public const string AppsPageId = PageIds.Apps;
    public const string TrendsPageId = PageIds.Trends;
    public const string HourlyPageId = PageIds.Hourly;
    public const string KeysPageId = PageIds.Keys;
    public const string InsightsPageId = PageIds.Insights;
    public const string CharsPageId = PageIds.Chars;
    public const string DailyPageId = PageIds.Daily;
    public const string HeatmapPageId = PageIds.Heatmap;

    public const int Columns = 12;
    public const int Rows = 6;

    public static PageLayoutConfig CreateOverview() => new()
    {
        PageId = OverviewPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "overview.kpi.keys", VariantId = "sparkline", Col = 0, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.clicks", VariantId = "sparkline", Col = 3, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.wheel", VariantId = "sparkline", Col = 6, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.distance", VariantId = "sparkline", Col = 9, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.active", VariantId = "sparkline", Col = 0, Row = 1, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.sessions", VariantId = "sparkline", Col = 3, Row = 1, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.key_rate", VariantId = "sparkline", Col = 6, Row = 1, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.apm", VariantId = "sparkline", Col = 9, Row = 1, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.distribution_ring", VariantId = "ring", Col = 0, Row = 2, ColSpan = 6, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "keys.top5_bars", VariantId = "bars", Col = 6, Row = 2, ColSpan = 6, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "overview.insight_bar", VariantId = "inline", Col = 0, Row = 5, ColSpan = 12, RowSpan = 1 }
        ]
    };

    /// <summary>极简沉浸：两张超大极简数字卡 + 12×3 面积主图 + 一条洞察。</summary>
    public static PageLayoutConfig CreateOverviewMinimal() => new()
    {
        PageId = OverviewPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "overview.kpi.keys", VariantId = "minimal", Col = 0, Row = 0, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "overview.kpi.active", VariantId = "minimal", Col = 6, Row = 0, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "hourly.distribution_chart", VariantId = "area", Col = 0, Row = 2, ColSpan = 12, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "overview.insight_bar", VariantId = "inline", Col = 0, Row = 5, ColSpan = 12, RowSpan = 1 }
        ]
    };

    /// <summary>
    /// 极客高密度监控：实时速率、APM、击键/点击、心流脉冲、外设寿命与键帽排行同屏。
    /// </summary>
    public static PageLayoutConfig CreateOverviewGeek() => new()
    {
        PageId = OverviewPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "overview.kpi.key_rate", VariantId = "minimal", Col = 0, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.apm", VariantId = "minimal", Col = 3, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.keys", VariantId = "sparkline", Col = 6, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "overview.kpi.clicks", VariantId = "sparkline", Col = 9, Row = 0, ColSpan = 3, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "insights.focus_pulse", VariantId = "timeline", Col = 0, Row = 1, ColSpan = 12, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "hardware.peripheral_wear", VariantId = "wear", Col = 0, Row = 4, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "keys.ranking_list", VariantId = "keycaps", Col = 6, Row = 4, ColSpan = 6, RowSpan = 2 }
        ]
    };

    public static PageLayoutConfig CreateApps() => new()
    {
        PageId = AppsPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "apps.summary_strip", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "apps.ranking_list", VariantId = "list", Col = 0, Row = 1, ColSpan = 8, RowSpan = 5 },
            new WidgetPlacement { ModuleId = "apps.category_summary", VariantId = "ring", Col = 8, Row = 1, ColSpan = 4, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "apps.key_composition", VariantId = "stacked", Col = 8, Row = 3, ColSpan = 4, RowSpan = 3 }
        ]
    };

    public static PageLayoutConfig CreateTrends() => new()
    {
        PageId = TrendsPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "trends.period_summary", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "trends.metric_selector_chart", VariantId = "line", Col = 0, Row = 1, ColSpan = 8, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "trends.detail_list", VariantId = "list", Col = 8, Row = 1, ColSpan = 4, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "trends.rhythm_profile", VariantId = "weekday", Col = 0, Row = 4, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "trends.insight_notes", VariantId = "list", Col = 6, Row = 4, ColSpan = 6, RowSpan = 2 }
        ]
    };

    public static PageLayoutConfig CreateHourly() => new()
    {
        PageId = HourlyPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "hourly.summary_strip", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "hourly.distribution_chart", VariantId = "columns", Col = 0, Row = 1, ColSpan = 8, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "hourly.hour_detail", VariantId = "ring", Col = 8, Row = 1, ColSpan = 4, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "hourly.top_hours", VariantId = "list", Col = 0, Row = 4, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "hourly.peak_windows", VariantId = "strip", Col = 6, Row = 4, ColSpan = 6, RowSpan = 2 }
        ]
    };

    public static PageLayoutConfig CreateKeys() => new()
    {
        PageId = KeysPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "keys.summary_strip", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "keys.ranking_list", VariantId = "bars", Col = 0, Row = 1, ColSpan = 8, RowSpan = 5 },
            new WidgetPlacement { ModuleId = "keys.hold_chart", VariantId = "histogram", Col = 8, Row = 1, ColSpan = 4, RowSpan = 5 }
        ]
    };

    public static PageLayoutConfig CreateInsights() => new()
    {
        PageId = InsightsPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "insights.summary_strip", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "insights.focus_pulse", VariantId = "timeline", Col = 0, Row = 1, ColSpan = 8, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "insights.focus_sessions", VariantId = "list", Col = 8, Row = 1, ColSpan = 4, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "insights.year_calendar", VariantId = "matrix", Col = 0, Row = 4, ColSpan = 7, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "insights.rhythm_profile", VariantId = "list", Col = 7, Row = 4, ColSpan = 5, RowSpan = 2 }
        ]
    };

    public static PageLayoutConfig CreateChars() => new()
    {
        PageId = CharsPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "chars.input_summary", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "chars.char_cloud", VariantId = "cloud", Col = 0, Row = 1, ColSpan = 7, RowSpan = 5 },
            new WidgetPlacement { ModuleId = "chars.word_ranking", VariantId = "list", Col = 7, Row = 1, ColSpan = 5, RowSpan = 5 }
        ]
    };

    public static PageLayoutConfig CreateDaily() => new()
    {
        PageId = DailyPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "daily.briefing_card", VariantId = "hero", Col = 0, Row = 0, ColSpan = 12, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.timeline", VariantId = "day", Col = 0, Row = 2, ColSpan = 7, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.app_list", VariantId = "list", Col = 7, Row = 2, ColSpan = 5, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.highlights_grid", VariantId = "grid", Col = 0, Row = 4, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.key_list", VariantId = "list", Col = 6, Row = 4, ColSpan = 6, RowSpan = 2 }
        ]
    };

    public static PageLayoutConfig CreateHeatmap() => new()
    {
        PageId = HeatmapPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "heatmap.summary_strip", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "heatmap.screen_canvas", VariantId = "grid", Col = 0, Row = 1, ColSpan = 8, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "heatmap.zone_stats", VariantId = "zones", Col = 8, Row = 1, ColSpan = 4, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "heatmap.top_cells", VariantId = "list", Col = 0, Row = 4, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "heatmap.channel_mix", VariantId = "legend", Col = 6, Row = 4, ColSpan = 6, RowSpan = 2 }
        ]
    };

    public static PageLayoutConfig CreateKeysCombos() => new()
    {
        PageId = KeysPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "keys.summary_strip", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "keys.combo_list", VariantId = "list", Col = 0, Row = 1, ColSpan = 7, RowSpan = 5 },
            new WidgetPlacement { ModuleId = "keys.hold_chart", VariantId = "histogram", Col = 7, Row = 1, ColSpan = 5, RowSpan = 5 }
        ]
    };

    public static PageLayoutConfig CreateKeysHeat() => new()
    {
        PageId = KeysPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "keys.summary_strip", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "keys.keyboard_heatmap", VariantId = "heatmap", Col = 0, Row = 1, ColSpan = 8, RowSpan = 5 },
            new WidgetPlacement { ModuleId = "keys.key_group_mix", VariantId = "mix", Col = 8, Row = 1, ColSpan = 4, RowSpan = 5 }
        ]
    };

    public static PageLayoutConfig CreateKeysHealth() => new()
    {
        PageId = KeysPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "hardware.peripheral_wear", VariantId = "wear", Col = 0, Row = 0, ColSpan = 8, RowSpan = 6 },
            new WidgetPlacement { ModuleId = "hardware.health_summary", VariantId = "summary", Col = 8, Row = 0, ColSpan = 4, RowSpan = 3 },
            new WidgetPlacement { ModuleId = "hardware.health_notes", VariantId = "notes", Col = 8, Row = 3, ColSpan = 4, RowSpan = 3 }
        ]
    };

    public static PageLayoutConfig CreateCharsCharts() => new()
    {
        PageId = CharsPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "chars.input_summary", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "chars.chart_board", VariantId = "board", Col = 0, Row = 1, ColSpan = 12, RowSpan = 5 }
        ]
    };

    public static PageLayoutConfig CreateCharsPrivacy() => new()
    {
        PageId = CharsPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "chars.input_summary", VariantId = "strip", Col = 0, Row = 0, ColSpan = 12, RowSpan = 1 },
            new WidgetPlacement { ModuleId = "chars.privacy_card", VariantId = "privacy", Col = 0, Row = 1, ColSpan = 12, RowSpan = 5 }
        ]
    };

    public static PageLayoutConfig CreateDailyPeriod() => new()
    {
        PageId = DailyPageId,
        Placements =
        [
            new WidgetPlacement { ModuleId = "daily.briefing_card", VariantId = "hero", Col = 0, Row = 0, ColSpan = 12, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.period_trend", VariantId = "line", Col = 0, Row = 2, ColSpan = 7, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.app_list", VariantId = "list", Col = 7, Row = 2, ColSpan = 5, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.highlights_grid", VariantId = "grid", Col = 0, Row = 4, ColSpan = 6, RowSpan = 2 },
            new WidgetPlacement { ModuleId = "daily.key_list", VariantId = "list", Col = 6, Row = 4, ColSpan = 6, RowSpan = 2 }
        ]
    };
}
