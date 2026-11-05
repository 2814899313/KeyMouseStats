using System;
using KeyMouseStats;

/// <summary>
/// Y 轴整值刻度。用户看到 3.61k / 7.22k / 10.83k 这种刻度是因为「最大值 ÷ 4」直接当步长;
/// 这个测试保证步长落在 1/1.5/2/2.5/3/4/5/6/8/10 × 10ⁿ 上,并且顶刻度一定罩得住数据。
/// </summary>
internal static class AxisScaleTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Near(double actual, double expected, string what)
    {
        Check(Math.Abs(actual - expected) < 1e-6, what + " (got " + actual + ", want " + expected + ")");
    }

    private static void Run()
    {
        double top, step;

        // 用户截图里的 14.44k:期望 0 / 4k / 8k / 12k / 16k。
        AxisScale.Nice(14440, 4, out top, out step);
        Near(step, 4000, "14.44k picks a 4k step");
        Near(top, 16000, "14.44k tops out at 16k");
        Check(AxisScale.Label(0) == "0" && AxisScale.Label(4000) == "4k" && AxisScale.Label(8000) == "8k"
            && AxisScale.Label(12000) == "12k" && AxisScale.Label(16000) == "16k", "ticks read as whole thousands");

        // 常见量级:800 / 1.2k / 8000 / 120 万。
        AxisScale.Nice(800, 4, out top, out step);
        Check(step > 0 && top >= 800 && Math.Abs(top / step - 4) < 1e-9, "small maxima keep four divisions");
        AxisScale.Nice(1200, 4, out top, out step);
        Near(step, 400, "1.2k picks a 400 step");
        Near(top, 1600, "1.2k tops out at 1.6k");
        AxisScale.Nice(8000, 4, out top, out step);
        Near(step, 2500, "8k picks a 2500 step");
        Near(top, 10000, "8k tops out at 10k");
        AxisScale.Nice(1.2e6, 4, out top, out step);
        Near(step, 4e5, "1.2M picks a 400k step");
        Near(top, 1.6e6, "1.2M tops out at 1.6M");
        Check(AxisScale.Label(1.6e6) == "1.6M", "millions keep one decimal");

        // 任何输入都必须顶得住数据,并且分成整齐的四等份。
        double[] samples = { 1, 7, 99, 101, 333, 999, 1001, 4321, 9999, 12345, 14440, 77777, 99999, 654321 };
        foreach (double sample in samples)
        {
            AxisScale.Nice(sample, 4, out top, out step);
            Check(top >= sample, "top covers " + sample);
            Check(step > 0 && Math.Abs(top - step * 4) < 1e-6, "top is four steps for " + sample);
            double ratio = step / Math.Pow(10, Math.Floor(Math.Log10(step)));
            Check(ratio >= 0.999 && ratio <= 10.001, "step stays a nice multiple around " + sample);
        }

        // 空数据 / 非法值:给一组能画出来的默认刻度。
        AxisScale.Nice(0, 4, out top, out step);
        Near(step, 1, "zero data falls back to a unit step");
        Near(top, 4, "zero data still has four divisions");
        AxisScale.Nice(double.NaN, 4, out top, out step);
        Near(step, 1, "NaN falls back to a unit step");
        AxisScale.Nice(-5, 4, out top, out step);
        Near(step, 1, "negative maxima are treated as empty");

        // 步长越小,小数刻度也不会出现两位小数。
        AxisScale.Nice(3, 4, out top, out step);
        Check(Math.Abs(step - 1) < 1e-9 && Math.Abs(top - 4) < 1e-9, "tiny values keep integer steps");

        // ---- 按小时趋势:活跃分钟数物理上限 60,轴必须锁死在 60,刻度 0/15/30/45/60 ----
        HourlyAxis.Compute(2, 69.5, out top, out step);
        Near(top, 60, "active minutes never exceed a 60-minute ceiling");
        Near(step, 15, "active minutes use quarter-hour ticks");
        HourlyAxis.Compute(2, 5, out top, out step);
        Near(top, 60, "a quiet day still uses the full 0-60 scale");
        Near(step, 15, "the 15-minute step is fixed");
        Check(HourlyAxis.Label(2, 0) == "0" && HourlyAxis.Label(2, 15) == "15"
            && HourlyAxis.Label(2, 45) == "45" && HourlyAxis.Label(2, 60) == "60", "minute ticks read as whole minutes");
        Check(HourlyAxis.Clamp(2, 75, 60) == 60, "historical over-60 values are clamped for drawing");
        Check(HourlyAxis.Clamp(0, 75, 60) == 75, "other hourly metrics are not clamped");

        // 其他小时指标继续用整值/小数刻度。
        HourlyAxis.Compute(0, 14440, out top, out step);
        Near(top, 16000, "hourly key counts use nice numbers");
        Near(step, 4000, "hourly key counts top out at a whole thousand");
        HourlyAxis.Compute(3, 1.7, out top, out step);
        Check(top >= 1.7 && top / step > 3.9 && top / step < 4.1, "key-per-second keeps four divisions");
        Check(HourlyAxis.Label(3, 0.5) == "0.5", "rates may show one decimal on the axis");
        Check(HourlyAxis.Label(0, 4000) == "4k", "hourly counts keep the k suffix");

        // ---- 速率单位统一为「每分钟」:击键/分 与 点击/分,不再一个按秒一个按分 ----
        Check(HourlyAxis.Unit(0) == " 次" && HourlyAxis.Unit(2) == " 分钟", "count/hour units are unchanged");
        Check(HourlyAxis.Unit(3) == " 次/分" && HourlyAxis.Unit(4) == " 次/分", "both rate metrics are per minute");

        DayRecord minute = new DayRecord();
        minute.Date = new DateTime(2026, 11, 5);
        minute.ActiveSeconds = 60;
        minute.HourKeys[9] = 120;      // 1 分钟活跃里敲了 120 下
        minute.HourClicks[9] = 60;
        minute.ActiveHours[9] = 60;    // 存的是秒:60 秒 = 1 个活跃分钟
        DateTime midnight = new DateTime(2026, 11, 6, 0, 0, 0);
        double[] keysPerMinute = Dashboard.HourlyValues(minute, minute.Date, 3, midnight);
        double[] clicksPerMinute = Dashboard.HourlyValues(minute, minute.Date, 4, midnight);
        Check(Math.Abs(keysPerMinute[9] - 120) < 1e-9, "KPM is keys per active minute, not per second");
        Check(Math.Abs(clicksPerMinute[9] - 60) < 1e-9, "clicks rate is also per active minute");
        Check(Math.Abs(keysPerMinute[9] / clicksPerMinute[9] - 2) < 1e-9, "both rates share the same time base");
        Check(double.IsNaN(keysPerMinute[10]), "hours without active time stay missing instead of dividing by zero");

        Console.WriteLine("Axis nice numbers: " + checks + " checks passed");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("AxisScaleTests", Run);
    }
}
