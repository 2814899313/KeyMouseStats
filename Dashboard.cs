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
                case 0xC0: return "`";
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

        private enum TabId { Overview = 0, Trend = 1, Hours = 2, Keys = 3, Apps = 4, Insights = 5 }
        private readonly string[] _tabNames = { "总览", "趋势", "时段分布", "按键排行", "应用统计", "使用洞察" };

        private TabId _tab = TabId.Overview;
        private int _trendMetric = 0;     // 0击键 1点击 2滚轮 3移动
        private int _trendRange = 14;     // 14 / 30
        private int _hourMetric = 0;      // 0击键 1点击
        private bool _activeHourToday = true;
        private RectangleF _activityRect, _sessionsRect;
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
        private bool _showCombos;
        private int _hover = -1;          // 图表悬停索引

        private readonly RectangleF[] _tabRects = new RectangleF[6];
        private readonly List<RectangleF> _chips = new List<RectangleF>();
        private readonly List<int> _chipIds = new List<int>();
        private RectangleF _closeRect, _exportRect;

        private float _s = 1f;
        private Font _fTitle, _fTab, _fH2, _fBody, _fSmall, _fAxis, _fNum, _fNum2, _fChip;
        private System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _hoverPaintTimer = new System.Windows.Forms.Timer();
        private Point _mouse;

        public Dashboard()
        {
            Text = "键鼠统计 1.3.1 - 数据分析";
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
            _hoverPaintTimer.Tick+=delegate { _hoverPaintTimer.Stop();if(Visible)Invalidate(); };
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
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode == Keys.D1) { _tab = TabId.Overview; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D2) { _tab = TabId.Trend; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D3) { _tab = TabId.Hours; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D4) { _tab = TabId.Keys; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D5) { _tab = TabId.Apps; _hover = -1; Invalidate(); }
            else if (e.KeyCode == Keys.D6) { _tab = TabId.Insights; _hover = -1; Invalidate(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _loadingTimer.Stop();_loadingTimer.Dispose();
            base.OnFormClosed(e);
            foreach(ContextMenuStrip menu in new[]{_keyboardMenu,_categoryMenu,_distributionMenu})if(menu!=null)menu.Dispose();
            _timer.Stop();
            _timer.Dispose();
            _hoverPaintTimer.Stop();_hoverPaintTimer.Dispose();
            if(_keyboardPlate!=null){_keyboardPlate.Dispose();_keyboardPlate=null;}
            foreach (Font font in new Font[] { _fTitle, _fTab, _fH2, _fBody, _fSmall, _fAxis, _fNum, _fNum2, _fChip })
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
                    Invalidate();
                    return;
                }
            }
            if(_tab==TabId.Trend && !_trendHourly){_mouse=e.Location;int index=ComputeHover();List<AnalysisPt> points=TrendPoints();if(index>=0 && index<points.Count)using(StatisticsReport report=new StatisticsReport(points[index].Day,1))report.ShowDialog(this); }
        }

        private void ApplyChip(int id)
        {
            if (id >= 300) { ApplyHourlyTrendChip(id); return; }
            if (id >= 200) { ApplyInsightChip(id); return; }
            if (_tab == TabId.Keys && (id == 76 || id == 77)) { _keyRange=id-74;return; }
            if (_tab == TabId.Keys && id == 75) { KeyboardMenu(); return; }
            if (_tab == TabId.Keys && id == 74) { _showKeyboardHeatmap = true; return; }
            if (_tab == TabId.Keys && (id == 72 || id == 73)) { _showKeyboardHeatmap = false; _showCombos = id == 73; return; }
            if (_tab == TabId.Hours && (id == 30 || id == 31)) { _activeHourToday = id == 30; return; }
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
            bool clickable = Hit(_closeRect, p) || Hit(_exportRect, p) || (_tab==TabId.Trend&&!_trendHourly&&h>=0);
            if (_tab == TabId.Overview) clickable |= Hit(_activityRect, new PointF(p.X - ContentX, p.Y - ContentY))
                || Hit(_sessionsRect, new PointF(p.X - ContentX, p.Y - ContentY))
                || Hit(_distributionMouseRect,new PointF(p.X-ContentX,p.Y-ContentY))
                || Hit(_distributionKeyboardRect,new PointF(p.X-ContentX,p.Y-ContentY));
            if(_tab==TabId.Overview && _overviewKeyboard)clickable |= Hit(_distributionCountRect,new PointF(p.X-ContentX,p.Y-ContentY));
            if(_tab==TabId.Overview && _overviewKeyboard)foreach(RectangleF r in _distributionPeriods)clickable |= Hit(r,new PointF(p.X-ContentX,p.Y-ContentY));
            foreach (RectangleF r in _tabRects) clickable |= Hit(r, p);
            foreach (RectangleF r in _themeRects) clickable |= Hit(r, p);
            foreach (RectangleF r in _chips) clickable |= Hit(r, new PointF(p.X - ContentX, p.Y - ContentY));
            if (_tab == TabId.Apps)
                foreach (RectangleF r in _appEditRects) clickable |= Hit(r, new PointF(p.X - ContentX, p.Y - ContentY));
            if (_tab == TabId.Insights)
                foreach (RectangleF r in _insightDates.Keys) clickable |= Hit(r, new PointF(p.X - ContentX, p.Y - ContentY));
            Cursor = clickable ? Cursors.Hand : Cursors.Default;
            if(_tab==TabId.Keys && _showKeyboardHeatmap)
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
        }

        private List<AnalysisPt> TrendPoints()
        {
            List<DayRecord> rs = Analysis.RangeDays(_trendRange);
            List<AnalysisPt> pts = new List<AnalysisPt>(rs.Count);
            foreach (DayRecord r in rs)
            {
                AnalysisPt p = new AnalysisPt();
                p.Day = r.Date;
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
            g.TranslateTransform(ContentX, ContentY);
            switch (_tab)
            {
                case TabId.Overview: PaintOverview(g); break;
                case TabId.Trend: PaintTrend(g); break;
                case TabId.Hours: PaintHours(g); break;
                case TabId.Keys: PaintKeys(g); break;
                case TabId.Apps: PaintApps(g); break;
                default: PaintInsights(g); break;
            }
            g.Restore(contentState);
            _loaderVisible=ThemeImages.Loading;
            PaintFooter(g);
            if(_loaderVisible)PaintLoading(g);
            renderWatch.Stop();
            _hoverPaintTimer.Interval=Math.Max(40,Math.Min(150,(int)renderWatch.ElapsedMilliseconds*2));
        }

        private readonly Timer _loadingTimer=new Timer { Interval=80 };
        private int _imageRevision, _loadingFrame;
        private bool _loaderVisible;
        private void PaintLoading(Graphics g)
        {
            float x=200,y=BH-42;
            using(SolidBrush background=new SolidBrush(Ccard))g.FillRectangle(background,x,y,270,32);
            string label=WuxiaArt.Active?"正在展开江湖绘卷":BalatroArt.Active?"正在洗牌与准备牌桌":MinecraftArt.Active?"正在构建方块世界":ResidentArt.Active?"正在读取幸存者档案":HaloArt.Active?"正在连接战术终端":NikkiArt.Active?"正在搭配奇迹衣橱":"正在准备界面";
            using(SolidBrush ink=new SolidBrush(Csub))g.DrawString(label,_fSmall,ink,x+44,y+8);
            for(int i=0;i<8;i++)
            {
                int alpha=55+((i+_loadingFrame)%8)*25;
                using(SolidBrush brush=new SolidBrush(Color.FromArgb(alpha,Cblue)))
                {
                    double angle=i*Math.PI/4;float px=x+19+(float)Math.Cos(angle)*10,py=y+16+(float)Math.Sin(angle)*10;
                    if(WuxiaArt.Active){using(Pen petal=new Pen(Color.FromArgb(alpha,Corange),1.2f)){g.DrawArc(petal,px-3,py-2,4,6,210,210);g.DrawArc(petal,px,py-2,4,6,120,210);}}
                    else if(MinecraftArt.Active)g.FillRectangle(brush,px-2,py-2,5,5);
                    else if(NikkiArt.Active)NikkiArt.Star(g,px,py,3,Color.FromArgb(alpha,Cpurple));
                    else g.FillEllipse(brush,px-2,py-2,4,4);
                }
            }
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
            using (SolidBrush b = new SolidBrush(Ctext)) g.DrawString("键鼠统计", _fH2, b, 62, 28);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(WuxiaArt.Active?"江湖行迹":BalatroArt.Active?"JOKER ACTIVITY":MinecraftArt.Active?"BLOCK ACTIVITY":ResidentArt.Active?"SURVIVOR ARCHIVE":ThemeArt.Dark?"SPARTAN ACTIVITY":"ACTIVITY MONITOR", _fAxis, b, 62, 48);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString("数据分析", _fSmall, b, 24, 113);

            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString((WuxiaArt.Active?"碧血丹心  /  ":BalatroArt.Active?"幻彩牌桌  /  ":MinecraftArt.Active?"方块世界  /  ":ResidentArt.Active?"幸存者档案  /  ":ThemeArt.Dark?"战术终端  /  ":"工作空间  /  ") + _tabNames[(int)_tab], _fSmall, b, 200, 24);
            using (SolidBrush b = new SolidBrush(Ctext)) g.DrawString(_tabNames[(int)_tab], _fTitle, b, 198, 61);
            if(_tab!=TabId.Insights || _insightView!=1 || _insightDay.Date==DateTime.Today)InteractionBadge.Draw(g,new RectangleF(336,74,112,23),_fSmall,"● 今日累积中");
            string[] descriptions = {
                "每一次输入，都有迹可循。查看今天的活动与使用习惯。",
                "沿着时间轴，发现每天的节奏与变化。",
                "找到最活跃的时刻，了解一天中的输入分布。",
                "从常用按键中，发现你的操作习惯。",
                "这些输入用在了哪里？按应用与窗口，拆解你的每一次操作。",
                "从每天的使用节奏，发现活跃时刻、连续使用段与休息间隔。" };
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(descriptions[(int)_tab], _fBody, b, 200, 104);
            GraphicsState clockState=g.Save();if(ResidentArt.Active)g.TranslateTransform(-398,-52);
            using (GraphicsPath p = RoundedRect(828, 72, 204, 42, 12))
            using (SolidBrush b = new SolidBrush(Ccard)) g.FillPath(b, p);
            using (SolidBrush b = new SolidBrush(Cgreen)) g.FillEllipse(b, 842, 88, 6, 6);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(DateTime.Now.ToString("yyyy.MM.dd   HH:mm:ss"), _fBody, b, 858, 82);
            g.Restore(clockState);
            if(NikkiArt.Active)
            {
                int[] emotes={1,2,3,0,4,5};
                int emote=_tab==TabId.Insights&&_insightView==1?3:emotes[(int)_tab];
                NikkiArt.Sticker(g,emote,new RectangleF(716,7,110,110));
            }
            if(ThemeArt.Dark)
            {
                ThemeArt.Symbol(g,WuxiaArt.Active?WuxiaArt.NavSymbols[(int)_tab]:BalatroArt.Active?BalatroArt.NavSymbols[(int)_tab]:(int)_tab,ResidentArt.Active?new RectangleF(646,6,64,64):new RectangleF(742,4,78,78));
                AppText(g,(WuxiaArt.Active?WuxiaArt.PageLabels:BalatroArt.Active?BalatroArt.PageLabels:MinecraftArt.Active?MinecraftArt.PageLabels:ResidentArt.Active?ResidentArt.PageLabels:HaloArt.PageLabels)[(int)_tab],_fSmall,WuxiaArt.Active?Corange:Ccyan,ResidentArt.Active?new RectangleF(452,64,180,22):new RectangleF(646,78,180,22),true);
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
                RectangleF r = new RectangleF(14, 146 + i * 54, 148, 44);
                _tabRects[i] = r;
                bool sel = (int)_tab == i;
                bool hov = Hit(r, ToBase(_mouse));
                if (sel || hov)
                    using (GraphicsPath p = RoundedRect(r.X, r.Y, r.Width, r.Height, 10))
                    using (SolidBrush b = new SolidBrush(sel ? ArtTheme.Mix(Ccard, Cblue, 0.18) : Ccard)) g.FillPath(b, p);
                Color color = sel ? Cblue : hov ? Ctext : Csub;
                if(ThemeArt.Dark)ThemeArt.Symbol(g,WuxiaArt.Active?WuxiaArt.NavSymbols[i]:BalatroArt.Active?BalatroArt.NavSymbols[i]:i,new RectangleF(23,r.Y+7,30,30));else DrawGlyph(g, i, 28, r.Y + 14, color);
                using (SolidBrush b = new SolidBrush(color)) g.DrawString(_tabNames[i], _fTab, b, 56, r.Y + 11);
                if (sel)
                    using (SolidBrush b = new SolidBrush(Cblue)) g.FillRectangle(b, 14, r.Y + 13, 3, 18);
            }
            using (Pen p = new Pen(Cline)) g.DrawLine(p, 24, BH - 140, 152, BH - 140);
            using (SolidBrush b = new SolidBrush(Csub))
            {
                g.DrawString("快捷操作", _fSmall, b, 24, BH - 121);
                g.DrawString("1 — 6   切换页面", _fSmall, b, 24, BH - 96);
                g.DrawString("Esc      关闭面板", _fSmall, b, 24, BH - 73);
            }
            using (SolidBrush b = new SolidBrush(Cgreen)) g.FillEllipse(b, 24, BH - 30, 6, 6);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString("1.3.1 · 本地记录 365 天", _fSmall, b, 38, BH - 36);
        }

        private void PaintFooter(Graphics g)
        {
            using (Pen p = new Pen(Cline)) g.DrawLine(p, 200, 666, 1032, 666);
            _exportRect = new RectangleF(912, 680, 120, 28);
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
            if(!_loaderVisible)using (SolidBrush b = new SolidBrush(Csub))
                g.DrawString(_tab == TabId.Apps ? "按捕获时的前台窗口归因 · 分类可手动调整 · 本页导出应用明细"
                    : _tab == TabId.Insights ? "活跃不等于专注 · 空白可能未采集 · 导出 " + _insightDay.ToString("yyyy-MM-dd") + " 的时间区间"
                    : _tab == TabId.Trend && _trendHourly ? "均值不含当前小时 · 数值仅为已采集部分 · 本页导出小时明细"
                    : _tab == TabId.Keys && _showKeyboardHeatmap ? "物理键位热力 · 长按计一次 · 右上角切换配列 · 导出包含配列外已归位按键"
                    : _tab == TabId.Keys && _showCombos ? "修饰键 + 普通键首次按下计 1 次 · 长按去重 · 左右修饰键合并 · 本页导出组合动作"
                    : _tab == TabId.Overview ? "本段 " + (ActivityMonitor.CurrentSession == null ? "--" : ActivityMonitor.FormatDuration(ActivityMonitor.CurrentSession.Seconds)) + " · 今日峰值 " + Store.Today.PeakApm + " APM · 实时键 " + LiveRate.KeysPerMin + " / 点 " + LiveRate.ClicksPerMin
                    : "鼠标路程为 DPI 估算值 · 设置后开始累计 · 原光标路程见导出文件", _fSmall, b, 200, 687);
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
                Analysis.FmtCount(today.Keys),
                Analysis.FmtCount(today.Clicks),
                Analysis.FmtCount(today.Wheel),
                Analysis.FmtDistance(today.MoveMeters),
                ActivityMonitor.FormatDuration(today.ActiveSeconds),
                today.Sessions.Count + " 段",
                LiveRate.KeysPerMin.ToString("N0", CultureInfo.InvariantCulture),
                LiveRate.Apm.ToString("N0", CultureInfo.InvariantCulture) };
            Color[] cols = { Cblue, Cgreen, Corange, Cpurple, Cgreen, Cblue, Ccyan, Cred };
            int[] haloSymbols = WuxiaArt.Active?WuxiaArt.MetricSymbols:BalatroArt.Active?new[]{0,1,2,3,4,5,6,7}:new[]{3,6,7,8,9,10,1,11};

            float gap = 16, tileW = (Cw - 3 * gap) / 4f;
            _activityRect = new RectangleF(Cx, Cy + 104, tileW, 88);
            _sessionsRect = new RectangleF(Cx + tileW + gap, Cy + 104, tileW, 88);
            for (int i = 0; i < 8; i++)
            {
                int col = i % 4, row = i / 4;
                float x = Cx + col * (tileW + gap);
                float y = Cy + row * (88 + 16);
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

        private void PaintTile(Graphics g, float x, float y, float w, float h, string label, string value, Color c, int haloSymbol = 1)
        {
            PaintCardBase(g, x, y, w, h);
            using (SolidBrush b = new SolidBrush(Csub)) g.DrawString(label, _fBody, b, x + 18, y + 13);
            Font valueFont = g.MeasureString(value, _fNum).Width > w - 32 ? _fNum2 : _fNum;
            using (SolidBrush b = new SolidBrush(Ctext)) g.DrawString(value, valueFont, b, x + 16, y + 36);
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
                if(ThemeArt.Dark && !WuxiaArt.Active)
                    using(Pen edge=new Pen(Ccyan,2)) {g.DrawLine(edge,x+8,y,x+38,y);g.DrawLine(edge,x+8,y,x+8,y+7);}
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
            Color[] colors={Cblue,Cgreen,Corange,Cpurple,Ccyan,Cred,ArtTheme.Mix(Cgreen,Corange,.5),ArtTheme.Mix(Cpurple,Ccyan,.5),ArtTheme.Mix(Corange,Cred,.5),ArtTheme.Mix(Cblue,Cpurple,.5),Csub};
            colors[_distributionTop]=Csub;
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
            Color[] cols = { Cblue, Ccyan, Cgreen, Corange, Cpurple };
            int n = Math.Min(5, rank.Count);
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

            // 汇总卡
            double sum = 0, max = 0;
            foreach (AnalysisPt p in pts) { sum += p.V; if (p.V > max) max = p.V; }
            double avg = pts.Count > 0 ? sum / pts.Count : 0;
            double last = pts.Count >= 2 ? pts[pts.Count - 2].V : 0;
            double cur = pts.Count > 0 ? pts[pts.Count - 1].V : 0;
            string delta;
            if (last == 0 && cur == 0) delta = "--";
            else if (last == 0) delta = "新增";
            else
            {
                double d = 100.0 * (cur - last) / last;
                delta = (d >= 0 ? "+" : "") + d.ToString("0", CultureInfo.InvariantCulture) + "%";
            }

            string unit = _trendMetric == 3 ? "m" : "次";
            string[] sTitles = { "期间合计", "日均", "最高单日", "较昨日" };
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
                using (SolidBrush b = new SolidBrush(Ctext))
                    g.DrawString(sVals[i], _fNum2, b, x + 16, 548);
            }
        }

        private void PaintChip(Graphics g, RectangleF r, string text, bool sel, string extra)
        {
            PointF mouse = ToBase(_mouse);
            bool hov = Hit(r, new PointF(mouse.X - ContentX, mouse.Y - ContentY));
            if(WuxiaArt.Active)WuxiaArt.Button(g,r,sel,hov);
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

            double max = 10;
            foreach (AnalysisPt p in pts) if (p.V > max) max = p.V;
            foreach (AnalysisPt p in pts) { double moving = PersonalStats.MovingAverage(p.Day, _trendMetric); if (moving > max) max = moving; }
            max = Math.Ceiling(max * 1.15);

            // 网格 + y 标签
            for (int i = 0; i <= 4; i++)
            {
                float gy = bottom - ph * i / 4f;
                using (Pen pen = new Pen(i == 0 ? Color.FromArgb(30, Ctext) : Color.FromArgb(13, Ctext)))
                {
                    if (i != 0) pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, left, gy, right, gy);
                }
                double v = max * i / 4.0;
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    string t = _trendMetric == 3 ? v.ToString("0.#", CultureInfo.InvariantCulture) : Analysis.FmtAxis(v);
                    SizeF sz = g.MeasureString(t, _fAxis);
                    g.DrawString(t, _fAxis, b, left - sz.Width - 6, gy - sz.Height / 2);
                }
            }

            if (pts.Count == 0) return;

            Color main = _trendMetric == 3 ? Cpurple : _trendMetric == 1 ? Cgreen : _trendMetric == 2 ? Corange : Cblue;

            Func<int, float> px = delegate(int i)
            {
                return pts.Count > 1 ? left + pw * i / (pts.Count - 1) : left + pw / 2;
            };
            Func<double, float> py = delegate(double v)
            {
                return bottom - (float)(v / max) * ph;
            };

            // 平均线
            double sum = 0; foreach (AnalysisPt p in pts) sum += p.V;
            double avg = sum / pts.Count;
            using (Pen pen = new Pen(Color.FromArgb(90, Ctext)))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, left, py(avg), right, py(avg));
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(140, Csub)))
                g.DrawString("平均 " + (_trendMetric == 3 ? Analysis.FmtMeters(avg) : Analysis.FmtAxis(avg)),
                    _fAxis, b, right - 70, py(avg) - 16);

            // 面积
            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLine(px(0), bottom, px(0), py(pts[0].V));
                for (int i = 1; i < pts.Count; i++)
                    area.AddLine(px(i - 1), py(pts[i - 1].V), px(i), py(pts[i].V));
                area.AddLine(px(pts.Count - 1), bottom, px(0), bottom);
                try
                {
                    using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(left, top, pw, ph), Color.FromArgb(64, main), Color.FromArgb(0, main), 90f))
                        g.FillPath(fill, area);
                }
                catch { }
            }

            // 折线
            using (Pen pen = new Pen(main, 2f))
            {
                pen.LineJoin = LineJoin.Round;
                for (int i = 0; i < pts.Count - 1; i++)
                    g.DrawLine(pen, px(i), py(pts[i].V), px(i + 1), py(pts[i + 1].V));
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

            // x 轴日期
            int step = Math.Max(1, (int)Math.Ceiling(pts.Count / 8.0));
            using (SolidBrush b = new SolidBrush(Csub))
            {
                for (int i = 0; i < pts.Count; i += step)
                {
                    string t = pts[i].Day.ToString("MM-dd");
                    g.DrawString(t, _fAxis, b, px(i) - 14, bottom + 6);
                }
            }

            // 数据点
            for (int i = 0; i < pts.Count; i++)
            {
                bool isToday = pts[i].Day == DateTime.Today;
                float d = isToday ? 4.5f : 2.8f;
                using (SolidBrush b = new SolidBrush(isToday ? main : Color.FromArgb(160, main)))
                    g.FillEllipse(b, px(i) - d, py(pts[i].V) - d, d * 2, d * 2);
                if(_trendMetric==0&&DailySignal.Get(pts[i].Day).High)using(Pen anomaly=new Pen(Corange,2))g.DrawEllipse(anomaly,px(i)-7,py(pts[i].V)-7,14,14);
            }

            if(ThemeArt.Active && pts.Count>0)
            {
                int champion=0;for(int i=1;i<pts.Count;i++)if(pts[i].V>pts[champion].V)champion=i;
                if(pts[champion].V>0)ThemeArt.Sticker(g,2,new RectangleF(Math.Max(left,Math.Min(right-30,px(champion)-15)),Math.Max(rect.Y+30,py(pts[champion].V)-32),30,30));
            }
            // 悬停提示
            if (_hover >= 0 && _hover < pts.Count)
            {
                AnalysisPt hp = pts[_hover];
                float hx = px(_hover), hy = py(hp.V);
                using (Pen pen = new Pen(Color.FromArgb(70, Ctext)))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawLine(pen, hx, top, hx, bottom);
                }
                using(Pen ring=new Pen(Ctext,2))g.DrawEllipse(ring,hx-8,hy-8,16,16);
                using (SolidBrush b = new SolidBrush(main))
                    g.FillEllipse(b, hx - 4.5f, hy - 4.5f, 9, 9);

                string l1 = hp.Day.ToString("MM-dd")+" · 点击查看日报";
                string l2 = _trendMetric == 3
                    ? Analysis.FmtMeters(hp.V)
                    : Analysis.FmtCount((long)hp.V) + " " + (_trendMetric == 0 ? "击键" : _trendMetric == 1 ? "点击" : "滚动");
                string l3=_trendMetric==0?DailySignal.Get(hp.Day).Note:"";
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
            float cx = Cx;
            for (int i = 0; i < 3; i++)
            {
                RectangleF r = new RectangleF(cx, 98, 88, 30);
                _chips.Add(r); _chipIds.Add(80 + i);
                PaintChip(g, r, metrics[i], _hourMetric == i, null);
                cx += 98;
            }

            IEnumerable<DayRecord> all = Store.History.Values;
            long[] vals = _hourMetric == 0 ? Analysis.SumHourKeys(all) : Analysis.SumHourClicks(all);
            if (_hourMetric == 2)
            {
                all = _activeHourToday ? (IEnumerable<DayRecord>)new DayRecord[] { Store.Today } : Analysis.RangeDays(90);
                double[] seconds = new double[24];
                foreach (DayRecord day in all) for (int h = 0; h < 24; h++) seconds[h] += day.ActiveHours[h];
                for (int h = 0; h < 24; h++) vals[h] = (long)Math.Round(seconds[h]);
                RectangleF today = new RectangleF(Cx + Cw - 186, 98, 88, 30);
                RectangleF history = new RectangleF(Cx + Cw - 88, 98, 88, 30);
                _chips.Add(today); _chipIds.Add(30); PaintChip(g, today, "今日", _activeHourToday, null);
                _chips.Add(history); _chipIds.Add(31); PaintChip(g, history, "近90天", !_activeHourToday, null);
                AppChip(g, 202, Cx + 350, 98, 112, "七日活跃对比", false);
            }

            // 热力条
            PaintCard(g, Cx, 138, Cw, 56, null);
            long hmax = 1;
            foreach (long v in vals) if (v > hmax) hmax = v;
            float cellW = 28, gap2 = (Cw - 24 - 24 * cellW) / 23f;
            float cellX = Cx + 12;
            for (int i = 0; i < 24; i++)
            {
                double frac = vals[i] / (double)hmax;
                int alpha = (int)(30 + 190 * Math.Sqrt(frac));
                using (GraphicsPath p = RoundedRect(cellX, 167, cellW, 16, 4))
                using (SolidBrush b = new SolidBrush(ThemeArt.Dark?(vals[i]<=0?HeatScale.Zero:HeatScale.At(frac)):Color.FromArgb(alpha, Cblue)))
                    g.FillPath(b, p);
                if (vals[i] > 0 && frac > 0.55)
                {
                    // 高值格子加亮边
                }
                cellX += cellW + gap2;
            }
            using (SolidBrush b = new SolidBrush(Csub))
                g.DrawString(ThemeArt.Dark?"24 小时热力（冷蓝低值 → 橙红高值）":"24 小时热力(颜色越亮越活跃)", _fSmall, b, Cx + 12, 138 + 8);

            // 柱状图
            RectangleF chart = new RectangleF(Cx, 208, Cw, 292);
            PaintCard(g, chart.X, chart.Y, chart.Width, chart.Height,
                _hourMetric == 2 ? "各时段活跃时长 · " + (_activeHourToday ? "今日" : "近90天") + " · 纵轴为分钟"
                    : _hourMetric == 0 ? "各时段击键分布 · 全部历史" : "各时段点击分布 · 全部历史");
            PaintHourBars(g, chart, vals);

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
            float tileW = (Cw - 3 * 16) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float x = Cx + i * (tileW + 16);
                PaintCardBase(g, x, 514, tileW, 62);
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(t1[i], _fSmall, b, x + 16, 522);
                using (SolidBrush b = new SolidBrush(Ctext))
                    g.DrawString(t2[i], _fNum2, b, x + 16, 538);
            }
        }

        private void PaintHourBars(Graphics g, RectangleF rect, long[] vals)
        {
            float left = rect.X + 50, right = rect.X + rect.Width - 16;
            float top = rect.Y + 38, bottom = rect.Y + rect.Height - 28;
            float ph = bottom - top, pw = right - left;

            long max = 10;
            foreach (long v in vals) if (v > max) max = v;
            double dmax = Math.Ceiling(max * 1.1);

            for (int i = 0; i <= 3; i++)
            {
                float gy = bottom - ph * i / 3f;
                using (Pen pen = new Pen(Color.FromArgb(13, Ctext)))
                {
                    if (i != 0) pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, left, gy, right, gy);
                }
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    string t = _hourMetric == 2 ? (dmax * i / 180.0).ToString("0.#", CultureInfo.InvariantCulture) : Analysis.FmtAxis(dmax * i / 3.0);
                    SizeF sz = g.MeasureString(t, _fAxis);
                    g.DrawString(t, _fAxis, b, left - sz.Width - 6, gy - sz.Height / 2);
                }
            }

            float slot = pw / 24f;
            float barW = slot * 0.58f;
            for (int i = 0; i < 24; i++)
            {
                float bx = left + slot * i + (slot - barW) / 2;
                float bh = (float)(vals[i] / dmax) * ph;
                bool isNow = i == DateTime.Now.Hour;
                bool isHover = i == _hover;

                Color c = isHover ? Ccyan
                    : isNow ? Cblue : Color.FromArgb(150, Cblue);
                using (GraphicsPath p = RoundedRect(bx, bottom - Math.Max(bh, 2), barW, Math.Max(bh, 2), 3))
                using (SolidBrush b = new SolidBrush(c))
                    g.FillPath(b, p);

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

        // ---------------------------------------------------------------- 标签页:按键排行

        private void PaintKeys(Graphics g)
        {
            string[] ranges = { "今日", "近7天", "近30天", "近90天" };
            int[] rangeIds={70,76,77,71},rangeValues={0,2,3,1};
            float cx = Cx;
            for (int i = 0; i < ranges.Length; i++)
            {
                RectangleF r = new RectangleF(cx, 98, 88, 30);
                _chips.Add(r); _chipIds.Add(rangeIds[i]);
                PaintChip(g, r, ranges[i], _keyRange == rangeValues[i], null);
                cx += 98;
            }

            for (int i = 0; i < 2; i++)
            {
                RectangleF choice = new RectangleF(Cx + Cw - 186 + i * 98, 98, 88, 30);
                _chips.Add(choice); _chipIds.Add(72 + i);
                PaintChip(g, choice, i == 0 ? "单键" : "组合键", !_showKeyboardHeatmap && _showCombos == (i == 1), null);
            }

            IEnumerable<DayRecord> src = KeyRangeDays();

            RectangleF heatChoice = new RectangleF(Cx + Cw - 294, 98, 98, 30);
            _chips.Add(heatChoice); _chipIds.Add(74);
            PaintChip(g, heatChoice, "键盘热力", _showKeyboardHeatmap, null);
            if (_showKeyboardHeatmap) { PaintKeyboardHeatmap(g, src); return; }

            List<KeyValuePair<string, long>> rank = _showCombos ? ShortcutStats.Ranking(src) : Analysis.KeyRanking(src);
            long total = 0;
            foreach (KeyValuePair<string, long> item in rank) total += item.Value;

            // 排行列表
            PaintCard(g, Cx, 138, 560, 438, null);
            if (rank.Count == 0)
            {
                using (SolidBrush b = new SolidBrush(Csub))
                {
                    string empty = _showCombos ? "还没有组合动作，试试 Ctrl+C 或 Win+Shift+S" : "暂无按键数据";
                    SizeF sz = g.MeasureString(empty, _fBody);
                    g.DrawString(empty, _fBody, b, Cx + 280 - sz.Width / 2, 138 + 200);
                }
            }
            else
            {
                int n = Math.Min(10, rank.Count);
                float rowH = 396f / 10f;
                long max = rank[0].Value;
                for (int i = 0; i < n; i++)
                {
                    float ry = 138 + 14 + i * rowH;

                    using (SolidBrush b = new SolidBrush(i < 3 ? Color.FromArgb(90, Cblue) : Color.FromArgb(30, Ctext)))
                        g.FillEllipse(b, Cx + 20, ry + rowH / 2 - 10, 20, 20);
                    using (SolidBrush b = new SolidBrush(i < 3 ? Ctext : Csub))
                    {
                        string t = (i + 1).ToString();
                        SizeF sz = g.MeasureString(t, _fSmall);
                        g.DrawString(t, _fSmall, b, Cx + 30 - sz.Width / 2, ry + rowH / 2 - 8);
                    }

                    using (SolidBrush b = new SolidBrush(Ctext))
                    {
                        if (_showCombos)
                            AppText(g, rank[i].Key, _fBody, Ctext, new RectangleF(Cx + 52, ry + rowH / 2 - 11, 196, 22), false);
                        else
                        {
                            SizeF sz = g.MeasureString(rank[i].Key, _fBody);
                            g.DrawString(rank[i].Key, _fBody, b, Cx + 92 - sz.Width, ry + rowH / 2 - 9);
                        }
                    }

                    float barX = Cx + (_showCombos ? 258 : 108), barW = _showCombos ? 170 : 320;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(20, Ctext)))
                        g.FillRectangle(b, barX, ry + rowH / 2 - 5, barW, 10);
                    float frac = max > 0 ? (float)rank[i].Value / max : 0;
                    Color c = i == 0 ? Cblue : i == 1 ? Ccyan : i == 2 ? Cgreen : Csub;
                    using (GraphicsPath bp = RoundedRect(barX, ry + rowH / 2 - 5, Math.Max(4, barW * frac), 10, 5))
                    using (SolidBrush b = new SolidBrush(c))
                        g.FillPath(b, bp);

                    using (SolidBrush b = new SolidBrush(Csub))
                        g.DrawString(Analysis.FmtCount(rank[i].Value), _fAxis, b, Cx + 444, ry + rowH / 2 - 7);
                }
            }

            // 右侧汇总
            float rx = Cx + 576, rw = Cw - 576;
            PaintCard(g, rx, 138, rw, 438, _showCombos ? "组合动作概况" : "按键概况");
            long keyDays = 0;
            foreach (DayRecord r in src) if (_showCombos ? r.ComboCounts.Count > 0 : r.KeyCounts.Count > 0) keyDays++;

            string[][] rows = {
                new string[] { _showCombos ? "组合种类" : "不同按键数", rank.Count.ToString(CultureInfo.InvariantCulture) + " 种" },
                new string[] { _showCombos ? "组合动作次数" : "总击键", Analysis.FmtCount(total) },
                new string[] { "最常用", rank.Count > 0 ? rank[0].Key : "--" },
                new string[] { "前三占比", Top3Pct(rank, total) },
                new string[] { "记录天数", keyDays + " 天" },
                new string[] { _showCombos ? "计数方式" : "键鼠比", _showCombos ? "长按计一次" : MouseKeyRatio(src) } };
            float ry2 = 138 + 46;
            for (int i = 0; i < rows.Length; i++)
            {
                using (SolidBrush b = new SolidBrush(Csub))
                    g.DrawString(rows[i][0], _fBody, b, rx + 20, ry2);
                AppText(g, rows[i][1], _showCombos && i == 2 ? _fBody : _fNum2, Ctext,
                    new RectangleF(rx + 20, ry2 + 17, rw - 40, 30), false);
                ry2 += 66;
                if (i < rows.Length - 1)
                {
                    using (Pen pen = new Pen(Color.FromArgb(14, Ctext)))
                        g.DrawLine(pen, rx + 20, ry2 - 10, rx + rw - 20, ry2 - 10);
                }
            }
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
            if (_tab == TabId.Keys && _showKeyboardHeatmap) { ExportKeyboardHeatmap(); return; }
            if (_tab == TabId.Trend && _trendHourly) { ExportHourlyTrend(); return; }
            if (_tab == TabId.Insights) { ExportInsights(); return; }
            if (_tab == TabId.Apps) { ExportApps(); return; }
            if (_tab == TabId.Keys && _showCombos)
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






