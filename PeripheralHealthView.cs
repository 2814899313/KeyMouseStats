// ============================================================================
//  KeyMouseStats v3.4.1  PeripheralHealthView.cs
//
//  「按键排行」内嵌外设健康视图(只读数据面板):
//    · 纯布局 HealthLayout,便于 100/150/175% 与负 ElasticH 单测;
//    · 两张卡默认只读,编辑才弹出小面板;
//    · 宿主只负责编辑/设备信息,切换子视图时隐藏并在关闭时销毁;
//    · WearDraw 只画内存快照,不读文件/注册表。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class WearDraw
    {
        internal static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0) { path.AddRectangle(rect); return path; }
            int d = Math.Max(2, radius * 2);
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void Bar(Graphics g, RectangleF rect, double ratio)
        {
            ArtTheme theme = ArtTheme.Current;
            using (GraphicsPath track = Rounded(Rectangle.Round(rect), Math.Max(2, (int)(rect.Height / 2))))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(18, theme.Text)))
                g.FillPath(fill, track);
            if (ratio <= 0) return;
            if (ratio > 1) ratio = 1;
            RectangleF area = new RectangleF(rect.X, rect.Y,
                Math.Max(2f, rect.Width * (float)ratio), rect.Height);
            using (GraphicsPath bar = Rounded(Rectangle.Round(area), Math.Max(2, (int)(rect.Height / 2))))
            using (SolidBrush fill = new SolidBrush(BarColor(ratio)))
                g.FillPath(fill, bar);
        }

        internal static Color PillBack(Color accent, bool selected)
        {
            ArtTheme theme = ArtTheme.Current;
            if (selected) return Color.FromArgb(34, accent);
            return Color.FromArgb(9, theme.Text);
        }

        internal static Color BarColor(double ratio)
        {
            if (ratio >= 1) return ArtTheme.Current.Red;
            if (ratio >= 0.8) return ArtTheme.Current.Orange;
            return ArtTheme.Current.Green;
        }

        internal static Color TextColor(double ratio)
        {
            if (ratio >= 1) return ArtTheme.Current.Red;
            if (ratio >= 0.8) return ArtTheme.Current.Orange;
            return ArtTheme.Current.Text;
        }

        internal static string Percent(double ratio)
        {
            double value = ratio * 100.0;
            if (value > 999.9) value = 999.9;
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        internal static string PercentNumber(double ratio)
        {
            double value = ratio * 100.0;
            if (value > 999.9) value = 999.9;
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class HealthLayoutResult
    {
        public RectangleF KeyboardCard;
        public RectangleF MouseCard;
        public RectangleF KeyboardPills;
        public RectangleF MousePills;
        public RectangleF KeyboardHero;
        public RectangleF KeyboardList;
        public RectangleF KeyboardMeta;
        public RectangleF MouseMetrics;
        public RectangleF MouseStatus;
        public RectangleF MouseStats;
        public RectangleF MouseSpark;
        public RectangleF MouseMeta;
        public RectangleF KeyboardEdit;
        public RectangleF MouseEdit;
        public float MouseMetricWidth;
        public float ListRowHeight;
        public bool Compact;
    }

    internal static class HealthLayout
    {
        internal const float Gap = 16f;
        internal const float CardPadding = 20f;
        internal const float PillsHeight = 26f;

        internal static HealthLayoutResult Compute(float cw, float left, float cardTop, float cardH)
        {
            if (cw < 320f) cw = 320f;
            if (cardH < 180f) cardH = 180f;
            bool compact = cardH < 430f;
            float pad = compact ? 16f : 20f;
            float cardW = (cw - Gap) / 2f;
            HealthLayoutResult result = new HealthLayoutResult();
            result.Compact = compact;
            result.KeyboardCard = new RectangleF(left, cardTop, cardW, cardH);
            result.MouseCard = new RectangleF(left + cardW + Gap, cardTop, cardW, cardH);

            float pillsTop = cardTop + (compact ? 58f : 72f);
            result.KeyboardPills = new RectangleF(left + pad, pillsTop, cardW - pad * 2f, PillsHeight);
            result.MousePills = new RectangleF(left + cardW + Gap + pad, pillsTop, cardW - pad * 2f, PillsHeight);

            float contentTop = cardTop + (compact ? 94f : 112f);
            float bottom = cardTop + cardH - (compact ? 14f : 18f);
            float metaH = 14f;
            result.KeyboardMeta = new RectangleF(left + pad, bottom - metaH, cardW - pad * 2f, metaH);
            result.MouseMeta = new RectangleF(left + cardW + Gap + pad, bottom - metaH, cardW - pad * 2f, metaH);

            float heroH = compact ? 86f : 104f;
            result.KeyboardHero = new RectangleF(left + pad, contentTop, cardW - pad * 2f, heroH);
            result.MouseMetrics = new RectangleF(left + cardW + Gap + pad, contentTop, cardW - pad * 2f, heroH);
            result.MouseMetricWidth = (cardW - pad * 2f - 16f) / 2f;

            float listTop = contentTop + heroH + (compact ? 8f : 12f);
            result.KeyboardList = new RectangleF(left + pad, listTop, cardW - pad * 2f,
                Math.Max(24f, result.KeyboardMeta.Y - listTop - 6f));
            result.ListRowHeight = Math.Min(compact ? 38f : 56f,
                Math.Max(24f, (result.KeyboardList.Height - (compact ? 18f : 20f)) / 4f));

            float statusTop = contentTop + heroH + (compact ? 6f : 10f);
            result.MouseStatus = new RectangleF(result.MouseCard.X + pad, statusTop, cardW - pad * 2f, compact ? 22f : 24f);
            result.MouseStats = new RectangleF(result.MouseCard.X + pad, result.MouseStatus.Bottom + 4f, cardW - pad * 2f, 18f);
            float sparkH = compact ? 70f : 100f;
            result.MouseSpark = new RectangleF(result.MouseCard.X + pad,
                Math.Max(result.MouseStats.Bottom + 6f, result.MouseMeta.Y - 8f - sparkH),
                cardW - pad * 2f, sparkH);

            float editW = compact ? 286f : 304f;
            float editH = compact ? 190f : 208f;
            result.KeyboardEdit = new RectangleF(result.KeyboardCard.Right - 16f - editW, cardTop + (compact ? 42f : 50f), editW, editH);
            result.MouseEdit = new RectangleF(result.MouseCard.Right - 16f - editW, cardTop + (compact ? 42f : 50f), editW, editH);
            return result;
        }
    }

    /// <summary>按需弹出的编辑面板。只改内存,保存由 Dashboard 负责落盘。</summary>
    internal sealed class HealthEditHost : Panel
    {
        internal Action SaveRequested;
        internal Action CancelRequested;

        private readonly Label _title = new Label();
        private readonly Label _aliasLabel = new Label();
        private readonly Label _ratedLabel = new Label();
        private readonly Label _rated2Label = new Label();
        private readonly Label _unit1 = new Label();
        private readonly Label _unit2 = new Label();
        private readonly TextBox _alias = new TextBox();
        private readonly NumericUpDown _rated1 = new NumericUpDown();
        private readonly NumericUpDown _rated2 = new NumericUpDown();
        private readonly Button _save = new Button();
        private readonly Button _cancel = new Button();
        private readonly List<Font> _fonts = new List<Font>();
        private Font _titleFont;
        private Font _baseFont;
        private float _scale = 1f;
        private bool _mouse;

        internal HealthEditHost()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint, true);
            _title.AutoSize = false;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _alias.MaxLength = 80;
            _rated1.Minimum = _rated2.Minimum = 100;
            _rated1.Maximum = _rated2.Maximum = 1000000;
            _rated1.Increment = _rated2.Increment = 1000;
            _rated1.DecimalPlaces = _rated2.DecimalPlaces = 0;
            _save.Text = "保存";
            _cancel.Text = "取消";
            _save.Click += delegate { if (SaveRequested != null) SaveRequested(); };
            _cancel.Click += delegate { if (CancelRequested != null) CancelRequested(); };
            Controls.AddRange(new Control[] { _title, _aliasLabel, _alias, _ratedLabel, _rated1, _unit1,
                _rated2Label, _rated2, _unit2, _save, _cancel });
            ApplyTheme();
        }

        internal void FocusAlias()
        {
            _alias.Focus();
            _alias.SelectAll();
        }

        internal string AliasText { get { return (_alias.Text ?? "").Trim(); } }
        internal long Rated1 { get { return (long)_rated1.Value * 10000L; } }
        internal long Rated2 { get { return (long)_rated2.Value * 10000L; } }

        internal void SetScale(float scale)
        {
            if (scale < 1f) scale = 1f;
            bool changed = Math.Abs(scale - _scale) > 0.001f || _baseFont == null;
            _scale = scale;
            if (changed) RebuildFonts();
            Relayout();
            ApplyTheme();
        }

        internal void SetMode(bool mouse, string alias, long rated1, long rated2, float scale)
        {
            _mouse = mouse;
            if (scale < 1f) scale = 1f;
            bool scaleChanged = _baseFont == null || Math.Abs(scale - _scale) > 0.001f;
            _scale = scale;
            _title.Text = mouse ? "编辑鼠标" : "编辑键盘";
            _alias.Text = alias ?? "";
            _rated1.Value = ClampMillions(rated1);
            _rated2.Value = ClampMillions(rated2);
            _rated2.Visible = mouse;
            _rated2Label.Visible = mouse;
            _unit2.Visible = mouse;
            if (scaleChanged) RebuildFonts();
            Relayout();
            ApplyTheme();
            Invalidate();
        }

        private decimal ClampMillions(long rated)
        {
            decimal value = Math.Max(0, rated) / 10000M;
            if (value < _rated1.Minimum) value = _rated1.Minimum;
            if (value > _rated1.Maximum) value = _rated1.Maximum;
            return value;
        }

        private void RebuildFonts()
        {
            _titleFont = new Font("Microsoft YaHei UI", 11f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
            _baseFont = new Font("Microsoft YaHei UI", 11f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            _fonts.Add(_titleFont);
            _fonts.Add(_baseFont);
            Font = _baseFont;
            Control[] children = new Control[] { _title, _aliasLabel, _alias, _ratedLabel, _rated1,
                _unit1, _rated2Label, _rated2, _unit2, _save, _cancel };
            for (int i = 0; i < children.Length; i++) children[i].Font = _baseFont;
            _title.Font = _titleFont;
        }

        private void Relayout()
        {
            float s = _scale;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            _title.SetBounds((int)(12 * s), (int)(8 * s), Math.Max(40, w - (int)(24 * s)), (int)(20 * s));
            _aliasLabel.SetBounds((int)(12 * s), (int)(34 * s), Math.Max(40, w - (int)(24 * s)), (int)(16 * s));
            _alias.SetBounds((int)(12 * s), (int)(51 * s), Math.Max(80, w - (int)(24 * s)), (int)(28 * s));
            _ratedLabel.SetBounds((int)(12 * s), (int)(89 * s), Math.Max(40, w - (int)(24 * s)), (int)(16 * s));
            if (_mouse)
            {
                _rated1.SetBounds((int)(12 * s), (int)(106 * s), (int)(94 * s), (int)(28 * s));
                _unit1.SetBounds((int)(109 * s), (int)(111 * s), (int)(30 * s), (int)(18 * s));
                _rated2Label.SetBounds((int)(148 * s), (int)(89 * s), (int)(110 * s), (int)(16 * s));
                _rated2.SetBounds((int)(148 * s), (int)(106 * s), (int)(94 * s), (int)(28 * s));
                _unit2.SetBounds((int)(245 * s), (int)(111 * s), (int)(30 * s), (int)(18 * s));
                _ratedLabel.Text = "左微动（万次）";
                _rated2Label.Text = "右微动（万次）";
            }
            else
            {
                _rated1.SetBounds((int)(12 * s), (int)(106 * s), (int)(118 * s), (int)(28 * s));
                _unit1.SetBounds((int)(133 * s), (int)(111 * s), (int)(30 * s), (int)(18 * s));
                _ratedLabel.Text = "额定寿命（万次）";
            }
            _unit1.Text = "万次";
            _unit2.Text = "万次";
            _cancel.SetBounds((int)(12 * s), Math.Max((int)(148 * s), h - (int)(40 * s)), (int)(80 * s), (int)(28 * s));
            _save.SetBounds(Math.Max((int)(98 * s), w - (int)(92 * s)), Math.Max((int)(148 * s), h - (int)(40 * s)),
                (int)(80 * s), (int)(28 * s));
        }

        private void ApplyTheme()
        {
            ArtTheme theme = ArtTheme.Current;
            BackColor = ArtTheme.Mix(theme.Card, theme.Background, 0.18);
            ForeColor = theme.Text;
            _title.ForeColor = theme.Text;
            _aliasLabel.ForeColor = theme.Muted;
            _ratedLabel.ForeColor = theme.Muted;
            _rated2Label.ForeColor = theme.Muted;
            _unit1.ForeColor = theme.Muted;
            _unit2.ForeColor = theme.Muted;
            TextBox[] boxes = new TextBox[] { _alias };
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].BackColor = theme.Raised;
                boxes[i].ForeColor = theme.Text;
                boxes[i].BorderStyle = BorderStyle.FixedSingle;
            }
            _rated1.BackColor = theme.Raised;
            _rated1.ForeColor = theme.Text;
            _rated2.BackColor = theme.Raised;
            _rated2.ForeColor = theme.Text;
            _save.BackColor = ArtTheme.Mix(theme.Raised, theme.Accent, 0.24);
            _save.ForeColor = theme.Text;
            _save.FlatStyle = FlatStyle.Flat;
            _save.FlatAppearance.BorderSize = 0;
            _cancel.BackColor = theme.Raised;
            _cancel.ForeColor = theme.Muted;
            _cancel.FlatStyle = FlatStyle.Flat;
            _cancel.FlatAppearance.BorderSize = 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ArtTheme theme = ArtTheme.Current;
            Color border = Color.FromArgb(42, theme.Accent);
            using (GraphicsPath path = WearDraw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Pen pen = new Pen(border))
                e.Graphics.DrawPath(pen, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                for (int i = 0; i < _fonts.Count; i++) _fonts[i].Dispose();
                _fonts.Clear();
                _titleFont = null;
                _baseFont = null;
            }
        }
    }

    /// <summary>按需弹出的设备信息面板。</summary>
    internal sealed class HealthInfoHost : Panel
    {
        internal Action CopyRequested;
        internal Action CloseRequested;

        private readonly Label _title = new Label();
        private readonly Label _name = new Label();
        private readonly TextBox _path = new TextBox();
        private readonly Button _copy = new Button();
        private readonly Button _close = new Button();
        private readonly List<Font> _fonts = new List<Font>();
        private Font _titleFont;
        private Font _baseFont;
        private float _scale = 1f;

        internal HealthInfoHost()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint, true);
            _title.AutoSize = false;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.Text = "设备信息";
            _path.ReadOnly = true;
            _path.Multiline = true;
            _path.ScrollBars = ScrollBars.None;
            _path.WordWrap = true;
            _copy.Text = "复制路径";
            _close.Text = "关闭";
            _copy.Click += delegate { if (CopyRequested != null) CopyRequested(); };
            _close.Click += delegate { if (CloseRequested != null) CloseRequested(); };
            Controls.AddRange(new Control[] { _title, _name, _path, _copy, _close });
            ApplyTheme();
        }

        internal string PathText { get { return _path.Text ?? ""; } }

        internal void SetScale(float scale)
        {
            if (scale < 1f) scale = 1f;
            bool changed = Math.Abs(scale - _scale) > 0.001f || _baseFont == null;
            _scale = scale;
            if (changed) RebuildFonts();
            Relayout();
            ApplyTheme();
        }

        internal void SetInfo(string friendly, string path, float scale)
        {
            if (scale < 1f) scale = 1f;
            bool scaleChanged = _baseFont == null || Math.Abs(scale - _scale) > 0.001f;
            _scale = scale;
            _name.Text = friendly ?? "";
            _path.Text = path ?? "";
            if (scaleChanged) RebuildFonts();
            Relayout();
            ApplyTheme();
            Invalidate();
        }

        private void RebuildFonts()
        {
            _titleFont = new Font("Microsoft YaHei UI", 11f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
            _baseFont = new Font("Microsoft YaHei UI", 10f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            _fonts.Add(_titleFont);
            _fonts.Add(_baseFont);
            Font = _baseFont;
            Control[] children = new Control[] { _title, _name, _path, _copy, _close };
            for (int i = 0; i < children.Length; i++) children[i].Font = _baseFont;
            _title.Font = _titleFont;
        }

        private void Relayout()
        {
            float s = _scale;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            _title.SetBounds((int)(12 * s), (int)(8 * s), Math.Max(40, w - (int)(24 * s)), (int)(20 * s));
            _name.SetBounds((int)(12 * s), (int)(31 * s), Math.Max(40, w - (int)(24 * s)), (int)(18 * s));
            _path.SetBounds((int)(12 * s), (int)(53 * s), Math.Max(80, w - (int)(24 * s)), Math.Max(28, h - (int)(96 * s)));
            _cancelLayout(w, h, s);
        }

        private void _cancelLayout(int w, int h, float s)
        {
            _copy.SetBounds((int)(12 * s), h - (int)(38 * s), (int)(76 * s), (int)(26 * s));
            _close.SetBounds(Math.Max((int)(94 * s), w - (int)(88 * s)), h - (int)(38 * s), (int)(76 * s), (int)(26 * s));
        }

        private void ApplyTheme()
        {
            ArtTheme theme = ArtTheme.Current;
            BackColor = ArtTheme.Mix(theme.Card, theme.Background, 0.18);
            ForeColor = theme.Text;
            _title.ForeColor = theme.Text;
            _name.ForeColor = theme.Muted;
            _path.BackColor = theme.Raised;
            _path.ForeColor = theme.Text;
            _path.BorderStyle = BorderStyle.FixedSingle;
            _copy.BackColor = ArtTheme.Mix(theme.Raised, theme.Accent, 0.20);
            _copy.ForeColor = theme.Text;
            _copy.FlatStyle = FlatStyle.Flat;
            _copy.FlatAppearance.BorderSize = 0;
            _close.BackColor = theme.Raised;
            _close.ForeColor = theme.Muted;
            _close.FlatStyle = FlatStyle.Flat;
            _close.FlatAppearance.BorderSize = 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ArtTheme theme = ArtTheme.Current;
            using (GraphicsPath path = WearDraw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Pen pen = new Pen(Color.FromArgb(42, theme.Accent)))
                e.Graphics.DrawPath(pen, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                for (int i = 0; i < _fonts.Count; i++) _fonts[i].Dispose();
                _fonts.Clear();
                _titleFont = null;
                _baseFont = null;
            }
        }
    }
}
