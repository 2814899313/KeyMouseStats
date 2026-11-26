using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

/// <summary>圆角矩形：半径来自主题，并夹到短边的一半以内。</summary>
public static class RoundRect
{
    public static double RadiusFor(Rect bounds, ThemeTokens theme, double? radius = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return ClampRadius(bounds, radius ?? theme.Radius);
    }

    public static double ClampRadius(Rect bounds, double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0)
        {
            return 0;
        }

        double max = Math.Min(Math.Abs(bounds.Width), Math.Abs(bounds.Height)) / 2;
        return Math.Min(radius, max);
    }

    public static RoundedRect Shape(Rect bounds, ThemeTokens theme, double? radius = null)
    {
        Rect normalized = PrimitiveMath.Normalize(bounds);
        return new RoundedRect(normalized, RadiusFor(normalized, theme, radius));
    }

    public static RoundedRect Shape(Rect bounds, double radius)
    {
        Rect normalized = PrimitiveMath.Normalize(bounds);
        return new RoundedRect(normalized, ClampRadius(normalized, radius));
    }

    public static void Draw(
        DrawingContext context,
        Rect bounds,
        ThemeTokens theme,
        IBrush? fill,
        IPen? pen,
        double? radius = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DrawRectangle(fill, pen, Shape(bounds, theme, radius), default);
    }
}
