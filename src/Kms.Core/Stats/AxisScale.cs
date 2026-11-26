using System.Globalization;

namespace Kms.Core.Stats;

/// <summary>
/// 坐标轴整数刻度。先把步长对齐到 1/1.5/2/2.5/3/4/5/6/8/10 × 10ⁿ，
/// 再取整倍数作为顶刻度；避免 3.61k / 7.22k 这种刻度。
/// </summary>
public static class AxisScale
{
    private static readonly double[] Fractions = [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];

    public static void Nice(double rawMax, int divisions, out double top, out double step) =>
        Nice(rawMax, divisions, integerTicks: true, out top, out step);

    public static void Nice(
        double rawMax,
        int divisions,
        bool integerTicks,
        out double top,
        out double step)
    {
        if (divisions < 1)
        {
            divisions = 1;
        }

        if (double.IsNaN(rawMax) || double.IsInfinity(rawMax) || rawMax <= 0)
        {
            step = 1;
            top = divisions;
            return;
        }

        double rough = rawMax * 1.05 / divisions;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double normalized = rough / magnitude;
        double fraction = 10;
        foreach (double candidate in Fractions)
        {
            if (normalized <= candidate + 1e-9)
            {
                fraction = candidate;
                break;
            }
        }

        step = fraction * magnitude;
        if (integerTicks && step < 1)
        {
            step = 1;
        }

        top = step * divisions;
    }

    public static string Label(double value)
    {
        if (value >= 1e6)
        {
            return (value / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        }

        if (value >= 1000)
        {
            return (value / 1000).ToString("0.##", CultureInfo.InvariantCulture) + "k";
        }

        return value.ToString("0", CultureInfo.InvariantCulture);
    }
}
