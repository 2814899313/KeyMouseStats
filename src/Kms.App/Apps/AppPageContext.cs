using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.App.Apps;

namespace Kms.App.Apps;

/// <summary>
/// 页面级应用上下文。Shell 负责异步取得 <see cref="AppProfileSnapshot"/>，
/// 页面模型只消费不可变快照与能力标记，不在 UI 线程访问 SQLite。
/// </summary>
public sealed class AppPageContext
{
    public static AppPageContext All { get; } = new(
        AppFilterScope.All,
        AppScopeSet.All,
        null,
        AppDataCapabilities.AllExact);

    public AppPageContext(
        AppFilterScope filter,
        AppScopeSet scope,
        AppProfileSnapshot? profile,
        AppDataCapabilities capabilities,
        AppCategoryService? categoryService = null)
    {
        Filter = filter;
        Scope = scope;
        Profile = profile;
        Capabilities = capabilities;
        CategoryService = categoryService;
    }

    public AppFilterScope Filter { get; }

    public AppScopeSet Scope { get; }

    public AppProfileSnapshot? Profile { get; }

    public AppCategoryService? CategoryService { get; }

    public AppDataCapabilities Capabilities { get; }

    public bool IsAll => Filter.Kind == AppFilterKind.All;

    public string DisplayName =>
        Filter.Kind switch
        {
            AppFilterKind.Category => CategoryService?.Find(Filter.CategoryKey ?? "")?.Name
                ?? "工作性质",
            AppFilterKind.App => AppIdentity.DisplayName(Filter.AppKey),
            _ => "全部应用"
        };

    public static AppPageContext Create(
        AppFilterScope filter,
        AppProfileSnapshot? profile,
        AppScopeSet? scope = null,
        AppCategoryService? categoryService = null)
    {
        if (filter.Kind == AppFilterKind.All)
        {
            return categoryService is null
                ? All
                : new AppPageContext(
                    AppFilterScope.All,
                    AppScopeSet.All,
                    null,
                    AppDataCapabilities.AllExact,
                    categoryService);
        }

        AppScopeSet resolvedScope = scope ?? filter.ToScopeSet();
        AppDataCapability exact = profile?.Capabilities.Exact ?? AppDataCapability.None;
        if (profile is null
            && resolvedScope.Count > 0
            && filter.Kind is AppFilterKind.App or AppFilterKind.Category)
        {
            // Profile 尚未异步返回时，单应用/分类的内存总量仍然可精确聚合；
            // 先放行 AppTotals，避免首帧把第一行 KPI 误降级成 PENDING 空洞。
            exact |= AppDataCapability.AppTotals;
        }

        if (profile is not null
            && (profile.Totals.AppCount > 0
                || profile.Totals.Keys > 0
                || profile.Totals.Clicks > 0
                || profile.Totals.Wheel > 0
                || profile.Totals.ActiveSeconds > 0))
        {
            // 内存/窗口总量一旦存在就一定是精确值；分桶表只影响 Hourly/VK/Heatmap。
            exact |= AppDataCapability.AppTotals;
        }

        // 字符内容来自独立 Rime commit log，按 Application 实时切片；
        // 它不属于 SQLite 应用分桶 Pending 维度。
        if (filter.Kind == AppFilterKind.App)
        {
            exact |= AppDataCapability.Chars;
        }

        AppDataCapability pending = AppDataCapability.All & ~exact;
        return new AppPageContext(
            filter,
            resolvedScope,
            profile,
            new AppDataCapabilities(
                exact,
                pending,
                profile?.Capabilities.Note
                    ?? "应用分桶加载中；未采集维度不会用全局数据替代。"),
            categoryService);
    }

    public StatsData FilterStats(StatsData data) =>
        AppScopeData.Filter(data, Scope, Profile);

    public AppPageContext WithProfile(AppProfileSnapshot? profile) =>
        Create(Filter, profile, Scope, CategoryService);
}
