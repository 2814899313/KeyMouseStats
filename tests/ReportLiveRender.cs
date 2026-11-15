using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>实时状态页:平滑面积流、网格、呼吸端点、峰值胶囊与底部实时维度。</summary>
internal static class ReportLiveRender
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;

    private static void Main()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        System.IO.Directory.CreateDirectory(output);
        Store.History.Clear(); Store.RollDay(DateTime.Today); Store.MouseDpi = 1600;
        DayRecord day = new DayRecord { Date = DateTime.Today, Keys = 7420, Clicks = 2180, ActiveSeconds = 25200, PeakApm = 0 };
        day.ComboCounts["ctrl_c"] = 31; day.ComboCounts["ctrl_v"] = 24; day.ComboCounts["alt_tab"] = 11;
        Store.Today.Keys = 7420; Store.Today.Clicks = 2180; Store.Today.ActiveSeconds = 25200; Store.Today.PeakApm = 383;
        Store.Today.ComboCounts["ctrl_c"] = 31; Store.Today.ComboCounts["ctrl_v"] = 24; Store.Today.ComboCounts["alt_tab"] = 11;
        Store.AllTimePeakApm = 383;
        DayRecord old = new DayRecord { Date = DateTime.Today.AddDays(-2), Keys = 5900, ActiveSeconds = 18000, PeakApm = 383 };
        Store.History[day.Date] = day; Store.History[old.Date] = old;
        // 构造一条近 5 分钟的动态曲线,并让最新采样点为 7 APM。
        long[] trend = new long[300];
        for (int i = 0; i < 300; i++)
            trend[i] = i < 24 ? -1 : (long)(7 + 6 * Math.Sin(i / 28.0) + 3 * Math.Sin(i / 9.0));
        typeof(LiveRate).GetField("_trend", Private).SetValue(null, trend);
        typeof(LiveRate).GetField("_trendIndex", Private).SetValue(null, 0);
        typeof(LiveRate).GetField("_kb", Private).SetValue(null, new long[60]);
        typeof(LiveRate).GetField("_ms", Private).SetValue(null, new long[60]);
        long[] kbs = (long[])typeof(LiveRate).GetField("_kb", Private).GetValue(null); kbs[0] = 7;
        try
        {
            FieldInfo trackerField = typeof(ActivityMonitor).GetField("Tracker", BindingFlags.NonPublic | BindingFlags.Static);
            object tracker = trackerField == null ? null : trackerField.GetValue(null);
            PropertyInfo active = tracker == null ? null : tracker.GetType().GetProperty("IsActive", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (active != null) active.SetValue(tracker, true, null);
        }
        catch { }
        using (StatisticsReport report = new StatisticsReport(DateTime.Today, 4))
        {
            report.ClientSize = new Size(1140, 900);
            report.StartPosition = FormStartPosition.Manual; report.Location = new Point(-4000, -4000); report.Show();
            typeof(StatisticsReport).GetMethod("OnLoad", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(report, new object[] { EventArgs.Empty });
            ReportDeck tabs = (ReportDeck)typeof(StatisticsReport).GetField("_tabs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(report);
            tabs.SelectedIndex = 1; Handles(report); report.PerformLayout();
            using (Bitmap bitmap = new Bitmap(report.Width, report.Height))
            {
                report.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(System.IO.Path.Combine(output, "report-live.png"));
            }
        }
        Store.History.Clear(); Store.RollDay(DateTime.Today);
        Console.WriteLine("Report live render: previews/report-live.png written");
    }

    private static void Handles(Control control)
    {
        IntPtr handle = control.Handle;
        foreach (Control child in control.Controls) Handles(child);
    }
}
