using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

/// <summary>把原语接到真实 DrawingContext，验证 Draw 路径不是只通过编译。</summary>
public sealed class PrimitiveRenderSmokeTests
{
    [Fact]
    public void Card_DrawsPixelsThroughHeadlessSkia()
    {
        ThemeTokens theme = ThemeCatalog.Get(0);
        CardSurface surface = new(theme);

        byte[] png = GoldenHarness.Render(surface, 220, 120);
        byte[] gray = GoldenHarness.Grayscale(png, out int width, out int height);

        Assert.Equal(220, width);
        Assert.Equal(120, height);
        Assert.True(gray.Distinct().Count() > 2, "卡片应至少有背景、边框、内容三种颜色。");
        GoldenHarness.AssertMatches("primitives-card-96", surface, 220, 120);
    }

    [Fact]
    public void HeatGrid_DrawsAllCells()
    {
        ThemeTokens theme = ThemeCatalog.Get(5);
        HeatSurface surface = new(theme);

        byte[] png = GoldenHarness.Render(surface, 160, 80);
        byte[] gray = GoldenHarness.Grayscale(png, out int width, out int height);

        Assert.Equal(160, width);
        Assert.Equal(80, height);
        Assert.True(gray.Distinct().Count() > 5);
        GoldenHarness.AssertMatches("primitives-heat-96", surface, 160, 80);
    }

    private sealed class CardSurface(ThemeTokens theme) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            CardGeometry card = Card.Layout(new Rect(0, 0, 220, 120), theme);
            Card.Draw(context, card, theme);
        }
    }

    private sealed class HeatSurface(ThemeTokens theme) : Control
    {
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            long[,] counts = new long[4, 6];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 6; x++)
                {
                    counts[y, x] = (y + 1) * (x + 2);
                }
            }

            HeatGridGeometry grid = HeatGrid.Measure(new Rect(0, 0, 160, 80), counts, theme);
            HeatGrid.Draw(context, grid);
        }
    }
}
