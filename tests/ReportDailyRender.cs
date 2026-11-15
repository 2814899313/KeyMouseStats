using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>每日趋势页:只有 14/30 天数据时的空轴、周锚点、十字准星与紧凑视图。</summary>
internal static class ReportDailyRender
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Main()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        System.IO.Directory.CreateDirectory(output);
        Store.History.Clear(); Store.RollDay(DateTime.Today); Store.MouseDpi = 1600;
        // 最近 14 天有记录，再往前的 16 天空白:复现“左空右满”。
        for (int i = 0; i < 14; i++)
        {
            DayRecord day = new DayRecord { Date = DateTime.Today.AddDays(-i), Keys = 6200 + (i * 431) % 2600, Clicks = 1800 + i * 7, ActiveSeconds = 16000 + i * 120, Wheel = 900 };
            day.ComboCounts["ctrl_c"] = 90;
            Store.History[day.Date] = day;
        }
        using (StatisticsReport report = new StatisticsReport(DateTime.Today, 1))
        {
            report.ClientSize = new Size(1140, 900);
            report.StartPosition = FormStartPosition.Manual;
            report.Location = new Point(-4000, -4000);
            report.Show();
            typeof(StatisticsReport).GetMethod("OnLoad", Private).Invoke(report, new object[] { EventArgs.Empty });
            ReportDeck tabs = (ReportDeck)typeof(StatisticsReport).GetField("_tabs", Private).GetValue(report);
            tabs.SelectedIndex = 1;
            CreateHandles(report);
            report.PerformLayout();
            Save(report, System.IO.Path.Combine(output, "report-daily.png"));
            ReportVisual chart = (ReportVisual)tabs.SelectedTab.Chart;
            RectangleF toggle = (RectangleF)typeof(ReportVisual).GetField("dailyToggle", Private).GetValue(chart);
            RectangleF plot = (RectangleF)typeof(ReportVisual).GetField("dailyChart", Private).GetValue(chart);
            MouseEventArgs hover = new MouseEventArgs(MouseButtons.None, 1, (int)(plot.X + plot.Width * 0.76f), (int)(plot.Y + plot.Height / 2), 0);
            typeof(ReportVisual).GetMethod("OnMouseMove", Private).Invoke(chart, new object[] { hover });
            Save(report, System.IO.Path.Combine(output, "report-daily-hover.png"));
            MouseEventArgs click = new MouseEventArgs(MouseButtons.Left, 1, (int)(toggle.X + toggle.Width * 0.75f), (int)(toggle.Y + toggle.Height / 2), 0);
            typeof(ReportVisual).GetMethod("OnMouseDown", Private).Invoke(chart, new object[] { click });
            Save(report, System.IO.Path.Combine(output, "report-daily-compact.png"));
        }
        Store.History.Clear(); Store.RollDay(DateTime.Today);
        Console.WriteLine("Report daily render: previews/report-daily*.png written");
    }

    private static void Save(StatisticsReport report, string path)
    {
        report.PerformLayout();
        using (Bitmap bitmap = new Bitmap(report.Width, report.Height))
        {
            report.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(path);
        }
    }

    private static void CreateHandles(Control control)
    {
        IntPtr handle = control.Handle;
        foreach (Control child in control.Controls) CreateHandles(child);
    }
}
