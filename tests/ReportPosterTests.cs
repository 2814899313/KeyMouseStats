using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using KeyMouseStats;

/// <summary>日报高清长图:3× 尺寸、落盘大小、空数据不崩。</summary>
internal static class ReportPosterTests
{
    [STAThread]
    static void Main()
    {
        DateTime day = DateTime.Today.AddDays(-1);

        DailyReport report = new DailyReport
        {
            Day = day,
            HasData = true,
            HasChars = true,
            Keys = 13420,
            Clicks = 3387,
            ActiveSeconds = 33780,
            Chars = 766,
            Commits = 276,
            DistinctChars = 315,
            AvgCharsPerCommit = 2.8,
            BaseDays = 7,
            BaseKeys = 6000,
            BaseClicks = 2000,
            BaseActiveSeconds = 18000,
            BaseChars = 640,
            BusiestHour = 14,
            BusiestHourKeys = 1912,
            LongestSessionSeconds = 11400,
            SessionCount = 18,
            MoveMeters = 864.2,
            PeakApm = 237,
            BusiestCharHour = 20,
            LongestBurstMinutes = 8
        };

        long[] hours =
        {
            0, 0, 0, 0, 0, 0, 0, 60, 540, 1200, 980, 760, 380,
            540, 1912, 1420, 1280, 1100, 860, 620, 980, 560, 220, 8
        };
        for (int i = 0; i < 24; i++) report.HourKeys[i] = hours[i];

        string[] names = { "Microsoft Edge", "Visual Studio Code", "ChatGPT", "文件资源管理器", "一个名字很长的写作工具" };
        double[] seconds = { 18000, 4680, 2580, 1800, 1380 };
        for (int i = 0; i < 5; i++)
            report.Apps.Add(new DailyReport.AppLine
            {
                Name = names[i],
                ProcessPath = names[i] + ".exe",
                Seconds = seconds[i]
            });

        report.Combos.AddRange(new[] { "CTRL+V 95", "CTRL+C 55", "CTRL+ALT+A 42", "ALT+TAB 37" });
        report.TopKeys.AddRange(new[] { "I 1,247", "空格 1,018", "A 980" });
        report.TopChars.AddRange(new[] { "的 33", "，28", "是 19" });
        report.TopPhrases.AddRange(new[] { "键鼠 12", "统计 9", "日报 7" });
        report.Composition[0] = 600;
        report.Composition[1] = 60;
        report.Composition[2] = 40;
        report.Composition[3] = 30;
        report.Composition[4] = 36;
        report.Reminders.AddRange(new[]
        {
            "击键比日均高 124%，今天是真在干活。",
            "最长一段连续使用 3 时 10 分，键盘替你喊过累。",
            "浏览器占了 53%，今天基本住在那儿。",
            "目标（活跃时长）已达成 156%"
        });

        DayRecord record = new DayRecord { Date = day, Keys = report.Keys, Clicks = report.Clicks, ActiveSeconds = report.ActiveSeconds };
        record.Sessions.Add(new ActiveSession { Start = day.AddHours(8).AddMinutes(30), Seconds = 30 * 60 });
        record.Sessions.Add(new ActiveSession { Start = day.AddHours(9).AddMinutes(10), Seconds = 120 * 60 });
        record.Sessions.Add(new ActiveSession { Start = day.AddHours(11).AddMinutes(40), Seconds = 35 * 60 });
        record.Sessions.Add(new ActiveSession { Start = day.AddHours(13).AddMinutes(5), Seconds = 190 * 60 });
        record.Sessions.Add(new ActiveSession { Start = day.AddHours(16).AddMinutes(35), Seconds = 70 * 60 });
        record.Sessions.Add(new ActiveSession { Start = day.AddHours(19).AddMinutes(10), Seconds = 50 * 60 });
        record.Sessions.Add(new ActiveSession { Start = day.AddHours(20).AddMinutes(20), Seconds = 68 * 60 });

        string path = Path.Combine(Path.GetTempPath(), "kms-report-poster-test.png");
        string oldData = Store.DataDirectory;
        string dataDir = Path.Combine(Path.GetTempPath(), "kms-poster-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (Bitmap bitmap = DailyReportPoster.Render(report, record, false))
            {
                Check(bitmap.Width == DailyReportPoster.LogicalWidth * DailyReportPoster.OutputScale,
                    "poster width is 3x logical");
                Check(bitmap.Height > 3000, "poster is tall enough: " + bitmap.Height);
                bitmap.Save(path, ImageFormat.Png);

                Store.DataDirectory = dataDir;
                string saved = DailyReportPoster.Save(bitmap, day);
                Check(File.Exists(saved), "poster saves into the daily folder");
                Check(saved.EndsWith("-日报.png", StringComparison.Ordinal), "poster filename is the daily png");
            }
            Check(new FileInfo(path).Length > 120000, "poster png has real content");

            DailyReport empty = new DailyReport { Day = DateTime.Today };
            using (Bitmap bitmap = DailyReportPoster.Render(empty, null, false))
            {
                Check(bitmap.Width == DailyReportPoster.LogicalWidth * DailyReportPoster.OutputScale,
                    "empty poster width is 3x logical");
                Check(bitmap.Height > 2000, "empty poster still renders");
            }

            Console.WriteLine("ReportPosterTests: 3x daily poster and empty-day fallback passed.");
        }
        finally
        {
            Store.DataDirectory = oldData;
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
            try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch (Exception) { }
        }
    }

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
