// ============================================================================
//  键鼠统计 - 星期节律  WeeklyReview.cs
//  按星期聚合最近 8 周(56 天)的已结束日。
//  区间聚合与分布见 RangeAnalysis.cs / RangeReport.cs。
//  只读取既有日记录,不新增采集;缺失日不补零,旧数据不反推。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeyMouseStats
{
    /// <summary>周期聚合辅助。所有窗口都以「最近一个已结束的日期」为终点。</summary>
    internal static class WeekAnalysis
    {
        /// <summary>今日尚未结束,不能与完整日直接比较;历史日期本身即完整日。</summary>
        public static DateTime CompleteEnd(DateTime date)
        {
            return RangeRules.CompleteEnd(date);
        }

        /// <summary>按时间升序返回窗口内每天,缺失日返回 null(由调用方决定是否跳过)。</summary>
        public static List<DayRecord> Window(DateTime end, int days)
        {
            List<DayRecord> list = new List<DayRecord>(days);
            for (int i = days - 1; i >= 0; i--) list.Add(Analysis.GetDay(end.AddDays(-i)));
            return list;
        }

        public static int Observed(List<DayRecord> days)
        {
            int count = 0;
            foreach (DayRecord day in days) if (day != null && !day.IsEmpty) count++;
            return count;
        }

        public static string Weekday(int index)
        {
            return RangeRules.Weekday(index);
        }

        public static int WeekdayIndex(DateTime date)
        {
            return RangeRules.WeekdayIndex(date);
        }
    }

    internal sealed class RhythmReportData
    {
        public const string Note = "按星期聚合最近 8 周(56 天)的已结束日,只统计有记录的天数,缺失日不补零。星期按周一至周日划分,不处理节假日。\n日均 = 该星期的合计 ÷ 该星期有记录的天数;样本天数很少时,结果容易被单天影响。最忙时段按该星期累计击键最多的小时。";
        public string Caption, Footer;
        public string[] Cards = new string[4], CardValues = new string[4];
        public string[] Headings = { "星期", "样本天数", "日均击键", "日均活跃", "日均点击", "最忙时段" };
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly int[] SampleDays = new int[7];
        public readonly long[] Keys = new long[7], Clicks = new long[7];
        public readonly double[] Active = new double[7];
        public readonly long[,] Hours = new long[7, 24];
        public int WindowDays = 56;
        public DateTime End;

        public static RhythmReportData Build(DateTime date)
        {
            RhythmReportData data = new RhythmReportData();
            data.End = WeekAnalysis.CompleteEnd(date);
            List<DayRecord> window = WeekAnalysis.Window(data.End, data.WindowDays);
            int observed = 0;
            foreach (DayRecord day in window)
            {
                if (day == null || day.IsEmpty) continue;
                observed++;
                int index = WeekAnalysis.WeekdayIndex(day.Date);
                data.SampleDays[index]++;
                data.Keys[index] += day.Keys;
                data.Clicks[index] += day.Clicks;
                data.Active[index] += day.ActiveSeconds;
                for (int h = 0; h < 24; h++) data.Hours[index, h] += day.HourKeys[h];
            }

            double bestKeys = double.NaN;
            int busiest = -1;
            for (int i = 0; i < 7; i++)
            {
                if (data.SampleDays[i] == 0)
                {
                    data.Rows.Add(new string[] { WeekAnalysis.Weekday(i), "0 天", "--", "--", "--", "无记录" });
                    continue;
                }
                double perDay = (double)data.Keys[i] / data.SampleDays[i];
                double activePerDay = data.Active[i] / data.SampleDays[i];
                int peakHour = 0;
                for (int h = 1; h < 24; h++) if (data.Hours[i, h] > data.Hours[i, peakHour]) peakHour = h;
                if (double.IsNaN(bestKeys) || perDay > bestKeys) { bestKeys = perDay; busiest = i; }
                data.Rows.Add(new string[] { WeekAnalysis.Weekday(i), data.SampleDays[i] + " 天", Analysis.FmtCount((long)Math.Round(perDay)),
                    ActivityMonitor.FormatDuration(activePerDay), Analysis.FmtCount((long)Math.Round((double)data.Clicks[i] / data.SampleDays[i])),
                    data.Hours[i, peakHour] > 0 ? peakHour.ToString("00") + ":00—" + (peakHour + 1).ToString("00") + ":00" : "无击键记录" });
            }

            data.Caption = data.End.AddDays(-(data.WindowDays - 1)).ToString("MM.dd") + "—" + data.End.ToString("MM.dd")
                + " 按星期聚合 · 每列一根日均击键柱与一根日均活跃柱";
            data.Cards = new string[] { "统计窗口", "有效天数", "日均击键", "最忙的星期" };
            long totalKeys = 0; double totalActive = 0;
            for (int i = 0; i < 7; i++) { totalKeys += data.Keys[i]; totalActive += data.Active[i]; }
            data.CardValues = new string[] { data.WindowDays + " 天", observed + " / " + data.WindowDays + " 天",
                observed > 0 ? Analysis.FmtCount((long)Math.Round((double)totalKeys / observed)) : "--",
                busiest >= 0 ? WeekAnalysis.Weekday(busiest) : "--" };
            data.Footer = observed == 0 ? "窗口内没有记录;先在活跃使用中积累数据。"
                : "日均 = 该星期的合计 ÷ 该星期有记录的天数,缺失日不补零。星期按周一至周日划分,不处理节假日。";
            return data;
        }
    }

    /// <summary>「星期节律」图表:每个星期一根日均击键柱与一根日均活跃柱。</summary>
    internal sealed class RhythmReportVisual : Control
    {
        internal float UiScale;
        private readonly RhythmReportData data;
        private readonly List<KeyValuePair<RectangleF, string>> targets = new List<KeyValuePair<RectangleF, string>>();
        private readonly ToolTip tip = new ToolTip { InitialDelay = 250, ReshowDelay = 100, AutoPopDelay = 20000 };
        private int hover = -1;

        public RhythmReportVisual(DateTime date)
        {
            data = RhythmReportData.Build(date);
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

                double peakKeys = 1, peakActive = 1;
                for (int i = 0; i < 7; i++)
                {
                    if (data.SampleDays[i] == 0) continue;
                    peakKeys = Math.Max(peakKeys, (double)data.Keys[i] / data.SampleDays[i]);
                    peakActive = Math.Max(peakActive, data.Active[i] / data.SampleDays[i]);
                }

                float left = 52, right = w - 24;
                float group = (right - left) / 7f;
                float barWidth = Math.Max(8, group / 2f - 10);
                TextAt(g, "日均击键 · 峰值 " + Analysis.FmtCount((long)peakKeys), small, t.Muted, new RectangleF(12, 111, 220, 18));
                TextAt(g, "日均活跃 · 峰值 " + ActivityMonitor.FormatDuration(peakActive), small, t.Muted, new RectangleF(12, 184, 220, 18));
                using (Pen keysAxis = new Pen(t.Line)) g.DrawLine(keysAxis, left, 176, right, 176);
                using (Pen activeAxis = new Pen(t.Line)) g.DrawLine(activeAxis, left, 248, right, 248);

                for (int i = 0; i < 7; i++)
                {
                    float x = left + i * group + group / 2f - barWidth / 2f;
                    double keysPerDay = data.SampleDays[i] == 0 ? 0 : (double)data.Keys[i] / data.SampleDays[i];
                    double activePerDay = data.SampleDays[i] == 0 ? 0 : data.Active[i] / data.SampleDays[i];
                    float keysHeight = (float)(keysPerDay / peakKeys * 46);
                    float activeHeight = (float)(activePerDay / peakActive * 46);
                    Fill(g, data.SampleDays[i] == 0 ? t.Raised : t.Accent, new RectangleF(x, 176 - keysHeight, barWidth, keysHeight));
                    Fill(g, data.SampleDays[i] == 0 ? t.Raised : t.Cyan, new RectangleF(x, 248 - activeHeight, barWidth, activeHeight));

                    RectangleF hit = new RectangleF(left + i * group, 118, group, 134);
                    targets.Add(new KeyValuePair<RectangleF, string>(hit,
                        WeekAnalysis.Weekday(i) + " · " + data.SampleDays[i] + " 天样本 · 日均击键 " + (data.SampleDays[i] == 0 ? "--" : Analysis.FmtCount((long)Math.Round(keysPerDay)))
                        + " · 日均活跃 " + (data.SampleDays[i] == 0 ? "--" : ActivityMonitor.FormatDuration(activePerDay))));
                    if (hover >= 0 && hover == targets.Count - 1)
                    {
                        using (Pen outline = new Pen(t.Accent, 1.4f)) g.DrawRectangle(outline, left + i * group + 2, 120, group - 4, 130);
                    }
                    TextAt(g, WeekAnalysis.Weekday(i), small, t.Muted, new RectangleF(left + i * group, 252, group, 18));
                }

                TextAt(g, hover >= 0 && hover < targets.Count ? targets[hover].Value : data.Footer, small, t.Muted,
                    new RectangleF(14, 274, w - 28, 20));
            }
        }
    }
}
