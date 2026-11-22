using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>v3.2 分页面壁纸与图标槽位的离屏渲染回归。</summary>
internal static class PageIconRender
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object form, string name, object value)
    {
        typeof(Dashboard).GetField(name, Private).SetValue(form, value);
    }

    [STAThread]
    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "KeyMouseStatsTests", "page-icon-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        Store.DataDirectory = data;
        Store.CustomThemeId = null;
        Store.ThemeId = 0;
        Store.MotionLevel = 0;
        try
        {
            string aPath = Path.Combine(root, "overview.png");
            string bPath = Path.Combine(root, "trend.png");
            using (Bitmap a = new Bitmap(320, 180)) using (Graphics g = Graphics.FromImage(a)) { g.Clear(Color.FromArgb(20, 70, 110)); a.Save(aPath, ImageFormat.Png); }
            using (Bitmap b = new Bitmap(320, 180)) using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.FromArgb(110, 30, 70)); b.Save(bPath, ImageFormat.Png); }
            CustomThemeSkin aSkin = CustomThemeStore.Import(aPath);
            CustomThemeSkin bSkin = CustomThemeStore.Import(bPath);
            if (!CustomThemeStore.Activate(aSkin.Id)) throw new Exception("activate overview skin failed");
            if (!CustomThemeStore.BindPageSkin(1, bSkin.Id)) throw new Exception("bind trend skin failed");
            if (!CustomThemeStore.PrepareCompositeNow(400, 300, 1f, 0)) throw new Exception("bake page 0 failed");
            if (!CustomThemeStore.PrepareCompositeNow(400, 300, 1f, 1)) throw new Exception("bake page 1 failed");
            if (!CustomArt.ActiveForPage(0) || !CustomArt.ActiveForPage(1)) throw new Exception("page art active check failed");

            using (Bitmap overview = new Bitmap(400, 300))
            using (Bitmap trend = new Bitmap(400, 300))
            {
                using (Graphics g = Graphics.FromImage(overview)) CustomArt.DrawPage(g, new RectangleF(0, 0, 400, 300), 0);
                using (Graphics g = Graphics.FromImage(trend)) CustomArt.DrawPage(g, new RectangleF(0, 0, 400, 300), 1);
                if (overview.GetPixel(200, 150) == trend.GetPixel(200, 150)) throw new Exception("page skins did not route to different backgrounds");
            }

            string iconPath = Path.Combine(root, "badge.png");
            using (Bitmap icon = new Bitmap(64, 64)) using (Graphics g = Graphics.FromImage(icon))
            {
                g.Clear(Color.Black);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 80, 180))) g.FillRectangle(b, 18, 12, 28, 40);
                icon.Save(iconPath, ImageFormat.Png);
            }
            if (!IconPack.Import(IconPack.SlotBrand, iconPath, IconMatte.RemoveBlack)) throw new Exception("icon import failed");
            if (IconPack.StyleKey(IconPack.SlotBrand) != "custom") throw new Exception("custom icon not active");
            using (Bitmap iconCanvas = new Bitmap(64, 64))
            using (Graphics g = Graphics.FromImage(iconCanvas))
            {
                g.Clear(Color.Transparent);
                if (!IconPack.Draw(g, IconPack.SlotBrand, 0, new RectangleF(0, 0, 64, 64), Color.White, Color.White)) throw new Exception("custom icon draw failed");
                bool visible = false;
                for (int y = 0; y < 64 && !visible; y++) for (int x = 0; x < 64; x++) if (iconCanvas.GetPixel(x, y).A > 40) { visible = true; break; }
                if (!visible) throw new Exception("custom icon rendered nothing");
            }

            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
                Set(form, "_s", 1f);
                form.ClientSize = new Size(1072, 724);
                if (!CustomThemeStore.PrepareCompositeNow(1072, 724, 1f, 0)) throw new Exception("bake render page 0 failed");
                if (!CustomThemeStore.PrepareCompositeNow(1072, 724, 1f, 1)) throw new Exception("bake render page 1 failed");
                Set(form, "_tab", Enum.ToObject(typeof(Dashboard).GetField("_tab", Private).FieldType, 0));
                Set(form, "_paintedTab", Enum.ToObject(typeof(Dashboard).GetField("_tab", Private).FieldType, 0));
                Render(form, "page-skin-overview");
                Set(form, "_tab", Enum.ToObject(typeof(Dashboard).GetField("_tab", Private).FieldType, 1));
                Set(form, "_paintedTab", Enum.ToObject(typeof(Dashboard).GetField("_tab", Private).FieldType, 1));
                Render(form, "page-skin-trend");

                // 图标变化必须使场景底板失效。
                Set(form, "_sceneCacheValid", true);
                typeof(Dashboard).GetMethod("OnIconPackChanged", Private).Invoke(form, new object[] { null, EventArgs.Empty });
                Application.DoEvents();
                if ((bool)typeof(Dashboard).GetField("_sceneCacheValid", Private).GetValue(form)) throw new Exception("icon change did not invalidate scene cache");

                Set(form, "_sceneCacheValid", true);
                if (!CustomThemeStore.BindPageSkin(0, bSkin.Id)) throw new Exception("rebind overview skin failed");
                Application.DoEvents();
                if ((bool)typeof(Dashboard).GetField("_sceneCacheValid", Private).GetValue(form)) throw new Exception("page skin change did not invalidate scene cache");
            }
            Console.WriteLine("PASS: page-specific wallpaper routing, custom icon render and scene-cache invalidation");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void Render(Dashboard form, string name)
    {
        Directory.CreateDirectory("previews");
        using (Bitmap bitmap = new Bitmap(1072, 724))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            typeof(Dashboard).GetMethod("OnPaint", Private).Invoke(form,
                new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, 1072, 724)) });
            bitmap.Save(Path.Combine("previews", name + ".png"), ImageFormat.Png);
        }
    }
}
