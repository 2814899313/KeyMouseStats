using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 字符内容页:区间派生指标、高频单字/词组、标点过滤、最近上屏隐私模糊。
/// 合成一份临时 commit_log.csv,通过 PathOverride 读取,不碰用户真实输入记录。
/// </summary>
internal static class TextRender
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
            string path = Path.Combine(Path.GetTempPath(), "text-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Text render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TextRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        string log = Path.Combine(dir, "commit_log.csv");
        try
        {
            string[] phrases =
            {
                "使用洞察界面", "怎么开启键盘灯常量", "查到了么", "我们明天再对一下这个方案",
                "键盘", "界面", "开启", "这个", "方案", "明天", "怎么", "一下",
                "键盘灯", "洞察", "统计", "输入法", "今天", "工作", "休息", "修改",
                "好的", "收到", "没问题", "继续", "键盘热力", "应用统计"
            };
            StringBuilder csv = new StringBuilder();
            DateTime start = DateTime.Today.AddDays(-29).AddHours(9);
            for (int i = 0; i < 900; i++)
            {
                string text = phrases[i % phrases.Length];
                if (i % 7 == 0) text += "，";
                DateTime time = start.AddMinutes(i * 45);
                if (time.Date > DateTime.Today) break;
                csv.Append(time.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                   .Append(text.Length).Append(',').Append(text).AppendLine();
            }
            for (int i = 0; i < 24; i++)
            {
                DateTime time = DateTime.Today.AddHours(9).AddMinutes(i * 12);
                if (time > DateTime.Now.AddMinutes(-5)) break;
                string text = phrases[(i * 3) % phrases.Length];
                csv.Append(time.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                   .Append(text.Length).Append(',').Append(text).AppendLine();
            }
            File.WriteAllText(log, csv.ToString(), new UTF8Encoding(false));
            RimeTextStats.PathOverride = log;

            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
                SetField(form, "_s", 1f);
                form.ClientSize = new Size(1072, 724);
                object text = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 6);
                SetField(form, "_tab", text);
                SetField(form, "_textRange", 1);   // 近 7 天
                SetField(form, "_textChart", 0);

                SetField(form, "_textWordTab", 0);
                SetField(form, "_textCharFilter", 0);
                Render(form, "text-chars", 1f);
                SetField(form, "_textCharFilter", 1);
                Render(form, "text-chars-punct", 1f);
                SetField(form, "_textWordTab", 1);
                Render(form, "text-phrases", 1f);

                SetField(form, "_textBlurRecent", true);
                Render(form, "text-blur", 1f);
                SetField(form, "_textBlurRecent", false);

                SetField(form, "_textRange", 0);   // 今日:应自动切到时段
                SetField(form, "_textChart", 1);
                Render(form, "text-today", 1f);

                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Console.WriteLine("Text render: previews/text-*.png written");
        }
        finally
        {
            RimeTextStats.PathOverride = null;
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
