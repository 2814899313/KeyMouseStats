using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 时段分布页的离屏验收:热力融进柱子、周×24 矩阵、矩阵悬停、150% 与空状态。
/// 数据为 90 天合成作息(工作日白天高、周末中午高),不碰用户数据。
/// </summary>
internal static class HoursRender
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
            string path = Path.Combine(Path.GetTempPath(), "hours-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Hours render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "HoursRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            DateTime now = DateTime.Now;
            for (int back = 89; back >= 0; back--)
            {
                DateTime date = DateTime.Today.AddDays(-back);
                // 每隔十几天缺一天记录,矩阵分母只算有记录日。
                if (back > 0 && back % 13 == 0) continue;
                DayRecord day = new DayRecord();
                day.Date = date;
                int weekday = ((int)date.DayOfWeek + 6) % 7;
                int lastHour = back == 0 ? Math.Max(1, now.Hour) : 23;
                for (int h = 0; h < 24; h++)
                {
                    if (h > lastHour) break;
                    int value;
                    if (weekday >= 5) value = h >= 11 && h < 15 ? 260 : h >= 9 && h < 23 ? 60 : 10;
                    else value = h >= 9 && h < 18 ? 420 : h >= 19 && h < 23 ? 180 : 15;
                    if (back == 0 && h == now.Hour) value /= 4;
                    day.HourKeys[h] = value;
                    day.HourClicks[h] = value / 6;
                    day.Keys += value;
                    day.Clicks += value / 6;
                    day.Wheel += value / 20;
                    day.MoveMeters += value * 0.001;
                    double minutes = Math.Min(60, 12 + value / 12.0);
                    day.ActiveHours[h] = minutes * 60;
                    day.ActiveSeconds += minutes * 60;
                }
                Store.History[day.Date] = day;
                Store.Total.Keys += day.Keys;
                Store.Total.Clicks += day.Clicks;
            }
            Store.RollDay(DateTime.Today);
            Directory.CreateDirectory(output);

            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
                SetField(form, "_s", 1f);
                form.ClientSize = new Size(1072, 724);
                object hours = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 2);
                SetField(form, "_tab", hours);
                SetField(form, "_hourMetric", 0);

                SetField(form, "_hourView", 0);
                SetField(form, "_hourRange", 2);
                Render(form, "hours-bars", 1f);
                SetField(form, "_hover", 10);
                Render(form, "hours-bars-hover", 1f);
                SetField(form, "_hover", -1);
                SetField(form, "_hourRange", 0);
                Render(form, "hours-bars-today", 1f);
                SetField(form, "_hourRange", 1);
                Render(form, "hours-bars-week", 1f);
                SetField(form, "_hourRange", 2);
                Render(form, "hours-bars-150", 1.5f);

                SetField(form, "_hourView", 1);
                Render(form, "hours-matrix", 1f);
                SetField(form, "_mouse", new Point(586, 372));
                Render(form, "hours-matrix-hover", 1f);
                SetField(form, "_mouse", new Point(-100, -100));
                SetField(form, "_hourRange", 1);
                Render(form, "hours-matrix-week", 1f);
                SetField(form, "_hourRange", 0);
                Render(form, "hours-matrix-today", 1f);
                SetField(form, "_hourRange", 2);
                Render(form, "hours-matrix-150", 1.5f);

                // 空状态:矩阵不能画成一块死色。
                Store.History.Clear();
                Store.Total = new Counters();
                Store.RollDay(DateTime.Today);
                Render(form, "hours-matrix-empty", 1f);

                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Console.WriteLine("Hours render: previews/hours-*.png written");
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
