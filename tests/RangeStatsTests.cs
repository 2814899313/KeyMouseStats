using System;
using System.Collections.Generic;
using KeyMouseStats;

/// <summary>
/// 区间分析处理层回归:口径规范化、聚合、分布分箱守恒、趋势拟合、星期偏移与对比。
/// 只使用内存中的合成数据,不读写用户的统计数据。
/// </summary>
internal static class RangeStatsTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
    private static void Near(double actual, double expected, double tolerance, string name)
    {
        Check(Math.Abs(actual - expected) <= tolerance, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static DayRecord Day(DateTime date, long keys, double active, long clicks, double wheel)
    {
        DayRecord day = new DayRecord();
        day.Date = date;
        day.Keys = keys;
        day.Clicks = clicks;
        day.Wheel = (long)wheel;
        day.ActiveSeconds = active;
        day.IdleSeconds = 60;
        day.AppSwitches = 3;
        day.AppObservedSeconds = active;
        day.PeakApm = keys > 0 ? keys / 100 : 0;
        day.ComboCounts["ctrl_c"] = 2;
        day.Sessions.Add(new ActiveSession { Start = date.AddHours(9), End = date.AddHours(10), Seconds = active / 2 });
        day.Sessions.Add(new ActiveSession { Start = date.AddHours(11), End = date.AddHours(11).AddMinutes(30), Seconds = active / 2 });
        AppUsage app = new AppUsage { ProcessPath = @"C:\Apps\code.exe", Title = "code", Keys = keys / 2, Clicks = clicks / 2, ActiveSeconds = active / 2 };
        day.Apps[app.Id] = app;
        return day;
    }

    private static void Main()
    {
        // ---- 口径:完整日边界 ----
        Check(RangeRules.CompleteEnd(DateTime.Today) == DateTime.Today.AddDays(-1), "today is not a complete day");
        Check(RangeRules.CompleteEnd(DateTime.Today.AddDays(-30)) == DateTime.Today.AddDays(-30), "past dates are complete");

        DateTime from, to;
        Check(RangeRules.TryNormalize(DateTime.Today.AddDays(-1), DateTime.Today, out from, out to)
            && from == DateTime.Today.AddDays(-1) && to == DateTime.Today.AddDays(-1), "end clamps to the last complete day");
        Check(!RangeRules.TryNormalize(DateTime.Today, DateTime.Today, out from, out to), "today-only range has no complete day");
        Check(RangeRules.TryNormalize(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-5), out from, out to)
            && (int)(to - from).TotalDays + 1 == 6, "six complete days");

        // ---- 合成数据:两个缺口 ----
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        long expectedKeys = 0;
        int expectedObserved = 0;
        for (int i = 1; i <= 10; i++)
        {
            DateTime date = DateTime.Today.AddDays(-i);
            if (i == 3 || i == 7) continue;                 // 缺口
            long keys = 1000 * i;
            Store.History[date] = Day(date, keys, 3600, 10 * i, 5 * i);
            expectedKeys += keys;
            expectedObserved++;
        }

        RangeMetrics range = RangeStats.Of(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-1));
        Check(range.Days == 10, "ten day range");
        Check(range.Observed == expectedObserved, "missing days are not counted as observed");
        Check(range.Keys == expectedKeys, "keys sum");
        Check(range.Clicks == 10 * (1 + 2 + 4 + 5 + 6 + 8 + 9 + 10), "clicks sum skips gaps");
        Check(range.Sessions == expectedObserved * 2, "sessions collected");
        Near(range.Coverage, expectedObserved / 10.0, 1e-12, "coverage uses range days as denominator");
        Near(range.KeysPerDay, (double)expectedKeys / expectedObserved, 1e-9, "daily average uses observed days only");
        Check(range.CoverageText == expectedObserved + " / 10 天", "coverage text");
        Check(Math.Abs(range.AppAttributedSeconds - expectedObserved * 1800.0) < 1e-9, "app time aggregated by process");
        Check(range.AppKeys.Count == 1 && range.AppKeys[@"C:\Apps\code.exe"] == expectedKeys / 2, "per-app keys aggregated");

        // ---- 空区间 ----
        RangeMetrics empty = RangeStats.Of(DateTime.Today, DateTime.Today);
        Check(empty.Days == 0 && empty.IsEmpty && empty.Coverage == 0, "today-only range is empty");
        Check(double.IsNaN(empty.KeysPerDay), "empty range has no daily average");
        Check(empty.CoverageText == "0 / 0 天", "empty coverage text");

        // ---- 分布 ----
        List<double> ten = new List<double>();
        for (int i = 1; i <= 10; i++) ten.Add(i);
        Distribution distribution = Distribution.Of(ten, 5);
        Check(distribution.Count == 10, "distribution keeps every sample");
        Near(distribution.Min, 1, 1e-12, "min");
        Near(distribution.Max, 10, 1e-12, "max");
        Near(distribution.P10, 1.9, 1e-12, "P10");
        Near(distribution.P50, 5.5, 1e-12, "P50");
        Near(distribution.P90, 9.1, 1e-12, "P90");
        Near(distribution.Mean, 5.5, 1e-12, "mean");
        Near(distribution.Sd, Math.Sqrt(82.5 / 9), 1e-9, "sample standard deviation");
        int binTotal = 0;
        foreach (int bin in distribution.Bins) binTotal += bin;
        Check(binTotal == distribution.Count, "bins conserve every sample");
        Check(distribution.Bins.Length == 5, "requested bin count");
        Near(distribution.BinLower(0), 1, 1e-12, "first bin starts at the minimum");
        Near(distribution.BinUpper(4), 10, 1e-12, "last bin ends at the maximum");

        Distribution single = Distribution.Of(new double[] { 42 }, 8);
        Check(single.Count == 1 && single.Bins.Length == 1 && single.Bins[0] == 1, "constant sample collapses to one bin");
        Near(single.P50, 42, 1e-12, "constant P50");

        Distribution blank = Distribution.Of(new double[] { double.NaN, double.PositiveInfinity });
        Check(blank.Count == 0 && blank.Bins.Length == 0 && double.IsNaN(blank.P50), "non-finite samples are ignored");

        // ---- 趋势 ----
        List<KeyValuePair<DateTime, double>> line = new List<KeyValuePair<DateTime, double>>();
        for (int i = 0; i < 5; i++) line.Add(new KeyValuePair<DateTime, double>(DateTime.Today.AddDays(-5 + i), 2.0 * i + 1));
        Trend trend = Trend.Linear(line);
        Check(trend.Usable && trend.Points == 5, "trend needs at least three samples");
        Near(trend.SlopePerDay, 2, 1e-12, "slope");
        Near(trend.Intercept, 1, 1e-12, "intercept");
        Near(trend.R2, 1, 1e-12, "perfect fit R2");
        Check(!Trend.Linear(new List<KeyValuePair<DateTime, double>>(line.GetRange(0, 2))).Usable, "two samples are not enough");
        Check(!Trend.Linear(null).Usable, "no samples means no trend");

        // 星期偏移:只有周一有值,加权偏移之和必须为 0
        List<KeyValuePair<DateTime, double>> weekday = new List<KeyValuePair<DateTime, double>>();
        int[] weekdayCounts = new int[7];
        for (int i = 13; i >= 0; i--)
        {
            DateTime date = DateTime.Today.AddDays(-1 - i);
            int index = RangeRules.WeekdayIndex(date);
            weekdayCounts[index]++;
            weekday.Add(new KeyValuePair<DateTime, double>(date, index == 0 ? 100 : 0));
        }
        double[] offsets = Trend.WeekdayOffsets(weekday);
        Check(offsets[0] > 0, "the busiest weekday has a positive offset");
        double weighted = 0;
        for (int i = 0; i < 7; i++) weighted += weekdayCounts[i] * offsets[i];
        Near(weighted, 0, 1e-9, "weighted weekday offsets sum to zero");
        Check(!double.IsNaN(offsets[0]), "weekday with samples has an offset");

        // ---- 对比 ----
        RangeMetrics first = RangeStats.Of(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-6));
        RangeMetrics second = RangeStats.Of(DateTime.Today.AddDays(-5), DateTime.Today.AddDays(-1));
        RangeComparison comparison = new RangeComparison(second, first);
        Check(comparison.Comparable, "both ranges have records");
        Check(double.IsNaN(new RangeComparison(empty, first).KeysChange), "empty baseline yields no change");
        Near(RangeMath.Relative(200, 100), 1.0, 1e-12, "doubling is +100%");
        Check(double.IsNaN(RangeMath.Relative(10, 0)), "zero baseline is not a change of 0%");
        Check(RangeComparison.Percent(double.NaN, "基准不足") == "基准不足", "nan formats as fallback text");
        Check(RangeComparison.Percent(1.0, "--") == "+100.0%", "percent formatting");

        // ---- 委托:旧的 PersonalStats 与新实现必须一致 ----
        List<double> samples = new List<double> { 3, 1, 4, 1, 5, 9, 2, 6 };
        Near(PersonalStats.Quantile(samples, .5), RangeMath.Quantile(samples, .5), 1e-12, "quantile delegates");
        Near(PersonalStats.Mean(samples), RangeMath.Mean(samples), 1e-12, "mean delegates");
        Near(PersonalStats.Sd(samples), RangeMath.Sd(samples), 1e-12, "standard deviation delegates");

        // ---- 日序列跳过缺失日并保持升序 ----
        List<KeyValuePair<DateTime, double>> series = RangeStats.DailySeries(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-1),
            delegate(DayRecord day) { return day.Keys; });
        Check(series.Count == expectedObserved, "daily series skips missing days");
        for (int i = 1; i < series.Count; i++) Check(series[i].Key > series[i - 1].Key, "daily series stays in order");
        Check(RangeStats.Recent(DateTime.Today, 7).Observed == 5, "recent window counts the seeded gap");

        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        Console.WriteLine("PASS: " + _checks + " range analysis checks");
    }
}
