using Avalonia.Media;

namespace Kms.Render.Tokens;

/// <summary>颜色运算。保留 v3 的线性通道插值，不改用 OKLCH。</summary>
public static class ColorMath
{
    public static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Max(0, Math.Min(1, amount));
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount),
            (byte)(a.G + (b.G - a.G) * amount),
            (byte)(a.B + (b.B - a.B) * amount));
    }

    public static Color Composite(Color foreground, int alpha, Color background)
    {
        int a = Math.Max(0, Math.Min(255, alpha));
        return Color.FromRgb(
            (byte)((foreground.R * a + background.R * (255 - a)) / 255),
            (byte)((foreground.G * a + background.G * (255 - a)) / 255),
            (byte)((foreground.B * a + background.B * (255 - a)) / 255));
    }

    /// <summary>WCAG 2.1 相对亮度。</summary>
    public static double RelativeLuminance(Color color) =>
        0.2126 * LinearChannel(color.R) +
        0.7152 * LinearChannel(color.G) +
        0.0722 * LinearChannel(color.B);

    public static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double LinearChannel(byte value)
    {
        double v = value / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}

/// <summary>经典热力色阶（全部点击）与三通道派生色阶。</summary>
public static class HeatScale
{
    public const int All = 0;
    public const int Left = 1;
    public const int Right = 2;
    public const int Middle = 3;

    public static Color At(ThemeTokens theme, double fraction)
    {
        double p = Math.Max(0, Math.Min(1, fraction)) * (theme.HeatStops.Count - 1);
        int index = Math.Min(theme.HeatStops.Count - 2, (int)p);
        return ColorMath.Mix(theme.HeatStops[index], theme.HeatStops[index + 1], p - index);
    }

    public static Color Zero(ThemeTokens theme) => ColorMath.Mix(theme.Card, theme.Text, 0.12);

    public static Color ChannelAccent(ThemeTokens theme, int channel) => channel switch
    {
        Left => theme.Cyan,
        Right => theme.Orange,
        Middle => theme.Purple,
        _ => theme.Accent
    };

    public static Color ChannelAt(ThemeTokens theme, int channel, double fraction)
    {
        if (channel <= All)
        {
            return At(theme, fraction);
        }

        Color accent = ChannelAccent(theme, channel);
        double p = Math.Max(0, Math.Min(1, fraction));
        if (p < 0.5)
        {
            Color dark = ColorMath.Mix(theme.Card, accent, 0.20);
            return ColorMath.Mix(dark, accent, p / 0.5);
        }

        return ColorMath.Mix(accent, theme.Text, (p - 0.5) / 0.5 * 0.34);
    }

    public static Color For(ThemeTokens theme, int channel, long count, long max)
    {
        if (count <= 0 || max <= 0)
        {
            return Zero(theme);
        }

        double fraction = Math.Log(1.0 + count) / Math.Log(1.0 + max);
        return ChannelAt(theme, channel, Math.Pow(Math.Min(1, fraction), 0.85));
    }
}
