namespace Kms.Core.Stats;

using Kms.Core.Model;

/// <summary>跨页面复用的个人统计口径。</summary>
public static class PersonalStats
{
    public static double Ratio(double numerator, double denominator) =>
        denominator > 0 ? numerator / denominator : double.NaN;

    public static long Combos(DayRecord day)
    {
        long total = 0;
        foreach (long value in day.ComboCounts.Values)
        {
            total += value;
        }

        return total;
    }
}
