// ============================================================================
//  键鼠统计 - 图标 / 徽章自由搭配  IconPack.cs
//
//  四个槽位:左上品牌徽章、右上主题浮标、侧栏 9 页导航、KPI 卡片角标。
//  内置风格全部是纯矢量 GDI+;自定义图片在导入时离线抠底、缩放、烘焙为
//  Format32bppPArgb,持久化到 themes/icons/,绘制路径只读内存位图。
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
    internal enum IconMatte
    {
        KeepAlpha = 0,
        RemoveBlack = 1,
        RemoveWhite = 2
    }

    internal sealed class IconStyle
    {
        public string Key;
        public string Name;
    }

    internal sealed class IconSlotConfig
    {
        public string Style = "default";
        public string File = "";
        public int Matte = (int)IconMatte.KeepAlpha;
        public Bitmap Bitmap;
    }

    internal static class IconPack
    {
        internal const string SlotBrand = "brand";
        internal const string SlotFloat = "float";
        internal const string SlotNav = "nav";
        internal const string SlotKpi = "kpi";
        internal static readonly string[] SlotKeys = { SlotBrand, SlotFloat, SlotNav, SlotKpi };
        internal static readonly string[] SlotLabels = { "左上主徽章", "右上主题浮标", "侧栏导航图标", "KPI 卡片角标" };
        internal static readonly string[] MatteLabels = { "保留原透明", "自动去除纯黑背景", "自动去除纯白背景" };

        internal static readonly IconStyle[] Styles =
        {
            new IconStyle { Key = "default", Name = "默认 · Halo / Orbit" },
            new IconStyle { Key = "cyber", Name = "赛博仪表" },
            new IconStyle { Key = "seal", Name = "古风印章" },
            new IconStyle { Key = "pixel", Name = "像素复古" },
            new IconStyle { Key = "compass", Name = "星轨罗盘" }
        };

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, IconSlotConfig> Slots = new Dictionary<string, IconSlotConfig>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private static string _loadedRoot;
        private static bool _loading;
        private const string ConfigName = "icons.json";

        internal static event EventHandler Changed;
        internal static string RootDirectory { get { return Path.Combine(Store.DataDirectory, "themes", "icons"); } }
        internal static string ConfigPath { get { return Path.Combine(RootDirectory, ConfigName); } }

        internal static bool EnsureLoaded()
        {
            string root = RootDirectory;
            if (_loaded && string.Equals(root, _loadedRoot, StringComparison.OrdinalIgnoreCase)) return true;
            return Load();
        }

        internal static bool Load()
        {
            if (_loading) return false;
            _loading = true;
            try
            {
                string root = RootDirectory;
                Dictionary<string, IconSlotConfig> parsed = new Dictionary<string, IconSlotConfig>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < SlotKeys.Length; i++)
                    parsed[SlotKeys[i]] = new IconSlotConfig();
                try
                {
                    if (Store.CanPersist && File.Exists(Path.Combine(root, ConfigName)))
                        ParseConfig(root, parsed);
                }
                catch { }
                lock (Gate)
                {
                    DisposeLocked();
                    Slots.Clear();
                    for (int i = 0; i < SlotKeys.Length; i++)
                    {
                        IconSlotConfig item;
                        Slots[SlotKeys[i]] = parsed.TryGetValue(SlotKeys[i], out item) ? item : new IconSlotConfig();
                    }
                    _loaded = true;
                    _loadedRoot = root;
                }
                return true;
            }
            finally
            {
                _loading = false;
            }
        }

        internal static void InvalidateForDataDirectoryChange()
        {
            lock (Gate)
            {
                DisposeLocked();
                Slots.Clear();
                _loaded = false;
                _loadedRoot = null;
            }
        }

        internal static string StyleKey(string slot)
        {
            EnsureLoaded();
            lock (Gate)
            {
                IconSlotConfig item = FindLocked(slot);
                if (item == null) return "default";
                return string.IsNullOrEmpty(item.File) ? NormalizeStyle(item.Style) : "custom";
            }
        }

        internal static string StyleName(string slot)
        {
            string key = StyleKey(slot);
            if (key == "custom") return "自定义图片";
            return StyleNameOf(key);
        }

        /// <summary>绘制路径专用:不触发 Load / 文件读取。未加载时按默认风格处理。</summary>
        internal static string StyleKeyCached(string slot)
        {
            lock (Gate)
            {
                if (!_loaded) return "default";
                IconSlotConfig item = FindLocked(slot);
                if (item == null) return "default";
                return string.IsNullOrEmpty(item.File) ? NormalizeStyle(item.Style) : "custom";
            }
        }

        internal static string StyleNameOf(string key)
        {
            for (int i = 0; i < Styles.Length; i++)
                if (string.Equals(Styles[i].Key, key, StringComparison.Ordinal)) return Styles[i].Name;
            return Styles[0].Name;
        }

        internal static string CustomFile(string slot)
        {
            EnsureLoaded();
            lock (Gate)
            {
                IconSlotConfig item = FindLocked(slot);
                return item == null ? "" : item.File ?? "";
            }
        }

        internal static int MatteMode(string slot)
        {
            EnsureLoaded();
            lock (Gate)
            {
                IconSlotConfig item = FindLocked(slot);
                return item == null ? 0 : item.Matte;
            }
        }

        internal static bool SetStyle(string slot, string styleKey)
        {
            if (!IsSlot(slot)) return false;
            EnsureLoaded();
            styleKey = NormalizeStyle(styleKey);
            bool changed;
            string oldFile;
            lock (Gate)
            {
                IconSlotConfig item = FindLocked(slot);
                if (item == null) return false;
                oldFile = item.File;
                changed = !string.Equals(item.Style, styleKey, StringComparison.Ordinal) || !string.IsNullOrEmpty(item.File);
                item.Style = styleKey;
                item.File = "";
                item.Matte = 0;
                if (item.Bitmap != null) { item.Bitmap.Dispose(); item.Bitmap = null; }
            }
            DeleteIconFile(oldFile);
            if (changed) SaveConfig();
            if (changed) RaiseChanged();
            return changed;
        }

        internal static bool ApplyStyleToAll(string styleKey)
        {
            styleKey = NormalizeStyle(styleKey);
            bool changed = false;
            for (int i = 0; i < SlotKeys.Length; i++)
                if (SetStyle(SlotKeys[i], styleKey)) changed = true;
            return changed;
        }

        internal static bool ClearSlot(string slot)
        {
            return SetStyle(slot, "default");
        }

        internal static bool Import(string slot, string sourcePath, IconMatte matte)
        {
            string error;
            return TryImport(slot, sourcePath, matte, out error);
        }

        internal static bool TryImport(string slot, string sourcePath, IconMatte matte, out string error)
        {
            error = null;
            if (!IsSlot(slot)) { error = "未知槽位"; return false; }
            if (!Store.CanPersist) { error = "数据目录不可写"; return false; }
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) { error = "图片文件不存在"; return false; }
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp")
            {
                error = "仅支持 .png / .jpg / .jpeg / .bmp";
                return false;
            }
            EnsureLoaded();
            string root = RootDirectory;
            string id = slot + "-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png";
            string path = Path.Combine(root, id);
            try
            {
                Directory.CreateDirectory(root);
                using (Bitmap source = LoadBitmap(sourcePath))
                {
                    if (source.Width < 8 || source.Height < 8) { error = "图片尺寸过小"; return false; }
                    using (Bitmap baked = BakeIcon(source, matte))
                    {
                        if (baked == null) { error = "图片处理失败"; return false; }
                        baked.Save(path, ImageFormat.Png);
                    }
                }
                Bitmap memory = LoadBitmap(path);
                string oldFile;
                lock (Gate)
                {
                    IconSlotConfig item = FindLocked(slot);
                    if (item == null) { memory.Dispose(); error = "未知槽位"; return false; }
                    oldFile = item.File;
                    item.File = id;
                    item.Style = "custom";
                    item.Matte = (int)matte;
                    if (item.Bitmap != null) item.Bitmap.Dispose();
                    item.Bitmap = memory;
                }
                DeleteIconFile(oldFile);
                SaveConfig();
                RaiseChanged();
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                error = ex.Message;
                return false;
            }
        }

        internal static void ImportAsync(string slot, string sourcePath, IconMatte matte, Action<bool, string> completed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                bool ok = false;
                try { ok = TryImport(slot, sourcePath, matte, out error); }
                catch (Exception ex) { error = ex.Message; }
                if (completed == null) return;
                try { completed(ok, error); }
                catch { }
            });
        }

        /// <summary>绘制槽位。返回 false 表示该槽位保持主题原有画法(default)。</summary>
        internal static bool Draw(Graphics g, string slot, int pageIndex, RectangleF rect, Color accent, Color ink)
        {
            if (g == null || rect.Width <= 0f || rect.Height <= 0f || !IsSlot(slot)) return false;
            // 绘制路径绝不触发 Load / 文件读取;未加载时保持主题原有画法。
            if (!_loaded) return false;
            IconSlotConfig item;
            lock (Gate)
            {
                item = FindLocked(slot);
                if (item == null) return false;
                if (item.File.Length > 0 && item.Bitmap != null)
                {
                    DrawImageFit(g, item.Bitmap, rect);
                    return true;
                }
                if (string.Equals(item.Style, "default", StringComparison.Ordinal)) return false;
                // 矢量绘制不持有快照;锁内没有阻塞 I/O。
                DrawVector(g, item.Style, slot, pageIndex, rect, accent, ink);
                return true;
            }
        }

        internal static Image Preview(string slot)
        {
            EnsureLoaded();
            lock (Gate)
            {
                IconSlotConfig item = FindLocked(slot);
                return item == null ? null : item.Bitmap;
            }
        }

        private static void DrawVector(Graphics g, string style, string slot, int pageIndex, RectangleF r, Color accent, Color ink)
        {
            SmoothingMode oldSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                float size = Math.Min(r.Width, r.Height);
                float unit = Math.Max(1f, size / 24f);
                if (string.Equals(style, "cyber", StringComparison.Ordinal))
                {
                    using (Pen p = new Pen(accent, Math.Max(1.2f, 2.0f * unit)))
                    {
                        p.StartCap = p.EndCap = LineCap.Round;
                        g.DrawArc(p, cx - size * .38f, cy - size * .38f, size * .76f, size * .76f, 135, 270);
                        for (int i = 0; i < 5; i++)
                        {
                            double a = Math.PI * (0.75 + i * 0.25);
                            float x1 = cx + (float)Math.Cos(a) * size * .28f;
                            float y1 = cy + (float)Math.Sin(a) * size * .28f;
                            float x2 = cx + (float)Math.Cos(a) * size * .38f;
                            float y2 = cy + (float)Math.Sin(a) * size * .38f;
                            g.DrawLine(p, x1, y1, x2, y2);
                        }
                        g.DrawLine(p, cx, cy, cx + size * .20f, cy - size * .16f);
                        using (SolidBrush dotBrush = new SolidBrush(ink))
                            g.FillEllipse(dotBrush, cx - 1.6f * unit, cy - 1.6f * unit, 3.2f * unit, 3.2f * unit);
                    }
                }
                else if (string.Equals(style, "seal", StringComparison.Ordinal))
                {
                    Color seal = ArtTheme.Mix(accent, Color.FromArgb(190, 40, 34), .55);
                    using (Pen p = new Pen(seal, Math.Max(1.5f, 2.2f * unit)))
                    using (GraphicsPath path = Rounded(r, size * .18f))
                    {
                        g.DrawPath(p, path);
                        float inset = size * .22f;
                        g.DrawLine(p, r.X + inset, r.Y + size * .38f, r.Right - inset, r.Y + size * .38f);
                        g.DrawLine(p, r.X + inset, r.Y + size * .62f, r.Right - inset, r.Y + size * .62f);
                        g.DrawLine(p, r.X + size * .38f, r.Y + inset, r.X + size * .38f, r.Bottom - inset);
                        g.DrawLine(p, r.X + size * .62f, r.Y + inset, r.X + size * .62f, r.Bottom - inset);
                    }
                }
                else if (string.Equals(style, "pixel", StringComparison.Ordinal))
                {
                    float cell = size / 24f;
                    using (SolidBrush b = new SolidBrush(accent))
                    {
                        int[,] pattern = { { 0, 1, 1, 0 }, { 1, 0, 0, 1 }, { 1, 0, 0, 1 }, { 0, 1, 1, 0 } };
                        for (int y = 0; y < 4; y++)
                            for (int x = 0; x < 4; x++)
                                if (pattern[y, x] != 0)
                                    g.FillRectangle(b, cx - size * .32f + x * size * .16f, cy - size * .32f + y * size * .16f, size * .14f, size * .14f);
                        g.FillRectangle(b, cx - cell, cy - cell, cell * 2, cell * 2);
                    }
                }
                else
                {
                    // compass / 星轨罗盘
                    using (Pen p = new Pen(accent, Math.Max(1.1f, 1.7f * unit)))
                    {
                        g.DrawEllipse(p, cx - size * .38f, cy - size * .38f, size * .76f, size * .76f);
                        g.DrawEllipse(p, cx - size * .24f, cy - size * .24f, size * .48f, size * .48f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            float x1 = cx + (float)Math.Cos(a) * size * .29f;
                            float y1 = cy + (float)Math.Sin(a) * size * .29f;
                            float x2 = cx + (float)Math.Cos(a) * size * .36f;
                            float y2 = cy + (float)Math.Sin(a) * size * .36f;
                            g.DrawLine(p, x1, y1, x2, y2);
                        }
                        using (SolidBrush b = new SolidBrush(ink))
                            g.FillPolygon(b, new PointF[] { new PointF(cx, cy - size * .20f), new PointF(cx + size * .07f, cy), new PointF(cx, cy + size * .20f), new PointF(cx - size * .07f, cy) });
                    }
                }
            }
            finally
            {
                g.SmoothingMode = oldSmoothing;
            }
        }

        private static void DrawImageFit(Graphics g, Image image, RectangleF rect)
        {
            if (image == null || rect.Width <= 0f || rect.Height <= 0f) return;
            float scale = Math.Min(rect.Width / image.Width, rect.Height / image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            RectangleF dest = new RectangleF(rect.X + (rect.Width - w) / 2f, rect.Y + (rect.Height - h) / 2f, w, h);
            InterpolationMode old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            try { g.DrawImage(image, dest); }
            finally { g.InterpolationMode = old; }
        }

        private static Bitmap BakeIcon(Bitmap source, IconMatte matte)
        {
            const int Size = 128;
            Bitmap result = new Bitmap(Size, Size, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Min((float)Size / source.Width, (float)Size / source.Height);
                float w = source.Width * scale, h = source.Height * scale;
                g.DrawImage(source, (Size - w) / 2f, (Size - h) / 2f, w, h);
            }
            if (matte == IconMatte.KeepAlpha) return result;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Color c = result.GetPixel(x, y);
                    if (c.A == 0) continue;
                    int alpha = c.A;
                    if (matte == IconMatte.RemoveBlack)
                    {
                        int max = Math.Max(c.R, Math.Max(c.G, c.B));
                        if (max <= 16) alpha = 0;
                        else if (max < 64) alpha = alpha * (max - 16) / 48;
                    }
                    else
                    {
                        int min = Math.Min(c.R, Math.Min(c.G, c.B));
                        int distance = 255 - min;
                        if (distance <= 12) alpha = 0;
                        else if (distance < 64) alpha = alpha * (distance - 12) / 52;
                    }
                    if (alpha != c.A) result.SetPixel(x, y, Color.FromArgb(alpha, c.R, c.G, c.B));
                }
            }
            return result;
        }

        private static void SaveConfig()
        {
            if (!Store.CanPersist) return;
            string root = RootDirectory;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"version\": 1,");
            sb.AppendLine("  \"slots\": {");
            lock (Gate)
            {
                for (int i = 0; i < SlotKeys.Length; i++)
                {
                    IconSlotConfig item = FindLocked(SlotKeys[i]);
                    if (item == null) continue;
                    if (i > 0) sb.AppendLine(",");
                    sb.Append("    \"" + SlotKeys[i] + "\": { \"style\": \"" + MiniJson.Escape(NormalizeStyle(item.Style))
                        + "\", \"file\": \"" + MiniJson.Escape(item.File ?? "") + "\", \"matte\": " + item.Matte.ToString(CultureInfo.InvariantCulture) + " }");
                }
            }
            sb.AppendLine();
            sb.AppendLine("  }");
            sb.AppendLine("}");
            string path = Path.Combine(root, ConfigName);
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static void ParseConfig(string root, Dictionary<string, IconSlotConfig> slots)
        {
            string text = File.ReadAllText(Path.Combine(root, ConfigName), Encoding.UTF8);
            Dictionary<string, object> obj = MiniJson.AsObject(MiniJson.Parse(text));
            object slotsValue;
            if (obj == null || !obj.TryGetValue("slots", out slotsValue)) return;
            Dictionary<string, object> map = MiniJson.AsObject(slotsValue);
            if (map == null) return;
            foreach (KeyValuePair<string, object> pair in map)
            {
                IconSlotConfig item;
                if (!slots.TryGetValue(pair.Key, out item)) continue;
                Dictionary<string, object> value = MiniJson.AsObject(pair.Value);
                if (value == null) continue;
                item.Style = NormalizeStyle(MiniJson.GetString(value, "style", "default"));
                item.File = MiniJson.GetString(value, "file", "") ?? "";
                item.Matte = Math.Max(0, Math.Min(2, MiniJson.GetInt(value, "matte", 0)));
                if (!IsSafeFile(item.File)) item.File = "";
                if (!string.IsNullOrEmpty(item.File) && File.Exists(Path.Combine(root, item.File)))
                {
                    try { item.Bitmap = LoadBitmap(Path.Combine(root, item.File)); }
                    catch { item.File = ""; }
                }
                else item.File = "";
            }
        }

        private static Bitmap LoadBitmap(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (Image image = Image.FromStream(stream))
                return new Bitmap(image);
        }

        private static bool IsSlot(string slot)
        {
            for (int i = 0; i < SlotKeys.Length; i++)
                if (string.Equals(SlotKeys[i], slot, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static IconSlotConfig FindLocked(string slot)
        {
            IconSlotConfig item;
            return slot != null && Slots.TryGetValue(slot, out item) ? item : null;
        }

        private static string NormalizeStyle(string key)
        {
            for (int i = 0; i < Styles.Length; i++)
                if (string.Equals(Styles[i].Key, key, StringComparison.Ordinal)) return key;
            return "default";
        }

        private static bool IsSafeFile(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128) return false;
            if (name == "." || name == ".." || name.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++) if (name.IndexOf(invalid[i]) >= 0) return false;
            return true;
        }

        private static void DeleteIconFile(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsSafeFile(name)) return;
            try { string path = Path.Combine(RootDirectory, name); if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void DisposeLocked()
        {
            foreach (KeyValuePair<string, IconSlotConfig> pair in Slots)
                if (pair.Value.Bitmap != null) pair.Value.Bitmap.Dispose();
        }

        private static void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler == null) return;
            try { handler(null, EventArgs.Empty); } catch { }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(1f, radius * 2f);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
