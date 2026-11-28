using System.Text.Json.Serialization;

namespace Kms.App.Layout;

/// <summary>
/// 单个模块在页面中的声明式位置。JSON 字段使用 camelCase，
/// 由 <see cref="LayoutJsonContext"/> 源生成器序列化，不经过反射。
/// </summary>
public sealed class WidgetPlacement
{
    public string ModuleId { get; set; } = "";

    public string? VariantId { get; set; }

    public int Col { get; set; }

    public int Row { get; set; }

    public int ColSpan { get; set; } = 1;

    public int RowSpan { get; set; } = 1;

    public WidgetPlacement Clone() => new()
    {
        ModuleId = ModuleId,
        VariantId = VariantId,
        Col = Col,
        Row = Row,
        ColSpan = ColSpan,
        RowSpan = RowSpan
    };
}

/// <summary>一个页面的完整布局；PageId 使用稳定的英文标识（overview / apps）。</summary>
public sealed class PageLayoutConfig
{
    public string PageId { get; set; } = "";

    public List<WidgetPlacement> Placements { get; set; } = [];

    public PageLayoutConfig Clone() => new()
    {
        PageId = PageId,
        Placements = Placements.Select(static placement => placement.Clone()).ToList()
    };
}

/// <summary>layouts.json 根对象。独立于 stats.txt，损坏时整体回退内置预设。</summary>
public sealed class LayoutDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<PageLayoutConfig> Pages { get; set; } = [];

    public LayoutDocument Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Pages = Pages.Select(static page => page.Clone()).ToList()
    };
}

/// <summary>
/// NativeAOT / Trim 兼容的 JSON 上下文。所有布局读写都必须走这里，
/// 禁止使用反射式 JsonSerializer 重载。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LayoutDocument))]
[JsonSerializable(typeof(PageLayoutConfig))]
[JsonSerializable(typeof(WidgetPlacement))]
[JsonSerializable(typeof(List<PageLayoutConfig>))]
[JsonSerializable(typeof(List<WidgetPlacement>))]
public partial class LayoutJsonContext : JsonSerializerContext;
