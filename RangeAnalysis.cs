// ============================================================================
//  键鼠统计 - 区间分析  RangeAnalysis.cs
//
//  1.7.0 引入的处理层:区间聚合、分布、趋势与对比只在这里定义一次,
//  页面只负责呈现,不再各算一套口径。
//
//  口径规则(全项目统一):
//    · 区间终点不超过「最近一个已结束的日」,今日尚未结束,不计入完整区间。
//    · 缺失日(无记录 / 空记录)不补零,只统计有效天数。
//    · 日均、均值一律按有效天数计算,不用区间天数当分母。
//    · 基准为 0 或样本不足时返回 NaN,由呈现层显示「基准不足」,不伪造 0。
//    · 旧数据不反推:没有对应观测的字段保持为空。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeyMouseStats
{
    /// <summary>区间口径规则。</summary>
    internal static class RangeRules
    {
        /// <summary>今日尚未结束,不能与完整日直接比较;历史日期本身即完整日。</summary>
        public static DateTime CompleteEnd(DateTime date)
        {
            return date.Date >= DateTime.Today ? DateTime.Today.AddDays(-1) : date.Date;
        }

        /// <summary>
        /// 规范化区间:终点不超过最近一个完整日。
        /// 返回 false 表示规范化后没有任何完整日,此时 to 会早于 from。
        /// </summary>
        public static bool TryNormalize(DateTime start, DateTime end, out DateTime from, out DateTime to)
        {
            from = start.Date;
            to = CompleteEnd(end);
            return to >= from;
        }

        public static int WeekdayIndex(DateTime date)
        {
            return ((int)date.DayOfWeek + 6) % 7;
        }

        public static string Weekday(int index)
        {
            return new string[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }[index];
        }

        public static string CoverageText(int observed, int days)
        {
            return observed + " / " + days + " 天";
        }
    }

    /// <summary>基础统计量。全项目只保留这一份实现。</summary>
    internal static class RangeMath
    {
        public static double Mean(IList<double> values)
        {
            if (values == null || values.Count == 0) return double.NaN;
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum / values.Count;
        }

        /// <summary>线性插值分位数;空集返回 NaN。</summary>
        public static double Quantile(IList<double> values, double p)
        {
            if (values == null || values.Count == 0) return double.NaN;
            List<double> sorted = new List<double>(values);
            sorted.Sort();
            double position = Math.Max(0, Math.Min(1, p)) * (sorted.Count - 1);
            int lo = (int)position, hi = Math.Min(sorted.Count - 1, lo + 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (position - lo);
        }

        /// <summary>样本标准差(除以 n-1);少于两个样本返回 NaN。</summary>
        public static double Sd(IList<double> values)
        {
            if (values == null || values.Count < 2) return double.NaN;
            double mean = Mean(values), sum = 0;
            for (int i = 0; i < values.Count; i++) sum += (values[i] - mean) * (values[i] - mean);
            return Math.Sqrt(sum / (values.Count - 1));
        }

        /// <summary>相对变化:基准大于 0 时返回比例,否则 NaN(呈现层显示「基准不足」)。</summary>
        public static double Relative(double value, double baseline)
        {
            if (double.IsNaN(value) || double.IsNaN(baseline) || baseline <= 0) return double.NaN;
            return value / baseline - 1;
        }

        public static bool IsNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>一组样本的分布与直方图分箱。分箱计数之和恒等于样本数。</summary>
    internal sealed class Distribution
    {
        public readonly List<double> Values;
        public readonly int[] Bins;
        public readonly double BinStart, BinWidth;
        public readonly int Count;

        private readonly double _p10, _p50, _p90, _mean, _sd, _min, _max;

        public double Min { get { return _min; } }
        public double P10 { get { return _p10; } }
        public double P50 { get { return _p50; } }
        public double P90 { get { return _p90; } }
        public double Max { get { return _max; } }
        public double Mean { get { return _mean; } }
        public double Sd { get { return _sd; } }
        /// <summary>变异系数 = 标准差 ÷ 均值;均值不大于 0 时返回 NaN。</summary>
        public double Cv { get { return RangeMath.IsNumber(_sd) && _mean > 0 ? _sd / _mean : double.NaN; } }
        public double Range { get { return Count > 0 ? _max - _min : double.NaN; } }

        private Distribution(List<double> values, int binCount)
        {
            Values = values;
            Count = values.Count;
            Bins = new int[0];
            BinStart = BinWidth = double.NaN;
            _min = _p10 = _p50 = _p90 = _max = _mean = _sd = double.NaN;
            if (Count == 0) return;
            _min = RangeMath.Quantile(values, 0);
            _max = RangeMath.Quantile(values, 1);
            _p10 = RangeMath.Quantile(values, .1);
            _p50 = RangeMath.Quantile(values, .5);
            _p90 = RangeMath.Quantile(values, .9);
            _mean = RangeMath.Mean(values);
            _sd = RangeMath.Sd(values);
            Bins = BuildBins(values, binCount, out BinStart, out BinWidth);
        }

        /// <summary>空集返回 Count 为 0 的分布,而不是 null。</summary>
        public static Distribution Of(IEnumerable<double> values, int binCount)
        {
            List<double> samples = new List<double>();
            if (values != null)
                foreach (double value in values)
                    if (RangeMath.IsNumber(value)) samples.Add(value);
            return new Distribution(samples, Math.Max(1, binCount));
        }

        public static Distribution Of(IEnumerable<double> values)
        {
            return Of(values, 10);
        }

        private static int[] BuildBins(List<double> samples, int binCount, out double start, out double width)
        {
            double minimum = RangeMath.Quantile(samples, 0), maximum = RangeMath.Quantile(samples, 1);
            if (maximum <= minimum)
            {
                start = minimum; width = 0;
                int[] single = new int[1];
                single[0] = samples.Count;
                return single;
            }
            start = minimum; width = (maximum - minimum) / binCount;
            int[] bins = new int[binCount];
            foreach (double value in samples)
            {
                int index = (int)((value - start) / width);
                if (index < 0) index = 0;
                if (index >= binCount) index = binCount - 1;   // 最大值归入最后一箱
                bins[index]++;
            }
            return bins;
        }

        /// <summary>第 index 箱的下界;越界返回 NaN。</summary>
        public double BinLower(int index)
        {
            if (Bins.Length == 0 || index < 0 || index >= Bins.Length) return double.NaN;
            return BinStart + BinWidth * index;
        }

        public double BinUpper(int index)
        {
            if (Bins.Length == 0 || index < 0 || index >= Bins.Length) return double.NaN;
            return BinWidth == 0 ? BinStart : BinStart + BinWidth * (index + 1);
        }
    }

    /// <summary>区间内的线性趋势与星期周期。</summary>
    internal sealed class Trend
    {
        public double SlopePerDay, Intercept, R2;
        public int Points;
        /// <summary>每个星期几相对区间均值的平均偏离;无样本的星期为 NaN。</summary>
        public readonly double[] WeekdayOffset = new double[7];

        /// <summary>至少 3 个有效样本才给结论;不足时 Usable 为 false。</summary>
        public bool Usable { get { return Points >= 3; } }

        /// <summary>按实际日期间隔拟合,缺失日不会把横轴压密。</summary>
        public static Trend Linear(List<KeyValuePair<DateTime, double>> samples)
        {
            Trend trend = new Trend();
            if (samples == null || samples.Count == 0) return trend;
            trend.Points = samples.Count;
            double[] offsets = WeekdayOffsets(samples);
            for (int i = 0; i < 7; i++) trend.WeekdayOffset[i] = offsets[i];
            if (samples.Count < 3) return trend;

            DateTime first = samples[0].Key;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            int n = samples.Count;
            foreach (KeyValuePair<DateTime, double> sample in samples)
            {
                double x = (sample.Key - first).TotalDays, y = sample.Value;
                sx += x; sy += y; sxx += x * x; sxy += x * y;
            }
            double denominator = n * sxx - sx * sx;
            if (denominator == 0) return trend;   // 所有样本同一天
            trend.SlopePerDay = (n * sxy - sx * sy) / denominator;
            trend.Intercept = (sy - trend.SlopePerDay * sx) / n;

            double mean = sy / n, residual = 0, total = 0;
            foreach (KeyValuePair<DateTime, double> sample in samples)
            {
                double predicted = trend.Intercept + trend.SlopePerDay * (sample.Key - first).TotalDays;
                residual += (sample.Value - predicted) * (sample.Value - predicted);
                total += (sample.Value - mean) * (sample.Value - mean);
            }
            trend.R2 = total > 0 ? 1 - residual / total : double.NaN;
            return trend;
        }

        /// <summary>按星期几去均值:把「长期趋势」和「作息规律」分开看。</summary>
        public static double[] WeekdayOffsets(List<KeyValuePair<DateTime, double>> samples)
        {
            double[] sums = new double[7];
            int[] counts = new int[7];
            double total = 0;
            int count = 0;
            foreach (KeyValuePair<DateTime, double> sample in samples)
            {
                int index = RangeRules.WeekdayIndex(sample.Key);
                sums[index] += sample.Value;
                counts[index]++;
                total += sample.Value;
                count++;
            }
            double[] offsets = new double[7];
            double mean = count > 0 ? total / count : double.NaN;
            for (int i = 0; i < 7; i++)
                offsets[i] = counts[i] > 0 ? sums[i] / counts[i] - mean : double.NaN;
            return offsets;
        }
    }

    /// <summary>一个完整日区间内的合计指标与样本集合。</summary>
    internal sealed class RangeMetrics
    {
        public DateTime Start, End;
        public int Days, Observed, Sessions;
        public long Keys, Clicks, Wheel, Combos, AppSwitches, PeakApm;
        public double ActiveSeconds, IdleSeconds, MoveMeters, SessionSeconds;
        public double AppObservedSeconds, AppAttributedSeconds;
        public readonly List<double> SessionLengths = new List<double>();
        public readonly Dictionary<string, double> AppSeconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, long> AppKeys = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, long> AppClicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public bool IsEmpty { get { return Days == 0 || Observed == 0; } }
        /// <summary>有效天数 ÷ 区间天数;空区间为 0。</summary>
        public double Coverage { get { return Days > 0 ? (double)Observed / Days : 0; } }
        public double KeysPerDay { get { return Observed > 0 ? (double)Keys / Observed : double.NaN; } }
        public double ClicksPerDay { get { return Observed > 0 ? (double)Clicks / Observed : double.NaN; } }
        public double ActivePerDay { get { return Observed > 0 ? ActiveSeconds / Observed : double.NaN; } }
        public double MeanSession { get { return Sessions > 0 ? SessionSeconds / Sessions : double.NaN; } }
        public string CoverageText { get { return RangeRules.CoverageText(Observed, Days); } }
    }

    /// <summary>区间聚合入口。</summary>
    internal static class RangeStats
    {
        /// <summary>规范化并聚合区间;区间内没有任何完整日时返回空指标(Days = 0)。</summary>
        public static RangeMetrics Of(DateTime start, DateTime end)
        {
            RangeMetrics metrics = new RangeMetrics();
            DateTime from, to;
            if (!RangeRules.TryNormalize(start, end, out from, out to)) return metrics;
            metrics.Start = from;
            metrics.End = to;
            metrics.Days = (int)(to - from).TotalDays + 1;

            for (DateTime date = from; date <= to; date = date.AddDays(1))
            {
                DayRecord day = Analysis.GetDay(date);
                if (day == null || day.IsEmpty) continue;
                metrics.Observed++;
                metrics.Keys += day.Keys;
                metrics.Clicks += day.Clicks;
                metrics.Wheel += day.Wheel;
                metrics.Combos += PersonalStats.Combos(day);
                metrics.ActiveSeconds += day.ActiveSeconds;
                metrics.IdleSeconds += day.IdleSeconds;
                metrics.MoveMeters += day.MoveMeters;
                metrics.AppSwitches += day.AppSwitches;
                metrics.AppObservedSeconds += day.AppObservedSeconds;
                if (day.PeakApm > metrics.PeakApm) metrics.PeakApm = day.PeakApm;

                foreach (ActiveSession session in day.Sessions)
                {
                    if (session.Seconds <= 0) continue;
                    metrics.Sessions++;
                    metrics.SessionSeconds += session.Seconds;
                    metrics.SessionLengths.Add(session.Seconds);
                }
                foreach (AppUsage app in day.Apps.Values)
                {
                    string path = app.ProcessPath ?? "";
                    double seconds;
                    metrics.AppSeconds.TryGetValue(path, out seconds);
                    metrics.AppSeconds[path] = seconds + app.ActiveSeconds;
                    long keys, clicks;
                    metrics.AppKeys.TryGetValue(path, out keys);
                    metrics.AppKeys[path] = keys + app.Keys;
                    metrics.AppClicks.TryGetValue(path, out clicks);
                    metrics.AppClicks[path] = clicks + app.Clicks;
                    metrics.AppAttributedSeconds += app.ActiveSeconds;
                }
            }
            return metrics;
        }

        /// <summary>近 N 天区间(含最近一个完整日)。</summary>
        public static RangeMetrics Recent(DateTime end, int days)
        {
            DateTime to = RangeRules.CompleteEnd(end);
            return Of(to.AddDays(-(Math.Max(1, days) - 1)), to);
        }

        /// <summary>按日期升序返回有效日的样本,缺失日直接跳过(不补零)。</summary>
        public static List<KeyValuePair<DateTime, double>> DailySeries(DateTime start, DateTime end, Func<DayRecord, double> selector)
        {
            List<KeyValuePair<DateTime, double>> series = new List<KeyValuePair<DateTime, double>>();
            DateTime from, to;
            if (!RangeRules.TryNormalize(start, end, out from, out to)) return series;
            for (DateTime date = from; date <= to; date = date.AddDays(1))
            {
                DayRecord day = Analysis.GetDay(date);
                if (day == null || day.IsEmpty) continue;
                double value = selector(day);
                if (RangeMath.IsNumber(value)) series.Add(new KeyValuePair<DateTime, double>(date, value));
            }
            return series;
        }

        public static List<double> DailyValues(DateTime start, DateTime end, Func<DayRecord, double> selector)
        {
            List<double> values = new List<double>();
            foreach (KeyValuePair<DateTime, double> sample in DailySeries(start, end, selector)) values.Add(sample.Value);
            return values;
        }

        /// <summary>区间内每日指标的分布。</summary>
        public static Distribution DailyDistribution(DateTime start, DateTime end, Func<DayRecord, double> selector, int binCount)
        {
            return Distribution.Of(DailyValues(start, end, selector), binCount);
        }

        /// <summary>区间内每日指标的趋势与星期偏移。</summary>
        public static Trend DailyTrend(DateTime start, DateTime end, Func<DayRecord, double> selector)
        {
            return Trend.Linear(DailySeries(start, end, selector));
        }
    }

    /// <summary>两个区间的对比。变化率按「日均」计算,避免有效天数不同时总量失真。</summary>
    internal sealed class RangeComparison
    {
        public readonly RangeMetrics Current, Previous;

        public RangeComparison(RangeMetrics current, RangeMetrics previous)
        {
            Current = current;
            Previous = previous;
        }

        /// <summary>两期都至少有一天有效记录时才给出变化率。</summary>
        public bool Comparable { get { return !Current.IsEmpty && !Previous.IsEmpty; } }

        public double KeysChange { get { return Change(Current.KeysPerDay, Previous.KeysPerDay); } }
        public double ClicksChange { get { return Change(Current.ClicksPerDay, Previous.ClicksPerDay); } }
        public double ActiveChange { get { return Change(Current.ActivePerDay, Previous.ActivePerDay); } }
        public double MeanSessionChange { get { return Change(Current.MeanSession, Previous.MeanSession); } }

        /// <summary>总量变化:仅在两期有效天数相同时才有意义。</summary>
        public double KeysTotalChange { get { return Change(Current.Keys, Previous.Keys); } }
        public bool SameCoverage { get { return Current.Observed == Previous.Observed; } }

        private static double Change(double current, double previous)
        {
            return RangeMath.Relative(current, previous);
        }

        public static string Percent(double value, string fallback)
        {
            return double.IsNaN(value) ? fallback : (value * 100).ToString("+0.0;-0.0;0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
