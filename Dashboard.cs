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
            long total = 0;
            foreach (DayRecord r in rs)
            {
                foreach (KeyValuePair<int, long> kv in r.KeyCounts)
                {
                    string name = KeyName(kv.Key);
                    long old;
                    byName.TryGetValue(name, out old);
                    byName[name] = old + kv.Value;
                    total += kv.Value;
                }
            }
            List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>(byName);
            list.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            if (list.Count > 0 && total > 0)
            {
                // 附带总量,供占比计算:塞在尾部不易用,改为由调用方自行求和
            }
            return list;
        }

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
        private int _hourMetric = 0;      // 0击键 1点击
        private int _hourView;             // 0 时段柱子 1 周×24 矩阵
        private int _hourRange = 2;        // 0 今日 1 近7天 2 全部历史
        private RectangleF _activityRect, _sessionsRect;
        /// <summary>1.8.0:总览页八张数字卡的位置,用于点击下钻到对应报告页。</summary>
        private readonly List<RectangleF> _overviewTiles = new List<RectangleF>();
        /// <summary>1.8.0:总览卡片 → 报告页(0 输入习惯 / 3 使用节奏 / 8 鼠标动作 / 11 区间回顾)。</summary>
        private static readonly int[] OverviewTileReport = { 0, 0, 0, 8, 11, 3, 0, 0 };
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
        private RectangleF _distributionCountRect;
        private readonly RectangleF[] _distributionPeriods=new RectangleF[4];
        private RectangleF _distributionMouseRect, _distributionKeyboardRect;
        private int _keyRange = 0;        // 0今日 1近90天 2近7天 3近30天
        private int _keyView;             // 0 按键排行 1 组合键排行 2 全键热力图
        private int _keyMetric;           // 0 击键次数 1 按住时长(列表与热力图共用)
        private int _hover = -1;          // 图表悬停索引

        private readonly RectangleF[] _tabRects = new RectangleF[9];
        private readonly List<RectangleF> _chips = new List<RectangleF>();
        private readonly List<int> _chipIds = new List<int>();
        private bool _appKeyGroups;
        private RectangleF _closeRect, _exportRect;

        private float _s = 1f;
        private Font _fTitle, _fTab, _fH2, _fBody, _fSmall, _fAxis, _fNum, _fNum2, _fChip;
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
            if (_crumb.Length == 0) return false;
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
            _timer.Tick += delegate { if (Visible && WindowState!=FormWindowState.Minimized) Invalidate(); };
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
                    _s = Math.Max(1f, g.DpiX / 96f);
                }
            }
            catch { }
            Rectangle work = Screen.FromControl(this).WorkingArea;
            _s = Math.Min(_s, Math.Min((work.Width - 24f) / BW, (work.Height - 24f) / BH));
            _s = Math.Max(0.5f, _s);
            ClientSize = new Size((int)Math.Round(BW * _s), (int)Math.Round(BH * _s));
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
            try
            {
                using (GraphicsPath p = StatsWidget.RoundedPath(
                    new Rectangle(Point.Empty, ClientSize), (int)Math.Round(14 * _s)))
                {
                    Region = new Region(p);
                }
            }
            catch { }

            _fTitle = new Font("Microsoft YaHei UI", 25f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fTab = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fH2 = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fBody = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fAxis = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fNum = new Font("Segoe UI", 29f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fNum2 = new Font("Segoe UI", 19f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fChip = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);

            _timer.Start();

            // 「每日首次打开面板」:把昨天的日报生成并落盘。一天只做一次,记在数据文件里。
            TryGenerateDailyReport();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // 1.8.0:Esc / Backspace 先返回上一层(下钻),没有上一层时才关闭面板。
            if (e.KeyCode == Keys.Escape) { if (PopCrumb()) return; Close(); return; }
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
                _tab = (TabId)next; _hover = -1; _chipFocus = -1;
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
            foreach (Font font in new Font[] { _fTitle, _fTab, _fH2, _fBody, _fSmall, _fAxis, _fNum, _fNum2, _fChip, _fChar })
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
                case TabId.Trend: return 1;      // 每日趋势
                case TabId.Hours: return 12;     // 星期节律
                case TabId.Keys: return _keyMetric == 1 ? 14 : 9;   // 按键时长 / 键位分布
                case TabId.Apps: return _appKeyGroups ? 15 : 2;       // 应用键位 / 应用时间
                default: return 11;              // 总览 / 洞察 → 区间回顾
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
            if (HandleThemeClick(p)) { Invalidate(); return; }
            if (Hit(_exportRect, p)) { ExportCsv(); return; }
            PointF contentPoint = new PointF(p.X - ContentX, p.Y - ContentY);
            if(_tab==TabId.Overview && (Hit(_distributionMouseRect,contentPoint)||Hit(_distributionKeyboardRect,contentPoint)))
            { _overviewKeyboard=Hit(_distributionKeyboardRect,contentPoint);Invalidate();return; }
            if(_tab==TabId.Overview && _overviewKeyboard && Hit(_distributionCountRect,contentPoint))
            {
                ContextMenuStrip choices=PreparePopup(ref _distributionMenu);
                foreach(int count in new[]{5,8,10})
                {
                    int selected=count;
                    ToolStripMenuItem item=new ToolStripMenuItem("前 "+count+" 个按键");item.Checked=_distributionTop==count;
                    item.Click+=delegate{_distributionTop=selected;Invalidate();};choices.Items.Add(item);
                }

                choices.Show(this,e.Location);return;
            }
            if(_tab==TabId.Overview && _overviewKeyboard)for(int i=0;i<4;i++)if(Hit(_distributionPeriods[i],contentPoint)){_distributionPeriod=i;Invalidate();return;}
            if (_tab == TabId.Overview && Hit(_activityRect, contentPoint))
            { using (ActivitySettings dialog = new ActivitySettings()) dialog.ShowDialog(this); Invalidate(); return; }
            if (_tab == TabId.Overview && Hit(_sessionsRect, contentPoint))
            { using (SessionDetails dialog = new SessionDetails()) dialog.ShowDialog(this); return; }
            // 1.8.0:点总览的数字卡 → 打开对应报告页(今天)。
            if (_tab == TabId.Overview && e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < _overviewTiles.Count && i < OverviewTileReport.Length; i++)
                {
                    if (!Hit(_overviewTiles[i], contentPoint)) continue;
                    using (StatisticsReport report = new StatisticsReport(DateTime.Today, OverviewTileReport[i], DateTime.Today, DateTime.Today))
                        report.ShowDialog(this);
                    return;
                }
            }
            for (int i = 0; i < _tabNames.Length; i++)
            {
                if (Hit(_tabRects[i], p)) { _tab = (TabId)i; _hover = -1; Invalidate(); return; }
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
                    using(StatisticsReport report=new StatisticsReport(points[index].Day,1))report.ShowDialog(this);
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
            if (id >= 510 && id <= 512) { _clickRange = id - 510; return; }   // 点击热区:今日 / 近 7 天 / 近 30 天
            if (id >= 514 && id <= 516)                                      // 点击热区:跨屏全览 / 选屏
            {
                _clickFocused = id != 515;
                return;
            }
            if (id == 517) { _clickSmooth = !_clickSmooth; _clickStamp = null; return; }   // 像素网格 / 平滑热力
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
            if (id < 70) { ApplyAppChip(id); return; }
            if (id >= 100) { _trendMetric = id - 100; return; }        // 100..103
            if (id >= 90) { _trendRange = (id == 91) ? 14 : 30; return; } // 91=14天 92=30天
            if (id >= 80) { _hourMetric = id - 80; return; }           // 80/81
            if (id >= 70) { _keyRange = id - 70; return; }             // 70/71
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
            bool clickable = Hit(_closeRect, p) || Hit(_exportRect, p) || (_tab==TabId.Trend&&!_trendHourly&&h>=0);
            if (_tab == TabId.Overview) clickable |= Hit(_activityRect, new PointF(p.X - ContentX, p.Y - ContentY))
                || Hit(_sessionsRect, new PointF(p.X - ContentX, p.Y - ContentY))
                || Hit(_distributionMouseRect,new PointF(p.X-ContentX,p.Y-ContentY))
                || Hit(_distributionKeyboardRect,new PointF(p.X-ContentX,p.Y-ContentY));
            if(_tab==TabId.Overview && _overviewKeyboard)clickable |= Hit(_distributionCountRect,new PointF(p.X-ContentX,p.Y-ContentY));
            if(_tab==TabId.Overview && _overviewKeyboard)foreach(RectangleF r in _distributionPeriods) clickable |= Hit(r,new PointF(p.X-ContentX,p.Y-ContentY));
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
                RectangleF r = new RectangleF(Cx, 188, Cw, 312);
                if (r.Contains(p))
                {
                    int idx = (int)((p.X - Cx - 50) / ((Cw - 50 - 16) / 24f));
                    if (idx >= 0 && idx < 24) return idx;
                }
            }
            return -1;
        }

        private sealed class AnalysisPt
        {
            public DateTime Day;
            public double V;
            /// <summary>这天有没有落盘的记录。false = 应用/电脑没运行过,折线断开并用灰带标出。</summary>
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
                p.HasData = Store.History.ContainsKey(r.Date);
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
            g.Restore(contentState);
            // 1.8.0:Tab 选中的 chip 画一圈焦点环。_chips 记的是内容坐标,这里已经恢复成窗口坐标,
            // 所以要补上 ContentX/ContentY 的偏移。
            if (_chipFocus >= 0 && _chipFocus < _chips.Count)
            {
                RectangleF focus = _chips[_chipFocus];
                using (GraphicsPath ring = RoundedRect(focus.X + ContentX - 2, focus.Y + ContentY - 2, focus.Width + 4, focus.Height + 4, 9))
                using (Pen pen = new Pen(Ccyan, 1.8f)) g.DrawPath(pen, ring);
            }
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
            PaintFooter(g);
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
            if(_tab!=TabId.Text && _tab!=TabId.Heat && (_tab!=TabId.Insights || _insightView!=1 || _insightDay.Date==DateTime.Today))InteractionBadge.Draw(g,new RectangleF(336,74,112,23),_fSmall,"● 今日累积中");
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

        private void PaintFooter(Graphics g)
        {
            using (Pen p = new Pen(Cline)) g.DrawLine(p, 200, 666, 1032, 666);
            _exportRect = new RectangleF(912, 680, 120, 28);
            RectangleF reportRect = new RectangleF(778, 680, 124, 28);
            // 字符内容页没有对应的统计报告(那份数据不进 stats.txt),所以不挂这个入口 ——
            // 挂一个点了跳不到正确内容的按钮,比没有更糟。
            if (_tab != TabId.Text && _tab != TabId.Day && _tab != TabId.Heat)
            {
                _chips.Add(reportRect); _chipIds.Add(45);
                PaintChip(g, reportRect, "在报告中查看", false, null);
            }
            else if (_tab == TabId.Text)
            {
                // 字符内容页没有对应的统计报告,这个位置放「重新读取」。
                PaintFooterChip(g, new RectangleF(800, 680, 104, 28), "重新读取", 404);
            }
            if (_tab == TabId.Day)
            {
                // 日报页:导出换成「保存 Markdown / 复制」。
                PaintFooterChip(g, new RectangleF(792, 680, 108, 28), "复制简报", 424);
                PaintFooterChip(g, new RectangleF(908, 680, 124, 28), "保存 Markdown", 423);
                if(!_loaderVisible)
                    AppText(g, StatusText(FooterHint()), _fSmall, Csub, new RectangleF(200, 683, 580, 24), false);
                return;
            }
            bool hov = Hit(_exportRect, ToBase(_mouse));
            using (GraphicsPath p = RoundedRect(_exportRect.X, _exportRect.Y, 120, 28, 8))
            using (SolidBrush b = new SolidBrush(hov ? ArtTheme.Mix(Cblue, Ctext, 0.18) : Cblue)) g.FillPath(b, p);
            using (Pen p = new Pen(ArtTheme.Current.OnAccent, 1.4f))
            {
                g.DrawLine(p, 928, 687, 928, 696);
                g.DrawLine(p, 925, 693, 928, 696);
                g.DrawLine(p, 931, 693, 928, 696);
                g.DrawLine(p, 923, 701, 933, 701);
            }
            using (SolidBrush b = new SolidBrush(ArtTheme.Current.OnAccent)) g.DrawString("导出 CSV", _fChip, b, 947, 685);
            if(!_loaderVisible)
                AppText(g, StatusText(FooterHint()), _fSmall, Csub, new RectangleF(200, 683, 580, 24), false);
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
                    : _tab == TabId.Trend ? "今日仍在累计 · 折线末段为虚线 · 灰色竖带=未运行/无记录，不参与日均与平均线 · 环比按昨日同时段（已完成整小时）"
                    : _tab == TabId.Hours ? (_hourView == 1 ? "行为周一至周日，列为 0–23 点 · " + HourRangeName() + (_hourRange == 0 ? "（进行中）" : " · 按有记录日平均（不含今天）") + " · 悬停看平均值" : "颜色越亮越活跃 · 柱高为数值 · 当前：" + HourRangeName() + " · 悬停看该时段明细")
                    : _tab == TabId.Keys && _keyView == 2 ? (_keyMetric == 1 ? "物理键位热力 · 按住时长口径:逐键累计,悬停看平均与最长 · 右上角切换配列 · 导出同时包含两种口径" : "物理键位热力 · 长按计一次 · 右上角切换配列 · 导出包含配列外已归位按键")
                    : _tab == TabId.Keys && _keyView == 1 ? "修饰键 + 普通键首次按下计 1 次 · 长按去重 · 左右修饰键合并 · 本页导出组合动作"
                    : _tab == TabId.Keys && _keyMetric == 1 ? "按住时长从 1.7.3 起采集 · 长按只计一次 · 可切换时间范围 · 本页导出按住时长明细"
                    : _tab == TabId.Keys ? "一级切换形态(排行/组合/热力) · 二级切换时间与指标 · 导出的 CSV 跟随当前筛选"
                    : _tab == TabId.Heat ? "九宫格按所选屏幕计算 · 单屏放大可看更细的格位 · 位置明细保留 30 天"
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
                else PaintTile(g, x, y, tileW, 88, labels[i], vals[i], cols[i], haloSymbols[i]);
            }

            float cardY = Cy + 2 * 88 + 16 + 16;   // 306
            float cardH = 234;

            // 左卡:鼠标按键分布(累计)
            PaintCard(g, Cx, cardY, 404, cardH, _overviewKeyboard?"键盘按键分布":"鼠标按键分布 · 累计");
            _distributionMouseRect=new RectangleF(Cx+278,cardY+8,52,24);
            _distributionKeyboardRect=new RectangleF(Cx+336,cardY+8,52,24);
            PaintChip(g,_distributionMouseRect,"鼠标",!_overviewKeyboard,null);
            PaintChip(g,_distributionKeyboardRect,"键盘",_overviewKeyboard,null);
            if(_overviewKeyboard)PaintKeyboardDistribution(g,Cx,cardY);
            else
            {
                PaintDonut(g, Cx + 112, cardY + 126, 78,
                    Store.Total.Left, Store.Total.Right, Store.Total.Middle, Store.Total.XButtons);
                PaintMouseLegend(g, Cx + 216, cardY + 42, cardH - 60);
            }

            // 右卡:按键 Top5(累计)
            PaintCard(g, Cx + 404 + 17, cardY, Cw - 404 - 17, cardH, "最常按键 Top 5 · 累计");
            PaintTopKeysMini(g, Cx + 404 + 17 + 20, cardY + 40, Cw - 404 - 17 - 40, cardH - 56);

            // 底部洞察
            PaintInsights(g, Cx, cardY + cardH + 14, Cw, 24);
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
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, y, w, h), portraitGlass?Color.FromArgb(176,Ccard2):Ccard2, portraitGlass?Color.FromArgb(195,Ccard):Ccard, 90f)) g.FillPath(b, p);
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

        private void PaintDonut(Graphics g, float cx, float cy, float r, long left, long right, long mid, long xb, string unit = "次点击")
        {
            long total = left + right + mid + xb;
            RectangleF rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);

            if (total == 0)
            {
                using (Pen pen = new Pen(Color.FromArgb(40, Ctext), 16f))
                    g.DrawArc(pen, rect, 0, 360);
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    SizeF sz = g.MeasureString("暂无数据", _fBody);
                    g.DrawString("暂无数据", _fBody, b, cx - sz.Width / 2, cy - sz.Height / 2);
                }
                return;
            }

            Pen[] pens = {
                new Pen(Cblue, 16f), new Pen(Cgreen, 16f),
                new Pen(Corange, 16f), new Pen(Cpurple, 16f) };
            long[] vals = { left, right, mid, xb };
            float start = -90f;
            // 底环
            using (Pen pen = new Pen(Color.FromArgb(26, Ctext), 16f))
                g.DrawArc(pen, rect, 0, 360);
            if(GujianArt.Active)GujianArt.RingAura(g,rect);
            else if(OriArt.Active)OriArt.RingAura(g,rect);
            for (int i = 0; i < 4; i++)
            {
                if (vals[i] <= 0) continue;
                float sweep = 360f * vals[i] / total;
                if (sweep < 0.4f) sweep = 0.4f;
                g.DrawArc(pens[i], rect, start + Math.Min(1.2f, sweep / 4), Math.Max(0.1f, sweep - Math.Min(2.4f, sweep / 2)));
                start += 360f * vals[i] / total;
            }
            foreach (Pen p in pens) p.Dispose();

            string v = Analysis.FmtCount(total);
            using (SolidBrush b = new SolidBrush(Ctext))
            {
                SizeF sz = g.MeasureString(v, _fNum2);
                g.DrawString(v, _fNum2, b, cx - sz.Width / 2, cy - 17);
            }
            using (SolidBrush b = new SolidBrush(Csub))
            {
                    SizeF sz = g.MeasureString(unit, _fSmall);
                    g.DrawString(unit, _fSmall, b, cx - sz.Width / 2, cy + 7);
            }
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
            double angle=(Math.Atan2(dy,dx)*180/Math.PI+450)%360,end=0;
            for(int i=0;i<values.Length;i++){end+=360.0*values[i]/total;if(values[i]>0 && angle<end)return i;}
            return -1;
        }
        private void PaintKeyboardDistribution(Graphics g,float x,float y)
        {
            _distributionCountRect=new RectangleF(x+169,y+8,94,24);
            PaintChip(g,_distributionCountRect,"Top "+_distributionTop+" ▾",false,null);
            string[] periods={"今日","本周","本月","本年"};
            for(int i=0;i<4;i++)
            {
                _distributionPeriods[i]=new RectangleF(x+12,y+61+i*37,50,28);
                PaintChip(g,_distributionPeriods[i],periods[i],_distributionPeriod==i,null);
            }
            DateTime today=DateTime.Today,start=DistributionStart(today,_distributionPeriod);
            List<DayRecord> days=new List<DayRecord>();
            foreach(DayRecord day in Store.History.Values)if(day.Date>=start && day.Date<=today)days.Add(day);
            List<KeyValuePair<string,long>> rank=Analysis.KeyRanking(days);
            long[] values=new long[_distributionTop+1];string[] names=new string[_distributionTop+1];names[_distributionTop]="其他";long total=0;
            for(int i=0;i<rank.Count;i++)
            {
                total+=rank[i].Value;
                if(i<_distributionTop){values[i]=rank[i].Value;names[i]=rank[i].Key;}
                else values[_distributionTop]+=rank[i].Value;
            }
            // 1.8.0:扇区颜色绑按键名,不绑扇区顺序。切区间后同一个键的颜色不变。
            List<string> sliceNames = new List<string>();
            for (int i = 0; i < _distributionTop; i++) sliceNames.Add(names[i]);
            Color[] colors = new Color[_distributionTop + 1];
            Color[] rankColors = KeyPalette.ForRanking(sliceNames);
            for (int i = 0; i < _distributionTop; i++) colors[i] = rankColors[i];
            colors[_distributionTop] = Csub;   // 「其他」始终是中性灰,不参与身份色
            RectangleF ring=new RectangleF(x+82,y+43,176,176);
            PointF pointer=ToBase(_mouse);pointer.X-=ContentX;pointer.Y-=ContentY;
            int hovered=DistributionHit(pointer,ring,18,values);
            using(Pen pen=new Pen(Color.FromArgb(26,Ctext),18))g.DrawEllipse(pen,ring);
            if(total>0)
            {
                float angle=-90;
                for(int i=0;i<values.Length;i++)if(values[i]>0)
                {
                    float sweep=(float)(360.0*values[i]/total);
                    using(Pen pen=new Pen(i==hovered?ArtTheme.Mix(colors[i],Ctext,.25):colors[i],i==hovered?22:18))g.DrawArc(pen,ring,angle,sweep);
                    angle+=sweep;
                }
            }
            using(StringFormat center=new StringFormat{Alignment=StringAlignment.Center})
            using(SolidBrush text=new SolidBrush(Ctext))using(SolidBrush sub=new SolidBrush(Csub))
            {
                if(hovered>=0)
                {
                    g.DrawString(names[hovered],_fBody,text,new RectangleF(x+117,y+91,106,24),center);
                    g.DrawString(Analysis.FmtCount(values[hovered])+" 次",_fNum2,text,new RectangleF(x+110,y+117,120,32),center);
                    g.DrawString("占比 "+(100.0*values[hovered]/total).ToString("0.0",CultureInfo.InvariantCulture)+"%",_fSmall,sub,new RectangleF(x+117,y+150,106,22),center);
                }
                else
                {
                    g.DrawString(total>0?Analysis.FmtCount(total):"暂无数据",_fNum2,text,new RectangleF(x+110,y+110,120,32),center);
                    g.DrawString("次已记录击键",_fAxis,sub,new RectangleF(x+114,y+144,112,19),center);
                }
            }
            int rows=(_distributionTop+1)/2;
            for(int i=0;i<Math.Min(_distributionTop,rank.Count);i++)
            {
                float row=y+66+(i%rows)*29,column=x+278+(i/rows)*60;
                using(SolidBrush brush=new SolidBrush(colors[i]))g.FillEllipse(brush,column,row+5,9,9);
                AppText(g,names[i],_fTab,i==hovered?colors[i]:Ctext,new RectangleF(column+13,row,46,24),false);
            }
        }
        private void PaintMouseLegend(Graphics g, float x, float y, float h)
        {
            long[] vals = { Store.Total.Left, Store.Total.Right, Store.Total.Middle, Store.Total.XButtons };
            string[] names = { "左键", "右键", "中键", "侧键" };
            Color[] cols = { Cblue, Cgreen, Corange, Cpurple };
            long total = vals[0] + vals[1] + vals[2] + vals[3];

            float rowY = y;
            for (int i = 0; i < 4; i++)
            {
                using (SolidBrush b = new SolidBrush(cols[i]))
                    g.FillEllipse(b, x, rowY + 5, 9, 9);
                using (SolidBrush b = new SolidBrush(Ctext))
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
            List<KeyValuePair<string, long>> rank = Analysis.KeyRanking(Store.History.Values);
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
            AppChip(g, 300, Cx + (_trendHourly ? 398 : 412), 98, _trendHourly ? 58 : 80, "按天", !_trendHourly);
            AppChip(g, 301, Cx + (_trendHourly ? 462 : 502), 98, _trendHourly ? 68 : 80, "按小时", _trendHourly);
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
            float rx = Cx + Cw - 2 * 88 - 10;
            for (int i = 0; i < 2; i++)
            {
                RectangleF r = new RectangleF(rx, 98, 88, 30);
                _chips.Add(r); _chipIds.Add(91 + i);
                PaintChip(g, r, ranges[i], _trendRange == (i == 0 ? 14 : 30), null);
                rx += 98;
            }

            List<AnalysisPt> pts = TrendPoints();
            PaintLineChart(g, new RectangleF(Cx, 150, Cw, 360), pts);

            // 汇总卡:今天仍在累计,所以「日均」只算完整且有记录的天,「较昨日」改成「较昨日同期」。
            bool live = pts.Count > 0 && pts[pts.Count - 1].Day == DateTime.Today;
            double sum = 0, max = 0, completeSum = 0;
            int completeCount = 0;
            foreach (AnalysisPt p in pts)
            {
                sum += p.V;
                if (p.V > max) max = p.V;
                if (p.HasData && (!live || p.Day != DateTime.Today)) { completeSum += p.V; completeCount++; }
            }
            double avg = completeCount > 0 ? completeSum / completeCount : (pts.Count > 0 ? sum / pts.Count : 0);
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
                _trendMetric == 3 ? Analysis.FmtMeters(avg) : Analysis.FmtCount((long)avg),
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
                    ink = compare.Available ? (compare.Percent.HasValue && compare.Percent.Value < 0 ? Cred : Cgreen) : Csub;
                using (SolidBrush b = new SolidBrush(ink))
                    g.DrawString(sVals[i], _fNum2, b, x + 16, 548);
                if (i == 3 && compare != null)
                    AppText(g, TrendCutoff.NoteText(compare), _fAxis, Csub, new RectangleF(x + 16, 571, tileW - 28, 12), false);
            }
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
            using (GraphicsPath p = RoundedRect(r.X, r.Y, r.Width, r.Height, 8))
            {
                using (SolidBrush b = new SolidBrush(sel ? Cblue : hov ? Ccard2 : Ccard)) g.FillPath(b, p);
                using (Pen pen = new Pen(sel ? Cblue : Cline)) g.DrawPath(pen, p);
            }
            using (SolidBrush b = new SolidBrush(sel ? ArtTheme.Current.OnAccent : hov ? Ctext : Csub))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, _fChip, b, r, format);
        }

        private void PaintLineChart(Graphics g, RectangleF rect, List<AnalysisPt> pts)
        {
            PaintCard(g, rect.X, rect.Y, rect.Width, rect.Height,
                _trendMetric == 3 ? "每日鼠标路程（估算，纵轴单位 m）" :
                string.Format("每日{0}", _trendMetric == 0 ? "击键" : _trendMetric == 1 ? "点击" : "滚轮"));
            if(_trendMetric==0)AppText(g,"橙圈：历史高值 ≥2σ · 今日不标异常",_fSmall,Corange,new RectangleF(rect.X+164,rect.Y+12,400,22),false);

            float left = rect.X + 56, right = rect.X + rect.Width - 16;
            float top = rect.Y + 44, bottom = rect.Y + rect.Height - 30;
            float ph = bottom - top, pw = right - left;

            double rawMax = 10;
            foreach (AnalysisPt p in pts) if (p.V > rawMax) rawMax = p.V;
            foreach (AnalysisPt p in pts) { double moving = PersonalStats.MovingAverage(p.Day, _trendMetric); if (!double.IsNaN(moving) && moving > rawMax) rawMax = moving; }
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

            // 平均线:只算完整且有记录的天。
            double sum = 0;
            int avgCount = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                if (!pts[i].HasData) continue;
                if (live && i == pts.Count - 1) continue;
                sum += pts[i].V;
                avgCount++;
            }
            double avg = avgCount > 0 ? sum / avgCount : (pts.Count > 0 ? pts[0].V : 0);
            using (Pen pen = new Pen(Color.FromArgb(90, Ctext)))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, left, py(avg), right, py(avg));
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(140, Csub)))
                g.DrawString("平均 " + (_trendMetric == 3 ? Analysis.FmtMeters(avg) : Analysis.FmtAxis(avg)),
                    _fAxis, b, right - 70, py(avg) - 16);

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
            // 未运行的日子两侧不连线;今天的最后一段单独用虚线。
            double grow = Entrance();
            using (Pen pen = new Pen(main, 2f))
            {
                pen.LineJoin = LineJoin.Round;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (!pts[i].HasData || !pts[i + 1].HasData) continue;
                    if (live && i + 1 == pts.Count - 1) continue;
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

            // Trailing seven-day mean needs seven observed days; missing dates break the overlay.
            using (Pen movingPen = new Pen(Ccyan, 2f) { DashStyle = DashStyle.Dash })
            {
                double previousMean = double.NaN;
                for (int i = 0; i < pts.Count; i++)
                {
                    double moving = PersonalStats.MovingAverage(pts[i].Day, _trendMetric);
                    if (i > 0 && !double.IsNaN(previousMean) && !double.IsNaN(moving)) g.DrawLine(movingPen, px(i - 1), py(previousMean), px(i), py(moving));
                    previousMean = moving;
                }
            }
            using (SolidBrush legend = new SolidBrush(Ccyan)) g.DrawString("虚线：7 日移动均值", _fSmall, legend, rect.Right - 162, rect.Y + 12);

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
                using(Pen ring=new Pen(Ctext,2))g.DrawEllipse(ring,hx-8,hy-8,16,16);
                using (SolidBrush b = new SolidBrush(main))
                    g.FillEllipse(b, hx - 4.5f, hy - 4.5f, 9, 9);

                string l1 = hp.Day.ToString("MM-dd") + (hoverToday ? " · 进行中" : hp.HasData ? " · 点击查看日报" : " · 未运行");
                string l2 = !hp.HasData
                    ? "无记录"
                    : _trendMetric == 3
                        ? Analysis.FmtMeters(hp.V)
                        : Analysis.FmtCount((long)hp.V) + " " + (_trendMetric == 0 ? "击键" : _trendMetric == 1 ? "点击" : "滚动");
                string l3 = hoverToday
                    ? "截至 " + DateTime.Now.ToString("HH:mm") + "，不要与整日比较"
                    : !hp.HasData
                        ? "应用或电脑没有运行，这一天没有落盘记录"
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
            for (int i = 0; i < 3; i++)
            {
                RectangleF r = new RectangleF(Cx + i * 80, 98, 76, 30);
                _chips.Add(r); _chipIds.Add(80 + i);
                PaintChip(g, r, metrics[i], _hourMetric == i, null);
            }
            // 时间跨度对所有指标一致,不再只在「活跃时长」下凭空出现。
            string[] ranges = { "今日", "近 7 天", "全部历史" };
            float[] rangeX = { Cx + 250, Cx + 328, Cx + 414 };
            float[] rangeW = { 72, 80, 88 };
            for (int i = 0; i < ranges.Length; i++)
            {
                RectangleF r = new RectangleF(rangeX[i], 98, rangeW[i], 30);
                _chips.Add(r); _chipIds.Add(30 + i);
                PaintChip(g, r, ranges[i], _hourRange == i, null);
            }
            AppChip(g, 203, Cx + 520, 98, 88, "时段柱子", _hourView == 0);
            AppChip(g, 204, Cx + 614, 98, 96, "周×24 矩阵", _hourView == 1);
            AppChip(g, 202, Cx + Cw - 112, 98, 112, "七日活跃对比", false);

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
                    using (SolidBrush b = new SolidBrush(Csub))
                    {
                        string t = i.ToString("00");
                        g.DrawString(t, _fAxis, b, left + slot * i + slot / 2 - 8, bottom + 6);
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

            using (SolidBrush b = new SolidBrush(Csub))
            {
                for (int h = 0; h < 24; h += 3)
                    g.DrawString(h.ToString("00"), _fAxis, b, left + cw * h + cw / 2 - 7, top - 17);
                g.DrawString("23", _fAxis, b, left + cw * 23 + cw / 2 - 7, top - 17);
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
            // 一级主视图:看什么形态。
            string[] views = { "按键排行", "组合键排行", "全键热力图" };
            float[] viewX = { Cx, Cx + 102, Cx + 212 };
            float[] viewW = { 96, 104, 104 };
            for (int i = 0; i < views.Length; i++)
            {
                RectangleF r = new RectangleF(viewX[i], 98, viewW[i], 30);
                _chips.Add(r); _chipIds.Add(72 + i);
                PaintChip(g, r, views[i], _keyView == i, null);
            }

            // 二级筛选:时间范围 + 指标口径,对列表与热力图共用。
            string[] ranges = { "今日", "近7天", "近30天", "近90天" };
            int[] rangeIds = { 70, 76, 77, 71 }, rangeValues = { 0, 2, 3, 1 };
            float[] rangeX = { Cx + 336, Cx + 406, Cx + 488, Cx + 578 };
            float[] rangeW = { 64, 76, 84, 84 };
            for (int i = 0; i < ranges.Length; i++)
            {
                RectangleF r = new RectangleF(rangeX[i], 98, rangeW[i], 30);
                _chips.Add(r); _chipIds.Add(rangeIds[i]);
                PaintChip(g, r, ranges[i], _keyRange == rangeValues[i], null);
            }
            if (_keyView != 1)
            {
                RectangleF countChoice = new RectangleF(Cx + 676, 98, 66, 30);
                _chips.Add(countChoice); _chipIds.Add(78);
                PaintChip(g, countChoice, "击键次数", _keyMetric == 0, null);
                RectangleF holdChoice = new RectangleF(Cx + 748, 98, 66, 30);
                _chips.Add(holdChoice); _chipIds.Add(79);
                PaintChip(g, holdChoice, "按住时长", _keyMetric == 1, null);
            }

            IEnumerable<DayRecord> src = KeyRangeDays();
            if (_keyView == 2) { PaintKeyboardHeatmap(g, src); return; }
            if (_keyView == 0 && _keyMetric == 1) { PaintHoldRanking(g); return; }

            bool combos = _keyView == 1;
            List<KeyValuePair<string, long>> rank = combos ? ShortcutStats.Ranking(src) : Analysis.KeyRanking(src);
            long total = 0;
            foreach (KeyValuePair<string, long> item in rank) total += item.Value;

            // 左侧:排行表格(排名 / 按键 / 频次 / 占比 / 平均按住 / 迷你条)
            PaintCard(g, Cx, 138, 560, 438, null);
            float xRank = Cx + 16, xKey = Cx + 42, xCount = Cx + 126, xShare = Cx + 200, xHold = Cx + 260, xBar = Cx + 360;
            float barW = 174;
            if (combos) { xCount = Cx + 200; xShare = Cx + 278; xBar = Cx + 344; barW = 190; }
            using (SolidBrush head = new SolidBrush(Csub))
            {
                g.DrawString("排名", _fSmall, head, xRank, 148);
                g.DrawString(combos ? "组合" : "按键", _fSmall, head, xKey, 148);
                SizeF sz = g.MeasureString("频次", _fSmall);
                g.DrawString("频次", _fSmall, head, xCount + (combos ? 70 : 66) - sz.Width, 148);
                sz = g.MeasureString("占比", _fSmall);
                g.DrawString("占比", _fSmall, head, xShare + (combos ? 54 : 52) - sz.Width, 148);
                if (!combos)
                {
                    sz = g.MeasureString("平均按住", _fSmall);
                    g.DrawString("平均按住", _fSmall, head, xHold + 86 - sz.Width, 148);
                }
                g.DrawString("频次条", _fSmall, head, xBar, 148);
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
                for (int i = 0; i < n; i++)
                {
                    float ry = 176 + i * 37;
                    using (SolidBrush b = new SolidBrush(i < 3 ? Color.FromArgb(70, Cblue) : Color.FromArgb(26, Ctext)))
                        g.FillEllipse(b, xRank, ry + 3, 18, 18);
                    using (SolidBrush b = new SolidBrush(i < 3 ? Ctext : Csub))
                    {
                        string t = (i + 1).ToString();
                        SizeF sz = g.MeasureString(t, _fSmall);
                        g.DrawString(t, _fSmall, b, xRank + 9 - sz.Width / 2, ry + 5);
                    }
                    AppText(g, rank[i].Key, _fBody, Ctext,
                        new RectangleF(xKey, ry, combos ? 150 : 74, 22), !combos);
                    AppText(g, Analysis.FmtCount(rank[i].Value), _fBody, Ctext,
                        new RectangleF(xCount, ry, combos ? 70 : 66, 22), true);
                    double share = total > 0 ? rank[i].Value * 100.0 / total : 0;
                    AppText(g, share.ToString("0.0", CultureInfo.InvariantCulture) + "%", _fSmall, Csub,
                        new RectangleF(xShare, ry + 2, combos ? 54 : 52, 20), true);
                    if (!combos)
                        AppText(g, AverageHoldText(rank[i].Key), _fSmall, Csub, new RectangleF(xHold, ry + 2, 86, 20), true);

                    using (SolidBrush track = new SolidBrush(Color.FromArgb(18, Ctext)))
                        g.FillRectangle(track, xBar, ry + 9, barW, 8);
                    float frac = max > 0 ? (float)rank[i].Value / max : 0;
                    frac *= (float)Entrance();
                    Color c = i == 0 ? Cblue : i == 1 ? Ccyan : i == 2 ? Cgreen : Color.FromArgb(150, Csub);
                    using (GraphicsPath bp = RoundedRect(xBar, ry + 9, Math.Max(4, barW * frac), 8, 4))
                    using (SolidBrush b = new SolidBrush(c))
                        g.FillPath(b, bp);
                }
                AppText(g, "共 " + rank.Count + " 种 · 总" + (combos ? "组合动作 " : "击键 ") + Analysis.FmtCount(total)
                    + " · 前三占比 " + Top3Pct(rank, total), _fSmall, Csub, new RectangleF(Cx + 16, 548, 520, 18), false);
            }

            // 右侧:Top 11–20 + 概况摘要
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
                for (int i = 0; i < shown; i++)
                {
                    int index = 10 + i;
                    float ry = 196 + i * 23;
                    AppText(g, (index + 1).ToString(), _fAxis, Csub, new RectangleF(rx + 16, ry, 24, 18), false);
                    AppText(g, rank[index].Key, _fSmall, Ctext, new RectangleF(rx + 42, ry, rw - 130, 18), false);
                    AppText(g, Analysis.FmtCount(rank[index].Value), _fAxis, Csub, new RectangleF(rx + rw - 76, ry, 60, 18), true);
                }
            }
            using (Pen pen = new Pen(Color.FromArgb(20, Ctext))) g.DrawLine(pen, rx + 16, 430, rx + rw - 16, 430);
            AppText(g, "共 " + rank.Count + " 种 · 总" + (combos ? "动作 " : "击键 ") + Analysis.FmtCount(total),
                _fSmall, Csub, new RectangleF(rx + 16, 440, rw - 32, 18), false);
            AppText(g, "最常用 " + (rank.Count > 0 ? rank[0].Key : "--") + " · 前三 " + Top3Pct(rank, total),
                _fSmall, Csub, new RectangleF(rx + 16, 462, rw - 32, 18), false);
            AppText(g, "记录 " + keyDays + " 天 · " + (combos ? "长按计一次" : "键鼠比 " + MouseKeyRatio(src)),
                _fSmall, Csub, new RectangleF(rx + 16, 484, rw - 32, 18), false);
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
            if (_tab == TabId.Heat) { ExportClickHeat(); return; }
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
                List<DateTime> days = new List<DateTime>(Store.History.Keys);
                days.Sort();
                foreach (DateTime d in days)
                {
                    DayRecord r = Store.History[d];
                    if (r.IsEmpty) continue;
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






