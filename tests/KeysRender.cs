using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 按键排行页的两级控制验收:主视图(按键排行 / 组合键排行 / 全键热力图)与二级筛选(时间 + 指标)。
/// 数据为合成按键计数,不碰用户数据。
/// </summary>
internal static class KeysRender
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
            string path = Path.Combine(Path.GetTempPath(), "keys-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Keys render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "KeysRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            Random random = new Random(7);
            for (int back = 6; back >= 0; back--)
            {
                DayRecord day = new DayRecord();
                day.Date = DateTime.Today.AddDays(-back);
                day.ActiveSeconds = 3600;
                int[] top = { 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 32 };
                for (int i = 0; i < top.Length; i++)
                    day.KeyCounts[top[i]] = 1200 - i * 90 + random.Next(0, 30);
                day.KeyCounts[17] = 900;   // Ctrl
                day.KeyCounts[16] = 700;   // Shift
                day.ComboCounts["Ctrl+C"] = 220;
                day.ComboCounts["Ctrl+V"] = 180;
                day.ComboCounts["Ctrl+S"] = 90;
                day.ComboCounts["Alt+Tab"] = 40;
                // 按住时长样本:让分布直方图与「平均按住」列都有数据。
                double[] holds = { 35, 80, 150, 320, 700, 1400, 3000, 6000 };
                int[] bucketCounts = { 420, 380, 300, 180, 90, 40, 12, 3 };
                for (int i = 0; i < 6; i++)
                {
                    KeyHold hold = new KeyHold();
                    hold.Count = 30 + i * 4;
                    hold.TotalMs = hold.Count * holds[i + 1];
                    hold.MaxMs = holds[i + 1] * 2.4;
                    day.Holds[top[i]] = hold;
                }
                for (int b = 0; b < bucketCounts.Length; b++) day.HoldBuckets[b] = bucketCounts[b];
                foreach (long bucket in bucketCounts) day.HoldCount += bucket;
                day.HoldTotalMs = 420 * 30 + 380 * 80 + 300 * 150 + 180 * 320 + 90 * 700 + 40 * 1400 + 12 * 3000 + 3 * 6000;
                day.HoldMaxMs = 7200;
                day.HoldActiveSeconds = 900;
                day.Keys = 0;
                foreach (KeyValuePair<int, long> pair in day.KeyCounts) day.Keys += pair.Value;
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
                object keys = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 3);
                SetField(form, "_tab", keys);
                SetField(form, "_keyRange", 2);   // 近 7 天

                SetField(form, "_keyView", 0);
                SetField(form, "_keyMetric", 0);
                Render(form, "keys-list", 1f);
                SetField(form, "_keyMetric", 1);
                Render(form, "keys-hold", 1f);
                SetField(form, "_mouse", new Point(845, 350));
                Render(form, "keys-hold-hover", 1f);
                SetField(form, "_mouse", new Point(-100, -100));

                SetField(form, "_keyView", 1);
                SetField(form, "_keyMetric", 0);
                Render(form, "keys-combos", 1f);

                SetField(form, "_keyView", 2);
                SetField(form, "_keyMetric", 0);
                Render(form, "keys-heat", 1f);
                SetField(form, "_keyMetric", 1);
                Render(form, "keys-heat-hold", 1f);
                SetField(form, "_keyMetric", 0);
                Render(form, "keys-heat-150", 1.5f);

                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Console.WriteLine("Keys render: previews/keys-*.png written");
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
