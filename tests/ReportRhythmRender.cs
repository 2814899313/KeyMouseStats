using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>使用节奏页:强度时间轴、当前时间游标、段悬浮与两个下半区模块。</summary>
internal static class ReportRhythmRender
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Main()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        System.IO.Directory.CreateDirectory(output);
        Store.History.Clear(); Store.RollDay(DateTime.Today); Store.MouseDpi = 1600;
        DayRecord day = new DayRecord { Date = DateTime.Today, Keys = 6820, Clicks = 2150, ActiveSeconds = 24600, Wheel = 900 };
        int[][] pattern = new int[][]
        {
            new int[]{8,12}, new int[]{9,5}, new int[]{9,40}, new int[]{10,5},
            new int[]{10,30}, new int[]{11,20}, new int[]{13,10}, new int[]{14,15},
            new int[]{14,50}, new int[]{15,5}, new int[]{15,40}, new int[]{16,20},
            new int[]{17,0}, new int[]{17,35}, new int[]{18,10}, new int[]{19,0},
            new int[]{19,25}, new int[]{20,5}, new int[]{20,40}, new int[]{21,0}
        };
        for (int i = 0; i < pattern.Length; i++)
        {
            DateTime start = DateTime.Today.AddHours(pattern[i][0]).AddMinutes(pattern[i][1]);
            double minutes = i % 5 == 0 ? 95 : i % 3 == 0 ? 42 : i % 4 == 0 ? 18 : 6;
            DateTime end = start.AddMinutes(minutes);
            day.Sessions.Add(new ActiveSession { Start = start, End = end, Seconds = minutes * 60 });
            for (int h = start.Hour; h <= end.Hour && h < 24; h++)
            {
                DateTime hourStart = DateTime.Today.AddHours(h), hourEnd = DateTime.Today.AddHours(h + 1);
                DateTime from = start > hourStart ? start : hourStart, to = end < hourEnd ? end : hourEnd;
                double overlap = (to - from).TotalSeconds;
                if (overlap > 0) { day.ActiveHours[h] += overlap; day.HourKeys[h] += (long)(overlap * (i % 4 == 0 ? 1.5 : .6)); day.HourClicks[h] += (long)(overlap * .22); }
            }
        }
        Store.History[day.Date] = day;
        using (StatisticsReport report = new StatisticsReport(DateTime.Today, 3))
        {
            report.ClientSize = new Size(1140, 980);
            report.StartPosition = FormStartPosition.Manual; report.Location = new Point(-4000, -4000); report.Show();
            typeof(StatisticsReport).GetMethod("OnLoad", Private).Invoke(report, new object[] { EventArgs.Empty });
            ReportDeck tabs = (ReportDeck)typeof(StatisticsReport).GetField("_tabs", Private).GetValue(report);
            tabs.SelectedIndex = 3; CreateHandles(report); report.PerformLayout();
            Save(report, System.IO.Path.Combine(output, "report-rhythm.png"));
            ReportVisual chart = (ReportVisual)tabs.SelectedTab.Chart;
            System.Collections.Generic.List<RectangleF> rects = (System.Collections.Generic.List<RectangleF>)typeof(ReportVisual).GetField("rhythmRects", Private).GetValue(chart);
            if (rects.Count > 0)
            {
                RectangleF target = rects[Math.Min(3, rects.Count - 1)];
                MouseEventArgs hover = new MouseEventArgs(MouseButtons.None, 1, (int)(target.X + target.Width / 2), (int)(target.Y + target.Height / 2), 0);
                typeof(ReportVisual).GetMethod("OnMouseMove", Private).Invoke(chart, new object[] { hover });
                Save(report, System.IO.Path.Combine(output, "report-rhythm-hover.png"));
            }
        }
        Store.History.Clear(); Store.RollDay(DateTime.Today);
        Console.WriteLine("Report rhythm render: previews/report-rhythm*.png written");
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
