namespace Kms.Core.Apps;

/// <summary>全局应用过滤的三种范围。</summary>
public enum AppFilterKind
{
    All = 0,
    App = 1,
    Category = 2
}

/// <summary>
/// 不可变过滤描述。CacheKey 是稳定的 UI/仓库缓存键：
/// all / app:&lt;app_key&gt; / cat:&lt;category_key&gt;。
/// </summary>
public readonly record struct AppFilterScope(
    AppFilterKind Kind,
    string? AppKey = null,
    string? CategoryKey = null,
    IReadOnlyList<string>? AppKeys = null)
{
    public static AppFilterScope All { get; } = new(AppFilterKind.All);

    public static AppFilterScope ByApp(string appKey)
    {
        string normalized = AppIdentity.NormalizeKey(appKey);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("AppKey 不能为空。", nameof(appKey));
        }

        return new AppFilterScope(AppFilterKind.App, normalized);
    }

    /// <summary>
    /// 同名多路径应用：主键用于展示，AppKeys 是展开后的全部真实应用键。
    /// 查询与页面过滤统一走 <see cref="ToScopeSet"/>，不会只查第一个路径。
    /// </summary>
    public static AppFilterScope ByAppGroup(string primaryAppKey, IEnumerable<string> appKeys)
    {
        string[] normalized = appKeys
            .Select(AppIdentity.NormalizeKey)
            .Where(static key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("AppKeys 不能为空。", nameof(appKeys));
        }

        string primary = AppIdentity.NormalizeKey(primaryAppKey);
        if (primary.Length == 0 || !normalized.Contains(primary, StringComparer.Ordinal))
        {
            primary = normalized[0];
        }

        return new AppFilterScope(
            AppFilterKind.App,
            primary,
            AppKeys: normalized.Length > 1 ? normalized : null);
    }

    public static AppFilterScope ByCategory(string categoryKey)
    {
        if (string.IsNullOrWhiteSpace(categoryKey))
        {
            throw new ArgumentException("CategoryKey 不能为空。", nameof(categoryKey));
        }

        return new AppFilterScope(AppFilterKind.Category, CategoryKey: categoryKey.Trim());
    }

    public string CacheKey => Kind switch
    {
        AppFilterKind.App => AppScopeSet.CacheKeyFor(
            AppKeys is { Count: > 0 } ? AppKeys : new[] { AppKey ?? "" }),
        AppFilterKind.Category => "cat:" + (CategoryKey ?? ""),
        _ => "all"
    };

    public AppScopeSet ToScopeSet(IEnumerable<string>? categoryAppKeys = null)
    {
        return Kind switch
        {
            AppFilterKind.App => AppScopeSet.FromApps(
                AppKeys is { Count: > 0 } ? AppKeys : new[] { AppKey ?? "" }),
            AppFilterKind.Category => AppScopeSet.FromCategory(CategoryKey ?? "", categoryAppKeys ?? []),
            _ => AppScopeSet.All
        };
    }
}

/// <summary>
/// 过滤展开后的应用键集合。All 表示不带谓词；App/Category 使用集合匹配，
/// 避免在每个查询里重复拼接 SQL 字符串。
/// </summary>
public sealed class AppScopeSet
{
    private readonly HashSet<string> _appKeys;

    private AppScopeSet(bool isAll, string cacheKey, IEnumerable<string> appKeys)
    {
        IsAll = isAll;
        CacheKey = cacheKey;
        _appKeys = new HashSet<string>(
            appKeys.Select(AppIdentity.NormalizeKey).Where(static key => key.Length > 0),
            StringComparer.Ordinal);
    }

    public static AppScopeSet All { get; } = new(true, "all", []);

    public bool IsAll { get; }

    public string CacheKey { get; }

    public IReadOnlySet<string> AppKeys => _appKeys;

    public int Count => _appKeys.Count;

    public static AppScopeSet FromApp(string appKey) =>
        FromApps([appKey]);

    /// <summary>同名多路径应用：用同一 AppScopeSet 同时匹配多个真实路径。</summary>
    public static AppScopeSet FromApps(IEnumerable<string> appKeys)
    {
        string[] normalized = appKeys
            .Select(AppIdentity.NormalizeKey)
            .Where(static key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            return new AppScopeSet(false, "app:", []);
        }

        return new AppScopeSet(false, CacheKeyFor(normalized), normalized);
    }

    public static string CacheKeyFor(IEnumerable<string> appKeys)
    {
        string[] normalized = appKeys
            .Select(AppIdentity.NormalizeKey)
            .Where(static key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        return normalized.Length == 0
            ? "app:"
            : "app:" + string.Join("|", normalized);
    }

    public static AppScopeSet FromCategory(string categoryKey, IEnumerable<string> appKeys)
    {
        if (string.IsNullOrWhiteSpace(categoryKey))
        {
            throw new ArgumentException("CategoryKey 不能为空。", nameof(categoryKey));
        }

        return new AppScopeSet(false, "cat:" + categoryKey.Trim(), appKeys);
    }

    public bool Contains(string? appKey)
    {
        if (IsAll)
        {
            return true;
        }

        string normalized = AppIdentity.NormalizeKey(appKey);
        return normalized.Length > 0 && _appKeys.Contains(normalized);
    }
}
