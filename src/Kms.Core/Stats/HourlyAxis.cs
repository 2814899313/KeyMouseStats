using System.Globalization;

namespace Kms.Core.Stats;

/// <summary>
/// 小时趋势的纵轴。活跃时长是「每个小时里活跃了多少分钟」，物理上限就是 60：
/// 不能再按「最大值 × 1.15」放大到 70，也不必给顶端留余量。刻度固定 0/15/30/45/60。
/// 其他指标继续走 <see cref="AxisScale"/> 的整值步长。
/// </summary>
public static class HourlyAxis
{
    public const double MaxActiveMinutes = 60;

    public static void Compute(int metric, double axisMax, out double top, out double step)
    {
        if (metric == 2)
        {
            top = MaxActiveMinutes;
            step = MaxActiveMinutes / 4;
            return;
        }

        // metric 3/4 都是「每分钟」的速率，允许小数步长；其余是次数，用整数步长。
        AxisScale.Nice(axisMax, 4, metric < 3, out top, out step);
    }

    public static string Label(int metric, double value)
    {
        if (metric == 2)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        if (metric >= 3)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return AxisScale.Label(value);
    }

    /// <summary>指标单位。击键/点击速率统一按分钟，不再一个按秒、一个按分。</summary>
    public static string Unit(int metric)
    {
        if (metric == 2)
        {
            return " 分钟";
        }

        if (metric >= 3)
        {
            return " 次/分";
        }

        return " 次";
    }

    /// <summary>绘制时把活跃分钟数夹到 <paramref name="top"/>，避免历史异常值把折线画到封顶线上方。</summary>
    public static double Clamp(int metric, double value, double top) =>
        metric == 2 ? Math.Min(value, top) : value;
}
