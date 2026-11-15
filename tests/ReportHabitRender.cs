using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 「输入习惯」报告页：卡片 / 刻度 / 头部按钮 / 说明抽屉的离屏预览。
/// 只写临时目录，不碰用户统计。
/// </summary>
internal static class ReportHabitRender
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Main()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        System.IO.Directory.CreateDirectory(output);
        Store.History.Clear();
        Store.RollDay(DateTime.Today);
        Store.MouseDpi = 1600;
        for (int i = 0; i < 40; i++)
        {
            DayRecord day = new DayRecord { Date = DateTime.Today.AddDays(-i), Keys = 3500 + (i * 191) % 4100, Clicks = 1400 + i * 11, ActiveSeconds = 14400, Wheel = 2300, MoveMeters = 120 };
            day.ComboCounts["ctrl_c"] = 150;
            day.Apps["editor"] = new AppUsage { ProcessPath = "C:\\Tools\\Code.exe", Keys = 2000, ActiveSeconds = 7000 };
            day.Apps["browser"] = new AppUsage { ProcessPath = "C:\\Tools\\Browser.exe", Keys = 1000, ActiveSeconds = 3200 };
            for (int h = 9; h < 18; h += 2) day.Sessions.Add(new ActiveSession { Start = day.Date.AddHours(h), End = day.Date.AddHours(h).AddMinutes(42), Seconds = 2520 });
            Store.History[day.Date] = day;
        }
        using (StatisticsReport report = new StatisticsReport(DateTime.Today))
        {
            report.ClientSize = new Size(1140, 830);
            report.StartPosition = FormStartPosition.Manual;
            report.Location = new Point(-4000, -4000);
            report.Show();
            IntPtr handle = report.Handle;
            typeof(StatisticsReport).GetMethod("OnLoad", Private).Invoke(report, new object[] { EventArgs.Empty });
            ReportDeck tabs = (ReportDeck)typeof(StatisticsReport).GetField("_tabs", Private).GetValue(report);
            tabs.SelectedIndex = 0;
            CreateHandles(report);
            report.PerformLayout();
            using (Bitmap bitmap = new Bitmap(report.Width, report.Height))
            {
                report.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(System.IO.Path.Combine(output, "report-habit.png"));
            }
            tabs.SelectedTab.DetailsOpen = true;
            tabs.PerformLayout();
            report.PerformLayout();
            using (Bitmap bitmap = new Bitmap(report.Width, report.Height))
            {
                report.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(System.IO.Path.Combine(output, "report-habit-details.png"));
            }
            tabs.SelectedTab.DetailsOpen = false;
            report.PerformLayout();
            ReportHelpOverlay help = new ReportHelpOverlay();
            help.ShowPreview(report, tabs.SelectedTab.Text, ReportDesign.Scope[0], HelpText(), 1f);
            typeof(ReportHelpOverlay).GetField("_progress", Private).SetValue(help, 1f);
            typeof(ReportHelpOverlay).GetField("_direction", Private).SetValue(help, 0);
            help.Invalidate();
            using (Bitmap overlay = new Bitmap(Math.Max(1, help.Width), Math.Max(1, help.Height)))
            using (Graphics g = Graphics.FromImage(overlay))
            {
                typeof(ReportHelpOverlay).GetMethod("OnPaint", Private).Invoke(help,
                    new object[] { new PaintEventArgs(g, new Rectangle(Point.Empty, overlay.Size)) });
                overlay.Save(System.IO.Path.Combine(output, "report-habit-help.png"));
            }
            help.Close();
        }
        Store.History.Clear(); Store.RollDay(DateTime.Today);
        Console.WriteLine("Report habit render: previews/report-habit*.png written");
    }

    private static string HelpText()
    {
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        b.AppendLine("个人基准仅使用所选日之前 30 天的记录，每项指标至少需要 7 个有效日。P50 是中位数；P90 表示 90% 的历史日不超过该值。");
        b.AppendLine("历史最高取全部已记录日；较昨日环比为当前值相对昨天的变化。缺失日不补零，分母为零时显示 —，不当作零值。");
        b.AppendLine();
        b.AppendLine("个人基准指标");
        for (int i = 0; i < ReportDesign.Metrics.Length; i++)
            b.AppendLine("• " + ReportDesign.Metrics[i] + "（" + ReportDesign.Units[i] + "）：" + ReportDesign.Definitions[i]);
        b.AppendLine();
        b.AppendLine("刻度与颜色");
        b.AppendLine("• 灰线 = P50（通常），表示过去 30 天有效日的中位数。");
        b.AppendLine("• 金线 = P90（较高），表示 90% 的历史日不超过它。");
        b.AppendLine("• 当前值超过 P50 后色条转为青色，超过 P90 转为金色；颜色只描述相对区间，不是评分。");
        b.AppendLine("• 六项指标单位不同，色条各自独立缩放，长度不能跨指标横向比较。");
        return b.ToString();
    }

    private static void CreateHandles(Control control)
    {
        IntPtr handle = control.Handle;
        foreach (Control child in control.Controls) CreateHandles(child);
    }
}
