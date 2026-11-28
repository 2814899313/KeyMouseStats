namespace Kms.App.Platform;

/// <summary>
/// 应用筛选磁贴的尺寸映射。时长经过平方根平滑后线性映射到 30..64px，
/// 既让高频应用显著放大，也保证低频应用不小于可点击热区。
/// </summary>
public static class AppTileMetrics
{
    public const double MinSize = 30;
    public const double MaxSize = 64;
    public const double IconRatio = 0.68;

    public static double SizeFor(double seconds, double maxSeconds)
    {
        double max = double.IsFinite(maxSeconds) && maxSeconds > 1 ? maxSeconds : 1;
        double value = double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
        double ratio = Math.Clamp(value / max, 0, 1);
        double size = MinSize + (MaxSize - MinSize) * Math.Sqrt(ratio);
        return Math.Clamp(size, MinSize, MaxSize);
    }

    public static double IconSizeFor(double tileSize)
    {
        double clamped = Math.Clamp(tileSize, MinSize, MaxSize);
        return clamped * IconRatio;
    }
}
