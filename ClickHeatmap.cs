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

            // v3.0:同一份数据再按通道记一份。ClickCells 仍是「全通道汇总」,
            // 保证旧版本与「全部点击」视图读到的数值一个都不变。
            int channel = ChannelOf(button);
            long v1Key = ((long)channel << 32) | (uint)key;
            if (day.ClickCellsV1 == null) day.ClickCellsV1 = new Dictionary<long, long>();
            long channelCount;
            day.ClickCellsV1.TryGetValue(v1Key, out channelCount);
            day.ClickCellsV1[v1Key] = channelCount + 1;
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
        // ---------------------------------------------------------------- 三通道(v3.0)

        /// <summary>把按键序号归一成通道:1/2/3 = 左/右/中,≥4 = 侧键,其余 = 0(未知)。</summary>
        public static int ChannelOf(int button)
        {
            if (button >= 1 && button <= 3) return button;
            if (button >= 4) return 4;
            return 0;
        }

        /// <summary>通道键打包:高 32 位通道,低 32 位 CellKey。</summary>
        public static long ChannelKey(int channel, int cellKey)
        {
            return ((long)channel << 32) | (uint)cellKey;
        }

        public static int ChannelOfKey(long key)
        {
            return (int)(key >> 32);
        }

        public static int CellKeyOfChannelKey(long key)
        {
            return (int)(key & 0xFFFFFFFF);
        }

        /// <summary>一天的三通道明细写成 "channel:cx:cy:count,...";按打包键排序保证文件稳定。</summary>
        public static string EncodeV1(Dictionary<long, long> cells)
        {
            if (cells == null || cells.Count == 0) return "";
            List<long> keys = new List<long>(cells.Keys);
            keys.Sort();
            StringBuilder sb = new StringBuilder(keys.Count * 12);
            foreach (long key in keys)
            {
                long count = cells[key];
                if (count <= 0) continue;
                int channel = ChannelOfKey(key);
                int cellX, cellY;
                SplitKey(CellKeyOfChannelKey(key), out cellX, out cellY);
                if (sb.Length > 0) sb.Append(',');
                sb.Append(channel.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(cellX.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(cellY.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(count.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static void DecodeV1(string text, Dictionary<long, long> target)
        {
            if (string.IsNullOrEmpty(text) || target == null) return;
            string[] parts = text.Split(',');
            int capacity = MaxCellsPerDay * 5;
            for (int i = 0; i < parts.Length && target.Count < capacity; i++)
            {
                string[] fields = parts[i].Split(':');
                if (fields.Length != 4) continue;
                int channel, cellX, cellY;
                long count;
                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out channel)) continue;
                if (channel < 0 || channel > 4) continue;
                if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellX)) continue;
                if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cellY)) continue;
                if (!long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) continue;
                if (count <= 0) continue;
                if (cellX < -32768 || cellX > 32767 || cellY < -32768 || cellY > 32767) continue;
                long key = ChannelKey(channel, CellKey(cellX, cellY));
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
                if (pair.Key >= oldest || pair.Value == null
                    || (pair.Value.ClickCells.Count == 0 && pair.Value.ClickCellsV1.Count == 0)) continue;
                cleared += pair.Value.ClickCells.Count;
                pair.Value.ClickCells.Clear();
                pair.Value.ClickCellsV1.Clear();
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
            /// <summary>本次聚合使用的通道过滤(0 = 全部点击)。</summary>
            public int Channel;
            /// <summary>区间内 channel=0(未区分按键)的历史点击数。</summary>
            public long LegacyTotal;
            /// <summary>分通道查询遇到未分类历史数据时为 true,供 UI 提示「早期版本未区分按键」。</summary>
            public bool HasLegacyUnclassified;

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

        /// <summary>按传入的屏幕范围聚合若干天(通道 0 = 全部点击,与 v2.4.0 完全等价)。</summary>
        public static Aggregate Collect(IEnumerable<DayRecord> days, Rectangle bounds)
        {
            return Collect(days, bounds, 0);
        }

        /// <summary>
        /// 带通道过滤的聚合。channel = 0 表示「全部点击」——它的逐格数值与 v2.4.0 完全一致;
        /// 1/2/3/4 分别只看 左/右/中/侧。
        /// 旧档只有 ch=(无 chv1)时,整格映射到 channel = 0 兼容通道,**绝不伪造归入左键**。
        /// </summary>
        public static Aggregate Collect(IEnumerable<DayRecord> days, Rectangle bounds, int channel)
        {
            Aggregate aggregate = new Aggregate();
            aggregate.Channel = channel;
            if (days == null) return aggregate;
            foreach (DayRecord day in days)
            {
                if (day == null) continue;
                bool hasFlat = day.ClickCells != null && day.ClickCells.Count > 0;
                bool hasChanneled = day.ClickCellsV1 != null && day.ClickCellsV1.Count > 0;
                if (!hasFlat && !hasChanneled) continue;
                bool useChanneled = hasChanneled && InvariantHolds(day);
                if (!useChanneled && !hasFlat) continue;
                aggregate.Days++;

                if (useChanneled)
                {
                    foreach (KeyValuePair<long, long> pair in day.ClickCellsV1)
                    {
                        long count = pair.Value;
                        if (count <= 0) continue;
                        int entryChannel = (int)(pair.Key >> 32);
                        int cellKey = (int)(pair.Key & 0xFFFFFFFF);
                        if (entryChannel == 0) aggregate.LegacyTotal += count;
                        if (channel != 0 && entryChannel != channel) continue;
                        Accumulate(aggregate, cellKey, count);
                    }
                }
                else
                {
                    foreach (KeyValuePair<int, long> pair in day.ClickCells)
                    {
                        long count = pair.Value;
                        if (count <= 0) continue;
                        aggregate.LegacyTotal += count;
                        if (channel != 0) continue;
                        Accumulate(aggregate, pair.Key, count);
                    }
                }
            }
            if (channel != 0 && aggregate.LegacyTotal > 0) aggregate.HasLegacyUnclassified = true;
            return aggregate;
        }

        private static void Accumulate(Aggregate aggregate, int cellKey, long count)
        {
            long old;
            aggregate.Cells.TryGetValue(cellKey, out old);
            aggregate.Cells[cellKey] = old + count;
            aggregate.Total += count;
            if (old + count > aggregate.Max)
            {
                aggregate.Max = old + count;
                aggregate.MaxKey = cellKey;
            }
        }

        /// <summary>
        /// chv1 与 ch 的一致性校验:分通道总和必须等于全通道汇总。
        /// 不成立时聚合退回 ch(权威总数),避免把损坏的 chv1 当真。
        /// </summary>
        internal static bool InvariantHolds(DayRecord day)
        {
            if (day == null || day.ClickCellsV1 == null || day.ClickCellsV1.Count == 0) return false;
            long channeled = 0;
            foreach (KeyValuePair<long, long> pair in day.ClickCellsV1)
                if (pair.Value > 0) channeled += pair.Value;
            if (day.ClickCells == null || day.ClickCells.Count == 0) return true;   // 只有 chv1 时直接信任
            long flat = 0;
            foreach (KeyValuePair<int, long> pair in day.ClickCells)
                if (pair.Value > 0) flat += pair.Value;
            return channeled == flat;
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

    /// <summary>鼠标动作视图的多日聚合；只读取已落盘的相对鼠标交叉数据。</summary>
    internal sealed class MouseActionStats
    {
        public readonly double[] Direction = new double[4]; // 左、右、上、下
        public long Flicks, Reversals, Packets, CalibratedPackets;
        public double DragCounts, PathCounts;
        public long Clicks, MoveClicks;
        public int Days;

        public bool HasData { get { return Packets > 0 || DirectionTotal > 0; } }
        public double DirectionTotal { get { return Direction[0] + Direction[1] + Direction[2] + Direction[3]; } }

        public double DirectionShare(int index)
        {
            return DirectionTotal > 0 ? Direction[index] / DirectionTotal : 0;
        }

        public double DragShare { get { return PathCounts > 0 ? DragCounts / PathCounts : 0; } }
        public double MoveClickShare { get { return Clicks > 0 ? MoveClicks / (double)Clicks : 0; } }

        public string HorizontalInsight()
        {
            double horizontal = Direction[0] + Direction[1];
            double vertical = Direction[2] + Direction[3];
            if (horizontal <= 0 || vertical <= 0) return "方向样本不足，继续使用后形成移动画像。";
            double ratio = horizontal / vertical;
            string amount = ratio >= 1 ? ratio.ToString("0.00", CultureInfo.InvariantCulture) + " : 1"
                : "1 : " + (1 / ratio).ToString("0.00", CultureInfo.InvariantCulture);
            return "水平 / 垂直 " + amount + (ratio >= 1.2 ? " · 典型横向跨屏特征" : ratio <= 0.83 ? " · 纵向移动为主" : " · 横纵较均衡");
        }

        public static MouseActionStats Build(IEnumerable<DayRecord> days)
        {
            MouseActionStats stats = new MouseActionStats();
            if (days == null) return stats;
            foreach (DayRecord day in days)
            {
                if (day == null || day.Cross == null) continue;
                CrossDay cross = day.Cross;
                if (cross.Packets > 0) stats.Days++;
                for (int i = 0; i < 4; i++) stats.Direction[i] += cross.Direction[i];
                stats.Flicks += cross.Flicks;
                stats.Reversals += cross.Reversals;
                stats.DragCounts += cross.DragCounts;
                stats.PathCounts += cross.PathCounts;
                stats.Clicks += cross.Clicks;
                stats.MoveClicks += cross.MoveClicks;
                stats.Packets += cross.Packets;
                stats.CalibratedPackets += cross.CalibratedPackets;
            }
            return stats;
        }

        public static string Percent(double value)
        {
            return value > 0 ? (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";
        }
    }

    /// <summary>
    /// 点击热区「三键通道」的可测门面(Milestone 2 / 3.3)。
    /// 全部是纯函数:不碰 Graphics、不碰 Form,状态机、chip 派发、缓存签名与
    /// 提示优先级都能被单测直接覆盖。
    /// </summary>
    internal static class ClickChannelUi
    {
        /// <summary>521=全部点击 / 522=左键 / 523=右键 / 524=中键。</summary>
        internal const int FirstChipId = 521;
        internal static readonly int[] ChannelChipIds = { 521, 522, 523, 524 };

        // 左卡内的摆放常量(相对 Cx,画布单位)。
        internal const int SegmentX = 22;
        internal const int SegmentY = 188;
        internal const int SegmentW = 236;

        internal static bool IsChannelChip(int id)
        {
            return id >= FirstChipId && id < FirstChipId + ChannelScale.Count;
        }

        internal static int ChannelOfChip(int id)
        {
            return IsChannelChip(id) ? id - FirstChipId : ChannelScale.All;
        }

        internal static int ChipOfChannel(int channel)
        {
            return FirstChipId + Clamp(channel);
        }

        internal static int Clamp(int channel)
        {
            if (channel < ChannelScale.All) return ChannelScale.All;
            if (channel >= ChannelScale.Count) return ChannelScale.Count - 1;
            return channel;
        }

        /// <summary>缓存签名片段:通道一变,离屏位图与聚合缓存都必须重算。</summary>
        internal static string StampSegment(int channel)
        {
            return "C" + Clamp(channel).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 历史数据提示的显示条件:切到单键视图、且聚合里存在未区分按键的历史点击。
        /// 「全部点击」永远不显示——它本来就包含历史数据。
        /// </summary>
        internal static bool ShouldShowLegacyPill(int channel, bool hasLegacyUnclassified)
        {
            return Clamp(channel) > ChannelScale.All && hasLegacyUnclassified;
        }

        /// <summary>「还没有点击位置数据」的显示条件:必须让位给历史提示胶囊。</summary>
        internal static bool ShouldShowEmpty(int channel, bool hasLegacyUnclassified, long total)
        {
            return total == 0 && !ShouldShowLegacyPill(channel, hasLegacyUnclassified);
        }

        internal static string LegacyPillText
        {
            get { return "早期版本记录未区分按键，仅「全部点击」包含历史数据"; }
        }

        /// <summary>左卡统计摘要首格文案,随通道变化。</summary>
        internal static string TotalLabel(int channel)
        {
            int c = Clamp(channel);
            return c == ChannelScale.All ? "有位置的点击" : ChannelScale.NameOf(c) + "点击数";
        }

        /// <summary>右卡「点击最多的区域」标题,随通道变化。</summary>
        internal static string MostHeading(int channel)
        {
            int c = Clamp(channel);
            return c == ChannelScale.All ? "点击最多的区域" : ChannelScale.NameOf(c) + "最常点击";
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
        /// <summary>0 全部点击 / 1 左键 / 2 右键 / 3 中键(Milestone 2 / 3.3)。</summary>
        private int _clickChannel;
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
        private bool _clickActionView;
        private MouseActionStats _mouseActionCache;
        private int _mouseActionRange = -1;
        private long _mouseActionSignature;
        private RectangleF _mouseActionInfoRect;
        private bool _mouseActionInfoHover;

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
        private ClickHeat.Aggregate ClickAggregate(Rectangle visible, Rectangle regionArea, Rectangle[] screens, int channel)
        {
            long signature = visible.X * 1000003L + visible.Y;
            signature = signature * 31 + visible.Width * 1003L + visible.Height;
            signature = signature * 31 + regionArea.X * 1000003L + regionArea.Y;
            signature = signature * 31 + regionArea.Width * 1003L + regionArea.Height;
            signature = signature * 31 + (_clickFocused ? 1 : 0) + (_clickScreen + 2);
            signature = signature * 31 + ClickChannelUi.Clamp(channel);
            if (screens != null)
                foreach (Rectangle rect in screens)
                    signature = signature * 131 + rect.X + rect.Y * 7L + rect.Width * 13L + rect.Height * 17L;
            foreach (DayRecord day in ClickRangeDays())
            {
                signature = signature * 131 + (day == null ? 0 : day.ClickCells.Count);
                signature = signature * 131 + (day == null ? 0 : day.ClickCellsV1.Count);
                signature = signature * 131 + (day == null ? 0 : day.Clicks);
            }
            if (_clickCache == null || _clickCacheRange != _clickRange || _clickCacheSignature != signature)
            {
                _clickCache = ClickHeat.Collect(ClickRangeDays(), visible, ClickChannelUi.Clamp(channel));
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

        private void PaintClickRangeChips(Graphics g)
        {
            string[] ranges = { "今日", "近 7 天", "近 30 天" };
            for (int i = 0; i < ranges.Length; i++)
            {
                RectangleF chip = new RectangleF(Cx + i * 98, 98, 88, 30);
                _chips.Add(chip);
                _chipIds.Add(510 + i);
                PaintChip(g, chip, ranges[i], _clickRange == i, null);
            }
        }

        /// <summary>点击热区 / 鼠标动作的二级视图切换；两个视图共用顶部时间范围。</summary>
        private void PaintClickViewToggle(Graphics g)
        {
            AppChip(g, 519, Cx + 330, 150, 92, "点击热区", !_clickActionView);
            AppChip(g, 520, Cx + 426, 150, 100, "鼠标动作", _clickActionView);
        }

        /// <summary>
        /// 三键通道切换(Milestone 2 / 3.3):全部点击 | 左键 | 右键 | 中键。
        /// 走标准 PaintSegmentGroup,因而自动登记进 _chips / _chipIds —— 键盘 Tab 遍历无需额外代码。
        /// </summary>
        private void PaintClickChannelChips(Graphics g)
        {
            PaintSegmentGroup(g, Cx + ClickChannelUi.SegmentX, ClickChannelUi.SegmentY,
                ClickChannelUi.SegmentW, ChannelScale.Names, ClickChannelUi.ChannelChipIds,
                ClickChannelUi.Clamp(_clickChannel), false);
        }

        private void PaintClickHeat(Graphics g)
        {
            PaintClickRangeChips(g);
            if (_clickActionView) { PaintMouseAction(g); return; }

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
            ClickHeat.Aggregate aggregate = ClickAggregate(visible, regionArea, screenRects, _clickChannel);

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
            // C 批:三栏按 Cw 比例(基准 832 宽时左卡仍是 600、右卡 220)
            float heatLeft = Cw * 0.721f;
            PaintCard(g, Cx, 138, heatLeft, 438f + ElasticH, null);
            using (Font title = new Font("Microsoft YaHei UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                AppText(g, "你的屏幕，点击的痕迹", title, Ctext, new RectangleF(Cx + 22, 152, 400, 28), false);
            PaintClickViewToggle(g);
            PaintClickChannelChips(g);
            string subtitle = screens.Length > 1
                ? (_clickFocused
                    ? "单屏放大 · 屏幕 " + (screenIndex + 1) + " · 九宫格按所选屏幕计算"
                    : "跨屏全览 · 九宫格按所选屏幕（屏幕 " + (screenIndex + 1) + "）计算")
                : "32 像素采样 · 只记位置，不截屏 · 位置明细保留 30 天";
            AppText(g, subtitle, _fSmall, Csub, new RectangleF(Cx + 270, 194, 308, 18), false);

            int hottest = ClickHeat.HottestRegion(aggregate);
            string[] labels = { ClickChannelUi.TotalLabel(_clickChannel), "最热区域", "单格最多" };
            string[] values = {
                Analysis.FmtCount(aggregate.Total),
                hottest >= 0 ? ClickHeat.RegionName(hottest) : "--",
                aggregate.Max > 0 ? Analysis.FmtCount(aggregate.Max) + " 次" : "--"
            };
            for (int i = 0; i < 3; i++)
            {
                float x = Cx + 22 + i * 188;
                AppText(g, labels[i], _fSmall, Csub, new RectangleF(x, 220, 150, 18), false);
                AppText(g, values[i], _fNum2, Ctext, new RectangleF(x, 236, 170, 26), false);
            }

            _clickTray = new RectangleF(Cx + 14, 266, Math.Max(120f, heatLeft - 28f), 296f + ElasticH);
            PaintClickTray(g, aggregate, screenIndex, screens);
            PaintClickHover(g, aggregate);
            // 右卡:所选屏幕的九宫格
            PaintCard(g, Cx + heatLeft + 12, 138, Cw - heatLeft - 12, 438f + ElasticH, null);
            using (Font title = new Font("Microsoft YaHei UI", 13, FontStyle.Bold, GraphicsUnit.Pixel))
                AppText(g, "屏幕 " + (screenIndex + 1) + " 九宫格", title, Ctext, new RectangleF(Cx + heatLeft + 28, 152, Math.Max(120f, Cw - heatLeft - 44f), 24), false);
            AppText(g, regionArea.Width + "×" + regionArea.Height + (chosen != null && chosen.Primary ? " · 主屏" : "")
                + " · " + Analysis.FmtCount(aggregate.RegionTotal) + " 次", _fSmall, Csub,
                new RectangleF(Cx + heatLeft + 28, 170, Math.Max(120f, Cw - heatLeft - 44f), 18), false);

            // 3.0:九宫格高度跟着右卡长高(旧版写死 56,2K 下 3×56 只占掉卡片顶部一条),
            // 格子封顶 120 并在剩余空间里垂直居中,避免出现顶边一条、下面一大片的失衡。
            float rightCardBottom = 138f + 438f + ElasticH;
            float gridTop = 188f;
            float gridAvail = Math.Max(168f, rightCardBottom - gridTop - 208f);   // 底部给图例块留 ~200px
            float cellH = Math.Min(120f, Math.Max(56f, gridAvail / 3f));
            float gridY = gridTop + Math.Max(0f, (gridAvail - cellH * 3f) / 2f);
            float gridX = Cx + heatLeft + 28, gridW = Math.Max(72f, Cw - heatLeft - 44f), cellW = gridW / 3f;
            PointF gridMouse = MouseContent();
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
                Color fill = ChannelScale.For(_clickChannel, aggregate.Regions[i], regionMax);
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
            float legendX = Cx + heatLeft + 28;
            float legendW = Math.Max(120f, Cw - heatLeft - 44f);
            float rowY = Math.Max(gridY + 3 * cellH + 10, rightCardBottom - 200f);
            AppText(g, ClickChannelUi.MostHeading(_clickChannel), _fSmall, Csub, new RectangleF(legendX, rowY, 180, 20), false);
            rowY += 20;
            for (int i = 0; i < Math.Min(3, ranked.Count); i++)
            {
                int region = ranked[i];
                double share = aggregate.RegionTotal > 0 ? (double)aggregate.Regions[region] / aggregate.RegionTotal : 0;
                AppText(g, (i + 1) + "  " + ClickHeat.RegionName(region), _fBody, i == 0 ? Ctext : Csub,
                    new RectangleF(legendX, rowY, 84, 20), false);
                AppText(g, (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%", _fBody,
                    i == 0 ? Cblue : Csub, new RectangleF(legendX + 84, rowY, Math.Max(40f, legendW - 84f), 20), true);
                rowY += 21;
            }
            if (ranked.Count == 0)
            {
                AppText(g, "这块屏幕还没有位置数据", _fSmall, Csub, new RectangleF(legendX, rowY, 190, 20), false);
                rowY += 21;
            }
            AppText(g, "覆盖格子", _fSmall, Csub, new RectangleF(legendX, rowY + 4, 90, 20), false);
            AppText(g, aggregate.RegionCells.ToString(CultureInfo.InvariantCulture) + " 格 · " + aggregate.Days + " 天",
                _fSmall, aggregate.RegionCells > 0 ? Ctext : Csub, new RectangleF(legendX + 70, rowY + 4, Math.Max(50f, legendW - 70f), 20), true);

            List<string> notes = new List<string>();
            if (aggregate.OffRegion > 0) notes.Add("其他屏幕 " + Analysis.FmtCount(aggregate.OffRegion) + " 次");
            if (aggregate.Vacant > 0) notes.Add("屏幕间空隙 " + Analysis.FmtCount(aggregate.Vacant) + " 次");
            if (aggregate.Outside > 0) notes.Add("旧布局位置 " + Analysis.FmtCount(aggregate.Outside) + " 次");
            int noteLines = 0;
            if (notes.Count > 0)
            {
                noteLines = Math.Min(3, notes.Count);
                for (int i = 0; i < noteLines; i++)
                    AppText(g, notes[i], _fSmall, Csub, new RectangleF(legendX, rowY + 24 + i * 16, legendW, 16), false);
            }

            // 颜色图例:标出真实次数范围,不用悬停去猜。图例宽度与标签都跟着右卡宽度走,
            // 不再把 2K 的「1 次 / 中点 / 最大」钉在左卡上或压出省略号。
            float legendY = rowY + 24 + noteLines * 16 + (noteLines > 0 ? 8 : 0);
            float barW = Math.Min(240f, legendW);
            ChannelScale.Bar(g, new RectangleF(legendX, legendY, barW, 8), _clickChannel);
            long legendMax = aggregate.Max;
            AppText(g, legendMax > 0 ? "1 次" : "0 次", _fSmall, Csub, new RectangleF(legendX, legendY + 10, 40, 16), false);
            AppText(g, legendMax > 1 ? Analysis.FmtCount(Math.Max(1, legendMax / 2)) + " 次" : "", _fSmall, Csub,
                new RectangleF(legendX + barW / 2f - 24, legendY + 10, 52, 16), true);
            AppText(g, legendMax > 0 ? Analysis.FmtCount(legendMax) + " 次" : "--", _fSmall, Csub,
                new RectangleF(legendX + barW - 58, legendY + 10, 58, 16), true);
        }

        private MouseActionStats MouseActionData()
        {
            // 悬停重绘不应反复跑 30 天聚合:按「区间 + 每日 Cross 计数」签名缓存。
            List<DayRecord> days = new List<DayRecord>(ClickRangeDays());
            long signature = _clickRange;
            for (int i = 0; i < days.Count; i++)
            {
                DayRecord day = days[i];
                if (day == null) { signature = signature * 31 + 1; continue; }
                signature = signature * 31 + day.Date.Ticks;
                CrossDay c = day.Cross;
                if (c == null) { signature = signature * 31 + 2; continue; }
                signature = signature * 31 + c.Packets;
                signature = signature * 31 + c.CalibratedPackets;
                signature = signature * 31 + c.Flicks;
                signature = signature * 31 + c.Reversals;
                signature = signature * 31 + c.Clicks;
                signature = signature * 31 + c.MoveClicks;
                signature = signature * 31 + BitConverter.DoubleToInt64Bits(c.PathCounts);
                signature = signature * 31 + BitConverter.DoubleToInt64Bits(c.DragCounts);
                for (int d = 0; d < 4; d++)
                    signature = signature * 31 + BitConverter.DoubleToInt64Bits(c.Direction[d]);
            }
            if (_mouseActionCache != null && _mouseActionRange == _clickRange && _mouseActionSignature == signature)
                return _mouseActionCache;
            _mouseActionCache = MouseActionStats.Build(days);
            _mouseActionRange = _clickRange;
            _mouseActionSignature = signature;
            return _mouseActionCache;
        }

        /// <summary>鼠标动作:方向罗盘 + 四项运动特征。和点击热区共用顶部时间范围。</summary>
        private void PaintMouseAction(Graphics g)
        {
            MouseActionStats data = MouseActionData();
            PaintCardBase(g, Cx, 138, Cw, 438f + ElasticH);
            AppText(g, "鼠标动作", _fH2, Ctext, new RectangleF(Cx + 22, 152, 260, 26), false);
            AppText(g, ClickRangeName() + " · 方向与操作特征 · 仅相对鼠标数据", _fSmall, Csub,
                new RectangleF(Cx + 22, 180, 430, 18), false);
            PaintClickViewToggle(g);

            // 样本徽标:版本与过滤规则不再常驻页脚。
            float badgeX = Cx + Cw - 216, badgeY = 150;
            _mouseActionInfoRect = new RectangleF(badgeX, badgeY, 196, 24);
            PointF badgeMouse = MouseContent();
            _mouseActionInfoHover = Hit(_mouseActionInfoRect, badgeMouse);
            using (GraphicsPath pill = RoundedRect(badgeX, badgeY, 196, 24, 12))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(16, Ctext))) g.FillPath(fill, pill);
            AppText(g, data.Packets > 0 ? "样本 " + Analysis.FmtCount(data.Packets) + " 包  ⓘ" : "暂无样本  ⓘ",
                _fAxis, Csub, new RectangleF(badgeX + 10, badgeY + 3, 176, 18), false);
            if (_mouseActionInfoHover)
            {
                string info = "仅 1.0.0 相对鼠标数据 · 排除校准期间，绝对坐标设备不参与。";
                SizeF size = g.MeasureString(info, _fSmall);
                RectangleF box = new RectangleF(Cx + Cw - size.Width - 60, 180, size.Width + 28, 30);
                using (GraphicsPath tip = RoundedRect(box.X, box.Y, box.Width, box.Height, 7))
                {
                    using (SolidBrush fill = new SolidBrush(Ccard2)) g.FillPath(fill, tip);
                    using (Pen edge = new Pen(Cline)) g.DrawPath(edge, tip);
                }
                AppText(g, info, _fSmall, Ctext, new RectangleF(box.X + 8, box.Y + 6, box.Width - 16, 20), false);
            }

            // 3.0:左右按 42% / 58% 切分并随 Cw、卡片高度一起伸缩(旧版写死 440 / 322 与 330 高)。
            float padX = 24f, padTop = 208f;
            float contentW = Math.Max(300f, Cw - padX * 2f);
            float contentBottom = 138f + 438f + ElasticH - 14f;
            float contentH = Math.Max(170f, contentBottom - padTop);
            float columnGap = 18f;
            float leftW = Math.Max(220f, (contentW - columnGap) * 0.42f);
            float rightW = Math.Max(200f, contentW - columnGap - leftW);
            PaintVectorCompass(g, new RectangleF(Cx + padX, padTop, leftW, contentH), data);
            PaintMouseActionCards(g, new RectangleF(Cx + padX + leftW + columnGap, padTop, rightW, contentH), data);
        }

        private void PaintVectorCompass(Graphics g, RectangleF area, MouseActionStats data)
        {
            // 雷达随可用宽高等比放大并垂直居中:半径取「半宽 - 轴标签」与「半高 - 标注」的较小值。
            bool twoNotes = data.CalibratedPackets <= 0 && data.Packets > 0;
            float textH = twoNotes ? 44f : 26f;
            float plotH = Math.Max(120f, area.Height - textH - 8f);
            float cx = area.X + area.Width / 2f;
            float cy = area.Y + plotH / 2f + 6f;
            float radius = Math.Max(40f, Math.Min(area.Width / 2f - 34f, plotH / 2f - 24f));
            PointF c = new PointF(cx, cy);
            using (SolidBrush halo = new SolidBrush(Color.FromArgb(26, Cblue))) g.FillEllipse(halo, cx - radius - 8, cy - radius - 8, radius * 2 + 16, radius * 2 + 16);
            using (Pen outer = new Pen(Color.FromArgb(90, Csub), 1.2f)) g.DrawEllipse(outer, cx - radius, cy - radius, radius * 2, radius * 2);
            using (Pen cross = new Pen(Color.FromArgb(42, Csub), 1f) { DashStyle = DashStyle.Dot })
            {
                g.DrawLine(cross, cx - radius, cy, cx + radius, cy);
                g.DrawLine(cross, cx, cy - radius, cx, cy + radius);
            }
            using (Pen ring = new Pen(Color.FromArgb(30, Csub), 1f))
                for (int i = 1; i <= 4; i++)
                {
                    float r = radius * i / 4f;
                    g.DrawEllipse(ring, cx - r, cy - r, r * 2, r * 2);
                }
            using (SolidBrush ringInk = new SolidBrush(Color.FromArgb(150, Csub)))
                for (int i = 1; i <= 3; i++)
                {
                    float r = radius * i / 4f;
                    g.DrawString((i * 10).ToString(CultureInfo.InvariantCulture) + "%", _fAxis, ringInk,
                        cx + r * 0.68f + 2, cy - r * 0.68f - 12);
                }
            using (SolidBrush center = new SolidBrush(Ctext)) g.FillEllipse(center, cx - 2, cy - 2, 4, 4);

            double maximum = 0.4;
            for (int i = 0; i < 4; i++) maximum = Math.Max(maximum, data.DirectionShare(i));
            float[] angles = { (float)Math.PI, 0f, -(float)Math.PI / 2f, (float)Math.PI / 2f };
            string[] labels = { "←", "→", "↑", "↓" };
            Color[] colors = { Ccyan, Cblue, Ccyan, Cblue };
            for (int i = 0; i < 4; i++)
            {
                double share = data.DirectionShare(i);
                float length = (float)(radius * Math.Min(1.0, share / maximum));
                if (share > 0 && length < 10) length = 10;
                double angle = angles[i];
                PointF direction = new PointF((float)Math.Cos(angle), (float)Math.Sin(angle));
                PointF perpendicular = new PointF(-direction.Y, direction.X);
                PointF tip = new PointF(c.X + direction.X * length, c.Y + direction.Y * length);
                PointF base1 = new PointF(c.X + perpendicular.X * 14, c.Y + perpendicular.Y * 14);
                PointF base2 = new PointF(c.X - perpendicular.X * 14, c.Y - perpendicular.Y * 14);
                PointF shoulder1 = new PointF(tip.X + perpendicular.X * 7, tip.Y + perpendicular.Y * 7);
                PointF shoulder2 = new PointF(tip.X - perpendicular.X * 7, tip.Y - perpendicular.Y * 7);
                using (GraphicsPath petal = new GraphicsPath())
                {
                    petal.AddPolygon(new[] { base1, shoulder1, tip, shoulder2, base2 });
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(125, colors[i]))) g.FillPath(fill, petal);
                    using (Pen edge = new Pen(Color.FromArgb(200, colors[i]), 1f)) g.DrawPath(edge, petal);
                }
                using (SolidBrush dot = new SolidBrush(colors[i])) g.FillEllipse(dot, tip.X - 3, tip.Y - 3, 6, 6);
                string value = (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                float lx = cx, ly = cy;
                if (i == 0) { lx = area.X + 8; ly = cy - 9; }
                else if (i == 1) { lx = area.Right - 58; ly = cy - 9; }
                else if (i == 2) { lx = cx - 24; ly = cy - radius - 28; }
                else { lx = cx - 24; ly = cy + radius + 10; }
                AppText(g, labels[i] + " " + value, _fAxis, colors[i],
                    new RectangleF(lx, ly, 64, 18), false);
            }

            float lastY = cy + radius + 34;
            AppText(g, data.HasData ? data.HorizontalInsight() : "尚无相对鼠标方向样本", _fBody,
                data.HasData ? Cgreen : Csub, new RectangleF(area.X + 20, lastY, area.Width - 40, 22), false);
            if (twoNotes)
                AppText(g, "甩动 / 折返需要已校准 DPI；当前只有原始方向分量。", _fAxis, Csub,
                    new RectangleF(area.X + 20, lastY + 22, area.Width - 40, 16), false);
        }

        private void PaintMouseActionCards(Graphics g, RectangleF area, MouseActionStats data)
        {
            string[] labels = { "快速甩动 (Flicks)", "急转 / 折返", "拖拽移动占比", "瞄准命中率" };
            string[] values =
            {
                data.CalibratedPackets > 0 ? Analysis.FmtCount(data.Flicks) + " 次" : data.Packets > 0 ? "需 DPI" : "--",
                data.CalibratedPackets > 0 ? Analysis.FmtCount(data.Reversals) + " 次" : data.Packets > 0 ? "需 DPI" : "--",
                MouseActionStats.Percent(data.DragShare),
                MouseActionStats.Percent(data.MoveClickShare)
            };
            string[] notes = { "瞬时高速位移", "方向突变 > 120°", "按住按键时的位移", "位移后紧跟点击" };
            // 3.0:2×2 网格直接铺满右侧整块可用区域,不再写死 120 高 + 垂直居中。
            float gap = 14f;
            float cardW = (area.Width - gap) / 2f;
            float cardH = (area.Height - gap) / 2f;
            for (int i = 0; i < 4; i++)
            {
                float x = area.X + (i % 2) * (cardW + gap);
                float y = area.Y + (i / 2) * (cardH + gap);
                PaintCardBase(g, x, y, cardW, cardH);
                AppText(g, labels[i], _fAxis, Csub, new RectangleF(x + 14, y + 16, cardW - 28, 18), false);
                // 数值落在卡片纵向 45% 处,备注贴底:卡片再高也不会出现「三行挤在顶端」的空框。
                float valueY = y + Math.Max(40f, (cardH - 28f) * 0.45f);
                // 卡片越高,数值字号越往上抬一档(_fNum 29px),避免大卡片里只挂三个小字。
                Font valueFont = cardH >= 250f ? _fNum : _fH2;
                AppText(g, values[i], valueFont, i < 2 && data.CalibratedPackets <= 0 ? Csub : Ctext,
                    new RectangleF(x + 14, valueY, cardW - 28, 34), false);
                AppText(g, notes[i], _fSmall, Csub, new RectangleF(x + 14, y + cardH - 30, cardW - 28, 18), false);
            }
        }


        private void PaintClickTray(Graphics g, ClickHeat.Aggregate aggregate, int screenIndex, Screen[] screens)
        {
            int width = Math.Max(1, (int)Math.Ceiling(_clickTray.Width * _s));
            int height = Math.Max(1, (int)Math.Ceiling(_clickTray.Height * _s));
            string stamp = _clickRange + "|" + Store.ThemeId + "|" + _s.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                + width + "x" + height + "|" + aggregate.Total + "|" + aggregate.Max + "|" + aggregate.Cells.Count + "|"
                + (_clickFocused ? "F" : "A") + screenIndex + "|" + (_clickSmooth ? "S" : "G") + "|" + screens.Length + "|" + ClickChannelUi.StampSegment(_clickChannel) + "|"
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

            // 极简显示器外框:先描边框与支架,再画玻璃面板,保证描边落在底纹之下。
            for (int i = 0; i < screens.Length; i++)
            {
                if (_clickFocused && i != screenIndex) continue;
                DrawMonitorFrame(g, projection.ToContent(screens[i].Bounds), i == screenIndex);
            }

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
                Color fill = ChannelScale.For(_clickChannel, pair.Value, aggregate.Max);
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

            bool legacyPill = ClickChannelUi.ShouldShowLegacyPill(_clickChannel, aggregate.HasLegacyUnclassified);
            if (legacyPill) DrawLegacyPill(g);
            if (ClickChannelUi.ShouldShowEmpty(_clickChannel, aggregate.HasLegacyUnclassified, aggregate.Total))
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
        /// <summary>
        /// 历史数据提示胶囊:切到单键视图、且区间内存在未区分按键的历史点击时显示。
        /// 优先级高于「还没有点击位置数据」——先说清历史数据的边界,再说当前视图为空。
        /// 半透明、小字号、居中靠上,不抢热力图本身的注意力。
        /// </summary>
        private void DrawLegacyPill(Graphics g)
        {
            if (!ClickChannelUi.ShouldShowLegacyPill(_clickChannel, true)) return;
            string text = ClickChannelUi.LegacyPillText;
            using (Font font = new Font("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF size = g.MeasureString(text, font);
                float width = Math.Min(_clickTray.Width - 24, size.Width + 26);
                float height = 26;
                RectangleF pill = new RectangleF(
                    _clickTray.X + (_clickTray.Width - width) / 2,
                    _clickTray.Y + 10, width, height);
                using (GraphicsPath path = RoundedRect(pill.X, pill.Y, pill.Width, pill.Height, height / 2))
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(52, ArtTheme.Current.Card))) g.FillPath(fill, path);
                    using (Pen border = new Pen(Color.FromArgb(90, ArtTheme.Mix(Cline, Ctext, 0.22))))
                        g.DrawPath(border, path);
                }
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(225, Csub)))
                using (StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                }) g.DrawString(text, font, ink, pill, format);
            }
        }

        /// <summary>
        /// 极简显示器拟物外框(参照 ClickHeat.DrawIcon 的比例):
        /// 细描边框 + 双立柱 + 底座横线。宽屏/竖屏/笔记本比例自适应;
        /// pane 高度 &lt; 48px 时省略底座,避免缩略图上糊成一团。
        /// 颜色全部来自主题派生(Mix(Cline, Ctext, 0.22)),13 套主题下都可见。
        /// </summary>
        private void DrawMonitorFrame(Graphics g, RectangleF pane, bool selected)
        {
            if (pane.Width < 24 || pane.Height < 18) return;
            RectangleF limit = new RectangleF(_clickTray.X + 4, _clickTray.Y + 4,
                _clickTray.Width - 8, _clickTray.Height - 8);
            float left = Math.Max(limit.X, pane.X - 3);
            float top = Math.Max(limit.Y, pane.Y - 3);
            float right = Math.Min(limit.Right, pane.Right + 3);
            float bottom = Math.Min(limit.Bottom, pane.Bottom + 3);
            if (right - left < 12 || bottom - top < 10) return;

            Color ink = ArtTheme.Mix(Cline, Ctext, 0.22);
            if (selected) ink = ArtTheme.Mix(ink, Cblue, 0.35);
            float thickness = selected ? 1.6f : 1.2f;
            RectangleF bezel = new RectangleF(left, top, right - left, bottom - top);
            using (GraphicsPath path = RoundedRect(bezel.X, bezel.Y, bezel.Width, bezel.Height, 4))
            using (Pen pen = new Pen(Color.FromArgb(selected ? 195 : 125, ink), thickness))
                g.DrawPath(pen, path);

            if (pane.Height < 48) return;
            float standH = Math.Max(4f, Math.Min(10f, pane.Height * 0.06f));
            float standY = bezel.Bottom + standH;
            if (standY > limit.Bottom) return;
            using (Pen pen = new Pen(Color.FromArgb(selected ? 175 : 110, ink), Math.Max(1f, thickness - 0.2f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                float midX = bezel.X + bezel.Width / 2;
                float span = Math.Max(6f, bezel.Width * 0.13f);
                g.DrawLine(pen, midX - span / 2, bezel.Bottom, midX - span / 2, standY);
                g.DrawLine(pen, midX + span / 2, bezel.Bottom, midX + span / 2, standY);
                g.DrawLine(pen, midX - span * 1.5f, standY, midX + span * 1.5f, standY);
            }
        }

        /// <summary>鼠标位置对应的格子(内容坐标)。返回 false 表示没有踩到有数据的格子。</summary>
        private bool HitClickCell(ClickHeat.Aggregate aggregate, out int key, out int cellX, out int cellY)
        {
            key = 0;
            cellX = cellY = 0;
            PointF point = ToBase(_mouse);
            point = ToContent(point);
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
            PointF contentPoint = ToContent(basePoint);
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
        private void ExportMouseAction()
        {
            try
            {
                MouseActionStats data = MouseActionData();
                StringBuilder csv = new StringBuilder();
                csv.AppendLine("# 键鼠统计 · 鼠标动作");
                csv.AppendLine("# 区间," + ClickRangeName());
                csv.AppendLine("# 记录天数," + data.Days);
                csv.AppendLine("# 样本包," + data.Packets);
                csv.AppendLine("# 已校准包," + data.CalibratedPackets);
                csv.AppendLine("方向,占比");
                string[] names = { "向左", "向右", "向上", "向下" };
                for (int i = 0; i < 4; i++)
                    csv.Append(names[i]).Append(',').Append(data.DirectionShare(i).ToString("0.######", CultureInfo.InvariantCulture)).AppendLine();
                csv.AppendLine("指标,数值,说明");
                csv.AppendLine("快速甩动 (Flicks)," + data.Flicks + ",瞬时高速位移");
                csv.AppendLine("急转 / 折返," + data.Reversals + ",方向突变 > 120°");
                csv.AppendLine("拖拽移动占比," + data.DragShare.ToString("0.######", CultureInfo.InvariantCulture) + ",按住按键时的位移");
                csv.AppendLine("瞄准命中率," + data.MoveClickShare.ToString("0.######", CultureInfo.InvariantCulture) + ",位移停止后紧跟点击");
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "鼠标动作_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
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
