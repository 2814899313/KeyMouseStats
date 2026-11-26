using Avalonia.Media;

namespace Kms.Render.Tokens;

/// <summary>字号阶梯（DIP）。13 套主题共用，M4 再按页面细化。</summary>
public sealed record Typography(
    double Display,
    double H1,
    double H2,
    double Body,
    double Caption,
    double Mono)
{
    public static Typography Default { get; } = new(38, 29, 21, 13, 11, 11);
}

/// <summary>
/// 一套主题的全部设计令牌。字段与 v3 <c>ArtTheme</c> 的 15 色 + 半径一一对应；
/// 后加的热力色阶用于点击热区，保持与 v3 <c>HeatScale</c> 一致。
/// </summary>
public sealed record ThemeTokens(
    int Id,
    string Name,
    double Radius,
    Color Background,
    Color Card,
    Color Raised,
    Color Line,
    Color Text,
    Color Muted,
    Color Accent,
    Color OnAccent,
    Color Green,
    Color Orange,
    Color Purple,
    Color Red,
    Color Cyan,
    Color Sidebar,
    IReadOnlyList<Color> HeatStops)
{
    public Typography Typography { get; init; } = Typography.Default;
}
