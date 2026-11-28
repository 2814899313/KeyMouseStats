namespace Kms.App.Layout;

/// <summary>一键场景模板。工厂委托显式登记，NativeAOT 下无需反射或 JSON 发现。</summary>
public sealed class LayoutScenario
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required Func<PageLayoutConfig> Create { get; init; }
}

/// <summary>内置场景目录。默认经典布局对全部页面可用，总览额外提供极简与极客两套。</summary>
public static class LayoutScenarios
{
    private static readonly Dictionary<string, LayoutScenario> Classic = new(StringComparer.Ordinal)
    {
        [LayoutPresets.OverviewPageId] = ClassicScenario("经典均衡", "8 张 KPI + 分布环 + 按键排行 + 洞察", LayoutPresets.CreateOverview),
        [LayoutPresets.AppsPageId] = ClassicScenario("经典均衡", "归因概览 + 应用排行 + 时长构成", LayoutPresets.CreateApps),
        [LayoutPresets.TrendsPageId] = ClassicScenario("经典均衡", "区间摘要 + 趋势主图 + 明细与节奏", LayoutPresets.CreateTrends),
        [LayoutPresets.HourlyPageId] = ClassicScenario("经典均衡", "小时摘要 + 24 小时柱状 + 详情", LayoutPresets.CreateHourly),
        [LayoutPresets.KeysPageId] = ClassicScenario("经典均衡", "按键摘要 + 排行 + 按住时长", LayoutPresets.CreateKeys),
        [LayoutPresets.InsightsPageId] = ClassicScenario("经典均衡", "心流摘要 + 时间线 + 专注段", LayoutPresets.CreateInsights),
        [LayoutPresets.CharsPageId] = ClassicScenario("经典均衡", "输入摘要 + 高频字词", LayoutPresets.CreateChars),
        [LayoutPresets.DailyPageId] = ClassicScenario("经典均衡", "日报概览 + 时间线 + 榜单", LayoutPresets.CreateDaily),
        [LayoutPresets.HeatmapPageId] = ClassicScenario("经典均衡", "热区摘要 + 点击位置 + 分区", LayoutPresets.CreateHeatmap)
    };

    private static readonly LayoutScenario[] OverviewExtras =
    [
        new LayoutScenario
        {
            Id = "overview.minimal",
            Name = "极简沉浸",
            Description = "超大极简数字卡 + 12×3 面积主图",
            Create = LayoutPresets.CreateOverviewMinimal
        },
        new LayoutScenario
        {
            Id = "overview.geek",
            Name = "极客高密度监控",
            Description = "实时速率 + 心流脉冲 + 外设寿命 + 键帽排行",
            Create = LayoutPresets.CreateOverviewGeek
        }
    ];

    private static readonly LayoutScenario[] OverviewScenarios =
        [Classic[LayoutPresets.OverviewPageId], .. OverviewExtras];

    private static readonly LayoutScenario[] Empty = [];

    public static IReadOnlyList<LayoutScenario> ForPage(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        if (string.Equals(pageId, LayoutPresets.OverviewPageId, StringComparison.Ordinal))
        {
            return OverviewScenarios;
        }

        return Classic.TryGetValue(pageId, out LayoutScenario? scenario) ? [scenario] : Empty;
    }

    private static LayoutScenario ClassicScenario(string name, string description, Func<PageLayoutConfig> create) => new()
    {
        Id = "classic",
        Name = name,
        Description = description,
        Create = create
    };
}
