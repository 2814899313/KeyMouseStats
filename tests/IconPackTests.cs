using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using KeyMouseStats;

/// <summary>v3.2 图标/徽章槽位、内置风格、黑/白底抠图与图标 Prompt。</summary>
internal static class IconPackTests
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

    private static string BlackIcon(string root)
    {
        string path = Path.Combine(root, "black-icon.png");
        using (Bitmap b = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.Black);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(240, 30, 160)))
                g.FillRectangle(brush, 16, 16, 32, 32);
            b.Save(path, ImageFormat.Png);
        }
        return path;
    }

    private static string WhiteIcon(string root)
    {
        string path = Path.Combine(root, "white-icon.png");
        using (Bitmap b = new Bitmap(64, 64, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.White);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(30, 40, 150)))
                g.FillEllipse(brush, 14, 14, 36, 36);
            b.Save(path, ImageFormat.Png);
        }
        return path;
    }

    private static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "KeyMouseStatsTests", "icon-pack-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        Store.DataDirectory = data;
        try
        {
            IconPack.InvalidateForDataDirectoryChange();
            Check(IconPack.Styles.Length == 5, "five built-in icon styles");
            Check(IconPack.SlotKeys.Length == 4, "four icon slots");
            Check(IconPack.StyleKey(IconPack.SlotBrand) == "default", "brand starts on default");
            Check(!IconPack.Draw(Graphics.FromImage(new Bitmap(1, 1)), IconPack.SlotBrand, 0,
                new RectangleF(0, 0, 24, 24), Color.White, Color.White), "default style keeps theme drawing");

            string black = BlackIcon(root);
            Check(IconPack.Import(IconPack.SlotBrand, black, IconMatte.RemoveBlack), "import black-background icon");
            Check(IconPack.StyleKey(IconPack.SlotBrand) == "custom", "import switches slot to custom");
            Check(IconPack.CustomFile(IconPack.SlotBrand).Length > 0, "custom icon file persisted");
            using (Bitmap preview = (Bitmap)IconPack.Preview(IconPack.SlotBrand))
            {
                Check(preview != null, "custom icon loaded into memory");
                Check(preview.GetPixel(1, 1).A == 0, "black background became transparent");
                Check(preview.GetPixel(64, 64).A > 200, "icon center remains opaque");
            }
            Check(IconPack.SetStyle(IconPack.SlotBrand, "cyber"), "switch brand to cyber");
            Check(IconPack.StyleKey(IconPack.SlotBrand) == "cyber", "cyber style active");
            Check(IconPack.CustomFile(IconPack.SlotBrand).Length == 0, "built-in style clears custom icon");
            Check(IconPack.Draw(Graphics.FromImage(new Bitmap(1, 1)), IconPack.SlotBrand, 0,
                new RectangleF(0, 0, 24, 24), Color.Cyan, Color.White), "vector style draws");

            string white = WhiteIcon(root);
            Check(IconPack.Import(IconPack.SlotNav, white, IconMatte.RemoveWhite), "import white-background nav icon");
            using (Bitmap preview = (Bitmap)IconPack.Preview(IconPack.SlotNav))
            {
                Check(preview.GetPixel(1, 1).A == 0, "white background became transparent");
                Check(preview.GetPixel(64, 64).A > 200, "nav icon center remains opaque");
            }

            Check(IconPack.ApplyStyleToAll("seal"), "apply seal to all slots");
            for (int i = 0; i < IconPack.SlotKeys.Length; i++)
                Check(IconPack.StyleKey(IconPack.SlotKeys[i]) == "seal", "all slots use seal: " + i);
            Check(IconPack.ClearSlot(IconPack.SlotKpi), "reset KPI slot");
            Check(IconPack.StyleKey(IconPack.SlotKpi) == "default", "KPI slot back to default");
            Check(IconPack.Import(IconPack.SlotNav, white, IconMatte.RemoveWhite), "re-import nav custom icon");

            Check(IconPack.SetStyle(IconPack.SlotKpi, "compass"), "set compass style");
            IconPack.Load();
            Check(IconPack.StyleKey(IconPack.SlotKpi) == "compass", "icon style survives reload");
            Check(IconPack.StyleKey(IconPack.SlotNav) == "custom", "custom nav icon survives reload");

            Check(SkinPrompt.IconPresets.Length == 3, "three icon prompt presets");
            SkinPromptPair icon = SkinPrompt.BuildIcon("neon", "发光的仪表盘", "霓虹青与电紫");
            Check(icon.Chinese.Contains("1:1"), "icon Chinese prompt is square");
            Check(icon.Chinese.Contains("#000000"), "icon Chinese prompt asks pure black background");
            Check(icon.Chinese.Contains("24×24"), "icon Chinese prompt requires 24px legibility");
            Check(icon.Chinese.Contains("单一居中主体"), "icon Chinese prompt requires one subject");
            Check(icon.Chinese.Contains("描边") || icon.Chinese.Contains("粗轮廓"), "icon Chinese prompt requires bold outline");
            Check(icon.Chinese.Contains("发光的仪表盘"), "icon Chinese custom subject");
            Check(icon.English.Contains("1:1"), "icon English prompt is square");
            Check(icon.English.Contains("#000000"), "icon English prompt asks pure black background");
            Check(icon.English.Contains("24x24"), "icon English prompt requires small-size legibility");
            Check(icon.English.Contains("--ar 1:1"), "icon English prompt aspect parameter");
            SkinPromptPair iconDefault = SkinPrompt.BuildIcon("seal", "", "");
            Check(iconDefault.Chinese.Contains("朱砂"), "icon default subject is localized");
            Check(!iconDefault.English.Contains("朱砂"), "icon English default does not leak Chinese");

            Console.WriteLine("PASS: " + _checks + " icon pack checks; slots, styles, matte import and prompts");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
