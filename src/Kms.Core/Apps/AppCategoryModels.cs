namespace Kms.Core.Apps;

/// <summary>
/// 一个工作性质分类。category_key 是稳定文本主键；legacy_id 仅用于兼容
/// events_window_session.category_id / app_category_rule.category_id 的历史数字。
/// </summary>
public sealed record AppCategoryDefinition(
    string CategoryKey,
    string Name,
    string ColorHex,
    bool IsWork,
    int SortOrder,
    bool IsBuiltin,
    int LegacyId);

public sealed record AppCategoryRule(
    string RuleKey,
    string CategoryKey,
    int CategoryId,
    string Source,
    long UpdatedMs);

public sealed record AppCategoryMatch(
    AppCategoryDefinition Category,
    string Source,
    string Reason);

/// <summary>内置分类与历史 0..9 的兼容映射。数值只增不改，确保旧规则可读。</summary>
public static class AppCategoryBuiltins
{
    public const string Coding = "coding";
    public const string Game = "game";
    public const string Media = "media";
    public const string Other = "other";
    public const string Uncategorized = "uncategorized";
    public const string Office = "office";
    public const string Research = "research";
    public const string Writing = "writing";
    public const string Communication = "communication";

    /// <summary>历史内置 key；旧规则读取时映射到 media，不再单独展示。</summary>
    public const string Entertainment = "entertainment";

    public const string Ai = "ai";

    public static IReadOnlyList<AppCategoryDefinition> All { get; } =
    [
        new(Coding, "编程", "#8B5CF6", true, 0, true, 0),
        new(Game, "游戏", "#EF4444", false, 1, true, 1),
        new(Media, "影音娱乐", "#F59E0B", false, 2, true, 2),
        new(Other, "其他", "#64748B", false, 3, true, 3),
        new(Uncategorized, "未分类", "#94A3B8", false, 4, true, 4),
        new(Office, "办公", "#3B82F6", true, 5, true, 5),
        new(Research, "科研仿真", "#06B6D4", true, 6, true, 6),
        new(Writing, "小说与文本创作", "#EC4899", true, 7, true, 7),
        new(Communication, "沟通", "#10B981", false, 8, true, 8),
        new(Ai, "AI 协同", "#22C55E", true, 10, true, 10)
    ];

    /// <summary>历史数字 9（原「娱乐」）统一映射到「影音娱乐」。</summary>
    public static IReadOnlyDictionary<int, string> LegacyAliases { get; } =
        new Dictionary<int, string> { [9] = Media };

    public static AppCategoryDefinition UncategorizedCategory =>
        All.First(static category => category.CategoryKey == Uncategorized);

    public static AppCategoryDefinition? ForLegacyId(int legacyId) =>
        All.FirstOrDefault(category => category.LegacyId == legacyId)
        ?? (LegacyAliases.TryGetValue(legacyId, out string? alias)
            ? ForKey(alias)
            : null);

    public static AppCategoryDefinition? ForKey(string? categoryKey)
    {
        if (string.IsNullOrWhiteSpace(categoryKey))
        {
            return null;
        }

        string key = string.Equals(categoryKey, Entertainment, StringComparison.Ordinal)
            ? Media
            : categoryKey;
        return All.FirstOrDefault(category =>
            string.Equals(category.CategoryKey, key, StringComparison.Ordinal));
    }

    public static int LegacyIdFor(string? categoryKey) =>
        ForKey(categoryKey)?.LegacyId ?? UncategorizedCategory.LegacyId;

    public static string KeyForLegacyId(int legacyId) =>
        ForLegacyId(legacyId)?.CategoryKey ?? Uncategorized;
}
