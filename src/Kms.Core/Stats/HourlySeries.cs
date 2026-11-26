using Kms.Core.Model;

namespace Kms.Core.Stats;

/// <summary>
/// Hours 页的小时序列口径，对应 v3 <c>Dashboard.HourlyValues</c> /
/// <c>HourlyComparisonDate</c> / <c>HourlyComparisonDates</c>。
/// 只做纯数据，不涉及任何绘制或平台类型。
/// </summary>
public static class HourlySeries
{
    /// <summary>
    /// 是否有时间维度。与 v3 <c>TimeInsights.HasTime</c> 一致：
    /// 日总活跃/空闲秒数或会话列表任一存在即为真，**不看小时桶**。
    /// </summary>
    public static bool HasTime(DayRecord? day) =>
        day is not null && (day.ActiveSeconds > 0 || day.IdleSeconds > 0 || day.Sessions.Count > 0);

    /// <summary>
    /// 取所选日 24 个小时的指标值。未来小时（含今天尚未走到的小时）为 <see cref="double.NaN"/>，
    /// 无记录日与缺少时间维度的速率指标同样整列为 NaN，而不是补 0。
    /// </summary>
    /// <param name="day">所选日记录，可为 null。</param>
    /// <param name="date">所选日（只取日期部分；不看 <paramref name="day"/>.Date）。</param>
    /// <param name="metric">0 击键 / 1 点击 / 2 活跃分钟 / 3 击键每分钟 / 4 点击每分钟。</param>
    /// <param name="now">当前本地时间，用于裁掉未来小时。</param>
    public static double[] Values(DayRecord? day, DateTime date, int metric, DateTime now)
    {
        double[] values = new double[24];
        if (day is null || (metric >= 2 && !HasTime(day)))
        {
            Array.Fill(values, double.NaN);
            return values;
        }

        DateTime selected = date.Date;
        DateTime today = now.Date;
        for (int hour = 0; hour < 24; hour++)
        {
            if (selected > today || (selected == today && hour > now.Hour))
            {
                values[hour] = double.NaN;
                continue;
            }

            values[hour] = metric == 0 ? day.HourKeys[hour]
                : metric == 1 ? day.HourClicks[hour]
                : metric == 2 ? day.ActiveHours[hour] / 60
                : PersonalStats.Ratio(
                    (metric == 3 ? day.HourKeys[hour] : day.HourClicks[hour]) * 60.0,
                    day.ActiveHours[hour]);
        }

        return values;
    }

    /// <summary>「近 N 天」只取一条对比曲线时使用的日期（至少往前 1 天）。</summary>
    public static DateTime ComparisonDate(DateTime selected, int compareDays) =>
        selected.Date.AddDays(-Math.Max(1, compareDays));

    /// <summary>
    /// 对比曲线的日期列表。mode：0 不对比 / 1 昨天 / 2 上周同日 /
    /// 3 近 N 天（不含所选日）/ 4 本周 7 天（周一为一周起始，不含所选日）。
    /// </summary>
    public static DateTime[] ComparisonDates(DateTime selected, int mode, int days)
    {
        List<DateTime> result = new();
        if (mode == 1)
        {
            result.Add(selected.Date.AddDays(-1));
        }
        else if (mode == 2)
        {
            result.Add(selected.Date.AddDays(-7));
        }
        else if (mode == 3)
        {
            for (int day = 1; day < Math.Max(2, Math.Min(14, days)); day++)
            {
                result.Add(selected.Date.AddDays(-day));
            }
        }
        else if (mode == 4)
        {
            int offset = ((int)selected.DayOfWeek + 6) % 7; // 周一=0 … 周日=6
            DateTime monday = selected.Date.AddDays(-offset);
            for (int day = 0; day < 7; day++)
            {
                if (monday.AddDays(day) != selected.Date)
                {
                    result.Add(monday.AddDays(day));
                }
            }
        }

        return result.ToArray();
    }
}
