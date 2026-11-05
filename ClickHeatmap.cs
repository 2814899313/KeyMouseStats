using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    /// <summary>
    /// 屏幕点击热区。把虚拟屏幕按 32 像素的格子采样,只记「哪个格子被点了多少次」。
    /// 不截屏、不记录窗口内容;位置明细只保留最近 30 天(计数本身仍按整天保留)。
    /// </summary>
    internal static class ClickHeat
    {
        /// <summary>采样格边长(屏幕物理像素)。</summary>
        public const int Cell = 32;
        /// <summary>位置明细保留天数。</summary>
        public const int KeepDays = 30;
        /// <summary>单日格子上限,防止导入异常文件把内存撑爆。</summary>
        public const int MaxCellsPerDay = 4000;

        /// <summary>
        /// 钩子与原始输入会各自看到同一次按下,两条路都记录位置时用这个小窗口去重。
        /// 30 ms 远小于人手双击的间隔(通常 ≥100 ms),不会把两次真实单击并成一次。
        /// </summary>
        private const uint DedupeWindowMs = 30;
        private static long _lastTick;
        private static int _lastButton = -1;
        private static int _lastCellKey = int.MinValue;

        /// <summary>屏幕坐标 → 格子坐标(向下取整,支持负坐标的多屏布局)。</summary>
        public static int CellOf(int screenX, int screenY, out int cellX, out int cellY)
        {
            cellX = (int)Math.Floor(screenX / (double)Cell);
            cellY = (int)Math.Floor(screenY / (double)Cell);
            return CellKey(cellX, cellY);
        }

        /// <summary>把格子坐标压进 32 位 key:高 16 位 X、低 16 位 Y,各偏移 32768。</summary>
        public static int CellKey(int cellX, int cellY)
        {
            int x = Math.Max(-32768, Math.Min(32767, cellX)) + 32768;
            int y = Math.Max(-32768, Math.Min(32767, cellY)) + 32768;
            return (x << 16) | y;
        }

        public static void SplitKey(int key, out int cellX, out int cellY)
        {
            cellX = ((key >> 16) & 0xFFFF) - 32768;
            cellY = (key & 0xFFFF) - 32768;
        }

        /// <summary>格子左上角的屏幕坐标。</summary>
        public static int CellLeft(int cellX) { return cellX * Cell; }

        /// <summary>
        /// 记录一次点击。返回 false 表示这次被去重窗口吃掉了(同一次物理按下的第二条通路)。
        /// </summary>
        public static bool Record(DayRecord day, int screenX, int screenY, int button)
        {
            if (day == null || button <= 0) return false;
            int cellX, cellY;
            int key = CellOf(screenX, screenY, out cellX, out cellY);
            long now = Environment.TickCount;
            if (button == _lastButton && key == _lastCellKey && unchecked((uint)(now - _lastTick)) < DedupeWindowMs)
                return false;
            _lastTick = now;
            _lastButton = button;
            _lastCellKey = key;
            long count;
            day.ClickCells.TryGetValue(key, out count);
            day.ClickCells[key] = count + 1;
            return true;
        }

        /// <summary>测试用:清掉去重状态。</summary>
        internal static void ResetDedupe()
        {
            _lastTick = 0;
            _lastButton = -1;
            _lastCellKey = int.MinValue;
        }

        // ---------------------------------------------------------------- 落盘编解码

        /// <summary>一天的位置明细分行写成 "cx:cy:count,cx:cy:count";按 key 排序保证文件稳定。</summary>
        public static string Encode(Dictionary<int, long> cells)
        {
            if (cells == null || cells.Count == 0) return "";
            List<int> keys = new List<int>(cells.Keys);
            keys.Sort();
            StringBuilder sb = new StringBuilder(keys.Count * 10);
            foreach (int key in keys)
            {
                long count = cells[key];
                if (count <= 0) continue;
                int cellX, cellY;
                SplitKey(key, out cellX, out cellY);
                if (sb.Length > 0) sb.Append(',');
                sb.Append(cellX.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(cellY.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(count.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static void Decode(string text, Dictionary<int, long> target)
        {
            if (string.IsNullOrEmpty(text) || target == null) return;
            string[] parts = text.Split(',');
            for (int i = 0; i < parts.Length && target.Count < MaxCellsPerDay; i++)
            {
                string[] fields = parts[i].Split(':');
                if (fields.Length != 3) continue;
                int cellX, cellY;
                long count;
                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellX)) continue;
                if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellY)) continue;
                if (!long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) continue;
                if (count <= 0) continue;
                if (cellX < -32768 || cellX > 32767 || cellY < -32768 || cellY > 32767) continue;
                int key = CellKey(cellX, cellY);
                long old;
                target.TryGetValue(key, out old);
                target[key] = old + Math.Min(count, 100000000L);
            }
        }

        /// <summary>
        /// 位置明细只留最近 KeepDays 天;被清掉的是采样格,当天总点击数仍然保留。
        /// 返回清掉的格子数(测试与日志用)。
        /// </summary>
        public static int Prune(IDictionary<DateTime, DayRecord> history)
        {
            if (history == null) return 0;
            DateTime oldest = DateTime.Today.AddDays(-(KeepDays - 1));
            int cleared = 0;
            foreach (KeyValuePair<DateTime, DayRecord> pair in history)
            {
                if (pair.Key >= oldest || pair.Value == null || pair.Value.ClickCells.Count == 0) continue;
                cleared += pair.Value.ClickCells.Count;
                pair.Value.ClickCells.Clear();
            }
            return cleared;
        }

        // ---------------------------------------------------------------- 聚合

        /// <summary>九宫格区域名,下标 = 行 × 3 + 列。</summary>
        public static readonly string[] RegionNames = { "左上", "上中", "右上", "左中", "正中", "右中", "左下", "下中", "右下" };

        /// <summary>
        /// 虚拟桌面 → 画布内容坐标的等比投影。显示器大小、分辨率、横竖屏、负坐标都只是
        /// 不同的 Rectangle;投影只做一次减法和等比缩放,所以布局再怪也不会错位。
        /// </summary>
        public sealed class Projection
        {
            public Rectangle Visible;
            public RectangleF Area;
            public float Scale = 1f;
            public float OriginX, OriginY;

            /// <summary>一块屏幕落在画布里的矩形。</summary>
            public RectangleF ToContent(Rectangle screen)
            {
                return new RectangleF(
                    OriginX + (screen.X - Visible.X) * Scale,
                    OriginY + (screen.Y - Visible.Y) * Scale,
                    Math.Max(1, screen.Width * Scale),
                    Math.Max(1, screen.Height * Scale));
            }

            public PointF ToContent(int screenX, int screenY)
            {
                return new PointF(OriginX + (screenX - Visible.X) * Scale,
                    OriginY + (screenY - Visible.Y) * Scale);
            }

            /// <summary>画布内容坐标 → 虚拟桌面像素坐标。落在可见范围外返回 false。</summary>
            public bool TryUnproject(PointF content, out int screenX, out int screenY)
            {
                screenX = Visible.X + (int)Math.Floor((content.X - OriginX) / Scale);
                screenY = Visible.Y + (int)Math.Floor((content.Y - OriginY) / Scale);
                return screenX >= Visible.Left && screenY >= Visible.Top
                    && screenX < Visible.Right && screenY < Visible.Bottom;
            }
        }

        /// <summary>把可见桌面等比放进画布区域(留黑边居中)。</summary>
        public static Projection Fit(Rectangle visible, RectangleF area)
        {
            Projection projection = new Projection();
            projection.Visible = visible;
            projection.Area = area;
            float scale = Math.Min(area.Width / Math.Max(1, visible.Width), area.Height / Math.Max(1, visible.Height));
            if (!(scale > 0) || float.IsInfinity(scale) || float.IsNaN(scale)) scale = 1f;
            projection.Scale = scale;
            projection.OriginX = area.X + (area.Width - visible.Width * scale) / 2f;
            projection.OriginY = area.Y + (area.Height - visible.Height * scale) / 2f;
            return projection;
        }

        public sealed class Aggregate
        {
            public readonly Dictionary<int, long> Cells = new Dictionary<int, long>();
            public long Total;
            public long Max;
            public int MaxKey = -1;
            public int Days;
            public readonly long[] Regions = new long[9];
            public long RegionTotal;
            /// <summary>九宫格统计用的屏幕范围(用户当前选中的那块屏;画面可以画全部)。</summary>
            public Rectangle RegionArea;
            /// <summary>落在当前可见范围之外(包含换过布局的旧位置)。</summary>
            public long Outside;
            /// <summary>可见范围内、但不在任何一块屏幕上(混合 DPI 布局会在屏与屏之间留出空隙)。</summary>
            public long Vacant;
            /// <summary>在别的屏幕上,不参与九宫格。</summary>
            public long OffRegion;
            /// <summary>所选屏幕上有多少格被点亮。</summary>
            public int RegionCells;

            /// <summary>
            /// 按当前屏幕布局算九宫格。
            /// visible 之外的旧位置计入 Outside;屏与屏之间的空隙计入 Vacant;
            /// 其他显示器计入 OffRegion。九宫格只统计 regionArea 那块屏。
            /// </summary>
            public void ComputeRegions(Rectangle visible, Rectangle regionArea, IList<Rectangle> screens)
            {
                for (int i = 0; i < Regions.Length; i++) Regions[i] = 0;
                RegionTotal = 0;
                Outside = 0;
                Vacant = 0;
                OffRegion = 0;
                RegionCells = 0;
                RegionArea = regionArea;
                foreach (KeyValuePair<int, long> pair in Cells)
                {
                    int cellX, cellY;
                    SplitKey(pair.Key, out cellX, out cellY);
                    int cx = CellLeft(cellX) + Cell / 2;
                    int cy = CellLeft(cellY) + Cell / 2;
                    bool onScreen = OnAnyScreen(cx, cy, screens);
                    bool inside = visible.Width > 0 && visible.Height > 0 && visible.Contains(cx, cy);
                    if (!onScreen)
                    {
                        if (inside) Vacant += pair.Value;
                        else Outside += pair.Value;   // 换过布局的旧位置
                        continue;
                    }
                    if (!inside || regionArea.Width <= 0 || regionArea.Height <= 0 || !regionArea.Contains(cx, cy))
                    {
                        // 别的屏幕上(单屏放大时也包括被裁掉的屏幕)
                        OffRegion += pair.Value;
                        continue;
                    }
                    int region = RegionAt(cx, cy, regionArea);
                    Regions[region] += pair.Value;
                    RegionTotal += pair.Value;
                    RegionCells++;
                }
            }
        }

        /// <summary>这个屏幕坐标是否落在某一块显示器上。</summary>
        public static bool OnAnyScreen(int screenX, int screenY, IList<Rectangle> screens)
        {
            if (screens == null || screens.Count == 0) return true;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i].Contains(screenX, screenY)) return true;
            return false;
        }

        /// <summary>把 Screen.AllScreens 的物理矩形拷成列表,供几何计算与测试使用。</summary>
        public static Rectangle[] ScreenRects(Screen[] screens)
        {
            if (screens == null || screens.Length == 0) return new Rectangle[0];
            Rectangle[] rects = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++) rects[i] = screens[i].Bounds;
            return rects;
        }

        /// <summary>按传入的屏幕范围聚合若干天。区域统计需要另外调用 ComputeRegions。</summary>
        public static Aggregate Collect(IEnumerable<DayRecord> days, Rectangle bounds)
        {
            Aggregate aggregate = new Aggregate();
            if (days == null) return aggregate;
            foreach (DayRecord day in days)
            {
                if (day == null || day.ClickCells == null || day.ClickCells.Count == 0) continue;
                aggregate.Days++;
                foreach (KeyValuePair<int, long> pair in day.ClickCells)
                {
                    long count = pair.Value;
                    if (count <= 0) continue;
                    long old;
                    aggregate.Cells.TryGetValue(pair.Key, out old);
                    aggregate.Cells[pair.Key] = old + count;
                    aggregate.Total += count;
                    if (old + count > aggregate.Max)
                    {
                        aggregate.Max = old + count;
                        aggregate.MaxKey = pair.Key;
                    }
                }
            }
            return aggregate;
        }

        /// <summary>屏幕坐标落在九宫格的第几格(超出范围时夹到边上)。</summary>
        public static int RegionAt(int screenX, int screenY, Rectangle bounds)
        {
            int width = Math.Max(1, bounds.Width), height = Math.Max(1, bounds.Height);
            int col = (int)((long)(screenX - bounds.Left) * 3 / width);
            int row = (int)((long)(screenY - bounds.Top) * 3 / height);
            col = Math.Max(0, Math.Min(2, col));
            row = Math.Max(0, Math.Min(2, row));
            return row * 3 + col;
        }

        public static string RegionName(int index)
        {
            return index >= 0 && index < RegionNames.Length ? RegionNames[index] : "--";
        }

        /// <summary>最热区域下标;没有数据返回 -1。</summary>
        public static int HottestRegion(Aggregate aggregate)
        {
            if (aggregate == null) return -1;
            int best = -1;
            for (int i = 0; i < aggregate.Regions.Length; i++)
                if (aggregate.Regions[i] > 0 && (best < 0 || aggregate.Regions[i] > aggregate.Regions[best])) best = i;
            return best;
        }

        /// <summary>
        /// 页面用的热力色。按对数归一:几十次的常用按钮也能显色,不会只剩少数几格亮。
        /// </summary>
        public static Color Heat(long count, long max)
        {
            if (count <= 0 || max <= 0) return HeatScale.Zero;
            double fraction = Math.Log(1.0 + count) / Math.Log(1.0 + max);
            return HeatScale.At(Math.Pow(Math.Min(1, fraction), 0.85));
        }

        /// <summary>屏幕 / 光标图标(侧栏、标题栏、空状态共用)。</summary>
        public static void DrawIcon(Graphics g, RectangleF r, Color color, float stroke)
        {
            float pad = Math.Max(1.5f, r.Width * 0.10f);
            RectangleF screen = new RectangleF(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height * 0.60f);
            using (Pen pen = new Pen(color, Math.Max(1.2f, stroke)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawRectangle(pen, screen.X, screen.Y, screen.Width, screen.Height);
                float standY = screen.Bottom + r.Height * 0.09f;
                g.DrawLine(pen, r.X + r.Width * 0.38f, screen.Bottom, r.X + r.Width * 0.38f, standY);
                g.DrawLine(pen, r.X + r.Width * 0.62f, screen.Bottom, r.X + r.Width * 0.62f, standY);
                g.DrawLine(pen, r.X + r.Width * 0.26f, standY, r.X + r.Width * 0.74f, standY);
            }
            using (SolidBrush brush = new SolidBrush(color))
            {
                float dot = Math.Max(2f, r.Width * 0.12f);
                g.FillEllipse(brush, screen.X + screen.Width * 0.22f, screen.Y + screen.Height * 0.30f, dot, dot);
                g.FillEllipse(brush, screen.X + screen.Width * 0.58f, screen.Y + screen.Height * 0.52f, dot, dot);
            }
        }
    }

    internal sealed partial class Dashboard
    {
        /// <summary>0 今日 / 1 近 7 天 / 2 近 30 天。</summary>
        private int _clickRange = 2;
        /// <summary>九宫格与单屏放大针对哪块屏(Screen.AllScreens 下标;-1 表示跟随主屏)。</summary>
        private int _clickScreen = -1;
        /// <summary>true:画布只画所选屏幕(格子更大);false:画全部屏幕。</summary>
        private bool _clickFocused;
        /// <summary>false 像素网格 / true 平滑热力。</summary>
        private bool _clickSmooth;
        private readonly List<RectangleF> _clickGridRects = new List<RectangleF>();
        private int _clickGridHover = -1;
        private ContextMenuStrip _clickScreenMenu;
        private Bitmap _clickPlate;
        private string _clickStamp;
        private RectangleF _clickTray;
        private Rectangle _clickScreenBounds;
        /// <summary>当前布局里每块显示器的物理矩形,供几何计算、空隙判断与测试使用。</summary>
        private Rectangle[] _clickScreenRects = new Rectangle[0];
        private ClickHeat.Projection _clickProjection;
        private readonly List<RectangleF> _clickCellRects = new List<RectangleF>();
        private readonly List<int> _clickCellKeys = new List<int>();
        private readonly List<long> _clickCellCounts = new List<long>();
        private ClickHeat.Aggregate _clickCache;
        private int _clickCacheRange = -1;
        private long _clickCacheSignature;

        private IEnumerable<DayRecord> ClickRangeDays()
        {
            return _clickRange == 0
                ? (IEnumerable<DayRecord>)new DayRecord[] { Store.Today }
                : Analysis.RangeDays(_clickRange == 1 ? 7 : 30);
        }

        /// <summary>
        /// 聚合结果缓存:悬停重绘每秒会走十几帧,没必要每帧都把 30 天的格子重算一遍。
        /// 签名包含区间、可见范围、九宫格屏幕与整份显示器布局,任何一项变了就重算。
        /// </summary>
        private ClickHeat.Aggregate ClickAggregate(Rectangle visible, Rectangle regionArea, Rectangle[] screens)
        {
            long signature = visible.X * 1000003L + visible.Y;
            signature = signature * 31 + visible.Width * 1003L + visible.Height;
            signature = signature * 31 + regionArea.X * 1000003L + regionArea.Y;
            signature = signature * 31 + regionArea.Width * 1003L + regionArea.Height;
            signature = signature * 31 + (_clickFocused ? 1 : 0) + (_clickScreen + 2);
            if (screens != null)
                foreach (Rectangle rect in screens)
                    signature = signature * 131 + rect.X + rect.Y * 7L + rect.Width * 13L + rect.Height * 17L;
            foreach (DayRecord day in ClickRangeDays())
            {
                signature = signature * 131 + (day == null ? 0 : day.ClickCells.Count);
                signature = signature * 131 + (day == null ? 0 : day.Clicks);
            }
            if (_clickCache == null || _clickCacheRange != _clickRange || _clickCacheSignature != signature)
            {
                _clickCache = ClickHeat.Collect(ClickRangeDays(), visible);
                _clickCache.ComputeRegions(visible, regionArea, screens);
                _clickCacheRange = _clickRange;
                _clickCacheSignature = signature;
            }
            return _clickCache;
        }

        /// <summary>当前选中的屏幕下标;-1 或越界时回落到主屏。</summary>
        private int ClickScreenIndex(Screen[] screens)
        {
            if (screens == null || screens.Length == 0) return 0;
            int index = _clickScreen;
            if (index < 0 || index >= screens.Length)
            {
                index = 0;
                for (int i = 0; i < screens.Length; i++) if (screens[i].Primary) { index = i; break; }
            }
            return Math.Max(0, Math.Min(screens.Length - 1, index));
        }

        private void ClickScreenMenu()
        {
            ContextMenuStrip menu = PreparePopup(ref _clickScreenMenu);
            Screen[] screens = Screen.AllScreens;
            int current = ClickScreenIndex(screens);
            for (int i = 0; i < screens.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem("屏幕 " + (i + 1) + " · "
                    + screens[i].Bounds.Width + "×" + screens[i].Bounds.Height + (screens[i].Primary ? "（主屏）" : ""));
                item.Checked = index == current;
                item.Click += delegate { _clickScreen = index; Invalidate(); };
                menu.Items.Add(item);
            }
            menu.Show(this, PointToClient(Cursor.Position));
        }

        /// <summary>某点落在第几块屏(1 起;-1 表示不在任何屏幕上,例如混合 DPI 布局的空隙)。</summary>
        private int ScreenIndexOfPoint(int screenX, int screenY)
        {
            for (int i = 0; i < _clickScreenRects.Length; i++)
                if (_clickScreenRects[i].Contains(screenX, screenY)) return i + 1;
            return -1;
        }

        private string ClickRangeName()
        {
            return _clickRange == 0 ? "今日" : _clickRange == 1 ? "近 7 天" : "近 30 天";
        }

        private void PaintClickHeat(Graphics g)
        {
            Screen[] screens = Screen.AllScreens;
            int screenIndex = ClickScreenIndex(screens);
            Screen chosen = screens.Length > 0 ? screens[screenIndex] : null;
            Rectangle virtualBounds = SystemInformation.VirtualScreen;
            if (virtualBounds.Width <= 0 || virtualBounds.Height <= 0) virtualBounds = new Rectangle(0, 0, 1920, 1080);
            Rectangle[] screenRects = ClickHeat.ScreenRects(screens);
            if (screenRects.Length == 0) screenRects = new Rectangle[] { virtualBounds };
            Rectangle regionArea = chosen != null ? chosen.Bounds : screenRects[0];
            Rectangle visible = _clickFocused ? regionArea : virtualBounds;
            _clickScreenRects = screenRects;
            _clickScreenBounds = visible;
            ClickHeat.Aggregate aggregate = ClickAggregate(visible, regionArea, screenRects);

            string[] ranges = { "今日", "近 7 天", "近 30 天" };
            for (int i = 0; i < ranges.Length; i++)
            {
                RectangleF chip = new RectangleF(Cx + i * 98, 98, 88, 30);
                _chips.Add(chip);
                _chipIds.Add(510 + i);
                PaintChip(g, chip, ranges[i], _clickRange == i, null);
            }
            // 一级视图:跨屏全览 / 每块屏幕(单屏即放大形态),取代原来「下拉选屏 + 单屏放大」两个重叠控件。
            float viewX = Cx + 300;
            RectangleF gallery = new RectangleF(viewX, 98, 88, 30);
            _chips.Add(gallery); _chipIds.Add(515);
            PaintChip(g, gallery, "跨屏全览", !_clickFocused, null);
            viewX += 96;
            for (int i = 0; i < screens.Length && viewX < Cx + Cw - 108; i++)
            {
                float chipW = 104;
                RectangleF chip = new RectangleF(viewX, 98, chipW, 30);
                _chips.Add(chip); _chipIds.Add(530 + i);
                string label = "屏幕 " + (i + 1) + (screens[i].Primary ? " (主屏)" : "");
                PaintChip(g, chip, label, _clickFocused && screenIndex == i, null);
                viewX += chipW + 8;
            }
            RectangleF smoothToggle = new RectangleF(Cx + Cw - 96, 98, 96, 30);
            _chips.Add(smoothToggle); _chipIds.Add(517);
            PaintChip(g, smoothToggle, _clickSmooth ? "平滑热力" : "像素网格", _clickSmooth, null);

            // 左卡:屏幕 + 热区
            PaintCard(g, Cx, 138, 600, 438, null);
            using (Font title = new Font("Microsoft YaHei UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                AppText(g, "你的屏幕，点击的痕迹", title, Ctext, new RectangleF(Cx + 22, 152, 400, 28), false);
            string subtitle = screens.Length > 1
                ? (_clickFocused
                    ? "单屏放大 · 屏幕 " + (screenIndex + 1) + " · 九宫格按所选屏幕计算"
                    : "跨屏全览 · 九宫格按所选屏幕（屏幕 " + (screenIndex + 1) + "）计算")
                : "32 像素采样 · 只记位置，不截屏 · 位置明细保留 30 天";
            AppText(g, subtitle, _fSmall, Csub, new RectangleF(Cx + 22, 184, 560, 20), false);

            int hottest = ClickHeat.HottestRegion(aggregate);
            string[] labels = { "有位置的点击", "最热区域", "单格最多" };
            string[] values = {
                Analysis.FmtCount(aggregate.Total),
                hottest >= 0 ? ClickHeat.RegionName(hottest) : "--",
                aggregate.Max > 0 ? Analysis.FmtCount(aggregate.Max) + " 次" : "--"
            };
            for (int i = 0; i < 3; i++)
            {
                float x = Cx + 22 + i * 188;
                AppText(g, labels[i], _fSmall, Csub, new RectangleF(x, 208, 150, 18), false);
                AppText(g, values[i], _fNum2, Ctext, new RectangleF(x, 224, 170, 26), false);
            }

            _clickTray = new RectangleF(Cx + 14, 254, 572, 308);
            PaintClickTray(g, aggregate, screenIndex, screens);
            PaintClickHover(g, aggregate);
            // 右卡:所选屏幕的九宫格
            PaintCard(g, Cx + 612, 138, 220, 438, null);
            using (Font title = new Font("Microsoft YaHei UI", 13, FontStyle.Bold, GraphicsUnit.Pixel))
                AppText(g, "屏幕 " + (screenIndex + 1) + " 九宫格", title, Ctext, new RectangleF(Cx + 630, 152, 190, 24), false);
            AppText(g, regionArea.Width + "×" + regionArea.Height + (chosen != null && chosen.Primary ? " · 主屏" : "")
                + " · " + Analysis.FmtCount(aggregate.RegionTotal) + " 次", _fSmall, Csub,
                new RectangleF(Cx + 630, 170, 190, 18), false);

            float gridX = Cx + 628, gridY = 188, gridW = 188, cellW = gridW / 3f, cellH = 56f;
            PointF gridMouse = ToBase(_mouse);
            gridMouse = new PointF(gridMouse.X - ContentX, gridMouse.Y - ContentY);
            _clickGridRects.Clear();
            _clickGridHover = -1;
            long regionMax = 0;
            for (int i = 0; i < 9; i++) if (aggregate.Regions[i] > regionMax) regionMax = aggregate.Regions[i];
            for (int i = 0; i < 9; i++)
            {
                int row = i / 3, col = i % 3;
                RectangleF cell = new RectangleF(gridX + col * cellW + 2, gridY + row * cellH + 2, cellW - 4, cellH - 4);
                _clickGridRects.Add(cell);
                if (cell.Contains(gridMouse))
                {
                    _clickGridHover = i;
                    using (Pen outline = new Pen(Cblue, 2f))
                        using (GraphicsPath hoverPath = RoundedRect(cell.X, cell.Y, cell.Width, cell.Height, 7))
                            g.DrawPath(outline, hoverPath);
                }
                Color fill = ClickHeat.Heat(aggregate.Regions[i], regionMax);
                using (GraphicsPath path = RoundedRect(cell.X, cell.Y, cell.Width, cell.Height, 7))
                {
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                    using (Pen pen = new Pen(i == hottest ? ArtTheme.Current.OnAccent : ArtTheme.Mix(fill, Ctext, 0.18), i == hottest ? 1.8f : 1f))
                        g.DrawPath(pen, path);
                }
                using (SolidBrush ink = new SolidBrush(HeatInk(fill)))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(ClickHeat.RegionName(i), _fSmall, ink, new RectangleF(cell.X, cell.Y + 6, cell.Width, 18), format);
                AppText(g, aggregate.Regions[i] > 0 ? Analysis.FmtCount(aggregate.Regions[i]) : "--", _fSmall,
                    HeatInk(fill), new RectangleF(cell.X, cell.Bottom - 22, cell.Width, 18), true);
            }

            // 九宫格悬停联动:在主图对应屏幕上打一层聚光矩形,不用文字也能对上位置。
            if (_clickGridHover >= 0 && chosen != null && _clickProjection != null)
            {
                int hoverRow = _clickGridHover / 3, hoverCol = _clickGridHover % 3;
                Rectangle region = new Rectangle(
                    regionArea.X + hoverCol * regionArea.Width / 3,
                    regionArea.Y + hoverRow * regionArea.Height / 3,
                    Math.Max(1, regionArea.Width / 3),
                    Math.Max(1, regionArea.Height / 3));
                RectangleF projected = _clickProjection.ToContent(region);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(56, Cblue))) g.FillRectangle(glow, projected);
                using (Pen outline = new Pen(Cblue, 2f)) g.DrawRectangle(outline, projected.X, projected.Y, projected.Width, projected.Height);
                using (SolidBrush ink = new SolidBrush(Ctext))
                    g.DrawString(ClickHeat.RegionName(_clickGridHover), _fSmall, ink, projected.X + 4, projected.Y + 2);
            }

            // 右卡下方:Top 3 区域 + 覆盖格子 + 其他屏幕/空隙提示
            List<int> ranked = new List<int>();
            for (int i = 0; i < 9; i++) if (aggregate.Regions[i] > 0) ranked.Add(i);
            ranked.Sort(delegate(int a, int b) { return aggregate.Regions[b].CompareTo(aggregate.Regions[a]); });
            float rowY = gridY + 3 * cellH + 10;
            AppText(g, "点击最多的区域", _fSmall, Csub, new RectangleF(Cx + 630, rowY, 180, 20), false);
            rowY += 20;
            for (int i = 0; i < Math.Min(3, ranked.Count); i++)
            {
                int region = ranked[i];
                double share = aggregate.RegionTotal > 0 ? (double)aggregate.Regions[region] / aggregate.RegionTotal : 0;
                AppText(g, (i + 1) + "  " + ClickHeat.RegionName(region), _fBody, i == 0 ? Ctext : Csub,
                    new RectangleF(Cx + 630, rowY, 84, 20), false);
                AppText(g, (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%", _fBody,
                    i == 0 ? Cblue : Csub, new RectangleF(Cx + 714, rowY, 100, 20), true);
                rowY += 21;
            }
            if (ranked.Count == 0)
            {
                AppText(g, "这块屏幕还没有位置数据", _fSmall, Csub, new RectangleF(Cx + 630, rowY, 190, 20), false);
                rowY += 21;
            }
            AppText(g, "覆盖格子", _fSmall, Csub, new RectangleF(Cx + 630, rowY + 4, 90, 20), false);
            AppText(g, aggregate.RegionCells.ToString(CultureInfo.InvariantCulture) + " 格 · " + aggregate.Days + " 天",
                _fSmall, aggregate.RegionCells > 0 ? Ctext : Csub, new RectangleF(Cx + 700, rowY + 4, 116, 20), true);

            List<string> notes = new List<string>();
            if (aggregate.OffRegion > 0) notes.Add("其他屏幕 " + Analysis.FmtCount(aggregate.OffRegion) + " 次");
            if (aggregate.Vacant > 0) notes.Add("屏幕间空隙 " + Analysis.FmtCount(aggregate.Vacant) + " 次");
            if (aggregate.Outside > 0) notes.Add("旧布局位置 " + Analysis.FmtCount(aggregate.Outside) + " 次");
            if (notes.Count > 0)
                AppText(g, string.Join(" · ", notes.ToArray()), _fSmall, Csub,
                    new RectangleF(Cx + 630, rowY + 24, 194, 18), false);

            // 颜色图例:标出真实次数范围,不用悬停去猜。
            float legendY = rowY + 44;
            HeatScale.Bar(g, new RectangleF(Cx + 630, legendY, 184, 8));
            long legendMax = aggregate.Max;
            AppText(g, legendMax > 0 ? "1 次" : "0 次", _fSmall, Csub, new RectangleF(Cx + 630, legendY + 10, 40, 16), false);
            AppText(g, legendMax > 1 ? Analysis.FmtCount(Math.Max(1, legendMax / 2)) + " 次" : "", _fSmall, Csub,
                new RectangleF(Cx + 700, legendY + 10, 50, 16), true);
            AppText(g, legendMax > 0 ? Analysis.FmtCount(legendMax) + " 次" : "--", _fSmall, Csub,
                new RectangleF(Cx + 784, legendY + 10, 42, 16), true);
        }

        /// <summary>屏幕托盘:缓存成位图,悬停重绘时只贴图。</summary>
        private void PaintClickTray(Graphics g, ClickHeat.Aggregate aggregate, int screenIndex, Screen[] screens)
        {
            int width = Math.Max(1, (int)Math.Ceiling(_clickTray.Width * _s));
            int height = Math.Max(1, (int)Math.Ceiling(_clickTray.Height * _s));
            string stamp = _clickRange + "|" + Store.ThemeId + "|" + _s.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                + width + "x" + height + "|" + aggregate.Total + "|" + aggregate.Max + "|" + aggregate.Cells.Count + "|"
                + (_clickFocused ? "F" : "A") + screenIndex + "|" + (_clickSmooth ? "S" : "G") + "|" + screens.Length + "|"
                + _clickScreenBounds.Left + "," + _clickScreenBounds.Top + "," + _clickScreenBounds.Width + "," + _clickScreenBounds.Height;
            if (_clickPlate == null || _clickStamp != stamp || _clickPlate.Width != width || _clickPlate.Height != height)
            {
                if (_clickPlate != null) { _clickPlate.Dispose(); _clickPlate = null; }
                _clickPlate = new Bitmap(width, height);
                using (Graphics plate = Graphics.FromImage(_clickPlate))
                {
                    plate.SmoothingMode = SmoothingMode.AntiAlias;
                    plate.ScaleTransform(_s, _s);
                    plate.TranslateTransform(-_clickTray.X, -_clickTray.Y);
                    DrawClickTrayContent(plate, aggregate, screenIndex, screens);
                }
                _clickStamp = stamp;
            }
            g.DrawImage(_clickPlate, _clickTray);
        }

        private void DrawClickTrayContent(Graphics g, ClickHeat.Aggregate aggregate, int screenIndex, Screen[] screens)
        {
            using (GraphicsPath shell = RoundedRect(_clickTray.X, _clickTray.Y, _clickTray.Width, _clickTray.Height, 14))
            {
                using (SolidBrush background = new SolidBrush(ArtTheme.Mix(Ccard, ArtTheme.Current.Background, 0.64)))
                    g.FillPath(background, shell);
                using (Pen edge = new Pen(ArtTheme.Mix(Ccard, Ctext, 0.07))) g.DrawPath(edge, shell);
            }

            RectangleF inner = new RectangleF(_clickTray.X + 10, _clickTray.Y + 10, _clickTray.Width - 20, _clickTray.Height - 20);
            ClickHeat.Projection projection = ClickHeat.Fit(_clickScreenBounds, inner);
            _clickProjection = projection;

            _clickCellRects.Clear();
            _clickCellKeys.Clear();
            _clickCellCounts.Clear();

            // 每块屏幕的底:暗色玻璃 + 边框。九宫格选中的屏幕加发光边框,其余降透明度。
            for (int i = 0; i < screens.Length; i++)
            {
                if (_clickFocused && i != screenIndex) continue;
                RectangleF rect = projection.ToContent(screens[i].Bounds);
                bool selected = i == screenIndex;
                using (GraphicsPath path = RoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 8))
                {
                    using (SolidBrush background = new SolidBrush(Color.FromArgb(selected ? 46 : 22, Ctext))) g.FillPath(background, path);
                    if (selected && !_clickFocused)
                        using (Pen glow = new Pen(Color.FromArgb(40, Cblue), 4f)) g.DrawPath(glow, path);
                    using (Pen border = new Pen(selected ? Color.FromArgb(170, Cblue) : Color.FromArgb(40, Ctext), selected ? 1.8f : 1f))
                        g.DrawPath(border, path);
                }
            }

            // 热力格。按可见范围与当前屏幕裁剪:布局变过的旧位置、混合 DPI 布局里的屏幕空隙都不画。
            // 悬停高亮不烘进位图:缓存图一旦带上鼠标位置,移开之后那格会一直亮着。
            foreach (KeyValuePair<int, long> pair in aggregate.Cells)
            {
                int cellX, cellY;
                ClickHeat.SplitKey(pair.Key, out cellX, out cellY);
                int sx = ClickHeat.CellLeft(cellX), sy = ClickHeat.CellLeft(cellY);
                int centerX = sx + ClickHeat.Cell / 2, centerY = sy + ClickHeat.Cell / 2;
                if (!ClickHeat.OnAnyScreen(centerX, centerY, _clickScreenRects)) continue;
                if (!projection.Visible.Contains(centerX, centerY)) continue;
                PointF origin = projection.ToContent(sx, sy);
                float size = Math.Max(1.5f, ClickHeat.Cell * projection.Scale + 0.35f);
                RectangleF rect = new RectangleF(origin.X, origin.Y, size, size);
                Color fill = ClickHeat.Heat(pair.Value, aggregate.Max);
                double warmth = Math.Log(1.0 + pair.Value) / Math.Log(1.0 + Math.Max(1, aggregate.Max));
                int alpha = 120 + (int)(95 * Math.Min(1, warmth));
                if (_clickSmooth)
                {
                    // 轻量高斯云:每格画一个由中心向外透明的径向渐变,相邻格自然融合。
                    float blob = Math.Max(10f, size * 3.2f);
                    using (GraphicsPath cloud = new GraphicsPath())
                    {
                        cloud.AddEllipse(rect.X + rect.Width / 2 - blob / 2, rect.Y + rect.Height / 2 - blob / 2, blob, blob);
                        using (PathGradientBrush brush = new PathGradientBrush(cloud))
                        {
                            brush.CenterColor = Color.FromArgb(Math.Min(210, alpha + 30), fill);
                            brush.SurroundColors = new Color[] { Color.FromArgb(0, fill) };
                            g.FillPath(brush, cloud);
                        }
                    }
                }
                else
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, fill)))
                    g.FillRectangle(brush, rect);
                _clickCellRects.Add(rect);
                _clickCellKeys.Add(pair.Key);
                _clickCellCounts.Add(pair.Value);
            }

            // 系统轮廓骨架:底部任务栏 + 顶部标题/标签栏参考线,帮用户把热区与桌面位置对上。
            for (int i = 0; i < screens.Length; i++)
            {
                if (_clickFocused && i != screenIndex) continue;
                RectangleF rect = projection.ToContent(screens[i].Bounds);
                if (rect.Width < 60 || rect.Height < 40) continue;
                float taskbarH = Math.Max(3f, rect.Height * 0.045f);
                using (SolidBrush bar = new SolidBrush(Color.FromArgb(26, Ctext)))
                    g.FillRectangle(bar, rect.X, rect.Bottom - taskbarH, rect.Width, taskbarH);
                using (Pen line = new Pen(Color.FromArgb(55, Ctext), 1f))
                {
                    g.DrawLine(line, rect.X, rect.Bottom - taskbarH, rect.Right, rect.Bottom - taskbarH);
                    float titleLine = rect.Y + Math.Max(3f, rect.Height * 0.075f);
                    g.DrawLine(line, rect.X + 2, titleLine, rect.Right - 2, titleLine);
                }
                // 任务栏右侧的托盘区微标:三个小方块。
                using (SolidBrush tray = new SolidBrush(Color.FromArgb(60, Ctext)))
                    for (int k = 0; k < 3; k++)
                        g.FillRectangle(tray, rect.Right - 8 - k * 5, rect.Bottom - taskbarH + taskbarH / 2 - 1.5f, 3, 3);
            }

            if (aggregate.Total == 0)
            {
                string empty = _clickFocused
                    ? "这块屏幕还没有点击位置数据\n从这一版开始记录，用一会儿鼠标就会出现"
                    : "还没有点击位置数据\n从这一版开始记录，用一会儿鼠标就会出现";
                using (SolidBrush ink = new SolidBrush(Csub))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(empty, _fBody, ink,
                        new RectangleF(_clickTray.X, _clickTray.Y + _clickTray.Height / 2 - 34, _clickTray.Width, 60), format);
            }

            // 屏幕编号叠在热力格之上:格子是后画的,先画编号会被盖住。
            for (int i = 0; i < screens.Length; i++)
            {
                if (_clickFocused && i != screenIndex) continue;
                RectangleF rect = projection.ToContent(screens[i].Bounds);
                if (rect.Width < 46) continue;
                string label = "屏幕 " + (i + 1);
                SizeF size = g.MeasureString(label, _fSmall);
                RectangleF badge = new RectangleF(rect.X + 5, rect.Y + 4, size.Width + 8, size.Height + 2);
                using (GraphicsPath path = RoundedRect(badge.X, badge.Y, badge.Width, badge.Height, 4))
                {
                    using (SolidBrush background = new SolidBrush(Color.FromArgb(160, ArtTheme.Current.Sidebar))) g.FillPath(background, path);
                    using (Pen border = new Pen(Color.FromArgb(45, Ctext))) g.DrawPath(border, path);
                }
                using (SolidBrush ink = new SolidBrush(Ctext)) g.DrawString(label, _fSmall, ink, badge.X + 4, badge.Y + 1);
            }
        }

        /// <summary>鼠标位置对应的格子(内容坐标)。返回 false 表示没有踩到有数据的格子。</summary>
        private bool HitClickCell(ClickHeat.Aggregate aggregate, out int key, out int cellX, out int cellY)
        {
            key = 0;
            cellX = cellY = 0;
            PointF point = ToBase(_mouse);
            point = new PointF(point.X - ContentX, point.Y - ContentY);
            if (_clickProjection == null || !_clickTray.Contains(point)) return false;
            int sx, sy;
            if (!_clickProjection.TryUnproject(point, out sx, out sy)) return false;
            if (!ClickHeat.OnAnyScreen(sx, sy, _clickScreenRects)) return false;
            key = ClickHeat.CellOf(sx, sy, out cellX, out cellY);
            long count;
            return aggregate.Cells.TryGetValue(key, out count);
        }

        private void PaintClickHover(Graphics g, ClickHeat.Aggregate aggregate)
        {
            _hoverDetail = "";
            Point point = _mouse;
            if (point.X < 0 && point.Y < 0) return;
            int cellX, cellY, key;
            if (!HitClickCell(aggregate, out key, out cellX, out cellY)) return;
            long count;
            aggregate.Cells.TryGetValue(key, out count);
            int hoverX = ClickHeat.CellLeft(cellX) + ClickHeat.Cell / 2;
            int hoverY = ClickHeat.CellLeft(cellY) + ClickHeat.Cell / 2;
            int screenNo = ScreenIndexOfPoint(hoverX, hoverY);

            for (int i = 0; i < _clickCellKeys.Count; i++)
            {
                if (_clickCellKeys[i] != key) continue;
                RectangleF rect = _clickCellRects[i];
                using (Pen pen = new Pen(ArtTheme.Current.OnAccent, 1.6f)) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;
            }

            Rectangle regionArea = aggregate.RegionArea;
            bool onChosen = regionArea.Width > 0 && regionArea.Height > 0 && regionArea.Contains(hoverX, hoverY);
            double denominator = onChosen ? aggregate.RegionTotal : aggregate.Total;
            double share = denominator > 0 ? (double)count / denominator * 100 : 0;
            string where = onChosen ? ClickHeat.RegionName(ClickHeat.RegionAt(hoverX, hoverY, regionArea))
                : screenNo > 0 ? "屏幕 " + screenNo : "屏幕间空隙";
            string line1 = where + "  ·  " + Analysis.FmtCount(count) + " 次";
            string line2 = "屏幕 (" + hoverX + ", " + hoverY + ")  ·  " + (onChosen ? "占所选屏幕 " : "占全部 ")
                + share.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            _hoverDetail = "点击热区 " + ClickRangeName() + " · " + line1 + " · " + line2;

            PointF basePoint = ToBase(_mouse);
            PointF contentPoint = new PointF(basePoint.X - ContentX, basePoint.Y - ContentY);
            using (Font font = new Font("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF size1 = g.MeasureString(line1, font);
                SizeF size2 = g.MeasureString(line2, font);
                float w = Math.Max(size1.Width, size2.Width) + 20;
                float h = 42;
                float x = contentPoint.X + 16, y = contentPoint.Y + 16;
                if (x + w > Cw - 4) x = contentPoint.X - w - 12;
                if (y + h > Cb - 4) y = contentPoint.Y - h - 12;
                RectangleF box = new RectangleF(x, y, w, h);
                using (GraphicsPath path = RoundedRect(box.X, box.Y, box.Width, box.Height, 8))
                {
                    using (SolidBrush background = new SolidBrush(Color.FromArgb(236, ArtTheme.Current.Sidebar))) g.FillPath(background, path);
                    using (Pen border = new Pen(Color.FromArgb(90, Ctext))) g.DrawPath(border, path);
                }
                using (SolidBrush ink = new SolidBrush(Ctext)) g.DrawString(line1, font, ink, x + 10, y + 5);
                using (SolidBrush ink = new SolidBrush(Csub)) g.DrawString(line2, font, ink, x + 10, y + 22);
            }
        }

        private void ExportClickHeat()
        {
            try
            {
                Rectangle bounds = SystemInformation.VirtualScreen;
                Rectangle[] screens = ClickHeat.ScreenRects(Screen.AllScreens);
                ClickHeat.Aggregate aggregate = ClickHeat.Collect(ClickRangeDays(), bounds);
                StringBuilder csv = new StringBuilder();
                csv.AppendLine("# 键鼠统计 · 屏幕点击热区");
                csv.AppendLine("# 区间," + ClickRangeName());
                csv.AppendLine("# 采样格(屏幕像素)," + ClickHeat.Cell);
                csv.AppendLine("# 有位置的点击," + aggregate.Total);
                csv.AppendLine("# 记录天数," + aggregate.Days);
                csv.AppendLine("# 虚拟桌面范围(左,上,宽,高)," + bounds.Left + "," + bounds.Top + "," + bounds.Width + "," + bounds.Height);
                for (int i = 0; i < screens.Length; i++)
                    csv.AppendLine("# 显示器," + (i + 1) + "," + screens[i].Left + "," + screens[i].Top + "," + screens[i].Width + "," + screens[i].Height);
                csv.AppendLine("# 屏幕间空隙(混合 DPI 布局可能有),由下面「屏幕编号=0」的行表示");
                csv.AppendLine("屏幕编号,屏幕X,屏幕Y,次数,占比");
                List<int> keys = new List<int>(aggregate.Cells.Keys);
                keys.Sort(delegate(int a, int b)
                {
                    int ax, ay, bx, by;
                    ClickHeat.SplitKey(a, out ax, out ay);
                    ClickHeat.SplitKey(b, out bx, out by);
                    int compare = ax.CompareTo(bx);
                    return compare != 0 ? compare : ay.CompareTo(by);
                });
                foreach (int key in keys)
                {
                    int cellX, cellY;
                    ClickHeat.SplitKey(key, out cellX, out cellY);
                    long count = aggregate.Cells[key];
                    int centerX = ClickHeat.CellLeft(cellX) + ClickHeat.Cell / 2;
                    int centerY = ClickHeat.CellLeft(cellY) + ClickHeat.Cell / 2;
                    int screenNo = 0;
                    for (int i = 0; i < screens.Length; i++)
                        if (screens[i].Contains(centerX, centerY)) { screenNo = i + 1; break; }
                    double share = aggregate.Total > 0 ? (double)count / aggregate.Total : 0;
                    csv.Append(screenNo).Append(',')
                       .Append(centerX).Append(',')
                       .Append(centerY).Append(',')
                       .Append(count).Append(',')
                       .Append(share.ToString("0.######", CultureInfo.InvariantCulture)).AppendLine();
                }
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "点击热区_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
                File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
