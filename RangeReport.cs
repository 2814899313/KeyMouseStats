// ============================================================================
//  键鼠统计 - 区间与分布报告页  RangeReport.cs
//
//  1.7.0 新增:
//    · 区间回顾:任意区间对比紧随其前的等长区间,数据全部来自 RangeStats。
//    · 分布:箱线图与直方图,用分布替代单点均值。
//  只读取既有日记录;缺失日不补零,今日不计入完整区间。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace KeyMouseStats
{
    /// <summary>区间回顾:当前区间对比紧随其前的等长区间。</summary>
    internal sealed class RangeReportData
    {
        public const string Note = "当前区间为最近若干个完整日,对比区间是紧随其前的等长区间;今日尚未结束,不计入。\n缺失日不补零,日均按有效天数计算。合计变化只在两期有效天数相同时才可直接比较,因此同时给出日均变化。基准为 0 或缺少记录时显示「基准不足」。";

        public string Caption, Footer, RangeLabel, ComparisonLabel = "紧随其前";
        public string[] Cards = new string[4], CardValues = new string[4];
        public string[] Headings = { "指标", "本期", "上期", "变化", "覆盖" };
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly List<KeyValuePair<DateTime, double>> Daily = new List<KeyValuePair<DateTime, double>>();
        public RangeMetrics Current, Previous;
        public double PreviousDailyMean;
        public bool HasPrevious;

        private static string Percent(double change)
        {
            return RangeComparison.Percent(change, "基准不足");
        }
        private static string Number(double value, string format)
        {
            return double.IsNaN(value) ? "--" : value.ToString(format, CultureInfo.InvariantCulture);
        }
        private static string Duration(double seconds)
        {
            return double.IsNaN(seconds) ? "--" : ActivityMonitor.FormatDuration(seconds);
        }

        public static RangeReportData Build(DateTime start, DateTime end)
        {
            return Build(start, end, DateTime.MinValue, DateTime.MinValue, "紧随其前");
        }

        public static RangeReportData Build(DateTime start, DateTime end, DateTime comparisonStart, DateTime comparisonEnd)
        {
            return Build(start, end, comparisonStart, comparisonEnd, "自定义");
        }

        /// <summary>comparison 为空(MinValue)时对比紧随其前的等长区间。</summary>
        public static RangeReportData Build(DateTime start, DateTime end, DateTime comparisonStart, DateTime comparisonEnd, string comparisonLabel)
        {
            RangeReportData data = new RangeReportData();
            data.ComparisonLabel = comparisonLabel;
            data.Current = RangeStats.Of(start, end);
            data.Previous = new RangeMetrics();
            if (data.Current.Days > 0)
            {
                if (comparisonStart == DateTime.MinValue)
                {
                    DateTime previousEnd = data.Current.Start.AddDays(-1);
                    data.Previous = RangeStats.Of(previousEnd.AddDays(-(data.Current.Days - 1)), previousEnd);
                }
                else
                {
                    data.Previous = RangeStats.Of(comparisonStart, comparisonEnd);
                }
            }
            data.HasPrevious = !data.Previous.IsEmpty;
            data.PreviousDailyMean = data.Previous.KeysPerDay;
            data.Daily.AddRange(RangeStats.DailySeries(start, end, delegate(DayRecord day) { return day.Keys; }));

            RangeMetrics now = data.Current, before = data.Previous;
            bool dpi = MouseDistance.ValidDpi(Store.MouseDpi);
            string coverage = "本期 " + now.CoverageText + " · 上期 " + before.CoverageText;
            data.RangeLabel = now.Days == 0 ? "无完整日"
                : now.Start.ToString("MM.dd") + "—" + now.End.ToString("MM.dd") + " · " + now.Days + " 天";
            data.Caption = now.Days == 0 ? "所选区间没有完整日"
                : data.RangeLabel + "，对比「" + data.ComparisonLabel + "」· 逐日击键（可在图上拖动选择子区间）";

            data.Cards = new string[] { "本期击键", "日均较上期", "本期活跃时长", "有效天数" };
            data.CardValues = new string[] { now.IsEmpty ? "无记录" : Analysis.FmtCount(now.Keys),
                Percent(RangeMath.Relative(now.KeysPerDay, before.KeysPerDay)),
                now.IsEmpty ? "--" : ActivityMonitor.FormatDuration(now.ActiveSeconds),
                now.CoverageText };

            data.Rows.Add(new string[] { "击键（合计）", Analysis.FmtCount(now.Keys), Analysis.FmtCount(before.Keys), Percent(RangeMath.Relative(now.Keys, before.Keys)), coverage });
            data.Rows.Add(new string[] { "日均击键", Number(now.KeysPerDay, "0"), Number(before.KeysPerDay, "0"), Percent(RangeMath.Relative(now.KeysPerDay, before.KeysPerDay)), "按有效天数平均" });
            data.Rows.Add(new string[] { "鼠标点击（合计）", Analysis.FmtCount(now.Clicks), Analysis.FmtCount(before.Clicks), Percent(RangeMath.Relative(now.Clicks, before.Clicks)), coverage });
            data.Rows.Add(new string[] { "滚轮事件", Analysis.FmtCount(now.Wheel), Analysis.FmtCount(before.Wheel), Percent(RangeMath.Relative(now.Wheel, before.Wheel)), "事件次数,不是滚动距离" });
            data.Rows.Add(new string[] { "组合动作（另计）", Analysis.FmtCount(now.Combos), Analysis.FmtCount(before.Combos), Percent(RangeMath.Relative(now.Combos, before.Combos)), "与击键有重叠,不相加" });
            data.Rows.Add(new string[] { "活跃时长（合计）", ActivityMonitor.FormatDuration(now.ActiveSeconds), ActivityMonitor.FormatDuration(before.ActiveSeconds), Percent(RangeMath.Relative(now.ActiveSeconds, before.ActiveSeconds)), coverage });
            data.Rows.Add(new string[] { "日均活跃时长", Duration(now.ActivePerDay), Duration(before.ActivePerDay), Percent(RangeMath.Relative(now.ActivePerDay, before.ActivePerDay)), "按有效天数平均" });
            data.Rows.Add(new string[] { "连续使用段", now.Sessions + " 段", before.Sessions + " 段", Percent(RangeMath.Relative(now.Sessions, before.Sessions)), "含空闲阈值内停顿" });
            data.Rows.Add(new string[] { "平均段长", Duration(now.MeanSession), Duration(before.MeanSession), Percent(RangeMath.Relative(now.MeanSession, before.MeanSession)), "本期 " + now.Sessions + " 段" });
            data.Rows.Add(new string[] { "移动距离", dpi ? Analysis.FmtMeters(now.MoveMeters) : "未设置 DPI", dpi ? Analysis.FmtMeters(before.MoveMeters) : "未设置 DPI", dpi ? Percent(RangeMath.Relative(now.MoveMeters, before.MoveMeters)) : "基准不足", "受 DPI 设置影响" });
            data.Rows.Add(new string[] { "单日峰值 APM", now.PeakApm.ToString(), before.PeakApm.ToString(), Percent(RangeMath.Relative(now.PeakApm, before.PeakApm)), "峰值从新版开始记录" });

            data.Footer = now.IsEmpty ? "所选区间没有任何完整日记录,先在活跃使用中积累数据。"
                : "对比区间(" + data.ComparisonLabel + ")截至 " + before.End.ToString("MM.dd") + "；缺失日不补零,日均按有效天数计算。";
            return data;
        }
    }

    /// <summary>自定义区间选择:两个日期,结果会被区间口径裁剪到最近一个完整日。</summary>
    internal sealed class RangePickerDialog : ThemedDialog
    {
        private readonly DateTimePicker _start = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
        private readonly DateTimePicker _end = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };

        public DateTime Start { get { return _start.Value.Date; } }
        public DateTime End { get { return _end.Value.Date; } }

        public RangePickerDialog(DateTime start, DateTime end)
        {
            Text = "自定义区间"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9f); ClientSize = new Size(360, 152);
            _start.Value = start; _end.Value = end;
            Label first = new Label { Text = "开始日期", TextAlign = ContentAlignment.MiddleLeft };
            Label last = new Label { Text = "结束日期", TextAlign = ContentAlignment.MiddleLeft };
            Label hint = new Label { Text = "今日尚未结束，区间会截至最近一个完整日。", TextAlign = ContentAlignment.MiddleLeft };
            Button ok = new Button { Text = "确定", DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
            first.SetBounds(20, 22, 84, 24); _start.SetBounds(110, 20, 210, 26);
            last.SetBounds(20, 60, 84, 24); _end.SetBounds(110, 58, 210, 26);
            hint.SetBounds(20, 94, 320, 20);
            ok.SetBounds(158, 118, 76, 28); cancel.SetBounds(244, 118, 76, 28);
            Controls.AddRange(new Control[] { first, last, hint, _start, _end, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }
    }

    /// <summary>区间回顾图表:当前区间逐日击键,叠加对比区间日均参考线。</summary>
    internal sealed class RangeReportVisual : Control
    {
        internal float UiScale;
        /// <summary>在图上拖拽选择的子区间(含起止日)。</summary>
        internal event Action<DateTime, DateTime> RangeSelected;
        private float _chartLeft, _chartRight, _dragFrom, _dragTo;
        private int _chartDays;
        private DateTime _chartStart;
        private bool _dragging;
        private readonly RangeReportData data;
        private readonly List<KeyValuePair<RectangleF, string>> targets = new List<KeyValuePair<RectangleF, string>>();
        private readonly ToolTip tip = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 20000 };
        private int hover = -1;

        public RangeReportVisual(DateTime start, DateTime end)
            : this(start, end, DateTime.MinValue, DateTime.MinValue, "紧随其前")
        {
        }

        public RangeReportVisual(DateTime start, DateTime end, DateTime comparisonStart, DateTime comparisonEnd, string comparisonLabel)
        {
            data = RangeReportData.Build(start, end, comparisonStart, comparisonEnd, comparisonLabel);
            DoubleBuffered = true; ResizeRedraw = true;
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                float scale = UiScale > 0 ? UiScale : 1f;
                PointF point = new PointF(e.X / scale, e.Y / scale);
                if (_dragging) { _dragTo = point.X; Invalidate(); return; }
                int next = -1;
                for (int i = 0; i < targets.Count; i++) if (targets[i].Key.Contains(point)) { next = i; break; }
                if (next != hover) { hover = next; tip.SetToolTip(this, next >= 0 ? targets[next].Value : ""); Invalidate(); }
            };
            MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || _chartDays <= 0) return;
                float scale = UiScale > 0 ? UiScale : 1f;
                _dragging = true; _dragFrom = _dragTo = e.X / scale; Invalidate();
            };
            MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (!_dragging) return;
                _dragging = false;
                _dragTo = e.X / (UiScale > 0 ? UiScale : 1f);
                Invalidate();
                SelectByX(_dragFrom, _dragTo);
            };
            MouseLeave += delegate { hover = -1; Invalidate(); };
        }

        public void RenderTo(Graphics g, float scale)
        {
            float previous = UiScale; UiScale = scale;
            try
            {
                using (PaintEventArgs args = new PaintEventArgs(g, new Rectangle(0, 0,
                    Math.Max(1, (int)(ClientSize.Width * scale)), Math.Max(1, (int)(ClientSize.Height * scale))))) OnPaint(args);
            }
            finally { UiScale = previous; }
        }

        /// <summary>按控件坐标(未缩放)选择子区间并触发 RangeSelected。需要先渲染一次以获得图表几何。</summary>
        internal bool SelectByX(float fromX, float toX)
        {
            if (_chartDays <= 0 || RangeSelected == null) return false;
            float left = Math.Min(fromX, toX), right = Math.Max(fromX, toX);
            if (right - left < 4) return false;
            float step = (_chartRight - _chartLeft) / _chartDays;
            if (step <= 0) return false;
            int first = (int)Math.Floor((left - _chartLeft) / step);
            int last = (int)Math.Floor((right - _chartLeft) / step);
            if (first < 0) first = 0;
            if (last > _chartDays - 1) last = _chartDays - 1;
            if (last < first) { int swap = first; first = last; last = swap; }
            RangeSelected(_chartStart.AddDays(first), _chartStart.AddDays(last));
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        private static void TextAt(Graphics g, string text, Font font, Color color, RectangleF rect)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text ?? "", font, brush, rect, format);
        }
        private static void Fill(Graphics g, Color color, RectangleF rect)
        {
            if (rect.Width > 0 && rect.Height > 0) using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, rect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float scale = UiScale > 0 ? UiScale : g.DpiX / 96f;
            g.ScaleTransform(scale, scale);
            float w = ClientSize.Width / scale;
            ArtTheme t = ArtTheme.Current;
            g.Clear(t.Background); g.SmoothingMode = SmoothingMode.AntiAlias; targets.Clear();

            using (Font small = new Font("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font body = new Font("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font large = new Font("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float card = (w - 44) / 4;
                for (int i = 0; i < 4; i++)
                {
                    float x = 10 + i * (card + 8);
                    ReportDesign.Surface(g, new RectangleF(x, 10, card, 67), t.Card, t.Line);
                    TextAt(g, data.Cards[i], small, t.Muted, new RectangleF(x + 12, 17, card - 24, 20));
                    TextAt(g, data.CardValues[i], data.CardValues[i].Length > 10 ? body : large, t.Text, new RectangleF(x + 12, 37, card - 24, 32));
                }
                TextAt(g, data.Caption, body, t.Text, new RectangleF(14, 86, w - 28, 23));

                long maximum = 1;
                foreach (KeyValuePair<DateTime, double> sample in data.Daily) maximum = Math.Max(maximum, (long)sample.Value);
                if (data.HasPrevious && data.PreviousDailyMean > maximum) maximum = (long)Math.Ceiling(data.PreviousDailyMean);

                float left = 56, right = w - 20, top = 126, bottom = 250;
                _chartLeft = left; _chartRight = right; _chartDays = data.Current.Days; _chartStart = data.Current.Start;
                using (Pen axis = new Pen(t.Line)) g.DrawLine(axis, left - 4, bottom, right, bottom);
                TextAt(g, Analysis.FmtCount(maximum), small, t.Muted, new RectangleF(8, top - 8, 44, 18));
                TextAt(g, "0", small, t.Muted, new RectangleF(34, bottom - 16, 20, 18));

                if (data.Current.Days > 0)
                {
                    float step = (right - left) / data.Current.Days;
                    float barWidth = Math.Max(2, Math.Min(18, step - 3));
                    int index = 0;
                    for (DateTime date = data.Current.Start; date <= data.Current.End; date = date.AddDays(1), index++)
                    {
                        double value = double.NaN;
                        foreach (KeyValuePair<DateTime, double> sample in data.Daily) if (sample.Key == date) { value = sample.Value; break; }
                        float x = left + index * step + (step - barWidth) / 2;
                        if (RangeMath.IsNumber(value))
                        {
                            float height = (float)(value / maximum * (bottom - top));
                            Fill(g, t.Accent, new RectangleF(x, bottom - height, barWidth, height));
                            targets.Add(new KeyValuePair<RectangleF, string>(new RectangleF(left + index * step, top - 6, step, bottom - top + 12),
                                date.ToString("MM.dd") + " " + RangeRules.Weekday(RangeRules.WeekdayIndex(date)) + " · " + Analysis.FmtCount((long)value) + " 次击键"));
                        }
                        else
                        {
                            using (Pen missing = new Pen(Color.FromArgb(80, t.Muted))) g.DrawLine(missing, x, bottom - 8, x + barWidth, bottom - 2);
                            targets.Add(new KeyValuePair<RectangleF, string>(new RectangleF(left + index * step, top - 6, step, bottom - top + 12),
                                date.ToString("MM.dd") + " · 无记录"));
                        }
                        if (data.Current.Days <= 40 || index % 5 == 0)
                            TextAt(g, date.ToString("MM.dd"), small, t.Muted, new RectangleF(left + index * step - 12, bottom + 4, step + 24, 18));
                    }
                }

                if (data.HasPrevious && data.PreviousDailyMean > 0)
                {
                    float y = bottom - (float)(data.PreviousDailyMean / maximum * (bottom - top));
                    using (Pen reference = new Pen(t.Orange, 1.4f))
                    {
                        reference.DashStyle = DashStyle.Dash;
                        g.DrawLine(reference, left - 4, y, right, y);
                    }
                    TextAt(g, "上期日均 " + Analysis.FmtCount((long)data.PreviousDailyMean), small, t.Orange, new RectangleF(right - 160, y - 18, 158, 18));
                }

                if (hover >= 0 && hover < targets.Count)
                    using (Pen outline = new Pen(t.Accent, 1.4f))
                    {
                        RectangleF r = targets[hover].Key;
                        g.DrawRectangle(outline, r.X + 1, r.Y + 2, Math.Max(2, r.Width - 2), r.Height - 4);
                    }
                if (_dragging && _chartDays > 0)
                {
                    float dragLeft = Math.Min(_dragFrom, _dragTo), dragRight = Math.Max(_dragFrom, _dragTo);
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(60, t.Accent)))
                        g.FillRectangle(brush, dragLeft, top - 6, Math.Max(1, dragRight - dragLeft), bottom - top + 12);
                }
                TextAt(g, hover >= 0 && hover < targets.Count ? targets[hover].Value : data.Footer, small, t.Muted,
                    new RectangleF(14, 274, w - 28, 20));
            }
        }
    }

    /// <summary>分布:箱线图与直方图。</summary>
    internal sealed class DistributionReportData
    {
        public const string Note = "样本只包含所选区间内的有效日 / 有效段,缺失日不补零。箱线图依次显示最小值、P10、中位数 P50、P90 与最大值;三项指标单位不同,各自按自身范围缩放,不能横向比较长度。\n变异系数 CV = 标准差 ÷ 均值,越大表示波动越大,它不是评分。直方图把逐日击键分成等宽区间,最后一箱包含最大值。";

        public string Caption, Footer;
        public string[] Cards = new string[4], CardValues = new string[4];
        public string[] Headings = { "样本", "数量", "P10", "P50（中位数）", "P90", "最大值", "变异系数" };
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly List<string> SeriesNames = new List<string>();
        public readonly List<Distribution> Series = new List<Distribution>();
        public readonly List<string> SeriesP50 = new List<string>();
        public readonly List<string> SeriesMin = new List<string>();
        public readonly List<string> SeriesMax = new List<string>();
        public Distribution DailyKeys;
        public int ObservedDays, RangeDays;

        private static int BinCount(int samples)
        {
            // 样本少时用更少的箱子,否则大量空箱会让直方图看不出形状。
            return Math.Max(1, Math.Min(12, samples / 2));
        }

        private static string Number(double value, string format)
        {
            return double.IsNaN(value) ? "--" : value.ToString(format, CultureInfo.InvariantCulture);
        }

        public static DistributionReportData Build(DateTime start, DateTime end)
        {
            DistributionReportData data = new DistributionReportData();
            RangeMetrics metrics = RangeStats.Of(start, end);
            data.ObservedDays = metrics.Observed;
            data.RangeDays = metrics.Days;

            List<double> dailyKeys = RangeStats.DailyValues(start, end, delegate(DayRecord day) { return day.Keys; });
            data.DailyKeys = Distribution.Of(dailyKeys, BinCount(dailyKeys.Count));
            data.Caption = metrics.Days == 0 ? "所选区间没有完整日"
                : metrics.Start.ToString("MM.dd") + "—" + metrics.End.ToString("MM.dd") + " · " + metrics.Days + " 天 · " + metrics.CoverageText + " 有效";

            List<double> appSeconds = new List<double>();
            for (DateTime date = metrics.Start; date <= metrics.End && metrics.Days > 0; date = date.AddDays(1))
            {
                DayRecord day = Analysis.GetDay(date);
                if (day == null || day.IsEmpty) continue;
                foreach (AppUsage app in day.Apps.Values) if (app.ActiveSeconds > 0) appSeconds.Add(app.ActiveSeconds);
            }

            data.AddSeries("逐日击键", Distribution.Of(dailyKeys, BinCount(dailyKeys.Count)), delegate(double v) { return Analysis.FmtCount((long)Math.Round(v)) + " 次"; });
            data.AddSeries("连续段时长", Distribution.Of(metrics.SessionLengths, BinCount(metrics.SessionLengths.Count)), delegate(double v) { return ActivityMonitor.FormatDuration(v); });
            data.AddSeries("单应用单日时长", Distribution.Of(appSeconds, BinCount(appSeconds.Count)), delegate(double v) { return ActivityMonitor.FormatDuration(v); });

            data.Cards = new string[] { "区间", "有效天数", "逐日击键 P50", "逐日击键 P90" };
            data.CardValues = new string[] { metrics.Days == 0 ? "无完整日" : metrics.Days + " 天", metrics.CoverageText,
                data.DailyKeys.Count > 0 ? Analysis.FmtCount((long)Math.Round(data.DailyKeys.P50)) : "--",
                data.DailyKeys.Count > 0 ? Analysis.FmtCount((long)Math.Round(data.DailyKeys.P90)) : "--" };

            for (int i = 0; i < data.Series.Count; i++)
            {
                Distribution series = data.Series[i];
                if (series.Count == 0) { data.Rows.Add(new string[] { data.SeriesNames[i], "0", "--", "--", "--", "--", "--" }); continue; }
                data.Rows.Add(new string[] { data.SeriesNames[i], series.Count.ToString(),
                    data.Format(i, series.P10), data.Format(i, series.P50), data.Format(i, series.P90), data.Format(i, series.Max),
                    Number(series.Cv, "0.###") });
            }
            data.Footer = metrics.IsEmpty ? "所选区间没有任何完整日记录。"
                : "样本量 " + data.DailyKeys.Count + " 天 / " + metrics.Sessions + " 段；CV 越大表示波动越大,不评价好坏。";
            return data;
        }

        private void AddSeries(string name, Distribution distribution, Func<double, string> formatter)
        {
            SeriesNames.Add(name);
            Series.Add(distribution);
            _formatters.Add(formatter);
            SeriesMin.Add(distribution.Count > 0 ? formatter(distribution.Min) : "--");
            SeriesP50.Add(distribution.Count > 0 ? formatter(distribution.P50) : "--");
            SeriesMax.Add(distribution.Count > 0 ? formatter(distribution.Max) : "--");
        }

        private readonly List<Func<double, string>> _formatters = new List<Func<double, string>>();
        internal string Format(int index, double value)
        {
            if (index < 0 || index >= _formatters.Count || !RangeMath.IsNumber(value)) return "--";
            return _formatters[index](value);
        }
    }

    /// <summary>分布图表:三行箱线图 + 逐日击键直方图。</summary>
    internal sealed class DistributionReportVisual : Control
    {
        internal float UiScale;
        private readonly DistributionReportData data;
        private readonly List<KeyValuePair<RectangleF, string>> targets = new List<KeyValuePair<RectangleF, string>>();
        private readonly ToolTip tip = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 20000 };
        private int hover = -1;

        public DistributionReportVisual(DateTime start, DateTime end)
        {
            data = DistributionReportData.Build(start, end);
            DoubleBuffered = true; ResizeRedraw = true;
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                float scale = UiScale > 0 ? UiScale : 1f;
                PointF point = new PointF(e.X / scale, e.Y / scale);
                int next = -1;
                for (int i = 0; i < targets.Count; i++) if (targets[i].Key.Contains(point)) { next = i; break; }
                if (next != hover) { hover = next; tip.SetToolTip(this, next >= 0 ? targets[next].Value : ""); Invalidate(); }
            };
            MouseLeave += delegate { hover = -1; Invalidate(); };
        }

        public void RenderTo(Graphics g, float scale)
        {
            float previous = UiScale; UiScale = scale;
            try
            {
                using (PaintEventArgs args = new PaintEventArgs(g, new Rectangle(0, 0,
                    Math.Max(1, (int)(ClientSize.Width * scale)), Math.Max(1, (int)(ClientSize.Height * scale))))) OnPaint(args);
            }
            finally { UiScale = previous; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        private static void TextAt(Graphics g, string text, Font font, Color color, RectangleF rect)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text ?? "", font, brush, rect, format);
        }
        private static void Fill(Graphics g, Color color, RectangleF rect)
        {
            if (rect.Width > 0 && rect.Height > 0) using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, rect);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float scale = UiScale > 0 ? UiScale : g.DpiX / 96f;
            g.ScaleTransform(scale, scale);
            float w = ClientSize.Width / scale;
            ArtTheme t = ArtTheme.Current;
            g.Clear(t.Background); g.SmoothingMode = SmoothingMode.AntiAlias; targets.Clear();

            using (Font small = new Font("Microsoft YaHei UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font body = new Font("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font large = new Font("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float card = (w - 44) / 4;
                for (int i = 0; i < 4; i++)
                {
                    float x = 10 + i * (card + 8);
                    ReportDesign.Surface(g, new RectangleF(x, 10, card, 67), t.Card, t.Line);
                    TextAt(g, data.Cards[i], small, t.Muted, new RectangleF(x + 12, 17, card - 24, 20));
                    TextAt(g, data.CardValues[i], data.CardValues[i].Length > 10 ? body : large, t.Text, new RectangleF(x + 12, 37, card - 24, 32));
                }
                TextAt(g, data.Caption, body, t.Text, new RectangleF(14, 86, w - 28, 23));

                // ---- 箱线图:每个指标按自身范围缩放 ----
                float left = 150, right = w - 90;
                for (int i = 0; i < data.Series.Count; i++)
                {
                    Distribution series = data.Series[i];
                    float y = 132 + i * 50;
                    TextAt(g, data.SeriesNames[i], small, t.Text, new RectangleF(14, y + 2, 130, 20));
                    if (series.Count == 0)
                    {
                        TextAt(g, "无样本", small, t.Muted, new RectangleF(left, y + 2, 120, 20));
                        continue;
                    }
                    double minimum = series.Min, maximum = series.Max;
                    bool flat = maximum <= minimum;
                    double scaleMin = minimum, scaleSpan = flat ? 1 : maximum - minimum;
                    float scaleLeft = left, scaleRight = right;
                    Func<double, float> Position = delegate(double value)
                    { return scaleLeft + (float)((value - scaleMin) / scaleSpan) * (scaleRight - scaleLeft); };

                    float mid = y + 14;
                    if (flat)
                    {
                        // 样本完全相同:只画一个标记,避免三个数值标签叠在一起。
                        float center = (left + right) / 2;
                        using (Pen marker = new Pen(t.Accent, 2.4f)) g.DrawLine(marker, center, mid - 11, center, mid + 11);
                        TextAt(g, data.SeriesP50[i] + "（全部相同）", small, t.Accent, new RectangleF(center - 90, y + 30, 220, 18));
                    }
                    else
                    {
                        using (Pen whisker = new Pen(t.Line, 1.4f)) g.DrawLine(whisker, Position(minimum), mid, Position(maximum), mid);
                        using (Pen cap = new Pen(t.Muted, 1.4f))
                        {
                            g.DrawLine(cap, Position(minimum), mid - 6, Position(minimum), mid + 6);
                            g.DrawLine(cap, Position(maximum), mid - 6, Position(maximum), mid + 6);
                        }
                        float p10 = Position(series.P10), p90 = Position(series.P90), p50 = Position(series.P50);
                        Fill(g, ArtTheme.Mix(t.Card, t.Accent, .35), new RectangleF(p10, mid - 11, Math.Max(2, p90 - p10), 22));
                        using (Pen median = new Pen(t.Accent, 2.4f)) g.DrawLine(median, p50, mid - 11, p50, mid + 11);
                        TextAt(g, data.SeriesMin[i], small, t.Muted, new RectangleF(left, y + 30, 90, 18));
                        float medianX = Math.Max(left + 80, Math.Min(right - 170, p50 - 45));
                        TextAt(g, data.SeriesP50[i], small, t.Accent, new RectangleF(medianX, y + 30, 110, 18));
                        TextAt(g, data.SeriesMax[i], small, t.Muted, new RectangleF(right - 90, y + 30, 90, 18));
                    }
                    targets.Add(new KeyValuePair<RectangleF, string>(new RectangleF(left, y, right - left, 46),
                        data.SeriesNames[i] + " · " + series.Count + " 个样本 · P10 " + data.Format(i, series.P10) + " · P50 " + data.Format(i, series.P50)
                        + " · P90 " + data.Format(i, series.P90) + " · 最大 " + data.Format(i, series.Max)));
                }

                // ---- 直方图:逐日击键 ----
                TextAt(g, "逐日击键分布 · " + data.DailyKeys.Count + " 个有效日", small, t.Text, new RectangleF(14, 292, 320, 18));
                Distribution daily = data.DailyKeys;
                if (daily.Count > 0 && daily.Bins.Length > 0)
                {
                    float histogramTop = 314, histogramBottom = 386, histogramLeft = 56, histogramRight = w - 24;
                    int maximum = 1;
                    foreach (int bin in daily.Bins) maximum = Math.Max(maximum, bin);
                    float step = (histogramRight - histogramLeft) / daily.Bins.Length;
                    for (int i = 0; i < daily.Bins.Length; i++)
                    {
                        float height = (float)daily.Bins[i] / maximum * (histogramBottom - histogramTop);
                        RectangleF bar = new RectangleF(histogramLeft + i * step + 1, histogramBottom - height, Math.Max(2, step - 3), height);
                        Fill(g, ArtTheme.Mix(t.Card, t.Cyan, .55), bar);
                        if (i == 0 || i == daily.Bins.Length - 1 || i % 3 == 0)
                            TextAt(g, Analysis.FmtCount((long)Math.Round(daily.BinLower(i))), small, t.Muted,
                                new RectangleF(histogramLeft + i * step - 14, histogramBottom + 2, step + 30, 18));
                        targets.Add(new KeyValuePair<RectangleF, string>(new RectangleF(histogramLeft + i * step, histogramTop, step, histogramBottom - histogramTop),
                            Analysis.FmtCount((long)Math.Round(daily.BinLower(i))) + "—" + Analysis.FmtCount((long)Math.Round(daily.BinUpper(i))) + " 次 · " + daily.Bins[i] + " 天"));
                    }
                    using (Pen axis = new Pen(t.Line)) g.DrawLine(axis, histogramLeft, histogramBottom, histogramRight, histogramBottom);
                }
                else TextAt(g, "区间内没有有效日", small, t.Muted, new RectangleF(56, 320, 240, 20));

                if (hover >= 0 && hover < targets.Count)
                    using (Pen outline = new Pen(t.Accent, 1.4f))
                    {
                        RectangleF r = targets[hover].Key;
                        g.DrawRectangle(outline, r.X, r.Y, Math.Max(2, r.Width), r.Height);
                    }
                TextAt(g, hover >= 0 && hover < targets.Count ? targets[hover].Value : data.Footer, small, t.Muted,
                    new RectangleF(14, 404, w - 28, 20));
            }
        }
    }
}
