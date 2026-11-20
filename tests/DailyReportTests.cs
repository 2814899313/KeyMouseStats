using System;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 日报生成器的回归。全部数据在 %TEMP% 沙盒里构造,不读用户真实 stats.txt 或 Rime 日志。
/// </summary>
internal static class DailyReportTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("DailyReportTests", Run);
    }

    private static void Run()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "KmsDailyReportTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        string previousData = Store.DataDirectory;
        try
        {
            Store.DataDirectory = sandbox;
            NativeBoot.RootOverride = Path.Combine(sandbox, "native");
            RimeTextStats.PathOverride = Path.Combine(sandbox, "missing-commit_log.csv");
            Store.History.Clear();
            Store.Total = new Counters();
            DateTime today = DateTime.Today;
            DayRecord yesterday = new DayRecord { Date = today.AddDays(-1), Keys = 5200, Clicks = 900, Wheel = 210, ActiveSeconds = 7200, IdleSeconds = 1200, MoveMeters = 8.4 };
            DayRecord current = new DayRecord { Date = today, Keys = 1800, Clicks = 260, Wheel = 70, ActiveSeconds = 2400, IdleSeconds = 600, MoveMeters = 3.1 };
            Store.History[yesterday.Date] = yesterday;
            Store.History[current.Date] = current;
            Store.Total.Keys = yesterday.Keys + current.Keys;
            Store.Total.Clicks = yesterday.Clicks + current.Clicks;
            Store.Total.Wheel = yesterday.Wheel + current.Wheel;
            Store.Total.MoveMeters = yesterday.MoveMeters + current.MoveMeters;
            Store.RollDay(today);

            // 一个绝对不会有数据的日子:2000-01-01。
            DailyReport empty = DailyReport.Build(new DateTime(2000, 1, 1));
            Check(!empty.HasData, "a date before first use has no data");
            Check(empty.Day == new DateTime(2000, 1, 1), "the report keeps the requested date");
            Check(empty.BaselineLabel == "无基准", "no baseline days means no baseline label");
            Check(empty.Reminders.Count >= 1, "an empty day still produces at least one line");

            string markdown = DailyReport.ToMarkdown(empty);
            Check(markdown.StartsWith("# 键鼠统计 · 日报", StringComparison.Ordinal), "the markdown has a title");
            Check(markdown.IndexOf("2000-01-01", StringComparison.Ordinal) >= 0, "the markdown carries the date");
            Check(markdown.IndexOf("没有任何记录", StringComparison.Ordinal) >= 0, "an empty day says so instead of printing zeros");
            Check(empty.FilePath.EndsWith("2000-01-01.md", StringComparison.Ordinal), "the file name is the date");

            // 昨天:沙盒里已注入有数据的记录,必须走「有数据」分支。
            DailyReport report = DailyReport.Build(today.AddDays(-1));
            Check(report.Day == today.AddDays(-1), "yesterday resolves correctly");
            Check(!report.InProgress, "yesterday is not in progress");
            Check(report.HasData, "yesterday's sandbox fixture is visible");
            string yesterdayText = DailyReport.ToMarkdown(report);
            Check(yesterdayText.IndexOf(report.Day.ToString("yyyy-MM-dd"), StringComparison.Ordinal) >= 0, "the markdown has yesterday's date");
            Check(yesterdayText.IndexOf("核心数字", StringComparison.Ordinal) >= 0, "a day with data has the numbers section");

            // 今天:必须标记成「进行中」。
            DailyReport todayReport = DailyReport.Build(today);
            Check(todayReport.InProgress, "today's report is marked as in progress");

            Console.WriteLine("DailyReportTests: " + checks + " checks passed");
        }
        finally
        {
            Store.DataDirectory = previousData;
            RimeTextStats.PathOverride = null;
            try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); } catch { }
        }
    }
}
