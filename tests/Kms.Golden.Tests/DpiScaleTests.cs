using Kms.Render.Layout;

namespace Kms.Golden.Tests;

public sealed class DpiScaleTests
{
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2.0)]
    [InlineData(0, 1.0)]
    public void FromDpi_MatchesCommonScales(double dpi, double expected)
    {
        Assert.Equal(expected, DpiScale.FromDpi(dpi), 9);
    }

    [Fact]
    public void Snap_RoundsToPhysicalPixel()
    {
        Assert.Equal(2.0 / 1.5, DpiScale.Snap(1.4, 1.5), 9);
        Assert.Equal(1.0, DpiScale.Snap(1.3, 1.0), 9);
    }

    [Fact]
    public void SnapStroke_PutsLineOnHalfPixelCenter()
    {
        Assert.Equal(2.5 / 1.5, DpiScale.SnapStroke(1.4, 1.5), 9);
        Assert.Equal(1.5, DpiScale.SnapStroke(1.2, 1.0), 9);
    }

    [Fact]
    public void ToDipAndBack_RoundTrips()
    {
        Assert.Equal(12.5, DpiScale.ToDip(DpiScale.ToPixels(12.5, 1.5), 1.5), 9);
    }
}
