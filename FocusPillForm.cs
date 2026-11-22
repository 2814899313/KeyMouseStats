// ============================================================================
//  KeyMouseStats v3.3  FocusPillForm.cs
//
//  独立无激活心流胶囊。默认 224×38,悬停展开 248×116 遥测预览。
//  呼吸灯只 Invalidate LED 小矩形;文字/状态变化才整窗重绘。
//  不引用 KmsDb / FlowBoard / SQLite;Paint 路径不写盘。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class FocusPillLayout
    {
        internal const int PillWidth = 224;
        internal const int PillHeight = 38;
        internal const int PreviewWidth = 248;
        internal const int PreviewHeight = 116;

        internal static Size Size(bool preview, float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            return new Size(
                (int)Math.Round((preview ? PreviewWidth : PillWidth) * scale),
                (int)Math.Round((preview ? PreviewHeight : PillHeight) * scale));
        }

        internal static RectangleF LedRect(Size client, float scale, bool preview)
        {
            if (scale < 0.5f) scale = 0.5f;
            float size = 12f * scale;
            float x = (preview ? 16f : 14f) * scale;
            float y = preview ? 16f * scale : (client.Height - size) / 2f;
            return new RectangleF(x, y, size, size);
        }

        internal static RectangleF StateRect(Size client, float scale, bool preview)
        {
            if (scale < 0.5f) scale = 0.5f;
            if (preview) return new RectangleF(38f * scale, 7f * scale, client.Width - 48f * scale, 24f * scale);
            float width = client.Width - 34f * scale - 96f * scale;
            if (width < 20f * scale) width = 20f * scale;
            return new RectangleF(34f * scale, 6f * scale, width, 26f * scale);
        }

        internal static RectangleF AppRect(Size client, float scale, bool preview)
        {
            if (scale < 0.5f) scale = 0.5f;
            if (preview) return new RectangleF(38f * scale, 28f * scale, client.Width - 48f * scale, 16f * scale);
            return new RectangleF(client.Width - 90f * scale, 11f * scale, 80f * scale, 18f * scale);
        }
    }

    internal sealed class FocusPillForm : Form
    {
        private readonly Action _openDashboard;
        private readonly Action _closeRequested;
        private FocusSnapshot _snapshot;
        private bool _preview;
        private bool _dragging;
        private bool _moved;
        private Point _dragStart;
        private Point _windowStart;
        private float _s = 1f;
        private Font _fState;
        private Font _fValue;
        private Font _fSmall;
        private Timer _breathTimer;
        private Timer _collapseTimer;
        private Timer _resizeTimer;
        private DateTime _collapseAt = DateTime.MinValue;
        private long _resizeStart;
        private int _resizeDuration;
        private Size _resizeFrom;
        private Size _resizeTo;
        private string _lastPillText = "";
        private FlowState _lastState = FlowState.Idle;

        internal FocusPillForm(Action openDashboard, Action closeRequested)
        {
            _openDashboard = openDashboard;
            _closeRequested = closeRequested;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = ArtTheme.Current.Card;
            Opacity = 0.96;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.Opaque, true);

            _s = 1f;
            RebuildFonts();
            ApplyMetrics();
            _lastPillText = PillText();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                using (Graphics g = CreateGraphics())
                    _s = Math.Max(1f, g.DpiX / 96f);
            }
            catch { }
            RebuildFonts();
            ApplyMetrics();
            ClampToScreen();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00080000 | 0x80; // NOACTIVATE | LAYERED | TOOLWINDOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            if (m.Msg == 0x02E0)
            {
                int dpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                if (dpi > 0)
                {
                    _s = Math.Max(1f, dpi / 96f);
                    RebuildFonts();
                    ApplyMetrics();
                }
            }
            base.WndProc(ref m);
        }

        internal FocusSnapshot Snapshot { get { return _snapshot; } }
        internal bool Previewing { get { return _preview; } }

        internal void SetScaleForTest(float scale)
        {
            _s = scale < 0.5f ? 0.5f : scale;
            RebuildFonts();
            ApplyMetrics();
        }

        internal void SetPreviewForTest(bool preview)
        {
            SetPreview(preview);
        }

        internal void ShowAt(Point location, FocusSnapshot snapshot)
        {
            _snapshot = snapshot;
            _preview = false;
            _collapseAt = DateTime.MinValue;
            if (_collapseTimer != null) _collapseTimer.Stop();
            if (_resizeTimer != null) _resizeTimer.Stop();
            ApplyMetrics();
            _lastPillText = PillText();
            _lastState = snapshot == null ? FlowState.Idle : snapshot.State;
            Location = location;
            ClampToScreen();
            if (!Visible) Show();
            EnsureBreath();
            Invalidate();
        }

        internal void UpdateSnapshot(FocusSnapshot snapshot)
        {
            _snapshot = snapshot;
            string text = PillText();
            FlowState state = snapshot == null ? FlowState.Idle : snapshot.State;
            bool changed = !string.Equals(text, _lastPillText, StringComparison.Ordinal) || state != _lastState;
            _lastPillText = text;
            _lastState = state;
            EnsureBreath();
            if (changed || _preview) Invalidate();
        }

        internal void HidePill()
        {
            if (_collapseTimer != null) _collapseTimer.Stop();
            if (_resizeTimer != null) _resizeTimer.Stop();
            if (_breathTimer != null) _breathTimer.Stop();
            _preview = false;
            ApplyMetrics();
            Hide();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) EnsureBreath();
            else if (_breathTimer != null) _breathTimer.Stop();
        }

        private void EnsureBreath()
        {
            if (!Visible || !Motion.Enabled)
            {
                if (_breathTimer != null) _breathTimer.Stop();
                return;
            }
            if (_breathTimer == null)
            {
                _breathTimer = new Timer();
                _breathTimer.Tick += delegate { BreathTick(); };
            }
            int interval = Motion.Level == Motion.LevelReduced ? 50 : 16;
            if (_breathTimer.Interval != interval) _breathTimer.Interval = interval;
            if (!_breathTimer.Enabled) _breathTimer.Start();
        }

        private void BreathTick()
        {
            if (!Visible || !Motion.Enabled)
            {
                if (_breathTimer != null) _breathTimer.Stop();
                return;
            }
            InvalidateLed();
        }

        private void InvalidateLed()
        {
            if (!IsHandleCreated || !Visible) return;
            RectangleF led = FocusPillLayout.LedRect(ClientSize, _s, _preview);
            float pad = 5f * _s;
            Invalidate(Rectangle.Round(new RectangleF(led.X - pad, led.Y - pad,
                led.Width + pad * 2f, led.Height + pad * 2f)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float liveScale = _s;
            try { liveScale = Math.Max(1f, g.DpiX / 96f); }
            catch { }
            if (Math.Abs(liveScale - _s) > 0.02f)
            {
                _s = liveScale;
                RebuildFonts();
                ApplyMetrics();
            }

            bool full = e.ClipRectangle.Width >= ClientSize.Width - 2
                && e.ClipRectangle.Height >= ClientSize.Height - 2;
            if (!full)
            {
                PaintLed(g);
                return;
            }
            PaintCard(g);
        }

        private void PaintCard(Graphics g)
        {
            float s = _s;
            ArtTheme theme = ArtTheme.Current;
            using (GraphicsPath path = RoundedPath(new Rectangle(Point.Empty, ClientSize),
                _preview ? (int)Math.Round(12f * s) : ClientSize.Height / 2))
            {
                using (SolidBrush bg = new SolidBrush(theme.Card)) g.FillPath(bg, path);
                using (Pen edge = new Pen(Color.FromArgb(90, theme.Line), 1f)) g.DrawPath(edge, path);
            }

            RectangleF led = FocusPillLayout.LedRect(ClientSize, s, _preview);
            DrawLed(g, led);

            using (StringFormat clip = new StringFormat())
            {
                clip.Trimming = StringTrimming.EllipsisCharacter;
                clip.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush text = new SolidBrush(theme.Text))
                using (SolidBrush muted = new SolidBrush(theme.Muted))
                {
                    if (_preview) PaintPreview(g, s, clip, text, muted);
                    else
                    {
                        g.DrawString(PillText(), _fState, text, FocusPillLayout.StateRect(ClientSize, s, false), clip);
                        string app = _snapshot == null ? "未识别应用" : _snapshot.MainAppName;
                        g.DrawString(app, _fSmall, muted, FocusPillLayout.AppRect(ClientSize, s, false), clip);
                    }
                }
            }
        }

        private void PaintPreview(Graphics g, float s, StringFormat clip, Brush text, Brush muted)
        {
            FlowState state = _snapshot == null ? FlowState.Idle : _snapshot.State;
            double seconds = _snapshot == null ? 0 : _snapshot.FocusSeconds;
            string app = _snapshot == null ? "未识别应用" : _snapshot.MainAppName;
            g.DrawString(StateName(state) + " · " + Duration(seconds), _fValue, text,
                FocusPillLayout.StateRect(ClientSize, s, true), clip);
            g.DrawString(app, _fSmall, muted, FocusPillLayout.AppRect(ClientSize, s, true), clip);

            long keys = Store.Today == null ? 0 : Store.Today.Keys;
            long clicks = Store.Today == null ? 0 : Store.Today.Clicks;
            int switches = _snapshot == null ? 0 : _snapshot.Switches5m;
            string line1 = "实时 APM " + LiveRate.Apm;
            string line2 = "今日 " + Analysis.FmtCount(keys) + " 键 / " + Analysis.FmtCount(clicks) + " 点击";
            string line3 = "连续专注 " + Duration(seconds);
            string line4 = "5 分钟切窗 " + switches + " 次";
            float x = 16f * s;
            float width = ClientSize.Width - 32f * s;
            g.DrawString(line1, _fSmall, text, new RectangleF(x, 50f * s, width, 15f * s), clip);
            g.DrawString(line2, _fSmall, muted, new RectangleF(x, 68f * s, width, 15f * s), clip);
            g.DrawString(line3, _fSmall, muted, new RectangleF(x, 86f * s, width, 15f * s), clip);
            g.DrawString(line4, _fSmall, muted, new RectangleF(x, 102f * s, width, 12f * s), clip);
        }

        private void PaintLed(Graphics g)
        {
            RectangleF led = FocusPillLayout.LedRect(ClientSize, _s, _preview);
            float pad = 5f * _s;
            using (SolidBrush bg = new SolidBrush(ArtTheme.Current.Card))
                g.FillRectangle(bg, led.X - pad, led.Y - pad, led.Width + pad * 2f, led.Height + pad * 2f);
            DrawLed(g, led);
        }

        private void DrawLed(Graphics g, RectangleF led)
        {
            Color color = LedColor();
            double alpha = BreathAlpha();
            using (SolidBrush glow = new SolidBrush(Color.FromArgb((int)Math.Round(55 * alpha), color)))
                g.FillEllipse(glow, led.X - 4f * _s, led.Y - 4f * _s,
                    led.Width + 8f * _s, led.Height + 8f * _s);
            using (SolidBrush fill = new SolidBrush(Color.FromArgb((int)Math.Round(255 * alpha), color)))
                g.FillEllipse(fill, led);
        }

        private Color LedColor()
        {
            FlowState state = _snapshot == null ? FlowState.Idle : _snapshot.State;
            if (state == FlowState.Deep) return ArtTheme.Current.Green;
            if (state == FlowState.Normal) return ArtTheme.Current.Cyan;
            if (state == FlowState.Fragmented) return ArtTheme.Current.Orange;
            return ArtTheme.Current.Muted;
        }

        private double BreathAlpha()
        {
            if (!Motion.Enabled || Motion.Paused || ActivityMonitor.Busy || HoldTracker.PendingCount > 0)
                return 1.0;
            double period = Motion.Level == Motion.LevelReduced ? 3000 : 2200;
            double phase = (Environment.TickCount % period) / period * Math.PI * 2.0;
            double amplitude = Motion.Level == Motion.LevelReduced ? 0.25 : 0.45;
            double value = 0.55 + amplitude * Math.Sin(phase);
            if (value < 0.1) value = 0.1;
            if (value > 1.0) value = 1.0;
            return value;
        }

        private string PillText()
        {
            FlowState state = _snapshot == null ? FlowState.Idle : _snapshot.State;
            double seconds = _snapshot == null ? 0 : _snapshot.FocusSeconds;
            return StateName(state) + " · " + Duration(seconds);
        }

        internal static string StateName(FlowState state)
        {
            if (state == FlowState.Deep) return "深度心流";
            if (state == FlowState.Normal) return "常规专注";
            if (state == FlowState.Fragmented) return "碎片切换";
            return "空闲";
        }

        private static string Duration(double seconds)
        {
            int minutes = (int)Math.Floor(Math.Max(0, seconds) / 60.0);
            if (minutes < 60) return minutes + "m";
            return (minutes / 60) + "h" + (minutes % 60 == 0 ? "" : (minutes % 60) + "m");
        }

        private void RebuildFonts()
        {
            Font state = _fState;
            Font value = _fValue;
            Font small = _fSmall;
            _fState = new Font("Microsoft YaHei UI", 11f * _s, FontStyle.Regular, GraphicsUnit.Pixel);
            _fValue = new Font("Microsoft YaHei UI", 15f * _s, FontStyle.Bold, GraphicsUnit.Pixel);
            _fSmall = new Font("Microsoft YaHei UI", 9f * _s, FontStyle.Regular, GraphicsUnit.Pixel);
            if (state != null) state.Dispose();
            if (value != null) value.Dispose();
            if (small != null) small.Dispose();
        }

        private void ApplyMetrics()
        {
            ClientSize = FocusPillLayout.Size(_preview, _s);
            ApplyRegion();
        }

        private void ApplyRegion()
        {
            try
            {
                int radius = _preview ? (int)Math.Round(12f * _s) : ClientSize.Height / 2;
                using (GraphicsPath path = RoundedPath(new Rectangle(Point.Empty, ClientSize), radius))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        private void ClampToScreen()
        {
            try
            {
                Rectangle bounded = WidgetPlacement.Clamp(Bounds, WidgetPlacement.WorkingAreas(),
                    Screen.FromControl(this).WorkingArea);
                Location = bounded.Location;
            }
            catch { }
        }

        private void SetPreview(bool preview)
        {
            if (_preview == preview) return;
            _preview = preview;
            Size target = FocusPillLayout.Size(_preview, _s);
            if (!Visible || !Motion.Enabled)
            {
                ApplySize(target);
                return;
            }
            _resizeFrom = ClientSize;
            _resizeTo = target;
            _resizeStart = Environment.TickCount;
            _resizeDuration = preview ? 160 : 140;
            if (_resizeTimer == null)
            {
                _resizeTimer = new Timer();
                _resizeTimer.Interval = 16;
                _resizeTimer.Tick += delegate { ResizeTick(); };
            }
            if (!_resizeTimer.Enabled) _resizeTimer.Start();
        }

        private void ResizeTick()
        {
            if (!Visible)
            {
                if (_resizeTimer != null) _resizeTimer.Stop();
                return;
            }
            double t = _resizeDuration <= 0 ? 1.0
                : (Environment.TickCount - _resizeStart) / (double)_resizeDuration;
            if (t >= 1.0)
            {
                ApplySize(_resizeTo);
                if (_resizeTimer != null) _resizeTimer.Stop();
                return;
            }
            double eased = Ease.Apply(Ease.OutCubic, t);
            int width = (int)Math.Round(_resizeFrom.Width + (_resizeTo.Width - _resizeFrom.Width) * eased);
            int height = (int)Math.Round(_resizeFrom.Height + (_resizeTo.Height - _resizeFrom.Height) * eased);
            ClientSize = new Size(Math.Max(1, width), Math.Max(1, height));
            ApplyRegion();
            Invalidate();
        }

        private void ApplySize(Size size)
        {
            ClientSize = size;
            ApplyRegion();
            ClampToScreen();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (_collapseTimer != null) _collapseTimer.Stop();
            SetPreview(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging) return;
            _collapseAt = DateTime.Now.AddMilliseconds(280);
            if (_collapseTimer == null)
            {
                _collapseTimer = new Timer();
                _collapseTimer.Interval = 50;
                _collapseTimer.Tick += delegate { CollapseTick(); };
            }
            if (!_collapseTimer.Enabled) _collapseTimer.Start();
        }

        private void CollapseTick()
        {
            if (!_preview) { if (_collapseTimer != null) _collapseTimer.Stop(); return; }
            if (DateTime.Now < _collapseAt) return;
            if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                // 窗口因展开而轻微移动时,鼠标可能仍在胶囊内;等下一次 Leave 再收起。
                if (_collapseTimer != null) _collapseTimer.Stop();
                return;
            }
            if (_collapseTimer != null) _collapseTimer.Stop();
            SetPreview(false);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                if (_closeRequested != null) _closeRequested();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _dragStart = Cursor.Position;
            _windowStart = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            Point now = Cursor.Position;
            int dx = now.X - _dragStart.X;
            int dy = now.Y - _dragStart.Y;
            if (Math.Abs(dx) > 4 || Math.Abs(dy) > 4) _moved = true;
            if (_moved) Location = new Point(_windowStart.X + dx, _windowStart.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_dragging) return;
            _dragging = false;
            if (_moved) ClampToScreen();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && _openDashboard != null) _openDashboard();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_breathTimer != null) { _breathTimer.Stop(); _breathTimer.Dispose(); _breathTimer = null; }
                if (_collapseTimer != null) { _collapseTimer.Stop(); _collapseTimer.Dispose(); _collapseTimer = null; }
                if (_resizeTimer != null) { _resizeTimer.Stop(); _resizeTimer.Dispose(); _resizeTimer = null; }
                if (_fState != null) { _fState.Dispose(); _fState = null; }
                if (_fValue != null) { _fValue.Dispose(); _fValue = null; }
                if (_fSmall != null) { _fSmall.Dispose(); _fSmall = null; }
            }
            base.Dispose(disposing);
        }

        internal static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
