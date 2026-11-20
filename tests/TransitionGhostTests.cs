using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 换页过渡零重影回归。重点不是「动画好看」,而是任意进度下画面必须与最终页逐像素一致:
/// 一旦有人重新引入整页 Bitmap 快照/DrawImage 二次叠加,这里会立即失败。
/// 全部数据与输出都在 %TEMP% 沙盒内,不读用户真实存档。
/// </summary>
internal static class TransitionGhostTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    /// <summary>页眉右上角有实时秒表;只比较换页正文区,避免把时间数字变化误判为重影。</summary>
    private static readonly Rectangle ContentRegion = new Rectangle(0, 145, 2560, 900);
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("TransitionGhostTests", Run);
    }

    private static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "KmsTransitionGhost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string previousData = Store.DataDirectory;
        Dashboard form = null;
        try
        {
            Store.DataDirectory = Path.Combine(root, "data");
            NativeBoot.RootOverride = Path.Combine(root, "native");
            RimeTextStats.PathOverride = Path.Combine(root, "missing.csv");
            Directory.CreateDirectory(Store.DataDirectory);
            Store.History.Clear();
            Store.Total = new Counters();
            Store.MouseDpi = 1600;
            Store.DailyReportDay = DateTime.Today;
            Store.ThemeId = 0;
            Store.KeyboardLayout = 1;
            Store.MotionLevel = Motion.LevelFull;
            Store.MotionFollowSystem = false;
            Motion.OverrideSystemReducedMotion(false);
            SeedDays();
            Store.RollDay(DateTime.Today);

            form = new Dashboard();
            typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
            form.ClientSize = new Size(2560, 1440);
            Set(form, "_s", 1.5f);
            Set(form, "_mouse", new Point(-100, -100));

            // 过渡字段必须彻底消失:不想再给「整页位图二次叠加」留任何入口。
            Check(typeof(Dashboard).GetField("_transitionSnap", Private) == null, "transition bitmap field removed");
            Check(typeof(Dashboard).GetField("_transitionNeedCapture", Private) == null, "transition capture flag removed");
            Check(typeof(Dashboard).GetField("_snapChips", Private) == null, "transition chip snapshot removed");
            Check(typeof(Dashboard).GetField("_viewSnap", Private) == null, "filter view snapshot removed");
            Check(typeof(Dashboard).GetField("_subSnap", Private) == null, "sub-view snapshot removed");
            Check(typeof(Dashboard).GetField("_subTransition", Private) == null, "sub-view transition removed");
            Check(typeof(Dashboard).GetField("_filterSnap", Private) == null, "filter snapshot removed");
            Check(typeof(Dashboard).GetField("_pageSnap", Private) == null, "page snapshot removed");

            // 切页 A: 趋势 -> 时段分布, 在 30% / 50% / 100% 各抓一帧。
            Set(form, "_tab", Enum.ToObject(Get(form, "_tab").GetType(), 1));
            Set(form, "_trendRange", 14);
            using (Bitmap trend = Paint(form, "trend-full"))
            {
                Set(form, "_tab", Enum.ToObject(Get(form, "_tab").GetType(), 2));
                Set(form, "_hourView", 0);
                Set(form, "_hourRange", 2);
                Set(form, "_hourMetric", 0);
                Bitmap[] frames = CaptureTransition(form, "hours");
                try
                {
                    long d0 = Diff(frames[0], frames[1], ContentRegion);
                    long d1 = Diff(frames[1], frames[2], ContentRegion);
                    Check(d0 == 0 && d1 == 0, "trend -> hours content frames are pixel-identical (0.3/0.5/1.0); diff=" + d0 + "/" + d1);
                    Check(frames[0].Width == 2560 && frames[0].Height == 1440, "2K frame size");
                }
                finally
                {
                    for (int i = 0; i < frames.Length; i++) frames[i].Dispose();
                }
            }

            // 切页 B: 时段分布 -> 趋势, 同一组断点再验一次。
            Set(form, "_tab", Enum.ToObject(Get(form, "_tab").GetType(), 1));
            Bitmap[] reverse = CaptureTransition(form, "trend");
            try
            {
                long d0 = Diff(reverse[0], reverse[1], ContentRegion);
                long d1 = Diff(reverse[1], reverse[2], ContentRegion);
                Check(d0 == 0 && d1 == 0, "hours -> trend content frames are pixel-identical (0.3/0.5/1.0); diff=" + d0 + "/" + d1);
            }
            finally
            {
                for (int i = 0; i < reverse.Length; i++) reverse[i].Dispose();
            }

            // 页面内筛选:趋势页「点击」->「击键」,t=0.08s 抓帧。
            Set(form, "_tab", Enum.ToObject(Get(form, "_tab").GetType(), 1));
            Set(form, "_trendMetric", 1);
            using (Bitmap beforeFilter = Paint(form, "trend-click-before"))
            {
                typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { 100 });
                Check(Convert.ToInt32(Get(form, "_trendMetric")) == 0, "trend filter switched to keys");
                Motion.ResetForTests();
                Motion.UseFakeClock(3000000);
                Motion.Start("enter", 280, Ease.OutCubic);
                Motion.AdvanceFakeClock(80);
                using (Bitmap mid = Paint(form, "audit-transition-2k-trend-filter-080"))
                {
                    Check(mid.Width == 2560 && mid.Height == 1440, "filter frame size");
                    Check(Diff(beforeFilter, mid, ContentRegion) > 0, "filter switched to a different metric");
                }
                Motion.AdvanceFakeClock(200);
                using (Bitmap end = Paint(form, "audit-transition-2k-trend-filter-280")) { }
                Motion.ResetForTests();
            }

            Console.WriteLine("Transition ghost: " + checks + " checks passed (2K / 150% / 0.3 & 0.5 progress)");
        }
        finally
        {
            Motion.ResetForTests();
            if (form != null)
            {
                typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
                form.Dispose();
            }
            Store.DataDirectory = previousData;
            RimeTextStats.PathOverride = null;
            NativeBoot.RootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static Bitmap[] CaptureTransition(Dashboard form, string name)
    {
        Directory.CreateDirectory("previews");
        Motion.ResetForTests();
        Motion.UseFakeClock(1000000);
        Motion.Start("page", 150, Ease.Linear);
        Motion.AdvanceFakeClock(45);   // 30%
        double p30 = Motion.Linear("page");
        Check(Math.Abs(p30 - 0.3) < 0.02, name + " progress 0.3, actual=" + p30.ToString("0.00"));
        Bitmap mid30 = Paint(form, "audit-transition-2k-" + name + "-30");
        Motion.AdvanceFakeClock(30);   // 50%
        double p50 = Motion.Linear("page");
        Check(Math.Abs(p50 - 0.5) < 0.02, name + " progress 0.5, actual=" + p50.ToString("0.00"));
        Bitmap mid50 = Paint(form, "audit-transition-2k-" + name + "-50");
        Motion.AdvanceFakeClock(75);   // 100%
        Bitmap full = Paint(form, "audit-transition-2k-" + name + "-100");
        return new Bitmap[] { mid30, mid50, full };
    }

    private static Bitmap Paint(Dashboard form, string name)
    {
        Bitmap bitmap = new Bitmap(2560, 1440, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            typeof(Dashboard).GetMethod("OnPaint", Private).Invoke(form,
                new object[] { new PaintEventArgs(g, new Rectangle(0, 0, bitmap.Width, bitmap.Height)) });
        }
        bitmap.Save(Path.Combine("previews", name + ".png"), ImageFormat.Png);
        return bitmap;
    }

    private static long Diff(Bitmap a, Bitmap b, Rectangle area)
    {
        if (a == null || b == null || a.Width != b.Width || a.Height != b.Height) return long.MaxValue;
        BitmapData da = null, db = null;
        try
        {
            da = a.LockBits(new Rectangle(0, 0, a.Width, a.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            db = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            int bytes = Math.Abs(da.Stride) * a.Height;
            byte[] ba = new byte[bytes];
            byte[] bb = new byte[bytes];
            Marshal.Copy(da.Scan0, ba, 0, bytes);
            Marshal.Copy(db.Scan0, bb, 0, bytes);
            long diff = 0;
            for (int y = area.Top; y < area.Bottom; y++)
            {
                int row = y * Math.Abs(da.Stride);
                for (int x = area.Left; x < area.Right; x++)
                {
                    int i = row + x * 4;
                    if (ba[i] != bb[i] || ba[i + 1] != bb[i + 1] || ba[i + 2] != bb[i + 2] || ba[i + 3] != bb[i + 3]) diff++;
                }
            }
            return diff;
        }
        finally
        {
            if (da != null) a.UnlockBits(da);
            if (db != null) b.UnlockBits(db);
        }
    }

    private static void SeedDays()
    {
        for (int off = 29; off >= 0; off--)
        {
            DateTime date = DateTime.Today.AddDays(-off);
            DayRecord day = new DayRecord();
            day.Date = date;
            day.Keys = 7000 + off * 173 % 6000;
            day.Clicks = day.Keys / 6 + 200;
            day.Wheel = 200 + off * 11 % 700;
            day.ActiveSeconds = 18000 + off * 31 % 6000;
            day.IdleSeconds = 2400;
            for (int h = 8; h < 23; h++)
            {
                day.HourKeys[h] = day.Keys / 15;
                day.HourClicks[h] = day.Clicks / 15;
                day.ActiveHours[h] = day.ActiveSeconds / 15.0;
            }
            DateTime start = date.AddHours(9).AddMinutes(12);
            for (int i = 0; i < 3; i++)
            {
                ActiveSession session = new ActiveSession();
                session.Start = start.AddHours(i * 4);
                session.End = session.Start.AddMinutes(100);
                session.Seconds = 6000;
                session.StartReason = SessionStartReason.Startup;
                day.Sessions.Add(session);
            }
            Store.History[date] = day;
            Store.Total.Keys += day.Keys;
            Store.Total.Clicks += day.Clicks;
        }
    }

    private static object Get(object target, string name) { return target.GetType().GetField(name, Private).GetValue(target); }
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, Private).SetValue(target, value); }
}
