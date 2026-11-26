namespace Kms.Render.Golden;

/// <summary>
/// Golden 比对的无依赖实现。只吃灰度/颜色值，不读文件、不依赖渲染后端。
/// 方案要求：SSIM ≥ 0.98、关键元素包围盒 ±2 DIP、色板 ΔE ≤ 2。
/// </summary>
public static class ImageMetrics
{
    /// <summary>全局 SSIM（单窗口）。identical 返回 1。</summary>
    public static double Ssim(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int width, int height)
    {
        if (width <= 0 || height <= 0 || a.Length < width * height || b.Length < width * height)
        {
            throw new ArgumentException("图像尺寸与缓冲区不匹配。");
        }

        int count = width * height;
        double meanA = 0;
        double meanB = 0;
        for (int i = 0; i < count; i++)
        {
            meanA += a[i];
            meanB += b[i];
        }

        meanA /= count;
        meanB /= count;

        double varianceA = 0;
        double varianceB = 0;
        double covariance = 0;
        for (int i = 0; i < count; i++)
        {
            double da = a[i] - meanA;
            double db = b[i] - meanB;
            varianceA += da * da;
            varianceB += db * db;
            covariance += da * db;
        }

        varianceA /= count;
        varianceB /= count;
        covariance /= count;

        const double c1 = 6.5025;   // (0.01 * 255)^2
        const double c2 = 58.5225;  // (0.03 * 255)^2
        return ((2 * meanA * meanB + c1) * (2 * covariance + c2)) /
               ((meanA * meanA + meanB * meanB + c1) * (varianceA + varianceB + c2));
    }

    public static int MaxChannelDelta(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("缓冲区长度不一致。");
        }

        int max = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int delta = Math.Abs(a[i] - b[i]);
            if (delta > max)
            {
                max = delta;
            }
        }

        return max;
    }

    /// <summary>CIE76 ΔE；输入为 sRGB 0..255。</summary>
    public static double DeltaE(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        (double l1, double a1, double bb1) = ToLab(r1, g1, b1);
        (double l2, double a2, double bb2) = ToLab(r2, g2, b2);
        double dl = l1 - l2;
        double da = a1 - a2;
        double db = bb1 - bb2;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    private static (double L, double A, double B) ToLab(byte r, byte g, byte b)
    {
        (double x, double y, double z) = ToXyz(r, g, b);
        double xn = x / 0.95047;
        double yn = y / 1.00000;
        double zn = z / 1.08883;
        double fx = LabF(xn);
        double fy = LabF(yn);
        double fz = LabF(zn);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static (double X, double Y, double Z) ToXyz(byte r, byte g, byte b)
    {
        double rl = Linear(r / 255.0);
        double gl = Linear(g / 255.0);
        double bl = Linear(b / 255.0);
        return (rl * 0.4124 + gl * 0.3576 + bl * 0.1805,
                rl * 0.2126 + gl * 0.7152 + bl * 0.0722,
                rl * 0.0193 + gl * 0.1192 + bl * 0.9505);
    }

    private static double Linear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double LabF(double value) =>
        value > 0.008856 ? Math.Cbrt(value) : (7.787 * value) + 16.0 / 116.0;
}
