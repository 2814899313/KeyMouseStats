// ============================================================================
//  键鼠统计 - 详细数据分析面板  Dashboard.cs
//  六个页面:总览 / 趋势 / 时段分布 / 按键排行 / 应用统计 / 使用洞察,全部 GDI+ 自绘。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    /// <summary>格式化与分析查询辅助。</summary>
    internal static class Analysis
    {
        public static string FmtCount(long n)
        {
            if (n < 10000) return n.ToString("N0", CultureInfo.InvariantCulture);
            double v = n;
            if (v < 1e8) return (v / 1e4).ToString("0.#", CultureInfo.InvariantCulture) + "万";
            return (v / 1e8).ToString("0.##", CultureInfo.InvariantCulture) + "亿";
        }

        public static string FmtDistance(double meters)
        {
            if (!MouseDistance.ValidDpi(Store.MouseDpi)) return "未设置 DPI";
            return FmtMeters(meters);
        }

        /// <summary>存储值为米，显示自动选择厘米、米、公里。</summary>
        public static string FmtMeters(double m)
        {
            if (m > 0 && m < 1) return (m * 100).ToString("0.##", CultureInfo.InvariantCulture) + " cm";
            if (m < 1000) return m.ToString("0.#", CultureInfo.InvariantCulture) + " m";
            if (m < 100000) return (m / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " km";
            return (m / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>坐标轴用短数字:1234 → 1.2k</summary>
        public static string FmtAxis(double v)
        {
            if (v >= 1e6) return (v / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M";
            if (v >= 1000) return (v / 1000).ToString("0.##", CultureInfo.InvariantCulture) + "k";
            return v.ToString("0", CultureInfo.InvariantCulture);
        }

        public static string FmtHours(double h)
        {
            if (h < 1) return (h * 60).ToString("0", CultureInfo.InvariantCulture) + " 分钟";
            return h.ToString("0.#", CultureInfo.InvariantCulture) + " 小时";
        }

        public static DayRecord GetDay(DateTime d)
        {
            DayRecord r;
            Store.History.TryGetValue(d.Date, out r);
            return r;
        }

        /// <summary>
        /// 趋势页的「有效数据日」判据:当天有条目,且不是全 0 的空壳。
        /// 灰带、主折线断线、区间日均与 7 日移动均值必需共用这一处 —— 以前灰带/折线只看
        /// ContainsKey、7 日均值只看 IsEmpty,于是"内存里有键但全 0"的日子会同时得到
        /// 「实线砸到 0」和「虚线说没数据」两种矛盾结论。
        /// ActivityTime 会按需给任意日期建档,所以空壳日在内存里是真实存在的。
        /// </summary>
        public static bool HasRecord(DateTime d)
        {
            DayRecord r = GetDay(d);
            return r != null && !r.IsEmpty;
        }

        /// <summary>最近 n 天(含今天,缺失的天补零)。</summary>
        public static List<DayRecord> RangeDays(int n)
        {
            List<DayRecord> list = new List<DayRecord>(n);
            for (int i = n - 1; i >= 0; i--)
            {
                DateTime d = DateTime.Today.AddDays(-i);
                DayRecord r = GetDay(d);
                if (r == null)
                {
                    r = new DayRecord();
                    r.Date = d;
                }
                list.Add(r);
            }
            return list;
        }

        public static long SumKeys(IEnumerable<DayRecord> rs)
        {
            long s = 0; foreach (DayRecord r in rs) s += r.Keys; return s;
        }
        public static long SumClicks(IEnumerable<DayRecord> rs)
        {
            long s = 0; foreach (DayRecord r in rs) s += r.Clicks; return s;
        }
        public static long SumWheel(IEnumerable<DayRecord> rs)
        {
            long s = 0; foreach (DayRecord r in rs) s += r.Wheel; return s;
        }
        public static double SumMove(IEnumerable<DayRecord> rs)
        {
            double s = 0; foreach (DayRecord r in rs) s += r.MoveMeters; return s;
        }

        public static long[] SumHourKeys(IEnumerable<DayRecord> rs)
        {
            long[] a = new long[24];
            foreach (DayRecord r in rs) for (int i = 0; i < 24; i++) a[i] += r.HourKeys[i];
            return a;
        }

        public static long[] SumHourClicks(IEnumerable<DayRecord> rs)
        {
            long[] a = new long[24];
            foreach (DayRecord r in rs) for (int i = 0; i < 24; i++) a[i] += r.HourClicks[i];
            return a;
        }

        /// <summary>按显示名聚合的按键统计(左右 Shift/Ctrl/Alt 合并)。</summary>
        public static List<KeyValuePair<string, long>> KeyRanking(IEnumerable<DayRecord> rs)
        {
            Dictionary<string, long> byName = new Dictionary<string, long>();
            foreach (DayRecord r in rs)
            {
                foreach (KeyValuePair<int, long> kv in r.KeyCounts)
                {
                    string name = KeyName(kv.Key);
                    long old;
                    byName.TryGetValue(name, out old);
                    byName[name] = old + kv.Value;
                }
            }
            List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>(byName);
            list.Sort(ByValueDescending);
            return list;
        }

        /// <summary>降序比较器。提成静态字段,避免每次 KeyRanking 都 new 一个 Comparison 闭包。</summary>
        private static readonly Comparison<KeyValuePair<string, long>> ByValueDescending =
            delegate (KeyValuePair<string, long> a, KeyValuePair<string, long> b) { return b.Value.CompareTo(a.Value); };

        public static long KeyTotal(IEnumerable<DayRecord> rs)
        {
            long total = 0;
            foreach (DayRecord r in rs)
                foreach (KeyValuePair<int, long> kv in r.KeyCounts)
                    total += kv.Value;
            return total;
        }

        public static string KeyName(int vk)
        {
            switch (vk)
            {
                case 0x08: return "退格";
                case 0x09: return "Tab";
                case 0x0D: return "回车";
                case 0x13: return "Pause";
                case 0x14: return "大写锁定";
                case 0x1B: return "Esc";
                case 0x20: return "空格";
                case 0x21: return "PgUp";
                case 0x22: return "PgDn";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x2C: return "PrtSc";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x5B: return "Win";
                case 0x5C: return "Win";
                case 0x5D: return "菜单键";
                case 0x6A: return "小键盘*";
                case 0x6B: return "小键盘+";
                case 0x6D: return "小键盘-";
                case 0x6E: return "小键盘.";
                case 0x6F: return "小键盘/";
                case 0x90: return "NumLock";
                case 0x91: return "ScrollLock";
                case 0xA0: case 0x10: return "Shift";
                case 0xA1: return "Shift";
                case 0xA2: case 0x11: return "Ctrl";
                case 0xA3: return "Ctrl";
                case 0xA4: case 0x12: return "Alt";
                case 0xA5: return "Alt";
                case 0xBA: return ";";
                case 0xBB: return "=";
                case 0xBC: return ",";
                case 0xBD: return "-";
                case 0xBE: return ".";
                case 0xBF: return "/";
                case 0xC0: return "";
                case 0xDB: return "[";
                case 0xDC: return "\\";
                case 0xDD: return "]";
                case 0xDE: return "'";
            }
            if (vk >= 0x30 && vk <= 0x39) return ((char)('0' + vk - 0x30)).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)('A' + vk - 0x41)).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "小键盘" + (vk - 0x60).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F).ToString();
            return "0x" + vk.ToString("X2");
        }
    }

    internal sealed partial class Dashboard : Form
    {
        // ---- 配色 ----
        private static Color Cbg { get { return ArtTheme.Current.Background; } }
        private static Color Ccard { get { return ArtTheme.Current.Card; } }
        private static Color Ccard2 { get { return ArtTheme.Current.Raised; } }
        private static Color Cline { get { return ArtTheme.Current.Line; } }
        private static Color Ctext { get { return ArtTheme.Current.Text; } }
        private static Color Csub { get { return ArtTheme.Current.Muted; } }
        private static Color Cblue { get { return ArtTheme.Current.Accent; } }
        private static Color Cgreen { get { return ArtTheme.Current.Green; } }
        private static Color Corange { get { return ArtTheme.Current.Orange; } }
        private static Color Cpurple { get { return ArtTheme.Current.Purple; } }
        private static Color Cred { get { return ArtTheme.Current.Red; } }
        private static Color Ccyan { get { return ArtTheme.Current.Cyan; } }

        // ---- 布局(96dpi 基准) ----
        private const int BW = 1072, BH = 724;
        private const int ContentX = 176, ContentY = 64;
        private const int TabY = 52, TabH = 40;
        private const int Cx = 24, Cw = 832, Cy = 98, Cb = 586;

        private enum TabId { Overview = 0, Trend = 1, Hours = 2, Keys = 3, Apps = 4, Insights = 5, Text = 6, Day = 7, Heat = 8 }
        private readonly string[] _tabNames = { "总览", "趋势", "时段分布", "按键排行", "应用统计", "使用洞察", "字符内容", "日报", "点击热区" };

        private TabId _tab = TabId.Overview;
        private int _trendMetric = 0;     // 0击键 1点击 2滚轮 3移动
        private int _trendRange = 14;     // 14 / 30
        private bool _trendCompare;
        private int _hourMetric = 0;      // 0 击键 1 点击 2 活跃时长(柱子/矩阵/连续段共用)
        private int _hourView;             // 0 时段柱子 1 周×24 矩阵 2 连续段强度
        private int _hourRange = 2;        // 0 今日 1 近7天 2 全部历史
        private RectangleF _activityRect, _sessionsRect;
        /// <summary>1.8.0:总览页八张数字卡的位置,用于点击下钻到对应报告页。</summary>
        private readonly List<RectangleF> _overviewTiles = new List<RectangleF>();
        /// <summary>1.8.0:总览卡片 → 报告页(0 输入习惯 / 3 使用节奏 / 8 鼠标动作 / 11 区间回顾)。</summary>
        private static readonly int[] OverviewTileReport = { 0, 0, 0, 2, 0, -1, 0, 1 };
        private ContextMenuStrip _keyboardMenu, _categoryMenu, _distributionMenu;
        private ContextMenuStrip PreparePopup(ref ContextMenuStrip slot)
        {
            if(slot==null || slot.IsDisposed){slot=new ContextMenuStrip();NikkiArt.Menu(slot);}
            while(slot.Items.Count>0){ToolStripItem item=slot.Items[0];slot.Items.RemoveAt(0);item.Dispose();}
            return slot;
        }
        private bool _overviewKeyboard;
        private int _distributionPeriod;
        private int _distributionTop=10;
        /// <summary>总览「按键分布」卡上的 7 个胶囊。以前靠 OnMouseClick 里手写的 if 链分发、
        /// 不进 _chips,键盘 Tab 永远摸不到;现在统一走 chip id。</summary>
        private const int DistributionMouseChipId = 303;
        private const int DistributionKeyboardChipId = 304;
        private const int DistributionTopChipId = 305;
        private const int DistributionPeriodChipId = 306;   // 306..309 = 今日/本周/本月/本年
        /// <summary>图例行 / 扇区的下钻 chip id 基数:350+i,i = 扇区下标(最后一个是「其他」)。</summary>
        private const int DistributionSliceChipId = 350;
        /// <summary>页脚「导出 CSV」的 chip id(47 在取值表里是空档,44/45/46 已被报告类入口占用)。</summary>
        internal const int ExportChipId = 47;
        /// <summary>步进/翻页类按钮的统一高度与圆角,与 ToggleChip 对齐(收敛方案 §8.2)。</summary>
        internal const float StepChipHeight = 30;
        internal const float StepChipRadius = 8;
        /// <summary>独立 ToggleChip 的统一高度与圆角。全站不再出现 24/25/27/28 高的小方块。</summary>
        internal const float ToggleChipHeight = 30;
        internal const float ToggleChipRadius = 8;

        // ---- 分布卡的悬停 / 下钻状态 -------------------------------------------
        /// <summary>当前悬停的扇区下标(-1 = 没有)。鼠标模式是 0..3,键盘模式是 0..N(含「其他」)。</summary>
        private int _distributionHoverSlice = -1;
        /// <summary>上一帧画出来的环几何与数据,供 OnMouseMove 做命中判断(避免在移动时重算排行)。</summary>
        private RectangleF _distributionRing;
        private float _distributionRingStroke;
        private long[] _distributionRingValues;
        private string[] _distributionRingNames;
        /// <summary>图例行的可点击矩形,按扇区下标对齐;同时登记进 _chips 供键盘 Tab 使用。</summary>
        private readonly List<int> _distributionLegendSlices = new List<int>();
        private readonly List<RectangleF> _distributionLegendRects = new List<RectangleF>();
        /// <summary>从分布卡下钻到「按键排行」时要高亮的按键显示名(空 = 不高亮)。</summary>
        private string _selectedKeyName = "";

        // ---- 按键排行的分帧缓存 -------------------------------------------------
        private sealed class RankCache
        {
            public DateTime From = DateTime.MaxValue;
            public int Days = -1;
            public long Live = -1;
            public List<KeyValuePair<string, long>> List;
        }
        private readonly RankCache[] _rankCaches = { new RankCache(), new RankCache() };
        private RectangleF _distributionCountRect;
        private readonly RectangleF[] _distributionPeriods=new RectangleF[4];
        private RectangleF _distributionMouseRect, _distributionKeyboardRect;
        private int _keyRange = 0;        // 0今日 1近90天 2近7天 3近30天
        private int _keyView;             // 0 按键排行 1 组合键排行 2 全键热力图
        private int _keyMetric;           // 0 击键次数 1 按住时长(列表与热力图共用)
        private int _hover = -1;          // 图表悬停索引
        /// <summary>连续段视图正在看的记录日。MinValue = 跟随最新记录日;翻页后固定在所选日(仅内存状态,不落盘)。</summary>
        private DateTime _hourRecordDate = DateTime.MinValue;
        /// <summary>连续段柱块的可命中矩形(内容坐标,按段下标排列),供 ComputeHover 定位到具体某一段。</summary>
        private readonly List<RectangleF> _segmentBars = new List<RectangleF>();

        private readonly RectangleF[] _tabRects = new RectangleF[9];
        private readonly List<RectangleF> _chips = new List<RectangleF>();
        private readonly List<int> _chipIds = new List<int>();
        private bool _appKeyGroups;
        private RectangleF _closeRect, _exportRect;

        private float _s = 1f;
        private float _dpiScale = 1f;
        private bool _fittingScreen;
        private Screen _lastScreen;
        private Font _fTitle, _fTab, _fH2, _fBody, _fSmall, _fAxis, _fNum, _fNum2, _fChip, _fKey;
        private System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _hoverPaintTimer = new System.Windows.Forms.Timer();
        private Point _mouse;
        // 1.8.0:动效状态。_paintedTab 用来发现换页,_lastFrameMs 用来给下一帧定间隔。
        private TabId _paintedTab;
        private double _lastFrameMs;
        private const string PageMotionKey = "page";
        /// <summary>1.8.0:下钻面包屑(非空时显示在标题行,并按 Esc / Backspace 返回上一层)。</summary>
        private string _crumb = "";
        /// <summary>1.8.0:键盘导航——当前用 Tab 选中的 chip(在 _chips 里的下标,-1 表示没有)。</summary>
        private int _chipFocus = -1;
        /// <summary>1.8.0:页脚短暂状态提示与它的过期时间。</summary>
        private string _status = "";
        private string _dailyReportPath = "";
        private Control _detailVisual;
        private string _detailTitle = "";
        private Bitmap _detailCache;
        private DateTime _detailCacheAt;
        private bool _detailLive;
        private int _detailContentHeight;
        private DateTime _statusUntil;
        /// <summary>1.8.0:最近一次绘制时悬停元素的明细文字,Ctrl+C 优先复制它。</summary>
        internal string _hoverDetail = "";

        /// <summary>进入下一层:记录面包屑文案。</summary>
        private void PushCrumb(string label)
        {
            _crumb = label == null ? "" : label;
            Invalidate();
        }
        /// <summary>返回上一层;返回 true 表示吃掉这次按键。</summary>
        private bool PopCrumb()
        {
            if (_selectedKeyScan >= 0 && ClearSelectedKey()) { _crumb = ""; return true; }
            bool hadKey = _selectedKeyName.Length > 0;
            _selectedKeyName = "";
            if (_crumb.Length == 0 && !hadKey) return false;
            _crumb = "";
            Invalidate();
            return true;
        }

        /// <summary>1.8.0:最近一次绘制时的主题,用来发现换主题并触发交叉淡化。</summary>
        private int _paintedTheme = -1;
        private Color _themeScrim = Color.Empty;

        /// <summary>内容区(不含页脚)的客户区像素矩形:动效只重绘这一块。</summary>
        private Rectangle ContentRegionPx()
        {
            int x = (int)(ContentX * _s), y = (int)(ContentY * _s);
            int w = Math.Max(1, (int)((BW - ContentX) * _s));
            int h = Math.Max(1, (int)((680 - ContentY) * _s));
            return new Rectangle(x, y, w, h);
        }

        /// <summary>动效的硬停规则:最小化、锁屏/断开/休眠、以及正按着键时都不推进。</summary>
        private void HeartbeatMotion()
        {
            Motion.Paused = WindowState == FormWindowState.Minimized
                || ActivityMonitor.Busy
                || HoldTracker.PendingCount > 0;
        }

        /// <summary>1.8.0:图表入场生长。换页与换筛选都会重开一次;无动效时 Entrance() 恒为 1。</summary>
        private const string EnterMotionKey = "enter";
        private double Entrance()
        {
            return Motion.Progress(EnterMotionKey);
        }
        private void RestartEntrance()
        {
            if (Visible) Motion.Start(EnterMotionKey, 280, Ease.OutCubic);
        }

        /// <summary>请求下一帧:间隔按上一帧实测耗时决定(夹在 16–40 ms,见 Motion.FrameInterval)。</summary>
        private void ScheduleFrame()
        {
            if (!Visible || WindowState == FormWindowState.Minimized || !IsHandleCreated) return;
            _hoverPaintTimer.Interval = Motion.FrameInterval(_lastFrameMs);
            if (!_hoverPaintTimer.Enabled) _hoverPaintTimer.Start();
        }

        public Dashboard()
        {
            Text = BuildFlavor.ProductName + " " + BuildInfo.Version + " - 数据分析";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            DoubleBuffered = true;
            BackColor = Cbg;
            KeyPreview = true;
            Icon = StatsWidget.MakeIcon();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate
            {
                if (!Visible || WindowState == FormWindowState.Minimized) return;
                FitToCurrentScreen();
                if (_detailVisual != null)
                {
                    if (_detailLive) Invalidate();
                    return;
                }
                Invalidate();
            };
            _hoverPaintTimer.Interval=40;
            // 1.8.0:同一个定时器兼顾悬停重绘与动效帧;动效结束后完全停表(见 Motion.cs)。
            _hoverPaintTimer.Tick+=delegate
            {
                _hoverPaintTimer.Stop();
                if(!Visible)return;
                HeartbeatMotion();
                if(Motion.Active || Motion.AnyChaseMoving(0.4))
                {
                    Rectangle region=Motion.InvalidateRegion(ContentRegionPx());
                    if(region.IsEmpty)Invalidate(ContentRegionPx()); else Invalidate(region);
                    _hoverPaintTimer.Interval=Motion.FrameInterval(_lastFrameMs);
                    _hoverPaintTimer.Start();
                }
                else
                {
                    Motion.Sweep();
                    Invalidate();
                }
            };
            _loadingTimer.Tick+=delegate
            {
                if(!Visible || WindowState==FormWindowState.Minimized)return;
                int revision=System.Threading.Volatile.Read(ref ThemeImages.Revision);
                if(revision!=_imageRevision){_imageRevision=revision;Invalidate();}
                else if(_loaderVisible)
                {
                    if(!ThemeImages.Loading){_loaderVisible=false;Invalidate();}
                    else {_loadingFrame++;Invalidate(Rectangle.Round(new RectangleF((200)*_s,(BH-42)*_s,270*_s,32*_s)));}
                }
            };
        }

        private float S(float v) { return v * _s; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                using (Graphics g = CreateGraphics())
                {
                    _dpiScale = Math.Max(1f, g.DpiX / 96f);
                }
            }
            catch { }
            FitToCurrentScreen();
            _fTitle = new Font("Microsoft YaHei UI", 25f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fTab = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fH2 = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fBody = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fAxis = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fNum = new Font("Segoe UI", 29f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fNum2 = new Font("Segoe UI", 19f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fChip = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fKey = new Font("Cascadia Mono", 12f, FontStyle.Regular, GraphicsUnit.Pixel);

            _timer.Start();

            // 「每日首次打开面板」:把昨天的日报生成并落盘。一天只做一次,记在数据文件里。
            TryGenerateDailyReport();
        }

        /// <summary>跨屏拖动时按当前显示器工作区重新适配,避免固定 1072×724 面板在小屏/不同 DPI 上被裁掉。</summary>
        private void FitToCurrentScreen()
        {
            if (_fittingScreen || !IsHandleCreated) return;
            Screen screen = Screen.FromControl(this);
            if (screen == null) return;
            Rectangle work = screen.WorkingArea;
            if (work.Width < 200 || work.Height < 200) return;
            float fit = Math.Min(_dpiScale, Math.Min((work.Width - 24f) / BW, (work.Height - 24f) / BH));
            fit = Math.Max(0.5f, fit);
            int targetW = (int)Math.Round(BW * fit);
            int targetH = (int)Math.Round(BH * fit);
            bool screenChanged = !ReferenceEquals(screen, _lastScreen);
            bool sizeChanged = ClientSize.Width != targetW || ClientSize.Height != targetH;
            _fittingScreen = true;
            try
            {
                _lastScreen = screen;
                if (sizeChanged)
                {
                    _s = fit;
                    ClientSize = new Size(targetW, targetH);
                }
                if (screenChanged || sizeChanged || Right > work.Right || Bottom > work.Bottom || Left < work.Left || Top < work.Top)
                {
                    int x = Math.Max(work.Left, Math.Min(Location.X, work.Right - Width));
                    int y = Math.Max(work.Top, Math.Min(Location.Y, work.Bottom - Height));
                    if (x != Location.X || y != Location.Y) Location = new Point(x, y);
                }
                ApplyWindowRegion();
            }
            catch { }
            finally { _fittingScreen = false; }
        }

        private void ApplyWindowRegion()
        {
            try
            {
                using (GraphicsPath p = StatsWidget.RoundedPath(
                    new Rectangle(Point.Empty, ClientSize), (int)Math.Round(14 * _s)))
                    Region = new Region(p);
            }
            catch { }
        }

        private void OpenDetail(Control visual, string title, bool live = false, int contentHeight = 0)
        {
            if (_detailVisual != null) _detailVisual.Dispose();
            if (_detailCache != null) { _detailCache.Dispose(); _detailCache = null; }
            _detailVisual = visual;
            _detailTitle = title ?? "";
            _detailLive = live;
            _detailContentHeight = contentHeight;
            _detailCacheAt = DateTime.MinValue;
            Invalidate();
        }

        private void CloseDetail()
        {
            if (_detailVisual != null) { _detailVisual.Dispose(); _detailVisual = null; }
            if (_detailCache != null) { _detailCache.Dispose(); _detailCache = null; }
            _detailLive = false;
            _detailContentHeight = 0;
            Invalidate();
        }

        private void OpenTrendDistribution()
        {
            // 分布口径只含完整日(RangeRules.TryNormalize 会把 end=今天 截到昨天),
            // 所以标题必须用同一个 CompleteEnd,否则会出现「标题写到 11.15、内容只有 11.14」。
            DateTime end = RangeRules.CompleteEnd(DateTime.Today);
            DateTime start = DateTime.Today.AddDays(-(_trendRange - 1));
            if (end < start) end = start;      // 今天刚过零点时还没有完整日,退化成单日窗口
            OpenDetail(new DistributionReportVisual(start, end), "分布 · " + start.ToString("MM.dd") + "–" + end.ToString("MM.dd"), false, 430);
        }

        private void OpenHoldTrend()
        {
            int[] spans = { 1, 90, 7, 30 };
            int span = _keyRange >= 0 && _keyRange < spans.Length ? spans[_keyRange] : 1;
            DateTime end = DateTime.Today, start = span <= 1 ? DateTime.Today : DateTime.Today.AddDays(-(span - 1));
            OpenDetail(new HoldTrendVisual(start, end), "按住时长趋势", false, 400);
        }

        private void OpenMouseAction()
        {
            if (_detailVisual != null) CloseDetail();
            _tab = TabId.Heat;
            _clickActionView = true;
            _clickRange = 0;
            _mouseActionCache = null;
            _mouseActionRange = -1;
            _hover = -1;
            Invalidate();
        }

        private void OpenOverviewDetail(int tile)
        {
            if (tile == 7) { OpenDetail(new ReportVisual(DateTime.Today, 4), "实时状态", true, 560); return; }
            if (tile == 3) { OpenMouseAction(); return; }
            if (tile == 5) { SetStatus("连续使用段请在「使用洞察 → 会话时间轴」查看"); return; }
            OpenDetail(new ReportVisual(DateTime.Today, 0), "输入画像 · 今日", false, 500);
        }

        private void PaintDetailOverlay(Graphics g)
        {
            if (_detailVisual == null) return;
            RectangleF card = new RectangleF(Cx, 94, Cw, 558);
            using (SolidBrush scrim = new SolidBrush(Color.FromArgb(110, Cbg)))
                g.FillRectangle(scrim, 0, 0, BW, BH);
            PaintCard(g, card.X, card.Y, card.Width, card.Height, _detailTitle);
            AppChip(g, 900, card.Right - 72, card.Y + 10, 58, "关闭", false);
            RectangleF area = new RectangleF(card.X + 16, card.Y + 42, card.Width - 32, card.Height - 56);
            float pixelScale = Math.Max(1f, _s);
            float designHeight = _detailContentHeight > 0 ? _detailContentHeight : area.Height;
            float fit = Math.Min(1f, area.Height / Math.Max(1f, designHeight));
            float renderScale = pixelScale * fit;
            int cw = Math.Max(1, (int)Math.Round(area.Width * pixelScale));
            int ch = Math.Max(1, (int)Math.Round(designHeight * renderScale));
            bool stale = _detailCache == null || _detailCache.Width != cw || _detailCache.Height != ch
                || (_detailLive && (DateTime.UtcNow - _detailCacheAt).TotalMilliseconds > 1200);
            if (stale)
            {
                if (_detailCache != null) _detailCache.Dispose();
                _detailCache = new Bitmap(cw, ch);
                _detailVisual.Size = new Size(Math.Max(1, (int)Math.Round(area.Width / fit * renderScale)), ch);
                using (Graphics scratch = Graphics.FromImage(_detailCache))
                {
                    scratch.SmoothingMode = SmoothingMode.AntiAlias;
                    scratch.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    RenderDetailVisual(_detailVisual, scratch, renderScale);
                }
                _detailCacheAt = DateTime.UtcNow;
            }
            float drawHeight = designHeight * fit;
            RectangleF drawn = new RectangleF(area.X, area.Y + Math.Max(0, (area.Height - drawHeight) / 2), area.Width, drawHeight);
            GraphicsState imageState = g.Save();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_detailCache, drawn);
            g.Restore(imageState);
        }

        private static void RenderDetailVisual(Control visual, Graphics g, float scale)
        {
            ReportVisual report = visual as ReportVisual;
            if (report != null) { report.RenderTo(g, scale); return; }
            CrossReportVisual cross = visual as CrossReportVisual;
            if (cross != null) { cross.RenderTo(g, scale); return; }
            DistributionReportVisual distribution = visual as DistributionReportVisual;
            if (distribution != null) { distribution.RenderTo(g, scale); return; }
            HoldTrendVisual hold = visual as HoldTrendVisual;
            if (hold != null) { hold.RenderTo(g, scale); return; }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            FitToCurrentScreen();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // 1.8.0:Esc / Backspace 先返回上一层(下钻),没有上一层时才关闭面板。
            if (e.KeyCode == Keys.Escape) { if (_detailVisual != null) { CloseDetail(); return; } if (PopCrumb()) return; Close(); return; }
            if (e.KeyCode == Keys.Back) { PopCrumb(); return; }
            // 1.8.0:Tab / Shift+Tab 在本页的 chip 之间移动,Enter / 空格激活。
            if (e.KeyCode == Keys.Tab)
            {
                int count = Math.Min(_chips.Count, _chipIds.Count);
                if (count == 0) return;
                _chipFocus = e.Shift ? (_chipFocus <= 0 ? count - 1 : _chipFocus - 1) : (_chipFocus + 1) % count;
                Invalidate();
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                if (ActivateFocusedChip()) { e.Handled = true; e.SuppressKeyPress = true; }
                return;
            }
            // 1.8.0:左右方向键翻页。
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            {
                int next = (int)_tab + (e.KeyCode == Keys.Right ? 1 : -1);
                if (next < 0) next = _tabNames.Length - 1;
                if (next >= _tabNames.Length) next = 0;
                _tab = (TabId)next; _hover = -1; _chipFocus = -1; _hourlyMenuOpen = false;
                _selectedKeyName = ""; _crumb = ""; _distributionHoverSlice = -1;
                Invalidate();
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            // 1.8.0:Ctrl+C 复制悬停明细(没有悬停就复制本页提示)。
            if (e.Control && e.KeyCode == Keys.C) { CopyText(); e.Handled = true; e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.D1) { _tab = TabId.Overview; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D2) { _tab = TabId.Trend; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D3) { _tab = TabId.Hours; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D4) { _tab = TabId.Keys; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D5) { _tab = TabId.Apps; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D6) { _tab = TabId.Insights; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D7) { _tab = TabId.Text; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D8) { _tab = TabId.Day; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D9) { _tab = TabId.Heat; _hover = -1; Invalidate(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _loadingTimer.Stop();_loadingTimer.Dispose();
            base.OnFormClosed(e);
            foreach(ContextMenuStrip menu in new[]{_keyboardMenu,_categoryMenu,_distributionMenu,_textRefreshMenu})if(menu!=null)menu.Dispose();
            _timer.Stop();
            _timer.Dispose();
            _hoverPaintTimer.Stop();_hoverPaintTimer.Dispose();
            if(_keyboardPlate!=null){_keyboardPlate.Dispose();_keyboardPlate=null;}
            if(_keyboardPlatePrev!=null){_keyboardPlatePrev.Dispose();_keyboardPlatePrev=null;}
            if(_clickPlate!=null){_clickPlate.Dispose();_clickPlate=null;}
            if(_clickScreenMenu!=null){_clickScreenMenu.Dispose();_clickScreenMenu=null;}
            foreach (Font font in new Font[] { _fTitle, _fTab, _fH2, _fBody, _fSmall, _fAxis, _fNum, _fNum2, _fChip, _fKey, _fChar })
                if (font != null) font.Dispose();
        }

        // ---------------------------------------------------------------- 拖动/关闭

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                PointF p = ToBase(e.Location);
                if (p.Y <= TabY && !Hit(_closeRect, p))   // 标题栏区域拖动(避开关闭按钮)
                {
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
            }
        }

        private PointF ToBase(Point p)
        {
            return new PointF(p.X / _s, p.Y / _s);
        }

        private bool Hit(RectangleF r, PointF p) { return r.Contains(p); }

        // ---------------------------------------------------------------- 与统计报告互跳

        /// <summary>当前页对应的报告页。</summary>
        private int ReportTabForPage()
        {
            switch (_tab)
            {
                case TabId.Trend: return 3;      // 分布
                case TabId.Hours: return 3;     // 分布
                case TabId.Keys: return 4;      // 按住时长趋势
                default: return 0;              // 总览 / 洞察 → 输入习惯
            }
        }
        /// <summary>当前页筛选对应的日期区间,带给报告作为自定义区间。</summary>
        private void CurrentRange(out DateTime start, out DateTime end)
        {
            end = DateTime.Today;
            switch (_tab)
            {
                case TabId.Keys:
                    int[] days = { 1, 90, 7, 30 };
                    int span = _keyRange >= 0 && _keyRange < days.Length ? days[_keyRange] : 1;
                    start = span <= 1 ? DateTime.Today : DateTime.Today.AddDays(-(span - 1));
                    break;
                case TabId.Apps:
                    start = _appDays <= 1 ? DateTime.Today : DateTime.Today.AddDays(-(_appDays - 1));
                    break;
                case TabId.Hours:
                    start = HourRangeStart();
                    break;
                case TabId.Trend:
                    if (_trendHourly) { start = end = _hourlyTrendDay; }
                    else start = DateTime.Today.AddDays(-(_trendRange - 1));   // 与趋势页 Analysis.RangeDays(_trendRange) 同口径:含今天、共 _trendRange 天
                    break;
                case TabId.Insights:
                    start = end = _insightDay;
                    break;
                default:
                    start = DateTime.Today;
                    break;
            }
        }
        private void OpenReport()
        {
            DateTime start, end;
            CurrentRange(out start, out end);
            using (StatisticsReport report = new StatisticsReport(end, ReportTabForPage(), start, end)) report.ShowDialog(this);
        }
        /// <summary>报告里点「在面板中查看」时调用:切到使用洞察的会话时间轴并定位到那一天。</summary>
        internal void FocusFromReport(DateTime day)
        {
            _insightDay = day.Date > DateTime.Today ? DateTime.Today : day.Date;
            _tab = TabId.Insights; _insightView = 1; _sessionPage = 0;
            Invalidate();
            Activate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            PointF p = ToBase(e.Location);
            if (Hit(_closeRect, p)) { Close(); return; }
            if (_hourlyMenuOpen)
            {
                bool menuHit = false;
                for (int i = 0; i < _chips.Count && i < _chipIds.Count; i++)
                    if (_chipIds[i] >= 326 && _chipIds[i] <= 333 && Hit(_chips[i], new PointF(p.X - ContentX, p.Y - ContentY))) { menuHit = true; break; }
                if (!menuHit) { _hourlyMenuOpen = false; Invalidate();
                    bool side = false;
                    foreach (RectangleF r in _tabRects) if (Hit(r, p)) { side = true; break; }
                    if (!side) return;
                }
            }
            if (_detailVisual != null)
            {
                for (int i = 0; i < _chips.Count && i < _chipIds.Count; i++)
                    if (_chipIds[i] == 900 && Hit(_chips[i], new PointF(p.X - ContentX, p.Y - ContentY))) { CloseDetail(); return; }
                bool sidebar = false;
                foreach (RectangleF r in _tabRects) if (Hit(r, p)) { sidebar = true; break; }
                if (sidebar) CloseDetail();
                else return;
            }

            if (Hit(_closeRect, p)) { Close(); return; }
            if (HandleThemeClick(p)) { Invalidate(); return; }
            if (Hit(_exportRect, p)) { ExportCsv(); return; }
            PointF contentPoint = new PointF(p.X - ContentX, p.Y - ContentY);
            // 分布卡:点圆环上的任一段 → 下钻到「按键排行」并高亮那个键。
            // 图例行走下面的 _chips 统一循环(它们登记成 350+i)。
            if (_tab == TabId.Overview && _overviewKeyboard)
            {
                int slice;
                if (HitDistributionRing(contentPoint, out slice)) { DrillIntoKeys(slice); return; }
            }
            // 总览「按键分布」卡上的 7 个胶囊不再在这里手写 if 链分发 —— 它们已经登记进 _chips,
            // 由下面的统一循环走 ApplyChip。这样 Tab / Enter 也能到达,且命中判断只有一处。
            if (_tab == TabId.Overview && Hit(_activityRect, contentPoint))
            { using (ActivitySettings dialog = new ActivitySettings()) dialog.ShowDialog(this); Invalidate(); return; }
            if (_tab == TabId.Overview && Hit(_sessionsRect, contentPoint))
            { using (SessionDetails dialog = new SessionDetails()) dialog.ShowDialog(this); return; }
            // 1.8.0:点总览的数字卡 → 打开对应报告页(今天)。
            if (_tab == TabId.Overview && e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < _overviewTiles.Count; i++)
                {
                    if (!Hit(_overviewTiles[i], contentPoint)) continue;
                    OpenOverviewDetail(i);
                    return;
                }
            }
            for (int i = 0; i < _tabNames.Length; i++)
            {
                if (Hit(_tabRects[i], p)) { _tab = (TabId)i; _hover = -1; _chipFocus = -1; _selectedKeyName = ""; _crumb = ""; _distributionHoverSlice = -1; Invalidate(); return; }
            }
            if (_tab == TabId.Apps && HandleAppClick(new PointF(p.X - ContentX, p.Y - ContentY))) return;
            if (_tab == TabId.Insights && HandleInsightClick(contentPoint)) { Invalidate(); return; }
            for (int i = 0; i < _chips.Count; i++)
            {
                if (Hit(_chips[i], new PointF(p.X - ContentX, p.Y - ContentY)))
                {
                    ApplyChip(_chipIds[i]);
                    RestartEntrance();          // 1.8.0:换筛选后图表重新入场
                    Invalidate();
                    return;
                }
            }
            if(_tab==TabId.Trend && !_trendHourly)
            {
                _mouse=e.Location;
                int index=ComputeHover();
                List<AnalysisPt> points=TrendPoints();
                if(index>=0 && index<points.Count)
                {
                    if(!points[index].HasData) { SetStatus(points[index].Day.ToString("MM-dd")+" 没有记录（应用或电脑未运行）"); return; }
                    OpenDetail(new ReportVisual(points[index].Day, 1), points[index].Day.ToString("MM-dd") + " 当日节奏", false, 300);
                }
            }
            // 1.8.0:在键盘热力图上点某个键即下钻到该键;点空白处返回全部按键。
            if(_tab==TabId.Keys && _keyView == 2 && e.Button==MouseButtons.Left)
            {
                PointF content=new PointF(e.X/_s-ContentX,e.Y/_s-ContentY);
                int scan;
                if(HitKeyboardKey(content,out scan)) { SelectKey(scan); PushCrumb(SelectedKeyLabel()); }
                else if(PopCrumb()) return;
            }
        }

        private void ApplyChip(int id)
        {
            if (id == 45) { OpenReport(); return; }   // 在报告中查看(带当前筛选)
            if (id == 44) { OpenHoldTrend(); return; }
            if (id == 302) { OpenTrendDistribution(); return; }
            if (id == ExportChipId) { ExportCsv(); return; }   // 页脚「导出 CSV」
            if (id >= DistributionMouseChipId && id <= DistributionPeriodChipId + 3) { ApplyDistributionChip(id); return; }
            if (id == 518) { _tab = TabId.Heat; _clickActionView = true; _clickRange = 0; _mouseActionCache = null; _mouseActionRange = -1; return; }
            if (id == 519) { _tab = TabId.Heat; _clickActionView = false; return; }
            if (id == 520) { _tab = TabId.Heat; _clickActionView = true; return; }
            if (id == 900) { CloseDetail(); return; }
            if (id == 46)
            {
                try { if (File.Exists(_dailyReportPath)) System.Diagnostics.Process.Start(_dailyReportPath); }
                catch { }
                return;
            }
            if (id == 425)
            {
                try
                {
                    Directory.CreateDirectory(DailyReport.Directory);
                    System.Diagnostics.Process.Start("explorer.exe", DailyReport.Directory);
                }
                catch { }
                return;
            }
            if (id == 205) { _hourView = 2; _hover = -1; return; }
            if (id == 93) { _trendCompare = !_trendCompare; return; } // 区间对比
            if (id >= 510 && id <= 512) { _clickRange = id - 510; return; }   // 点击热区:今日 / 近 7 天 / 近 30 天
            if (id >= 514 && id <= 516)                                      // 点击热区:跨屏全览 / 选屏
            {
                _clickFocused = id != 515;
                return;
            }
            if (id == 517) { _clickSmooth = !_clickSmooth; _clickStamp = null; return; }   // 像素网格 / 平滑热力
            if (id == 518) { _tab = TabId.Heat; _clickActionView = true; _clickRange = 0; _mouseActionCache = null; _mouseActionRange = -1; return; }
            if (id == 519) { _tab = TabId.Heat; _clickActionView = false; return; }
            if (id == 520) { _tab = TabId.Heat; _clickActionView = true; return; }
            if (id >= 530 && id <= 539) { _clickScreen = id - 530; _clickFocused = true; return; }
            if (id >= 420 && id <= 429) { ApplyDayChip(id); return; }
            if (_tab == TabId.Hours && (id == 203 || id == 204)) { _hourView = id - 203; _hover = -1; return; }
            if (id >= 400) { ApplyTextChip(id); return; }
            if (id >= 300) { ApplyHourlyTrendChip(id); return; }
            if (id >= 200) { ApplyInsightChip(id); return; }
            if (_tab == TabId.Keys && (id == 76 || id == 77)) { _keyRange=id-74;return; }
            if (_tab == TabId.Keys && id == 75) { KeyboardMenu(); return; }
            if (_tab == TabId.Keys && id >= 72 && id <= 74) { _keyView = id - 72; _hover = -1; return; }
            if (_tab == TabId.Keys && (id == 78 || id == 79)) { _keyMetric = id - 78; return; }
            if (_tab == TabId.Hours && id >= 30 && id <= 32) { _hourRange = id - 30; _hover = -1; return; }
            // 连续段是单日时间轴:范围胶囊换成了日期切换器(33 上一日 / 34 下一日 / 35 回到最新记录日),
            // 必须放在 "id < 70 → ApplyAppChip" 之前认领。
            if (_tab == TabId.Hours && _hourView == 2 && id >= 33 && id <= 35)
            {
                StepHourRecordDay(id == 33 ? -1 : id == 34 ? 1 : 0);
                return;
            }
            if (id < 70) { ApplyAppChip(id); return; }
            if (id >= 100) { _trendMetric = id - 100; return; }        // 100..103
            // 换区间会换掉点数与日期,旧悬停索引必须清掉,否则会指到另一天。
            if (id >= 90) { _trendRange = (id == 91) ? 14 : 30; _hover = -1; return; } // 91=14天 92=30天
            if (id >= 80) { _hourMetric = id - 80; _hover = -1; return; }   // 80/81
            if (id >= 70) { _keyRange = id - 70; return; }             // 70/71
        }

        /// <summary>
        /// 总览「按键分布」卡上的胶囊:鼠标 / 键盘 / Top N ▾ / 今日·本周·本月·本年。
        /// 抽出来是为了让鼠标点击和键盘 Enter 走同一条路(以前只有 OnMouseClick 里的手写 if)。
        /// </summary>
        private void ApplyDistributionChip(int id)
        {
            if (id == DistributionMouseChipId) { _overviewKeyboard = false; _distributionHoverSlice = -1; Invalidate(); return; }
            if (id == DistributionKeyboardChipId) { _overviewKeyboard = true; _distributionHoverSlice = -1; Invalidate(); return; }
            if (id == DistributionTopChipId) { ShowDistributionTopMenu(); return; }
            if (id >= DistributionSliceChipId && id < DistributionSliceChipId + 32) { DrillIntoKeys(id - DistributionSliceChipId); return; }
            int period = id - DistributionPeriodChipId;
            if (period >= 0 && period < _distributionPeriods.Length)
            {
                _distributionPeriod = period;
                _distributionHoverSlice = -1;
                Invalidate();
            }
        }

        /// <summary>
        /// 从分布卡下钻到「按键排行」。slice 是扇区下标;等于 Top N 时代表「其他」,
        /// 只切页不高亮任何单个键(用户想看的是完整榜单)。
        /// </summary>
        internal void DrillIntoKeys(int slice)
        {
            if (!_overviewKeyboard || _distributionRingNames == null) return;
            _selectedKeyName = slice >= 0 && slice < _distributionTop && slice < _distributionRingNames.Length
                ? (_distributionRingNames[slice] ?? "") : "";
            _selectedKeyScan = -1;          // 下钻来自排行,不是全键热力图,别让旧的热力选中残留
            _tab = TabId.Keys;
            _keyView = 0;                   // 按键排行
            _hover = -1;
            _chipFocus = -1;
            PushCrumb(_selectedKeyName.Length > 0 ? "按键排行 · " + _selectedKeyName : "");
            Motion.Start(PageMotionKey, 150, Ease.OutCubic);
            RestartEntrance();
            Invalidate();
        }

        /// <summary>Top N 下拉:菜单贴着按钮底边弹出,而不是跟着上一次鼠标坐标跑。</summary>
        private void ShowDistributionTopMenu()
        {
            ContextMenuStrip choices = PreparePopup(ref _distributionMenu);
            foreach (int count in new[] { 5, 8, 10 })
            {
                int selected = count;
                ToolStripMenuItem item = new ToolStripMenuItem("前 " + count + " 个按键");
                item.Checked = _distributionTop == count;
                item.Click += delegate { _distributionTop = selected; _distributionHoverSlice = -1; Invalidate(); };
                choices.Items.Add(item);
            }
            Point anchor = new Point((int)Math.Round(_distributionCountRect.X + ContentX),
                (int)Math.Round(_distributionCountRect.Bottom + ContentY));
            choices.Show(this, anchor);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point previousMouse=_mouse;
            _mouse = e.Location;
            int h = ComputeHover();
            _hover = h;
            PointF p = ToBase(_mouse);
            // 字符内容页的悬停靠自己登记的元素表(图表柱、排行行),与趋势/时段页的索引式悬停不同。
            if (_tab == TabId.Text) UpdateTextHover(true);
            if (_tab == TabId.Heat && _clickActionView)
            {
                bool infoHover = Hit(_mouseActionInfoRect, new PointF(p.X - ContentX, p.Y - ContentY));
                if (infoHover != _mouseActionInfoHover) { _mouseActionInfoHover = infoHover; Invalidate(); }
            }
            bool clickable = Hit(_closeRect, p) || Hit(_exportRect, p) || (_tab==TabId.Trend&&!_trendHourly&&h>=0);
            if (_tab == TabId.Overview)
            {
                PointF overviewPoint = new PointF(p.X - ContentX, p.Y - ContentY);
                clickable |= Hit(_activityRect, overviewPoint) || Hit(_sessionsRect, overviewPoint);
                // 分布卡:扇区与图例行都是可点的,鼠标移上去要有手型。
                UpdateDistributionHover(overviewPoint);
                int slice;
                if (_overviewKeyboard) clickable |= HitDistributionRing(overviewPoint, out slice);
            }
            // 分布卡的鼠标/键盘/Top N/时段胶囊已经登记进 _chips,下面统一的 foreach 会覆盖,
            // 不再在这里重复写一遍命中判断(重复的副本迟早会和真实控件走偏)。
            if (_tab == TabId.Overview)
                foreach (RectangleF r in _overviewTiles) clickable |= Hit(r, new PointF(p.X - ContentX, p.Y - ContentY));
            if (_tab == TabId.Keys && _keyView == 2)
            {
                PointF content = new PointF(p.X - ContentX, p.Y - ContentY);
                for (int i = 0; i < _keyboardHoverRects.Count; i++) clickable |= Hit(_keyboardHoverRects[i], content);
            }
            foreach (RectangleF r in _tabRects) clickable |= Hit(r, p);
            foreach (RectangleF r in _themeRects) clickable |= Hit(r, p);
            foreach (RectangleF r in _chips) clickable |= Hit(r, new PointF(p.X - ContentX, p.Y - ContentY));
            if (_tab == TabId.Apps)
            {
                PointF appPoint = new PointF(p.X - ContentX, p.Y - ContentY);
                foreach (RectangleF r in _appEditRects) clickable |= Hit(r, appPoint);
                foreach (RectangleF r in _appCompareRects) clickable |= Hit(r, appPoint);
                foreach (RectangleF r in _appCategoryRects) clickable |= Hit(r, appPoint);
            }
            if (_tab == TabId.Insights)
                foreach (RectangleF r in _insightDates.Keys) clickable |= Hit(r, new PointF(p.X - ContentX, p.Y - ContentY));
            Cursor = clickable ? Cursors.Hand : Cursors.Default;
            if(_tab==TabId.Keys && _keyView == 2)
            {
                PointF old=ToBase(previousMouse),next=ToBase(_mouse);
                old.X-=ContentX;old.Y-=ContentY;next.X-=ContentX;next.Y-=ContentY;
                foreach(RectangleF key in _keyboardHoverRects)if(key.Contains(old)&&key.Contains(next))return;
            }
            if(!_hoverPaintTimer.Enabled)_hoverPaintTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouse = new Point(-100, -100);
            _hover = -1;
            _distributionHoverSlice = -1;
            _mouseActionInfoHover = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        private int ComputeHover()
        {
            PointF p = ToBase(_mouse);
            p = new PointF(p.X - ContentX, p.Y - ContentY);
            if (_tab == TabId.Trend)
            {
                RectangleF r = new RectangleF(Cx, 150, Cw, 360);
                if (r.Contains(p))
                {
                    List<AnalysisPt> pts = TrendPoints();
                    if (pts.Count == 0) return -1;
                    float left = Cx + 56, right = Cx + Cw - 16;
                    float step = pts.Count > 1 ? (right - left) / (pts.Count - 1) : 0;
                    int idx = step > 0 ? (int)Math.Round((p.X - left) / step) : 0;
                    if (idx >= 0 && idx < pts.Count) return idx;
                }
            }
            else if (_tab == TabId.Hours)
            {
                // 连续段:柱块代表"段"而不是"小时",按上一帧登记的可命中矩形定位到具体那一段。
                if (_hourView == 2)
                {
                    for (int i = _segmentBars.Count - 1; i >= 0; i--)
                        if (_segmentBars[i].Contains(p)) return i;
                    return -1;
                }
                RectangleF r = new RectangleF(Cx, 188, Cw, 312);
                if (r.Contains(p))
                {
                    int idx = (int)((p.X - Cx - 50) / ((Cw - 50 - 16) / 24f));
                    if (idx >= 0 && idx < 24) return idx;
                }
            }
            return -1;
        }

        internal sealed class AnalysisPt
        {
            public DateTime Day;
            public double V;
            /// <summary>这天有没有可用记录。false = 缺失或空壳日:折线断开并用灰带标出。</summary>
            public bool HasData;
        }

        private List<AnalysisPt> TrendPoints()
        {
            List<DayRecord> rs = Analysis.RangeDays(_trendRange);
            List<AnalysisPt> pts = new List<AnalysisPt>(rs.Count);
            foreach (DayRecord r in rs)
            {
                AnalysisPt p = new AnalysisPt();
                p.Day = r.Date;
                // 与 Analysis.HasRecord / PersonalStats.History 同一判据:有条目且非全 0 空壳。
                // 只判 ContainsKey 会把空壳日当成"有数据",主折线在那里连到 0 形成假深 V。
                p.HasData = Store.History.ContainsKey(r.Date) && !r.IsEmpty;
                switch (_trendMetric)
                {
                    case 0: p.V = r.Keys; break;
                    case 1: p.V = r.Clicks; break;
                    case 2: p.V = r.Wheel; break;
                    default: p.V = r.MoveMeters; break;
                }
                pts.Add(p);
            }
            return pts;
        }

        /// <summary>
        /// 区间日均:只统计「完整且有有效数据」的天(排除今天、排除缺失/空壳日)。
        /// 没有样本时返回 NaN,由调用方显示 -- 或不画平均线 —— 卡片与平均线以前各写一套兜底,
        /// 只有今天有数据时会给出两个互相矛盾的假均值。
        /// </summary>
        internal static double TrendAverage(List<AnalysisPt> pts, bool live, out int count)
        {
            double sum = 0;
            count = 0;
            if (pts == null) return double.NaN;
            for (int i = 0; i < pts.Count; i++)
            {
                if (!pts[i].HasData) continue;
                if (live && i == pts.Count - 1) continue;
                sum += pts[i].V;
                count++;
            }
            return count > 0 ? sum / count : double.NaN;
        }

        /// <summary>
        /// 主折线段是否应该画:两端都必须有有效数据;今天的最后一段留给虚线,不算实线段。
        /// 抽成纯函数是为了让「无记录日必须断线」有可回归的断言(见 tests/StatisticsTests.cs)。
        /// </summary>
        internal static bool TrendSegmentDrawn(bool leftValid, bool rightValid, bool live, int rightIndex, int count)
        {
            if (!leftValid || !rightValid) return false;
            if (live && rightIndex == count - 1) return false;
            return true;
        }

        // ---------------------------------------------------------------- 绘制总入口

        protected override void OnPaint(PaintEventArgs e)
        {
            if(!_loadingTimer.Enabled && Visible)_loadingTimer.Start();
            Rectangle loader=Rectangle.Round(new RectangleF((200)*_s,(BH-42)*_s,270*_s,32*_s));
            if(_loaderVisible && loader.Contains(e.ClipRectangle))
            {e.Graphics.ScaleTransform(_s,_s);PaintLoading(e.Graphics);return;}
            System.Diagnostics.Stopwatch renderWatch=System.Diagnostics.Stopwatch.StartNew();
            Graphics g = e.Graphics;
            // 1.8.0:离屏渲染(未显示、测试截图)时不做动效,画面保持确定性。
            Motion.Live = Visible;
            ThemeFadeTick();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(_s, _s);
            g.Clear(Cbg);
            PaintThemeTexture(g);

            _chips.Clear();
            _chipIds.Clear();
            PaintHeader(g);
            PaintTabs(g);
            PaintThemePicker(g);

            GraphicsState contentState = g.Save();
            // 1.8.0:换页时内容淡入(一层背景色薄幕渐隐)。刻意不做位移:内容一旦位移,
            // 命中测试用的 chip 矩形就和眼睛看到的位置差最多十几毫秒的偏移,快速点击会点错。
            if (_tab != _paintedTab)
            {
                _paintedTab = _tab;
                _crumb = "";                       // 换页即离开原来那一层
                _selectedKeyScan = -1;
                if (Visible) { Motion.Start(PageMotionKey, 150, Ease.OutCubic); RestartEntrance(); }   // 没显示就不做动效
            }
            double pageProgress = Motion.Progress(PageMotionKey);
            g.TranslateTransform(ContentX, ContentY);
            if (_detailVisual == null)
            {
                switch (_tab)
                {
                    case TabId.Overview: PaintOverview(g); break;
                    case TabId.Trend: PaintTrend(g); break;
                    case TabId.Hours: PaintHours(g); break;
                    case TabId.Keys: PaintKeys(g); break;
                    case TabId.Apps: PaintApps(g); break;
                    case TabId.Text: PaintText(g); break;
                    case TabId.Day: PaintDay(g); break;
                    case TabId.Heat: PaintClickHeat(g); break;
                    default: PaintInsights(g); break;
                }
            }
            PaintDetailOverlay(g);
            g.Restore(contentState);
            if (pageProgress < 1)
            {
                int alpha = (int)(150 * (1 - pageProgress));
                if (alpha > 0)
                {
                    Rectangle region = ContentRegionPx();
                    using (SolidBrush scrim = new SolidBrush(Color.FromArgb(alpha, Cbg)))
                        g.FillRectangle(scrim, region.X / _s, region.Y / _s, region.Width / _s, region.Height / _s);
                }
            }
            _loaderVisible=ThemeImages.Loading;
            if (_detailVisual == null) PaintFooter(g);
            // 焦点环必须画在页脚之后:页脚按钮的命中区是在 PaintFooter 里才登记的,而 _chips 每帧
            // 会在 PaintHeader 之前清空。旧顺序(页面绘制完就画笔)会让焦点落在页脚按钮时,
            // 环画到上一个 chip 上、或因为下标越界干脆不画。
            PaintFocusRing(g);
            if(_loaderVisible)PaintLoading(g);
            PaintThemeFade(g);   // 1.8.0:换主题时最后叠一层旧底色,做成一次交叉淡化
            renderWatch.Stop();
            _lastFrameMs = renderWatch.Elapsed.TotalMilliseconds;
            Motion.RecordFrame(_lastFrameMs);
            // 悬停重绘沿用原有节奏;动效(通道或数值滚动)在跑时按实测帧耗时定下一帧。
            if (Motion.Active || Motion.AnyChaseMoving(0.4)) ScheduleFrame();
            else _hoverPaintTimer.Interval=Math.Max(40,Math.Min(150,(int)renderWatch.ElapsedMilliseconds*2));
        }
        private readonly Timer _loadingTimer=new Timer { Interval=80 };
        private int _imageRevision, _loadingFrame;
        private bool _loaderVisible;
        private void PaintLoading(Graphics g)
        {
            float x=200,y=BH-42;
            using(SolidBrush background=new SolidBrush(Ccard))g.FillRectangle(background,x,y,270,32);
            string label=GujianArt.Active?"正在引动琴心剑魄":OriArt.Active?"正在唤醒尼博尔的灵光":EuroTruckArt.Active?"正在检查车辆与规划路线":WuxiaArt.Active?"正在展开江湖绘卷":BalatroArt.Active?"正在洗牌与准备牌桌":MinecraftArt.Active?"正在构建方块世界":ResidentArt.Active?"正在读取幸存者档案":HaloArt.Active?"正在连接战术终端":NikkiArt.Active?"正在搭配奇迹衣橱":"正在准备界面";
            using(SolidBrush ink=new SolidBrush(Csub))g.DrawString(label,_fSmall,ink,x+(GujianArt.Active||OriArt.Active?80:44),y+8);
            if(GujianArt.Active)GujianArt.Loading(g,x+4,y+2,_loadingFrame);
            else if(OriArt.Active)OriArt.Loading(g,x+4,y+2,_loadingFrame);
            else for(int i=0;i<8;i++)
            {
                int alpha=55+((i+_loadingFrame)%8)*25;
                using(SolidBrush brush=new SolidBrush(Color.FromArgb(alpha,Cblue)))
                {
                    double angle=i*Math.PI/4;float px=x+19+(float)Math.Cos(angle)*10,py=y+16+(float)Math.Sin(angle)*10;
                    if(EuroTruckArt.Active){using(Pen road=new Pen(Color.FromArgb(alpha,Corange),1.4f)){g.DrawLine(road,px-3,py+2,px+3,py+2);g.DrawEllipse(road,px-3,py-2,6,4);}}
                    else if(WuxiaArt.Active){using(Pen petal=new Pen(Color.FromArgb(alpha,Corange),1.2f)){g.DrawArc(petal,px-3,py-2,4,6,210,210);g.DrawArc(petal,px,py-2,4,6,120,210);}}
                    else if(MinecraftArt.Active)g.FillRectangle(brush,px-2,py-2,5,5);
                    else if(NikkiArt.Active)NikkiArt.Star(g,px,py,3,Color.FromArgb(alpha,Cpurple));
                    else g.FillEllipse(brush,px-2,py-2,4,4);
                }
            }
            if(EuroTruckArt.Active)using(Pen lane=new Pen(Color.FromArgb(180,Corange),1.2f)){lane.DashStyle=DashStyle.Dash;g.DrawLine(lane,x+211,y+17,x+258,y+17);}
            if(ResidentArt.Active)using(Pen pulse=new Pen(Cred,1.4f))g.DrawLines(pulse,new[]{new PointF(x+215,y+17),new PointF(x+224,y+17),new PointF(x+229,y+9),new PointF(x+234,y+23),new PointF(x+240,y+17),new PointF(x+258,y+17)});
            if(HaloArt.Active)using(Pen radar=new Pen(Ccyan,1.2f))
            {g.DrawArc(radar,x+7,y+4,24,24,(_loadingFrame*24)%360,110);g.DrawLine(radar,x+19,y+16,x+28,y+16);}
        }

        private void PaintHeader(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(ArtTheme.Current.Sidebar))
                g.FillRectangle(b, 0, 0, ContentX, BH);
            if(NikkiArt.Active)
            {
                NikkiArt.Star(g,151,87,7,Cpurple);
                NikkiArt.Star(g,146,565,4,Corange);
            }
            using (Pen pen = new Pen(Cline)) g.DrawLine(pen, ContentX, 0, ContentX, BH);
            using (GraphicsPath mark = RoundedRect(20, 28, 32, 32, 10))
            using (SolidBrush b = new SolidBrush(Cblue)) g.FillPath(b, mark);
            if(ThemeArt.Dark)ThemeArt.Sticker(g,0,new RectangleF(23,31,26,26));
            else if(NikkiArt.Active) NikkiArt.Star(g,36,44,11,ArtTheme.Current.OnAccent);
            else DrawGlyph(g, 0, 28, 36, ArtTheme.Current.OnAccent);
            using (SolidBrush b = new SolidBrush(Ctext)) g.DrawString(BuildFlavor.ProductName, _fH2, b, 62, 28);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(GujianArt.Active?"琴心剑魄 · 山海行记":OriArt.Active?"SPIRIT FOREST ARCHIVE":EuroTruckArt.Active?"LONG HAUL LOG":WuxiaArt.Active?"江湖行迹":BalatroArt.Active?"JOKER ACTIVITY":MinecraftArt.Active?"BLOCK ACTIVITY":ResidentArt.Active?"SURVIVOR ARCHIVE":ThemeArt.Dark?"SPARTAN ACTIVITY":"ACTIVITY MONITOR", _fAxis, b, 62, 48);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString("数据分析", _fSmall, b, 24, 113);

            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString((GujianArt.Active?"山海剑境  /  ":OriArt.Active?"灵光森林  /  ":EuroTruckArt.Active?"长途之路  /  ":WuxiaArt.Active?"碧血丹心  /  ":BalatroArt.Active?"幻彩牌桌  /  ":MinecraftArt.Active?"方块世界  /  ":ResidentArt.Active?"幸存者档案  /  ":ThemeArt.Dark?"战术终端  /  ":"工作空间  /  ") + _tabNames[(int)_tab] + (_crumb.Length>0?"  /  "+_crumb+"   ·   Esc 返回":""), _fSmall, b, 200, 24);
            using (SolidBrush b = new SolidBrush(Ctext)) g.DrawString(_tabNames[(int)_tab], _fTitle, b, 198, 61);
            if(_tab!=TabId.Text && _tab!=TabId.Heat && _tab!=TabId.Day && (_tab!=TabId.Insights || _insightView!=1 || _insightDay.Date==DateTime.Today))InteractionBadge.Draw(g,new RectangleF(336,74,112,23),_fSmall,"● 今日累积中");
            string[] descriptions = {
                "每一次输入，都有迹可循。查看今天的活动与使用习惯。",
                "沿着时间轴，发现每天的节奏与变化。",
                "找到最活跃的时刻，了解一天中的输入分布。",
                "从常用按键中，发现你的操作习惯。",
                "这些输入用在了哪里？按应用与窗口，拆解你的每一次操作。",
                "从每天的使用节奏，发现活跃时刻、连续使用段与休息间隔。",
                "记录输入的节奏，发现常用的文字。数据来自本机 Rime 日志。",
                "把昨天收成一张卡片：数字、时段、应用、文字，还有几句不太客气的点评。",
                "手指最常落在屏幕的哪一块？位置按 32 像素网格本地记录，不截屏。" };
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(descriptions[(int)_tab], _fBody, b, 200, 104);
            GraphicsState clockState=g.Save();if(ResidentArt.Active)g.TranslateTransform(-398,-52);
            using (GraphicsPath p = RoundedRect(828, 72, 204, 42, 12))
            using (SolidBrush b = new SolidBrush(Ccard)) g.FillPath(b, p);
            using (SolidBrush b = new SolidBrush(Cgreen)) g.FillEllipse(b, 842, 88, 6, 6);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(DateTime.Now.ToString("yyyy.MM.dd   HH:mm:ss"), _fBody, b, 858, 82);
            g.Restore(clockState);
            if(NikkiArt.Active)
            {
                int[] emotes={1,2,3,0,4,5,2,0,4};
                int emote=_tab==TabId.Insights&&_insightView==1?3:emotes[(int)_tab];
                NikkiArt.Sticker(g,emote,new RectangleF(716,7,110,110));
            }
            if(ThemeArt.Dark)
            {
                RectangleF emblem=ResidentArt.Active?new RectangleF(646,6,64,64):new RectangleF(742,4,78,78);
                if(_tab==TabId.Heat) ClickHeat.DrawIcon(g,emblem,Ccyan,3f);
                else ThemeArt.Symbol(g,NavSymbolFor((int)_tab),emblem);
                AppText(g,PageLabelFor((int)_tab),_fSmall,GujianArt.Active?Corange:OriArt.Active?Ccyan:EuroTruckArt.Active?Corange:WuxiaArt.Active?Corange:Ccyan,ResidentArt.Active?new RectangleF(452,64,180,22):new RectangleF(646,78,180,22),true);
            }
            _closeRect = new RectangleF(BW - 48, 16, 28, 28);
            bool hov = Hit(_closeRect, ToBase(_mouse));
            using (GraphicsPath p = RoundedRect(_closeRect.X, _closeRect.Y, 28, 28, 8))
            using (SolidBrush b = new SolidBrush(hov ? Color.FromArgb(75, Cred) : Ccard)) g.FillPath(b, p);
            using (Pen pen = new Pen(hov ? Ctext : Csub, 1.5f))
            {
                float cx = _closeRect.X + 14, cy = _closeRect.Y + 14;
                g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                g.DrawLine(pen, cx - 4, cy + 4, cx + 4, cy - 4);
            }
        }

        private void PaintTabs(Graphics g)
        {
            for (int i = 0; i < _tabNames.Length; i++)
            {
                // 2.0.7 扩到 9 页:行距 36、行高 32,给左下的主题选择器留出位置。
                RectangleF r = new RectangleF(14, 146 + i * 36, 148, 32);
                _tabRects[i] = r;
                bool sel = (int)_tab == i;
                bool hov = Hit(r, ToBase(_mouse));
                if (sel || hov)
                    using (GraphicsPath p = RoundedRect(r.X, r.Y, r.Width, r.Height, 9))
                    using (SolidBrush b = new SolidBrush(sel ? ArtTheme.Mix(Ccard, Cblue, 0.18) : Ccard)) g.FillPath(b, p);
                Color color = sel ? Cblue : hov ? Ctext : Csub;
                if (i == 8) ClickHeat.DrawIcon(g, new RectangleF(23, r.Y + 4, 26, 26), color, 1.5f);
                else if (ThemeArt.Dark) ThemeArt.Symbol(g, NavSymbolFor(i), new RectangleF(24, r.Y + 4, 26, 26));
                else DrawGlyph(g, i, 28, r.Y + 9, color);
                using (SolidBrush b = new SolidBrush(color)) g.DrawString(_tabNames[i], _fTab, b, 56, r.Y + 7);
                if (sel)
                    using (SolidBrush b = new SolidBrush(Cblue)) g.FillRectangle(b, 14, r.Y + 9, 3, 14);
            }
            using (Pen p = new Pen(Cline)) g.DrawLine(p, 24, BH - 142, 152, BH - 142);
            using (SolidBrush b = new SolidBrush(Csub))
            {
                g.DrawString("1—9 / ← →   切换页面", _fAxis, b, 24, BH - 126);
                g.DrawString("Tab       选中筛选项", _fAxis, b, 24, BH - 104);
                g.DrawString("Ctrl+C   复制明细", _fAxis, b, 24, BH - 82);
                g.DrawString("Esc       返回 / 关闭", _fAxis, b, 24, BH - 60);
            }
            using (SolidBrush b = new SolidBrush(Cgreen)) g.FillEllipse(b, 24, BH - 22, 6, 6);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(BuildInfo.Version + " · 本地记录 365 天", _fSmall, b, 38, BH - 28);
        }

        /// <summary>
        /// 页脚上的按钮:在窗口坐标里画,但命中区按内容坐标登记(页脚不参与内容平移)。
        /// 直接往 _chips 里塞窗口坐标就是 1.8.1 里「在报告中查看点不动」的那个偏移 bug。
        /// </summary>
        private void PaintFooterChip(Graphics g, RectangleF windowRect, string text, int id)
        {
            PointF p = ToBase(_mouse);
            bool hov = windowRect.Contains(p);
            using (GraphicsPath path = RoundedRect(windowRect.X, windowRect.Y, windowRect.Width, windowRect.Height, 8))
            {
                using (SolidBrush b = new SolidBrush(hov ? Ccard2 : Ccard)) g.FillPath(b, path);
                using (Pen pen = new Pen(Cline)) g.DrawPath(pen, path);
            }
            using (SolidBrush b = new SolidBrush(hov ? Ctext : Csub))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, _fChip, b, windowRect, format);
            _chips.Add(new RectangleF(windowRect.X - ContentX, windowRect.Y - ContentY, windowRect.Width, windowRect.Height));
            _chipIds.Add(id);
        }

        /// <summary>
        /// 页脚主按钮(实心 Accent 底 + OnAccent 描边)。downloadGlyph 为 true 时在文字左侧画一个
        /// 下载箭头,箭头与文字按整体宽度居中;以前「导出 CSV」是自己一份内联绘制,没有描边、
        /// 文字钉死在 (947,685)、也不进 _chips,和「保存 Markdown」长得不一样且键盘摸不到。
        /// </summary>
        private void PaintFooterPrimaryChip(Graphics g, RectangleF windowRect, string text, int id, bool downloadGlyph)
        {
            PointF p = ToBase(_mouse);
            bool hov = windowRect.Contains(p);
            using (GraphicsPath path = RoundedRect(windowRect.X, windowRect.Y, windowRect.Width, windowRect.Height, 8))
            {
                using (SolidBrush b = new SolidBrush(hov ? ArtTheme.Mix(Cblue, Ctext, 0.18) : Cblue)) g.FillPath(b, path);
                using (Pen pen = new Pen(ArtTheme.Current.OnAccent, 1.4f)) g.DrawPath(pen, path);
            }
            Color ink = ArtTheme.Current.OnAccent;
            if (downloadGlyph)
            {
                const float glyphW = 11f, glyphGap = 6f;
                SizeF textSize = g.MeasureString(text, _fChip);
                float left = windowRect.X + (windowRect.Width - (glyphW + glyphGap + textSize.Width)) / 2f;
                float mid = windowRect.Y + windowRect.Height / 2f;
                float cx = left + glyphW / 2f;
                using (Pen pen = new Pen(ink, 1.4f))
                {
                    g.DrawLine(pen, cx, mid - 7, cx, mid + 2);
                    g.DrawLine(pen, cx - 3, mid - 1, cx, mid + 2);
                    g.DrawLine(pen, cx + 3, mid - 1, cx, mid + 2);
                    g.DrawLine(pen, cx - 5, mid + 5, cx + 5, mid + 5);
                }
                using (SolidBrush b = new SolidBrush(ink))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, _fChip, b, new RectangleF(left + glyphW + glyphGap, windowRect.Y, textSize.Width + 8f, windowRect.Height), format);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(ink))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, _fChip, b, windowRect, format);
            }
            _chips.Add(new RectangleF(windowRect.X - ContentX, windowRect.Y - ContentY, windowRect.Width, windowRect.Height));
            _chipIds.Add(id);
        }

        private void PaintFooter(Graphics g)
        {
            using (Pen p = new Pen(Cline)) g.DrawLine(p, 200, 666, 1032, 666);
            // 命中区只在真的画出「导出 CSV」时才有效。以前这里无条件先写死坐标,而日报/字符内容
            // 分支又提前 return,于是 _exportRect 会残留成 (912,680,120,28) —— OnMouseClick 里
            // Hit(_exportRect) 排在 _chips 之前,正好把日报页「保存 Markdown」的 912~1032 抢走。
            // 先清空,再由导出分支自己赋值,顺序上就不可能再抢。
            _exportRect = RectangleF.Empty;
            // 字符内容页没有对应的统计报告(那份数据不进 stats.txt),所以不挂这个入口 ——
            if (_tab == TabId.Text)
            {
                // 字符内容页没有对应的统计报告,这个位置放「重新读取」。
                PaintFooterChip(g, new RectangleF(800, 680, 104, 28), "重新读取", 404);
            }
            if (_tab == TabId.Day)
            {
                // 日报页:导出换成「复制简报」和蓝色主按钮「保存 Markdown」。
                PaintFooterChip(g, new RectangleF(792, 680, 108, 28), "复制简报", 424);
                PaintFooterPrimaryChip(g, new RectangleF(908, 680, 124, 28), "保存 Markdown", 423, false);
                if(!_loaderVisible)
                {
                    string dayPath = Path.Combine(DailyReport.Directory,
                        _dayReport == null ? "" : _dayReport.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md");
                    bool archived = _dayReport != null && File.Exists(dayPath);
                    RectangleF badge = new RectangleF(200, 680, 330, 26);
                    using (GraphicsPath path = RoundedRect(badge.X, badge.Y, badge.Width, badge.Height, 13))
                    {
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(archived ? 14 : 8, Cgreen))) g.FillPath(fill, path);
                        using (Pen edge = new Pen(Color.FromArgb(archived ? 55 : 30, Cgreen))) g.DrawPath(edge, path);
                    }
                    using (SolidBrush dot = new SolidBrush(Cgreen)) g.FillEllipse(dot, badge.X + 10, badge.Y + 9, 7, 7);
                    bool statusShown = _status.Length > 0 && DateTime.UtcNow < _statusUntil;
                    string badgeText = statusShown ? _status
                        : archived ? "✓ 已归档至本地日报目录" : "日报尚未归档 · 点击打开目录";
                    AppText(g, badgeText, _fSmall,
                        archived || statusShown ? Ctext : Csub, new RectangleF(badge.X + 24, badge.Y + 4, badge.Width - 34, 18), false);
                    _chips.Add(new RectangleF(badge.X - ContentX, badge.Y - ContentY, badge.Width, badge.Height));
                    _chipIds.Add(425);
                }
                return;
            }
            // 导出 CSV 与「保存 Markdown」是同一类主按钮,走同一个绘制函数:
            // 补上 OnAccent 描边、图标与文字整体居中,并登记进 _chips 以便 Tab / Enter 触发。
            _exportRect = new RectangleF(912, 680, 120, 28);
            PaintFooterPrimaryChip(g, _exportRect, "导出 CSV", ExportChipId, true);
            if(!_loaderVisible)
                PaintFooterStatus(g, StatusText(FooterHint()), new RectangleF(200, 683, 574, 24));
        }

        private void PaintFooterStatus(Graphics g, string text, RectangleF area)
        {
            if (text != null && text.StartsWith("昨日日报已生成", StringComparison.Ordinal))
            {
                string suffix = text.Length > 7 ? text.Substring(7).TrimStart(':', '：') : "";
                RectangleF badge = new RectangleF(area.X, 680, 300, 26);
                using (GraphicsPath path = RoundedRect(badge.X, badge.Y, badge.Width, badge.Height, 13))
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(14, Cgreen))) g.FillPath(fill, path);
                    using (Pen edge = new Pen(Color.FromArgb(55, Cgreen))) g.DrawPath(edge, path);
                }
                using (SolidBrush dot = new SolidBrush(Cgreen)) g.FillEllipse(dot, badge.X + 10, badge.Y + 9, 7, 7);
                AppText(g, "✓ 昨日日报已归档" + (suffix.Length > 0 ? " · " + suffix : ""), _fSmall, Ctext,
                    new RectangleF(badge.X + 24, badge.Y + 4, badge.Width - 34, 18), false);
                _chips.Add(new RectangleF(badge.X - ContentX, badge.Y - ContentY, badge.Width, badge.Height));
                _chipIds.Add(46);
                return;
            }
            AppText(g, text, _fSmall, Csub, area, false);
        }

        /// <summary>页脚提示(也用于 Ctrl+C 复制「你在看什么」)。</summary>
        private string FooterHint()
        {
            return _tab == TabId.Apps ? (_appView == 1 ? "APM = 每分钟击键+点击 · 只统计前台活跃 ≥60 秒的应用 · 悬停看明细"
                                              : _appView == 2 ? "时长占比 = 该应用前台活跃 ÷ 全部应用前台活跃 · 图例右侧同时给出 APM"
                                              : "按捕获时的前台窗口归因 · 分类可手动调整 · 本页导出应用明细")
                    : _tab == TabId.Day ? DayFooterHint()
                    : _tab == TabId.Text ? TextFooterHint()
                    : _tab == TabId.Insights ? "活跃不等于专注 · 空白可能未采集 · 导出 " + _insightDay.ToString("yyyy-MM-dd") + " 的时间区间"
                    : _tab == TabId.Trend && _trendHourly ? "均值不含当前小时 · 数值仅为已采集部分 · 本页导出小时明细"
                    : _tab == TabId.Trend ? "今日仍在累计 · 折线末段为虚线 · 灰色竖带=未运行（不计入日均）" + (_trendCompare ? " · 已叠加较上期均值" : " · 环比按昨日同时段")
                    : _tab == TabId.Hours ? (_hourView == 2 ? "单日时间轴 · 段高与颜色=" + HourMetricName() + "强度(按当日峰值归一) · ‹ › 翻记录日 · 底部为短段占比(<5 分钟)" : _hourView == 1 ? "行为周一至周日，列为 0–23 点 · " + HourRangeName() + (_hourRange == 0 ? "（进行中）" : " · 按有记录日平均（不含今天）") + " · 悬停看平均值" : "颜色越亮越活跃 · 柱高为数值 · 当前：" + HourRangeName() + " · 悬停看该时段明细")
                    : _tab == TabId.Keys && _keyView == 2 ? (_keyMetric == 1 ? "物理键位热力 · 按住时长口径:逐键累计,悬停看平均与最长 · 右上角切换配列 · 导出同时包含两种口径" : "物理键位热力 · 长按计一次 · 右上角切换配列 · 导出包含配列外已归位按键")
                    : _tab == TabId.Keys && _keyView == 1 ? "修饰键 + 普通键首次按下计 1 次 · 长按去重 · 左右修饰键合并 · 本页导出组合动作"
                    : _tab == TabId.Keys && _keyMetric == 1 ? "按住时长从 1.7.3 起采集 · 长按只计一次 · 可切换时间范围 · 本页导出按住时长明细"
                    : _tab == TabId.Keys ? ""
                    : _tab == TabId.Heat ? (_clickActionView ? ClickRangeName() + " · 鼠标动作 · 方向/甩动/拖拽/点击均来自相对鼠标数据" : "九宫格按所选屏幕计算 · 单屏放大可看更细的格位 · 位置明细保留 30 天")
                    : _tab == TabId.Overview ? "本段 " + (ActivityMonitor.CurrentSession == null ? "--" : ActivityMonitor.FormatDuration(ActivityMonitor.CurrentSession.Seconds)) + " · 今日峰值 " + Store.Today.PeakApm + " APM · 实时键 " + LiveRate.KeysPerMin + " / 点 " + LiveRate.ClicksPerMin
                    : "鼠标路程为 DPI 估算值 · 设置后开始累计 · 原光标路程见导出文件";
        }

        /// <summary>短暂状态提示(2.5 秒),用于「已复制」这类反馈。</summary>
        private void SetStatus(string text)
        {
            _status = text ?? "";
            _statusUntil = DateTime.UtcNow.AddMilliseconds(2500);
            Motion.Start("status", 2500, Ease.Linear);
            ScheduleFrame();
            Invalidate(new Rectangle(200, 680, (int)(830 * _s), (int)(24 * _s)));
        }
        /// <summary>页脚要显示的文字:状态提示优先,过期后回到本页提示。</summary>
        private string StatusText(string hint)
        {
            if (_status.Length > 0 && DateTime.UtcNow < _statusUntil) return _status;
            if (_status.Length > 0) _status = "";
            return hint;
        }
        /// <summary>Ctrl+C:优先复制悬停元素的明细,否则复制本页提示(含当前筛选)。</summary>
        internal string CopyText()
        {
            string text = _hoverDetail.Length > 0 ? _hoverDetail : FooterHint();
            try { Clipboard.SetText(text); SetStatus("已复制：" + (text.Length > 60 ? text.Substring(0, 60) + "…" : text)); }
            catch (Exception) { SetStatus("剪贴板不可用，已在页脚显示内容"); }
            return text;
        }
        /// <summary>激活当前键盘选中的 chip(供测试与 Enter 使用)。</summary>
        internal bool ActivateFocusedChip()
        {
            if (_chipFocus < 0 || _chipFocus >= _chips.Count || _chipFocus >= _chipIds.Count) return false;
            ApplyChip(_chipIds[_chipFocus]);
            Invalidate();
            return true;
        }

        /// <summary>
        /// Tab 选中的 chip 画一圈焦点环。_chips 记的是内容坐标(页脚按钮也按内容坐标登记),
        /// 这里已经恢复成窗口坐标,所以要补上 ContentX/ContentY 的偏移。
        /// 换页/换视图后控件集合变了,下标越界时清掉焦点,避免环压在无关控件上。
        /// </summary>
        private void PaintFocusRing(Graphics g)
        {
            if (_chipFocus >= _chips.Count) { _chipFocus = -1; return; }
            if (_chipFocus < 0) return;
            RectangleF focus = _chips[_chipFocus];
            using (GraphicsPath ring = RoundedRect(focus.X + ContentX - 2, focus.Y + ContentY - 2,
                focus.Width + 4, focus.Height + 4, StepChipRadius + 1))
            using (Pen pen = new Pen(Ccyan, 1.8f)) g.DrawPath(pen, ring);
        }

        /// <summary>主题侧栏图标的编号。数组比标签页少一项时夹到末项,不让以后加页变成运行时越界。</summary>
        private static int NavSymbolFor(int index)
        {
            int[] symbols = GujianArt.Active ? GujianArt.NavSymbols
                : OriArt.Active ? OriArt.NavSymbols
                : EuroTruckArt.Active ? EuroTruckArt.NavSymbols
                : WuxiaArt.Active ? WuxiaArt.NavSymbols
                : BalatroArt.Active ? BalatroArt.NavSymbols : null;
            if (symbols == null || symbols.Length == 0) return index;
            return symbols[Math.Max(0, Math.Min(index, symbols.Length - 1))];
        }

        /// <summary>主题的页面英文名,同样夹取末项。</summary>
        private static string PageLabelFor(int index)
        {
            string[] labels = GujianArt.Active ? GujianArt.PageLabels
                : OriArt.Active ? OriArt.PageLabels
                : EuroTruckArt.Active ? EuroTruckArt.PageLabels
                : WuxiaArt.Active ? WuxiaArt.PageLabels
                : BalatroArt.Active ? BalatroArt.PageLabels
                : MinecraftArt.Active ? MinecraftArt.PageLabels
                : ResidentArt.Active ? ResidentArt.PageLabels
                : HaloArt.Active ? HaloArt.PageLabels : null;
            if (labels == null || labels.Length == 0) return "";
            return labels[Math.Max(0, Math.Min(index, labels.Length - 1))];
        }

        private void DrawGlyph(Graphics g, int kind, float x, float y, Color color)
        {
            using (Pen p = new Pen(color, 1.5f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                if (kind == 0)
                {
                    g.DrawRectangle(p, x, y, 6, 6); g.DrawRectangle(p, x + 10, y, 6, 6);
                    g.DrawRectangle(p, x, y + 10, 6, 6); g.DrawRectangle(p, x + 10, y + 10, 6, 6);
                }
                else if (kind == 1)
                    g.DrawLines(p, new PointF[] { new PointF(x, y + 14), new PointF(x + 5, y + 8), new PointF(x + 10, y + 11), new PointF(x + 16, y + 2) });
                else if (kind == 2)
                {
                    g.DrawEllipse(p, x, y, 16, 16); g.DrawLine(p, x + 8, y + 3, x + 8, y + 8); g.DrawLine(p, x + 8, y + 8, x + 12, y + 10);
                }
                else if (kind == 4)
                {
                    g.DrawRectangle(p, x, y, 16, 16);
                    g.DrawLine(p, x, y + 5, x + 16, y + 5);
                    g.DrawLine(p, x + 5, y + 5, x + 5, y + 16);
                }
                else if (kind == 6)
                {
                    // 第 7 页「字符内容」:一张纸 + 三行字,最后一行短。
                    g.DrawRectangle(p, x, y, 16, 16);
                    g.DrawLine(p, x + 3, y + 5, x + 13, y + 5);
                    g.DrawLine(p, x + 3, y + 9, x + 13, y + 9);
                    g.DrawLine(p, x + 3, y + 13, x + 9, y + 13);
                }
                else
                {
                    for (int i = 0; i < 3; i++)
                    { g.DrawLine(p, x, y + 3 + i * 5, x + 1, y + 3 + i * 5); g.DrawLine(p, x + 5, y + 3 + i * 5, x + 16 - i * 3, y + 3 + i * 5); }
                }
            }
        }

        // ---------------------------------------------------------------- 标签页:总览

        /// <summary>
        /// 带分帧缓存的按键排行。总览页在实时击键时约 25fps 重绘,而「分布环」与「Top5 迷你卡」
        /// 每帧各要一次全量 KeyCounts 聚合 + 排序 —— 这是 Gen0 抖动的主要来源。
        /// 失效条件全部 O(1):窗口起点、有记录的天数、今天的击键数。
        /// 后两者合起来覆盖「跨天翻页 / 导入补数据 / 今天继续敲」三种会改变结果的情形。
        /// slot 用来区分两个不同窗口的缓存(否则分布环与 Top5 会互相顶掉,永远命中不到)。
        /// </summary>
        internal List<KeyValuePair<string, long>> CachedKeyRanking(int slot, DateTime from)
        {
            RankCache cache = _rankCaches[slot];
            int days = Store.History.Count;
            long live = Store.Today == null ? 0L : Store.Today.Keys;
            if (cache.List != null && cache.From == from && cache.Days == days && cache.Live == live)
                return cache.List;
            List<DayRecord> daysList = new List<DayRecord>();
            foreach (DayRecord day in Store.History.Values) if (day.Date >= from) daysList.Add(day);
            cache.List = Analysis.KeyRanking(daysList);
            cache.From = from;
            cache.Days = days;
            cache.Live = live;
            return cache.List;
        }

        private void PaintOverview(Graphics g)
        {
            DayRecord today = Store.Today;
            string[] labels = { "今日击键", "今日点击", "今日滚轮", "鼠标路程(估算)", "有效使用时长", "连续使用段", "击键速率 / 分", "操作速率 APM" };
            string[] vals = {
                Analysis.FmtCount(Motion.Roll("ov-keys", today.Keys, 160)),
                Analysis.FmtCount(Motion.Roll("ov-clicks", today.Clicks, 160)),
                Analysis.FmtCount(Motion.Roll("ov-wheel", today.Wheel, 160)),
                Analysis.FmtDistance(Motion.Roll("ov-meters", today.MoveMeters, 160)),
                ActivityMonitor.FormatDuration(today.ActiveSeconds),
                today.Sessions.Count + " 段",
                Motion.Roll("ov-rate", LiveRate.KeysPerMin, 160).ToString("N0", CultureInfo.InvariantCulture),
                Motion.Roll("ov-apm", LiveRate.Apm, 160).ToString("N0", CultureInfo.InvariantCulture) };
            Color[] cols = { Cblue, Cgreen, Corange, Cpurple, Cgreen, Cblue, Ccyan, Cred };
            int[] haloSymbols = GujianArt.Active?GujianArt.MetricSymbols:OriArt.Active?OriArt.MetricSymbols:EuroTruckArt.Active?EuroTruckArt.MetricSymbols:WuxiaArt.Active?WuxiaArt.MetricSymbols:BalatroArt.Active?new[]{0,1,2,3,4,5,6,7}:new[]{3,6,7,8,9,10,1,11};

            float gap = 16, tileW = (Cw - 3 * gap) / 4f;
            string keysBaseline=OverviewBaseline(true),activeBaseline=OverviewBaseline(false);
            _activityRect = new RectangleF(Cx, Cy + 104, tileW, 88);
            _sessionsRect = new RectangleF(Cx + tileW + gap, Cy + 104, tileW, 88);
            _overviewTiles.Clear();
            for (int i = 0; i < 8; i++)
            {
                int col = i % 4, row = i / 4;
                float x = Cx + col * (tileW + gap);
                float y = Cy + row * (88 + 16);
                _overviewTiles.Add(new RectangleF(x, y, tileW, 88));   // 1.8.0:点击下钻用
                if(i==7)
                {
                    PaintCardBase(g,x,y,tileW,88);
                    AppText(g,"操作速率 APM",_fBody,Csub,new RectangleF(x+18,y+13,tileW-36,20),false);
                    AppText(g,vals[i],_fNum2,Ctext,new RectangleF(x+18,y+35,68,28),false);
                    LiveRate.PaintTrend(g,new RectangleF(x+85,y+37,tileW-103,27),Ccyan);
                    AppText(g,"最近 5 分钟 · 60秒滚动值",_fAxis,Csub,new RectangleF(x+18,y+67,tileW-28,18),false);
                }
                else PaintTile(g, x, y, tileW, 88, labels[i], vals[i], cols[i], haloSymbols[i], i==0?keysBaseline:i==4?activeBaseline:null);
            }

            float cardY = Cy + 2 * 88 + 16 + 16;   // 306
            float cardH = 234;

            // 左卡:按键分布。绘制顺序 = Tab 顺序:
            //   Top 10 ▾ → 鼠标 → 键盘 → 今日 / 本周 / 本月 / 本年
            // 旧顺序把 Top N 放在键盘绘制函数内部、晚于鼠标/键盘两颗,于是按 Tab 会
            // 「右(鼠标) → 更右(键盘) → 回跳左(Top N) → 甩到最左列(今日)」。
            PaintCard(g, Cx, cardY, 404, cardH, "按键分布");
            _distributionLegendSlices.Clear();
            _distributionLegendRects.Clear();
            if (_overviewKeyboard) PaintDistributionTopChip(g, Cx, cardY);
            PaintDistributionModeChips(g, Cx, cardY);
            PaintDistributionPeriodChips(g, Cx, cardY);
            if (_overviewKeyboard) PaintKeyboardDistribution(g, Cx, cardY);
            else PaintMouseDistribution(g, Cx, cardY);

            // 右卡:按键 Top5(累计)
            PaintCard(g, Cx + 404 + 17, cardY, Cw - 404 - 17, cardH, "最常按键 Top 5 · 累计");
            PaintTopKeysMini(g, Cx + 404 + 17 + 20, cardY + 40, Cw - 404 - 17 - 40, cardH - 56);

            // 底部洞察
            PaintInsights(g, Cx, cardY + cardH + 14, Cw, 24);
        }

        /// <summary>分布卡右上角的 Top N 下拉。只在键盘模式出现(鼠标模式没有 Top-N 概念)。</summary>
        private void PaintDistributionTopChip(Graphics g, float x, float y)
        {
            _distributionCountRect = new RectangleF(x + 169, y + 7, 96, ToggleChipHeight);
            PaintToggleChip(g, _distributionCountRect, "Top " + _distributionTop + " ▾", false, DistributionTopChipId);
        }

        private void PaintDistributionModeChips(Graphics g, float x, float y)
        {
            _distributionMouseRect = new RectangleF(x + 272, y + 7, 56, ToggleChipHeight);
            _distributionKeyboardRect = new RectangleF(x + 336, y + 7, 56, ToggleChipHeight);
            PaintToggleChip(g, _distributionMouseRect, "鼠标", !_overviewKeyboard, DistributionMouseChipId);
            PaintToggleChip(g, _distributionKeyboardRect, "键盘", _overviewKeyboard, DistributionKeyboardChipId);
        }

        /// <summary>今日 / 本周 / 本月 / 本年。鼠标与键盘两种模式共用同一个时间维度。</summary>
        private void PaintDistributionPeriodChips(Graphics g, float x, float y)
        {
            string[] periods = { "今日", "本周", "本月", "本年" };
            for (int i = 0; i < 4; i++)
            {
                _distributionPeriods[i] = new RectangleF(x + 12, y + 61 + i * 37, 52, ToggleChipHeight);
                PaintToggleChip(g, _distributionPeriods[i], periods[i], _distributionPeriod == i, DistributionPeriodChipId + i);
            }
        }

        private static string OverviewBaseline(bool keys)
        {
            List<double> values=PersonalStats.History(DateTime.Today.AddDays(-1),30,delegate(DayRecord day){return keys?day.Keys:day.ActiveSeconds;});
            if(values.Count<7)return null;
            double p50=PersonalStats.Quantile(values,.5),p90=PersonalStats.Quantile(values,.9);
            if(keys)return "P50 "+Analysis.FmtCount((long)p50)+" · P90 "+Analysis.FmtCount((long)p90);
            return "P50 "+OverviewDuration(p50)+" · P90 "+OverviewDuration(p90);
        }
        private static string OverviewDuration(double seconds)
        {
            int total=(int)Math.Round(Math.Max(0,seconds));int hours=total/3600,minutes=(total%3600)/60;
            return hours>0?hours+"时"+minutes.ToString("00")+"分":minutes+"分";
        }
        private void PaintTile(Graphics g, float x, float y, float w, float h, string label, string value, Color c, int haloSymbol = 1, string extra = null)
        {
            PaintCardBase(g, x, y, w, h);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(label, _fBody, b, x + 18, y + 13);
            Font valueFont = g.MeasureString(value, _fNum).Width > w - 32 ? _fNum2 : _fNum;
            // 有副标(例如「较上期 +12%」)时数字要上提一点,否则 29 px 的字会压到副标上。
            float valueTop = string.IsNullOrEmpty(extra) ? y + 36 : y + 30;
            using (SolidBrush b = new SolidBrush(Ctext)) g.DrawString(value, valueFont, b, x + 16, valueTop);
            if (!string.IsNullOrEmpty(extra))
                using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(extra, _fAxis, b, x + 18, y + h - 19);
            using (GraphicsPath p = RoundedRect(x + w - 48, y + 18, 30, 30, 10))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(24, c))) g.FillPath(b, p);
            if(ThemeArt.Dark){ThemeArt.Symbol(g,haloSymbol,new RectangleF(x+w-51,y+14,37,37));return;}
            using (Pen p = new Pen(c, 2))
            {
                g.DrawLine(p, x + w - 40, y + 38, x + w - 40, y + 32);
                g.DrawLine(p, x + w - 33, y + 38, x + w - 33, y + 27);
                g.DrawLine(p, x + w - 26, y + 38, x + w - 26, y + 23);
            }
        }

        private void PaintCardBase(Graphics g, float x, float y, float w, float h)
        {
            int radius = ArtTheme.Current.Radius;
            using (GraphicsPath shadow = RoundedRect(x + (Store.ThemeId == 4 ? 3 : 0), y + 3, w, h, radius))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(Store.ThemeId == 4 ? 75 : 25, ArtTheme.Current.Sidebar))) g.FillPath(b, shadow);
            using (GraphicsPath p = RoundedRect(x, y, w, h, radius))
            {
                bool portraitGlass=ThemeArt.Active && (ThemeArt.Dark || _tab!=TabId.Overview) && w>400 && h>120;
                // 高反差原画(雪山、夜路高光、车灯)会顺着半透明底板把线条透到数据上。
                // 暗色美术主题把底板抬到 85%~93%,浅色美术主题(Nikki)保持原来的通透感。
                int glassTop = ThemeArt.Dark ? 216 : 176;
                int glassBottom = ThemeArt.Dark ? 236 : 195;
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, w, h), portraitGlass?Color.FromArgb(glassTop,Ccard2):Ccard2, portraitGlass?Color.FromArgb(glassBottom,Ccard):Ccard, 90f)) g.FillPath(b, p);
                using (Pen pen = new Pen(Cline)) g.DrawPath(pen, p);
                if(NikkiArt.Active)
                {
                    using(Pen light=new Pen(Color.FromArgb(210,Color.White)))g.DrawLine(light,x+radius,y+2,x+w-radius,y+2);
                    NikkiArt.Star(g,x+w-13,y+13,4,Color.FromArgb(140,Corange));
                }
                if(ThemeArt.Dark && !GujianArt.Active && !WuxiaArt.Active && !EuroTruckArt.Active && !OriArt.Active)
                    using(Pen edge=new Pen(Ccyan,2)) {g.DrawLine(edge,x+8,y,x+38,y);g.DrawLine(edge,x+8,y,x+8,y+7);}
                if(GujianArt.Active)GujianArt.CardOrnaments(g,new RectangleF(x,y,w,h));
                if(OriArt.Active)OriArt.CardOrnaments(g,new RectangleF(x,y,w,h));
                if(EuroTruckArt.Active)EuroTruckArt.CardOrnaments(g,new RectangleF(x,y,w,h));
                if(WuxiaArt.Active)WuxiaArt.CardOrnaments(g,new RectangleF(x,y,w,h));
                if (Store.ThemeId == 3)
                    using (Pen pen = new Pen(Color.FromArgb(120, Ccyan))) g.DrawLine(pen, x + 10, y, x + Math.Min(w - 10, 65), y);
            }
        }

        private void PaintCard(Graphics g, float x, float y, float w, float h, string title)
        {
            PaintCardBase(g, x, y, w, h);
            if (title == null) return;
            using (SolidBrush b = new SolidBrush(Ctext))
                g.DrawString(title, _fH2, b, x + 18, y + 12);
        }

        /// <summary>
        /// 分布环的统一几何。两种模式必须共用同一个外接矩形与笔宽 ——
        /// 鼠标环原来自己用 Cx+112/cardY+126/r78,在时段胶囊也出现在鼠标模式之后,
        /// 它的左外沿(50)正好穿过胶囊列(36–88),环被画在胶囊上面。
        /// </summary>
        internal static RectangleF DistributionRingRect(float x, float y)
        {
            // y + 49 → 外沿 = 环心 + 97 = 346,距 Top 10 ▾ 底边 343 留 3px 净空。
            return new RectangleF(x + 82, y + 49, 176, 176);
        }

        /// <summary>分布环的笔宽。两种模式一致,切模式时环不换粗细。</summary>
        internal const float DistributionRingStroke = 18f;

        /// <summary>鼠标分布卡:环 + 图例。</summary>
        private void PaintMouseDistribution(Graphics g, float x, float y)
        {
            long left, right, middle, extra;
            MousePeriodValues(out left, out right, out middle, out extra);
            RectangleF ring = DistributionRingRect(x, y);
            PaintDonut(g, ring.X + ring.Width / 2f, ring.Y + ring.Height / 2f, ring.Width / 2f,
                left, right, middle, extra);
            // 图例与键盘模式同一列起点(x+278),两种模式的版式才对得上。
            PaintMouseLegend(g, x + 278, y + 42, 234 - 60);
        }

        /// <summary>
        /// 按 _distributionPeriod 从 Store.History 聚合鼠标四键。
        /// 旧版这里直接读 Store.Total(建库以来全量、且含已归档月份),而键盘侧是
        /// DistributionStart 起 ≤365 天的窗口 —— 同一张卡上两个环的时间口径完全不同,
        /// 切一次模式数字毫无可比性。现在两边共用同一个时间维度。
        /// </summary>
        internal void MousePeriodValues(out long left, out long right, out long middle, out long extra)
        {
            DateTime today = DateTime.Today;
            DateTime start = DistributionStart(today, _distributionPeriod);
            left = 0; right = 0; middle = 0; extra = 0;
            foreach (DayRecord day in Store.History.Values)
            {
                if (day == null || day.Date < start || day.Date > today) continue;
                left += day.Left; right += day.Right; middle += day.Middle; extra += day.XButtons;
            }
        }

        private static readonly string[] MouseSliceNames = { "左键", "右键", "中键", "侧键" };

        /// <summary>鼠标四键的身份色。用方法而不是静态数组:静态字段初始化器在 Dashboard
        /// 的静态构造期跑,那时去读依赖 ArtTheme 的 Cblue/Cgreen 属性容易踩初始化顺序。</summary>
        private static Color MouseSliceColor(int index)
        {
            switch (index)
            {
                case 0: return Cblue;
                case 1: return Cgreen;
                case 2: return Corange;
                default: return Cpurple;
            }
        }

        private void PaintDonut(Graphics g, float cx, float cy, float r, long left, long right, long mid, long xb)
        {
            long[] vals = { left, right, mid, xb };
            long total = left + right + mid + xb;
            RectangleF rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            _distributionRing = rect;
            _distributionRingStroke = DistributionRingStroke;
            _distributionRingValues = vals;
            _distributionRingNames = MouseSliceNames;

            if (total == 0)
            {
                using (Pen pen = new Pen(Color.FromArgb(40, Ctext), DistributionRingStroke))
                    g.DrawArc(pen, rect, 0, 360);
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    SizeF sz = g.MeasureString("该时段暂无记录", _fBody);
                    g.DrawString("该时段暂无记录", _fBody, b, cx - sz.Width / 2, cy - sz.Height / 2);
                }
                return;
            }

            float[] draw = SliceSweeps(vals, total, 0.4f);
            int hovered = _distributionHoverSlice;
            using (Pen track = new Pen(Color.FromArgb(26, Ctext), DistributionRingStroke))
                g.DrawArc(track, rect, 0, 360);
            if(GujianArt.Active)GujianArt.RingAura(g,rect);
            else if(OriArt.Active)OriArt.RingAura(g,rect);
            float start = -90f;
            for (int i = 0; i < 4; i++)
            {
                if (draw[i] <= 0f) continue;
                float sweep = draw[i];
                // 间隙从扇区自身角度里内缩,而 start 累加的是 draw[i] ——
                // 两者严格守恒,不会再出现「抬到 0.4° 的碎扇区压到下一块身上」。
                float inset = Math.Min(1.2f, sweep / 4);
                Color color = i == hovered ? ArtTheme.Mix(MouseSliceColor(i), Ctext, .25f) : MouseSliceColor(i);
                using (Pen pen = new Pen(color, i == hovered ? DistributionRingStroke + 4 : DistributionRingStroke))
                    g.DrawArc(pen, rect, start + inset, Math.Max(0.1f, sweep - inset * 2));
                start += sweep;
            }

            if (hovered >= 0 && hovered < 4)
            {
                int slice = hovered;
                using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
                using (SolidBrush text = new SolidBrush(Ctext))
                using (SolidBrush sub = new SolidBrush(Csub))
                {
                    g.DrawString(MouseSliceNames[slice], _fBody, text, new RectangleF(cx - 53, cy - 46, 106, 24), center);
                    g.DrawString(Analysis.FmtCount(vals[slice]) + " 次", _fNum2, text, new RectangleF(cx - 60, cy - 20, 120, 32), center);
                    g.DrawString("占比 " + (100.0 * vals[slice] / total).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                        _fSmall, sub, new RectangleF(cx - 53, cy + 13, 106, 22), center);
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Ctext))
                {
                    string v = Analysis.FmtCount(total);
                    SizeF sz = g.MeasureString(v, _fNum2);
                    g.DrawString(v, _fNum2, b, cx - sz.Width / 2, cy - 17);
                }
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    SizeF sz = g.MeasureString("次点击", _fSmall);
                    g.DrawString("次点击", _fSmall, b, cx - sz.Width / 2, cy + 7);
                }
            }
        }

        /// <summary>
        /// 把各扇区的角度算好:先按真实比例,再把小于 minSweep 的抬到 minSweep,
        /// 最后把「多出来的角度」从宽扇区里按比例扣回,保证 Σdraw == 360。
        /// 守恒是必须的 —— 旧实现把 sweep 抬到 0.4° 却让 start 按真值累加,
        /// 碎扇区画出来比它占的角度宽,于是盖在下一块身上。
        /// </summary>
        internal static float[] SliceSweeps(long[] values, long total, float minSweep)
        {
            float[] draw = new float[values.Length];
            if (total <= 0) return draw;
            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                draw[i] = values[i] > 0 ? 360f * values[i] / total : 0f;
                sum += draw[i];
            }
            if (minSweep <= 0f) return draw;
            for (int i = 0; i < draw.Length; i++) if (draw[i] > 0f && draw[i] < minSweep) draw[i] = minSweep;
            float deficit = 0f;
            for (int i = 0; i < draw.Length; i++) deficit += draw[i];
            deficit -= sum;
            if (deficit <= 0.0001f) return draw;
            float pool = 0f;
            for (int i = 0; i < draw.Length; i++) if (draw[i] > minSweep) pool += draw[i];
            if (pool <= 0.0001f) return draw;
            float take = Math.Min(deficit, pool * 0.98f);
            for (int i = 0; i < draw.Length; i++)
                if (draw[i] > minSweep) draw[i] -= take * (draw[i] / pool);
            return draw;
        }

        internal static DateTime DistributionStart(DateTime today,int period)
        {
            today=today.Date;
            if(period==1)return today.AddDays(-((int)today.DayOfWeek+6)%7);
            if(period==2)return new DateTime(today.Year,today.Month,1);
            if(period==3)return new DateTime(today.Year,1,1);
            return today;
        }
        internal static int DistributionHit(PointF point,RectangleF ring,float width,long[] values)
        {
            double dx=point.X-(ring.X+ring.Width/2),dy=point.Y-(ring.Y+ring.Height/2);
            double radius=ring.Width/2,distance=Math.Sqrt(dx*dx+dy*dy);
            if(distance<radius-width/2 || distance>radius+width/2)return -1;
            long total=0;foreach(long value in values)total+=value;
            if(total<=0)return -1;
            // 0° 对应 12 点方向,顺时针增长,与 DrawArc 的 start=-90 + 正 sweep 完全一致。
            // 浮点累加的末端可能停在 359.9999,此时回退到"最后一个有值的扇区",
            // 而不是返回一个零值下标或 -1,免得 12 点方向出现一条点不中的死缝。
            double angle=(Math.Atan2(dy,dx)*180/Math.PI+450)%360,end=0;
            for(int i=0;i<values.Length;i++){end+=360.0*values[i]/total;if(values[i]>0 && angle<end)return i;}
            for (int i = values.Length - 1; i >= 0; i--) if (values[i] > 0) return i;
            return -1;
        }

        /// <summary>上一帧画出来的环是否命中;命中时给出扇区下标。</summary>
        internal bool HitDistributionRing(PointF contentPoint, out int slice)
        {
            slice = -1;
            if (_distributionRingValues == null || _distributionRingValues.Length == 0) return false;
            if (_distributionRingStroke <= 0f || _distributionRing.Width <= 0f) return false;
            slice = DistributionHit(contentPoint, _distributionRing, _distributionRingStroke, _distributionRingValues);
            return slice >= 0;
        }

        /// <summary>
        /// 双向悬停:圆环扇区命中和右侧图例行命中都归到同一个 _distributionHoverSlice,
        /// 这样环与图例永远同步高亮。几何取自上一帧的绘制结果,移动鼠标不会重算排行。
        /// </summary>
        private void UpdateDistributionHover(PointF contentPoint)
        {
            int hover = -1;
            if (HitDistributionRing(contentPoint, out hover)) { }
            else
            {
                hover = -1;
                for (int i = 0; i < _distributionLegendRects.Count && i < _distributionLegendSlices.Count; i++)
                    if (_distributionLegendRects[i].Contains(contentPoint)) { hover = _distributionLegendSlices[i]; break; }
            }
            if (hover != _distributionHoverSlice)
            {
                _distributionHoverSlice = hover;
                Invalidate();
            }
        }
        private void PaintKeyboardDistribution(Graphics g,float x,float y)
        {
            DateTime today = DateTime.Today;
            DateTime start = DistributionStart(today, _distributionPeriod);
            // 排行走分帧缓存:实时击键时每帧都做一次全量聚合 + 排序是掉帧的主因。
            List<KeyValuePair<string, long>> rank = CachedKeyRanking(0, start);
            int slices = _distributionTop + 1;
            long[] values = new long[slices];
            string[] names = new string[slices];
            names[_distributionTop] = "其他";
            long total = 0;
            for (int i = 0; i < rank.Count; i++)
            {
                total += rank[i].Value;
                if (i < _distributionTop) { values[i] = rank[i].Value; names[i] = rank[i].Key; }
                else values[_distributionTop] += rank[i].Value;
            }
            // 小于 0.35° 的碎块并入「其他」:18px 笔宽下这种角度只会画出一个孤立圆点,
            // 既看不清也点不中。合并后各扇区角度仍然是真实占比(分母 total 不变)。
            if (total > 0)
            {
                long moved = 0;
                for (int i = 0; i < _distributionTop; i++)
                {
                    if (values[i] <= 0) continue;
                    if (360.0 * values[i] / total < 0.35) { moved += values[i]; values[i] = 0; names[i] = null; }
                }
                if (moved > 0) values[_distributionTop] += moved;
            }

            // 扇区颜色绑按键名,不绑扇区顺序。零值/被合并的档位不占色板槽位。
            List<string> sliceNames = new List<string>();
            for (int i = 0; i < _distributionTop; i++)
                if (values[i] > 0 && !string.IsNullOrEmpty(names[i])) sliceNames.Add(names[i]);
            Color[] rankColors = KeyPalette.ForRanking(sliceNames);
            Color[] colors = new Color[slices];
            int colorIndex = 0;
            for (int i = 0; i < _distributionTop; i++)
                colors[i] = (values[i] > 0 && !string.IsNullOrEmpty(names[i])) ? rankColors[colorIndex++] : Csub;
            colors[_distributionTop] = Csub;   // 「其他」始终是中性灰,不参与身份色

            // 环外接矩形与笔宽走统一常量:两种模式共用一个环,切模式时不位移也不换粗细。
            RectangleF ring = DistributionRingRect(x, y);
            float cx = ring.X + ring.Width / 2f, cy = ring.Y + ring.Height / 2f;
            _distributionRing = ring;
            _distributionRingStroke = DistributionRingStroke;
            _distributionRingValues = values;
            _distributionRingNames = names;

            int hovered = _distributionHoverSlice;
            using (Pen pen = new Pen(Color.FromArgb(26, Ctext), DistributionRingStroke)) g.DrawEllipse(pen, ring);
            if (total > 0)
            {
                float[] draw = SliceSweeps(values, total, 0f);
                float angle = -90;
                for (int i = 0; i < values.Length; i++)
                {
                    if (draw[i] <= 0f) continue;
                    Color color = i == hovered ? ArtTheme.Mix(colors[i], Ctext, .25) : colors[i];
                    using (Pen pen = new Pen(color, i == hovered ? DistributionRingStroke + 4 : DistributionRingStroke))
                        g.DrawArc(pen, ring, angle, draw[i]);
                    angle += draw[i];
                }
            }

            // 环心文字全部由 (cx, cy) 派生 —— 旧版是 5 个硬编码矩形,环一挪就全歪。
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
            using (SolidBrush text = new SolidBrush(Ctext))
            using (SolidBrush sub = new SolidBrush(Csub))
            {
                if (hovered >= 0 && hovered < values.Length)
                {
                    g.DrawString(string.IsNullOrEmpty(names[hovered]) ? "其他" : names[hovered],
                        _fBody, text, new RectangleF(cx - 53, cy - 46, 106, 24), center);
                    g.DrawString(Analysis.FmtCount(values[hovered]) + " 次", _fNum2, text,
                        new RectangleF(cx - 60, cy - 20, 120, 32), center);
                    g.DrawString("占比 " + (100.0 * values[hovered] / total).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                        _fSmall, sub, new RectangleF(cx - 53, cy + 13, 106, 22), center);
                }
                else
                {
                    g.DrawString(total > 0 ? Analysis.FmtCount(total) : "暂无数据", _fNum2, text,
                        new RectangleF(cx - 60, cy - 27, 120, 32), center);
                    g.DrawString(total > 0 ? "次已记录击键" : "该时段暂无记录", _fSmall, sub,
                        new RectangleF(cx - 56, cy + 7, 112, 19), center);
                }
            }

            // 图例:「其他」在 values[Top] > 0 时也要出现,否则那块最大的灰扇区没有任何文字解释。
            int visible = 0;
            for (int i = 0; i < values.Length; i++) if (values[i] > 0) visible++;
            int rows = Math.Max(1, (visible + 1) / 2);
            int slot = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] <= 0) continue;
                float row = y + 66 + (slot % rows) * 28;
                float column = x + 278 + (slot / rows) * 60;
                RectangleF hit = new RectangleF(column - 2, row - 2, 60, 26);
                _chips.Add(hit); _chipIds.Add(DistributionSliceChipId + i);
                _distributionLegendRects.Add(hit);
                _distributionLegendSlices.Add(i);
                using (SolidBrush brush = new SolidBrush(colors[i])) g.FillEllipse(brush, column, row + 5, 9, 9);
                // 字号统一 _fChip(12px):旧版图例用 _fTab(14px,一级导航字号),层级是错的。
                Color ink = i == hovered ? (i >= _distributionTop ? Cblue : colors[i]) : Ctext;
                AppText(g, string.IsNullOrEmpty(names[i]) ? "其他" : names[i], _fChip, ink,
                    new RectangleF(column + 13, row, 46, 24), false);
                slot++;
            }
        }
        /// <summary>鼠标图例。数值必须与 PaintDonut 同源(同一时间段),否则环和图例会各说各话。</summary>
        private void PaintMouseLegend(Graphics g, float x, float y, float h)
        {
            long left, right, middle, extra;
            MousePeriodValues(out left, out right, out middle, out extra);
            long[] vals = { left, right, middle, extra };
            string[] names = { "左键", "右键", "中键", "侧键" };
            long total = left + right + middle + extra;
            int hovered = _distributionHoverSlice;

            float rowY = y;
            for (int i = 0; i < 4; i++)
            {
                // 鼠标图例只参与悬停高亮,不登记进 _chips —— 点它没有下钻动作,
                // 多出来的 Tab 停靠点只会让键盘遍历变长。
                RectangleF row = new RectangleF(x - 2, rowY - 2, 120, 40);
                _distributionLegendRects.Add(row);
                _distributionLegendSlices.Add(i);
                using (SolidBrush b = new SolidBrush(MouseSliceColor(i)))
                    g.FillEllipse(b, x, rowY + 5, 9, 9);
                using (SolidBrush b = new SolidBrush(i == hovered ? MouseSliceColor(i) : Ctext))
                    g.DrawString(names[i], _fBody, b, x + 17, rowY);
                string pct = total > 0 ? (100f * vals[i] / total).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "--";
                string txt = Analysis.FmtCount(vals[i]) + "  ·  " + pct;
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(txt, _fBody, b, x + 17, rowY + 17);
                rowY += 44;
            }
        }

        private void PaintTopKeysMini(Graphics g, float x, float y, float w, float h)
        {
            // 与分布环共用同一套缓存机制(独立槽位):每帧重排全量历史在 25fps 下是最贵的一步。
            List<KeyValuePair<string, long>> rank = CachedKeyRanking(1, DateTime.MinValue);
            if (rank.Count == 0)
            {
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    SizeF sz = g.MeasureString("还没有键盘记录", _fBody);
                    g.DrawString("还没有键盘记录", _fBody, b, x + w / 2 - sz.Width / 2, y + h / 2 - 10);
                }
                return;
            }

            long max = rank[0].Value;
            // 1.8.0:颜色绑按键不绑排名。同一按键在任何区间都是同一个颜色,
            // 排名只由位置和条长表达;前六名做一次撞色去重。
            List<string> rankNames = new List<string>();
            int n = Math.Min(5, rank.Count);
            for (int i = 0; i < n; i++) rankNames.Add(rank[i].Key);
            Color[] cols = KeyPalette.ForRanking(rankNames);
            float rowH = h / 5f;
            for (int i = 0; i < n; i++)
            {
                float ry = y + i * rowH;
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString((i + 1).ToString(), _fAxis, b, x, ry + 7);
                using (SolidBrush b = new SolidBrush(Ctext))
                    g.DrawString(rank[i].Key, _fBody, b, x + 20, ry + 3);

                float barX = x + 78, barW = w - 78 - 78;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(22, Ctext)))
                    g.FillRectangle(b, barX, ry + rowH / 2 - 4, barW, 8);
                float frac = max > 0 ? (float)rank[i].Value / max : 0;
                using (SolidBrush b = new SolidBrush(cols[i % cols.Length]))
                    g.FillRectangle(b, barX, ry + rowH / 2 - 4, Math.Max(3, barW * frac), 8);

                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(Analysis.FmtCount(rank[i].Value), _fAxis, b, x + w - 66, ry + 7);
            }
        }

        private void PaintInsights(Graphics g, float x, float y, float w, float h)
        {
            List<string> ins = BuildInsights();
            string text = string.Join("      ·      ", ins.ToArray());
            PaintCardBase(g, x, y, w, h + 6);
            using (SolidBrush b = new SolidBrush(Csub))
                g.DrawString("洞察  " + text, _fBody, b, x + 16, y + 4);
        }

        private List<string> BuildInsights()
        {
            List<string> res = new List<string>();

            List<KeyValuePair<string, long>> rank = Analysis.KeyRanking(Store.History.Values);
            if (rank.Count > 0)
            {
                long total = Analysis.KeyTotal(Store.History.Values);
                double pct = total > 0 ? 100.0 * rank[0].Value / total : 0;
                res.Add(string.Format("最常用按键 {0}({1:0.#}%)", rank[0].Key, pct));
            }

            long[] hour = new long[24];
            long[] hk = Analysis.SumHourKeys(Store.History.Values);
            long[] hc = Analysis.SumHourClicks(Store.History.Values);
            for (int i = 0; i < 24; i++) hour[i] = hk[i] + hc[i];
            int peak = 0, active = 0;
            long peakV = 0;
            for (int i = 0; i < 24; i++)
            {
                if (hour[i] > peakV) { peakV = hour[i]; peak = i; }
                if (hour[i] > 0) active++;
            }
            if (peakV > 0)
                res.Add(string.Format("高峰时段 {0:00}:00–{1:00}:00", peak, (peak + 1) % 24));

            long days = 0;
            foreach (DayRecord r in Store.History.Values) if (!r.IsEmpty) days++;
            if (days > 1)
                res.Add(string.Format("日均击键 {0}", Analysis.FmtCount((long)(Store.Total.Keys / (double)days))));
            else
                res.Add("已记录 " + days + " 天");

            return res;
        }

        // ---------------------------------------------------------------- 标签页:趋势

        private void PaintTrend(Graphics g)
        {
            AppChip(g, 300, Cx + (_trendHourly ? 398 : 400), 98, _trendHourly ? 58 : 80, "按天", !_trendHourly);
            AppChip(g, 301, Cx + (_trendHourly ? 462 : 490), 98, _trendHourly ? 68 : 68, "按小时", _trendHourly);
            if (_trendHourly) { PaintHourlyTrend(g); return; }
            // 指标选择
            string[] metrics = { "击键", "点击", "滚轮", "鼠标路程" };
            float cx = Cx;
            for (int i = 0; i < metrics.Length; i++)
            {
                RectangleF r = new RectangleF(cx, 98, 88, 30);
                _chips.Add(r); _chipIds.Add(100 + i);
                PaintChip(g, r, metrics[i], _trendMetric == i, null);
                cx += 98;
            }
            // 时间范围(右对齐)
            string[] ranges = { "近14天", "近30天" };
            float rx = Cx + Cw - 2 * 88 - 4;
            for (int i = 0; i < 2; i++)
            {
                RectangleF r = new RectangleF(rx, 98, 88, 30);
                _chips.Add(r); _chipIds.Add(91 + i);
                PaintChip(g, r, ranges[i], _trendRange == (i == 0 ? 14 : 30), null);
                rx += 98;
            }
            AppChip(g, 93, Cx + 566, 98, 68, "对比上期", _trendCompare);

            List<AnalysisPt> pts = TrendPoints();
            PaintLineChart(g, new RectangleF(Cx, 150, Cw, 360), pts);
            AppChip(g, 302, Cx + 100, 154, 58, "分布", false);
            // 汇总卡:今天仍在累计,所以「日均」只算完整且有记录的天,「较昨日」改成「较昨日同期」。
            bool live = pts.Count > 0 && pts[pts.Count - 1].Day == DateTime.Today;
            double sum = 0, max = 0;
            foreach (AnalysisPt p in pts)
            {
                sum += p.V;
                if (p.V > max) max = p.V;
            }
            // 日均与图上平均线共用同一函数、同一兜底:没有完整日时给 NaN(卡片显示 --、不画平均线),
            // 不再退化成 sum/pts.Count 这种把区间总天数当分母的假均值。
            int completeCount;
            double avg = TrendAverage(pts, live, out completeCount);
            if (_trendCompare) PaintTrendCompare(g, new RectangleF(Cx, 150, Cw, 360), avg);
            TrendCutoff.Comparison compare = live
                ? TrendCutoff.Compare(Store.Today, Analysis.GetDay(DateTime.Today.AddDays(-1)), _trendMetric, DateTime.Now)
                : null;
            string delta;
            if (compare != null) delta = TrendCutoff.DeltaText(compare);
            else
            {
                double previous = pts.Count >= 2 ? pts[pts.Count - 2].V : 0;
                double current = pts.Count > 0 ? pts[pts.Count - 1].V : 0;
                if (previous == 0 && current == 0) delta = "--";
                else if (previous == 0) delta = "新增";
                else delta = (100.0 * (current - previous) / previous).ToString("+0;-0;0", CultureInfo.InvariantCulture) + "%";
            }

            string[] sTitles = { "期间合计", "日均·有记录日", "最高单日", "较昨日同期" };
            string[] sVals = {
                _trendMetric == 3 ? Analysis.FmtMeters(sum) : Analysis.FmtCount((long)sum),
                double.IsNaN(avg) ? "--" : _trendMetric == 3 ? Analysis.FmtMeters(avg) : Analysis.FmtCount((long)avg),
                _trendMetric == 3 ? Analysis.FmtMeters(max) : Analysis.FmtCount((long)max),
                delta };

            float tileW = (Cw - 3 * 16) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = Cx + i * (tileW + 16);
                PaintCardBase(g, x, 524, tileW, 62);
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(sTitles[i], _fSmall, b, x + 16, 532);
                Color ink = Ctext;
                if (i == 3 && compare != null)
                {
                    // 颜色跟着"显示出来的值"走:四舍五入成 0% 的极小波动不该染成红色。
                    double percent = compare.Percent.HasValue ? compare.Percent.Value : 0;
                    double shown = Math.Round(percent, MidpointRounding.AwayFromZero);
                    ink = !compare.Available || compare.NoInputToday || shown == 0 ? Csub : percent < 0 ? Cred : Cgreen;
                }
                using (SolidBrush b = new SolidBrush(ink))
                    g.DrawString(sVals[i], i == 3 ? DeltaValueFont(sVals[i]) : _fNum2, b, x + 16, 548);
                if (i == 3 && compare != null)
                    AppText(g, TrendCutoff.NoteText(compare), _fAxis, Csub, new RectangleF(x + 16, 571, tileW - 28, 12), false);
            }
        }

        /// <summary>
        /// 第 4 张卡的数值字体。19px 的数字墨迹低,不会压到下面那行说明;但「累计中 / 新增 /
        /// 今日尚无输入」这类中文会填满 19px 的字框,和说明叠在一起,所以状态词改用正文字号。
        /// </summary>
        private Font DeltaValueFont(string text)
        {
            if (text != null) for (int i = 0; i < text.Length; i++) if (text[i] > 0x2E80) return _fBody;
            return _fNum2;
        }

        private void PaintTrendCompare(Graphics g, RectangleF rect, double currentAverage)
        {
            DateTime end = DateTime.Today.AddDays(-_trendRange), start = end.AddDays(-(_trendRange - 1));
            double sum = 0; int count = 0;
            for (DateTime day = start; day <= end; day = day.AddDays(1))
            {
                DayRecord record = Analysis.GetDay(day);
                if (record == null || record.IsEmpty) continue;
                sum += TrendMetricValue(record); count++;
            }
            string text = count == 0 ? "较上期：无样本" : "较上期 " + PersonalStats.Change(currentAverage, sum / count);
            bool neutral = count == 0 || double.IsNaN(currentAverage);
            Color color = neutral ? Csub : currentAverage >= sum / count ? Cgreen : Cred;
            // 胶囊放在图例左侧同一行:原来贴在 rect.Right-196,不透明白底正好盖住右上角的
            // 「虚线：前 7 日均值」图例。不下移到绘图区,是为了不压住纵轴顶部与「今日截至」标签。
            RectangleF r = new RectangleF(rect.Right - 354, rect.Y + 11, 184, 24);
            using (GraphicsPath path = RoundedRect(r.X, r.Y, r.Width, r.Height, 12))
            using (SolidBrush fill = new SolidBrush(Ccard2)) g.FillPath(fill, path);
            using (Pen edge = new Pen(Cline))
            using (GraphicsPath path = RoundedRect(r.X, r.Y, r.Width, r.Height, 12)) g.DrawPath(edge, path);
            AppText(g, text, _fSmall, color, new RectangleF(r.X + 9, r.Y, r.Width - 18, r.Height), true);
        }
        private double TrendMetricValue(DayRecord day)
        {
            switch (_trendMetric)
            {
                case 1: return day.Clicks;
                case 2: return day.Wheel;
                case 3: return day.MoveMeters;
                default: return day.Keys;
            }
        }
        /// <summary>选中态胶囊的实际底色:美术主题自绘的是深色渐变底板,通用主题填 Accent。</summary>
        internal static Color ChipSelectedBackdrop()
        {
            if (EuroTruckArt.Active) return Color.FromArgb(74, 52, 22);
            if (GujianArt.Active) return Color.FromArgb(17, 68, 73);
            if (OriArt.Active) return Color.FromArgb(17, 74, 96);
            if (WuxiaArt.Active) return Color.FromArgb(93, 31, 27);
            return Cblue;
        }

        /// <summary>
        /// 在给定底色上挑对比度最高的字色。
        /// 旧实现用「简单亮度 ≥0.55 就取 OnAccent」这种固定阈值去猜,在边界主题上必翻车:
        /// 「生化危机」的 Accent #DA7773 简单亮度是 0.5481,差 0.0019 判成亮底,近白字压上去
        /// 只有 2.85:1。这里改成把 OnAccent / 近白 / 近黑三个候选各算一次真实 WCAG 对比度,
        /// 取最高者 —— 阈值没有了,边界也就不存在了。
        /// </summary>
        internal static Color BestInkOn(Color backdrop)
        {
            Color best = ArtTheme.Current.OnAccent;
            double bestRatio = ArtTheme.ContrastRatio(best, backdrop);
            Color[] candidates = { Color.FromArgb(245, 247, 250), Color.FromArgb(12, 14, 18) };
            foreach (Color candidate in candidates)
            {
                double ratio = ArtTheme.ContrastRatio(candidate, backdrop);
                if (ratio > bestRatio + 0.0001) { bestRatio = ratio; best = candidate; }
            }
            return best;
        }

        /// <summary>
        /// 选中态胶囊的字色:按真实底色择优,保证 13 套主题都 ≥4.5:1
        /// (见 tests/SegmentTrayTests 的全主题断言)。
        /// </summary>
        internal static Color ChipSelectedInk()
        {
            return BestInkOn(ChipSelectedBackdrop());
        }

        private void PaintChip(Graphics g, RectangleF r, string text, bool sel, string extra)
        {
            PointF mouse = ToBase(_mouse);
            bool hov = Hit(r, new PointF(mouse.X - ContentX, mouse.Y - ContentY));
            if(GujianArt.Active)GujianArt.Button(g,r,sel,hov);
            else if(OriArt.Active)OriArt.Button(g,r,sel,hov);
            else if(EuroTruckArt.Active)EuroTruckArt.Button(g,r,sel,hov);
            else if(WuxiaArt.Active)WuxiaArt.Button(g,r,sel,hov);
            else
            using (GraphicsPath p = RoundedRect(r.X, r.Y, r.Width, r.Height, ToggleChipRadius))
            {
                using (SolidBrush b = new SolidBrush(sel ? Cblue : hov ? Ccard2 : Ccard)) g.FillPath(b, p);
                using (Pen pen = new Pen(sel ? Cblue : Cline)) g.DrawPath(pen, p);
            }
            // 未选中字色统一走 FilterInkColor():纯色主题下它就是 Csub(与旧版逐像素一致),
            // 美术主题下自动抬到 214/255 的 Ctext —— 否则 Csub 压在美术底板上只有 1.3~1.9:1。
            using (SolidBrush b = new SolidBrush(sel ? ChipSelectedInk() : hov ? Ctext : FilterInkColor()))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, _fChip, b, r, format);
        }

        /// <summary>
        /// 标准 ToggleChip:独立一颗、h30、r8、Ccard 底板 + Cline 描边,选中用 ChipSelectedInk()。
        /// 统一走这里 = 同时解决「形状不一致」和「键盘 Tab 摸不到」两件事:登记命中区在绘制处完成。
        /// </summary>
        internal void PaintToggleChip(Graphics g, RectangleF r, string text, bool selected, int id)
        {
            _chips.Add(r);
            _chipIds.Add(id);
            PaintChip(g, r, text, selected, null);
        }

        /// <summary>步进按钮到端点/边界时的字色。与时段分布连续段箭头同一档灰。</summary>
        internal static Color StepDisabledInk { get { return Color.FromArgb(84, Csub); } }

        /// <summary>
        /// 文字步进按钮(前一天 / 回今天 / 后一天 / 上一页 / 下一页)的统一画法:
        /// 与 ToggleChip 同一套几何(h30 / r8 / Ccard / Cline)。唯一的区别是多一个禁用态 ——
        /// 到边界时灰字且【不登记命中区】,不会出现「看起来能点、点了安静地什么都不发生」。
        /// </summary>
        internal void PaintStepChip(Graphics g, RectangleF r, string text, bool enabled, int id)
        {
            bool hover = enabled && Hit(r, ContentMouse());
            if (enabled) { _chips.Add(r); _chipIds.Add(id); }
            using (GraphicsPath path = RoundedRect(r.X, r.Y, r.Width, r.Height, StepChipRadius))
            {
                using (SolidBrush fill = new SolidBrush(hover ? Ccard2 : Ccard)) g.FillPath(fill, path);
                using (Pen edge = new Pen(Cline)) g.DrawPath(edge, path);
            }
            Color ink = !enabled ? StepDisabledInk : hover ? Ctext : FilterInkColor();
            using (SolidBrush brush = new SolidBrush(ink))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, _fChip, brush, r, format);
        }

        /// <summary>
        /// 箭头步进按钮(日报 &lt; &gt;):与文字步进同一几何、同一禁用语义,只画雪佛龙。
        /// 返回是否登记了命中区,方便调用方断言。
        /// </summary>
        internal bool PaintStepArrow(Graphics g, RectangleF r, bool right, bool enabled, int id)
        {
            bool hover = enabled && Hit(r, ContentMouse());
            if (enabled) { _chips.Add(r); _chipIds.Add(id); }
            using (GraphicsPath path = RoundedRect(r.X, r.Y, r.Width, r.Height, StepChipRadius))
            {
                using (SolidBrush fill = new SolidBrush(hover ? Ccard2 : Ccard)) g.FillPath(fill, path);
                using (Pen edge = new Pen(Cline)) g.DrawPath(edge, path);
            }
            Color ink = !enabled ? StepDisabledInk : hover ? Ctext : FilterInkColor();
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            using (Pen pen = new Pen(ink, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (right)
                    g.DrawLines(pen, new[] { new PointF(cx - 2, cy - 5), new PointF(cx + 3, cy), new PointF(cx - 2, cy + 5) });
                else
                    g.DrawLines(pen, new[] { new PointF(cx + 2, cy - 5), new PointF(cx - 3, cy), new PointF(cx + 2, cy + 5) });
            }
            return enabled;
        }

        private void PaintLineChart(Graphics g, RectangleF rect, List<AnalysisPt> pts)
        {
            // 标题固定 4 字(与「分布」chip 不打架);鼠标路程的估算口径与单位挪到副标题行。
            PaintCard(g, rect.X, rect.Y, rect.Width, rect.Height,
                _trendMetric == 3 ? "每日鼠标路程" :
                string.Format("每日{0}", _trendMetric == 0 ? "击键" : _trendMetric == 1 ? "点击" : "滚轮"));
            if(_trendMetric==0)AppText(g,"橙圈：历史高值 ≥2σ · 今日不标异常",_fSmall,Corange,new RectangleF(rect.X+190,rect.Y+12,400,22),false);
            else if(_trendMetric==3)AppText(g,"估算 · 纵轴单位 m",_fSmall,Csub,new RectangleF(rect.X+190,rect.Y+12,400,22),false);

            float left = rect.X + 56, right = rect.X + rect.Width - 16;
            float top = rect.Y + 44, bottom = rect.Y + rect.Height - 30;
            float ph = bottom - top, pw = right - left;

            double rawMax = 10;
            foreach (AnalysisPt p in pts) if (p.V > rawMax) rawMax = p.V;
            // 7 日均线取 [d-7, d-1],与 Tooltip 的「较前 7 日均值」同一窗口;
            // 坐标上限也必须用同一条曲线,否则会为了一个画不出来的点抬高纵轴。
            foreach (AnalysisPt p in pts) { double moving = PersonalStats.MovingAverage(p.Day.AddDays(-1), _trendMetric); if (!double.IsNaN(moving) && moving > rawMax) rawMax = moving; }
            double max, stepY;
            AxisScale.Nice(rawMax, 4, _trendMetric != 3, out max, out stepY);

            // 网格 + y 标签:刻度对齐到整值(4k / 8k / 12k / 16k),不是「最大值 ÷ 4」。
            for (int i = 0; i <= 4; i++)
            {
                float gy = bottom - ph * i / 4f;
                using (Pen pen = new Pen(i == 0 ? Color.FromArgb(30, Ctext) : Color.FromArgb(13, Ctext)))
                {
                    if (i != 0) pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, left, gy, right, gy);
                }
                double v = stepY * i;
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    string t = _trendMetric == 3 ? v.ToString("0.#", CultureInfo.InvariantCulture) : AxisScale.Label(v);
                    SizeF sz = g.MeasureString(t, _fAxis);
                    g.DrawString(t, _fAxis, b, left - sz.Width - 6, gy - sz.Height / 2);
                }
            }

            if (pts.Count == 0) return;

            Color main = _trendMetric == 3 ? Cpurple : _trendMetric == 1 ? Cgreen : _trendMetric == 2 ? Corange : Cblue;
            // 今日的点是「进行中」:实线只画到昨天,最后一段用虚线接上,面积也不铺到今天。
            bool live = pts[pts.Count - 1].Day == DateTime.Today;
            int lastComplete = live ? pts.Count - 2 : pts.Count - 1;

            Func<int, float> px = delegate(int i)
            {
                return pts.Count > 1 ? left + pw * i / (pts.Count - 1) : left + pw / 2;
            };
            Func<double, float> py = delegate(double v)
            {
                return bottom - (float)(v / max) * ph;
            };

            // 未运行/无记录的日子:画一条浅灰竖带并在底部标「未运行」,折线在这些日子断开。
            int missingStart = -1;
            for (int i = 0; i <= pts.Count; i++)
            {
                bool missing = i < pts.Count && !pts[i].HasData;
                if (missing) { if (missingStart < 0) missingStart = i; continue; }
                if (missingStart < 0) continue;
                int missingEnd = i - 1;
                float x0 = missingStart == 0 ? left : (px(missingStart - 1) + px(missingStart)) / 2f;
                float x1 = missingEnd == pts.Count - 1 ? right : (px(missingEnd) + px(missingEnd + 1)) / 2f;
                using (SolidBrush band = new SolidBrush(Color.FromArgb(14, Csub)))
                    g.FillRectangle(band, x0, top, Math.Max(1, x1 - x0), ph);
                if (x1 - x0 >= 42)
                {
                    string label = "未运行";
                    SizeF ls = g.MeasureString(label, _fAxis);
                    using (SolidBrush ink = new SolidBrush(Color.FromArgb(150, Csub)))
                        g.DrawString(label, _fAxis, ink, x0 + (x1 - x0 - ls.Width) / 2, bottom - ls.Height - 2);
                }
                missingStart = -1;
            }

            // 平均线:与「日均·有记录日」卡片共用 TrendAverage;没有完整日就直接不画,
            // 不再退化成 pts[0].V 这种把第一天当均值的假线。
            int avgCount;
            double avg = TrendAverage(pts, live, out avgCount);
            if (!double.IsNaN(avg))
            {
                using (Pen pen = new Pen(Color.FromArgb(90, Ctext)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, left, py(avg), right, py(avg));
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(140, Csub)))
                    g.DrawString("平均 " + (_trendMetric == 3 ? Analysis.FmtMeters(avg) : Analysis.FmtAxis(avg)),
                        _fAxis, b, right - 70, py(avg) - 16);
            }

            // 面积:只铺完整日,并按「有记录」的连续段切开,未运行的日子不填充。
            if (lastComplete >= 0)
            {
                try
                {
                    using (LinearGradientBrush fill = new LinearGradientBrush(
                        new RectangleF(left, top, pw, ph), Color.FromArgb(38, main), Color.FromArgb(0, main), 90f))
                    {
                        int areaStart = -1;
                        for (int i = 0; i <= lastComplete + 1; i++)
                        {
                            bool inside = i <= lastComplete && pts[i].HasData;
                            if (inside) { if (areaStart < 0) areaStart = i; continue; }
                            if (areaStart < 0) continue;
                            int areaEnd = i - 1;
                            if (areaEnd > areaStart)
                            {
                                using (GraphicsPath area = new GraphicsPath())
                                {
                                    area.AddLine(px(areaStart), bottom, px(areaStart), py(pts[areaStart].V));
                                    for (int j = areaStart + 1; j <= areaEnd; j++)
                                        area.AddLine(px(j - 1), py(pts[j - 1].V), px(j), py(pts[j].V));
                                    area.AddLine(px(areaEnd), bottom, px(areaStart), bottom);
                                    g.FillPath(fill, area);
                                }
                            }
                            areaStart = -1;
                        }
                    }
                }
                catch { }
            }

            // 折线。1.8.0:入场时从基线生长(悬停与坐标轴仍用真实位置)。
            // 缺失/空壳日两侧不连线;今天的最后一段单独用虚线。
            double grow = Entrance();
            using (Pen pen = new Pen(main, 2f))
            {
                pen.LineJoin = LineJoin.Round;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (!TrendSegmentDrawn(pts[i].HasData, pts[i + 1].HasData, live, i + 1, pts.Count)) continue;
                    g.DrawLine(pen, px(i), py(pts[i].V) + (bottom - py(pts[i].V)) * (float)(1 - grow),
                        px(i + 1), py(pts[i + 1].V) + (bottom - py(pts[i + 1].V)) * (float)(1 - grow));
                }
            }
            if (live && lastComplete >= 0 && pts[lastComplete].HasData && pts[pts.Count - 1].HasData)
            using (Pen dashed = new Pen(Color.FromArgb(215, main), 2f))
            {
                dashed.DashStyle = DashStyle.Dash;
                g.DrawLine(dashed, px(lastComplete), py(pts[lastComplete].V),
                    px(pts.Count - 1), py(pts[pts.Count - 1].V));
            }

            // 前 7 日均值:[d-7, d-1] 七个完整日,与 Tooltip 的「较前 7 日均值」同一窗口,
            // 也不会把今天未完成的半天算进末点。缺失日仍然整点断开(History 只收 7/7 齐的天)。
            using (Pen movingPen = new Pen(Ccyan, 2f) { DashStyle = DashStyle.Dash })
            {
                double previousMean = double.NaN;
                for (int i = 0; i < pts.Count; i++)
                {
                    double moving = PersonalStats.MovingAverage(pts[i].Day.AddDays(-1), _trendMetric);
                    if (i > 0 && !double.IsNaN(previousMean) && !double.IsNaN(moving)) g.DrawLine(movingPen, px(i - 1), py(previousMean), px(i), py(moving));
                    previousMean = moving;
                }
            }
            using (SolidBrush legend = new SolidBrush(Ccyan)) g.DrawString("虚线：前 7 日均值", _fSmall, legend, rect.Right - 162, rect.Y + 12);

            // x 轴日期:从最后一个点往回按步长取,保证今天一定有标签,又不会出现两个相邻标签叠字。
            int step = Math.Max(1, (int)Math.Ceiling(pts.Count / 8.0));
            using (SolidBrush b = new SolidBrush(Csub))
            {
                for (int i = pts.Count - 1; i >= 0; i -= step)
                {
                    string t = pts[i].Day.ToString("MM-dd");
                    g.DrawString(t, _fAxis, b, px(i) - 14, bottom + 6);
                }
            }

            // 数据点(同样随入场从基线升起);未运行的日子不画点,由灰带表示。
            for (int i = 0; i < pts.Count; i++)
            {
                if (!pts[i].HasData) continue;
                bool isToday = pts[i].Day == DateTime.Today;
                float d = isToday ? 4.5f : 2.8f;
                float cy = py(pts[i].V) + (bottom - py(pts[i].V)) * (float)(1 - grow);
                using (SolidBrush b = new SolidBrush(isToday ? main : Color.FromArgb(160, main)))
                    g.FillEllipse(b, px(i) - d, cy - d, d * 2, d * 2);
                if(_trendMetric==0&&DailySignal.Get(pts[i].Day).High)using(Pen anomaly=new Pen(Corange,2))g.DrawEllipse(anomaly,px(i)-7,cy-7,14,14);
            }

            if(ThemeArt.Active && pts.Count>0)
            {
                int champion=0;for(int i=1;i<pts.Count;i++)if(pts[i].V>pts[champion].V)champion=i;
                if(pts[champion].V>0)ThemeArt.Sticker(g,2,new RectangleF(Math.Max(left,Math.Min(right-30,px(champion)-15)),Math.Max(rect.Y+30,py(pts[champion].V)-32),30,30));
            }

            // 末端标注:今天还没结束。贴着最后一个点放,放不下就翻到点的另一侧。
            if (live && pts.Count > 0 && pts[pts.Count - 1].HasData)
            {
                int last = pts.Count - 1;
                string tag = TrendCutoff.LiveTag(DateTime.Now);
                SizeF size = g.MeasureString(tag, _fSmall);
                float tw = size.Width + 16, th = 20;
                float tx = px(last) + 12;
                if (tx + tw > right) tx = px(last) - 12 - tw;
                tx = Math.Max(left, Math.Min(right - tw, tx));
                float ty = py(pts[last].V) - th - 8;
                if (ty < top) ty = Math.Min(bottom - th, py(pts[last].V) + 10);
                using (GraphicsPath path = RoundedRect(tx, ty, tw, th, 6))
                {
                    using (SolidBrush background = new SolidBrush(Ccard2)) g.FillPath(background, path);
                    using (Pen border = new Pen(Cline)) g.DrawPath(border, path);
                }
                using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(tag, _fSmall, b, tx + 8, ty + 4);
            }

            // 悬停提示
            if (_hover >= 0 && _hover < pts.Count)
            {
                AnalysisPt hp = pts[_hover];
                bool hoverToday = hp.Day == DateTime.Today;
                float hx = px(_hover), hy = py(hp.V);
                using (Pen pen = new Pen(Color.FromArgb(70, Ctext)))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawLine(pen, hx, top, hx, bottom);
                }
                // 无记录日只保留竖向参考线:以前还会在基线画空心圈+实心点,看起来像「这天是 0」。
                if (hp.HasData)
                {
                    using(Pen ring=new Pen(Ctext,2))g.DrawEllipse(ring,hx-8,hy-8,16,16);
                    using (SolidBrush b = new SolidBrush(main))
                        g.FillEllipse(b, hx - 4.5f, hy - 4.5f, 9, 9);
                }

                string l1 = hp.Day.ToString("MM-dd") + (hoverToday ? " · 进行中" : hp.HasData ? " · 点击查看日报" : " · 未运行");
                string l2 = !hp.HasData
                    ? "无记录"
                    : _trendMetric == 3
                        ? Analysis.FmtMeters(hp.V)
                        : Analysis.FmtCount((long)hp.V) + " " + (_trendMetric == 0 ? "击键" : _trendMetric == 1 ? "点击" : "滚动");
                string l3 = hoverToday
                    ? "截至 " + DateTime.Now.ToString("HH:mm") + "，不要与整日比较"
                    : !hp.HasData
                        ? (Store.History.ContainsKey(hp.Day)
                            ? "这天有条目但没有任何输入，按无记录处理"
                            : "应用或电脑没有运行，这一天没有落盘记录")
                        : hp.V <= 0
                            ? "这天有记录，但没有" + (_trendMetric == 0 ? "击键" : _trendMetric == 1 ? "点击" : _trendMetric == 2 ? "滚轮" : "鼠标移动")
                            : (_trendMetric == 0 ? DailySignal.Get(hp.Day).Note : "");
                SizeF s1 = g.MeasureString(l1, _fSmall);
                SizeF s2 = g.MeasureString(l2, _fBody);
                float bw = Math.Max(Math.Max(s1.Width, s2.Width),g.MeasureString(l3,_fSmall).Width) + 20;
                float bx = hx + 12; if (bx + bw > right) bx = hx - 12 - bw;
                bx=Math.Max(left,Math.Min(right-bw,bx));
                float by = hy - 72; if (by < top) by = top;
                using (GraphicsPath bp = RoundedRect(bx, by, bw, l3.Length>0?66:46, 8))
                {
                    using (SolidBrush b = new SolidBrush(Ccard2))
                        g.FillPath(b, bp);
                    using (Pen pen = new Pen(Color.FromArgb(50, Ctext)))
                        g.DrawPath(pen, bp);
                }
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(l1, _fSmall, b, bx + 10, by + 5);
                using (SolidBrush b = new SolidBrush(Ctext))
                    g.DrawString(l2, _fBody, b, bx + 10, by + 21);
                if(l3.Length>0)using(SolidBrush b=new SolidBrush(Corange))g.DrawString(l3,_fSmall,b,bx+10,by+43);
            }
        }

        // ---------------------------------------------------------------- 标签页:时段分布

        private void PaintHours(Graphics g)
        {
            string[] metrics = { "击键", "点击", "活跃时长" };
            string[] views = { "柱子", "矩阵", "连续段" };
            string[] ranges = { "今日", "近 7 天", "历史" };
            PaintSegmentGroup(g, Cx, 98, 216, metrics, new[] { 80, 81, 82 }, _hourMetric, false);
            PaintSegmentGroup(g, Cx + 232, 98, 288, views, new[] { 203, 204, 205 }, _hourView, true);
            // 连续段画的是 00:00–24:00 的单日时间轴:「今日 / 近 7 天 / 历史」在这里只会误导
            // (三档最终都落到"最新记录日",切了也不动)。换成日期切换器,把"看哪一天"变成
            // 显式、可翻页的选择,同时这一步先走掉,省下 24 小时柱子的无用计算。
            if (_hourView == 2)
            {
                List<DayRecord> records = IntensityDays();
                int index = IntensityIndex(records);
                PaintHourDaySwitcher(g, records, index);
                PaintHourIntensity(g, index < 0 ? null : records[index]);
                return;
            }
            PaintSegmentGroup(g, Cx + Cw - 210, 98, 210, ranges, new[] { 30, 31, 32 }, _hourRange, false);

            IEnumerable<DayRecord> days = HourDays();
            long[] vals;
            if (_hourMetric == 2)
            {
                double[] seconds = new double[24];
                foreach (DayRecord day in days) for (int h = 0; h < 24; h++) seconds[h] += day.ActiveHours[h];
                vals = new long[24];
                for (int h = 0; h < 24; h++) vals[h] = (long)Math.Round(seconds[h]);
            }
            else vals = _hourMetric == 0 ? Analysis.SumHourKeys(days) : Analysis.SumHourClicks(days);

            // 柱子与矩阵二选一:原来柱子图上方还有一条 24 格热力条,和柱子表达同一份一维数据,
            // 既没有刻度又稀释信息密度;热力配色直接融进柱高,矩阵换成「周 × 24 小时」才有新信息。
            HourProfile.Result profile = null;
            if (_hourView == 1)
            {
                DateTime first, last;
                if (_hourRange == 0) { first = DateTime.Today; last = DateTime.Today; }
                else { first = _hourRange == 1 ? DateTime.Today.AddDays(-7) : HourRangeStart(); last = DateTime.Today.AddDays(-1); }
                profile = HourProfile.BuildRange(Store.History.Values, _hourMetric, first, last);
                PaintHourMatrix(g, new RectangleF(Cx, 150, Cw, 350), profile);
            }
            else
            {
                string rangeName = HourRangeName();
                PaintCard(g, Cx, 150, Cw, 350,
                    _hourMetric == 2 ? "各时段活跃时长 · " + rangeName + " · 纵轴为分钟 · 颜色越亮越活跃"
                        : _hourMetric == 0 ? "各时段击键分布 · " + rangeName + " · 颜色越亮越活跃"
                        : "各时段点击分布 · " + rangeName + " · 颜色越亮越活跃");
                PaintHourBars(g, new RectangleF(Cx, 150, Cw, 350), vals);
            }

            // 统计卡:矩阵视角换成「工作日 vs 周末」的对照,不再复述全天分布。
            if (_hourView == 1)
            {
                if (profile != null && profile.HasData)
                {
                    int bestRow = 0, bestCol = 0;
                    double workPeak = 0, weekendPeak = 0;
                    for (int row = 0; row < 7; row++)
                        for (int col = 0; col < 24; col++)
                        {
                            double value = profile.Average[row, col];
                            if (value > profile.Average[bestRow, bestCol]) { bestRow = row; bestCol = col; }
                            if (row < 5) { if (value > workPeak) workPeak = value; }
                            else if (value > weekendPeak) weekendPeak = value;
                        }
                    string ratio = workPeak > 0 ? (weekendPeak / workPeak * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "--";
                    PaintHourStatCards(g,
                        new[] { "最活跃格子", "工作日峰值", "周末峰值", "周末/工作日" },
                        new[] { HourWeekdays[bestRow] + " " + bestCol.ToString("00") + " 点", HourMatrixValue(workPeak), HourMatrixValue(weekendPeak), ratio });
                }
                else
                {
                    PaintHourStatCards(g,
                        new[] { "最活跃格子", "工作日峰值", "周末峰值", "周末/工作日" },
                        new[] { "--", "--", "--", "--" });
                }
                return;
            }

            // 统计卡
            int peak = 0; long peakV = 0; int active = 0; long sum = 0;
            for (int i = 0; i < 24; i++)
            {
                sum += vals[i];
                if (vals[i] > peakV) { peakV = vals[i]; peak = i; }
                if (vals[i] > 0) active++;
            }
            long nightSum = 0;
            for (int i = 0; i < 6; i++) nightSum += vals[i];
            string nightPct = sum > 0 ? (100.0 * nightSum / sum).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "--";

            string[] t1 = { "最忙时段", "活跃小时", "峰值/小时", "深夜占比 0-6点" };
            if (_hourMetric == 2) { t1[1] = "有活动时段"; t1[2] = "峰值时段时长"; }
            string[] t2 = {
                peakV > 0 ? string.Format("{0:00}:00–{1:00}:00", peak, (peak + 1) % 24) : "--",
                active + (_hourMetric == 2 ? " 个" : " 小时"),
                _hourMetric == 2 ? ActivityMonitor.FormatDuration(peakV) : Analysis.FmtCount(peakV),
                nightPct };
            PaintHourStatCards(g, t1, t2);
        }

        // ---------------------------------------------------------------- 连续段:单日翻页

        /// <summary>筛选胶囊未选中时的字色。美术主题下 Csub 是按深色底调的中灰,压在亮原画上只有
        /// 1.3~1.9:1;换成同色相的 214/255 亮色后,叠在近不透明底板上实测最差 6.3:1(暖暖),
        /// 其余美术主题 ≥7.3:1(见 tests/SegmentTrayTests)。</summary>
        internal static Color FilterInkColor()
        {
            int alpha = ArtTheme.FilterInkAlpha;
            return alpha >= 255 ? Csub : Color.FromArgb(alpha, Ctext);
        }

        /// <summary>鼠标在内容坐标系里的位置。胶囊与图表的命中测试统一走这里,别再各写一遍减法。</summary>
        private PointF ContentMouse()
        {
            PointF p = ToBase(_mouse);
            return new PointF(p.X - ContentX, p.Y - ContentY);
        }

        /// <summary>当前时段指标的中文叫法:标题、页脚、悬停气泡共用一套词。</summary>
        private string HourMetricName()
        {
            return _hourMetric == 2 ? "活跃时长" : _hourMetric == 1 ? "点击" : "击键";
        }

        /// <summary>日期标签:跨年时补年份,否则历史档里 2025-12-03 和 2026-12-03 长得一样。</summary>
        private static string HourRecordLabel(DateTime date)
        {
            return date.Year == DateTime.Today.Year ? date.ToString("MM-dd") : date.ToString("yyyy-MM-dd");
        }

        /// <summary>
        /// 连续段可翻页的记录日(升序)。优先只收"确实有连续段"的日子:有键鼠但没段的老格式日
        /// 会让翻页停在空图上;一天都没有段时才退回"非空日",至少还能看到日期。
        /// </summary>
        internal static List<DayRecord> IntensityDays()
        {
            List<DayRecord> all = new List<DayRecord>();
            List<DayRecord> withSessions = new List<DayRecord>();
            foreach (DayRecord day in Store.History.Values)
            {
                if (day == null || day.IsEmpty) continue;
                all.Add(day);
                if (day.Sessions != null && day.Sessions.Count > 0) withSessions.Add(day);
            }
            List<DayRecord> list = withSessions.Count > 0 ? withSessions : all;
            list.Sort(delegate(DayRecord a, DayRecord b) { return a.Date.CompareTo(b.Date); });
            return list;
        }

        /// <summary>当前记录日在列表里的下标;-1 表示一天可画的都没有。
        /// _hourRecordDate 为 MinValue 时跟随最新记录日(默认),翻页后固定在所选日。</summary>
        private int IntensityIndex(List<DayRecord> days)
        {
            if (days == null || days.Count == 0) return -1;
            List<DateTime> dates = new List<DateTime>(days.Count);
            foreach (DayRecord day in days) dates.Add(day.Date.Date);
            return ResolveIntensityIndex(dates, _hourRecordDate);
        }

        /// <summary>
        /// 纯函数版:在升序记录日里定位"当前该画哪一天"。抽出来是为了让翻页边界
        /// (默认最新 / 翻到最早 / 钉住的日期被裁掉之后退回最新)有可回归的断言。
        /// </summary>
        internal static int ResolveIntensityIndex(IList<DateTime> dates, DateTime pinned)
        {
            if (dates == null || dates.Count == 0) return -1;
            if (pinned != DateTime.MinValue)
                for (int i = dates.Count - 1; i >= 0; i--)
                    if (dates[i].Date == pinned.Date) return i;
            return dates.Count - 1;
        }

        /// <summary>连续段翻页:-1 前一天 / +1 后一天 / 0 回到最新记录日。</summary>
        private void StepHourRecordDay(int delta)
        {
            List<DayRecord> days = IntensityDays();
            int index = IntensityIndex(days);
            _hover = -1;
            if (index < 0 || delta == 0) { _hourRecordDate = DateTime.MinValue; return; }
            index = Math.Max(0, Math.Min(days.Count - 1, index + delta));
            _hourRecordDate = days[index].Date.Date;
        }

        /// <summary>连续段的日期切换器:‹ 上一日 | MM-dd | 下一日 ›;点中间的日期回到最新记录日。
        /// 已经是「跟随最新」时中间那颗不登记命中区,与端点箭头同一原则:不能点了没有任何反应。</summary>
        private void PaintHourDaySwitcher(Graphics g, List<DayRecord> days, int index)
        {
            DayRecord record = index < 0 ? null : days[index];
            float x = Cx + Cw - 210, y = 98, width = 210, height = 32, pad = 3, gap = 3;
            float chipH = height - pad * 2, arrow = 30;
            float label = width - pad * 2 - gap * 2 - arrow * 2;
            PaintSegmentTray(g, x, y, width);
            RectangleF prev = new RectangleF(x + pad, y + pad, arrow, chipH);
            RectangleF pick = new RectangleF(prev.Right + gap, y + pad, label, chipH);
            RectangleF next = new RectangleF(pick.Right + gap, y + pad, arrow, chipH);
            PaintHourDayArrow(g, prev, -1, index > 0, 33);
            PaintHourDayArrow(g, next, 1, index >= 0 && index < days.Count - 1, 34);

            bool pinned = record != null && _hourRecordDate != DateTime.MinValue;
            bool hover = pinned && Hit(pick, ContentMouse());
            if (hover)
            {
                using (GraphicsPath path = RoundedRect(pick.X, pick.Y, pick.Width, pick.Height, 7))
                using (SolidBrush fill = new SolidBrush(Ccard2)) g.FillPath(fill, path);
            }
            if (pinned) { _chips.Add(pick); _chipIds.Add(35); }
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (SolidBrush brush = new SolidBrush(record == null ? Csub : hover ? Ctext : FilterInkColor()))
                g.DrawString(record == null ? "无记录" : HourRecordLabel(record.Date), _fChip, brush, pick, format);
        }

        /// <summary>切换器箭头。到端点时灰化且不登记命中区,不会"点了安静地什么都不发生"。</summary>
        private void PaintHourDayArrow(Graphics g, RectangleF chip, int direction, bool enabled, int id)
        {
            bool hover = enabled && Hit(chip, ContentMouse());
            if (enabled) { _chips.Add(chip); _chipIds.Add(id); }
            if (hover)
            {
                using (GraphicsPath path = RoundedRect(chip.X, chip.Y, chip.Width, chip.Height, 7))
                using (SolidBrush fill = new SolidBrush(Ccard2)) g.FillPath(fill, path);
            }
            Color ink = !enabled ? StepDisabledInk : hover ? Ctext : FilterInkColor();
            float cx = chip.X + chip.Width / 2f, cy = chip.Y + chip.Height / 2f;
            using (Pen pen = new Pen(ink, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (direction < 0)
                    g.DrawLines(pen, new[] { new PointF(cx + 2, cy - 5), new PointF(cx - 3, cy), new PointF(cx + 2, cy + 5) });
                else
                    g.DrawLines(pen, new[] { new PointF(cx - 2, cy - 5), new PointF(cx + 3, cy), new PointF(cx - 2, cy + 5) });
            }
        }

        /// <summary>
        /// 单日连续段强度:横轴 00:00–24:00,每段一根柱;柱高与颜色 = 该段的输入强度,按**当日**最强段归一
        /// (不跨日比较)。强度口径跟随顶部指标:击键 APM / 点击 CPM / 活跃时长(段长分钟)。
        /// </summary>
        private void PaintHourIntensity(Graphics g, DayRecord record)
        {
            _segmentBars.Clear();
            _hoverDetail = "";
            if (record == null)
            {
                PaintCard(g, Cx, 150, Cw, 350, "连续段强度 · 单日时间轴");
                AppText(g, "还没有任何连续使用段记录", _fBody, Csub, new RectangleF(Cx + 24, 220, Cw - 48, 24), false);
                PaintHourStatCards(g, new[] { "连续段", "平均段长", "最长段", "短段占比 (<5m)" }, new[] { "--", "--", "--", "--" });
                return;
            }
            PaintCard(g, Cx, 150, Cw, 350,
                "连续段强度 · " + HourRecordLabel(record.Date) + " · " + HourMetricName() + "强度 · 柱高按当日峰值归一");
            float left = Cx + 36, right = Cx + Cw - 18, top = 196, bottom = 352, track = right - left;
            using (Pen grid = new Pen(Color.FromArgb(24, Ctext), 1f) { DashStyle = DashStyle.Dash })
            {
                for (int h = 0; h <= 24; h += 2) { float x = left + track * h / 24f; g.DrawLine(grid, x, top, x, bottom); }
                g.DrawLine(grid, left, bottom, right, bottom);
            }
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            using (SolidBrush axis = new SolidBrush(Color.FromArgb(232, Ctext)))
                for (int h = 0; h <= 24; h += 6)
                    g.DrawString(h.ToString("00") + ":00", _fBody, axis, new RectangleF(left + track * h / 24f - 24, bottom + 6, 48, 20), center);

            List<ActiveSession> sessions = new List<ActiveSession>();
            foreach (ActiveSession session in record.Sessions) if (session.Seconds > 0) sessions.Add(session);
            double[] rates = new double[sessions.Count];
            double[] keys = new double[sessions.Count];
            double[] clicks = new double[sessions.Count];
            // 归一化下限跟随指标单位:击键/点击用 1 次/分兜底;活跃时长用"1 秒"折成分钟兜底,
            // 否则当天最长段不足 1 分钟时,柱高永远顶不满,"按当日峰值归一"就失真了。
            double maxRate = _hourMetric == 2 ? 1.0 / 60.0 : 1;
            for (int i = 0; i < sessions.Count; i++)
            {
                ActiveSession session = sessions[i];
                // 活跃时长口径不需要击键/点击摊派,直接跳过 24 次重叠计算。
                if (_hourMetric != 2)
                {
                    for (int h = 0; h < 24; h++)
                    {
                        DateTime hs = record.Date.AddHours(h), he = hs.AddHours(1);
                        DateTime from = session.Start > hs ? session.Start : hs, to = session.End < he ? session.End : he;
                        double overlap = (to - from).TotalSeconds; if (overlap <= 0) continue;
                        if (record.ActiveHours[h] > 0)
                        {
                            keys[i] += record.HourKeys[h] * overlap / record.ActiveHours[h];
                            clicks[i] += record.HourClicks[h] * overlap / record.ActiveHours[h];
                        }
                    }
                }
                // 指标分流:0 击键 APM / 1 点击 CPM / 2 活跃时长(段长分钟数本身就是强度)。
                rates[i] = _hourMetric == 2
                    ? session.Seconds / 60.0
                    : session.Seconds > 0 ? (_hourMetric == 1 ? clicks[i] : keys[i]) / session.Seconds * 60 : 0;
                maxRate = Math.Max(maxRate, rates[i]);
            }
            RectangleF[] bars = new RectangleF[sessions.Count];
            for (int i = 0; i < sessions.Count; i++)
            {
                ActiveSession session = sessions[i];
                double startSeconds = Math.Max(0, (session.Start - record.Date).TotalSeconds);
                double endSeconds = Math.Min(86400, (session.End - record.Date).TotalSeconds);
                float x = left + (float)(startSeconds / 86400 * track);
                float w = Math.Max(2, (float)(endSeconds / 86400 * track) - (float)(startSeconds / 86400 * track));
                float ratio = (float)Math.Max(0, Math.Min(1, rates[i] / maxRate));
                // 零强度段只留 4px 的"占位尾巴":原来的 12px 底座会让"这段没输入"看起来像有活动。
                float height = ratio <= 0 ? 4 : 6 + ratio * 76;
                // 颜色从"贴近卡片底色的暗青"连续插值到主题青:强度越高越饱和、越亮。
                // 起点用 Composite(Ccyan, 64, Ccard) 而不是带 alpha 的 FromArgb(90, Csub) ——
                // Mix 只取 RGB、会丢掉 alpha,那样写等于直接把 Csub 当起点(浅色主题下低强度反而更亮)。
                Color low = ArtTheme.Composite(Ccyan, 64, Ccard);
                Color color = ArtTheme.Mix(low, Ccyan, (float)Math.Sqrt(ratio));
                bars[i] = new RectangleF(x, bottom - height, w, height);
                using (GraphicsPath path = RoundedRect(x, bottom - height, w, height, 3))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(i == _hover ? 250 : 215, color))) g.FillPath(fill, path);
                if (i == _hover)
                    using (Pen edge = new Pen(Color.FromArgb(200, Ctext), 1f))
                    using (GraphicsPath path = RoundedRect(x, bottom - height, w, height, 3)) g.DrawPath(edge, path);
                // 命中区至少 8px 宽:2~3px 的短段否则几乎悬停不到。
                float hitW = Math.Max(w, 8f);
                _segmentBars.Add(new RectangleF(x + w / 2 - hitW / 2, bottom - height, hitW, height));
            }
            if (record.Date == DateTime.Today)
            {
                double seconds = (DateTime.Now - record.Date).TotalSeconds;
                float x = left + (float)(Math.Max(0, Math.Min(86400, seconds)) / 86400 * track);
                using (Pen glow = new Pen(Color.FromArgb(36, Cred), 6f)) g.DrawLine(glow, x, top, x, bottom);
                using (Pen now = new Pen(Color.FromArgb(210, Cred), 1.4f) { DashStyle = DashStyle.Dash }) g.DrawLine(now, x, top, x, bottom);
                using (SolidBrush pin = new SolidBrush(Cred))
                {
                    PointF[] tip = { new PointF(x - 4, top - 2), new PointF(x + 4, top - 2), new PointF(x, top + 6) };
                    g.FillPolygon(pin, tip);
                }
                string clock = DateTime.Now.ToString("HH:mm");
                RectangleF stamp = new RectangleF(Math.Min(x + 7, right - 44), top - 18, 44, 15);
                AppText(g, clock, _fAxis, Cred, stamp, false);
            }

            // 悬停气泡:段的起止、持续时长与当前口径的强度。
            if (_hover >= 0 && _hover < sessions.Count)
                PaintHourSegmentTip(g, new RectangleF(left, top, track, bottom - top), sessions[_hover], bars[_hover], rates[_hover], maxRate);

            // 底部四个微胶囊:分段标签、加粗次数和嵌入进度条组成稳定的托底结构。
            // 分档阈值与"短段占比"公式与报告页共用 ReportDesign,不再两处各写一套。
            int[] bins = ReportDesign.SegmentBins(sessions);
            double maxSeconds = 0, totalSeconds = 0;
            foreach (ActiveSession session in sessions)
            {
                totalSeconds += session.Seconds; maxSeconds = Math.Max(maxSeconds, session.Seconds);
            }
            int binMax = 1; foreach (int value in bins) binMax = Math.Max(binMax, value);
            string[] names = ReportDesign.SegmentBinNames;
            float gap = 8, cardW = (track - gap * 3) / 4f, cardY = 382, cardH = 84;
            for (int i = 0; i < 4; i++)
            {
                float x = left + i * (cardW + gap);
                RectangleF card = new RectangleF(x, cardY, cardW, cardH);
                using (GraphicsPath path = RoundedRect(card.X, card.Y, card.Width, card.Height, 9))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(14, Ctext))) g.FillPath(fill, path);
                using (Pen edge = new Pen(Color.FromArgb(26, Ctext)))
                using (GraphicsPath path = RoundedRect(card.X, card.Y, card.Width, card.Height, 9)) g.DrawPath(edge, path);
                AppText(g, names[i], _fAxis, Csub, new RectangleF(x + 10, cardY + 8, cardW - 20, 16), false);
                AppText(g, bins[i] + " 次", _fH2, Ctext, new RectangleF(x + 10, cardY + 26, cardW - 20, 24), false);
                float barX = x + 10, barW = cardW - 20;
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(26, Ctext))) g.FillRectangle(trackBrush, barX, cardY + cardH - 15, barW, 4);
                using (SolidBrush fill = new SolidBrush(i == 3 ? Corange : Cblue)) g.FillRectangle(fill, barX, cardY + cardH - 15, barW * bins[i] / binMax, 4);
            }
            PaintHourStatCards(g,
                new[] { "连续段", "平均段长", "最长段", "短段占比 (<5m)" },
                new[] { sessions.Count + " 段", ActivityMonitor.FormatDuration(sessions.Count > 0 ? totalSeconds / sessions.Count : 0), ActivityMonitor.FormatDuration(maxSeconds), ReportDesign.ShortShare(bins[0], sessions.Count) });
        }

        /// <summary>连续段的悬停气泡:起止时间、持续时长、当前口径的强度。</summary>
        private void PaintHourSegmentTip(Graphics g, RectangleF plot, ActiveSession session, RectangleF bar, double rate, double maxRate)
        {
            string span = session.Start.ToString("HH:mm") + "–" + session.End.ToString("HH:mm");
            string duration = ActivityMonitor.FormatDuration(session.Seconds);
            string strength = _hourMetric == 2
                ? "占当日最长段 " + (maxRate > 0 ? (100.0 * rate / maxRate).ToString("0", CultureInfo.InvariantCulture) + "%" : "--")
                : (int)Math.Round(rate) + (_hourMetric == 1 ? " 点击/分" : " 击键/分");
            _hoverDetail = span + " · 持续 " + duration + " · " + strength;
            float bubbleW = 176, bubbleH = 46;
            float bx = bar.X + bar.Width / 2 - bubbleW / 2;
            bx = Math.Max(plot.X + 4, Math.Min(bx, plot.Right - bubbleW - 4));
            float by = Math.Max(plot.Y + 2, bar.Y - bubbleH - 6);
            using (GraphicsPath path = RoundedRect(bx, by, bubbleW, bubbleH, 8))
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(244, Ccard2))) g.FillPath(fill, path);
                using (Pen edge = new Pen(Color.FromArgb(64, Ctext))) g.DrawPath(edge, path);
            }
            AppText(g, span, _fSmall, Csub, new RectangleF(bx + 10, by + 4, bubbleW - 20, 16), false);
            AppText(g, duration + " · " + strength, _fBody, Ctext, new RectangleF(bx + 10, by + 20, bubbleW - 20, 20), false);
        }

        /// <summary>
        /// 筛选胶囊的托盘底板。纯色主题背景是平的,7% 的近白雾足够;美术主题的原画会从这个 alpha
        /// 里透上来(亮部合成后 Δ&lt;1.5/255,等于没画),所以抬到与卡片同一档的近不透明底
        /// (PaintCardBase 用 216~236,这里 232)。
        /// 抽成 internal static 是为了让 tests/SegmentTrayTests 真的在离屏位图上画一次:
        /// 只断言常量的话,把这里改回旧配方测试也不会红。
        /// </summary>
        internal static void PaintSegmentTray(Graphics g, float x, float y, float width)
        {
            const float height = 32;
            using (GraphicsPath outer = RoundedRect(x, y, width, height, 10))
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(ArtTheme.FilterPlateAlpha, ArtTheme.FilterPlateColor))) g.FillPath(fill, outer);
                Color edge = ThemeArt.Active ? ArtTheme.Mix(Cline, Ctext, 0.18) : Cline;
                using (Pen pen = new Pen(edge)) g.DrawPath(pen, outer);
            }
        }

        private void PaintSegmentGroup(Graphics g, float x, float y, float width, string[] labels, int[] ids, int selected, bool icons)
        {
            float gap = 3, pad = 3, height = 32;
            PaintSegmentTray(g, x, y, width);
            float segment = (width - pad * 2 - gap * (labels.Length - 1)) / labels.Length;
            PointF mouse = ContentMouse();
            Color idle = FilterInkColor();
            for (int i = 0; i < labels.Length; i++)
            {
                float sx = x + pad + i * (segment + gap);
                RectangleF r = new RectangleF(sx, y + pad, segment, height - pad * 2);
                _chips.Add(r); _chipIds.Add(ids[i]);
                bool hover = Hit(r, mouse);
                Color ink = selected == i ? ArtTheme.Current.OnAccent : hover ? Ctext : idle;
                if (selected == i || hover)
                {
                    using (GraphicsPath path = RoundedRect(r.X, r.Y, r.Width, r.Height, 7))
                    using (SolidBrush fill = new SolidBrush(selected == i ? Cblue : Ccard2)) g.FillPath(fill, path);
                }
                if (icons)
                {
                    PaintHourModeIcon(g, sx + 10, y + 10, i, ink);
                    using (StringFormat format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                    using (SolidBrush brush = new SolidBrush(ink))
                        g.DrawString(labels[i], _fChip, brush, new RectangleF(sx + 25, y + pad, segment - 28, height - pad * 2), format);
                }
                else
                {
                    using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (SolidBrush brush = new SolidBrush(ink))
                        g.DrawString(labels[i], _fChip, brush, r, format);
                }
            }
        }

        private void PaintHourModeIcon(Graphics g, float x, float y, int mode, Color ink)
        {
            using (SolidBrush brush = new SolidBrush(ink))
            {
                if (mode == 0)
                {
                    g.FillRectangle(brush, x, y + 8, 2, 5);
                    g.FillRectangle(brush, x + 5, y + 5, 2, 8);
                    g.FillRectangle(brush, x + 10, y + 1, 2, 12);
                }
                else if (mode == 1)
                {
                    g.FillRectangle(brush, x, y + 1, 5, 5);
                    g.FillRectangle(brush, x + 7, y + 1, 5, 5);
                    g.FillRectangle(brush, x, y + 8, 5, 5);
                    g.FillRectangle(brush, x + 7, y + 8, 5, 5);
                }
                else
                {
                    g.FillRectangle(brush, x, y + 2, 11, 2);
                    g.FillRectangle(brush, x + 2, y + 6, 11, 2);
                    g.FillRectangle(brush, x, y + 10, 8, 2);
                }
            }
        }

        private static readonly string[] HourWeekdays = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

        private string HourMatrixValue(double value)
        {
            return _hourMetric == 2
                ? value.ToString("0.#", CultureInfo.InvariantCulture) + " 分钟"
                : Analysis.FmtCount((long)Math.Round(value)) + " 次";
        }

        private string HourRangeName()
        {
            return _hourRange == 0 ? "今日" : _hourRange == 1 ? "近 7 天" : "全部历史";
        }

        /// <summary>时段页当前时间跨度对应的日期。今日用 Store.Today,近 7 天含今天,全部历史用已落盘记录。</summary>
        private IEnumerable<DayRecord> HourDays()
        {
            if (_hourRange == 0) return new DayRecord[] { Store.Today };
            if (_hourRange == 1) return Analysis.RangeDays(7);
            return Store.History.Values;
        }

        private DateTime HourRangeStart()
        {
            if (_hourRange == 0) return DateTime.Today;
            if (_hourRange == 1) return DateTime.Today.AddDays(-6);
            DateTime start = DateTime.Today;
            foreach (DateTime day in Store.History.Keys) if (day < start) start = day;
            return start;
        }

        private void PaintHourStatCards(Graphics g, string[] titles, string[] values)
        {
            float tileW = (Cw - 3 * 16) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = Cx + i * (tileW + 16);
                PaintCardBase(g, x, 514, tileW, 62);
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(titles[i], _fSmall, b, x + 16, 522);
                using (SolidBrush b = new SolidBrush(Ctext))
                    g.DrawString(values[i], _fNum2, b, x + 16, 538);
            }
        }

        private void PaintHourBars(Graphics g, RectangleF rect, long[] vals)
        {
            // 柱子视图没有自己的悬停明细;不清空的话从连续段切过来后 Ctrl+C 会复制上一段旧明细。
            _hoverDetail = "";
            float left = rect.X + 50, right = rect.X + rect.Width - 16;
            float top = rect.Y + 38, bottom = rect.Y + rect.Height - 28;
            float ph = bottom - top, pw = right - left;

            long max = 10;
            foreach (long v in vals) if (v > max) max = v;
            double dmax, stepY;
            AxisScale.Nice(max, 4, true, out dmax, out stepY);

            for (int i = 0; i <= 4; i++)
            {
                float gy = bottom - ph * i / 4f;
                using (Pen pen = new Pen(Color.FromArgb(13, Ctext)))
                {
                    if (i != 0) pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, left, gy, right, gy);
                }
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    double labelValue = stepY * i;
                    string t = _hourMetric == 2
                        ? (labelValue / 60.0).ToString("0.#", CultureInfo.InvariantCulture)
                        : AxisScale.Label(labelValue);
                    SizeF sz = g.MeasureString(t, _fAxis);
                    g.DrawString(t, _fAxis, b, left - sz.Width - 6, gy - sz.Height / 2);
                }
            }

            float slot = pw / 24f;
            float barW = slot * 0.58f;
            double grow = Entrance();   // 1.8.0:入场时柱子从基线生长
            for (int i = 0; i < 24; i++)
            {
                float bx = left + slot * i + (slot - barW) / 2;
                float bh = (float)(vals[i] / dmax) * ph * (float)grow;
                bool isNow = i == DateTime.Now.Hour;
                bool isHover = i == _hover;
                double frac = max > 0 ? Math.Max(0, Math.Min(1, vals[i] / (double)max)) : 0;

                // 柱高就是数值,颜色深浅跟着高度走:低频深灰蓝,峰值荧光蓝,悬停青色。
                Color c;
                if (isHover) c = Ccyan;
                else if (vals[i] <= 0) c = Color.FromArgb(70, Csub);
                else
                {
                    c = ArtTheme.Mix(Color.FromArgb(90, Csub), Cblue, (float)Math.Sqrt(frac));
                    if (frac > 0.82) c = ArtTheme.Mix(c, Ccyan, (float)((frac - 0.82) / 0.18));
                }
                using (GraphicsPath p = RoundedRect(bx, bottom - Math.Max(bh, 2), barW, Math.Max(bh, 2), 3))
                {
                    using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
                    if (isNow)
                    {
                        using (Pen outline = new Pen(Color.FromArgb(110, Ctext), 1f))
                            g.DrawPath(outline, p);
                    }
                }

                if (i % 3 == 0)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(220, Ctext)))
                    {
                        string t = i.ToString("00");
                        g.DrawString(t, _fBody, b, left + slot * i + slot / 2 - 9, bottom + 6);
                    }
                }
            }

            // 悬停提示
            if (_hover >= 0 && _hover < 24)
            {
                long v = vals[_hover];
                string l1 = string.Format("{0:00}:00–{1:00}:00", _hover, (_hover + 1) % 24);
                string l2 = _hourMetric == 2 ? ActivityMonitor.FormatDuration(v) : Analysis.FmtCount(v) + " 次";
                float tw = 110;
                float bx = left + slot * _hover + slot / 2 - tw / 2;
                bx = Math.Max(rect.X + 8, Math.Min(bx, rect.Right - tw - 8));
                float by = top - 2;
                using (GraphicsPath bp = RoundedRect(bx, by, tw, 42, 8))
                {
                    using (SolidBrush b = new SolidBrush(Ccard2))
                        g.FillPath(b, bp);
                    using (Pen pen = new Pen(Color.FromArgb(50, Ctext)))
                        g.DrawPath(pen, bp);
                }
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(l1, _fSmall, b, bx + 10, by + 4);
                using (SolidBrush b = new SolidBrush(Ctext))
                    g.DrawString(l2, _fBody, b, bx + 10, by + 19);
            }
        }

        /// <summary>
        /// 周 × 24 小时作息矩阵:纵轴周一至周日,横轴 0–23 点,格子颜色=近 90 天该星期几该小时的平均强度。
        /// 这是「24 格热力条」真正值得占用的形态:一眼能看出工作日与周末的作息差别。
        /// </summary>
        private void PaintHourMatrix(Graphics g, RectangleF rect, HourProfile.Result data)
        {
            PaintCard(g, rect.X, rect.Y, rect.Width, rect.Height,
                "周 × 24 小时作息矩阵 · " + (_hourRange == 0 ? "今日（进行中）"
                    : _hourRange == 1 ? "近 7 天 · 按有记录日平均（不含今天）"
                    : "全部历史 · 按有记录日平均（不含今天）"));
            float left = rect.X + 52, right = rect.Right - 16;
            float top = rect.Y + 70, bottom = rect.Bottom - 30;
            float cw = (right - left) / 24f, ch = (bottom - top) / 7f;

            using (SolidBrush b = new SolidBrush(Color.FromArgb(220, Ctext)))
            {
                for (int h = 0; h < 24; h += 3)
                    g.DrawString(h.ToString("00"), _fBody, b, left + cw * h + cw / 2 - 9, top - 19);
                g.DrawString("23", _fBody, b, left + cw * 23 + cw / 2 - 9, top - 19);
            }

            if (!data.HasData)
            {
                AppText(g, "所选区间还没有带小时的记录", _fNum2, Ctext, new RectangleF(rect.X + 80, rect.Y + 150, rect.Width - 160, 36), false);
                AppText(g, "用一会儿键盘或鼠标，矩阵会按星期几与小时累积出作息轮廓。", _fBody, Csub,
                    new RectangleF(rect.X + 80, rect.Y + 188, rect.Width - 160, 26), false);
                return;
            }

            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            int hoverRow = -1, hoverCol = -1;
            for (int row = 0; row < 7; row++)
            {
                AppText(g, HourWeekdays[row], _fSmall, Csub, new RectangleF(rect.X + 12, top + row * ch + ch / 2 - 9, 36, 18), false);
                for (int col = 0; col < 24; col++)
                {
                    double value = data.Average[row, col];
                    double frac = data.Max > 0 ? Math.Max(0, Math.Min(1, value / data.Max)) : 0;
                    RectangleF cell = new RectangleF(left + col * cw + 1, top + row * ch + 1, Math.Max(2, cw - 2), Math.Max(2, ch - 2));
                    using (SolidBrush b = new SolidBrush(value <= 0 ? HeatScale.Zero : HeatScale.At(frac)))
                        g.FillRectangle(b, cell);
                    if (cell.Contains(mouse))
                    {
                        hoverRow = row; hoverCol = col;
                        using (Pen outline = new Pen(Color.FromArgb(210, Ctext), 1.4f)) g.DrawRectangle(outline, cell.X, cell.Y, cell.Width, cell.Height);
                    }
                }
            }

            // 右下角图例:低 → 高
            float legendX = right - 132, legendY = rect.Bottom - 19;
            for (int i = 0; i < 72; i++)
                using (SolidBrush b = new SolidBrush(HeatScale.At(i / 71.0)))
                    g.FillRectangle(b, legendX + 18 + i, legendY, 1, 7);
            AppText(g, "低", _fAxis, Csub, new RectangleF(legendX, legendY - 5, 18, 16), false);
            AppText(g, "高", _fAxis, Csub, new RectangleF(legendX + 92, legendY - 5, 18, 16), false);

            if (hoverRow >= 0 && hoverCol >= 0)
            {
                string head = HourWeekdays[hoverRow] + " " + hoverCol.ToString("00") + ":00–" + ((hoverCol + 1) % 24).ToString("00") + ":00";
                string body = _hourMetric == 2
                    ? data.Average[hoverRow, hoverCol].ToString("0.#", CultureInfo.InvariantCulture) + " 分钟/小时"
                    : Analysis.FmtCount((long)Math.Round(data.Average[hoverRow, hoverCol])) + " 次/小时";
                string note = "近 90 天有 " + data.Days[hoverRow] + " 个" + HourWeekdays[hoverRow] + "有记录";
                float boxW = 210, boxH = 58;
                float boxX = Math.Min(Math.Max(mouse.X + 14, left), right - boxW);
                float boxY = Math.Min(Math.Max(mouse.Y - boxH - 12, top), bottom - boxH);
                using (GraphicsPath bp = RoundedRect(boxX, boxY, boxW, boxH, 8))
                {
                    using (SolidBrush b = new SolidBrush(Ccard2)) g.FillPath(b, bp);
                    using (Pen pen = new Pen(Color.FromArgb(50, Ctext))) g.DrawPath(pen, bp);
                }
                AppText(g, head, _fSmall, Csub, new RectangleF(boxX + 10, boxY + 5, boxW - 20, 18), false);
                AppText(g, "平均 " + body, _fBody, Ctext, new RectangleF(boxX + 10, boxY + 22, boxW - 20, 20), false);
                AppText(g, note, _fAxis, Csub, new RectangleF(boxX + 10, boxY + 41, boxW - 20, 14), false);
                _hoverDetail = HourWeekdays[hoverRow] + " " + hoverCol.ToString("00") + ":00 " + body + " · " + note;
            }
            else _hoverDetail = "";
        }

        // ---------------------------------------------------------------- 标签页:按键排行

        /// <summary>按键排行的「按住时长」模式:复用区间聚合,口径与报告页一致。</summary>
        private void PaintHoldRanking(Graphics g)
        {
            int[] spans = { 1, 90, 7, 30 };
            int span = _keyRange >= 0 && _keyRange < spans.Length ? spans[_keyRange] : 1;
            DateTime end = DateTime.Today;
            DateTime start = span <= 1 ? DateTime.Today : DateTime.Today.AddDays(-(span - 1));
            HoldReportData data = HoldReportData.Build(start, end);

            PaintCard(g, Cx, 138, 560, 438, null);
            PaintCard(g, Cx + 572, 138, Cw - 572, 438, null);
            AppText(g, "每键平均按住时长" + (data.TopKeys.Count > 0 ? " · Top " + data.TopKeys.Count : ""), _fH2, Ctext, new RectangleF(Cx + 18, 148, 400, 24), false);
            AppChip(g, 44, Cx + 438, 148, 104, "按住趋势", false);
            AppText(g, "覆盖 " + data.CardValues[3] + " · 样本 " + data.Samples + " · 累计按住 " + data.CardValues[4]
                + " · 有键按下 " + data.CardValues[5], _fSmall, Csub, new RectangleF(Cx + 18, 172, 540, 20), false);
            if (data.Samples == 0)
            {
                AppText(g, "该区间没有按键时长样本", _fNum2, Ctext, new RectangleF(Cx + 30, 300, 500, 40), false);
                AppText(g, "按住时长从 1.7.3 起采集；更早的日期不会有数据。", _fBody, Csub, new RectangleF(Cx + 30, 345, 500, 30), false);
                return;
            }
            double peak = 1;
            foreach (KeyValuePair<int, KeyHold> pair in data.TopKeys) peak = Math.Max(peak, pair.Value.Mean);
            for (int i = 0; i < data.TopKeys.Count; i++)
            {
                KeyValuePair<int, KeyHold> pair = data.TopKeys[i];
                float y = 200 + i * 30;
                AppText(g, Analysis.KeyName(pair.Key), _fBody, Ctext, new RectangleF(Cx + 18, y, 70, 22), false);
                RectangleF track = new RectangleF(Cx + 92, y + 5, 330, 12);
                using (SolidBrush b = new SolidBrush(Cbg)) g.FillRectangle(b, track);
                using (SolidBrush b = new SolidBrush(Ccyan)) g.FillRectangle(b, track.X, track.Y, (float)(pair.Value.Mean / peak * track.Width), track.Height);
                AppText(g, pair.Value.Mean.ToString("0", CultureInfo.InvariantCulture) + " ms · " + pair.Value.Count + " 次", _fSmall, Ctext,
                    new RectangleF(Cx + 430, y, 120, 22), false);
            }
            if (data.TopKeys.Count == 0) AppText(g, "每个键至少 10 次样本才进入排行", _fBody, Csub, new RectangleF(Cx + 18, 210, 500, 24), false);

            AppText(g, "按住时长分布 · 最长 " + data.CardValues[2], _fH2, Ctext, new RectangleF(Cx + 590, 148, 230, 24), false);
            long bucketPeak = 1;
            foreach (long value in data.Buckets) bucketPeak = Math.Max(bucketPeak, value);
            float histLeft = Cx + 590, histBottom = 470, histTop = 230;
            float step = (Cw - 590 - 20) / (float)data.Buckets.Length;
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            int hoverBucket = mouse.X >= histLeft && mouse.X < histLeft + data.Buckets.Length * step
                && mouse.Y >= histTop - 8 && mouse.Y <= histBottom + 20
                ? Math.Max(0, Math.Min(data.Buckets.Length - 1, (int)((mouse.X - histLeft) / step))) : -1;
            for (int i = 0; i < data.Buckets.Length; i++)
            {
                float height = (float)data.Buckets[i] / bucketPeak * (histBottom - histTop);
                using (SolidBrush b = new SolidBrush(i == hoverBucket ? Ccyan : Cblue))
                    g.FillRectangle(b, histLeft + i * step + 1, histBottom - height, Math.Max(2, step - 4), height);
            }

            // X 轴只画区间边界节点,不再把 8 个「50-1..」截断标签硬塞进 27px 的槽位;
            // 完整区间、次数与占比放到悬停提示里。
            string[] ticks = { "0", "50", "100", "200", "500", "1s", "2s", "≥5s" };
            using (Pen axis = new Pen(Color.FromArgb(24, Ctext))) g.DrawLine(axis, histLeft, histBottom, histLeft + data.Buckets.Length * step, histBottom);
            using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near })
            using (SolidBrush lb = new SolidBrush(Csub))
                for (int i = 0; i < ticks.Length && i < data.Buckets.Length; i++)
                    g.DrawString(ticks[i], _fAxis, lb,
                        new RectangleF(histLeft + i * step - step / 2, histBottom + 4, step, 18), centered);

            if (hoverBucket >= 0)
            {
                long count = data.Buckets[hoverBucket];
                double share = data.Samples > 0 ? count * 100.0 / data.Samples : 0;
                string range = HoldTracker.BucketLabel(hoverBucket);
                string line1 = range;
                string line2 = Analysis.FmtCount(count) + " 次 · 占 " + share.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                SizeF s1 = g.MeasureString(line1, _fSmall);
                SizeF s2 = g.MeasureString(line2, _fBody);
                float boxW = Math.Max(s1.Width, s2.Width) + 20;
                float boxX = Math.Max(Cx + 572, Math.Min(histLeft + hoverBucket * step + step / 2 - boxW / 2, Cx + Cw - 20 - boxW));
                float boxY = Math.Max(histTop - 8, histBottom - (float)data.Buckets[hoverBucket] / bucketPeak * (histBottom - histTop) - 60);
                using (GraphicsPath bp = RoundedRect(boxX, boxY, boxW, 48, 8))
                {
                    using (SolidBrush b = new SolidBrush(Ccard2)) g.FillPath(b, bp);
                    using (Pen pen = new Pen(Color.FromArgb(50, Ctext))) g.DrawPath(pen, bp);
                }
                AppText(g, line1, _fSmall, Csub, new RectangleF(boxX + 10, boxY + 5, boxW - 20, 18), false);
                AppText(g, line2, _fBody, Ctext, new RectangleF(boxX + 10, boxY + 23, boxW - 20, 20), false);
                _hoverDetail = range + " · " + Analysis.FmtCount(count) + " 次 · 占 " + share.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            }
            else _hoverDetail = "";
            AppText(g, data.Footer, _fSmall, Csub, new RectangleF(Cx + 590, 500, Cw - 610, 60), false);
        }
        private void PaintKeys(Graphics g)
        {
            // 一级主视图:看什么形态。三组控制分别放在左/中/右,用留白和竖线隔开维度。
            string[] views = { "按键排行", "组合键排行", "全键热力" };
            PaintSegmentGroup(g, Cx, 98, 276, views, new[] { 72, 73, 74 }, _keyView, false);
            using (Pen divider = new Pen(Color.FromArgb(28, Ctext)))
                g.DrawLine(divider, Cx + 312, 104, Cx + 312, 122);

            string[] ranges = { "今日", "近7天", "近30天", "近90天" };
            int rangeIndex = _keyRange == 0 ? 0 : _keyRange == 2 ? 1 : _keyRange == 3 ? 2 : 3;
            PaintSegmentGroup(g, Cx + 328, 98, 260, ranges, new[] { 70, 76, 77, 71 }, rangeIndex, false);
            if (_keyView != 1)
                PaintSegmentGroup(g, Cx + Cw - 150, 98, 150, new[] { "击键次数", "按住时长" }, new[] { 78, 79 }, _keyMetric, false);

            IEnumerable<DayRecord> src = KeyRangeDays();
            if (_keyView == 2) { PaintKeyboardHeatmap(g, src); return; }
            if (_keyView == 0 && _keyMetric == 1) { PaintHoldRanking(g); return; }

            bool combos = _keyView == 1;
            List<KeyValuePair<string, long>> rank = combos ? ShortcutStats.Ranking(src) : Analysis.KeyRanking(src);
            long total = 0;
            foreach (KeyValuePair<string, long> item in rank) total += item.Value;

            // 左侧:排行表格(排名 / 键帽 / 频次 / 平均按住 / 频次条内嵌占比)
            PaintCard(g, Cx, 138, 560, 438, null);
            float xRank = Cx + 16, xKey = Cx + 50, xCount = Cx + 170, xHold = Cx + 236, xBar = combos ? Cx + 236 : Cx + 312;
            float barW = combos ? 260 : 184, percentW = 44, percentX = xBar + barW + 4;
            using (SolidBrush head = new SolidBrush(Csub))
            {
                g.DrawString("排名", _fSmall, head, xRank, 148);
                g.DrawString(combos ? "组合" : "按键", _fSmall, head, xKey, 148);
                SizeF sz = g.MeasureString("频次", _fSmall);
                g.DrawString("频次", _fSmall, head, xCount + 60 - sz.Width, 148);
                if (!combos)
                {
                    sz = g.MeasureString("平均按住", _fSmall);
                    g.DrawString("平均按住", _fSmall, head, xHold + 68 - sz.Width, 148);
                }
                g.DrawString("频次条", _fSmall, head, xBar, 148);
                sz = g.MeasureString("占比", _fSmall);
                g.DrawString("占比", _fSmall, head, percentX + percentW - sz.Width, 148);
            }
            using (Pen pen = new Pen(Color.FromArgb(22, Ctext))) g.DrawLine(pen, Cx + 16, 168, Cx + 544, 168);

            if (rank.Count == 0)
            {
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    string empty = combos ? "还没有组合动作，试试 Ctrl+C 或 Win+Shift+S" : "暂无按键数据";
                    SizeF sz = g.MeasureString(empty, _fBody);
                    g.DrawString(empty, _fBody, b, Cx + 280 - sz.Width / 2, 138 + 200);
                }
            }
            else
            {
                int n = Math.Min(10, rank.Count);
                long max = rank[0].Value;
                // 从总览分布卡下钻过来时高亮那一行(按显示名匹配,与 KeyRanking 的归并口径一致)。
                int highlighted = -1;
                if (!combos && _selectedKeyName.Length > 0)
                    for (int i = 0; i < n; i++) if (rank[i].Key == _selectedKeyName) { highlighted = i; break; }
                for (int i = 0; i < n; i++)
                {
                    float ry = 176 + i * 37;
                    if (i == highlighted)
                        using (GraphicsPath band = RoundedRect(Cx + 8, ry - 4, 538, 30, 8))
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(34, Cblue)))
                        using (Pen edge = new Pen(Color.FromArgb(120, Cblue)))
                        {
                            g.FillPath(fill, band);
                            g.DrawPath(edge, band);
                        }
                    using (SolidBrush b = new SolidBrush(i < 3 ? Color.FromArgb(70, Cblue) : Color.FromArgb(26, Ctext)))
                        g.FillEllipse(b, xRank, ry + 3, 18, 18);
                    using (SolidBrush b = new SolidBrush(i < 3 ? Ctext : Csub))
                    {
                        string t = (i + 1).ToString();
                        SizeF sz = g.MeasureString(t, _fSmall);
                        g.DrawString(t, _fSmall, b, xRank + 9 - sz.Width / 2, ry + 5);
                    }
                    PaintKeycap(g, rank[i].Key, new RectangleF(xKey, ry + 2, 110, 22), i < 3 || i == highlighted);
                    AppText(g, Analysis.FmtCount(rank[i].Value), _fBody, Ctext,
                        new RectangleF(xCount, ry, 60, 22), true);
                    if (!combos) PaintHoldCell(g, rank[i].Key, new RectangleF(xHold, ry + 2, 68, 20));

                    double share = total > 0 ? rank[i].Value * 100.0 / total : 0;
                    using (SolidBrush track = new SolidBrush(Color.FromArgb(18, Ctext)))
                        g.FillRectangle(track, xBar, ry + 11, barW, 7);
                    float frac = max > 0 ? (float)rank[i].Value / max : 0;
                    frac *= (float)Entrance();
                    Color c = i == 0 ? Cblue : i == 1 ? Ccyan : i == 2 ? Cgreen : Color.FromArgb(150, Csub);
                    using (GraphicsPath bp = RoundedRect(xBar, ry + 11, Math.Max(4, barW * frac), 7, 4))
                    using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, bp);
                    AppText(g, share.ToString("0.0", CultureInfo.InvariantCulture) + "%", _fAxis,
                        Color.FromArgb(220, Ctext), new RectangleF(percentX, ry + 2, percentW, 18), true);
                }
                string noun = combos ? "组合动作 " : "击键 ";
                AppText(g, "共 " + rank.Count + " 种 · 总" + noun + Analysis.FmtCount(total)
                    + " · 前三占比 " + Top3Pct(rank, total), _fSmall, Csub, new RectangleF(Cx + 16, 548, 520, 18), false);
                if (combos) PaintShortcutSavings(g, src);
            }

            // 右侧:Top 11–20 + 底部输入画像卡片
            float rx = Cx + 576, rw = Cw - 576;
            PaintCard(g, rx, 138, rw, 438, combos ? "更多组合" : "更多按键");
            long keyDays = 0;
            foreach (DayRecord r in src) if (combos ? r.ComboCounts.Count > 0 : r.KeyCounts.Count > 0) keyDays++;
            AppText(g, "Top 11–20", _fSmall, Csub, new RectangleF(rx + 16, 172, 140, 18), false);
            if (rank.Count <= 10)
            {
                AppText(g, "已经显示全部 " + rank.Count + " 种", _fSmall, Csub, new RectangleF(rx + 16, 200, rw - 32, 18), false);
            }
            else
            {
                int shown = Math.Min(10, rank.Count - 10);
                long sideMax = rank[10].Value;
                for (int i = 0; i < shown; i++)
                {
                    int index = 10 + i;
                    float ry = 194 + i * 23;
                    AppText(g, (index + 1).ToString(), _fAxis, Csub, new RectangleF(rx + 16, ry, 22, 18), false);
                    PaintKeycap(g, rank[index].Key, new RectangleF(rx + 42, ry + 1, 92, 18), false);
                    float frac = sideMax > 0 ? (float)rank[index].Value / sideMax : 0;
                    using (SolidBrush track = new SolidBrush(Color.FromArgb(20, Ctext)))
                        g.FillRectangle(track, rx + rw - 78, ry + 15, 60, 3);
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(130, Csub)))
                        g.FillRectangle(fill, rx + rw - 78, ry + 15, 60 * frac, 3);
                    AppText(g, Analysis.FmtCount(rank[index].Value), _fAxis, Csub, new RectangleF(rx + rw - 78, ry, 64, 18), true);
                }
            }

            using (Pen pen = new Pen(Color.FromArgb(20, Ctext))) g.DrawLine(pen, rx + 16, 430, rx + rw - 16, 430);
            using (GraphicsPath profile = RoundedRect(rx + 12, 438, rw - 24, 120, 9))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(12, Ctext))) g.FillPath(fill, profile);
            using (Pen edge = new Pen(Color.FromArgb(28, Ctext)))
            using (GraphicsPath profile = RoundedRect(rx + 12, 438, rw - 24, 120, 9)) g.DrawPath(edge, profile);
            AppText(g, "输入画像", _fBody, Ctext, new RectangleF(rx + 24, 446, rw - 48, 20), false);
            AppText(g, "共 " + rank.Count + " 种 · 总" + (combos ? "动作 " : "击键 ") + Analysis.FmtCount(total),
                _fSmall, Csub, new RectangleF(rx + 24, 468, rw - 48, 18), false);
            AppText(g, "最常用 " + (rank.Count > 0 ? KeycapLabel(rank[0].Key) : "--") + " · 前三 " + Top3Pct(rank, total),
                _fSmall, Csub, new RectangleF(rx + 24, 486, rw - 48, 18), false);
            AppText(g, "记录 " + keyDays + " 天 · " + (combos ? "长按计一次" : "键鼠比 " + MouseKeyRatio(src)),
                _fSmall, Csub, new RectangleF(rx + 24, 504, rw - 48, 18), false);
            AppText(g, combos ? "组合动作按首次按下计 1 次" : "按住时长从 1.7.3 起采集",
                _fAxis, Color.FromArgb(150, Csub), new RectangleF(rx + 24, 526, rw - 48, 16), false);
        }

        private static string KeycapLabel(string key)
        {
            if (string.IsNullOrEmpty(key)) return "--";
            switch (key)
            {
                case "空格": return "Space";
                case "退格": return "⌫ Bksp";
                case "回车": return "⏎ Enter";
                case "Esc": return "Esc";
                case "左上": return "↖";
                case "上": return "↑";
                case "右下": return "↘";
                case "下": return "↓";
                case "左下": return "↙";
                case "右": return "→";
                case "右上": return "↗";
                case "左": return "←";
                default: return key;
            }
        }

        private void PaintKeycap(Graphics g, string key, RectangleF area, bool accent)
        {
            string label = KeycapLabel(key);
            SizeF size = g.MeasureString(label, _fKey);
            float w = Math.Min(area.Width, Math.Max(24, size.Width + 14));
            RectangleF cap = new RectangleF(area.X + (area.Width - w) / 2, area.Y, w, area.Height);
            using (GraphicsPath path = RoundedRect(cap.X, cap.Y, cap.Width, cap.Height, 5))
            {
                using (SolidBrush fill = new SolidBrush(accent ? Color.FromArgb(34, Cblue) : Color.FromArgb(15, Ctext))) g.FillPath(fill, path);
                using (Pen edge = new Pen(accent ? Color.FromArgb(85, Cblue) : Color.FromArgb(34, Ctext))) g.DrawPath(edge, path);
            }
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (SolidBrush ink = new SolidBrush(accent ? Ctext : Csub))
                g.DrawString(label, _fKey, ink, cap, format);
        }

        private void PaintHoldCell(Graphics g, string key, RectangleF area)
        {
            string text = AverageHoldText(key);
            bool longHold = IsLongHold(text);
            AppText(g, text, _fSmall, longHold ? Corange : Csub, new RectangleF(area.X, area.Y, area.Width - 16, area.Height), true);
            if (longHold) AppText(g, "长", _fAxis, Corange, new RectangleF(area.Right - 14, area.Y, 14, area.Height), true);
        }

        private static bool IsLongHold(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            double value;
            string number = text.Trim();
            if (number.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                number = number.Substring(0, number.Length - 2).Trim();
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value >= 250;
            }
            if (number.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                number = number.Substring(0, number.Length - 1).Trim();
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value >= 0.25;
            }
            return false;
        }

        private void PaintShortcutSavings(Graphics g, IEnumerable<DayRecord> days)
        {
            DayRecord comboDay = new DayRecord();
            foreach (DayRecord day in days)
                foreach (KeyValuePair<string, long> pair in day.ComboCounts)
                {
                    long count; comboDay.ComboCounts.TryGetValue(pair.Key, out count);
                    comboDay.ComboCounts[pair.Key] = count + pair.Value;
                }
            CrossReportData savings = ShortcutSavings.Build(comboDay);
            AppText(g, "快捷键估算节省 " + savings.CardValues[2] + " · 假设模型，不计入活跃时长",
                _fAxis, Csub, new RectangleF(Cx + 16, 566, 520, 14), false);
        }
        private Dictionary<string, string> _holdAverageCache;
        private int _holdAverageRange = -1;
        private DateTime _holdAverageDay = DateTime.MinValue;

        /// <summary>表格里的「平均按住」:按当前区间构建一次 HoldReportData 并缓存,避免逐帧重算。</summary>
        private string AverageHoldText(string keyName)
        {
            if (_holdAverageCache == null || _holdAverageRange != _keyRange || _holdAverageDay != DateTime.Today)
            {
                _holdAverageCache = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    int span = _keyRange == 0 ? 1 : _keyRange == 2 ? 7 : _keyRange == 3 ? 30 : 90;
                    DateTime start = span <= 1 ? DateTime.Today : DateTime.Today.AddDays(-(span - 1));
                    HoldReportData data = HoldReportData.Build(start, DateTime.Today);
                    foreach (string[] row in data.Rows) if (row.Length > 2) _holdAverageCache[row[0]] = row[2];
                }
                catch { }
                _holdAverageRange = _keyRange;
                _holdAverageDay = DateTime.Today;
            }
            string text;
            return _holdAverageCache.TryGetValue(keyName ?? "", out text) ? text : "—";
        }

        private string Top3Pct(List<KeyValuePair<string, long>> rank, long total)
        {
            if (total == 0 || rank.Count == 0) return "--";
            long s = 0;
            for (int i = 0; i < Math.Min(3, rank.Count); i++) s += rank[i].Value;
            return (100.0 * s / total).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        private IEnumerable<DayRecord> KeyRangeDays()
        {return _keyRange==0?(IEnumerable<DayRecord>)new DayRecord[]{Store.Today}:Analysis.RangeDays(_keyRange==2?7:_keyRange==3?30:90);}

        private string MouseKeyRatio(IEnumerable<DayRecord> days)
        {
            long clicks=Analysis.SumClicks(days);if (clicks == 0) return "--";
            double r = Analysis.SumKeys(days) / (double)clicks;
            return r.ToString("0.0", CultureInfo.InvariantCulture) + " : 1";
        }

        // ---------------------------------------------------------------- 导出

        private void ExportCsv()
        {
            if (_tab == TabId.Text) { ExportText(); return; }
            if (_tab == TabId.Heat) { if (_clickActionView) ExportMouseAction(); else ExportClickHeat(); return; }
            if (_tab == TabId.Keys && _keyView == 2) { ExportKeyboardHeatmap(); return; }
            if (_tab == TabId.Trend && _trendHourly) { ExportHourlyTrend(); return; }
            if (_tab == TabId.Insights) { ExportInsights(); return; }
            if (_tab == TabId.Apps) { ExportApps(); return; }
            if (_tab == TabId.Keys && _keyView == 1)
            {
                try
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "组合键统计_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
                    IEnumerable<DayRecord> days = KeyRangeDays();
                    File.WriteAllText(path, ShortcutStats.Export(days), new UTF8Encoding(true));
                    ThemeMessage.Show(this, "已导出：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                return;
            }
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string path = Path.Combine(desktop,
                    string.Format("键鼠统计导出_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now));

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("日期,击键,点击,左键,右键,中键,侧键,滚轮,鼠标路程估算(m;设置DPI后累计),光标路程(px;含旧版数据),有效使用秒数,观测空闲秒数,连续使用段数");
                List<DateTime> days = new List<DateTime>();
                if (_tab == TabId.Trend)
                {
                    // 按天视图导出跟随页面所选区间,不再无视 _trendRange 直接倒全部历史。
                    DateTime from = DateTime.Today.AddDays(-(_trendRange - 1));
                    for (DateTime d = from; d <= DateTime.Today; d = d.AddDays(1)) days.Add(d);
                }
                else
                {
                    days.AddRange(Store.History.Keys);
                    days.Sort();
                }
                foreach (DateTime d in days)
                {
                    DayRecord r;
                    if (!Store.History.TryGetValue(d, out r) || r.IsEmpty) continue;
                    sb.Append(d.ToString("yyyy-MM-dd")).Append(',');
                    sb.Append(r.Keys).Append(',');
                    sb.Append(r.Clicks).Append(',');
                    sb.Append(r.Left).Append(',');
                    sb.Append(r.Right).Append(',');
                    sb.Append(r.Middle).Append(',');
                    sb.Append(r.XButtons).Append(',');
                    sb.Append(r.Wheel).Append(',');
                    sb.Append(r.MoveMeters.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(r.MovePx.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(r.ActiveSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(r.IdleSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                    sb.AppendLine(r.Sessions.Count.ToString(CultureInfo.InvariantCulture));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出到:\n" + path, "导出成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ThemeMessage.Show(this, "导出失败:\n" + ex.Message, "导出失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------------------------------------------------------- 通用

        internal static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            if (w < r * 2) r = w / 2;
            if (h < r * 2) r = h / 2;
            if (r < 0.5f) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}







