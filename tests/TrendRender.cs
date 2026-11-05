using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 趋势页「今天还没结束」的离屏验收:末端虚线 + 「今日截至 HH:mm · 累计中」标签,
/// 以及底部「较昨日同期」卡片。数据是合成的,不碰用户数据。
/// </summary>
internal static class TrendRender
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
            string path = Path.Combine(Path.GetTempPath(), "trend-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Trend render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TrendRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            Random random = new Random(11);
            DateTime now = DateTime.Now;
            for (int back = 13; back >= 0; back--)
            {
                // 10-23 到 10-25(back 12–10)模拟「应用/电脑没运行」:完全不落盘,趋势页应画灰带并断线。
                if (back == 12 || back == 11 || back == 10) continue;
                DayRecord day = new DayRecord();
                day.Date = DateTime.Today.AddDays(-back);
                bool today = back == 0;
                bool noTyping = back == 7;   // 有记录但当天没打字:0 点 + 鼠标/滚轮仍算记录
                int hours = today ? Math.Max(1, now.Hour) : 24;
                for (int h = 0; h < 24; h++)
                {
                    if (h >= hours) break;
                    // 上午高、晚上低;今天只填已完成的小时,当前小时给一小部分。
                    int baseValue = 420 + (int)(160 * Math.Sin(h / 5.0)) + random.Next(0, 60);
                    int value = today && h == now.Hour ? baseValue / 3 : baseValue;
                    if (noTyping) value = 0;
                    day.HourKeys[h] = value;
                    day.HourClicks[h] = noTyping ? 0 : value / 4;
                    day.Keys += value;
                    day.Clicks += noTyping ? 0 : value / 4;
                    day.Wheel += noTyping ? 4 : value / 10;
                    day.MoveMeters += noTyping ? 1.2 : value * 0.002;
                    // 每小时活跃分钟数(存秒):40–60 分钟,用来验证 0–60 的纵轴。
                    int minutes = noTyping ? 55 : Math.Min(60, 35 + h % 20);
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
                object trend = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 1);
                SetField(form, "_tab", trend);
                SetField(form, "_trendRange", 14);
                SetField(form, "_trendMetric", 0);
                Render(form, "trend-partial", 1f);

                // 悬停「未运行」的那一天:提示框应说明没有记录,而不是显示 0 击键。
                FieldInfo hoverField = typeof(Dashboard).GetField("_hover", Private);
                hoverField.SetValue(form, 3);
                Render(form, "trend-partial-missing-hover", 1f);

                // 悬停今天的点:提示框应明确写「进行中 / 截至 HH:mm」。
                hoverField.SetValue(form, 13);
                Render(form, "trend-partial-hover", 1f);
                hoverField.SetValue(form, -1);
                Render(form, "trend-partial-150", 1.5f);

                // 近 30 天:左边一整段没有记录的日期都应变成灰带。
                SetField(form, "_trendRange", 30);
                Render(form, "trend-partial-30", 1f);
                SetField(form, "_trendRange", 14);

                // 点击指标:同样的处理。
                SetField(form, "_trendMetric", 1);
                Render(form, "trend-partial-clicks", 1f);

                // 按小时:活跃时长模式的纵轴必须锁在 0–60 分钟。
                SetField(form, "_trendHourly", true);
                SetField(form, "_hourlyTrendDay", DateTime.Today);
                SetField(form, "_hourlyCompareMode", 0);
                SetField(form, "_hourlyTrendMetric", 2);
                Render(form, "trend-hourly-active", 1f);
                SetField(form, "_hourlyTrendMetric", 0);
                Render(form, "trend-hourly-keys", 1f);
                SetField(form, "_hourlyTrendMetric", 3);
                Render(form, "trend-hourly-kpm", 1f);
                SetField(form, "_trendHourly", false);
                SetField(form, "_hourlyTrendMetric", 0);

                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Console.WriteLine("Trend render: previews/trend-partial*.png written");
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
