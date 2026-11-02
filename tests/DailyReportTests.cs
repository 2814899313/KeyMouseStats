using System;
using KeyMouseStats;

/// <summary>
/// 日报生成器的回归。数据来自真实的 stats.txt 与 Rime 日志,所以只断言「结构」而不断言
/// 具体数字:换一台机器、换一天跑,这些断言都必须仍然成立。
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
        // 日报读的是真实数据文件;测试进程里得自己加载一次,否则一切都被当成「没数据」。
        Store.Load();

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

        // 昨天:有数据就断言结构,没数据就断言「说了没数据」——换台新机器跑也必须成立。
        DailyReport yesterday = DailyReport.Build(DateTime.Today.AddDays(-1));
        Check(yesterday.Day == DateTime.Today.AddDays(-1), "yesterday resolves correctly");
        Check(!yesterday.InProgress, "yesterday is not in progress");
        string yesterdayText = DailyReport.ToMarkdown(yesterday);
        Check(yesterdayText.IndexOf(yesterday.Day.ToString("yyyy-MM-dd"), StringComparison.Ordinal) >= 0, "the markdown has yesterday's date");
        if (yesterday.HasData)
            Check(yesterdayText.IndexOf("核心数字", StringComparison.Ordinal) >= 0, "a day with data has the numbers section");
        else
            Check(yesterdayText.IndexOf("没有任何记录", StringComparison.Ordinal) >= 0, "a day without data says so");

        // 今天:必须标记成「进行中」。
        DailyReport today = DailyReport.Build(DateTime.Today);
        Check(today.InProgress, "today's report is marked as in progress");

        Console.WriteLine("DailyReportTests: " + checks + " checks passed");
    }
}
