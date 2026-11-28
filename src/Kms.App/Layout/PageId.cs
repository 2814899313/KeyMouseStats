namespace Kms.App.Layout;

/// <summary>
/// 九大分析页的强类型页面标识。模块归属、编辑抽屉过滤与测试都使用该枚举，
/// 不再依赖散落的字符串常量。枚举顺序与侧栏页面顺序一致。
/// </summary>
public enum PageId
{
    Overview = 0,
    Trends = 1,
    Hourly = 2,
    Keys = 3,
    Apps = 4,
    Insights = 5,
    Chars = 6,
    Daily = 7,
    Heatmap = 8
}

/// <summary>
/// <see cref="PageId"/> 与稳定英文 pageId 的唯一映射。
/// 序列化仍写原来的小写字符串，保证既有 layouts.json 与官方预设零迁移。
/// </summary>
public static class PageIds
{
    public const string Overview = "overview";
    public const string Trends = "trend";
    public const string Hourly = "hourly";
    public const string Keys = "keys";
    public const string Apps = "apps";
    public const string Insights = "insights";
    public const string Chars = "chars";
    public const string Daily = "daily";
    public const string Heatmap = "heat";

    public static string ToId(this PageId page) => page switch
    {
        PageId.Overview => Overview,
        PageId.Trends => Trends,
        PageId.Hourly => Hourly,
        PageId.Keys => Keys,
        PageId.Apps => Apps,
        PageId.Insights => Insights,
        PageId.Chars => Chars,
        PageId.Daily => Daily,
        PageId.Heatmap => Heatmap,
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, "未知页面")
    };

    public static string Label(this PageId page) => page switch
    {
        PageId.Overview => "总览",
        PageId.Trends => "趋势",
        PageId.Hourly => "时段",
        PageId.Keys => "按键",
        PageId.Apps => "应用",
        PageId.Insights => "洞察",
        PageId.Chars => "字符",
        PageId.Daily => "日报",
        PageId.Heatmap => "热区",
        _ => page.ToString()
    };

    public static PageId Parse(string pageId) =>
        TryParse(pageId, out PageId page)
            ? page
            : throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "未知页面");

    public static bool TryParse(string? pageId, out PageId page)
    {
        switch (pageId)
        {
            case Overview:
                page = PageId.Overview;
                return true;
            case Trends:
                page = PageId.Trends;
                return true;
            case Hourly:
                page = PageId.Hourly;
                return true;
            case Keys:
                page = PageId.Keys;
                return true;
            case Apps:
                page = PageId.Apps;
                return true;
            case Insights:
                page = PageId.Insights;
                return true;
            case Chars:
                page = PageId.Chars;
                return true;
            case Daily:
                page = PageId.Daily;
                return true;
            case Heatmap:
                page = PageId.Heatmap;
                return true;
            default:
                page = PageId.Overview;
                return false;
        }
    }
}
