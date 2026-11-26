using Avalonia.Media;

namespace Kms.Render.Text;

/// <summary>
/// 文本度量缓存的键。前景色不属于度量身份：颜色不影响字形宽度/高度，
/// 绘制前通过 <see cref="TextMeasureCache.GetFormattedText"/> 设置。
/// </summary>
public readonly record struct TextMeasureKey
{
    public TextMeasureKey(
        string text,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight = FontWeight.Normal,
        double maxWidth = double.PositiveInfinity)
    {
        Text = text;
        FontFamily = fontFamily;
        FontSize = fontSize;
        FontWeight = fontWeight;
        MaxWidth = maxWidth;
    }

    /// <summary>要测量的文本。空串合法；null 由缓存拒绝。</summary>
    public string Text { get; init; }

    /// <summary>已解析的字族名（通常来自 <see cref="FontChains.ResolveFamily"/>）。</summary>
    public string FontFamily { get; init; }

    /// <summary>Avalonia DIP 字号。</summary>
    public double FontSize { get; init; }

    public FontWeight FontWeight { get; init; }

    /// <summary>
    /// 最大行宽；<see cref="double.PositiveInfinity"/> 或 NaN 表示不约束，负数按 0 处理。
    /// 归一化发生在 <see cref="TextMeasureCache"/> 内部。
    /// </summary>
    public double MaxWidth { get; init; }
}
