// ============================================================================
//  tests/HwheelDisplayTests.cs  ·  v3.4 横向滚轮展示与导出
//  只测纯字符串/求和逻辑,不落盘、不打开 Dashboard 窗口。
// ============================================================================
using System;
using KeyMouseStats;

internal static class HwheelDisplayTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("HwheelDisplayTests", Run);
    }

    private static void Run()
    {
        SumAndStore();
        OverviewDetail();
        TrendLine();
        Csv();
        Console.WriteLine("HwheelDisplayTests: " + _checks + " 项断言通过");
    }

    private static void SumAndStore()
    {
        DayRecord first = new DayRecord();
        first.Date = new DateTime(2026, 11, 20);
        first.Wheel = 100;
        first.HWheel = 30;
        DayRecord second = new DayRecord();
        second.Date = new DateTime(2026, 11, 21);
        second.Wheel = 50;
        second.HWheel = 7;
        DayRecord[] days = { first, second };
        Equal(Analysis.SumWheel(days), 150, "垂直滚轮求和");
        Equal(Analysis.SumHWheel(days), 37, "横向滚轮求和");

        Store.Parsed parsed = Store.ParseText(
            "total_wheel=88\ntotal_hwheel=33\n[day=2026-11-21]\nkeys=1\nwheel=8\nhwheel=3\n");
        DayRecord day;
        Check(parsed.History.TryGetValue(new DateTime(2026, 11, 21), out day), "解析日记录");
        Equal(day.HWheel, 3, "DayRecord.HWheel 解析");
        Equal(parsed.Total.HWheel, 33, "Counters.HWheel 解析");
    }

    private static void OverviewDetail()
    {
        DayRecord empty = new DayRecord();
        Check(Dashboard.OverviewWheelDetail(empty) == null, "横滚为 0 时总览不显示副标");
        DayRecord day = new DayRecord();
        day.HWheel = 12345;
        Check(Dashboard.OverviewWheelDetail(day) == "横向 1.2万", "总览横滚副标");
        Check(Dashboard.OverviewWheelDetail(null) == null, "空记录安全");
    }

    private static void TrendLine()
    {
        Dashboard.AnalysisPt point = new Dashboard.AnalysisPt();
        point.V = 12345;
        point.HWheel = 678;
        string wheel = Dashboard.TrendValueLine(point, 2);
        Check(wheel.IndexOf("横向 678") >= 0, "趋势滚轮 Tooltip 包含横向");
        string keys = Dashboard.TrendValueLine(point, 0);
        Check(keys.IndexOf("横向") < 0, "击键 Tooltip 不混入横向");
        point.HWheel = 0;
        Check(Dashboard.TrendValueLine(point, 2).IndexOf("横向") < 0, "横滚为 0 时不追加");
        point.V = 1.5;
        Check(Dashboard.TrendValueLine(point, 3).IndexOf("m") >= 0, "鼠标路程仍走原格式");
    }

    private static void Csv()
    {
        DayRecord day = new DayRecord();
        day.Date = new DateTime(2026, 11, 21);
        day.Keys = 10;
        day.Clicks = 4;
        day.Left = 2;
        day.Right = 1;
        day.Middle = 1;
        day.Wheel = 700;
        day.HWheel = 120;
        string csv = Dashboard.BuildCsv(new DayRecord[] { day });
        Check(Dashboard.CsvHeader.IndexOf("横向滚轮") > 0, "CSV 表头包含横向滚轮");
        Check(csv.IndexOf("横向滚轮") > 0, "CSV 输出表头");
        Check(csv.IndexOf("2026-11-21") == 0
            || csv.IndexOf("2026-11-21") == Dashboard.CsvHeader.Length + Environment.NewLine.Length,
            "CSV 首行为日期");
        Check(csv.IndexOf(",700,120,") > 0, "CSV 滚轮后紧跟横向滚轮");
        Check(Dashboard.BuildCsv(null).IndexOf("日期") == 0, "空数据仍输出表头");
        DayRecord empty = new DayRecord();
        empty.Date = new DateTime(2026, 11, 20);
        Check(Dashboard.BuildCsv(new DayRecord[] { empty }).IndexOf("2026-11-20") < 0,
            "空记录不输出数据行");
    }
}
