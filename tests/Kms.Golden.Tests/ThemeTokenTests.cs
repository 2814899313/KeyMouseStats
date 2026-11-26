using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

public sealed class ThemeTokenTests
{
    [Fact]
    public void Catalog_HasThirteenContiguousThemes()
    {
        Assert.Equal(13, ThemeCatalog.All.Count);
        for (int i = 0; i < ThemeCatalog.All.Count; i++)
        {
            Assert.Equal(i, ThemeCatalog.All[i].Id);
        }
    }

    [Fact]
    public void Radii_KeepIntentionalOddities()
    {
        double[] expected = [13, 4, 13, 5, 0, 18, 6, 5, 2, 7, 5, 7, 7];

        Assert.Equal(expected, ThemeCatalog.All.Select(theme => theme.Radius));
    }

    [Fact]
    public void MidnightBlue_MatchesV3Colors()
    {
        ThemeTokens theme = ThemeCatalog.Get(0);

        Assert.Equal("午夜蓝", theme.Name);
        Assert.Equal("#0F141E", Hex(theme.Background));
        Assert.Equal("#729BFF", Hex(theme.Accent));
        Assert.Equal("#101622", Hex(theme.OnAccent));
        Assert.Equal("#0C111A", Hex(theme.Sidebar));
        Assert.Equal(5, theme.HeatStops.Count);
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    [Theory]
    [InlineData(2, "奥日 · 灵光森林")]
    [InlineData(4, "蓝图工坊")]
    [InlineData(5, "暖暖 · 星愿织梦")]
    [InlineData(12, "古剑奇谭 · 琴心剑魄")]
    public void Names_MatchV3(int id, string name)
    {
        Assert.Equal(name, ThemeCatalog.Get(id).Name);
    }

    [Fact]
    public void Mix_IsLinearChannelInterpolation()
    {
        Color mixed = ColorMath.Mix(Color.Parse("#000000"), Color.Parse("#FFFFFF"), 0.5);

        Assert.Equal((byte)127, mixed.R);
        Assert.Equal((byte)127, mixed.G);
        Assert.Equal((byte)127, mixed.B);
    }

    [Fact]
    public void Contrast_BlackOnWhiteIsTwentyOne()
    {
        double ratio = ColorMath.ContrastRatio(Colors.Black, Colors.White);

        Assert.Equal(21, ratio, 6);
    }

    [Fact]
    public void HeatScale_EndpointsAreTableStops()
    {
        ThemeTokens theme = ThemeCatalog.Get(0);

        Assert.Equal(theme.HeatStops[0], HeatScale.At(theme, 0));
        Assert.Equal(theme.HeatStops[^1], HeatScale.At(theme, 1));
        Assert.Equal(ColorMath.Mix(theme.Card, theme.Text, 0.12), HeatScale.Zero(theme));
    }

    [Theory]
    [InlineData(1, 0x4C, 0xC9, 0xF0)]
    [InlineData(2, 0xF1, 0xBB, 0x6F)]
    [InlineData(3, 0xB1, 0x9D, 0xF8)]
    [InlineData(0, 0x72, 0x9B, 0xFF)]
    public void ChannelAccent_MatchesV3(int channel, byte r, byte g, byte b)
    {
        Color accent = HeatScale.ChannelAccent(ThemeCatalog.Get(0), channel);

        Assert.Equal(r, accent.R);
        Assert.Equal(g, accent.G);
        Assert.Equal(b, accent.B);
    }
}
