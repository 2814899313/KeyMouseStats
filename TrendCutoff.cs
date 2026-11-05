using System;
using System.Globalization;

namespace KeyMouseStats
{
    /// <summary>
    /// 「今天还没结束」的处理。时间序列里拿半天数据去比整天,日均会被拉低、环比会砸出 -95%,
    /// 用户第一反应是系统坏了。这里只做两件事:
    ///   1. 环比只比「昨天同一时段」——即两边都只取已完成的整小时;
    ///   2. 明确告诉调用方「今天进行到几点」,由图表在末端标注「进行中」。
    /// </summary>
    internal static class TrendCutoff
    {
        internal sealed class Comparison
        {
            /// <summary>是否有可比的昨日同期数据。</summary>
            public bool Available;
            /// <summary>今天仍在累计(调用时恒为 true;保留给未来跨天补算)。</summary>
            public bool Live;
            /// <summary>已完成的整小时数(0–24)。09:57 → 9,比较窗口为 00:00–09:00。</summary>
            public int Hours;
            /// <summary>比较终点 = 今天 00:00 + Hours。</summary>
            public DateTime Cutoff;
            public double Today, Yesterday;
            public double? Percent;
            /// <summary>不可用原因或补充说明。</summary>
            public string Note = "";
        }

        /// <summary>
        /// metric:0 击键 / 1 点击 / 2 滚轮 / 3 鼠标路程。只有击键与点击有逐小时数据;
        /// 滚轮与路程没有小时维度,不能做同期环比,返回不可用并由页面显示「累计中」。
        /// </summary>
        public static Comparison Compare(DayRecord today, DayRecord yesterday, int metric, DateTime now)
        {
            Comparison comparison = new Comparison();
            comparison.Live = true;
            comparison.Hours = Math.Max(0, Math.Min(24, now.Hour));
            DateTime day = today != null && today.Date != DateTime.MinValue ? today.Date : now.Date;
            comparison.Cutoff = day.AddHours(comparison.Hours);
            if (metric != 0 && metric != 1)
            {
                comparison.Note = "该指标没有逐小时数据";
                return comparison;
            }
            if (comparison.Hours < 1)
            {
                comparison.Note = "今天还没有完整小时";
                return comparison;
            }
            long[] current = today == null ? null : (metric == 0 ? today.HourKeys : today.HourClicks);
            long[] previous = yesterday == null ? null : (metric == 0 ? yesterday.HourKeys : yesterday.HourClicks);
            for (int i = 0; i < comparison.Hours && i < 24; i++)
            {
                if (current != null && i < current.Length) comparison.Today += current[i];
                if (previous != null && i < previous.Length) comparison.Yesterday += previous[i];
            }
            if (yesterday == null)
            {
                comparison.Note = "没有昨天的记录";
                return comparison;
            }
            if (comparison.Yesterday <= 0)
            {
                comparison.Note = comparison.Today > 0 ? "昨日同期无记录" : "昨天同期也没有记录";
                return comparison;
            }
            comparison.Available = true;
            comparison.Percent = (comparison.Today - comparison.Yesterday) * 100.0 / comparison.Yesterday;
            return comparison;
        }

        /// <summary>指标卡上的值:百分比 / 新增 / 累计中 / --。</summary>
        public static string DeltaText(Comparison comparison)
        {
            if (comparison == null) return "--";
            if (!comparison.Available)
            {
                if (comparison.Hours < 1 || comparison.Note == "该指标没有逐小时数据") return "累计中";
                if (comparison.Today > 0) return "新增";
                return "--";
            }
            double percent = comparison.Percent.HasValue ? comparison.Percent.Value : 0;
            return (percent >= 0 ? "+" : "") + percent.ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>指标卡下方的小字:比较窗口 / 昨日同期基数 / 不可用原因。</summary>
        public static string NoteText(Comparison comparison)
        {
            if (comparison == null) return "";
            if (comparison.Available)
                return "截至 " + comparison.Cutoff.ToString("HH:mm") + " · 昨日同期 " + Analysis.FmtCount((long)comparison.Yesterday);
            return comparison.Note;
        }

        /// <summary>图表末端的标注文字,例如「今日截至 09:57 · 累计中」。</summary>
        public static string LiveTag(DateTime now)
        {
            return "今日截至 " + now.ToString("HH:mm") + " · 累计中";
        }
    }
}
