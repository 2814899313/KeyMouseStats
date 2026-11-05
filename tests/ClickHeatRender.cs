using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 点击热区页的离屏渲染验收:用合成数据画 今日 / 近 7 天 / 近 30 天 / 悬停 / 单屏放大 / 空状态,
/// 输出到 previews/。不装钩子、不碰用户数据(数据目录临时重定向)。
/// </summary>
internal static class ClickHeatRender
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private static void SetField(object target, string name, object value)
    {
        Type type = target == null ? typeof(Dashboard) : target.GetType();
        type.GetField(name, Private).SetValue(target, value);
    }

    private static void Main()
    {
        // 与 Program.Main 一致:按物理像素取屏幕布局,否则混合 DPI 下渲染的是虚拟化坐标。
        try { SetProcessDPIAware(); } catch { }
        try { Run(); }
        catch (Exception error)
        {
            string path = Path.Combine(Path.GetTempPath(), "clickheat-render-error.txt");
            try { File.WriteAllText(path, error.ToString()); } catch { }
            Console.WriteLine("Click heat render failed, see " + path);
            Environment.ExitCode = 1;
        }
    }

    private static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ClickHeatRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Store.DataDirectory = dir;
        Rectangle screen = SystemInformation.VirtualScreen;
        if (screen.Width <= 0) screen = new Rectangle(0, 0, 1920, 1080);
        try
        {
            Store.History.Clear();
            Store.Total = new Counters();
            Random random = new Random(20261104);
            // 按本机真实显示器布局生成点击:不同分辨率/不同 DPI 的屏幕各画各的热点,
            // 屏与屏之间的空隙不该出现任何格子。
            for (int back = 29; back >= 0; back--)
            {
                DayRecord day = new DayRecord();
                day.Date = DateTime.Today.AddDays(-back);
                int clicks = 260 + random.Next(180);
                int screenTotal = 0;
                foreach (Screen real in Screen.AllScreens)
                {
                    Rectangle area = real.Bounds;
                    int weight = Math.Max(1, area.Width * area.Height / 1000000);
                    screenTotal += weight;
                }
                foreach (Screen real in Screen.AllScreens)
                {
                    Rectangle area = real.Bounds;
                    int weight = Math.Max(1, area.Width * area.Height / 1000000);
                    int budget = Math.Max(30, clicks * weight / Math.Max(1, screenTotal));
                    AddCluster(day, random, area, area.Left + 24, area.Top + 80, Math.Max(40, area.Width / 12), area.Height / 2, budget / 4);
                    AddCluster(day, random, area, area.Left + area.Width / 5, area.Top + area.Height / 4, area.Width / 2, area.Height / 3, budget / 3);
                    AddCluster(day, random, area, area.Right - 140, area.Top + 20, 100, 40, budget / 6);
                    AddCluster(day, random, area, area.Left + area.Width / 8, area.Bottom - 110, area.Width / 2, 50, budget / 5);
                    AddCluster(day, random, area, area.Left + area.Width * 3 / 5, area.Top + area.Height * 2 / 3, area.Width / 5, area.Height / 6, budget / 5);
                }
                int used = 0;
                foreach (KeyValuePair<int, long> pair in day.ClickCells) used += (int)pair.Value;
                day.Clicks = Math.Max(used, clicks);
                Store.History[day.Date] = day;
                Store.Total.Clicks += day.Clicks;
                Store.Total.Left += day.Clicks;
            }
            Store.RollDay(DateTime.Today);
            string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
            if (string.IsNullOrEmpty(output)) output = "previews";
            Directory.CreateDirectory(output);

            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
                SetField(form, "_s", 1f);
                form.ClientSize = new Size(1072, 724);
                object heat = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 8);
                SetField(form, "_tab", heat);

                SetField(form, "_clickRange", 2);
                Render(form, "click-heat-30", 1f);
                SetField(form, "_clickSmooth", true);
                Render(form, "click-heat-smooth", 1f);
                SetField(form, "_clickSmooth", false);
                // 九宫格悬停联动:鼠标放到「上中」格,主图应出现对应聚光矩形。
                SetField(form, "_mouse", new Point(922, 280));
                Render(form, "click-heat-grid-hover", 1f);
                SetField(form, "_mouse", new Point(-100, -100));
                SetField(form, "_clickRange", 1);
                Render(form, "click-heat-7", 1f);
                SetField(form, "_clickRange", 0);
                Render(form, "click-heat-today", 1f);

                // 悬停到中间那团最热的位置:先画一帧填好格子矩形,再进行命中。
                Render(form, "click-heat-today", 1f);
                IList<RectangleF> rects = (IList<RectangleF>)typeof(Dashboard).GetField("_clickCellRects", Private).GetValue(form);
                IList<long> counts = (IList<long>)typeof(Dashboard).GetField("_clickCellCounts", Private).GetValue(form);
                int best = -1;
                for (int i = 0; i < rects.Count && i < counts.Count; i++)
                    if (best < 0 || counts[i] > counts[best]) best = i;
                if (best >= 0)
                {
                    RectangleF cell = rects[best];
                    SetField(form, "_mouse", new Point((int)(cell.X + cell.Width / 2 + 176), (int)(cell.Y + cell.Height / 2 + 64)));
                    Render(form, "click-heat-hover", 1f);
                    string detail = (string)form.GetType().GetField("_hoverDetail", Private).GetValue(form);
                    if (string.IsNullOrEmpty(detail)) throw new Exception("hovering a click cell must produce a tooltip detail");
                    Console.WriteLine("Click heat hover: " + detail);
                    SetField(form, "_mouse", new Point(-100, -100));
                }
                SetField(form, "_clickRange", 2);
                Render(form, "click-heat-150", 1.5f);

                // 单屏放大:分别聚焦每一块真实屏幕,验证九宫格/格子尺度都跟着换。
                SetField(form, "_clickFocused", true);
                Screen[] screens = Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    SetField(form, "_clickScreen", i);
                    Render(form, "click-heat-focus-" + (i + 1), 1f);
                }
                SetField(form, "_clickFocused", false);
                SetField(form, "_clickScreen", -1);

                // 换两套深/浅主题各画一帧,确认新页不依赖主题图标数组的长度。
                Store.ThemeId = 2;
                Render(form, "click-heat-theme-ori", 1f);
                Store.ThemeId = 5;
                Render(form, "click-heat-theme-nikki", 1f);
                Store.ThemeId = 0;
                Render(form, "click-heat-theme-0", 1f);

                // 空状态:清空后按今日重画。
                Store.History.Clear();
                Store.Total = new Counters();
                Store.RollDay(DateTime.Today);
                SetField(form, "_clickRange", 0);
                Render(form, "click-heat-empty", 1f);
                SetField(form, "_clickFocused", true);
                Render(form, "click-heat-empty-focus", 1f);

                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Console.WriteLine("Click heat render: previews/click-heat-*.png written");
        }
        finally
        {
            try { foreach (string file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); } catch { }
        }
    }

    private static void AddCluster(DayRecord day, Random random, Rectangle screen, int x, int y, int w, int h, int count)
    {
        // 用几枚「热点」加抖动,模拟真实点击的重复落点。
        int[] hotX = new int[8];
        int[] hotY = new int[8];
        for (int i = 0; i < hotX.Length; i++)
        {
            hotX[i] = x + random.Next(Math.Max(1, w));
            hotY[i] = y + random.Next(Math.Max(1, h));
        }
        for (int i = 0; i < count; i++)
        {
            int pick = random.Next(hotX.Length);
            int px = hotX[pick] + random.Next(-10, 11);
            int py = hotY[pick] + random.Next(-10, 11);
            if (px < screen.Left || py < screen.Top || px >= screen.Right || py >= screen.Bottom) continue;
            int cellX, cellY;
            int key = ClickHeat.CellOf(px, py, out cellX, out cellY);
            long old;
            day.ClickCells.TryGetValue(key, out old);
            day.ClickCells[key] = old + 1;
        }
    }

    private static string OutputDirectory()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        Directory.CreateDirectory(output);
        return output;
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
}
