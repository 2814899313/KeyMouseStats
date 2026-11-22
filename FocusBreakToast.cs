// ============================================================================
//  KeyMouseStats v3.3  FocusBreakToast.cs
//
//  无激活微休息浮动提示。右下角安全区滑出,15 秒自动淡出。
//  自绘「延后 10 分钟 / 今日静音」按钮,不抢焦点、不建子控件。
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
    internal static class FocusBreakToastLayout
    {
        internal const int Width = 360;
        internal const int Height = 132;
        internal const int LifeSeconds = 15;

        internal static Size Size(float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            return new Size((int)Math.Round(Width * scale), (int)Math.Round(Height * scale));
        }

        internal static RectangleF SnoozeRect(Size client, float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            return new RectangleF(16f * scale, client.Height - 40f * scale,
                client.Width / 2f - 24f * scale, 28f * scale);
        }

        internal static RectangleF MuteRect(Size client, float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            return new RectangleF(client.Width / 2f + 8f * scale, client.Height - 40f * scale,
                client.Width / 2f - 24f * scale, 28f * scale);
        }

        internal static RectangleF CloseRect(Size client, float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            return new RectangleF(client.Width - 26f * scale, 6f * scale, 20f * scale, 20f * scale);
        }

        /// <summary>0 无 / 1 延后 / 2 静音 / 3 关闭。</summary>
        internal static int HitTest(Size client, float scale, Point point)
        {
            if (CloseRect(client, scale).Contains(point.X, point.Y)) return 3;
            if (SnoozeRect(client, scale).Contains(point.X, point.Y)) return 1;
            if (MuteRect(client, scale).Contains(point.X, point.Y)) return 2;
            return 0;
        }

        internal static Rectangle ToastBounds(Rectangle workArea, float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            Size size = Size(scale);
            int width = Math.Min(size.Width, Math.Max(240, workArea.Width - 16));
            int height = Math.Min(size.Height, Math.Max(100, workArea.Height - 16));
            int margin = (int)Math.Round(24f * scale);
            int x = workArea.Right - width - margin;
            int y = workArea.Bottom - height - margin;
            if (x < workArea.Left + 8) x = workArea.Left + 8;
            if (y < workArea.Top + 8) y = workArea.Top + 8;
            return new Rectangle(x, y, width, height);
        }
    }

    internal sealed class FocusBreakToast : Form
    {
        private readonly Action _snooze;
        private readonly Action _mute;
        private int _minutes;
        private string _app = "";
        private float _s = 1f;
        private Font _fTitle;
        private Font _fBody;
        private Font _fSmall;
        private Timer _lifeTimer;
        private Timer _fadeTimer;
        private int _lifeRemainingMs;
        private int _hoverButton;
        private bool _fading;

        internal FocusBreakToast(Action snooze, Action mute)
        {
            _snooze = snooze;
            _mute = mute;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = ArtTheme.Current.Card;
            Opacity = 0.98;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.Opaque, true);

            RebuildFonts();
            ApplyMetrics();
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
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
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

        internal void ShowToast(int focusMinutes, string appName, Rectangle workArea)
        {
            _minutes = Math.Max(0, focusMinutes);
            _app = string.IsNullOrEmpty(appName) ? "当前应用" : appName;
            _hoverButton = 0;
            _fading = false;
            if (_fadeTimer != null) _fadeTimer.Stop();
            Opacity = 0.98;
            _lifeRemainingMs = FocusBreakToastLayout.LifeSeconds * 1000;
            Rectangle bounds = FocusBreakToastLayout.ToastBounds(workArea, _s);
            ClientSize = bounds.Size;
            Location = bounds.Location;
            ApplyRegion();
            if (!Visible) Show();
            EnsureLifeTimer();
            Invalidate();
        }

        internal void HideToast()
        {
            _fading = false;
            if (_lifeTimer != null) _lifeTimer.Stop();
            if (_fadeTimer != null) _fadeTimer.Stop();
            Opacity = 0.98;
            Hide();
        }

        /// <summary>测试用:不显示窗口也能验证按钮回调与状态联动。</summary>
        internal void ActivateActionForTest(int action)
        {
            HandleAction(action);
        }

        internal void PrepareForTest(int focusMinutes, string appName, float scale)
        {
            _minutes = Math.Max(0, focusMinutes);
            _app = string.IsNullOrEmpty(appName) ? "当前应用" : appName;
            _s = scale < 0.5f ? 0.5f : scale;
            RebuildFonts();
            ApplyMetrics();
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                if (_lifeTimer != null) _lifeTimer.Stop();
                if (_fadeTimer != null) _fadeTimer.Stop();
            }
        }

        private void EnsureLifeTimer()
        {
            if (_lifeTimer == null)
            {
                _lifeTimer = new Timer();
                _lifeTimer.Interval = 1000;
                _lifeTimer.Tick += delegate
                {
                    if (!Visible) { _lifeTimer.Stop(); return; }
                    _lifeRemainingMs -= 1000;
                    if (_lifeRemainingMs <= 0) FadeOut();
                };
            }
            if (!_lifeTimer.Enabled) _lifeTimer.Start();
        }

        private void FadeOut()
        {
            if (_fading) return;
            _fading = true;
            if (_lifeTimer != null) _lifeTimer.Stop();
            if (!Visible || !Motion.Enabled)
            {
                HideToast();
                return;
            }
            if (_fadeTimer == null)
            {
                _fadeTimer = new Timer();
                _fadeTimer.Interval = 30;
                _fadeTimer.Tick += delegate
                {
                    double next = Opacity - 0.18;
                    if (next <= 0.02) { HideToast(); return; }
                    Opacity = next;
                };
            }
            if (!_fadeTimer.Enabled) _fadeTimer.Start();
        }

        private void HandleAction(int action)
        {
            if (action == 1)
            {
                if (_snooze != null) _snooze();
                FadeOut();
            }
            else if (action == 2)
            {
                if (_mute != null) _mute();
                FadeOut();
            }
            else if (action == 3) FadeOut();
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

            ArtTheme theme = ArtTheme.Current;
            using (GraphicsPath path = RoundedPath(new Rectangle(Point.Empty, ClientSize), (int)Math.Round(12f * _s)))
            {
                using (SolidBrush bg = new SolidBrush(theme.Card)) g.FillPath(bg, path);
                using (Pen edge = new Pen(Color.FromArgb(110, theme.Line), 1f)) g.DrawPath(edge, path);
            }

            using (StringFormat clip = new StringFormat())
            using (SolidBrush text = new SolidBrush(theme.Text))
            using (SolidBrush muted = new SolidBrush(theme.Muted))
            {
                clip.Trimming = StringTrimming.EllipsisCharacter;
                clip.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString("该休息一下了", _fTitle, text,
                    new RectangleF(16f * _s, 12f * _s, ClientSize.Width - 52f * _s, 22f * _s), clip);
                g.DrawString("连续专注 " + _minutes + " 分钟 · " + _app, _fBody, text,
                    new RectangleF(16f * _s, 40f * _s, ClientSize.Width - 32f * _s, 18f * _s), clip);
                g.DrawString("起来活动 2 分钟，让注意力恢复。", _fSmall, muted,
                    new RectangleF(16f * _s, 60f * _s, ClientSize.Width - 32f * _s, 16f * _s), clip);
                g.DrawString("×", _fBody, muted,
                    FocusBreakToastLayout.CloseRect(ClientSize, _s), clip);

                DrawButton(g, FocusBreakToastLayout.SnoozeRect(ClientSize, _s), "延后 10 分钟", 1);
                DrawButton(g, FocusBreakToastLayout.MuteRect(ClientSize, _s), "今日静音", 2);
            }
        }

        private void DrawButton(Graphics g, RectangleF rect, string text, int id)
        {
            ArtTheme theme = ArtTheme.Current;
            bool hover = _hoverButton == id;
            Color fill = ArtTheme.Mix(theme.Raised, theme.Accent, hover ? 0.32 : 0.16);
            using (GraphicsPath path = RoundedPath(Rectangle.Round(rect), (int)Math.Round(8f * _s)))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen edge = new Pen(Color.FromArgb(hover ? 190 : 120, theme.Accent), 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(edge, path);
            }
            using (StringFormat center = new StringFormat())
            using (SolidBrush ink = new SolidBrush(theme.Text))
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;
                center.Trimming = StringTrimming.EllipsisCharacter;
                center.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text, _fSmall, ink, rect, center);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = FocusBreakToastLayout.HitTest(ClientSize, _s, e.Location);
            if (hit == _hoverButton) return;
            _hoverButton = hit;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverButton == 0) return;
            _hoverButton = 0;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int hit = FocusBreakToastLayout.HitTest(ClientSize, _s, e.Location);
            if (hit != 0) HandleAction(hit);
        }

        private void ApplyMetrics()
        {
            ClientSize = FocusBreakToastLayout.Size(_s);
            ApplyRegion();
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

        private void ApplyRegion()
        {
            try
            {
                using (GraphicsPath path = RoundedPath(new Rectangle(Point.Empty, ClientSize), (int)Math.Round(12f * _s)))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        private void RebuildFonts()
        {
            Font title = _fTitle;
            Font body = _fBody;
            Font small = _fSmall;
            _fTitle = new Font("Microsoft YaHei UI", 14f * _s, FontStyle.Bold, GraphicsUnit.Pixel);
            _fBody = new Font("Microsoft YaHei UI", 11f * _s, FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font("Microsoft YaHei UI", 9.5f * _s, FontStyle.Regular, GraphicsUnit.Pixel);
            if (title != null) title.Dispose();
            if (body != null) body.Dispose();
            if (small != null) small.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_lifeTimer != null) { _lifeTimer.Stop(); _lifeTimer.Dispose(); _lifeTimer = null; }
                if (_fadeTimer != null) { _fadeTimer.Stop(); _fadeTimer.Dispose(); _fadeTimer = null; }
                if (_fTitle != null) { _fTitle.Dispose(); _fTitle = null; }
                if (_fBody != null) { _fBody.Dispose(); _fBody = null; }
                if (_fSmall != null) { _fSmall.Dispose(); _fSmall = null; }
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath RoundedPath(Rectangle r, int radius)
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
