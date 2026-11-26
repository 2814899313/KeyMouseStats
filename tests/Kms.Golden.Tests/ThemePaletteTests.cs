using Avalonia.Media;
using Kms.App.Theming;
using Kms.Render.Theming;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

public sealed class ThemePaletteTests
{
    [Fact]
    public void Build_ProducesReadableCustomPalette()
    {
        ThemeTokens baseTheme = ThemeCatalog.Get(0);
        ThemeTokens built = ThemePaletteBuilder.Build(
            "测试皮肤",
            Color.FromRgb(0x07, 0x0B, 0x19),
            Color.FromRgb(0x2D, 0x89, 0x99),
            Color.FromRgb(0xE9, 0xF1, 0xF7),
            baseTheme);

        Assert.Equal("测试皮肤", built.Name);
        Assert.True(Contrast(built.Text, built.Background) >= 12.0);
        Assert.True(Contrast(built.Accent, built.Background) >= 4.4);
        Assert.True(Contrast(built.Muted, built.Background) >= 4.4);
        Assert.NotEqual(built.Background, built.Card);
        Assert.NotEqual(built.Card, built.Raised);
    }

    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value)
        {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        double la = 0.2126 * Linear(a.R) + 0.7152 * Linear(a.G) + 0.0722 * Linear(a.B);
        double lb = 0.2126 * Linear(b.R) + 0.7152 * Linear(b.G) + 0.0722 * Linear(b.B);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
