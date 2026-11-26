using Kms.Render.Golden;

namespace Kms.Golden.Tests;

public sealed class ImageMetricsTests
{
    [Fact]
    public void Ssim_IdenticalIsOne()
    {
        byte[] pixels = [10, 20, 30, 40, 50, 60];

        Assert.Equal(1, ImageMetrics.Ssim(pixels, pixels, 3, 2), 9);
    }

    [Fact]
    public void Ssim_InvertedIsLow()
    {
        byte[] a = [0, 0, 255, 255];
        byte[] b = [255, 255, 0, 0];

        Assert.True(ImageMetrics.Ssim(a, b, 2, 2) < 0.1);
    }

    [Fact]
    public void MaxChannelDelta_ReportsLargestDifference()
    {
        byte[] a = [0, 128, 255];
        byte[] b = [1, 130, 200];

        Assert.Equal(55, ImageMetrics.MaxChannelDelta(a, b));
    }

    [Fact]
    public void DeltaE_IdenticalIsZero()
    {
        Assert.Equal(0, ImageMetrics.DeltaE(18, 32, 46, 18, 32, 46), 9);
    }

    [Fact]
    public void DeltaE_BlackToWhiteIsAboutOneHundred()
    {
        Assert.InRange(ImageMetrics.DeltaE(0, 0, 0, 255, 255, 255), 99, 101);
    }

    [Fact]
    public void Ssim_RejectsMismatchedBuffer()
    {
        Assert.Throws<ArgumentException>(() => ImageMetrics.Ssim([1, 2], [1], 2, 1));
    }
}
