// ============================================================================
//  键鼠统计 - 自定义壁纸皮肤引擎  CustomTheme.cs
//
//  设计约束:
//    * 图片只在导入 / 启动时的后台线程解码,绘制路径只读内存位图;
//    * 主背景缓存固定 1920×1080,缩略图 240×135,均落在数据目录 themes/ 下;
//    * 配色从 48×48 低分辨率采样提取:暗调主色 + 高对比强调色;
//    * 配置使用零依赖精简 JSON(themes/custom-themes.json),坏文件不会影响启动。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace KeyMouseStats
{
    internal sealed class CustomThemeSkin
    {
        public string Id;
        public string Name;
        public string ImageFile;
        public string ThumbFile;
        public string CacheFile;
        public int Dim = 70;
        public int SourceWidth;
        public int SourceHeight;
        public Color Background = Color.FromArgb(15, 20, 30);
        public Color Accent = Color.FromArgb(110, 168, 255);
        public Color Text = Color.FromArgb(246, 248, 253);

        internal string ImagePath { get { return CustomThemeStore.ResolveFile(ImageFile); } }
        internal string ThumbPath { get { return CustomThemeStore.ResolveFile(ThumbFile); } }
        internal string CachePath { get { return CustomThemeStore.ResolveFile(CacheFile); } }
    }

    internal sealed class CustomPalette
    {
        public Color Background;
        public Color Accent;
        public Color Text;
    }

    /// <summary>
    /// 从背景色 / 强调色构建完整的 ArtTheme。卡片、线条、辅助色都从这两色派生,
    /// 因此任何一张用户图片都能得到一套互相协调、且与深色背景保持对比度的配色。
    /// </summary>
    internal static class ThemePaletteBuilder
    {
        internal static ArtTheme Build(string name, Color background, Color accent, Color text)
        {
            background = Darken(background);
            accent = Boost(accent, 0.40, 0.55);
            accent = EnsureContrast(accent, background, 4.5);
            text = EnsureContrast(text, background, 13.0);

            Color card = ArtTheme.Mix(background, accent, 0.085);
            if (ArtTheme.RelativeLuminance(card) <= ArtTheme.RelativeLuminance(background) + 0.004)
                card = ArtTheme.Mix(background, Color.White, 0.07);
            Color raised = ArtTheme.Mix(card, accent, 0.10);
            if (ArtTheme.RelativeLuminance(raised) <= ArtTheme.RelativeLuminance(card))
                raised = ArtTheme.Mix(card, Color.White, 0.06);

            Color line = ArtTheme.Mix(background, text, 0.24);
            Color muted = EnsureContrast(ArtTheme.Mix(background, text, 0.62), background, 4.5);
            Color green = EnsureContrast(ArtTheme.Mix(accent, Color.FromArgb(75, 210, 168), 0.72), background, 3.2);
            Color orange = EnsureContrast(ArtTheme.Mix(accent, Color.FromArgb(241, 187, 111), 0.72), background, 3.2);
            Color purple = EnsureContrast(ArtTheme.Mix(accent, Color.FromArgb(177, 157, 248), 0.72), background, 3.2);
            Color red = EnsureContrast(ArtTheme.Mix(accent, Color.FromArgb(255, 107, 129), 0.72), background, 3.2);
            Color cyan = EnsureContrast(ArtTheme.Mix(accent, Color.FromArgb(76, 201, 240), 0.72), background, 3.2);
            Color sidebar = ArtTheme.Mix(background, Color.Black, 0.35);
            Color onAccent = ArtTheme.ContrastRatio(accent, Color.White) >= 4.5
                ? Color.White : Color.FromArgb(10, 14, 20);

            ArtTheme theme = new ArtTheme();
            theme.Name = string.IsNullOrEmpty(name) ? "自定义壁纸" : name;
            theme.Radius = 10;
            theme.Background = background;
            theme.Card = card;
            theme.Raised = raised;
            theme.Line = line;
            theme.Text = text;
            theme.Muted = muted;
            theme.Accent = accent;
            theme.Green = green;
            theme.Orange = orange;
            theme.Purple = purple;
            theme.Red = red;
            theme.Cyan = cyan;
            theme.Sidebar = sidebar;
            theme.OnAccent = onAccent;
            return theme;
        }

        /// <summary>强制主色落到暗调区间,白字对比度至少 13.5:1。</summary>
        private static Color Darken(Color color)
        {
            for (int i = 0; i < 16 && ArtTheme.ContrastRatio(Color.White, color) < 13.5; i++)
                color = ArtTheme.Mix(color, Color.Black, 0.18);
            if (Math.Max(color.R, Math.Max(color.G, color.B)) < 18)
                color = ArtTheme.Mix(color, Color.FromArgb(22, 27, 36), 0.45);
            return color;
        }

        private static Color EnsureContrast(Color color, Color background, double minimum)
        {
            for (int i = 0; i < 16 && ArtTheme.ContrastRatio(color, background) < minimum; i++)
                color = ArtTheme.Mix(color, Color.White, 0.10);
            return color;
        }

        internal static Color Boost(Color color, double minimumSaturation, double minimumValue)
        {
            double h, s, v;
            ToHsv(color, out h, out s, out v);
            if (s < minimumSaturation) s = minimumSaturation;
            if (v < minimumValue) v = minimumValue;
            if (v > 0.93) v = 0.93;
            return FromHsv(h, s, v);
        }

        internal static void ToHsv(Color color, out double h, out double s, out double v)
        {
            h = color.GetHue();
            s = color.GetSaturation();
            v = color.GetBrightness();
        }

        internal static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            if (s < 0) s = 0; else if (s > 1) s = 1;
            if (v < 0) v = 0; else if (v > 1) v = 1;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb(255, ClampChannel((r + m) * 255), ClampChannel((g + m) * 255), ClampChannel((b + m) * 255));
        }

        private static int ClampChannel(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (int)Math.Round(value);
        }
    }

    /// <summary>
    /// 48×48 低分辨率采样。暗调主色取亮度最低的 42% 像素加权平均;
    /// 强调色取饱和度最高的一簇色相,再提升饱和度与对比度,避免提取出灰扑扑的“平均色”。
    /// </summary>
    internal static class PaletteExtractor
    {
        private struct Sample
        {
            public byte R, G, B;
            public double Luma, Saturation, Hue;
        }

        internal static CustomPalette Extract(Image source)
        {
            CustomPalette fallback = new CustomPalette
            {
                Background = Color.FromArgb(15, 20, 30),
                Accent = Color.FromArgb(110, 168, 255),
                Text = Color.FromArgb(246, 248, 253)
            };
            if (source == null) return fallback;

            const int Size = 48;
            List<Sample> samples = new List<Sample>(Size * Size);
            using (Bitmap sample = new Bitmap(Size, Size, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(sample))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, Size, Size));
                }
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        Color c = sample.GetPixel(x, y);
                        Sample s = new Sample();
                        s.R = c.R; s.G = c.G; s.B = c.B;
                        s.Luma = ArtTheme.RelativeLuminance(c);
                        s.Saturation = c.GetSaturation();
                        s.Hue = c.GetHue();
                        samples.Add(s);
                    }
                }
            }
            if (samples.Count == 0) return fallback;

            Sample[] sorted = samples.ToArray();
            Array.Sort(sorted, delegate(Sample a, Sample b) { return a.Luma.CompareTo(b.Luma); });

            int darkCount = Math.Max(1, sorted.Length * 42 / 100);
            double dr = 0, dg = 0, db = 0, dw = 0;
            for (int i = 0; i < darkCount; i++)
            {
                double weight = 1.05 - sorted[i].Luma;
                dr += sorted[i].R * weight;
                dg += sorted[i].G * weight;
                db += sorted[i].B * weight;
                dw += weight;
            }
            Color background = dw <= 0 ? fallback.Background
                : Color.FromArgb(255, (int)(dr / dw), (int)(dg / dw), (int)(db / dw));

            double[] bins = new double[24];
            for (int i = 0; i < sorted.Length; i++)
            {
                Sample s = sorted[i];
                if (s.Saturation < 0.16 || s.Luma < 0.06 || s.Luma > 0.985) continue;
                int bin = (int)(s.Hue / 15.0);
                if (bin < 0) bin = 0;
                if (bin > 23) bin = 23;
                bins[bin] += s.Saturation * s.Saturation * (0.25 + s.Luma);
            }
            int best = -1;
            double bestWeight = 0;
            for (int i = 0; i < bins.Length; i++)
            {
                if (bins[i] <= bestWeight) continue;
                bestWeight = bins[i];
                best = i;
            }

            Color accent = fallback.Accent;
            if (best >= 0 && bestWeight > 0.02)
            {
                double ar = 0, ag = 0, ab = 0, aw = 0;
                for (int i = 0; i < sorted.Length; i++)
                {
                    Sample s = sorted[i];
                    if (s.Saturation < 0.16 || s.Luma < 0.06 || s.Luma > 0.985) continue;
                    int bin = (int)(s.Hue / 15.0);
                    if (bin < 0) bin = 0;
                    if (bin > 23) bin = 23;
                    if (bin != best) continue;
                    double weight = s.Saturation * s.Saturation * (0.3 + s.Luma);
                    ar += s.R * weight;
                    ag += s.G * weight;
                    ab += s.B * weight;
                    aw += weight;
                }
                if (aw > 0)
                    accent = Color.FromArgb(255, (int)(ar / aw), (int)(ag / aw), (int)(ab / aw));
            }
            accent = ThemePaletteBuilder.Boost(accent, 0.45, 0.60);

            Color text = ArtTheme.Mix(Color.FromArgb(246, 248, 253), accent, 0.06);
            return new CustomPalette { Background = background, Accent = accent, Text = text };
        }
    }

    /// <summary>
    /// 自定义壁纸仓库。所有磁盘访问都在导入 / 启动 / 切换 / 删除时发生,
    /// 绘制只调用 DrawActive(),读取 PaintGate 保护下的内存位图。
    /// </summary>
    internal static class CustomThemeStore
    {
        internal const int DefaultDim = 70;
        internal const int MinimumDim = 50;
        internal const int MaximumDim = 85;
        internal const string ConfigName = "custom-themes.json";

        private static readonly object Gate = new object();
        private static readonly object PaintGate = new object();
        private static readonly object ConfigGate = new object();
        private static readonly List<CustomThemeSkin> SkinsCache = new List<CustomThemeSkin>();
        private static readonly Dictionary<string, string> PageSkins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<CompositeEntry> Composites = new List<CompositeEntry>();
        private static readonly HashSet<string> CompositeBuilding = new HashSet<string>(StringComparer.Ordinal);
        private const int CompositeCacheLimit = 3;
        private static long _compositeUse;
        private sealed class CompositeEntry
        {
            public string Key;
            public Bitmap Bitmap;
            public long Use;
        }
        internal static readonly string[] PageKeys = { "overview", "trend", "hours", "keys", "apps", "insights", "text", "day", "heat" };
        internal static readonly string[] PageLabels = { "总览", "趋势", "时段", "按键", "应用", "洞察", "文本", "日报", "热力" };
        private static string _loadedRoot;
        private static string _preparingId;
        private static Bitmap _background;
        private static string _requestedCompositeKey;
        private static string _requestedSkinId;
        private static int _requestedDim = DefaultDim;
        private static int _requestedPageIndex = -1;
        private static int _requestedWidth;
        private static int _requestedHeight;
        private static float _requestedDpi = 1f;
        private static Timer _compositeTimer;
        private static volatile ArtTheme _activeTheme;
        private static volatile string _activeThemeId;
        private static volatile string _activeName;
        private static volatile int _activeDim = DefaultDim;

        internal static string RootDirectory { get { return Path.Combine(Store.DataDirectory, "themes"); } }
        internal static string ConfigPath { get { return Path.Combine(RootDirectory, ConfigName); } }

        internal static ArtTheme ActiveTheme
        {
            get
            {
                ArtTheme theme = _activeTheme;
                return theme != null && string.Equals(_activeThemeId, Store.CustomThemeId, StringComparison.Ordinal) ? theme : null;
            }
        }

        internal static string ActiveName
        {
            get
            {
                if (ActiveTheme == null) return "";
                return _activeName ?? "";
            }
        }

        internal static string ActiveNameOr(string fallback)
        {
            string name = ActiveName;
            return string.IsNullOrEmpty(name) ? fallback : name;
        }

        internal static int ActiveDim { get { return NormalizeDim(_activeDim); } }

        /// <summary>后台合成完成时通知打开的面板重绘;事件可能来自 ThreadPool。</summary>
        internal static event EventHandler CompositeReady;

        internal static bool HasActiveComposite
        {
            get
            {
                string key;
                lock (Gate) key = _requestedCompositeKey;
                lock (PaintGate) return FindCompositeLocked(key) != null;
            }
        }

        internal static int CompositeCacheCount
        {
            get { lock (PaintGate) return Composites.Count; }
        }

        internal static List<CustomThemeSkin> Skins
        {
            get
            {
                EnsureLoaded();
                lock (Gate) return new List<CustomThemeSkin>(SkinsCache);
            }
        }

        internal static CustomThemeSkin ActiveSkin
        {
            get
            {
                EnsureLoaded();
                string id = Store.CustomThemeId;
                if (string.IsNullOrEmpty(id)) return null;
                lock (Gate) return FindLocked(id);
            }
        }

        internal static int NormalizeDim(int value)
        {
            if (value < MinimumDim) value = MinimumDim;
            if (value > MaximumDim) value = MaximumDim;
            return MinimumDim + ((value - MinimumDim + 2) / 5) * 5;
        }

        internal static string PageKey(int index)
        {
            return index >= 0 && index < PageKeys.Length ? PageKeys[index] : null;
        }

        internal static string PageLabel(int index)
        {
            return index >= 0 && index < PageLabels.Length ? PageLabels[index] : "";
        }

        /// <summary>页面原始绑定;空表示继承全局 ActiveSkinId。</summary>
        internal static string PageBinding(int index)
        {
            EnsureLoaded();
            string key = PageKey(index);
            if (key == null) return null;
            lock (Gate)
            {
                string id;
                return PageSkins.TryGetValue(key, out id) ? id : null;
            }
        }

        /// <summary>页面实际生效的 skinId:独立绑定优先,否则回退全局。</summary>
        internal static string EffectiveSkinId(int index)
        {
            EnsureLoaded();
            string key = PageKey(index);
            if (key == null) return Store.CustomThemeId;
            lock (Gate) return EffectiveSkinIdLocked(key);
        }

        private static string EffectiveSkinIdLocked(string pageKey)
        {
            string id;
            if (!string.IsNullOrEmpty(pageKey) && PageSkins.TryGetValue(pageKey, out id) && FindLocked(id) != null) return id;
            return Store.CustomThemeId;
        }

        internal static CustomThemeSkin EffectiveSkin(int index)
        {
            string id = EffectiveSkinId(index);
            if (string.IsNullOrEmpty(id)) return null;
            lock (Gate) return FindLocked(id);
        }

        internal static int EffectiveDim(int index)
        {
            CustomThemeSkin skin = EffectiveSkin(index);
            return skin == null ? ActiveDim : NormalizeDim(skin.Dim);
        }

        internal static bool IsPageBound(int index)
        {
            string key = PageKey(index);
            if (key == null) return false;
            EnsureLoaded();
            lock (Gate) return PageSkins.ContainsKey(key);
        }

        /// <summary>把页面绑定到某个 skinId;id 为空表示恢复跟随全局。</summary>
        internal static bool BindPageSkin(int index, string id)
        {
            EnsureLoaded();
            string key = PageKey(index);
            if (key == null) return false;
            bool changed;
            lock (Gate)
            {
                if (string.IsNullOrEmpty(id)) changed = PageSkins.Remove(key);
                else
                {
                    if (FindLocked(id) == null) return false;
                    string previous;
                    changed = !PageSkins.TryGetValue(key, out previous) || !string.Equals(previous, id, StringComparison.Ordinal);
                    PageSkins[key] = id;
                }
            }
            if (!changed) return true;
            SaveConfig();
            RaiseCompositeReady();
            return true;
        }

        internal static void ClearPageSkins()
        {
            bool changed;
            lock (Gate) { changed = PageSkins.Count > 0; PageSkins.Clear(); }
            if (changed) SaveConfig();
        }

        internal static string ResolveFile(string name)
        {
            if (!IsSafeFile(name)) return "";
            return Path.Combine(RootDirectory, name);
        }

        /// <summary>数据目录切换后清空内存状态;下一次 EnsureLoaded 会从新目录重读。</summary>
        internal static void InvalidateForDataDirectoryChange()
        {
            lock (Gate)
            {
                _loadedRoot = null;
                _preparingId = null;
                SkinsCache.Clear();
                PageSkins.Clear();
                CompositeBuilding.Clear();
                _activeTheme = null;
                _activeThemeId = null;
                _activeName = "";
                _activeDim = DefaultDim;
                _requestedCompositeKey = null;
                _requestedWidth = 0;
                _requestedHeight = 0;
                _requestedPageIndex = -1;
                _requestedDpi = 1f;
                Store.CustomThemeId = null;
            }
            DisposeBackground();
            DisposeComposite();
        }

        internal static void EnsureLoaded()
        {
            string root = RootDirectory;
            if (string.Equals(root, _loadedRoot, StringComparison.OrdinalIgnoreCase)) return;
            Load();
        }

        /// <summary>在 Store.Load 之后或数据目录切换后调用。仅允许在读盘阶段执行。</summary>
        internal static void Load()
        {
            if (!Store.CanPersist) return;
            string root = RootDirectory;
            List<CustomThemeSkin> loaded = new List<CustomThemeSkin>();
            Dictionary<string, string> pageBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string active = null;
            int activeDim = DefaultDim;
            try
            {
                ParseConfig(root, loaded, pageBindings, out active, out activeDim);
            }
            catch
            {
                loaded.Clear();
                pageBindings.Clear();
                active = null;
                activeDim = DefaultDim;
            }

            lock (Gate)
            {
                _loadedRoot = root;
                _preparingId = null;
                SkinsCache.Clear();
                SkinsCache.AddRange(loaded);
                PageSkins.Clear();
                foreach (KeyValuePair<string, string> pair in pageBindings)
                {
                    bool knownPage = false;
                    for (int i = 0; i < PageKeys.Length; i++)
                        if (string.Equals(PageKeys[i], pair.Key, StringComparison.OrdinalIgnoreCase)) { knownPage = true; break; }
                    if (knownPage && FindLocked(pair.Value) != null) PageSkins[pair.Key] = pair.Value;
                }
                CustomThemeSkin activeSkin = string.IsNullOrEmpty(active) ? null : FindLocked(active);
                if (activeSkin == null) active = null;
                if (activeSkin != null)
                {
                    activeSkin.Dim = NormalizeDim(activeSkin.Dim);
                    activeDim = activeSkin.Dim;
                }
                _activeThemeId = active;
                _activeName = activeSkin == null ? "" : activeSkin.Name;
                _activeDim = NormalizeDim(activeDim);
                _activeTheme = activeSkin == null ? null : BuildTheme(activeSkin);
                Store.CustomThemeId = active;
            }
            DisposeBackground();
            DisposeComposite();
            if (!string.IsNullOrEmpty(active)) PrepareAsync(active);
            RefreshCompositeRequest();
        }

        internal static bool Activate(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return false;
            ArtTheme theme;
            int dim;
            string name;
            lock (Gate)
            {
                CustomThemeSkin skin = FindLocked(id);
                if (skin == null) return false;
                skin.Dim = NormalizeDim(skin.Dim);
                theme = BuildTheme(skin);
                dim = skin.Dim;
                name = skin.Name;
                _activeTheme = theme;
                _activeThemeId = id;
                _activeName = name;
                _activeDim = dim;
                Store.CustomThemeId = id;
            }
            SaveConfig();
            DisposeBackground();
            DisposeComposite();
            PrepareAsync(id);
            RefreshCompositeRequest();
            return true;
        }

        internal static void ClearActive()
        {
            bool changed;
            lock (Gate)
            {
                changed = !string.IsNullOrEmpty(Store.CustomThemeId);
                Store.CustomThemeId = null;
                _activeTheme = null;
                _activeThemeId = null;
                _activeName = "";
                _activeDim = DefaultDim;
                _requestedCompositeKey = null;
                _requestedWidth = 0;
                _requestedHeight = 0;
                _requestedPageIndex = -1;
                _requestedDpi = 1f;
            }
            DisposeBackground();
            DisposeComposite();
            if (changed) SaveConfig();
        }

        internal static bool SetDim(string id, int value)
        {
            value = NormalizeDim(value);
            bool active = false;
            lock (Gate)
            {
                CustomThemeSkin skin = FindLocked(id);
                if (skin == null) return false;
                skin.Dim = value;
                if (string.Equals(Store.CustomThemeId, id, StringComparison.Ordinal))
                {
                    _activeDim = value;
                    active = true;
                }
            }
            SaveConfig();
            if (active) RefreshCompositeRequest();
            return true;
        }

        internal static bool SetActiveDim(int value)
        {
            string id = Store.CustomThemeId;
            return !string.IsNullOrEmpty(id) && SetDim(id, value);
        }

        internal static bool Delete(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return false;
            bool active = false;
            List<string> paths = new List<string>();
            lock (Gate)
            {
                CustomThemeSkin skin = FindLocked(id);
                if (skin == null) return false;
                if (string.Equals(Store.CustomThemeId, id, StringComparison.Ordinal))
                {
                    active = true;
                    Store.CustomThemeId = null;
                    _activeTheme = null;
                    _activeThemeId = null;
                    _activeName = "";
                    _activeDim = DefaultDim;
                    _requestedCompositeKey = null;
                    _requestedWidth = 0;
                    _requestedHeight = 0;
                    _requestedPageIndex = -1;
                    _requestedDpi = 1f;
                }
                SkinsCache.Remove(skin);
                List<string> boundPages = new List<string>();
                foreach (KeyValuePair<string, string> pair in PageSkins)
                    if (string.Equals(pair.Value, id, StringComparison.Ordinal)) boundPages.Add(pair.Key);
                for (int i = 0; i < boundPages.Count; i++) PageSkins.Remove(boundPages[i]);
                AddDeletePath(paths, skin.ImageFile);
                AddDeletePath(paths, skin.ThumbFile);
                AddDeletePath(paths, skin.CacheFile);
            }
            if (active)
            {
                DisposeBackground();
                DisposeComposite();
            }
            for (int i = 0; i < paths.Count; i++)
            {
                try { if (File.Exists(paths[i])) File.Delete(paths[i]); }
                catch { }
            }
            SaveConfig();
            return true;
        }

        /// <summary>同步导入。测试与需要确定性的调用方使用;界面使用 ImportAsync。</summary>
        internal static CustomThemeSkin Import(string sourcePath)
        {
            CustomThemeSkin skin;
            string error;
            if (!TryImport(sourcePath, out skin, out error)) throw new InvalidOperationException(error);
            return skin;
        }

        internal static bool TryImport(string sourcePath, out CustomThemeSkin skin, out string error)
        {
            skin = null;
            error = null;
            if (!Store.CanPersist)
            {
                error = "数据目录不可写";
                return false;
            }
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                error = "图片文件不存在";
                return false;
            }
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp")
            {
                error = "仅支持 .png / .jpg / .jpeg / .bmp";
                return false;
            }
            try
            {
                FileInfo info = new FileInfo(sourcePath);
                if (info.Length <= 0)
                {
                    error = "图片文件为空";
                    return false;
                }
                if (info.Length > 64L * 1024 * 1024)
                {
                    error = "图片超过 64 MB,请先压缩";
                    return false;
                }
            }
            catch
            {
                error = "无法读取图片";
                return false;
            }

            EnsureLoaded();
            string root = RootDirectory;
            string id = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string imageFile = id + extension;
            string thumbFile = id + "-thumb.jpg";
            string cacheFile = id + "-bg.jpg";
            string imagePath = Path.Combine(root, imageFile);
            string thumbPath = Path.Combine(root, thumbFile);
            string cachePath = Path.Combine(root, cacheFile);
            bool copied = false;
            try
            {
                Directory.CreateDirectory(root);
                File.Copy(sourcePath, imagePath, false);
                copied = true;
                string baseName = SanitizeDisplayName(Path.GetFileNameWithoutExtension(sourcePath));
                using (Bitmap original = LoadBitmap(imagePath))
                {
                    if (original.Width < 16 || original.Height < 16)
                    {
                        error = "图片尺寸过小";
                        Cleanup(imagePath, thumbPath, cachePath);
                        return false;
                    }
                    BuildThumb(original, thumbPath);
                    BuildCache(original, cachePath);
                    CustomPalette palette = PaletteExtractor.Extract(original);
                    skin = new CustomThemeSkin();
                    skin.Id = id;
                    skin.Name = baseName;
                    skin.ImageFile = imageFile;
                    skin.ThumbFile = thumbFile;
                    skin.CacheFile = cacheFile;
                    skin.Dim = DefaultDim;
                    skin.SourceWidth = original.Width;
                    skin.SourceHeight = original.Height;
                    skin.Background = palette.Background;
                    skin.Accent = palette.Accent;
                    skin.Text = palette.Text;
                }
                lock (Gate)
                {
                    skin.Name = UniqueNameLocked(baseName);
                    SkinsCache.Add(skin);
                }
                if (!SaveConfig())
                {
                    lock (Gate) SkinsCache.Remove(skin);
                    Cleanup(imagePath, thumbPath, cachePath);
                    skin = null;
                    error = "皮肤配置写入失败";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                if (copied) Cleanup(imagePath, thumbPath, cachePath);
                skin = null;
                error = ex.Message;
                return false;
            }
        }

        internal static void ImportAsync(string sourcePath, Action<CustomThemeSkin, string> completed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                CustomThemeSkin skin = null;
                string error = null;
                try
                {
                    if (!TryImport(sourcePath, out skin, out error)) skin = null;
                }
                catch (Exception ex)
                {
                    skin = null;
                    error = ex.Message;
                }
                if (completed == null) return;
                try { completed(skin, error); }
                catch { }
            });
        }

        internal static void PrepareAsync(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (Gate)
            {
                if (string.Equals(_preparingId, id, StringComparison.Ordinal)) return;
                _preparingId = id;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (!string.Equals(Store.CustomThemeId, id, StringComparison.Ordinal)) return;
                    CustomThemeSkin skin;
                    lock (Gate) skin = FindLocked(id);
                    if (skin == null) return;
                    Bitmap bitmap = LoadCacheOrRegenerate(skin);
                    if (bitmap == null) return;
                    lock (PaintGate)
                    {
                        if (!string.Equals(Store.CustomThemeId, id, StringComparison.Ordinal))
                        {
                            bitmap.Dispose();
                            return;
                        }
                        Bitmap old = _background;
                        _background = bitmap;
                        if (old != null) old.Dispose();
                    }
                }
                catch
                {
                }
                finally
                {
                    lock (Gate)
                    {
                        if (string.Equals(_preparingId, id, StringComparison.Ordinal)) _preparingId = null;
                    }
                }
            });
        }

        /// <summary>同步准备当前背景。测试 / 离屏渲染使用,不要放进 Paint 路径。</summary>
        internal static bool PrepareActiveNow()
        {
            EnsureLoaded();
            string id = Store.CustomThemeId;
            if (string.IsNullOrEmpty(id)) return false;
            CustomThemeSkin skin;
            lock (Gate) skin = FindLocked(id);
            if (skin == null) return false;
            Bitmap bitmap = LoadCacheOrRegenerate(skin);
            if (bitmap == null) return false;
            lock (PaintGate)
            {
                Bitmap old = _background;
                _background = bitmap;
                if (old != null) old.Dispose();
            }
            return true;
        }

        internal static bool HasActiveBackground
        {
            get { lock (PaintGate) return _background != null; }
        }

        /// <summary>请求按当前视口缓存一张 1:1 物理尺寸的合成背景。只在非 Paint 路径调用。</summary>
        internal static void RequestComposite(int physicalWidth, int physicalHeight, float dpiScale)
        {
            RequestComposite(physicalWidth, physicalHeight, dpiScale, -1);
        }

        internal static void RequestComposite(int physicalWidth, int physicalHeight, float dpiScale, int pageIndex)
        {
            string id = EffectiveSkinId(pageIndex);
            if (string.IsNullOrEmpty(id) || physicalWidth <= 0 || physicalHeight <= 0) return;
            if (dpiScale <= 0f) dpiScale = 1f;
            int dim = EffectiveDim(pageIndex);
            string key = BuildCompositeKey(id, physicalWidth, physicalHeight, dim, dpiScale);
            lock (Gate)
            {
                _requestedWidth = physicalWidth;
                _requestedHeight = physicalHeight;
                _requestedDpi = dpiScale;
                _requestedSkinId = id;
                _requestedDim = dim;
                _requestedPageIndex = pageIndex;
                _requestedCompositeKey = key;
            }
            ScheduleCompositeBake();
        }

        /// <summary>同步烘焙一张合成背景。仅供测试/离屏确定性渲染使用,不要放进 Paint。</summary>
        internal static bool PrepareCompositeNow(int physicalWidth, int physicalHeight, float dpiScale)
        {
            return PrepareCompositeNow(physicalWidth, physicalHeight, dpiScale, -1);
        }

        internal static bool PrepareCompositeNow(int physicalWidth, int physicalHeight, float dpiScale, int pageIndex)
        {
            string id = EffectiveSkinId(pageIndex);
            if (string.IsNullOrEmpty(id) || physicalWidth <= 0 || physicalHeight <= 0) return false;
            if (dpiScale <= 0f) dpiScale = 1f;
            int dim = EffectiveDim(pageIndex);
            string key = BuildCompositeKey(id, physicalWidth, physicalHeight, dim, dpiScale);
            lock (Gate)
            {
                _requestedWidth = physicalWidth;
                _requestedHeight = physicalHeight;
                _requestedDpi = dpiScale;
                _requestedSkinId = id;
                _requestedDim = dim;
                _requestedPageIndex = pageIndex;
                _requestedCompositeKey = key;
                if (_compositeTimer != null) _compositeTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            Bitmap built = BuildComposite(id, physicalWidth, physicalHeight, dim, dpiScale);
            if (built == null) return false;
            PutComposite(key, built);
            return true;
        }

        /// <summary>命中当前视口签名的合成缓存时,按物理 1:1 直接 blit;不缩放、不叠遮罩。
        /// 只画 g.ClipBounds 与页面 bounds 相交的那一块:悬停局部帧从 10 ms 的全屏拷贝降到 2–4 ms。</summary>
        internal static bool DrawActive(Graphics g, RectangleF bounds)
        {
            return DrawActive(g, bounds, -1);
        }

        internal static bool DrawActive(Graphics g, RectangleF bounds, int pageIndex)
        {
            string id = EffectiveSkinId(pageIndex);
            if (string.IsNullOrEmpty(id) || bounds.Width <= 0 || bounds.Height <= 0) return false;
            Matrix transform = g.Transform;
            float scaleX = Math.Abs(transform.Elements[0]);
            float scaleY = Math.Abs(transform.Elements[3]);
            if (scaleX <= 0f) scaleX = 1f;
            if (scaleY <= 0f) scaleY = 1f;
            int width = (int)Math.Round(bounds.Width * scaleX);
            int height = (int)Math.Round(bounds.Height * scaleY);
            int dim = EffectiveDim(pageIndex);
            string key = BuildCompositeKey(id, width, height, dim, scaleX);
            lock (PaintGate)
            {
                CompositeEntry entry = FindCompositeLocked(key);
                if (entry == null) return false;
                RectangleF visible = RectangleF.Intersect(g.ClipBounds, bounds);
                if (visible.Width <= 0f || visible.Height <= 0f) return true;
                int destX = (int)Math.Round(visible.X * scaleX);
                int destY = (int)Math.Round(visible.Y * scaleY);
                int srcX = (int)Math.Round((visible.X - bounds.X) * scaleX);
                int srcY = (int)Math.Round((visible.Y - bounds.Y) * scaleY);
                int copyW = Math.Min((int)Math.Round(visible.Width * scaleX), entry.Bitmap.Width - srcX);
                int copyH = Math.Min((int)Math.Round(visible.Height * scaleY), entry.Bitmap.Height - srcY);
                if (copyW <= 0 || copyH <= 0) return true;
                GraphicsState state = g.Save();
                g.ResetTransform();
                if (srcX == 0 && srcY == 0 && copyW == entry.Bitmap.Width && copyH == entry.Bitmap.Height)
                    g.DrawImageUnscaled(entry.Bitmap, 0, 0);
                else
                    g.DrawImage(entry.Bitmap, new Rectangle(destX, destY, copyW, copyH),
                        srcX, srcY, copyW, copyH, GraphicsUnit.Pixel);
                g.Restore(state);
                return true;
            }
        }

        private static CompositeEntry FindCompositeLocked(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < Composites.Count; i++)
            {
                CompositeEntry entry = Composites[i];
                if (!string.Equals(entry.Key, key, StringComparison.Ordinal)) continue;
                entry.Use = ++_compositeUse;
                return entry;
            }
            return null;
        }

        private static void PutComposite(string key, Bitmap bitmap)
        {
            if (bitmap == null) return;
            lock (PaintGate)
            {
                CompositeEntry existing = FindCompositeLocked(key);
                if (existing != null)
                {
                    bitmap.Dispose();
                    return;
                }
                CompositeEntry entry = new CompositeEntry();
                entry.Key = key;
                entry.Bitmap = bitmap;
                entry.Use = ++_compositeUse;
                Composites.Add(entry);
                while (Composites.Count > CompositeCacheLimit)
                {
                    int oldest = 0;
                    for (int i = 1; i < Composites.Count; i++)
                        if (Composites[i].Use < Composites[oldest].Use) oldest = i;
                    CompositeEntry victim = Composites[oldest];
                    Composites.RemoveAt(oldest);
                    if (victim.Bitmap != null) victim.Bitmap.Dispose();
                }
            }
        }

        private static string BuildCompositeKey(string id, int width, int height, int dim, float dpi)
        {
            if (string.IsNullOrEmpty(id) || width <= 0 || height <= 0) return "";
            return id + "|" + width.ToString(CultureInfo.InvariantCulture)
                + "|" + height.ToString(CultureInfo.InvariantCulture)
                + "|" + NormalizeDim(dim).ToString(CultureInfo.InvariantCulture)
                + "|" + dpi.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void ScheduleCompositeBake()
        {
            lock (Gate)
            {
                if (_compositeTimer == null)
                    _compositeTimer = new Timer(CompositeTimerTick, null, 140, Timeout.Infinite);
                else
                    _compositeTimer.Change(140, Timeout.Infinite);
            }
        }

        private static void CompositeTimerTick(object state)
        {
            try { PrepareCompositeAsync(); }
            catch { }
        }

        private static void PrepareCompositeAsync()
        {
            string id;
            string key;
            int width, height, dim;
            float dpi;
            lock (Gate)
            {
                id = _requestedSkinId;
                key = _requestedCompositeKey;
                width = _requestedWidth;
                height = _requestedHeight;
                dim = _requestedDim;
                dpi = _requestedDpi;
            }
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key) || width <= 0 || height <= 0) return;
            lock (PaintGate)
            {
                if (FindCompositeLocked(key) != null) return;
            }
            lock (Gate)
            {
                if (CompositeBuilding.Contains(key)) return;
                CompositeBuilding.Add(key);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap built = null;
                bool accepted = false;
                try
                {
                    built = BuildComposite(id, width, height, dim, dpi);
                    if (built != null)
                    {
                        PutComposite(key, built);
                        built = null;
                        accepted = true;
                    }
                }
                catch
                {
                }
                finally
                {
                    if (built != null) built.Dispose();
                    lock (Gate) CompositeBuilding.Remove(key);
                }
                if (accepted) RaiseCompositeReady();
                string latest;
                lock (Gate) latest = _requestedCompositeKey;
                if (!string.IsNullOrEmpty(latest) && !string.Equals(latest, key, StringComparison.Ordinal))
                    ScheduleCompositeBake();
            });
        }

        private static Bitmap BuildComposite(string id, int width, int height, int dim, float dpi)
        {
            CustomThemeSkin skin;
            lock (Gate) skin = FindLocked(id);
            if (skin == null) return null;
            Bitmap source = null;
            try { source = LoadCacheOrRegenerate(skin); }
            catch { }
            if (source == null) return null;
            Bitmap composite = null;
            try
            {
                composite = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(composite))
                {
                    g.ScaleTransform(dpi, dpi);
                    RectangleF bounds = new RectangleF(0, 0, (float)(width / dpi), (float)(height / dpi));
                    CustomArt.DrawWallpaper(g, bounds, source, dim);
                }
                return composite;
            }
            catch
            {
                if (composite != null) composite.Dispose();
                return null;
            }
            finally
            {
                source.Dispose();
            }
        }

        private static void DisposeComposite()
        {
            lock (PaintGate)
            {
                for (int i = 0; i < Composites.Count; i++)
                    if (Composites[i].Bitmap != null) Composites[i].Bitmap.Dispose();
                Composites.Clear();
            }
            lock (Gate) CompositeBuilding.Clear();
        }

        private static void RaiseCompositeReady()
        {
            EventHandler handler = CompositeReady;
            if (handler == null) return;
            try { handler(null, EventArgs.Empty); }
            catch { }
        }

        private static void RefreshCompositeRequest()
        {
            int width, height, page;
            float dpi;
            lock (Gate)
            {
                width = _requestedWidth;
                height = _requestedHeight;
                dpi = _requestedDpi;
                page = _requestedPageIndex;
            }
            if (width > 0 && height > 0) RequestComposite(width, height, dpi, page);
        }

        private static void DisposeBackground()
        {
            lock (PaintGate)
            {
                if (_background == null) return;
                _background.Dispose();
                _background = null;
            }
        }

        private static CustomThemeSkin FindLocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < SkinsCache.Count; i++)
                if (string.Equals(SkinsCache[i].Id, id, StringComparison.Ordinal)) return SkinsCache[i];
            return null;
        }

        private static CustomThemeSkin FindSkinName(List<CustomThemeSkin> skins, string id)
        {
            if (skins == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < skins.Count; i++)
                if (string.Equals(skins[i].Id, id, StringComparison.Ordinal)) return skins[i];
            return null;
        }

        private static ArtTheme BuildTheme(CustomThemeSkin skin)
        {
            return ThemePaletteBuilder.Build(skin.Name, skin.Background, skin.Accent, skin.Text);
        }

        private static Bitmap LoadCacheOrRegenerate(CustomThemeSkin skin)
        {
            string cache = skin.CachePath;
            if (!string.IsNullOrEmpty(cache) && File.Exists(cache))
            {
                try { return LoadBitmap(cache); }
                catch { }
            }
            string image = skin.ImagePath;
            if (string.IsNullOrEmpty(image) || !File.Exists(image)) return null;
            try
            {
                using (Bitmap original = LoadBitmap(image))
                {
                    BuildCache(original, cache);
                }
                return LoadBitmap(cache);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap LoadBitmap(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (Image image = Image.FromStream(stream))
            {
                return new Bitmap(image);
            }
        }

        private static void BuildThumb(Bitmap source, string path)
        {
            using (Bitmap thumb = RenderCover(source, 240, 135)) SaveJpeg(thumb, path, 88L);
        }

        private static void BuildCache(Bitmap source, string path)
        {
            using (Bitmap cache = RenderCover(source, 1920, 1080)) SaveJpeg(cache, path, 90L);
        }

        private static Bitmap RenderCover(Bitmap source, int width, int height)
        {
            Bitmap target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.Clear(Color.Black);
                g.CompositingMode = CompositingMode.SourceOver;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                double scale = Math.Max((double)width / source.Width, (double)height / source.Height);
                double drawW = source.Width * scale;
                double drawH = source.Height * scale;
                g.DrawImage(source, (float)((width - drawW) / 2.0), (float)((height - drawH) / 2.0),
                    (float)drawW, (float)drawH);
            }
            return target;
        }

        private static void SaveJpeg(Bitmap bitmap, string path, long quality)
        {
            ImageCodecInfo codec = null;
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
                if (codecs[i].FormatID == ImageFormat.Jpeg.Guid) { codec = codecs[i]; break; }
            if (codec == null)
            {
                bitmap.Save(path, ImageFormat.Jpeg);
                return;
            }
            using (EncoderParameters parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bitmap.Save(path, codec, parameters);
            }
        }

        private static void AddDeletePath(List<string> paths, string name)
        {
            string path = ResolveFile(name);
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        /// <summary>默认显示名取原图文件名;清掉非法字符并压到 40 字以内。</summary>
        private static string SanitizeDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "自定义壁纸";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsControl(c)) continue;
                bool bad = false;
                for (int k = 0; k < invalid.Length; k++)
                    if (c == invalid[k]) { bad = true; break; }
                if (bad) continue;
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                }
                else sb.Append(c);
            }
            string result = sb.ToString().Trim().Trim('.');
            if (result.Length == 0) return "自定义壁纸";
            if (result.Length > 40) result = result.Substring(0, 40).Trim().Trim('.');
            return result.Length == 0 ? "自定义壁纸" : result;
        }

        /// <summary>同名壁纸追加「 2」「 3」;已有记录读取时保持原名。</summary>
        private static string UniqueNameLocked(string baseName)
        {
            string candidate = baseName;
            int suffix = 2;
            while (NameExistsLocked(candidate))
            {
                string tail = " " + suffix.ToString(CultureInfo.InvariantCulture);
                int max = Math.Max(1, 40 - tail.Length);
                string stem = baseName.Length > max ? baseName.Substring(0, max).TrimEnd() : baseName;
                candidate = stem + tail;
                suffix++;
            }
            return candidate;
        }

        private static bool NameExistsLocked(string name)
        {
            for (int i = 0; i < SkinsCache.Count; i++)
                if (string.Equals(SkinsCache[i].Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void Cleanup(string imagePath, string thumbPath, string cachePath)
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch { }
            try { if (File.Exists(thumbPath)) File.Delete(thumbPath); } catch { }
            try { if (File.Exists(cachePath)) File.Delete(cachePath); } catch { }
        }

        private static bool IsSafeFile(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128) return false;
            if (name == "." || name == ".." || name.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
                if (name.IndexOf(invalid[i]) >= 0) return false;
            return true;
        }

        private static string ColorHex(Color color)
        {
            return color.R.ToString("X2", CultureInfo.InvariantCulture)
                + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static Color ParseHex(string value, Color fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            string text = value.Trim();
            if (text.StartsWith("#", StringComparison.Ordinal)) text = text.Substring(1);
            if (text.Length == 8) text = text.Substring(2);
            if (text.Length != 6) return fallback;
            uint packed;
            if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed)) return fallback;
            return Color.FromArgb((int)((packed >> 16) & 255), (int)((packed >> 8) & 255), (int)(packed & 255));
        }

        private static bool SaveConfig()
        {
            if (!Store.CanPersist) return false;
            string root = RootDirectory;
            string active;
            int activeDim;
            List<CustomThemeSkin> skins;
            Dictionary<string, string> pageSkins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lock (Gate)
            {
                active = Store.CustomThemeId ?? "";
                activeDim = _activeDim;
                skins = new List<CustomThemeSkin>(SkinsCache);
                foreach (KeyValuePair<string, string> pair in PageSkins) pageSkins[pair.Key] = pair.Value;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"version\": 2,");
            sb.AppendLine("  \"active\": \"" + MiniJson.Escape(active) + "\",");
            sb.AppendLine("  \"dim\": " + activeDim.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"pageSkins\": {");
            int pageWritten = 0;
            foreach (KeyValuePair<string, string> pair in pageSkins)
            {
                bool known = false;
                for (int i = 0; i < PageKeys.Length; i++)
                    if (string.Equals(PageKeys[i], pair.Key, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                if (!known || FindSkinName(skins, pair.Value) == null) continue;
                if (pageWritten > 0) sb.AppendLine(",");
                sb.Append("    \"" + MiniJson.Escape(pair.Key) + "\": \"" + MiniJson.Escape(pair.Value) + "\"");
                pageWritten++;
            }
            if (pageWritten > 0) sb.AppendLine();
            sb.AppendLine("  },");
            sb.AppendLine("  \"skins\": [");
            for (int i = 0; i < skins.Count; i++)
            {
                CustomThemeSkin skin = skins[i];
                sb.Append("    { \"id\": \"" + MiniJson.Escape(skin.Id) + "\"");
                sb.Append(", \"name\": \"" + MiniJson.Escape(skin.Name) + "\"");
                sb.Append(", \"image\": \"" + MiniJson.Escape(skin.ImageFile) + "\"");
                sb.Append(", \"thumb\": \"" + MiniJson.Escape(skin.ThumbFile) + "\"");
                sb.Append(", \"cache\": \"" + MiniJson.Escape(skin.CacheFile) + "\"");
                sb.Append(", \"dim\": " + NormalizeDim(skin.Dim).ToString(CultureInfo.InvariantCulture));
                sb.Append(", \"w\": " + skin.SourceWidth.ToString(CultureInfo.InvariantCulture));
                sb.Append(", \"h\": " + skin.SourceHeight.ToString(CultureInfo.InvariantCulture));
                sb.Append(", \"background\": \"" + ColorHex(skin.Background) + "\"");
                sb.Append(", \"accent\": \"" + ColorHex(skin.Accent) + "\"");
                sb.Append(", \"text\": \"" + ColorHex(skin.Text) + "\" }");
                if (i + 1 < skins.Count) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            string path = Path.Combine(root, ConfigName);
            string temp = path + ".tmp";
            lock (ConfigGate)
            {
                try
                {
                    Directory.CreateDirectory(root);
                    File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                    return true;
                }
                catch
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    return false;
                }
            }
        }

        private static void ParseConfig(string root, List<CustomThemeSkin> skins, Dictionary<string, string> pageSkins, out string active, out int activeDim)
        {
            active = null;
            activeDim = DefaultDim;
            string path = Path.Combine(root, ConfigName);
            if (!File.Exists(path)) return;
            string text = File.ReadAllText(path, Encoding.UTF8);
            Dictionary<string, object> rootObject = MiniJson.AsObject(MiniJson.Parse(text));
            if (rootObject == null) return;
            active = MiniJson.GetString(rootObject, "active", null);
            activeDim = NormalizeDim(MiniJson.GetInt(rootObject, "dim", DefaultDim));
            object pagesValue;
            if (pageSkins != null && rootObject.TryGetValue("pageSkins", out pagesValue))
            {
                Dictionary<string, object> pages = MiniJson.AsObject(pagesValue);
                if (pages != null)
                {
                    foreach (KeyValuePair<string, object> pair in pages)
                    {
                        string id = pair.Value as string;
                        if (string.IsNullOrEmpty(id)) continue;
                        pageSkins[pair.Key] = id;
                    }
                }
            }
            object listValue;
            if (!rootObject.TryGetValue("skins", out listValue)) return;
            List<object> list = MiniJson.AsArray(listValue);
            if (list == null) return;
            for (int i = 0; i < list.Count && skins.Count < 64; i++)
            {
                Dictionary<string, object> item = MiniJson.AsObject(list[i]);
                if (item == null) continue;
                string id = MiniJson.GetString(item, "id", null);
                string image = MiniJson.GetString(item, "image", null);
                string thumb = MiniJson.GetString(item, "thumb", null);
                string cache = MiniJson.GetString(item, "cache", null);
                if (!IsSafeFile(id) || !IsSafeFile(image) || !IsSafeFile(cache)) continue;
                bool hasImage = File.Exists(Path.Combine(root, image));
                bool hasCache = File.Exists(Path.Combine(root, cache));
                if (!hasImage && !hasCache) continue;
                bool duplicate = false;
                for (int k = 0; k < skins.Count; k++)
                    if (string.Equals(skins[k].Id, id, StringComparison.Ordinal)) { duplicate = true; break; }
                if (duplicate) continue;
                CustomThemeSkin skin = new CustomThemeSkin();
                skin.Id = id;
                skin.Name = MiniJson.GetString(item, "name", "自定义壁纸");
                if (string.IsNullOrEmpty(skin.Name)) skin.Name = "自定义壁纸";
                skin.ImageFile = image;
                skin.ThumbFile = IsSafeFile(thumb) ? thumb : "";
                skin.CacheFile = cache;
                skin.Dim = NormalizeDim(MiniJson.GetInt(item, "dim", DefaultDim));
                skin.SourceWidth = Math.Max(0, MiniJson.GetInt(item, "w", 0));
                skin.SourceHeight = Math.Max(0, MiniJson.GetInt(item, "h", 0));
                skin.Background = ParseHex(MiniJson.GetString(item, "background", ""), Color.FromArgb(15, 20, 30));
                skin.Accent = ParseHex(MiniJson.GetString(item, "accent", ""), Color.FromArgb(110, 168, 255));
                skin.Text = ParseHex(MiniJson.GetString(item, "text", ""), Color.FromArgb(246, 248, 253));
                skins.Add(skin);
            }
        }
    }

    /// <summary>
    /// 内存缓存 → Cover 裁切 → 左/上/下羽化 → 可调暗度遮罩。
    /// 这里的每个方法都只操作传入的位图,不读取任何文件。
    /// </summary>
    internal static class CustomArt
    {
        internal static bool Active { get { return CustomThemeStore.ActiveTheme != null; } }

        internal static bool ActiveForPage(int pageIndex)
        {
            return !string.IsNullOrEmpty(CustomThemeStore.EffectiveSkinId(pageIndex));
        }

        internal static void DrawPage(Graphics g, RectangleF bounds)
        {
            DrawPage(g, bounds, -1);
        }

        internal static void DrawPage(Graphics g, RectangleF bounds, int pageIndex)
        {
            if (!ActiveForPage(pageIndex)) return;
            if (CustomThemeStore.DrawActive(g, bounds, pageIndex)) return;
            // 合成缓存尚未就绪:只铺一层主题底色。绝不在这里同步烘焙或创建渐变笔刷。
            using (SolidBrush fallback = new SolidBrush(ArtTheme.Current.Background))
                g.FillRectangle(fallback, bounds);
        }

        internal static void DrawWallpaper(Graphics g, RectangleF bounds, Image image, int dim)
        {
            if (image == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            Color background = ArtTheme.Current.Background;

            double scale = Math.Max(bounds.Width / (double)image.Width, bounds.Height / (double)image.Height);
            float drawW = (float)(image.Width * scale);
            float drawH = (float)(image.Height * scale);
            float drawX = bounds.X + (bounds.Width - drawW) / 2f;
            float drawY = bounds.Y + (bounds.Height - drawH) / 2f;

            CompositingMode previousMode = g.CompositingMode;
            InterpolationMode previousInterpolation = g.InterpolationMode;
            PixelOffsetMode previousPixelOffset = g.PixelOffsetMode;
            g.CompositingMode = CompositingMode.SourceOver;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (ImageAttributes grade = new ImageAttributes())
            {
                // 0.86 基础压暗:即使 50% 遮罩落在纯白区域,白字仍能保持 ≥ 4.5:1。
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix00 = 0.86f;
                matrix.Matrix11 = 0.86f;
                matrix.Matrix22 = 0.86f;
                grade.SetColorMatrix(matrix);
                Rectangle destination = Rectangle.Round(new RectangleF(drawX, drawY, drawW, drawH));
                g.DrawImage(image, destination, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, grade);
            }
            g.CompositingMode = previousMode;
            g.InterpolationMode = previousInterpolation;
            g.PixelOffsetMode = previousPixelOffset;

            DrawFeathers(g, bounds, background);

            int alpha = (CustomThemeStore.NormalizeDim(dim) * 255 + 50) / 100;
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(alpha, 3, 5, 9)))
                g.FillRectangle(shade, bounds);
        }

        private static void DrawFeathers(Graphics g, RectangleF bounds, Color background)
        {
            float leftWidth = Math.Min(bounds.Width * 0.20f, 220f);
            if (leftWidth > 1)
            {
                RectangleF left = new RectangleF(bounds.X, bounds.Y, leftWidth, bounds.Height);
                using (LinearGradientBrush brush = new LinearGradientBrush(left,
                    Color.FromArgb(172, background), Color.FromArgb(0, background), 0f))
                    g.FillRectangle(brush, left);
            }

            float topHeight = Math.Min(Math.Max(80f, bounds.Height * 0.18f), bounds.Height * 0.4f);
            if (topHeight > 1)
            {
                RectangleF top = new RectangleF(bounds.X, bounds.Y, bounds.Width, topHeight);
                using (LinearGradientBrush brush = new LinearGradientBrush(top,
                    Color.FromArgb(194, background), Color.FromArgb(0, background), 90f))
                    g.FillRectangle(brush, top);
            }

            float bottomHeight = Math.Min(Math.Max(120f, bounds.Height * 0.24f), bounds.Height * 0.45f);
            if (bottomHeight > 1)
            {
                RectangleF bottom = new RectangleF(bounds.X, bounds.Bottom - bottomHeight, bounds.Width, bottomHeight);
                using (LinearGradientBrush brush = new LinearGradientBrush(bottom,
                    Color.FromArgb(0, background), Color.FromArgb(210, background), 90f))
                    g.FillRectangle(brush, bottom);
            }
        }

        internal static void Sticker(Graphics g, int index, RectangleF bounds)
        {
            Color ink = ArtTheme.Current.OnAccent;
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float radius = Math.Min(bounds.Width, bounds.Height) * (index % 2 == 0 ? 0.34f : 0.24f);
            PointF[] points = new PointF[]
            {
                new PointF(cx, cy - radius),
                new PointF(cx + radius * 0.22f, cy - radius * 0.22f),
                new PointF(cx + radius, cy),
                new PointF(cx + radius * 0.22f, cy + radius * 0.22f),
                new PointF(cx, cy + radius),
                new PointF(cx - radius * 0.22f, cy + radius * 0.22f),
                new PointF(cx - radius, cy),
                new PointF(cx - radius * 0.22f, cy - radius * 0.22f)
            };
            using (SolidBrush brush = new SolidBrush(ink)) g.FillPolygon(brush, points);
        }

        internal static void Symbol(Graphics g, int kind, RectangleF bounds)
        {
            Color accent = ArtTheme.Current.Accent;
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float radius = Math.Min(bounds.Width, bounds.Height) * 0.30f;
            using (Pen pen = new Pen(accent, 1.4f))
            {
                if (kind % 3 == 0) g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                else if (kind % 3 == 1) g.DrawRectangle(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                else g.DrawLine(pen, cx - radius, cy, cx + radius, cy);
            }
        }
    }

    /// <summary>零依赖、只覆盖本配置所需子集的小型 JSON 解析器。</summary>
    internal static class MiniJson
    {
        internal static object Parse(string text)
        {
            int index = 0;
            object value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            return value;
        }

        internal static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        internal static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        internal static string GetString(Dictionary<string, object> obj, string key, string fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null) return fallback;
            string text = value as string;
            return text == null ? fallback : text;
        }

        internal static int GetInt(Dictionary<string, object> obj, string key, int fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null) return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        internal static string Escape(string value)
        {
            if (value == null) return "";
            StringBuilder sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length) throw new FormatException("JSON 意外结束");
            char c = text[index];
            if (c == '{') return ParseObject(text, ref index);
            if (c == '[') return ParseArray(text, ref index);
            if (c == '"') return ParseString(text, ref index);
            if (c == 't') { Expect(text, ref index, "true"); return true; }
            if (c == 'f') { Expect(text, ref index, "false"); return false; }
            if (c == 'n') { Expect(text, ref index, "null"); return null; }
            return ParseNumber(text, ref index);
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++;
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}') { index++; return result; }
            for (; ; )
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"') throw new FormatException("JSON 键格式错误");
                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':') throw new FormatException("JSON 缺少冒号");
                index++;
                result[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("JSON 对象未闭合");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == '}') { index++; return result; }
                throw new FormatException("JSON 对象分隔符错误");
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            List<object> result = new List<object>();
            index++;
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']') { index++; return result; }
            for (; ; )
            {
                result.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index >= text.Length) throw new FormatException("JSON 数组未闭合");
                if (text[index] == ',') { index++; continue; }
                if (text[index] == ']') { index++; return result; }
                throw new FormatException("JSON 数组分隔符错误");
            }
        }

        private static string ParseString(string text, ref int index)
        {
            index++;
            StringBuilder sb = new StringBuilder();
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (index >= text.Length) throw new FormatException("JSON 转义未完成");
                char escape = text[index++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 > text.Length) throw new FormatException("JSON Unicode 转义错误");
                        string hex = text.Substring(index, 4);
                        index += 4;
                        int code;
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("JSON Unicode 转义错误");
                        sb.Append((char)code);
                        break;
                    default: throw new FormatException("JSON 未知转义");
                }
            }
            throw new FormatException("JSON 字符串未闭合");
        }

        private static object ParseNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length)
            {
                char c = text[index];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') index++;
                else break;
            }
            if (start == index) throw new FormatException("JSON 数值格式错误");
            double value;
            if (!double.TryParse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("JSON 数值格式错误");
            return value;
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                throw new FormatException("JSON 字面量格式错误");
            index += literal.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        }
    }
}
