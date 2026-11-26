using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

/// <summary>卡片几何：外框、圆角表面、内边距后的内容区。</summary>
public readonly record struct CardGeometry(
    Rect Bounds,
    RoundedRect Surface,
    Rect Content,
    double Radius);

/// <summary>卡片底板 + 边框，以及按行/列切分内容区的纯几何函数。</summary>
public static class Card
{
    public static CardGeometry Layout(
        Rect bounds,
        ThemeTokens theme,
        double padding = 14,
        double? radius = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        Rect normalized = PrimitiveMath.Normalize(bounds);
        double r = RoundRect.RadiusFor(normalized, theme, radius);
        Rect content = PrimitiveMath.Inset(normalized, padding);

        return new CardGeometry(
            normalized,
            new RoundedRect(normalized, r, r),
            content,
            r);
    }

    public static IReadOnlyList<Rect> SplitRows(Rect bounds, int count, double gap = 8) =>
        Split(bounds, count, gap, rows: true);

    public static IReadOnlyList<Rect> SplitColumns(Rect bounds, int count, double gap = 8) =>
        Split(bounds, count, gap, rows: false);

    public static void Draw(
        DrawingContext context,
        CardGeometry card,
        ThemeTokens theme,
        bool raised = false,
        IPen? border = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(theme);

        IBrush fill = new SolidColorBrush(raised ? theme.Raised : theme.Card);
        IPen pen = border ?? new Pen(new SolidColorBrush(theme.Line), 1);
        context.DrawRectangle(fill, pen, card.Surface, default);
    }

    private static IReadOnlyList<Rect> Split(Rect bounds, int count, double gap, bool rows)
    {
        if (count <= 0)
        {
            return [];
        }

        Rect normalized = PrimitiveMath.Normalize(bounds);
        double spacing = Math.Max(0, PrimitiveMath.Finite(gap));
        double length = rows ? normalized.Height : normalized.Width;
        double totalGap = spacing * (count - 1);
        double size = Math.Max(0, (length - totalGap) / count);

        Rect[] result = new Rect[count];
        for (int i = 0; i < count; i++)
        {
            double offset = i * (size + spacing);
            result[i] = rows
                ? new Rect(normalized.X, normalized.Y + offset, normalized.Width, size)
                : new Rect(normalized.X + offset, normalized.Y, size, normalized.Height);
        }

        return result;
    }
}
