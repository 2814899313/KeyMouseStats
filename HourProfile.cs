using System;
using System.Collections.Generic;

namespace KeyMouseStats
{
    /// <summary>
    /// 「周 × 24 小时」作息矩阵的聚合:按星期几(周一=0)与小时求「有记录日的平均值」。
    /// 不把没开机的日期与今天的半天算进分母,否则周末/工作日的对比会被稀释。
    /// </summary>
    internal static class HourProfile
    {
        internal sealed class Result
        {
            /// <summary>[7,24] 每个星期几每个小时的平均值。</summary>
            public readonly double[,] Average = new double[7, 24];
            /// <summary>每个星期几参与统计的有记录日数。</summary>
            public readonly int[] Days = new int[7];
            public int TotalDays;
            public double Max;
            public bool HasData { get { return TotalDays > 0; } }
        }

        /// <summary>metric:0 击键 / 1 点击 / 2 活跃分钟。默认统计 [今天-daysBack, 昨天],不含今天。</summary>
        public static Result Build(IEnumerable<DayRecord> history, int metric, DateTime today, int daysBack)
        {
            if (daysBack < 1) daysBack = 1;
            return BuildRange(history, metric, today.AddDays(-daysBack), today.AddDays(-1));
        }

        /// <summary>统计 [firstDate, lastDate] 闭区间内、有落盘记录的日期;适合「今日 / 近 7 天 / 全部历史」。</summary>
        public static Result BuildRange(IEnumerable<DayRecord> history, int metric, DateTime firstDate, DateTime lastDate)
        {
            Result result = new Result();
            if (history == null) return result;
            DateTime first = firstDate.Date, last = lastDate.Date;
            if (last < first) return result;
            double[,] sums = new double[7, 24];
            foreach (DayRecord day in history)
            {
                if (day == null) continue;
                if (day.Date < first || day.Date > last) continue;
                int weekday = ((int)day.Date.DayOfWeek + 6) % 7;   // 周一=0 … 周日=6
                result.Days[weekday]++;
                result.TotalDays++;
                for (int h = 0; h < 24; h++)
                {
                    double value = metric == 0 ? day.HourKeys[h] : metric == 1 ? day.HourClicks[h]
                        : metric == 2 ? day.ActiveHours[h] / 60.0 : 0;
                    sums[weekday, h] += value;
                }
            }
            for (int weekday = 0; weekday < 7; weekday++)
            {
                int days = result.Days[weekday];
                for (int h = 0; h < 24; h++)
                {
                    double average = days > 0 ? sums[weekday, h] / days : 0;
                    result.Average[weekday, h] = average;
                    if (average > result.Max) result.Max = average;
                }
            }
            return result;
        }
    }
}
