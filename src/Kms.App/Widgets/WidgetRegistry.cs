using Kms.App.Layout;
using Kms.Core.Apps;

namespace Kms.App.Widgets;

/// <summary>
/// 静态模块注册表。所有模块在静态构造函数中显式登记，
/// 不使用反射/程序集扫描，NativeAOT 与 Trim 下行为稳定。
/// </summary>
public static class WidgetRegistry
{
    private static readonly Dictionary<string, IStatWidgetModule> Modules = new(StringComparer.Ordinal);
    private static readonly List<WidgetDescriptor> Ordered = [];

    static WidgetRegistry()
    {
        Register(new DelegateWidgetModule(Kpi("overview.kpi.keys", "击键", KpiKind.Keys)));
        Register(new DelegateWidgetModule(Kpi("overview.kpi.clicks", "点击", KpiKind.Clicks)));
        Register(new DelegateWidgetModule(Kpi("overview.kpi.wheel", "滚轮", KpiKind.Wheel)));
        Register(new DelegateWidgetModule(Kpi("overview.kpi.distance", "鼠标路程", KpiKind.Distance)));
        Register(new DelegateWidgetModule(Kpi("overview.kpi.active", "有效使用时长", KpiKind.Active)));
        Register(new DelegateWidgetModule(Kpi("overview.kpi.sessions", "连续使用段", KpiKind.Sessions)));
        Register(new DelegateWidgetModule(Kpi("overview.kpi.key_rate", "击键速率", KpiKind.KeyRate)));
        Register(new DelegateWidgetModule(Kpi("overview.kpi.apm", "操作速率", KpiKind.Apm)));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "overview.distribution_ring",
            Title = "按键分布",
            Category = "overview",
            OwnerPage = PageId.Overview,
            DefaultSpan = new WidgetSpan(6, 3),
            SupportedSpans = [new(6, 3), new(6, 2), new(3, 2), new(12, 2), new(12, 3)],
            SupportedVariants = ["ring", "bars", "treemap"],
            Factory = OverviewWidgets.CreateDistribution
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "keys.top5_bars",
            Title = "最常按键",
            Category = "keys",
            OwnerPage = PageId.Keys,
            AllowedPages = new HashSet<PageId> { PageId.Overview },
            DefaultSpan = new WidgetSpan(6, 3),
            SupportedSpans = [new(6, 3), new(6, 2), new(12, 2), new(12, 3)],
            SupportedVariants = ["bars", "keycaps", "list"],
            Factory = OverviewWidgets.CreateTopKeys
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "overview.insight_bar",
            Title = "区间洞察",
            Category = "overview",
            OwnerPage = PageId.Overview,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["inline"],
            Factory = OverviewWidgets.CreateInsight
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "trends.period_summary",
            Title = "区间摘要",
            Category = "trends",
            OwnerPage = PageId.Trends,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = TrendWidgets.CreateSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "trends.metric_selector_chart",
            Title = "趋势曲线",
            Category = "trends",
            OwnerPage = PageId.Trends,
            DefaultSpan = new WidgetSpan(8, 3),
            SupportedSpans = [new(8, 3), new(8, 4), new(12, 3), new(6, 2), new(3, 2)],
            SupportedVariants = ["line", "impulse", "cumulative"],
            Factory = TrendWidgets.CreateChart
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "trends.detail_list",
            Title = "逐时明细",
            Category = "trends",
            OwnerPage = PageId.Trends,
            DefaultSpan = new WidgetSpan(4, 3),
            SupportedSpans = [new(4, 3), new(4, 2)],
            SupportedVariants = ["list"],
            Factory = TrendWidgets.CreateDetail
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "trends.rhythm_profile",
            Title = "节奏",
            Category = "trends",
            OwnerPage = PageId.Trends,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 2)],
            SupportedVariants = ["weekday"],
            Factory = TrendWidgets.CreateRhythm
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "trends.insight_notes",
            Title = "区间洞察",
            Category = "trends",
            OwnerPage = PageId.Trends,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 2)],
            SupportedVariants = ["list"],
            Factory = TrendWidgets.CreateInsight
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hourly.summary_strip",
            Title = "小时摘要",
            Category = "hourly",
            OwnerPage = PageId.Hourly,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = HourlyWidgets.CreateSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hourly.distribution_chart",
            Title = "24 小时柱状",
            Category = "hourly",
            OwnerPage = PageId.Hourly,
            AllowedPages = new HashSet<PageId> { PageId.Overview },
            DefaultSpan = new WidgetSpan(8, 3),
            SupportedSpans = [new(8, 3), new(8, 4), new(12, 3), new(6, 2), new(3, 2)],
            SupportedVariants = ["columns", "area", "polar"],
            Factory = HourlyWidgets.CreateChart
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hourly.hour_detail",
            Title = "小时详情",
            Category = "hourly",
            OwnerPage = PageId.Hourly,
            DefaultSpan = new WidgetSpan(4, 3),
            SupportedSpans = [new(4, 3), new(4, 2)],
            SupportedVariants = ["ring"],
            Factory = HourlyWidgets.CreateDetail
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hourly.top_hours",
            Title = "Top 小时",
            Category = "hourly",
            OwnerPage = PageId.Hourly,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 2)],
            SupportedVariants = ["list"],
            Factory = HourlyWidgets.CreateTopHours
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hourly.peak_windows",
            Title = "黄金时段",
            Category = "hourly",
            OwnerPage = PageId.Hourly,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = HourlyWidgets.CreateRhythm
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "keys.summary_strip",
            Title = "按键摘要",
            Category = "keys",
            OwnerPage = PageId.Keys,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = KeysWidgets.CreateSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "keys.ranking_list",
            Title = "按键排行",
            Category = "keys",
            OwnerPage = PageId.Keys,
            AllowedPages = new HashSet<PageId> { PageId.Overview },
            DefaultSpan = new WidgetSpan(8, 5),
            SupportedSpans = [new(8, 5), new(8, 4), new(12, 5), new(6, 2), new(3, 2)],
            SupportedVariants = ["bars", "keycaps", "list"],
            Factory = KeysWidgets.CreateRanking
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "keys.combo_list",
            Title = "组合键 Top 10",
            Category = "keys",
            OwnerPage = PageId.Keys,
            DefaultSpan = new WidgetSpan(7, 5),
            SupportedSpans = [new(7, 5), new(7, 4), new(12, 5), new(6, 2), new(3, 2)],
            SupportedVariants = ["list"],
            Factory = KeysWidgets.CreateCombos
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "keys.hold_chart",
            Title = "按住时长",
            Category = "keys",
            OwnerPage = PageId.Keys,
            DefaultSpan = new WidgetSpan(4, 5),
            SupportedSpans = [new(4, 5), new(5, 5), new(12, 5)],
            SupportedVariants = ["histogram"],
            Factory = KeysWidgets.CreateHolds
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "keys.keyboard_heatmap",
            Title = "全键热力",
            Category = "keys",
            OwnerPage = PageId.Keys,
            DefaultSpan = new WidgetSpan(8, 5),
            SupportedSpans = [new(8, 5), new(8, 4), new(12, 5)],
            SupportedVariants = ["heatmap"],
            Factory = KeysWidgets.CreateKeyboard
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "keys.key_group_mix",
            Title = "键位构成",
            Category = "keys",
            OwnerPage = PageId.Keys,
            DefaultSpan = new WidgetSpan(4, 5),
            SupportedSpans = [new(4, 5), new(4, 4), new(12, 5)],
            SupportedVariants = ["mix"],
            Factory = KeysWidgets.CreateGroups
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hardware.peripheral_wear",
            Title = "设备磨损",
            Category = "hardware",
            OwnerPage = PageId.Keys,
            AllowedPages = new HashSet<PageId> { PageId.Overview },
            DefaultSpan = new WidgetSpan(8, 6),
            SupportedSpans = [new(8, 6), new(8, 5), new(12, 6), new(6, 2)],
            SupportedVariants = ["wear"],
            Factory = KeysWidgets.CreateWear
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hardware.health_summary",
            Title = "健康摘要",
            Category = "hardware",
            OwnerPage = PageId.Keys,
            DefaultSpan = new WidgetSpan(4, 3),
            SupportedSpans = [new(4, 3), new(4, 2)],
            SupportedVariants = ["summary"],
            Factory = KeysWidgets.CreateHealthSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "hardware.health_notes",
            Title = "估算说明",
            Category = "hardware",
            OwnerPage = PageId.Keys,
            DefaultSpan = new WidgetSpan(4, 3),
            SupportedSpans = [new(4, 3), new(4, 2)],
            SupportedVariants = ["notes"],
            Factory = KeysWidgets.CreateHealthNotes
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "insights.summary_strip",
            Title = "心流摘要",
            Category = "insights",
            OwnerPage = PageId.Insights,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = InsightsWidgets.CreateSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "insights.focus_pulse",
            Title = "24 小时心流时间线",
            Category = "insights",
            OwnerPage = PageId.Insights,
            AllowedPages = new HashSet<PageId> { PageId.Overview },
            DefaultSpan = new WidgetSpan(8, 3),
            SupportedSpans = [new(8, 3), new(8, 4), new(12, 3)],
            SupportedVariants = ["timeline"],
            Factory = InsightsWidgets.CreateFocusPulse
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "insights.focus_sessions",
            Title = "专注段排行",
            Category = "insights",
            OwnerPage = PageId.Insights,
            DefaultSpan = new WidgetSpan(4, 3),
            SupportedSpans = [new(4, 3), new(4, 2)],
            SupportedVariants = ["list"],
            Factory = InsightsWidgets.CreateSessions
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "insights.year_calendar",
            Title = "作息矩阵",
            Category = "insights",
            OwnerPage = PageId.Insights,
            DefaultSpan = new WidgetSpan(7, 2),
            SupportedSpans = [new(7, 2), new(7, 1), new(12, 2)],
            SupportedVariants = ["matrix"],
            Factory = InsightsWidgets.CreateCalendar
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "insights.rhythm_profile",
            Title = "节奏摘要",
            Category = "insights",
            OwnerPage = PageId.Insights,
            DefaultSpan = new WidgetSpan(5, 2),
            SupportedSpans = [new(5, 2), new(5, 1), new(12, 2)],
            SupportedVariants = ["list"],
            Factory = InsightsWidgets.CreateRhythm
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "chars.input_summary",
            Title = "输入摘要",
            Category = "chars",
            OwnerPage = PageId.Chars,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = CharsWidgets.CreateSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "chars.char_cloud",
            Title = "高频字",
            Category = "chars",
            OwnerPage = PageId.Chars,
            DefaultSpan = new WidgetSpan(7, 5),
            SupportedSpans = [new(7, 5), new(7, 4), new(12, 5)],
            SupportedVariants = ["cloud"],
            Factory = CharsWidgets.CreateCloud
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "chars.word_ranking",
            Title = "高频词组",
            Category = "chars",
            OwnerPage = PageId.Chars,
            DefaultSpan = new WidgetSpan(5, 5),
            SupportedSpans = [new(5, 5), new(5, 4), new(12, 5)],
            SupportedVariants = ["list"],
            Factory = CharsWidgets.CreateWords
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "chars.chart_board",
            Title = "字符图表",
            Category = "chars",
            OwnerPage = PageId.Chars,
            DefaultSpan = new WidgetSpan(12, 5),
            SupportedSpans = [new(12, 5), new(12, 4)],
            SupportedVariants = ["board"],
            Factory = CharsWidgets.CreateCharts
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "chars.privacy_card",
            Title = "数据来源与隐私",
            Category = "chars",
            OwnerPage = PageId.Chars,
            DefaultSpan = new WidgetSpan(12, 5),
            SupportedSpans = [new(12, 5), new(12, 4)],
            SupportedVariants = ["privacy"],
            Factory = CharsWidgets.CreatePrivacy
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "daily.briefing_card",
            Title = "日报概览",
            Category = "daily",
            OwnerPage = PageId.Daily,
            DefaultSpan = new WidgetSpan(12, 2),
            SupportedSpans = [new(12, 2), new(12, 1)],
            SupportedVariants = ["hero"],
            Factory = DailyWidgets.CreateBriefing
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "daily.timeline",
            Title = "会话时间线",
            Category = "daily",
            OwnerPage = PageId.Daily,
            DefaultSpan = new WidgetSpan(7, 2),
            SupportedSpans = [new(7, 2), new(7, 1), new(12, 2)],
            SupportedVariants = ["day"],
            Factory = DailyWidgets.CreateTimeline
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "daily.period_trend",
            Title = "区间每日趋势",
            Category = "daily",
            OwnerPage = PageId.Daily,
            DefaultSpan = new WidgetSpan(7, 2),
            SupportedSpans = [new(7, 2), new(7, 1), new(12, 2)],
            SupportedVariants = ["line"],
            Factory = DailyWidgets.CreatePeriodTrend
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "daily.app_list",
            Title = "应用排行",
            Category = "daily",
            OwnerPage = PageId.Daily,
            DefaultSpan = new WidgetSpan(5, 2),
            SupportedSpans = [new(5, 2), new(5, 1), new(12, 2)],
            SupportedVariants = ["list"],
            Factory = DailyWidgets.CreateApps
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "daily.highlights_grid",
            Title = "当日高光",
            Category = "daily",
            OwnerPage = PageId.Daily,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 2)],
            SupportedVariants = ["grid"],
            Factory = DailyWidgets.CreateHighlights
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "daily.key_list",
            Title = "按键排行",
            Category = "daily",
            OwnerPage = PageId.Daily,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 2)],
            SupportedVariants = ["list"],
            Factory = DailyWidgets.CreateKeys
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "heatmap.summary_strip",
            Title = "热区摘要",
            Category = "heatmap",
            OwnerPage = PageId.Heatmap,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = HeatWidgets.CreateSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "heatmap.screen_canvas",
            Title = "点击位置",
            Category = "heatmap",
            OwnerPage = PageId.Heatmap,
            DefaultSpan = new WidgetSpan(8, 3),
            SupportedSpans = [new(8, 3), new(8, 4), new(8, 5), new(12, 4)],
            SupportedVariants = ["grid"],
            Factory = HeatWidgets.CreateCanvas
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "heatmap.zone_stats",
            Title = "屏幕分区",
            Category = "heatmap",
            OwnerPage = PageId.Heatmap,
            DefaultSpan = new WidgetSpan(4, 3),
            SupportedSpans = [new(4, 3), new(4, 4), new(4, 5), new(12, 4), new(6, 2), new(3, 2)],
            SupportedVariants = ["zones"],
            Factory = HeatWidgets.CreateZones
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "heatmap.top_cells",
            Title = "Top 采样格",
            Category = "heatmap",
            OwnerPage = PageId.Heatmap,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 1)],
            SupportedVariants = ["list"],
            Factory = HeatWidgets.CreateTopCells
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "heatmap.channel_mix",
            Title = "通道构成",
            Category = "heatmap",
            OwnerPage = PageId.Heatmap,
            DefaultSpan = new WidgetSpan(6, 2),
            SupportedSpans = [new(6, 2), new(6, 1), new(12, 1)],
            SupportedVariants = ["legend"],
            Factory = HeatWidgets.CreateChannels
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "apps.summary_strip",
            Title = "归因概览",
            Category = "apps",
            OwnerPage = PageId.Apps,
            DefaultSpan = new WidgetSpan(12, 1),
            SupportedSpans = [new(12, 1), new(12, 2)],
            SupportedVariants = ["strip"],
            Factory = AppsWidgets.CreateSummary
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "apps.ranking_list",
            Title = "应用排行",
            Category = "apps",
            OwnerPage = PageId.Apps,
            DefaultSpan = new WidgetSpan(8, 5),
            SupportedSpans = [new(8, 5), new(8, 4), new(12, 5), new(12, 4), new(6, 2), new(3, 2)],
            SupportedVariants = ["list"],
            Factory = AppsWidgets.CreateRanking
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "apps.category_summary",
            Title = "时长构成",
            Category = "apps",
            OwnerPage = PageId.Apps,
            DefaultSpan = new WidgetSpan(4, 2),
            SupportedSpans = [new(4, 2), new(4, 3), new(6, 2), new(3, 2)],
            SupportedVariants = ["ring", "bars", "treemap"],
            Factory = AppsWidgets.CreateCategory
        }));

        Register(new DelegateWidgetModule(new WidgetDescriptor
        {
            ModuleId = "apps.key_composition",
            Title = "应用 × 键位组",
            Category = "apps",
            OwnerPage = PageId.Apps,
            DefaultSpan = new WidgetSpan(4, 3),
            SupportedSpans = [new(4, 3), new(4, 2), new(6, 3), new(6, 2)],
            SupportedVariants = ["stacked"],
            Factory = AppsWidgets.CreateComposition
        }));

        // ---------- 4.1.1 细颗粒微指标卡（3×1 / 3×2） ----------
        Register(new DelegateWidgetModule(Micro("trends.kpi.total", "周期总量", PageId.Trends, "trends", TrendWidgets.CreateKpiTotal)));
        Register(new DelegateWidgetModule(Micro("trends.kpi.daily_avg", "日均均值", PageId.Trends, "trends", TrendWidgets.CreateKpiDailyAverage)));
        Register(new DelegateWidgetModule(Micro("trends.kpi.peak_day", "峰值日", PageId.Trends, "trends", TrendWidgets.CreateKpiPeakDay)));
        Register(new DelegateWidgetModule(Micro("trends.kpi.growth", "环比变化", PageId.Trends, "trends", TrendWidgets.CreateKpiGrowth)));

        Register(new DelegateWidgetModule(Micro("hourly.kpi.peak_hour", "黄金高峰", PageId.Hourly, "hourly", HourlyWidgets.CreateKpiPeakHour)));
        Register(new DelegateWidgetModule(Micro("hourly.kpi.active_hours", "活跃小时", PageId.Hourly, "hourly", HourlyWidgets.CreateKpiActiveHours)));
        Register(new DelegateWidgetModule(Micro("hourly.kpi.night_ratio", "夜间占比", PageId.Hourly, "hourly", HourlyWidgets.CreateKpiNightRatio)));
        Register(new DelegateWidgetModule(Micro("hourly.kpi.offpeak_hour", "低谷时段", PageId.Hourly, "hourly", HourlyWidgets.CreateKpiOffPeakHour)));

        Register(new DelegateWidgetModule(Micro("keys.kpi.total", "总击键", PageId.Keys, "keys", KeysWidgets.CreateKpiTotal)));
        Register(new DelegateWidgetModule(Micro("keys.kpi.top_key", "最高频按键", PageId.Keys, "keys", KeysWidgets.CreateKpiTopKey)));
        Register(new DelegateWidgetModule(Micro("keys.kpi.avg_hold", "平均按住", PageId.Keys, "keys", KeysWidgets.CreateKpiAverageHold)));
        Register(new DelegateWidgetModule(Micro("keys.kpi.combo_ratio", "组合键占比", PageId.Keys, "keys", KeysWidgets.CreateKpiComboRatio)));

        Register(new DelegateWidgetModule(Micro("apps.kpi.count", "活跃应用数", PageId.Apps, "apps", AppsWidgets.CreateKpiCount)));
        Register(new DelegateWidgetModule(Micro("apps.kpi.top_app", "主力专注应用", PageId.Apps, "apps", AppsWidgets.CreateKpiTopApp)));
        Register(new DelegateWidgetModule(Micro("apps.kpi.focus_share", "主力占比", PageId.Apps, "apps", AppsWidgets.CreateKpiFocusShare)));
        Register(new DelegateWidgetModule(Micro("apps.kpi.switches", "应用切换", PageId.Apps, "apps", AppsWidgets.CreateKpiSwitches)));

        Register(new DelegateWidgetModule(Micro("insights.kpi.focus_time", "深度专注", PageId.Insights, "insights", InsightsWidgets.CreateKpiFocusTime)));
        Register(new DelegateWidgetModule(Micro("insights.kpi.sessions", "有效专注段", PageId.Insights, "insights", InsightsWidgets.CreateKpiSessions)));
        Register(new DelegateWidgetModule(Micro("insights.kpi.flow_score", "心流节律", PageId.Insights, "insights", InsightsWidgets.CreateKpiFlowScore)));

        Register(new DelegateWidgetModule(Micro("chars.kpi.total", "上屏字数", PageId.Chars, "chars", CharsWidgets.CreateKpiTotal)));
        Register(new DelegateWidgetModule(Micro("chars.kpi.avg_len", "平均上屏长度", PageId.Chars, "chars", CharsWidgets.CreateKpiAverageLength)));
        Register(new DelegateWidgetModule(Micro("chars.kpi.distinct", "去重汉字", PageId.Chars, "chars", CharsWidgets.CreateKpiDistinct)));
        Register(new DelegateWidgetModule(Micro("chars.kpi.first_rate", "候选首选率", PageId.Chars, "chars", CharsWidgets.CreateKpiFirstRate)));

        Register(new DelegateWidgetModule(Micro("heatmap.kpi.clicks", "总点击量", PageId.Heatmap, "heatmap", HeatWidgets.CreateKpiClicks)));
        Register(new DelegateWidgetModule(Micro("heatmap.kpi.channel", "主力点击通道", PageId.Heatmap, "heatmap", HeatWidgets.CreateKpiChannel)));
        Register(new DelegateWidgetModule(Micro("heatmap.kpi.top_zone", "最热区域", PageId.Heatmap, "heatmap", HeatWidgets.CreateKpiTopZone)));
        Register(new DelegateWidgetModule(Micro("heatmap.kpi.coverage", "热区覆盖", PageId.Heatmap, "heatmap", HeatWidgets.CreateKpiCoverage)));
    }

    public static IReadOnlyList<WidgetDescriptor> Descriptors => Ordered;

    public static int Count => Ordered.Count;

    /// <summary>某个页面可用的模块目录：本页专属 + 明确列入 AllowedPages 的通用模块。</summary>
    public static IReadOnlyList<WidgetDescriptor> ForPage(PageId page) =>
        Ordered.Where(descriptor => descriptor.IsAvailableOn(page)).ToArray();

    public static IReadOnlyList<WidgetDescriptor> ForPage(string pageId) =>
        ForPage(PageIds.Parse(pageId));

    public static bool TryGet(string moduleId, out IStatWidgetModule module) =>
        Modules.TryGetValue(moduleId, out module!);

    /// <summary>编辑抽屉/新增模块使用的页面域查询；跨页模块必须有 AllowedPages 才能命中。</summary>
    public static bool TryGetForPage(
        PageId page,
        string moduleId,
        out IStatWidgetModule module)
    {
        if (Modules.TryGetValue(moduleId, out module!) && module.Descriptor.IsAvailableOn(page))
        {
            return true;
        }

        module = null!;
        return false;
    }

    public static IStatWidgetModule Get(string moduleId) =>
        Modules.TryGetValue(moduleId, out IStatWidgetModule? module)
            ? module
            : throw new KeyNotFoundException($"未注册模块：{moduleId}");

    /// <summary>空或非法 VariantId 回退到模块 SupportedVariants[0]。</summary>
    public static string ResolveVariant(WidgetDescriptor descriptor, string? requested)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return !string.IsNullOrWhiteSpace(requested)
               && descriptor.SupportedVariants.Contains(requested, StringComparer.Ordinal)
            ? requested
            : descriptor.SupportedVariants[0];
    }

    public static void Register(IStatWidgetModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        WidgetDescriptor descriptor = module.Descriptor;
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.ModuleId))
        {
            throw new ArgumentException("模块 ModuleId 不能为空。", nameof(module));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Title))
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 缺少标题。", nameof(module));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Category))
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 缺少分类。", nameof(module));
        }

        if (descriptor.OwnerPage < PageId.Overview || descriptor.OwnerPage > PageId.Heatmap)
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 的 OwnerPage 非法。", nameof(module));
        }

        if (Modules.ContainsKey(descriptor.ModuleId))
        {
            throw new ArgumentException($"模块重复注册：{descriptor.ModuleId}。", nameof(module));
        }

        if (!descriptor.DefaultSpan.IsWithin(LayoutPresets.Columns, LayoutPresets.Rows))
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 默认尺寸超出 12×6。", nameof(module));
        }

        if (descriptor.SupportedSpans.Count == 0)
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 未声明支持的尺寸。", nameof(module));
        }

        foreach (WidgetSpan span in descriptor.SupportedSpans)
        {
            if (!span.IsWithin(LayoutPresets.Columns, LayoutPresets.Rows))
            {
                throw new ArgumentException($"模块 {descriptor.ModuleId} 的尺寸 {span} 超出 12×6。", nameof(module));
            }
        }

        if (!descriptor.SupportedSpans.Contains(descriptor.DefaultSpan))
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 的默认尺寸不在 SupportedSpans 内。", nameof(module));
        }

        if (descriptor.SupportedVariants.Count == 0
            || descriptor.SupportedVariants.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 必须声明至少一个有效变体。", nameof(module));
        }

        if (descriptor.Factory is null)
        {
            throw new ArgumentException($"模块 {descriptor.ModuleId} 缺少静态工厂委托。", nameof(module));
        }

        ApplyCapabilities(descriptor);
        Modules.Add(descriptor.ModuleId, module);
        Ordered.Add(descriptor);
    }

    /// <summary>
    /// 用稳定 ModuleId 前缀标注能力，避免 82 个对象初始化器重复写同一组常量。
    /// 新增模块仍必须在静态构造函数显式 Register；缺省能力为 AppTotals。
    /// </summary>
    private static void ApplyCapabilities(WidgetDescriptor descriptor)
    {
        descriptor.GlobalOnly =
            descriptor.ModuleId.StartsWith("hardware.", StringComparison.Ordinal)
            || string.Equals(descriptor.ModuleId, "chars.privacy_card", StringComparison.Ordinal);

        descriptor.RequiredCapabilities = descriptor.GlobalOnly
            ? AppDataCapability.None
            : RequiredCapabilities(descriptor.ModuleId);
    }

    private static AppDataCapability RequiredCapabilities(string moduleId)
    {
        if (moduleId.StartsWith("hourly.", StringComparison.Ordinal))
        {
            return AppDataCapability.Hourly;
        }

        if (moduleId.StartsWith("heatmap.", StringComparison.Ordinal))
        {
            return AppDataCapability.Heatmap;
        }

        if (moduleId.StartsWith("insights.", StringComparison.Ordinal))
        {
            return AppDataCapability.Focus;
        }

        if (moduleId.StartsWith("chars.", StringComparison.Ordinal))
        {
            return AppDataCapability.Chars;
        }

        if (string.Equals(moduleId, "overview.distribution_ring", StringComparison.Ordinal)
            || string.Equals(moduleId, "keys.top5_bars", StringComparison.Ordinal)
            || string.Equals(moduleId, "daily.key_list", StringComparison.Ordinal))
        {
            return AppDataCapability.KeyDetail;
        }

        if (string.Equals(moduleId, "overview.insight_bar", StringComparison.Ordinal))
        {
            return AppDataCapability.KeyDetail | AppDataCapability.Hourly;
        }

        if (string.Equals(moduleId, "daily.timeline", StringComparison.Ordinal))
        {
            return AppDataCapability.Focus;
        }

        if (moduleId.StartsWith("keys.", StringComparison.Ordinal)
            && !string.Equals(moduleId, "keys.kpi.total", StringComparison.Ordinal))
        {
            return AppDataCapability.KeyDetail;
        }

        return AppDataCapability.AppTotals;
    }

    private static WidgetDescriptor Micro(
        string moduleId,
        string title,
        PageId ownerPage,
        string category,
        Func<WidgetContext, WidgetView> factory) => new()
    {
        ModuleId = moduleId,
        Title = title,
        Category = category,
        OwnerPage = ownerPage,
        DefaultSpan = new WidgetSpan(3, 1),
        SupportedSpans = [new(3, 1), new(3, 2)],
        SupportedVariants = ["value"],
        Factory = factory
    };

    private static WidgetDescriptor Kpi(string moduleId, string title, KpiKind kind) => new()
    {
        ModuleId = moduleId,
        Title = title,
        Category = "overview",
        OwnerPage = PageId.Overview,
        DefaultSpan = new WidgetSpan(3, 1),
        SupportedSpans = [new(3, 1), new(4, 1), new(6, 1), new(4, 2), new(6, 2)],
        SupportedVariants = ["sparkline", "minimal"],
        Factory = context => OverviewWidgets.CreateKpi(context, kind)
    };
}
