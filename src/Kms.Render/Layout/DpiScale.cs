namespace Kms.Render.Layout;

/// <summary>DPI 与 DIP 的换算。Avalonia 的绘制坐标是 DIP，像素对齐只在需要时显式做。</summary>
public static class DpiScale
{
    public const double BaseDpi = 96.0;

    public static double FromDpi(double dpi) =>
        dpi <= 0 ? 1 : Math.Max(1, dpi) / BaseDpi;

    public static double ToPixels(double dip, double scale) => dip * scale;

    public static double ToDip(double pixels, double scale) =>
        scale <= 0 ? pixels : pixels / scale;

    /// <summary>把 DIP 对齐到物理像素中心；scale=1 时等价于四舍五入。</summary>
    public static double Snap(double dip, double scale)
    {
        if (scale <= 0)
        {
            scale = 1;
        }

        return Math.Round(dip * scale, MidpointRounding.AwayFromZero) / scale;
    }

    public static double SnapStroke(double dip, double scale) =>
        (Math.Round(dip * scale, MidpointRounding.AwayFromZero) + 0.5) / scale;
}
