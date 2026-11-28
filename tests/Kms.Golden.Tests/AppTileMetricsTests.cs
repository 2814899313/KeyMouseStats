using Kms.App.Platform;

namespace Kms.Golden.Tests;

/// <summary>应用筛选磁贴：平方根时长映射与 30..64px 夹紧。</summary>
public sealed class AppTileMetricsTests
{
    [Fact]
    public void SizeFor_ClampsZeroAndExtremeDurations()
    {
        Assert.Equal(AppTileMetrics.MinSize, AppTileMetrics.SizeFor(0, 0));
        Assert.Equal(AppTileMetrics.MinSize, AppTileMetrics.SizeFor(-10, 1000 * 3600));
        Assert.Equal(AppTileMetrics.MaxSize, AppTileMetrics.SizeFor(1000 * 3600, 1000 * 3600));
        Assert.Equal(AppTileMetrics.MaxSize, AppTileMetrics.SizeFor(double.MaxValue, 1000 * 3600));
        Assert.Equal(AppTileMetrics.MinSize, AppTileMetrics.SizeFor(double.NaN, 1000 * 3600));
    }

    [Fact]
    public void SizeFor_UsesSquareRootCurve()
    {
        double half = AppTileMetrics.SizeFor(500 * 3600, 1000 * 3600);
        double expected = AppTileMetrics.MinSize
            + (AppTileMetrics.MaxSize - AppTileMetrics.MinSize) * Math.Sqrt(0.5);
        Assert.Equal(expected, half, 9);
        Assert.InRange(half, AppTileMetrics.MinSize, AppTileMetrics.MaxSize);

        double quarter = AppTileMetrics.SizeFor(250 * 3600, 1000 * 3600);
        Assert.True(quarter < half);
    }

    [Fact]
    public void IconSizeFor_StaysAtSixtyEightPercentAndClamps()
    {
        Assert.Equal(64 * 0.68, AppTileMetrics.IconSizeFor(64), 9);
        Assert.Equal(30 * 0.68, AppTileMetrics.IconSizeFor(30), 9);
        Assert.Equal(64 * 0.68, AppTileMetrics.IconSizeFor(100), 9);
        Assert.Equal(30 * 0.68, AppTileMetrics.IconSizeFor(1), 9);
    }
}
