using System;
using System.Drawing;
using System.IO;
using KeyMouseStats;

/// <summary>周报/月报 3× 长图:尺寸、落盘文件名、空数据兜底。</summary>
internal static class PeriodPosterTests
{
    [STAThread]
    static void Main()
    {
        DateTime today = DateTime.Today;
        Store.History.Clear();
        for (int ago = 60; ago >= 1; ago--)
        {
            DateTime date = today.AddDays(-ago);
            DayRecord day = new DayRecord();
            day.Date = date;
            day.Keys = 4200 + (ago * 617 % 8000);
            day.Clicks = 700 + (ago * 91 % 1000);
            day.ActiveSeconds = (3.5 + (ago % 6)) * 3600;
            day.PeakApm = 90 + (ago % 100);
            for (int h = 9; h < 18; h++) day.HourKeys[h] = 300 + (h * 23 + ago * 11) % 600;
            day.Sessions.Add(new ActiveSession { Start = date.AddHours(9), End = date.AddHours(11), Seconds = 7200 });
            AppUsage app = new AppUsage();
            app.ProcessPath = @"C:\Apps\chrome.exe";
            app.Title = "Edge";
            app.Keys = day.Keys / 3;
            app.ActiveSeconds = day.ActiveSeconds / 2;
            day.Apps[app.Id] = app;
            Store.History[date] = day;
        }

        string oldData = Store.DataDirectory;
        string dataDir = Path.Combine(Path.GetTempPath(), "kms-period-" + Guid.NewGuid().ToString("N"));
        try
        {
            Store.DataDirectory = dataDir;

            PeriodReport week = PeriodReport.BuildWeek(today.AddDays(-1));
            using (Bitmap bitmap = PeriodPoster.Render(week, false))
            {
                Check(bitmap.Width == PeriodPoster.LogicalWidth * PeriodPoster.OutputScale, "week width");
                Check(bitmap.Height > 3000, "week height " + bitmap.Height);
                string path = PeriodPoster.Save(bitmap, week);
                Check(File.Exists(path), "week file exists");
                Check(path.EndsWith("-周报.png", StringComparison.Ordinal), "week filename");
            }

            PeriodReport month = PeriodReport.BuildMonth(new DateTime(today.Year, today.Month, 1).AddMonths(-1));
            using (Bitmap bitmap = PeriodPoster.Render(month, false))
            {
                Check(bitmap.Width == PeriodPoster.LogicalWidth * PeriodPoster.OutputScale, "month width");
                Check(bitmap.Height > 3000, "month height " + bitmap.Height);
                string path = PeriodPoster.Save(bitmap, month);
                Check(File.Exists(path), "month file exists");
                Check(path.EndsWith("-月报.png", StringComparison.Ordinal), "month filename");
            }

            Store.History.Clear();
            PeriodReport empty = PeriodReport.BuildWeek(today.AddDays(-1));
            using (Bitmap bitmap = PeriodPoster.Render(empty, false))
            {
                Check(bitmap.Width == PeriodPoster.LogicalWidth * PeriodPoster.OutputScale, "empty width");
                Check(bitmap.Height > 2000, "empty still renders");
            }

            Console.WriteLine("PeriodPosterTests: week/month 3x posters and empty fallback passed.");
        }
        finally
        {
            Store.DataDirectory = oldData;
            try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch (Exception) { }
        }
    }

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
}
