using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 应用统计页的离屏渲染验收:次数列表 / APM 交叉分析 / 时长占比环图 / 悬停 / 150% / 空状态。
/// 不装钩子、不碰用户数据(数据目录临时重定向)。
/// </summary>
internal static class AppsRender
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
            string path = Path.Combine(Path.GetTempPath(), "apps-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Apps render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AppsRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            AppActivity.Rules.Clear();
            // 用系统自带的 exe,才能看到真实的 16×16 图标;分类用显式应用规则钉死。
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            object[][] apps =
            {
                new object[] { Path.Combine(system, "notepad.exe"), "main.cs - 记事本", 4800, 420, 3600.0, 0 },
                new object[] { Path.Combine(system, "cmd.exe"), "命令提示符", 300, 260, 1800.0, 0 },
                new object[] { Path.Combine(system, "mspaint.exe"), "未命名 - 画图", 220, 3200, 1200.0, 4 },
                new object[] { Path.Combine(system, "control.exe"), "控制面板", 1800, 120, 900.0, 5 },
                new object[] { Path.Combine(system, "explorer.exe"), "文件资源管理器", 600, 180, 700.0, 3 },
                new object[] { Path.Combine(system, "charmap.exe"), "字符映射表", 60, 20, 45.0, 4 },
                new object[] { Path.Combine(system, "mmc.exe"), "控制台", 20, 10, 15.0, 4 }
            };
            foreach (object[] app in apps) AppActivity.Rules[AppActivity.AppRuleKey((string)app[0])] = (int)app[5];
            for (int back = 6; back >= 0; back--)
            {
                DayRecord day = new DayRecord();
                day.Date = DateTime.Today.AddDays(-back);
                day.ActiveSeconds = 2400;
                day.AppObservedSeconds = 7200;
                double factor = 0.75 + (6 - back) * 0.04;
                foreach (object[] app in apps)
                {
                    AppUsage usage = new AppUsage
                    {
                        ProcessPath = (string)app[0],
                        Title = (string)app[1],
                        Keys = (long)((int)app[2] * factor),
                        Clicks = (long)((int)app[3] * factor),
                        Wheel = (long)((int)app[3] * factor / 4),
                        ActiveSeconds = (double)app[4] * factor
                    };
                    day.Apps[usage.Id] = usage;
                }
                Store.History[day.Date] = day;
            }
            Store.RollDay(DateTime.Today);
            Directory.CreateDirectory(output);

            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
                SetField(form, "_s", 1f);
                form.ClientSize = new Size(1072, 724);
                object appsTab = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 4);
                SetField(form, "_tab", appsTab);
                SetField(form, "_appDays", 7);

                SetField(form, "_appView", 0);
                Render(form, "apps-list", 1f);
                SetField(form, "_appCategory", 5);
                Render(form, "apps-category", 1f);
                SetField(form, "_appCategory", -1);
                SetField(form, "_appKeyGroups", true);
                Render(form, "apps-keygroups", 1f);
                SetField(form, "_appKeyGroups", false);

                SetField(form, "_appView", 1);
                Render(form, "apps-apm", 1f);
                SetField(form, "_mouse", new Point(500, 410));
                Render(form, "apps-apm-hover", 1f);
                SetField(form, "_mouse", new Point(-100, -100));
                Render(form, "apps-apm-150", 1.5f);

                SetField(form, "_appView", 2);
                Render(form, "apps-share", 1f);
                SetField(form, "_mouse", new Point(576, 344));
                Render(form, "apps-share-hover", 1f);
                SetField(form, "_mouse", new Point(-100, -100));
                SetField(form, "_mouse", new Point(500, 410));
                Render(form, "apps-share-hover", 1f);
                SetField(form, "_mouse", new Point(420, 500));
                Render(form, "apps-share-donut-hover", 1f);
                SetField(form, "_mouse", new Point(-100, -100));
                Render(form, "apps-share-150", 1.5f);

                // 空状态:清空后画 APM 与占比。
                Store.History.Clear();
                Store.Total = new Counters();
                Store.RollDay(DateTime.Today);
                SetField(form, "_appView", 1);
                Render(form, "apps-apm-empty", 1f);
                SetField(form, "_appView", 2);
                Render(form, "apps-share-empty", 1f);

                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Console.WriteLine("Apps render: previews/apps-*.png written");
        }
        finally
        {
            try { foreach (string file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); } catch { }
            AppActivity.Rules.Clear();
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
