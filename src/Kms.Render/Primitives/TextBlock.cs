using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Kms.Render.Primitives;

/// <summary>
/// 文本原语。Ellipsis 是纯函数（测量委托注入，可在无字体后端的环境单测）；
/// DrawText / DrawCentered 只做 FormattedText 的薄封装。
/// </summary>
public static class TextBlock
{
    public const string DefaultFamily = "Microsoft YaHei UI";
    public const string NumericFamily = "Segoe UI";
    public const string MonoFamily = "Cascadia Mono";
    public const string EllipsisCharacter = "…";

    /// <summary>
    /// 按测量委托截断，保留完整文本元素（不会把代理对/组合字符切断）。
    /// maxWidth ≤ 0 时返回空串；连省略号都放不下也返回空串。
    /// </summary>
    public static string Ellipsis(string text, double maxWidth, Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (double.IsPositiveInfinity(maxWidth))
        {
            return text;
        }

        if (!double.IsFinite(maxWidth) || maxWidth <= 0)
        {
            return string.Empty;
        }

        if (measure(text) <= maxWidth)
        {
            return text;
        }

        if (measure(EllipsisCharacter) > maxWidth)
        {
            return string.Empty;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int low = 0;
        int high = starts.Length - 1;
        int best = 0;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            int cut = mid < starts.Length ? starts[mid] : text.Length;
            string candidate = string.Concat(text.AsSpan(0, cut), EllipsisCharacter);

            if (measure(candidate) <= maxWidth)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        int prefixLength = best < starts.Length ? starts[best] : text.Length;
        return string.Concat(text.AsSpan(0, prefixLength), EllipsisCharacter);
    }

    public static FormattedText Create(
        string text,
        double fontSize,
        IBrush foreground,
        string fontFamily = DefaultFamily,
        FontWeight? weight = null,
        double maxWidth = double.PositiveInfinity,
        int maxLines = 1,
        TextAlignment alignment = TextAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(foreground);

        Typeface typeface = new(
            new FontFamily(fontFamily),
            FontStyle.Normal,
            weight ?? FontWeight.Normal,
            FontStretch.Normal);

        FormattedText formatted = new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(1, PrimitiveMath.Finite(fontSize)),
            foreground);

        formatted.TextAlignment = alignment;
        formatted.MaxLineCount = Math.Max(1, maxLines);

        if (double.IsFinite(maxWidth) && maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }
        else
        {
            formatted.Trimming = TextTrimming.None;
        }

        return formatted;
    }

    public static Size Measure(FormattedText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Size(text.Width, text.Height);
    }

    public static void Draw(DrawingContext context, FormattedText text, Point origin)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(text);
        context.DrawText(text, origin);
    }

    public static void DrawText(
        DrawingContext context,
        string text,
        Point origin,
        double fontSize,
        IBrush foreground,
        string fontFamily = DefaultFamily,
        FontWeight? weight = null,
        double maxWidth = double.PositiveInfinity,
        int maxLines = 1,
        TextAlignment alignment = TextAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(context);
        FormattedText formatted = Create(
            text,
            fontSize,
            foreground,
            fontFamily,
            weight,
            maxWidth,
            maxLines,
            alignment);
        context.DrawText(formatted, origin);
    }

    /// <summary>在 bounds 内水平居中、垂直居中绘制；maxWidth ≤ 0 时使用 bounds 宽度。</summary>
    public static void DrawCentered(
        DrawingContext context,
        string text,
        Rect bounds,
        double fontSize,
        IBrush foreground,
        string fontFamily = DefaultFamily,
        FontWeight? weight = null,
        double maxWidth = 0,
        int maxLines = 1)
    {
        ArgumentNullException.ThrowIfNull(context);

        Rect normalized = PrimitiveMath.Normalize(bounds);
        double width = maxWidth > 0
            ? Math.Min(maxWidth, normalized.Width)
            : normalized.Width;

        FormattedText formatted = Create(
            text,
            fontSize,
            foreground,
            fontFamily,
            weight,
            width,
            maxLines,
            TextAlignment.Center);

        double x = normalized.X + (normalized.Width - width) / 2;
        double y = normalized.Y + (normalized.Height - formatted.Height) / 2;
        context.DrawText(formatted, new Point(x, y));
    }
}
