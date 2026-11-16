using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>主面板新增可视化验收:总览基线、趋势区间对比、时段页连续段强度。</summary>
internal static class DashboardAdditionsRender
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(Dashboard form, string name, object value) { typeof(Dashboard).GetField(name, Private).SetValue(form, value); }
    private static void Main()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        string isolatedData = Path.Combine(Path.GetTempPath(), "DashboardAdditions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedData);
        Store.DataDirectory = isolatedData;
        Directory.CreateDirectory(output);
        Store.History.Clear(); Store.Total = new Counters(); Store.MouseDpi = 1600;
        for (int back = 39; back >= 0; back--)
        {
            DayRecord day = new DayRecord { Date = DateTime.Today.AddDays(-back), Keys = 5400 + (back * 173) % 3200, Clicks = 1500 + back * 17, Wheel = 1800 + back * 11, MoveMeters = 80 + back % 60, ActiveSeconds = 18000 };
            for (int h = 9; h < 22; h++)
            {
                day.HourKeys[h] = 250 + (h * 47 + back * 13) % 500;
                day.HourClicks[h] = day.HourKeys[h] / 5;
                day.ActiveHours[h] = 3000;
                day.Keys += day.HourKeys[h];
            }
            day.Sessions.Add(new ActiveSession { Start = day.Date.AddHours(9).AddMinutes(10), End = day.Date.AddHours(10).AddMinutes(5), Seconds = 3300 });
            day.Sessions.Add(new ActiveSession { Start = day.Date.AddHours(10).AddMinutes(20), End = day.Date.AddHours(11).AddMinutes(15), Seconds = 3300 });
            day.Sessions.Add(new ActiveSession { Start = day.Date.AddHours(14).AddMinutes(15), End = day.Date.AddHours(16), Seconds = 6300 });
            day.Sessions.Add(new ActiveSession { Start = day.Date.AddHours(19), End = day.Date.AddHours(19).AddMinutes(22), Seconds = 1320 });
            int[] keyCodes = { 32, 73, 65, 78, 69, 17, 8, 13 };
            long[] counts = { 2400, 2200, 1900, 1600, 1300, 900, 700, 500 };
            for (int k = 0; k < keyCodes.Length; k++) day.KeyCounts[keyCodes[k]] = counts[k] + back * 7;
            string[] paths = { @"C:\Apps\Code.exe", @"C:\Apps\Browser.exe", @"C:\Game\Minecraft.exe" };
            long[] appKeys = { 6200, 3900, 2100 };
            for (int a = 0; a < paths.Length; a++)
            {
                AppUsage app = new AppUsage { ProcessPath = paths[a], Title = Path.GetFileNameWithoutExtension(paths[a]), Keys = appKeys[a], Clicks = 900 + a * 120, Wheel = 600 + a * 80, ActiveSeconds = 3000 + a * 700, HasKeyGroups = true };
                day.Apps[app.Id] = app;
                day.Cross.AppHours[paths[a]] = new double[24];
                for (int h = 9; h < 19; h++) { day.Cross.ObservedHours[h] = 3600; day.Cross.AppHours[paths[a]][h] = a == 0 ? 900 : a == 1 ? 500 : 300; }
                foreach (ActiveSession session in day.Sessions) session.AppSeconds[paths[a]] = Math.Min(session.Seconds, 1200 + a * 180);
            }
            Store.History[day.Date] = day;
        }
        Store.RollDay(DateTime.Today);
        using (Dashboard form = new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
            Set(form, "_s", 1f); form.ClientSize = new Size(1072, 724);
            object overview = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 0);
            Set(form, "_tab", overview); Render(form, Path.Combine(output, "dashboard-overview-baseline.png"));
            object trend = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 1);
            Set(form, "_tab", trend); Set(form, "_trendCompare", true); Render(form, Path.Combine(output, "dashboard-trend-compare.png"));
            Set(form, "_trendHourly", true); typeof(Dashboard).GetMethod("ApplyHourlyTrendChip", Private).Invoke(form, new object[] { 326 });
            Render(form, Path.Combine(output, "dashboard-hourly-menu.png"));
            Set(form, "_trendHourly", false);
            object hours = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 2);
            Set(form, "_tab", hours); Set(form, "_hourView", 2); Set(form, "_hourRange", 0); Render(form, Path.Combine(output, "dashboard-hours-intensity.png"));
            // 连续段:三种指标必须各出一张(指标真正联动)、一张带悬停气泡、一张钉到更早的记录日。
            for (int metric = 0; metric < 3; metric++)
            {
                Set(form, "_hourMetric", metric);
                Render(form, Path.Combine(output, "dashboard-hours-intensity-m" + metric + ".png"));
            }
            Set(form, "_hourMetric", 0); Set(form, "_hover", 2);
            Render(form, Path.Combine(output, "dashboard-hours-intensity-hover.png"));
            Set(form, "_hover", -1);
            Set(form, "_hourRecordDate", DateTime.Today.AddDays(-3));
            Render(form, Path.Combine(output, "dashboard-hours-intensity-pinned.png"));
            Set(form, "_hourRecordDate", DateTime.MinValue);
            object keys = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 3);
            Set(form, "_tab", keys); Set(form, "_keyView", 0); Set(form, "_keyMetric", 0); Set(form, "_keyRange", 0); Render(form, Path.Combine(output, "dashboard-keys-redesign.png"));
            object apps = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 4);
            Set(form, "_tab", apps); Set(form, "_appDays", 7);
            Render(form, Path.Combine(output, "dashboard-apps-table.png"));
            typeof(Dashboard).GetMethod("HandleAppClick", Private).Invoke(form, new object[] { new PointF(80, 315) });
            Render(form, Path.Combine(output, "dashboard-app-detail.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 900 });
            Set(form, "_tab", trend); typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 302 });
            Render(form, Path.Combine(output, "dashboard-trend-distribution.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 900 });
            Set(form, "_tab", keys); Set(form, "_keyMetric", 1); typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 44 });
            Render(form, Path.Combine(output, "dashboard-hold-trend.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 900 });
            object heat = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 8);
            Set(form, "_tab", heat); typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 518 });
            Render(form, Path.Combine(output, "dashboard-mouse-action.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 900 });
            Set(form, "_tab", overview); typeof(Dashboard).GetMethod("OpenOverviewDetail", Private).Invoke(form, new object[] { 7 });
            Render(form, Path.Combine(output, "dashboard-live-detail.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 900 });
            typeof(Dashboard).GetMethod("OpenOverviewDetail", Private).Invoke(form, new object[] { 0 });
            Render(form, Path.Combine(output, "dashboard-input-detail.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 900 });
            typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
        }
        Store.History.Clear(); Store.RollDay(DateTime.Today);
        Console.WriteLine("Dashboard additions render written");
    }
    private static void Render(Dashboard form, string path)
    {
        using (Bitmap bitmap = new Bitmap(1072, 724))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            typeof(Dashboard).GetMethod("OnPaint", Private).Invoke(form, new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)) });
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
