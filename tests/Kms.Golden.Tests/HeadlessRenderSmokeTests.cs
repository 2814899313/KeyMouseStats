using Avalonia.Controls;
using Avalonia.Media;
using Kms.Render.Golden;

namespace Kms.Golden.Tests;

/// <summary>验证 headless + Skia 能离屏出图，后续 golden 基线复用这条管线。</summary>
public sealed class HeadlessRenderSmokeTests
{
    [Fact]
    public void RenderTargetBitmap_ProducesComparablePng()
    {
        Border border = new()
        {
            Width = 64,
            Height = 32,
            Background = Brushes.Red
        };

        byte[] first = GoldenHarness.Render(border, 64, 32);
        byte[] second = GoldenHarness.Render(border, 64, 32);

        Assert.True(first.Length > 0);
        Assert.Equal(1, GoldenHarness.Ssim(first, second), 9);

        byte[] gray = GoldenHarness.Grayscale(first, out int width, out int height);
        Assert.Equal(64, width);
        Assert.Equal(32, height);
        Assert.True(gray[0] > 0);
        Assert.True(ImageMetrics.MaxChannelDelta(gray, gray) == 0);
    }

    [Theory]
    [InlineData(96, 64, 32)]
    [InlineData(144, 96, 48)]
    [InlineData(192, 128, 64)]
    public void Render_ScalesPixelsWithDpi(double dpi, int expectedWidth, int expectedHeight)
    {
        Border border = new() { Width = 64, Height = 32, Background = Brushes.Red };

        byte[] png = GoldenHarness.Render(border, 64, 32, dpi);
        GoldenHarness.Grayscale(png, out int width, out int height);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }
}
