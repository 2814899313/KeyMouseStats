using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 使用洞察页:年度日历(近3月/全年)、会话时间轴、七日×24 矩阵的离屏验收。
/// 数据为 40 天合成作息,不碰用户数据。
/// </summary>
internal static class InsightsRender
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, Private).SetValue(target, value);
    }

    private static void Main()
    {
        try { Run(); }
        catch (Exception error)
        {
            string path = Path.Combine(Path.GetTempPath(), "insights-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Insights render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static ActiveSession Period(DateTime day, int startHour, int startMinute, int endHour, int endMinute)
    {
        ActiveSession session = new ActiveSession();
        session.Start = day.AddHours(startHour).AddMinutes(startMinute);
        session.End = day.AddHours(endHour).AddMinutes(endMinute);
        session.Seconds = (session.End - session.Start).TotalSeconds;
        return session;
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "InsightsRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            Random random = new Random(5);
            for (int back = 39; back >= 0; back--)
            {
                DateTime date = DateTime.Today.AddDays(-back);
                DayRecord day = new DayRecord();
                day.Date = date;
                int[] hours = { 9, 10, 11, 13, 14, 15, 16, 20, 21 };
                foreach (int h in hours) day.ActiveHours[h] = (40 + random.Next(0, 21)) * 60;
                double active = 0;
                foreach (double seconds in day.ActiveHours) active += seconds;
                day.ActiveSeconds = active;
                day.IdleSeconds = (back == 0 ? 9000 : 7200) + random.Next(0, 900);
                day.Keys = (long)(active / 2);
                day.Sessions.Add(Period(date, 9, 0, 12, 10));
                day.Sessions.Add(Period(date, 13, 30, 18, 0));
                day.Sessions.Add(Period(date, 20, 0, 22, 15));
                day.IdlePeriods.Add(Period(date, 12, 15, 13, 25));
                day.IdlePeriods.Add(Period(date, 18, 10, 19, 55));
                day.IdlePeriods.Add(Period(date, 22, 20, 23, 40));
                Store.History[day.Date] = day;
                Store.Total.Keys += day.Keys;
            }
            Store.RollDay(DateTime.Today);
            Directory.CreateDirectory(output);

            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
                SetField(form, "_s", 1f);
                form.ClientSize = new Size(1072, 724);
                object insights = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 5);
                SetField(form, "_tab", insights);
                SetField(form, "_insightDay", DateTime.Today);

                SetField(form, "_insightView", 0);
                SetField(form, "_calendarSpan", 0);
                Render(form, "insights-calendar-3m", 1f);
                SetField(form, "_calendarSpan", 2);
                Render(form, "insights-calendar-year", 1f);

                SetField(form, "_insightView", 1);
                Render(form, "insights-timeline", 1f);
                Render(form, "insights-timeline-150", 1.5f);

                SetField(form, "_insightView", 2);
                Render(form, "insights-week", 1f);

                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Console.WriteLine("Insights render: previews/insights-*.png written");
        }
        finally
        {
            try { foreach (string file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); } catch { }
        }
    }

    private static void Render(Dashboard form, string name, float scale)
    {
        SetField(form, "_s", scale);
        using (Bitmap bitmap = new Bitmap((int)(1072 * scale), (int)(724 * scale)))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            typeof(Dashboard).GetMethod("OnPaint", Private).Invoke(form,
                new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)) });
            bitmap.Save(Path.Combine(OutputDirectory(), name + ".png"), ImageFormat.Png);
        }
    }

    private static string OutputDirectory()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        Directory.CreateDirectory(output);
        return output;
    }
}
