using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using KeyMouseStats;

/// <summary>v3.2 分页面壁纸继承、独立覆盖、配置往返与 LRU 上限。</summary>
internal static class PageSkinTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        _checks++;
    }

    [STAThread]
    private static void Main()
    {
        try { Run(); }
        catch (Exception ex)
        {
            Console.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static string Image(string root, string name, Color color)
    {
        string path = Path.Combine(root, name + ".png");
        using (Bitmap b = new Bitmap(320, 180, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(b))
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0, 0, 320, 180), color, Color.Black, 35f))
                g.FillRectangle(brush, 0, 0, 320, 180);
            b.Save(path, ImageFormat.Png);
        }
        return path;
    }

    private static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "KeyMouseStatsTests", "page-skins-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        Store.DataDirectory = data;
        Store.CustomThemeId = null;
        try
        {
            CustomThemeSkin a = CustomThemeStore.Import(Image(root, "a", Color.FromArgb(20, 70, 90)));
            CustomThemeSkin b = CustomThemeStore.Import(Image(root, "b", Color.FromArgb(100, 40, 30)));
            CustomThemeSkin c = CustomThemeStore.Import(Image(root, "c", Color.FromArgb(40, 90, 45)));
            CustomThemeSkin d = CustomThemeStore.Import(Image(root, "d", Color.FromArgb(70, 40, 100)));
            Check(CustomThemeStore.Activate(a.Id), "activate global skin");
            for (int i = 0; i < CustomThemeStore.PageKeys.Length; i++)
                Check(CustomThemeStore.EffectiveSkinId(i) == a.Id, "unbound page inherits global: " + i);

            Check(CustomThemeStore.BindPageSkin(1, b.Id), "bind trend to b");
            Check(CustomThemeStore.BindPageSkin(2, c.Id), "bind hours to c");
            Check(CustomThemeStore.BindPageSkin(3, d.Id), "bind keys to d");
            Check(CustomThemeStore.PageBinding(1) == b.Id, "raw page binding is retained");
            Check(CustomThemeStore.EffectiveSkinId(1) == b.Id, "bound page uses its skin");
            Check(CustomThemeStore.EffectiveSkinId(0) == a.Id, "global page still uses active skin");
            Check(CustomThemeStore.EffectiveSkin(1).Id == b.Id, "effective skin object matches");
            CustomThemeStore.SetDim(b.Id, 85);
            Check(CustomThemeStore.EffectiveDim(1) == 85, "per-page skin dim is used");
            Check(!CustomThemeStore.BindPageSkin(4, "missing-skin"), "unknown page skin is rejected");

            Check(CustomThemeStore.PrepareCompositeNow(320, 180, 1f, 0), "bake global page composite");
            Check(CustomThemeStore.PrepareCompositeNow(320, 180, 1f, 1), "bake page 1 composite");
            Check(CustomThemeStore.PrepareCompositeNow(320, 180, 1f, 2), "bake page 2 composite");
            Check(CustomThemeStore.PrepareCompositeNow(320, 180, 1f, 3), "bake page 3 composite");
            Check(CustomThemeStore.CompositeCacheCount <= 3, "viewport LRU keeps at most three entries");
            Check(CustomThemeStore.CompositeCacheCount >= 1, "viewport LRU keeps entries");

            string config = File.ReadAllText(CustomThemeStore.ConfigPath);
            Check(config.Contains("\"version\": 2"), "config version upgraded");
            Check(config.Contains("\"pageSkins\""), "pageSkins written");
            Check(config.Contains(CustomThemeStore.PageKeys[1]), "page key written");

            Store.Load();
            Check(!Store.LoadFailed, "store reload does not fail");
            Check(CustomThemeStore.EffectiveSkinId(1) == b.Id, "page binding survives config reload");
            Check(CustomThemeStore.EffectiveSkinId(2) == c.Id, "second page binding survives reload");

            Check(CustomThemeStore.BindPageSkin(1, null), "restore page to global");
            Check(CustomThemeStore.EffectiveSkinId(1) == a.Id, "restored page follows global");
            Check(CustomThemeStore.IsPageBound(2), "other page remains independent");

            Check(CustomThemeStore.Delete(c.Id), "delete page-bound skin");
            Check(!CustomThemeStore.IsPageBound(2), "deleting a skin drops its page binding");
            Check(CustomThemeStore.EffectiveSkinId(2) == a.Id, "page falls back after skin deletion");

            CustomThemeStore.ClearPageSkins();
            for (int i = 0; i < CustomThemeStore.PageKeys.Length; i++)
                Check(!CustomThemeStore.IsPageBound(i), "clear drops every page binding: " + i);

            Console.WriteLine("PASS: " + _checks + " page skin checks; inheritance, overrides, persistence and LRU");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
