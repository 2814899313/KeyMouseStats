using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>v3.2:自定义壁纸皮肤的离屏渲染回归(九页 + 配置浮层)。</summary>
internal static class CustomSkinRender
{
    static int checks;
    static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception(message);
    }

    [STAThread]
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "KeyMouseStatsTests", "custom-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Store.DataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(Store.DataDirectory);
        try
        {
            string wallpaper = Path.Combine(root, "custom-wallpaper.png");
            using (Bitmap art = MakeArtwork(640, 360)) art.Save(wallpaper, ImageFormat.Png);
            CustomThemeSkin skin = CustomThemeStore.Import(wallpaper);
            Check(CustomThemeStore.Activate(skin.Id), "activate wallpaper");
            Check(CustomThemeStore.PrepareActiveNow(), "prepare wallpaper cache");
            Check(CustomThemeStore.ActiveTheme != null, "custom theme active");

            Seed();
            Directory.CreateDirectory("previews");
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", flags).Invoke(form, new object[] { EventArgs.Empty });
                typeof(Dashboard).GetField("_s", flags).SetValue(form, 1f);
                form.ClientSize = new Size(1072, 724);
                Check(CustomThemeStore.PrepareCompositeNow(1072, 724, 1f), "prepare 1072x724 composite");
                FieldInfo tab = typeof(Dashboard).GetField("_tab", flags);
                MethodInfo paint = typeof(Dashboard).GetMethod("OnPaint", flags);
                for (int page = 0; page < 9; page++)
                {
                    tab.SetValue(form, Enum.ToObject(tab.FieldType, page));
                    typeof(Dashboard).GetField("_keyView", flags).SetValue(form, page == 3 ? 2 : 0);
                    using (Bitmap bmp = new Bitmap(1072, 724))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        paint.Invoke(form, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)) });
                        Color corner = bmp.GetPixel(8, 700);
                        Check(ArtTheme.RelativeLuminance(corner) < 0.10, "page " + page + " background is dark");
                        bmp.Save("previews/custom-skin-page-" + page + ".png", ImageFormat.Png);
                    }
                }
                typeof(Dashboard).GetMethod("OnFormClosed", flags).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }

            RenderDialog(1f, "previews/custom-skin-dialog.png");
            RenderDialog(1f, "previews/custom-skin-dialog-100.png");
            RenderDialog(1.25f, "previews/custom-skin-dialog-125.png");
            RenderDialog(1.5f, "previews/custom-skin-dialog-150.png");
            CheckDarkSelectLifecycle(1.5f);
            Check(File.Exists("previews/custom-skin-overview.png") || File.Exists("previews/custom-skin-page-0.png"), "overview preview written");
            Check(File.Exists("previews/custom-skin-dialog.png"), "dialog 100% preview written");
            Check(File.Exists("previews/custom-skin-dialog-125.png"), "dialog 125% preview written");
            Check(File.Exists("previews/custom-skin-dialog-150.png"), "dialog 150% preview written");
            Console.WriteLine("PASS: " + checks + " custom skin render checks; nine pages + dialog 100/125/150% previews");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>自绘下拉框必须能反复打开/关闭;不能在 Closed 事件里销毁菜单句柄。</summary>
    static void CheckDarkSelectLifecycle(float scale)
    {
        CustomThemeDialog.DpiScaleOverrideForTests = scale;
        try
        {
            using (CustomThemeDialog dialog = new CustomThemeDialog())
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-30000, -30000);
                dialog.Show();
                Application.DoEvents();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                object slot = typeof(CustomThemeDialog).GetField("_iconSlot", flags).GetValue(dialog);
                MethodInfo showMenu = slot.GetType().GetMethod("ShowMenu", flags);
                FieldInfo menuField = slot.GetType().GetField("_menu", flags);
                for (int i = 0; i < 4; i++)
                {
                    showMenu.Invoke(slot, null);
                    Application.DoEvents();
                    ContextMenuStrip menu = (ContextMenuStrip)menuField.GetValue(slot);
                    Check(menu != null, "dark dropdown menu created");
                    menu.Close();
                    Application.DoEvents();
                }
                dialog.Close();
                Application.DoEvents();
            }
            Check(true, "dark dropdown reusable after repeated close");
        }
        finally
        {
            CustomThemeDialog.DpiScaleOverrideForTests = 0f;
        }
    }

    static void RenderDialog(float scale, string path)
    {
        CustomThemeDialog.DpiScaleOverrideForTests = scale;
        try
        {
            using (CustomThemeDialog dialog = new CustomThemeDialog())
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-30000, -30000);
                dialog.Show();
                Application.DoEvents();
                int expectedWidth = (int)Math.Round(880 * scale);
                int expectedHeight = (int)Math.Round(790 * scale);
                Check(dialog.ClientSize.Width == expectedWidth, "dialog width at " + scale.ToString("0.0") + "x");
                Check(dialog.ClientSize.Height > 0 && dialog.ClientSize.Height <= expectedHeight, "dialog height at " + scale.ToString("0.0") + "x");
                Check(!dialog.AutoScroll, "dialog-level scrolling disabled at " + scale.ToString("0.0") + "x");
                Panel previewPanel = (Panel)typeof(CustomThemeDialog)
                    .GetField("_previewPanel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                Control iconPreview = (Control)typeof(CustomThemeDialog)
                    .GetField("_iconPreview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                Check(previewPanel.Bottom <= dialog.ClientSize.Height, "preview panel stays inside the dialog");
                Check(iconPreview.Bottom <= dialog.ClientSize.Height, "2x2 icon preview stays inside the dialog");
                Check(previewPanel.Height >= (int)Math.Round(60 * scale), "preview panel fits at least three lines");
                Check(iconPreview.Height >= (int)Math.Round(170 * scale), "2x2 icon preview keeps full height");
                foreach (Control control in dialog.Controls)
                    Check(control.Right <= dialog.ClientSize.Width && control.Bottom <= dialog.ClientSize.Height,
                        "control stays inside dialog: " + control.GetType().Name + " " + control.Bounds);
                // Form.DrawToBitmap 会把原生标题栏也画进来;先抓整窗,再裁到客户区,
                // 预览图才能完整看到底部提示与 2×2 图标预览。
                Point clientOrigin = dialog.PointToScreen(Point.Empty);
                int offsetX = clientOrigin.X - dialog.Bounds.X;
                int offsetY = clientOrigin.Y - dialog.Bounds.Y;
                using (Bitmap window = new Bitmap(dialog.Width, dialog.Height))
                using (Bitmap bmp = new Bitmap(dialog.ClientSize.Width, dialog.ClientSize.Height))
                {
                    dialog.DrawToBitmap(window, new Rectangle(0, 0, window.Width, window.Height));
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.DrawImage(window, new Rectangle(0, 0, bmp.Width, bmp.Height),
                            new Rectangle(offsetX, offsetY, bmp.Width, bmp.Height), GraphicsUnit.Pixel);
                    bmp.Save(path, ImageFormat.Png);
                }
                dialog.Close();
            }
        }
        finally
        {
            CustomThemeDialog.DpiScaleOverrideForTests = 0f;
        }
    }

    static void Seed()
    {
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        Store.ThemeId = 0;
        Store.KeyboardLayout = 1;
        Store.Today.Keys = 6388;
        Store.Today.Clicks = 1764;
        Store.Today.Left = 1200;
        Store.Today.Right = 430;
        Store.Today.Middle = 134;
        Store.Today.Wheel = 1130;
        Store.Today.ActiveSeconds = 8110;
        Store.Today.MoveMeters = 213.7;
        Store.Today.PeakApm = 129;
        for (int i = 0; i < 24; i++)
        {
            Store.Today.HourKeys[i] = (i >= 7 && i <= 23) ? i * 47 : 0;
            Store.Today.HourClicks[i] = Store.Today.HourKeys[i] / 4;
            Store.Today.ActiveHours[i] = (i >= 7 && i <= 23) ? 960 : 0;
        }
        int[] keys = { 32, 87, 65, 83, 68, 69, 13, 8, 9, 162, 49, 50 };
        for (int i = 0; i < keys.Length; i++) Store.Today.KeyCounts[keys[i]] = 1040 - i * 63;
    }

    static Bitmap MakeArtwork(int width, int height)
    {
        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush background = new LinearGradientBrush(
                new Rectangle(0, 0, width, height), Color.FromArgb(9, 16, 34), Color.FromArgb(3, 7, 16), 25f))
                g.FillRectangle(background, 0, 0, width, height);
            using (LinearGradientBrush glow = new LinearGradientBrush(
                new Rectangle(width / 2, 0, width / 2, height), Color.FromArgb(200, 28, 92, 170), Color.FromArgb(8, 8, 16), 90f))
                g.FillRectangle(glow, width / 2, 0, width / 2, height);
            using (Pen ring = new Pen(Color.FromArgb(64, 232, 255), 3f))
            {
                g.DrawArc(ring, width * 0.55f, height * 0.18f, width * 0.28f, height * 0.52f, 200, 230);
                g.DrawArc(ring, width * 0.64f, height * 0.30f, width * 0.22f, height * 0.38f, 20, 190);
            }
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(250, 62, 158)))
                g.FillEllipse(accent, width * 0.72f, height * 0.40f, width * 0.07f, width * 0.07f);
            using (Pen horizon = new Pen(Color.FromArgb(90, 80, 210, 255), 1.4f))
                g.DrawLine(horizon, width * 0.50f, height * 0.76f, width, height * 0.76f);
        }
        return bitmap;
    }
}
