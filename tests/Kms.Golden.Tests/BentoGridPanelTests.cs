using Avalonia;
using Kms.App.Widgets;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class BentoGridPanelTests
{
    [Fact]
    public void WidgetSpan_ParsesGridNotation()
    {
        Assert.Equal(new WidgetSpan(3, 1), WidgetSpan.Parse("3x1"));
        Assert.Equal(new WidgetSpan(6, 4), WidgetSpan.Parse(" 6×4 "));
        Assert.Throws<FormatException>(() => WidgetSpan.Parse("3"));
        Assert.Throws<FormatException>(() => WidgetSpan.Parse("0x1"));
    }

    [Fact]
    public void ComputeCell_EvenlyDividesColumnsAndRows()
    {
        Size viewport = new(1200, 600);
        const double spacing = 12;

        Rect first = BentoGridMath.ComputeCell(viewport, 12, 6, spacing, 0, 0, 1, 1);
        Rect last = BentoGridMath.ComputeCell(viewport, 12, 6, spacing, 11, 5, 1, 1);

        double cellWidth = (1200 - 11 * spacing) / 12.0;
        double cellHeight = (600 - 5 * spacing) / 6.0;
        Assert.Equal(0, first.X, 6);
        Assert.Equal(0, first.Y, 6);
        Assert.Equal(cellWidth, first.Width, 6);
        Assert.Equal(cellHeight, first.Height, 6);
        Assert.Equal(1200, last.Right, 6);
        Assert.Equal(600, last.Bottom, 6);
        Assert.Equal(cellWidth, last.Width, 6);
        Assert.Equal(cellHeight, last.Height, 6);
    }

    [Fact]
    public void ComputeCell_RespectsSpans()
    {
        Size viewport = new(1200, 600);
        Rect spanned = BentoGridMath.ComputeCell(viewport, 12, 6, 12, 0, 0, 6, 3);

        double cellWidth = (1200 - 11 * 12) / 12.0;
        double cellHeight = (600 - 5 * 12) / 6.0;
        Assert.Equal(6 * cellWidth + 5 * 12, spanned.Width, 6);
        Assert.Equal(3 * cellHeight + 2 * 12, spanned.Height, 6);
    }

    [Fact]
    public void ComputeCell_ClampsOutOfBoundsPlacement()
    {
        Size viewport = new(1200, 600);
        Rect clamped = BentoGridMath.ComputeCell(viewport, 12, 6, 12, 20, 20, 99, 99);

        Assert.Equal(viewport.Width, clamped.Right, 6);
        Assert.Equal(viewport.Height, clamped.Bottom, 6);
        Assert.Equal((1200 - 11 * 12) / 12.0, clamped.Width, 6);
        Assert.Equal((600 - 5 * 12) / 6.0, clamped.Height, 6);
    }

    [Fact]
    public void ComputeCell_NeverExceedsViewport()
    {
        Size[] viewports =
        [
            new(928, 632),
            new(720, 491),
            new(1500, 950),
            new(1237.5, 711.25),
            new(320, 240)
        ];

        foreach (Size viewport in viewports)
        {
            for (int col = -2; col < 16; col += 3)
            {
                for (int row = -2; row < 10; row += 2)
                {
                    Rect cell = BentoGridMath.ComputeCell(viewport, 12, 6, 12, col, row, 6, 4);
                    Assert.True(cell.X >= -1e-6, $"{viewport} x={cell.X}");
                    Assert.True(cell.Y >= -1e-6, $"{viewport} y={cell.Y}");
                    Assert.True(cell.Width >= -1e-6, $"{viewport} w={cell.Width}");
                    Assert.True(cell.Height >= -1e-6, $"{viewport} h={cell.Height}");
                    Assert.True(cell.Right <= viewport.Width + 1e-6,
                        $"{viewport} right={cell.Right}");
                    Assert.True(cell.Bottom <= viewport.Height + 1e-6,
                        $"{viewport} bottom={cell.Bottom}");
                }
            }
        }
    }
}
