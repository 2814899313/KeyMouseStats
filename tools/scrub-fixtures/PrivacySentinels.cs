namespace Kms.Tools.ScrubFixtures;

/// <summary>
/// v3 的哨兵标题不含隐私，脱敏时必须原样保留，否则「未识别 / 无前台窗口」
/// 这类状态会退化成普通类别，丢掉测试价值。
/// </summary>
internal static class PrivacySentinels
{
    private static readonly HashSet<string> Titles = new(StringComparer.Ordinal)
    {
        "",
        "其他窗口（当日标题数量已达上限）",
        "无法识别的前台窗口",
        "窗口切换中 / 无法归因",
        "无标题窗口",
        "无前台窗口"
    };

    public static bool IsTitle(string value) => Titles.Contains(value);
}
