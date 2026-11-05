using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 小组件「存储」页:用合成目录渲染 248×208 的离屏预览,不扫描真实数据。
/// 程序本体用等长临时文件模拟,避免预览里出现测试 exe 的几十 KB。
/// </summary>
internal static class WidgetStorageRender
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
            string path = Path.Combine(Path.GetTempPath(), "widget-storage-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Widget storage render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static void WriteSized(string path, long bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength(bytes);
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WidgetStorage-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(dir, "data");
        Directory.CreateDirectory(data);
        Store.DataDirectory = data;
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        try
        {
            string exe = Path.Combine(dir, "KeyMouseStats.exe");
            WriteSized(exe, 30L * 1024 * 1024);
            WriteSized(Path.Combine(data, "stats.txt"), 3L * 1024 * 1024);
            WriteSized(Path.Combine(data, "stats.txt.bak"), 3L * 1024 * 1024);
            for (int i = 0; i < 3; i++) WriteSized(Path.Combine(data, "backup", "stats-2026110" + (i + 1) + ".txt"), 5770000);
            WriteSized(Path.Combine(data, "日报", "2026-11-05.md"), 9000);
            string rime = Path.Combine(dir, "commit_log.csv");
            WriteSized(rime, 178000);
            StorageUsage usage = StorageUsage.Measure(exe, data, rime);

            using (StatsWidget form = new StatsWidget())
            {
                form.ClientSize = new Size(248, 208);
                SetField(form, "_s", 1f);
                SetField(form, "_fTitle", new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold));
                SetField(form, "_fMode", new Font("Microsoft YaHei UI", 8.25f));
                SetField(form, "_fLabel", new Font("Microsoft YaHei UI", 8.25f));
                SetField(form, "_fValue", new Font("Segoe UI", 13.5f, FontStyle.Bold));
                SetField(form, "_mode", 3);
                SetField(form, "_storage", usage);
                SetField(form, "_storageAt", DateTime.Now);

                using (Bitmap bitmap = new Bitmap(248, 208))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    using (SolidBrush backdrop = new SolidBrush(ArtTheme.Current.Background)) graphics.FillRectangle(backdrop, 0, 0, 248, 208);
                    typeof(StatsWidget).GetMethod("OnPaint", Private).Invoke(form,
                        new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, 248, 208)) });
                    Directory.CreateDirectory(output);
                    bitmap.Save(Path.Combine(output, "widget-storage-preview.png"), ImageFormat.Png);
                }
            }
            Console.WriteLine("Widget storage render: previews/widget-storage-preview.png written");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
