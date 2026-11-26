using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.App.Theming;

/// <summary>从 V3 自定义壁纸的背景/强调/文字色生成一整套协调的深色令牌。</summary>
internal static class ThemePaletteBuilder
{
    public static ThemeTokens Build(string name, Color background, Color accent, Color text, ThemeTokens baseTheme)
    {
        background = Darken(background);
        accent = EnsureContrast(accent, background, 4.5);
        text = EnsureContrast(text, background, 13.0);

        Color card = Mix(background, accent, 0.085);
        if (Luminance(card) <= Luminance(background) + 0.004)
        {
            card = Mix(background, Colors.White, 0.07);
        }

        Color raised = Mix(card, accent, 0.10);
        if (Luminance(raised) <= Luminance(card))
        {
            raised = Mix(card, Colors.White, 0.06);
        }

        Color line = Mix(background, text, 0.24);
        Color muted = EnsureContrast(Mix(background, text, 0.62), background, 4.5);
        Color green = EnsureContrast(Mix(accent, Color.FromRgb(75, 210, 168), 0.72), background, 3.2);
        Color orange = EnsureContrast(Mix(accent, Color.FromRgb(241, 187, 111), 0.72), background, 3.2);
        Color purple = EnsureContrast(Mix(accent, Color.FromRgb(177, 157, 248), 0.72), background, 3.2);
        Color red = EnsureContrast(Mix(accent, Color.FromRgb(255, 107, 129), 0.72), background, 3.2);
        Color cyan = EnsureContrast(Mix(accent, Color.FromRgb(76, 201, 240), 0.72), background, 3.2);
        Color sidebar = Mix(background, Colors.Black, 0.35);
        Color onAccent = Contrast(accent, Colors.White) >= 4.5 ? Colors.White : Color.FromRgb(10, 14, 20);

        return new ThemeTokens(
            100,
            string.IsNullOrWhiteSpace(name) ? "自定义壁纸" : name,
            10,
            background,
            card,
            raised,
            line,
            text,
            muted,
            accent,
            onAccent,
            green,
            orange,
            purple,
            red,
            cyan,
            sidebar,
            baseTheme.HeatStops);
    }

    private static Color Darken(Color color)
    {
        for (int i = 0; i < 16 && Contrast(Colors.White, color) < 13.5; i++)
        {
            color = Mix(color, Colors.Black, 0.18);
        }

        if (Math.Max(color.R, Math.Max(color.G, color.B)) < 18)
        {
            color = Mix(color, Color.FromRgb(22, 27, 36), 0.45);
        }

        return color;
    }

    private static Color EnsureContrast(Color color, Color background, double minimum)
    {
        for (int i = 0; i < 16 && Contrast(color, background) < minimum; i++)
        {
            color = Mix(color, Colors.White, 0.10);
        }

        return color;
    }

    private static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * amount),
            (byte)Math.Round(a.G + (b.G - a.G) * amount),
            (byte)Math.Round(a.B + (b.B - a.B) * amount));
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte value)
    {
        double v = value / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
