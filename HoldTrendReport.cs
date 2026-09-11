// ============================================================================
//  键鼠统计 - 按住时长趋势  HoldTrendReport.cs
//
//  1.7.6 新增:
//    · 逐日「累计按住」(逐键时长之和)与「有键按下」(至少一个键被按住的墙钟区间并集)。
//      两者都不是按压力度:同时按下多个键时,前者各算一份,后者只算一份。
//    · 占比 = 有键按下 ÷ 活跃时长。活跃时长来自活动监控,「有键按下」只会更小;
//      跨零点的那一段按开始那天计入,所以单日占比理论上可能略微超过 100%。
//    · 缺失日期不补零:只画实际观测到的日子,和报告其它页一致。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace KeyMouseStats
{
    /// <summary>按住时长趋势:逐日累计按住与有键按下。</summary>
    internal sealed class HoldTrendData
    {
        public const string Note = "累计按住 = 逐键时长之和,同时按下多个键会各算一份;有键按下 = 至少一个键被按住的墙钟时间(区间并集,不重复计时),它不会超过活跃时长。\n占比 = 有键按下 ÷ 活跃时长;活跃时长含空闲阈值以内的短暂停顿,所以正常也不会接近 100%。跨零点的那一段按开始那天计入。\n缺失日期不补零:只显示实际观测到的日子。1.7.6 起才开始采集「有键按下」,更早的日期这一项为 0。";

        public string Caption, Footer;
        public string[] Cards = new string[4], CardValues = new string[4];
        public string[] Headings = { "日期", "击键", "有效样本", "累计按住", "有键按下", "活跃时长", "占活跃" };
        public readonly List<string[]> Rows = new List<string[]>();
        /// <summary>逐日序列,顺序与 Rows 一致;用于画图。</summary>
        public readonly List<DateTime> Dates = new List<DateTime>();
        public readonly List<double> HoldMs = new List<double>();
        public readonly List<double> HoldActive = new List<double>();
        public readonly List<double> Active = new List<double>();
        public double TotalMs, TotalActive, TotalHoldActive;
        public long TotalKeys, TotalSamples;
        public int Days, Observed;

        private static string Share(double part, double whole)
        {
            return whole > 0 ? (part * 100.0 / whole).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "--";
        }

        public static HoldTrendData Build(DateTime start, DateTime end)
        {
            HoldTrendData data = new HoldTrendData();
            RangeMetrics range = RangeStats.Of(start, end, false);   // 只取完整日;今天由下面显式接上
            data.Days = range.Days;
            bool includesToday = RangeStats.IncludesRunningToday(range, start, end);
            data.Observed = range.Observed + (includesToday && !Store.Today.IsEmpty ? 1 : 0);
            data.TotalKeys = range.Keys + (includesToday ? Store.Today.Keys : 0);

            DateTime from = range.Days > 0 ? range.Start : DateTime.Today;
            DateTime to = includesToday ? DateTime.Today : range.End;
            if (range.Days > 0 || includesToday)
            {
                for (DateTime date = from; date <= to; date = date.AddDays(1))
                {
                    DayRecord day = Analysis.GetDay(date);
                    if (day == null || day.IsEmpty) continue;
                    data.Dates.Add(date);
                    data.HoldMs.Add(day.HoldTotalMs);
                    data.HoldActive.Add(day.HoldActiveSeconds);
                    data.Active.Add(day.ActiveSeconds);
                    data.TotalMs += day.HoldTotalMs;
                    data.TotalHoldActive += day.HoldActiveSeconds;
                    data.TotalActive += day.ActiveSeconds;
                    data.TotalSamples += day.HoldCount;
                    data.Rows.Add(new string[]{
                        date.ToString("MM-dd ddd", CultureInfo.InvariantCulture),
                        day.Keys.ToString("N0", CultureInfo.InvariantCulture),
                        day.HoldCount.ToString("N0", CultureInfo.InvariantCulture),
                        HoldReportData.Duration(day.HoldTotalMs),
                        HoldReportData.Duration(day.HoldActiveSeconds * 1000),
                        ActivityMonitor.FormatDuration(day.ActiveSeconds),
                        Share(day.HoldActiveSeconds, day.ActiveSeconds) });
                }
            }

            data.Caption = range.Days == 0 ? (includesToday ? "今日（进行中） · 按住时长趋势" : "所选区间没有完整日")
                : range.Start.ToString("MM.dd") + "—" + range.End.ToString("MM.dd") + " · " + range.Days + " 个完整日" + (includesToday ? " + 今日（进行中）" : "") + " · 按住时长趋势";
            data.Cards = new string[] { "累计按住", "有键按下", "占活跃时长", "日均有键按下" };
            data.CardValues = new string[]{
                HoldReportData.Duration(data.TotalMs),
                HoldReportData.Duration(data.TotalHoldActive * 1000),
                Share(data.TotalHoldActive, data.TotalActive),
                data.Observed > 0 ? HoldReportData.Duration(data.TotalHoldActive * 1000 / data.Observed) : "--" };
            data.Footer = data.Observed == 0
                ? "区间内没有任何已观测的日。按住时长从 1.7.3 起采集,「有键按下」从 1.7.6 起采集;更早的日期不会有数据。"
                : "观测 " + data.Observed + " 天 · 累计按住 " + HoldReportData.Duration(data.TotalMs)
                    + " · 有键按下 " + HoldReportData.Duration(data.TotalHoldActive * 1000)
                    + (data.TotalActive > 0 ? "（活跃时长的 " + Share(data.TotalHoldActive, data.TotalActive) + "）" : "")
                    + " · 有效样本 " + data.TotalSamples.ToString("N0", CultureInfo.InvariantCulture);
            return data;
        }
    }

    /// <summary>按住时长趋势图:逐日两根柱(累计按住 / 有键按下),悬停给出当天明细。</summary>
    internal sealed class HoldTrendVisual : Control
    {
        internal float UiScale;
        private readonly HoldTrendData data;
        private readonly List<KeyValuePair<RectangleF, string>> targets = new List<KeyValuePair<RectangleF, string>>();
        private readonly ToolTip tip = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 20000 };
        private int hover = -1;

        public HoldTrendVisual(DateTime start, DateTime end)
        {
            data = HoldTrendData.Build(start, end);
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
                for (int i = 0; i < data.Cards.Length; i++)
                {
                    float x = 10 + i * (card + 8);
                    ReportDesign.Surface(g, new RectangleF(x, 10, card, 67), t.Card, t.Line);
                    TextAt(g, data.Cards[i], small, t.Muted, new RectangleF(x + 12, 17, card - 24, 20));
                    TextAt(g, data.CardValues[i], data.CardValues[i].Length > 10 ? body : large, t.Text, new RectangleF(x + 12, 37, card - 24, 32));
                }
                TextAt(g, data.Caption, body, t.Text, new RectangleF(14, 86, w - 28, 23));

                float top = 148, bottom = 330, left = 30, right = w - 30;
                long peak = 1;
                for (int i = 0; i < data.HoldMs.Count; i++)
                {
                    peak = Math.Max(peak, (long)data.HoldMs[i]);
                    peak = Math.Max(peak, (long)(data.HoldActive[i] * 1000));
                }
                if (data.Dates.Count == 0) TextAt(g, "区间内没有可画的日期", small, t.Muted, new RectangleF(left, 220, right - left, 20));

                float step = data.Dates.Count > 0 ? (right - left) / data.Dates.Count : 0;
                for (int i = 0; i < data.Dates.Count; i++)
                {
                    float x = left + i * step;
                    bool current = hover == i;
                    float totalHeight = (float)(data.HoldMs[i] / peak) * (bottom - top);
                    float activeHeight = (float)(data.HoldActive[i] * 1000 / peak) * (bottom - top);
                    float wide = Math.Max(2, step - 6);
                    float narrow = Math.Max(1.5f, wide * .46f);
                    RectangleF track = new RectangleF(x + 3, top, step - 6, bottom - top);
                    if (current) Fill(g, t.Raised, track);
                    // 宽柱:累计按住(逐键求和);窄柱叠在前面:有键按下(墙钟并集)。
                    Fill(g, ArtTheme.Mix(t.Card, t.Cyan, .55), new RectangleF(x + 3, bottom - totalHeight, wide, totalHeight));
                    Fill(g, ArtTheme.Mix(t.Card, t.Accent, .75), new RectangleF(x + 3, bottom - activeHeight, narrow, activeHeight));
                    targets.Add(new KeyValuePair<RectangleF, string>(track,
                        data.Dates[i].ToString("yyyy.MM.dd ddd", CultureInfo.InvariantCulture)
                        + " · 累计按住 " + HoldReportData.Duration(data.HoldMs[i])
                        + " · 有键按下 " + HoldReportData.Duration(data.HoldActive[i] * 1000)
                        + " · 活跃 " + ActivityMonitor.FormatDuration(data.Active[i])));
                }
                using (Pen axis = new Pen(t.Line)) g.DrawLine(axis, left, bottom, right, bottom);
                if (data.Dates.Count > 0)
                {
                    TextAt(g, data.Dates[0].ToString("MM-dd", CultureInfo.InvariantCulture), small, t.Muted, new RectangleF(left, bottom + 4, 80, 18));
                    TextAt(g, data.Dates[data.Dates.Count - 1].ToString("MM-dd", CultureInfo.InvariantCulture), small, t.Muted,
                        new RectangleF(right - 80, bottom + 4, 80, 18));
                    TextAt(g, "峰值 " + HoldReportData.Duration(peak), small, t.Muted, new RectangleF(left, top - 18, 200, 18));
                }

                // 图例:两根柱各自的含义。
                Fill(g, ArtTheme.Mix(t.Card, t.Cyan, .55), new RectangleF(left, 352, 16, 9));
                TextAt(g, "累计按住(逐键求和,可重复计同时按下的键)", small, t.Muted, new RectangleF(left + 22, 348, 300, 18));
                Fill(g, ArtTheme.Mix(t.Card, t.Accent, .75), new RectangleF(left + 330, 352, 16, 9));
                TextAt(g, "有键按下(墙钟并集,不重复)", small, t.Muted, new RectangleF(left + 352, 348, 260, 18));

                if (hover >= 0 && hover < targets.Count)
                    using (Pen outline = new Pen(t.Accent, 1.4f))
                    {
                        RectangleF r = targets[hover].Key;
                        g.DrawRectangle(outline, r.X, r.Y, Math.Max(2, r.Width), r.Height);
                    }
                TextAt(g, hover >= 0 && hover < targets.Count ? targets[hover].Value : data.Footer, small, t.Muted,
                    new RectangleF(14, 378, w - 28, 20));
            }
        }
    }
}
