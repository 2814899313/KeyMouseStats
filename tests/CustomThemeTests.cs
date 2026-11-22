using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using KeyMouseStats;

/// <summary>
/// v3.2:自定义壁纸导入、缩略图/背景缓存、暗调主色与强调色提取、压暗遮罩、
/// Prompt 模板约束与配置持久化。全部夹具落在 %TEMP% 沙盒。
/// </summary>
internal static class CustomThemeTests
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
        try { Run(); }
        catch (Exception ex)
        {
            Console.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "KeyMouseStatsTests", "custom-theme-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        Store.DataDirectory = data;
        Store.CustomThemeId = null;
        try
        {
            CheckPromptTemplates();
            CheckPaletteAndPaletteBuilder();
            CheckDimAndWallpaperRender();

            string png = Path.Combine(root, "夜景.png");
            string jpg = Path.Combine(root, "night-road.jpg");
            string bmp = Path.Combine(root, "book.bmp");
            using (Bitmap art = MakeWallpaper(320, 180, false))
            {
                art.Save(png, ImageFormat.Png);
                art.Save(jpg, ImageFormat.Jpeg);
                art.Save(bmp, ImageFormat.Bmp);
            }

            CustomThemeSkin pngSkin = CustomThemeStore.Import(png);
            Check(pngSkin != null, "png import returns a skin");
            Check(pngSkin.Id.Length > 0, "skin id");
            Check(pngSkin.Name == "夜景", "default name uses the original file name");
            Check(pngSkin.ImageFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "original extension preserved");
            Check(File.Exists(CustomThemeStore.ConfigPath), "custom-themes.json created");
            Check(File.Exists(pngSkin.ImagePath), "original image copied");
            Check(File.Exists(pngSkin.ThumbPath), "thumbnail generated");
            Check(File.Exists(pngSkin.CachePath), "background cache generated");
            using (Image thumb = Image.FromFile(pngSkin.ThumbPath))
                Check(thumb.Width == 240 && thumb.Height == 135, "thumbnail is 240x135");
            using (Image cache = Image.FromFile(pngSkin.CachePath))
                Check(cache.Width == 1920 && cache.Height == 1080, "background cache is 1920x1080");

            ArtTheme extracted = ThemePaletteBuilder.Build(pngSkin.Name, pngSkin.Background, pngSkin.Accent, pngSkin.Text);
            Check(ArtTheme.RelativeLuminance(extracted.Background) < 0.10, "imported background is dark");
            Check(ArtTheme.ContrastRatio(extracted.Accent, extracted.Background) >= 4.5, "accent keeps 4.5:1 on background");
            Check(extracted.Accent.GetSaturation() >= 0.35, "accent is saturated");
            Check(ArtTheme.ContrastRatio(extracted.Text, extracted.Background) >= 12.0, "text keeps 12:1 on background");
            Check(ArtTheme.RelativeLuminance(extracted.Card) > ArtTheme.RelativeLuminance(extracted.Background), "card separates from background");

            Check(CustomThemeStore.Activate(pngSkin.Id), "activate imported skin");
            Check(CustomThemeStore.ActiveTheme != null, "active theme built");
            Check(Store.CustomThemeId == pngSkin.Id, "active id persisted in store");
            Check(ArtTheme.Current.Name == pngSkin.Name, "ArtTheme.Current follows custom skin");
            Check(ThemeArt.Active, "ThemeArt.Active includes custom skin");
            Check(ThemeArt.Dark, "ThemeArt.Dark includes custom skin");
            Check(CustomThemeStore.PrepareActiveNow(), "prepare background synchronously");
            Check(CustomThemeStore.HasActiveBackground, "in-memory background available");
            Check(CustomThemeStore.PrepareCompositeNow(2560, 1440, 1.5f), "bake 2560x1440 viewport composite");
            Check(CustomThemeStore.HasActiveComposite, "viewport composite available");
            double compositeMedian = MeasureCompositeFrame();
            Check(compositeMedian < 15.0, "composited background frame stays below 15 ms: " + compositeMedian.ToString("0.00"));

            Check(CustomThemeStore.NormalizeDim(49) == 50, "dim clamps below 50");
            Check(CustomThemeStore.NormalizeDim(86) == 85, "dim clamps above 85");
            Check(CustomThemeStore.NormalizeDim(70) == 70, "default dim stays 70");
            Check(CustomThemeStore.NormalizeDim(73) == 75, "dim steps by 5");
            Check(CustomThemeStore.SetActiveDim(73), "set active dim");
            Check(CustomThemeStore.ActiveDim == 75, "active dim rounded to 75");
            Check(CustomThemeStore.SetDim(pngSkin.Id, 86), "set per-skin dim");
            CustomThemeSkin reloadedDim = FindSkin(pngSkin.Id);
            Check(reloadedDim != null && reloadedDim.Dim == 85, "per-skin dim clamped to 85");

            // 绘制路径回归:删掉 theme 目录后仍能从合成缓存完整出图。
            Check(CustomThemeStore.PrepareCompositeNow(1072, 724, 1f), "bake 1072x724 composite for drawing regression");
            string hidden = CustomThemeStore.RootDirectory + "-hidden";
            Directory.Move(CustomThemeStore.RootDirectory, hidden);
            try
            {
                using (Bitmap canvas = new Bitmap(1072, 724))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.Fuchsia);
                    CustomArt.DrawPage(g, new RectangleF(0, 0, 1072, 724));
                    bool changed = false;
                    for (int y = 100; y < 600 && !changed; y += 40)
                        for (int x = 300; x < 1000 && !changed; x += 40)
                            if (canvas.GetPixel(x, y) != Color.Fuchsia) changed = true;
                    Check(changed, "drawing uses in-memory cache without touching files");
                }
            }
            finally
            {
                Directory.Move(hidden, CustomThemeStore.RootDirectory);
            }

            Store.Load();
            Check(!Store.LoadFailed, "stats load does not fail");
            Check(CustomThemeStore.Skins.Count == 1, "config reloads one skin");
            Check(Store.CustomThemeId == pngSkin.Id, "active skin survives Store.Load");
            Check(CustomThemeStore.ActiveDim == 85, "dim survives reload");
            Check(CustomThemeStore.PrepareActiveNow(), "prepare after reload");

            string asyncJpg = jpg;
            ManualResetEvent done = new ManualResetEvent(false);
            CustomThemeSkin asyncSkin = null;
            string asyncError = null;
            CustomThemeStore.ImportAsync(asyncJpg, delegate(CustomThemeSkin skin, string error)
            {
                asyncSkin = skin;
                asyncError = error;
                done.Set();
            });
            Check(done.WaitOne(20000), "async import completes");
            done.Close();
            Check(asyncSkin != null, "async import returns a skin: " + (asyncError ?? ""));
            Check(asyncSkin.ImageFile.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase), "jpg import");

            CustomThemeSkin bmpSkin = CustomThemeStore.Import(bmp);
            Check(bmpSkin.ImageFile.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase), "bmp import");
            string jpeg = Path.Combine(root, "archive.jpeg");
            File.Copy(jpg, jpeg, true);
            CustomThemeSkin jpegSkin = CustomThemeStore.Import(jpeg);
            Check(jpegSkin.ImageFile.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase), "jpeg import");
            string duplicateDir = Path.Combine(root, "duplicate");
            Directory.CreateDirectory(duplicateDir);
            string duplicatePng = Path.Combine(duplicateDir, "夜景.png");
            File.Copy(png, duplicatePng, true);
            CustomThemeSkin duplicateSkin = CustomThemeStore.Import(duplicatePng);
            Check(duplicateSkin.Name == "夜景 2", "duplicate display names get an ordinal suffix");
            Check(CustomThemeStore.Skins.Count == 5, "five custom wallpapers coexist");

            string gif = Path.Combine(root, "not-supported.gif");
            File.Copy(png, gif, true);
            CustomThemeSkin rejected;
            string rejectError;
            Check(!CustomThemeStore.TryImport(gif, out rejected, out rejectError), "gif is rejected");
            Check(!string.IsNullOrEmpty(rejectError), "rejection explains the supported formats");
            Check(!CustomThemeStore.TryImport(Path.Combine(root, "missing.png"), out rejected, out rejectError), "missing file is rejected");

            Check(CustomThemeStore.Delete(asyncSkin.Id), "delete imported jpg");
            Check(!File.Exists(asyncSkin.CachePath), "delete removes background cache");
            Check(!File.Exists(asyncSkin.ThumbPath), "delete removes thumbnail");
            Check(CustomThemeStore.Skins.Count == 4, "delete shrinks gallery");

            Check(CustomThemeStore.Delete(pngSkin.Id), "delete active skin");
            Check(Store.CustomThemeId == null, "deleting active skin clears active id");
            Check(!CustomArt.Active, "deleting active skin leaves no custom art");
            Check(CustomThemeStore.Delete(bmpSkin.Id), "delete bmp skin");
            Check(CustomThemeStore.Delete(jpegSkin.Id), "delete jpeg skin");
            Check(CustomThemeStore.Delete(duplicateSkin.Id), "delete duplicate skin");
            Check(CustomThemeStore.Skins.Count == 0, "gallery empty after deletes");

            Console.WriteLine("PASS: " + checks + " custom theme checks; caches, palette, dim render and prompts");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    static CustomThemeSkin FindSkin(string id)
    {
        System.Collections.Generic.List<CustomThemeSkin> list = CustomThemeStore.Skins;
        for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return list[i];
        return null;
    }

    /// <summary>2560×1440 / 150% 下命中合成缓存后的背景管线中位耗时。</summary>
    static double MeasureCompositeFrame()
    {
        const int width = 2560, height = 1440;
        const float scale = 1.5f;
        RectangleF bounds = new RectangleF(0, 0, width / scale, height / scale);
        using (Bitmap canvas = new Bitmap(width, height))
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.ScaleTransform(scale, scale);
            Check(CustomThemeStore.DrawActive(g, bounds), "composite cache hit at 2560x1440/150%");
            for (int i = 0; i < 5; i++) CustomArt.DrawPage(g, bounds);
            double[] samples = new double[30];
            Stopwatch watch = new Stopwatch();
            for (int i = 0; i < samples.Length; i++)
            {
                watch.Reset();
                watch.Start();
                CustomArt.DrawPage(g, bounds);
                watch.Stop();
                samples[i] = watch.Elapsed.TotalMilliseconds;
            }
            Array.Sort(samples);
            double median = samples[samples.Length / 2];
            Console.WriteLine("composite background median=" + median.ToString("0.00") + " ms at 2560x1440/150%");
            return median;
        }
    }

    static void CheckPromptTemplates()
    {
        Check(SkinPrompt.Presets.Length == 4, "four prompt presets");
        string[] subjects = { "霓虹赛车", "远山孤舟", "磨砂方块", "台灯书页" };
        string[] colors = { "靛蓝与洋红", "墨黑与月白", "石墨与冷白", "深棕与琥珀" };
        for (int i = 0; i < SkinPrompt.Presets.Length; i++)
        {
            SkinPromptPair pair = SkinPrompt.Build(SkinPrompt.Presets[i], subjects[i], colors[i]);
            Check(pair.Chinese.Contains("16:9"), "Chinese prompt is 16:9");
            Check(pair.Chinese.Contains("右侧 60%"), "Chinese subject on the right");
            Check(pair.Chinese.Contains("左侧 35%"), "Chinese left gutter reserved");
            Check(pair.Chinese.Contains("顶部 20%"), "Chinese top 20% band");
            Check(pair.Chinese.Contains("景深虚化"), "Chinese depth of field");
            Check(pair.Chinese.Contains("白色标题"), "Chinese white title band");
            Check(pair.Chinese.Contains("避免高频噪点"), "Chinese anti-clutter constraint");
            Check(pair.Chinese.Contains(subjects[i]), "Chinese custom subject");
            Check(pair.Chinese.Contains(colors[i]), "Chinese custom palette");
            Check(pair.English.Contains("16:9"), "English prompt is 16:9");
            Check(pair.English.Contains("right 60%"), "English subject on the right");
            Check(pair.English.Contains("left 35%"), "English left gutter reserved");
            Check(pair.English.Contains("top 20%"), "English top 20% band");
            Check(pair.English.Contains("depth of field"), "English depth of field");
            Check(pair.English.Contains("high-frequency"), "English anti-clutter constraint");
            Check(pair.English.Contains("--ar 16:9"), "English MJ aspect parameter");
        }
        SkinPromptPair defaults = SkinPrompt.Build("cyber", "", "");
        Check(defaults.Chinese.Contains("赛博驾驶舱"), "Chinese default subject");
        Check(defaults.English.Contains("cyberpunk cockpit"), "English default subject is localized");
        Check(!defaults.English.Contains("雨夜"), "English default does not leak the Chinese subject");
        Check(SkinPrompt.Find("ink").Name.Contains("水墨"), "preset lookup by key");
        Check(SkinPrompt.Find("missing").Key == "cyber", "unknown preset falls back");
    }

    static void CheckPaletteAndPaletteBuilder()
    {
        using (Bitmap art = MakeWallpaper(320, 180, false))
        {
            CustomPalette palette = PaletteExtractor.Extract(art);
            Check(ArtTheme.RelativeLuminance(palette.Background) < 0.12, "sampled background is dark");
            Check(palette.Accent.GetSaturation() >= 0.35, "sampled accent is saturated");
            ArtTheme theme = ThemePaletteBuilder.Build("测试壁纸", palette.Background, palette.Accent, palette.Text);
            Check(theme.Name == "测试壁纸", "theme keeps the skin name");
            Check(ArtTheme.ContrastRatio(theme.Accent, theme.Background) >= 4.5, "accent contrast");
            Check(ArtTheme.ContrastRatio(theme.Text, theme.Background) >= 12.0, "text contrast");
            Check(ArtTheme.ContrastRatio(theme.Muted, theme.Background) >= 4.5, "muted contrast");
        }
        // 纯白图片也必须被压成暗调,否则 50% 遮罩下白字仍会失去对比度。
        ArtTheme white = ThemePaletteBuilder.Build("白", Color.White, Color.White, Color.White);
        Check(ArtTheme.RelativeLuminance(white.Background) < 0.10, "white image is darkened");
        Check(ArtTheme.ContrastRatio(white.Text, white.Background) >= 12.0, "white image keeps text contrast");
        Check(ArtTheme.ContrastRatio(white.Accent, white.Background) >= 4.5, "white image gets a usable accent");
    }

    static void CheckDimAndWallpaperRender()
    {
        using (Bitmap wall = MakeWallpaper(320, 180, true))
        using (Bitmap low = new Bitmap(1072, 724))
        using (Bitmap high = new Bitmap(1072, 724))
        {
            using (Graphics g = Graphics.FromImage(low)) CustomArt.DrawWallpaper(g, new RectangleF(0, 0, 1072, 724), wall, 50);
            using (Graphics g = Graphics.FromImage(high)) CustomArt.DrawWallpaper(g, new RectangleF(0, 0, 1072, 724), wall, 85);
            Color lowSample = low.GetPixel(800, 360);
            Color highSample = high.GetPixel(800, 360);
            Check(ArtTheme.RelativeLuminance(lowSample) > ArtTheme.RelativeLuminance(highSample) + 0.02, "higher dim is darker");
            Check(ArtTheme.ContrastRatio(Color.White, lowSample) >= 4.5, "50% dim keeps white text readable");
            Check(ArtTheme.ContrastRatio(Color.White, highSample) >= 4.5, "85% dim keeps white text readable");
            Color left = low.GetPixel(20, 360);
            Color middle = low.GetPixel(800, 360);
            Check(ArtTheme.RelativeLuminance(left) < ArtTheme.RelativeLuminance(middle), "left edge is feathered darker");
            Color top = low.GetPixel(800, 8);
            Check(ArtTheme.RelativeLuminance(top) < ArtTheme.RelativeLuminance(middle), "top edge is feathered darker");
        }
    }

    /// <summary>暗调蓝底 + 洋红/青色主体;bright=true 时右半为纯白,用于验证压暗遮罩。</summary>
    static Bitmap MakeWallpaper(int width, int height, bool bright)
    {
        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush background = new LinearGradientBrush(
                new Rectangle(0, 0, width, height), Color.FromArgb(10, 18, 36), Color.FromArgb(2, 6, 14), 0f))
                g.FillRectangle(background, 0, 0, width, height);
            if (bright)
            {
                using (SolidBrush white = new SolidBrush(Color.White))
                    g.FillRectangle(white, width / 2, 0, width / 2, height);
            }
            else
            {
                using (SolidBrush magenta = new SolidBrush(Color.FromArgb(255, 44, 150)))
                    g.FillEllipse(magenta, width * 0.56f, height * 0.18f, width * 0.30f, height * 0.62f);
                using (Pen cyan = new Pen(Color.FromArgb(62, 230, 255), Math.Max(2f, width / 80f)))
                    g.DrawArc(cyan, width * 0.30f, height * 0.26f, width * 0.38f, height * 0.42f, 200, 220);
            }
        }
        return bitmap;
    }
}
