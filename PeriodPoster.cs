// ============================================================================
//  KeyMouseStats v3.0.1 · PeriodPoster.cs
//
//  周报 / 月报的 3× 高清长图。固定午夜深色,沿用日报的排版、字体和助手,
//  周报主色薄荷,月报主色紫罗兰。只画报告纸,不含左侧导航和顶栏。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace KeyMouseStats
{
    internal static class PeriodPoster
    {
        internal const int LogicalWidth = 1440;
        internal const int OutputScale = 3;

        private const int Margin = 60;
        private const int PadX = 56;
        private const int PadTop = 46;
        private const int PadBottom = 30;
        private const int ContentX = Margin + PadX;
        private const int ContentW = LogicalWidth - Margin * 2 - PadX * 2;

        private static readonly string[] WeekNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        private static readonly string[] MonthNames =
        {
            "", "一月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "十一月", "十二月"
        };

        // ---------------------------------------------------------------- 对外

        internal static Bitmap Render(PeriodReport report, bool mask)
        {
            if (report == null) report = PeriodReport.BuildWeek(DateTime.Today.AddDays(-1));

            bool previous = PrivacyMask.Enabled;
            PrivacyMask.Enabled = mask;
            try
            {
                float contentBottom;
                using (Bitmap measure = new Bitmap(LogicalWidth, 2800, PixelFormat.Format32bppArgb))
                using (Graphics mg = Graphics.FromImage(measure))
                {
                    DailyReportPoster.Prepare(mg);
                    contentBottom = PaintContent(mg, report);
                }

                int canvasHeight = (int)Math.Ceiling(contentBottom + PadBottom + Margin);
                if (canvasHeight < 900) canvasHeight = 900;
                if (canvasHeight > 2600) canvasHeight = 2600;

                Bitmap bitmap = new Bitmap(LogicalWidth * OutputScale, canvasHeight * OutputScale,
                    PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    DailyReportPoster.Prepare(g);
                    g.Clear(DailyReportPoster.CCanvas);
                    g.ScaleTransform(OutputScale, OutputScale);

                    RectangleF sheet = new RectangleF(Margin, Margin,
                        LogicalWidth - Margin * 2f, canvasHeight - Margin * 2f);
                    using (GraphicsPath path = DailyReportPoster.Rounded(sheet.X, sheet.Y, sheet.Width, sheet.Height, 30f))
                    {
                        using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface)) g.FillPath(fill, path);
                        using (Pen border = new Pen(DailyReportPoster.CLine2, 1f)) g.DrawPath(border, path);
                        g.SetClip(path);
                        PaintContent(g, report);
                        g.ResetClip();
                    }
                }
                return bitmap;
            }
            finally
            {
                PrivacyMask.Enabled = previous;
            }
        }

        internal static string Save(Bitmap bitmap, PeriodReport report)
        {
            if (!Store.CanPersist) return "";
            string dir = DailyReport.Directory;
            Directory.CreateDirectory(dir);
            string name = report.Kind == PeriodKind.Week
                ? report.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-周报.png"
                : report.End.ToString("yyyy-MM", CultureInfo.InvariantCulture) + "-月报.png";
            string path = Path.Combine(dir, name);
            try { bitmap.SetResolution(96f * OutputScale, 96f * OutputScale); }
            catch (Exception) { }
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        // ---------------------------------------------------------------- 画布

        private static Color Accent(PeriodReport report)
        {
            return report.Kind == PeriodKind.Week ? DailyReportPoster.CMint : DailyReportPoster.CViolet;
        }

        private static float PaintContent(Graphics g, PeriodReport report)
        {
            using (DailyReportPoster.Fonts f = new DailyReportPoster.Fonts())
            {
                float x = ContentX;
                float y = Margin + PadTop;
                float w = ContentW;
                Color accent = Accent(report);

                y = PaintHeader(g, f, report, x, y, w, accent);
                y = PaintMetrics(g, f, report, x, y, w, accent);
                if (report.Kind == PeriodKind.Week)
                {
                    y = PaintWeekOutline(g, f, report, x, y, w, accent);
                    y = PaintWeekTrend(g, f, report, x, y, w, accent);
                    y = PaintWeekRhythmApps(g, f, report, x, y, w, accent);
                    y = PaintWeekHighlights(g, f, report, x, y, w, accent);
                }
                else
                {
                    y = PaintMonthShape(g, f, report, x, y, w, accent);
                    y = PaintMonthTrend(g, f, report, x, y, w, accent);
                    y = PaintMonthWeeks(g, f, report, x, y, w, accent);
                    y = PaintMonthAppsExtremes(g, f, report, x, y, w, accent);
                }
                PaintFooter(g, f, report, x, y, w);
                return y + 46f;
            }
        }

        // ---------------------------------------------------------------- 页头

        private static float PaintHeader(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            DrawRight(g, "键鼠统计 · ACTIVITY MONITOR", f.Brand, DailyReportPoster.CMuted, x + w, y + 1);
            DailyReportPoster.FillDot(g, x, y + 5f, 5f, accent);
            string kicker = report.Kind == PeriodKind.Week
                ? "WEEKLY / 周报  ·  " + report.End.Year
                : "MONTHLY / 月报  ·  " + report.End.Year;
            DailyReportPoster.Draw(g, kicker, f.Eyebrow, DailyReportPoster.CMuted, x + 14f, y);
            y += 30f;

            float dateTop = y;
            string big;
            string month;
            string range;
            string status;
            string dateLabel;

            if (report.Kind == PeriodKind.Week)
            {
                big = "7";
                month = MonthNames[report.End.Month];
                range = RangeText(report.Start, report.End);
                status = report.End >= DateTime.Today ? "进行中" : "已结束";
                dateLabel = report.End.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            }
            else
            {
                big = report.Anchor.Month.ToString(CultureInfo.InvariantCulture);
                month = MonthNames[report.Anchor.Month];
                range = report.Anchor.Year + " · "
                    + (report.IsPartialMonth ? "截至 " + report.End.ToString("MM.dd", CultureInfo.InvariantCulture) : "全月");
                status = report.IsPartialMonth ? "截至 " + report.End.Month + "." + report.End.Day : "已归档";
                dateLabel = report.Anchor.ToString("yyyy.MM", CultureInfo.InvariantCulture);
            }

            DailyReportPoster.Draw(g, big, f.DateBig, accent, x, dateTop - 10f);
            float dateW = DailyReportPoster.Measure(g, big, f.DateBig);
            DailyReportPoster.Draw(g, month, f.DateMonth, DailyReportPoster.CText, x + dateW + 16f, dateTop + 6f);
            DailyReportPoster.Draw(g, range, f.DateSub, DailyReportPoster.CMuted, x + dateW + 16f, dateTop + 32f);

            float statusW = DailyReportPoster.Measure(g, status, f.Status) + 26f;
            DailyReportPoster.Pill(g, x + w - statusW, dateTop + 4f, statusW, 24f, status,
                f.Status, accent, 16, 42, accent);
            DrawRight(g, dateLabel, f.DateSub, DailyReportPoster.CMuted, x + w, dateTop + 38f);
            y = dateTop + 96f;

            bool has = report.ActiveDays > 0;
            if (!has)
            {
                DailyReportPoster.Draw(g, report.Headline, f.Headline, DailyReportPoster.CText, x, y);
                y += 64f;
            }
            else if (report.Kind == PeriodKind.Week)
            {
                DailyReportPoster.Draw(g, "这 7 天，你把最好的状态", f.Headline, DailyReportPoster.CText, x, y);
                y += 60f;
                float cx = x;
                cx += DailyReportPoster.Draw(g, "留给了 ", f.Headline, DailyReportPoster.CText, cx, y);
                cx += 6f;
                cx += DailyReportPoster.Draw(g, WeekNames[(int)report.BestDay.DayOfWeek], f.Headline, accent, cx, y);
                DailyReportPoster.Draw(g, "。", f.Headline, DailyReportPoster.CText, cx, y);
                y += 64f;
            }
            else
            {
                float cx = x;
                cx += DailyReportPoster.Draw(g, MonthNames[report.Anchor.Month] + "，键盘敲了 ",
                    f.Headline, DailyReportPoster.CText, cx, y);
                cx += 12f;
                cx += DailyReportPoster.DrawCount(g, f, Analysis.FmtCount(report.Keys), cx, y, accent);
                cx += 6f;
                DailyReportPoster.Draw(g, "次，", f.Headline, DailyReportPoster.CText, cx, y);
                y += 60f;
                cx = x;
                cx += DailyReportPoster.Draw(g, "最长连续 ", f.Headline, DailyReportPoster.CText, cx, y);
                cx += 12f;
                cx += DailyReportPoster.Draw(g, report.LongestStreak + " 天", f.Headline, accent, cx, y);
                DailyReportPoster.Draw(g, "。", f.Headline, DailyReportPoster.CText, cx, y);
                y += 64f;
            }

            string lede = report.Lede;
            if (PrivacyMask.Enabled && report.Apps.Count > 0)
            {
                for (int i = 0; i < report.Apps.Count; i++)
                {
                    string name = report.Apps[i].Name;
                    if (!string.IsNullOrEmpty(name)) lede = lede.Replace(name, "某个应用");
                }
            }
            List<string> lines = DailyReportPoster.Wrap(g, f.Lede, lede, Math.Min(w, 980f));
            for (int i = 0; i < lines.Count && i < 2; i++)
                DailyReportPoster.Draw(g, lines[i], f.Lede, DailyReportPoster.CMuted, x, y + i * 23f);
            y += Math.Min(2, Math.Max(1, lines.Count)) * 23f + 24f;

            DailyReportPoster.HLine(g, x, y, w, DailyReportPoster.CLine);
            return y + 24f;
        }

        // ---------------------------------------------------------------- 指标

        private static float PaintMetrics(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            const float h = 122f;
            float col = w / 4f;
            double avg = report.DayCount > 0 ? report.ActiveSeconds / report.DayCount : 0;
            double prevAvg = report.PrevDayCount > 0 ? report.PrevActiveSeconds / report.PrevDayCount : 0;

            DrawMetric(g, f, report, x, y, col, 0, "总击键", "KEYS",
                Analysis.FmtCount(report.Keys), report.Keys, report.PrevKeys, false, accent);
            DrawMetric(g, f, report, x, y, col, 1, "总点击", "CLICKS",
                Analysis.FmtCount(report.Clicks), report.Clicks, report.PrevClicks, false, accent);
            DrawMetric(g, f, report, x, y, col, 2, "有效使用", "FOCUS",
                ActivityMonitor.FormatDuration(report.ActiveSeconds), report.ActiveSeconds, report.PrevActiveSeconds, true, accent);
            DrawMetric(g, f, report, x, y, col, 3, "日均专注", "AVG / DAY",
                ActivityMonitor.FormatDuration(avg), avg, prevAvg, true, accent);

            for (int i = 1; i < 4; i++)
                DailyReportPoster.VLine(g, x + col * i, y + 12f, y + h - 14f, DailyReportPoster.CLine);
            DailyReportPoster.HLine(g, x, y + h, w, DailyReportPoster.CLine);
            return y + h + 24f;
        }

        private static void DrawMetric(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float col, int index, string label, string en, string value,
            double current, double baseValue, bool mixed, Color accent)
        {
            float px = x + col * index + (index == 0 ? 0f : 26f);
            float avail = col - (index == 0 ? 0f : 26f) - 12f;

            DailyReportPoster.Draw(g, label, f.MetricLabel, DailyReportPoster.CMuted, px, y + 14f);
            DrawRight(g, en, f.MetricLabelEn, Color.FromArgb(210, DailyReportPoster.CMuted),
                x + col * (index + 1) - 12f, y + 16f);

            if (mixed)
                DailyReportPoster.Draw(g, value, f.MetricValueCjk, DailyReportPoster.CText, px, y + 38f);
            else
                DrawCountValue(g, f, value, px, y + 34f, avail);

            string delta = DailyReportPoster.DeltaText(current, baseValue);
            if (delta.Length > 0)
            {
                Color color = delta.StartsWith("▲", StringComparison.Ordinal) ? DailyReportPoster.CMint
                    : delta.StartsWith("▼", StringComparison.Ordinal) ? DailyReportPoster.CBad
                    : DailyReportPoster.CMuted;
                DailyReportPoster.Draw(g, delta, f.Delta, color, px, y + 87f);
                float dw = DailyReportPoster.Measure(g, delta, f.Delta);
                DailyReportPoster.Draw(g, report.ComparisonLabel, f.Micro, DailyReportPoster.CMuted,
                    px + dw + 8f, y + 89f);
            }
            else
            {
                DailyReportPoster.Draw(g, "暂无对比", f.Micro, DailyReportPoster.CMuted, px, y + 89f);
            }
        }

        private static void DrawCountValue(Graphics g, DailyReportPoster.Fonts f, string text,
            float x, float y, float avail)
        {
            if (string.IsNullOrEmpty(text)) return;
            string number = text;
            string unit = "";
            int cut = text.IndexOf('万');
            if (cut < 0) cut = text.IndexOf('亿');
            if (cut > 0) { number = text.Substring(0, cut); unit = text.Substring(cut); }

            Font font = DailyReportPoster.Measure(g, number, f.MetricValue) > avail - 20f
                ? f.MetricValueCjk : f.MetricValue;
            float nx = x + DailyReportPoster.Draw(g, number, font, DailyReportPoster.CText, x, y);
            if (unit.Length > 0)
                DailyReportPoster.Draw(g, unit, f.MetricUnit, DailyReportPoster.CMuted, nx + 4f, y + 20f);
        }

        private static void DrawRight(Graphics g, string text, Font font, Color color, float right, float y)
        {
            DailyReportPoster.DrawRight(g, text, font, color, right, y);
        }

        // ---------------------------------------------------------------- 周报:轮廓

        private static float PaintWeekOutline(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            DailyReportPoster.Draw(g, "一周的轮廓", f.SectionTitle, DailyReportPoster.CText, x, top);
            DailyReportPoster.Draw(g, "每天的有效使用时长 · 7 天", f.SectionSub, DailyReportPoster.CMuted, x, top + 21f);

            float asideW = 300f;
            float gap = 36f;
            float leftW = w - asideW - gap;
            float plotTop = top + 54f;
            float plotH = 180f;
            double max = 1;
            for (int i = 0; i < report.Days.Count; i++)
                if (report.Days[i].ActiveSeconds > max) max = report.Days[i].ActiveSeconds;

            float slot = leftW / 7f;
            for (int i = 0; i < report.Days.Count && i < 7; i++)
            {
                PeriodDay day = report.Days[i];
                float barW = slot * 0.48f;
                float bx = x + slot * i + (slot - barW) / 2f;
                float bh = (float)(day.ActiveSeconds / max) * plotH;
                if (bh < 3f && day.HasData) bh = 3f;
                float by = plotTop + plotH - bh;

                if (day.HasData)
                {
                    using (GraphicsPath path = DailyReportPoster.Rounded(bx, by, barW, bh, 7f))
                    {
                        if (day.Date == report.BestDay)
                        {
                            using (SolidBrush solid = new SolidBrush(accent)) g.FillPath(solid, path);
                        }
                        else
                        {
                            RectangleF rect = new RectangleF(bx, by, barW, Math.Max(3f, bh));
                            using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                                Color.FromArgb(210, accent), Color.FromArgb(28, accent), LinearGradientMode.Vertical))
                                g.FillPath(brush, path);
                        }
                    }
                }

                string value = day.HasData ? TimeText(day.ActiveSeconds) : "—";
                DailyReportPoster.DrawCentered(g, value, f.AppTime,
                    day.Date == report.BestDay ? accent : DailyReportPoster.CText2, bx + barW / 2f, by - 18f);
                DailyReportPoster.DrawCentered(g, WeekNames[(int)day.Date.DayOfWeek], f.Chart,
                    DailyReportPoster.CMuted, bx + barW / 2f, plotTop + plotH + 9f);
                DailyReportPoster.DrawCentered(g, day.Date.ToString("MM.dd", CultureInfo.InvariantCulture), f.ChartSmall,
                    DailyReportPoster.CMuted, bx + barW / 2f, plotTop + plotH + 24f);
            }
            DailyReportPoster.HLine(g, x, plotTop + plotH, leftW, DailyReportPoster.CLine);

            float ax = x + leftW + gap;
            float ay = top + 44f;
            using (GraphicsPath box = DailyReportPoster.Rounded(ax, ay, asideW, 254f, 20f))
            {
                using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface2)) g.FillPath(fill, box);
                using (Pen border = new Pen(DailyReportPoster.CLine, 1f)) g.DrawPath(border, box);
            }
            DailyReportPoster.Draw(g, report.ActiveDays + "/7", f.HeadlineNum, accent, ax + 22f, ay + 18f);
            DrawRight(g, "天有记录", f.Micro, DailyReportPoster.CMuted, ax + asideW - 22f, ay + 40f);

            float listY = ay + 92f;
            DailyReportPoster.HLine(g, ax + 22f, listY, asideW - 44f, DailyReportPoster.CLine);
            DrawAside(g, f, "最高单日", report.BestActiveSeconds > 0 ? TimeText(report.BestActiveSeconds) : "—",
                report.BestDay.Month + "." + report.BestDay.Day + " " + WeekNames[(int)report.BestDay.DayOfWeek],
                ax + 22f, listY + 12f, asideW - 44f);

            PeriodDay lowest = null;
            for (int i = 0; i < report.Days.Count; i++)
            {
                if (!report.Days[i].HasData) continue;
                if (lowest == null || report.Days[i].ActiveSeconds < lowest.ActiveSeconds) lowest = report.Days[i];
            }
            listY += 54f;
            DailyReportPoster.HLine(g, ax + 22f, listY, asideW - 44f, DailyReportPoster.CLine);
            DrawAside(g, f, "最低单日", lowest != null ? TimeText(lowest.ActiveSeconds) : "—",
                lowest != null ? lowest.Date.Month + "." + lowest.Date.Day + " " + WeekNames[(int)lowest.Date.DayOfWeek] : "—",
                ax + 22f, listY + 12f, asideW - 44f);

            listY += 54f;
            DailyReportPoster.HLine(g, ax + 22f, listY, asideW - 44f, DailyReportPoster.CLine);
            string weekend = WeekendText(report);
            DrawAside(g, f, "周末变化", weekend, "对比工作日", ax + 22f, listY + 12f, asideW - 44f);

            float bottom = Math.Max(plotTop + plotH + 34f, ay + 266f);
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        private static void DrawAside(Graphics g, DailyReportPoster.Fonts f, string label, string value,
            string note, float x, float y, float w)
        {
            DailyReportPoster.Draw(g, label, f.Micro, DailyReportPoster.CMuted, x, y);
            DrawRight(g, value, f.StatValue, DailyReportPoster.CText, x + w, y - 4f);
            DailyReportPoster.Draw(g, note, f.Micro, DailyReportPoster.CMuted, x, y + 19f);
        }

        private static string WeekendText(PeriodReport report)
        {
            if (report.WorkdayAvgSeconds <= 0 || report.WeekendAvgSeconds <= 0) return "—";
            double delta = report.WeekendAvgSeconds / report.WorkdayAvgSeconds - 1;
            return (delta >= 0 ? "+" : "−")
                + Math.Abs(delta * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        // ---------------------------------------------------------------- 周报:轨迹

        private static float PaintWeekTrend(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            DailyReportPoster.Draw(g, "一周轨迹", f.SectionTitle, DailyReportPoster.CText, x, top);
            DailyReportPoster.Draw(g, "每日击键次数 · 与上一周对照", f.SectionSub, DailyReportPoster.CMuted, x, top + 21f);
            DrawLegend(g, f, x + w - 150f, top + 4f, accent, "本周", "上一周");

            float plotL = x + 44f;
            float plotR = x + w;
            float plotT = top + 48f;
            float plotB = plotT + 220f;
            float plotW = plotR - plotL;
            long max = 1;
            for (int i = 0; i < report.Days.Count; i++) if (report.Days[i].Keys > max) max = report.Days[i].Keys;
            for (int i = 0; i < report.PreviousDays.Count; i++) if (report.PreviousDays[i].Keys > max) max = report.PreviousDays[i].Keys;
            long step = NiceStep(max);
            long scale = step;
            while (scale < max) scale += step;

            for (long v = 0; v <= scale; v += step)
            {
                float gy = plotB - (float)v / scale * (plotB - plotT);
                DailyReportPoster.HLine(g, plotL, gy, plotW, DailyReportPoster.CLine);
                DrawRight(g, v == 0 ? "0" : v.ToString("N0", CultureInfo.InvariantCulture),
                    f.Chart, DailyReportPoster.CMuted, plotL - 9f, gy - 5f);
            }

            int n = report.Days.Count;
            float stepX = n > 1 ? plotW / (n - 1) : plotW;
            PointF[] prev = new PointF[report.PreviousDays.Count];
            for (int i = 0; i < report.PreviousDays.Count && i < n; i++)
                prev[i] = new PointF(plotL + stepX * i,
                    plotB - (float)report.PreviousDays[i].Keys / scale * (plotB - plotT));
            using (Pen pen = new Pen(Color.FromArgb(150, DailyReportPoster.CMuted), 1.4f))
            {
                pen.DashStyle = DashStyle.Dash;
                if (prev.Length > 1) g.DrawLines(pen, prev);
            }

            PointF[] points = new PointF[n];
            for (int i = 0; i < n; i++)
                points[i] = new PointF(plotL + stepX * i,
                    plotB - (float)report.Days[i].Keys / scale * (plotB - plotT));
            if (n > 1)
            {
                using (GraphicsPath area = new GraphicsPath())
                {
                    area.AddLines(points);
                    area.AddLine(points[n - 1].X, plotB, points[0].X, plotB);
                    area.CloseFigure();
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new RectangleF(plotL, plotT, plotW, plotB - plotT),
                        Color.FromArgb(55, accent), Color.FromArgb(0, accent), LinearGradientMode.Vertical))
                        g.FillPath(brush, area);
                }
                using (Pen pen = new Pen(accent, 2.2f)) g.DrawLines(pen, points);
            }
            for (int i = 0; i < n; i++)
            {
                using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface)) g.FillEllipse(fill, points[i].X - 4f, points[i].Y - 4f, 8f, 8f);
                using (Pen pen = new Pen(accent, 2f)) g.DrawEllipse(pen, points[i].X - 4f, points[i].Y - 4f, 8f, 8f);
                DailyReportPoster.DrawCentered(g, WeekNames[(int)report.Days[i].Date.DayOfWeek],
                    f.Chart, DailyReportPoster.CMuted, points[i].X, plotB + 10f);
                DailyReportPoster.DrawCentered(g, report.Days[i].Date.ToString("MM.dd", CultureInfo.InvariantCulture),
                    f.ChartSmall, DailyReportPoster.CMuted, points[i].X, plotB + 25f);
            }
            DailyReportPoster.HLine(g, plotL, plotB, plotW, DailyReportPoster.CLine2);

            int bestIndex = 0;
            for (int i = 1; i < n; i++) if (report.Days[i].Keys > report.Days[bestIndex].Keys) bestIndex = i;
            if (n > 0 && report.Days[bestIndex].Keys > 0)
                DrawPeak(g, f, points[bestIndex], Analysis.FmtCount(report.Days[bestIndex].Keys) + " 次",
                    plotL, plotR, plotT, plotB);

            float bottom = plotB + 44f;
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        // ---------------------------------------------------------------- 周报:节奏 + 应用

        private static float PaintWeekRhythmApps(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            float gap = 46f;
            float leftW = (w - gap) * 0.46f;
            float rightW = w - leftW - gap;
            float rightX = x + leftW + gap;

            DailyReportPoster.Draw(g, "节奏分布", f.SectionTitle, DailyReportPoster.CText, x, top);
            DailyReportPoster.Draw(g, "上午 / 下午 / 晚上", f.SectionSub, DailyReportPoster.CMuted, x, top + 21f);
            DrawRight(g, "01", f.Ghost, DailyReportPoster.CLine2, x + leftW, top - 8f);

            float barY = top + 58f;
            DrawRhythm(g, f, x, barY, leftW, "上午", report.MorningShare, accent);
            DrawRhythm(g, f, x, barY + 40f, leftW, "下午", report.AfternoonShare, accent);
            DrawRhythm(g, f, x, barY + 80f, leftW, "晚上", report.EveningShare, accent);

            float splitY = barY + 122f;
            DailyReportPoster.HLine(g, x, splitY, leftW, DailyReportPoster.CLine);
            DailyReportPoster.Draw(g, "工作日", f.Micro, DailyReportPoster.CMuted, x, splitY + 14f);
            DailyReportPoster.Draw(g, TimeText(report.WorkdayAvgSeconds), f.StatValue, DailyReportPoster.CText, x, splitY + 34f);
            DailyReportPoster.Draw(g, "周末", f.Micro, DailyReportPoster.CMuted, x + leftW / 2f, splitY + 14f);
            DailyReportPoster.Draw(g, TimeText(report.WeekendAvgSeconds), f.StatValue, DailyReportPoster.CText, x + leftW / 2f, splitY + 34f);

            DailyReportPoster.Draw(g, "应用版图", f.SectionTitle, DailyReportPoster.CText, rightX, top);
            DailyReportPoster.Draw(g, "TOP 5 · 按活跃时长", f.SectionSub, DailyReportPoster.CMuted, rightX, top + 21f);
            DrawRight(g, "02", f.Ghost, DailyReportPoster.CLine2, x + w, top - 8f);
            PaintApps(g, f, report, rightX, top + 52f, rightW, accent);

            float bottom = Math.Max(splitY + 66f, top + 52f + Math.Max(1, report.Apps.Count) * 38f);
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        private static void DrawRhythm(Graphics g, DailyReportPoster.Fonts f, float x, float y, float w,
            string label, double share, Color accent)
        {
            if (share < 0) share = 0;
            if (share > 1) share = 1;
            DailyReportPoster.Draw(g, label, f.HabitKey, DailyReportPoster.CText2, x, y);
            float trackX = x + 44f;
            float trackW = w - 44f - 44f;
            using (GraphicsPath track = DailyReportPoster.Rounded(trackX, y + 8f, trackW, 6f, 3f))
            using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface3)) g.FillPath(fill, track);
            using (GraphicsPath fill = DailyReportPoster.Rounded(trackX, y + 8f, Math.Max(2f, trackW * (float)share), 6f, 3f))
            using (SolidBrush brush = new SolidBrush(accent)) g.FillPath(brush, fill);
            DrawRight(g, (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%",
                f.HabitCount, DailyReportPoster.CText, x + w, y + 2f);
        }

        private static void PaintApps(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            if (report.Apps.Count == 0)
            {
                DailyReportPoster.Draw(g, "没有应用归因数据", f.Note, DailyReportPoster.CMuted, x, y + 16f);
                return;
            }
            double max = 1;
            for (int i = 0; i < report.Apps.Count; i++) if (report.Apps[i].Seconds > max) max = report.Apps[i].Seconds;
            float rowH = 38f;
            for (int i = 0; i < report.Apps.Count; i++)
            {
                PeriodApp app = report.Apps[i];
                float ry = y + i * rowH;
                string name = PrivacyMask.Enabled ? "应用 " + (i + 1) + " · 已隐藏" : app.Name;
                DailyReportPoster.Draw(g, (i + 1).ToString("00", CultureInfo.InvariantCulture), f.AppRank,
                    DailyReportPoster.CMuted, x, ry + 9f);
                DailyReportPoster.DrawAppMark(g, f, name, x + 24f, ry + 5f, i);
                DailyReportPoster.Draw(g, DailyReportPoster.Fit(g, f.AppName, name, 150f),
                    f.AppName, i == 0 ? DailyReportPoster.CText : DailyReportPoster.CText2, x + 62f, ry + 9f);

                float tx = x + 224f;
                float timeW = 48f;
                float pctW = 38f;
                float trackW = x + w - tx - timeW - pctW - 24f;
                DailyReportPoster.DrawTrack(g, tx, ry + rowH / 2f - 2f, trackW, i,
                    (float)(app.Seconds / max), accent);
                DrawRight(g, DailyReportPoster.ShortDuration(app.Seconds), f.AppTime,
                    DailyReportPoster.CText2, x + w - pctW - 10f, ry + 9f);
                int pct = (int)Math.Round(app.Seconds / Math.Max(1.0, report.ActiveSeconds) * 100.0);
                DrawRight(g, pct + "%", f.AppPct, DailyReportPoster.CMuted, x + w, ry + 10f);

                if (i < report.Apps.Count - 1)
                {
                    using (Pen pen = new Pen(DailyReportPoster.CLine, 1f))
                    {
                        pen.DashStyle = DashStyle.Dot;
                        g.DrawLine(pen, x + 62f, ry + rowH - 1f, x + w, ry + rowH - 1f);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 周报:三个关键词

        private static float PaintWeekHighlights(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            float gap = 34f;
            float colW = (w - gap * 2f) / 3f;
            string weekend = WeekendText(report);
            PaintHighlight(g, f, x, top, colW, accent, "连续记录", report.LongestStreak + " 天",
                "这是本期最长的一次连续记录。");
            PaintHighlight(g, f, x + colW + gap, top, colW, accent, "单日峰值",
                Analysis.FmtCount(report.BestKeys) + " 次",
                report.BestDay.Month + "." + report.BestDay.Day + " " + WeekNames[(int)report.BestDay.DayOfWeek] + "。");
            PaintHighlight(g, f, x + (colW + gap) * 2f, top, colW, accent, "周末变化", weekend,
                weekend == "—" ? "没有足够的工作日/周末数据。" : "周末日均对比工作日。");
            float bottom = top + 116f;
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        private static void PaintHighlight(Graphics g, DailyReportPoster.Fonts f, float x, float y, float w,
            Color accent, string kicker, string value, string note)
        {
            using (Pen pen = new Pen(accent, 2f)) g.DrawLine(pen, x, y, x, y + 86f);
            DailyReportPoster.Draw(g, kicker, f.Micro, DailyReportPoster.CMuted, x + 16f, y);
            DailyReportPoster.Draw(g, value, f.MetricValueCjk, DailyReportPoster.CText, x + 16f, y + 22f);
            List<string> lines = DailyReportPoster.Wrap(g, f.Micro, note, w - 20f);
            for (int i = 0; i < lines.Count && i < 2; i++)
                DailyReportPoster.Draw(g, lines[i], f.Micro, DailyReportPoster.CMuted, x + 16f, y + 58f + i * 16f);
        }

        // ---------------------------------------------------------------- 月报:日历

        private static float PaintMonthShape(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            DailyReportPoster.Draw(g, "这个月的形状", f.SectionTitle, DailyReportPoster.CText, x, top);
            DailyReportPoster.Draw(g, "每一天的有效使用时长", f.SectionSub, DailyReportPoster.CMuted, x, top + 21f);
            DrawCalendarLegend(g, f, x + w - 160f, top + 4f, accent);

            float asideW = 300f;
            float gap = 36f;
            float leftW = w - asideW - gap;
            float gridTop = top + 50f;
            int daysInMonth = DateTime.DaysInMonth(report.Anchor.Year, report.Anchor.Month);
            DateTime first = new DateTime(report.Anchor.Year, report.Anchor.Month, 1);
            int lead = ((int)first.DayOfWeek + 6) % 7;
            int rows = (lead + daysInMonth + 6) / 7;
            float cellGap = 6f;
            float cellW = (leftW - cellGap * 6f) / 7f;
            float cellH = cellW * 0.62f;

            for (int i = 0; i < 7; i++)
                DailyReportPoster.DrawCentered(g, WeekNames[(i + 1) % 7].Substring(1), f.ChartSmall,
                    DailyReportPoster.CMuted, x + cellW / 2f + i * (cellW + cellGap), gridTop);

            for (int day = 1; day <= daysInMonth; day++)
            {
                int slot = lead + day - 1;
                int row = slot / 7;
                int col = slot % 7;
                float cx = x + col * (cellW + cellGap);
                float cy = gridTop + 18f + row * (cellH + cellGap);
                PeriodDay data = FindDay(report, day);
                bool future = data == null || data.Date > report.End;
                using (GraphicsPath cell = DailyReportPoster.Rounded(cx, cy, cellW, cellH, 7f))
                {
                    using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface2)) g.FillPath(fill, cell);
                    if (!future && data != null && data.HasData && data.ActiveSeconds > 0)
                    {
                        int alpha = data.ActiveSeconds < 3 * 3600 ? 36
                            : data.ActiveSeconds < 6 * 3600 ? 72
                            : data.ActiveSeconds < 8 * 3600 ? 118 : 184;
                        using (SolidBrush overlay = new SolidBrush(Color.FromArgb(alpha, accent))) g.FillPath(overlay, cell);
                    }
                    if (!future && data != null && data.Date == report.BestDay)
                    {
                        using (Pen border = new Pen(accent, 1.5f)) g.DrawPath(border, cell);
                        DailyReportPoster.FillDot(g, cx + cellW - 12f, cy + cellH - 12f, 4f, accent);
                    }
                }
                DailyReportPoster.Draw(g, day.ToString(CultureInfo.InvariantCulture), f.ChartSmall,
                    future ? Color.FromArgb(65, DailyReportPoster.CMuted) : DailyReportPoster.CText2, cx + 8f, cy + 7f);
            }

            float ax = x + leftW + gap;
            float ay = top + 44f;
            using (GraphicsPath box = DailyReportPoster.Rounded(ax, ay, asideW, 232f, 20f))
            {
                using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface2)) g.FillPath(fill, box);
                using (Pen border = new Pen(DailyReportPoster.CLine, 1f)) g.DrawPath(border, box);
            }
            DailyReportPoster.Draw(g, report.ActiveDays + "/" + daysInMonth, f.HeadlineNum, accent, ax + 22f, ay + 18f);
            DrawRight(g, "天有记录", f.Micro, DailyReportPoster.CMuted, ax + asideW - 22f, ay + 40f);

            float listY = ay + 92f;
            DailyReportPoster.HLine(g, ax + 22f, listY, asideW - 44f, DailyReportPoster.CLine);
            DrawAside(g, f, "最长连续", report.LongestStreak + " 天",
                report.LongestStreak > 0
                    ? report.LongestStreakStart.Month + "." + report.LongestStreakStart.Day + " 起" : "—",
                ax + 22f, listY + 12f, asideW - 44f);
            listY += 46f;
            DailyReportPoster.HLine(g, ax + 22f, listY, asideW - 44f, DailyReportPoster.CLine);
            DrawAside(g, f, "最高单日", report.BestActiveSeconds > 0 ? TimeText(report.BestActiveSeconds) : "—",
                report.BestDay.Month + "." + report.BestDay.Day + " " + WeekNames[(int)report.BestDay.DayOfWeek],
                ax + 22f, listY + 12f, asideW - 44f);
            listY += 46f;
            DailyReportPoster.HLine(g, ax + 22f, listY, asideW - 44f, DailyReportPoster.CLine);
            DrawAside(g, f, "最长会话", report.LongestSessionSeconds > 0 ? TimeText(report.LongestSessionSeconds) : "—",
                "一次坐下没有离开", ax + 22f, listY + 12f, asideW - 44f);

            float bottom = Math.Max(gridTop + 18f + rows * (cellH + cellGap) + 14f, ay + 244f);
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        private static PeriodDay FindDay(PeriodReport report, int day)
        {
            if (day < 1 || day > report.Days.Count) return null;
            return report.Days[day - 1];
        }

        private static void DrawCalendarLegend(Graphics g, DailyReportPoster.Fonts f, float x, float y, Color accent)
        {
            DailyReportPoster.Draw(g, "少", f.ChartSmall, DailyReportPoster.CMuted, x, y + 4f);
            for (int i = 0; i < 4; i++)
            {
                int alpha = 36 + i * 48;
                using (GraphicsPath r = DailyReportPoster.Rounded(x + 18f + i * 14f, y + 2f, 10f, 10f, 3f))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, accent))) g.FillPath(b, r);
            }
            DailyReportPoster.Draw(g, "多", f.ChartSmall, DailyReportPoster.CMuted, x + 18f + 4 * 14f + 4f, y + 4f);
        }

        // ---------------------------------------------------------------- 月报:轨迹

        private static float PaintMonthTrend(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            DailyReportPoster.Draw(g, "30 天轨迹", f.SectionTitle, DailyReportPoster.CText, x, top);
            DailyReportPoster.Draw(g, "每日活跃时长 · 7 日均线", f.SectionSub, DailyReportPoster.CMuted, x, top + 21f);
            DrawLegend(g, f, x + w - 160f, top + 4f, accent, "每日", "7 日均线");

            float plotL = x + 44f;
            float plotR = x + w;
            float plotT = top + 48f;
            float plotB = plotT + 220f;
            float plotW = plotR - plotL;
            double max = 12;
            double highest = 0;
            for (int i = 0; i < report.Days.Count; i++)
                if (report.Days[i].ActiveSeconds / 3600.0 > highest) highest = report.Days[i].ActiveSeconds / 3600.0;
            while (max < highest) max += 3;

            for (double v = 0; v <= max + 0.01; v += 3)
            {
                float gy = plotB - (float)(v / max) * (plotB - plotT);
                DailyReportPoster.HLine(g, plotL, gy, plotW, DailyReportPoster.CLine);
                DrawRight(g, v == 0 ? "0" : v.ToString("0") + "h", f.Chart,
                    DailyReportPoster.CMuted, plotL - 9f, gy - 5f);
            }

            int n = report.Days.Count;
            float stepX = n > 1 ? plotW / (n - 1) : plotW;
            PointF[] points = new PointF[n];
            for (int i = 0; i < n; i++)
                points[i] = new PointF(plotL + stepX * i,
                    plotB - (float)(report.Days[i].ActiveSeconds / 3600.0 / max) * (plotB - plotT));
            if (n > 1)
            {
                using (GraphicsPath area = new GraphicsPath())
                {
                    area.AddLines(points);
                    area.AddLine(points[n - 1].X, plotB, points[0].X, plotB);
                    area.CloseFigure();
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new RectangleF(plotL, plotT, plotW, plotB - plotT),
                        Color.FromArgb(45, accent), Color.FromArgb(0, accent), LinearGradientMode.Vertical))
                        g.FillPath(brush, area);
                }
                using (Pen pen = new Pen(accent, 2.2f)) g.DrawLines(pen, points);
            }

            // 7 日均线
            List<PointF> avg = new List<PointF>();
            for (int i = 0; i < n; i++)
            {
                int start = Math.Max(0, i - 6);
                double sum = 0;
                int count = 0;
                for (int k = start; k <= i; k++)
                {
                    if (!report.Days[k].HasData) continue;
                    sum += report.Days[k].ActiveSeconds / 3600.0;
                    count++;
                }
                if (count == 0) continue;
                avg.Add(new PointF(plotL + stepX * i, plotB - (float)(sum / count / max) * (plotB - plotT)));
            }
            if (avg.Count > 1)
            {
                using (Pen pen = new Pen(Color.FromArgb(160, DailyReportPoster.CMuted), 1.4f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawLines(pen, avg.ToArray());
                }
            }

            int last = n;
            int[] marks = { 1, 5, 10, 15, 20, 25, last };
            for (int i = 0; i < marks.Length; i++)
            {
                int d = marks[i];
                if (d < 1 || d > n) continue;
                DailyReportPoster.DrawCentered(g, d.ToString(CultureInfo.InvariantCulture), f.Chart,
                    DailyReportPoster.CMuted, plotL + stepX * (d - 1), plotB + 10f);
            }

            // 自然周分隔:第一周只画标签,不画左边框。
            for (int i = 0; i < report.Weeks.Count; i++)
            {
                PeriodWeek week = report.Weeks[i];
                int index = (week.Start - report.Start).Days;
                if (index < 0 || index >= n) continue;
                float wx = plotL + stepX * index;
                if (index > 0)
                {
                    using (Pen pen = new Pen(DailyReportPoster.CLine, 1f))
                    {
                        pen.DashStyle = DashStyle.Dash;
                        g.DrawLine(pen, wx, plotT, wx, plotB);
                    }
                }
                DailyReportPoster.DrawCentered(g, week.Label, f.ChartSmall, DailyReportPoster.CMuted,
                    wx + stepX * Math.Max(0, week.Days.Count - 1) / 2f, plotT - 12f);
            }

            DailyReportPoster.HLine(g, plotL, plotB, plotW, DailyReportPoster.CLine2);
            if (report.ActiveDays > 0 && report.BestActiveSeconds > 0
                && report.BestDay >= report.Start && report.BestDay < report.Start.AddDays(n))
            {
                int index = (report.BestDay - report.Start).Days;
                DrawPeak(g, f, points[index], TimeText(report.BestActiveSeconds), plotL, plotR, plotT, plotB);
            }

            float bottom = plotB + 42f;
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        // ---------------------------------------------------------------- 月报:周卡片

        private static float PaintMonthWeeks(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            DailyReportPoster.Draw(g, "周度", f.SectionTitle, DailyReportPoster.CText, x, top);
            DailyReportPoster.Draw(g, "按自然周拆分", f.SectionSub, DailyReportPoster.CMuted, x, top + 21f);
            DrawRight(g, "01", f.Ghost, DailyReportPoster.CLine2, x + w, top - 8f);

            int count = report.Weeks.Count;
            if (count == 0) return top + 30f;
            float gap = 12f;
            float cardW = (w - gap * (count - 1)) / count;
            float cardH = 116f;
            float cardTop = top + 48f;
            double dayMax = 1;
            for (int i = 0; i < report.Days.Count; i++)
                if (report.Days[i].ActiveSeconds > dayMax) dayMax = report.Days[i].ActiveSeconds;

            for (int i = 0; i < count; i++)
            {
                PeriodWeek week = report.Weeks[i];
                float cx = x + i * (cardW + gap);
                using (GraphicsPath card = DailyReportPoster.Rounded(cx, cardTop, cardW, cardH, 12f))
                {
                    using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface2)) g.FillPath(fill, card);
                    using (Pen border = new Pen(DailyReportPoster.CLine, 1f)) g.DrawPath(border, card);
                }
                string weekLabel = week.Label + " · " + ShortDate(week.Start) + " — " + ShortDate(week.End);
                DailyReportPoster.Draw(g, DailyReportPoster.Fit(g, f.ChartSmall, weekLabel, cardW - 72f),
                    f.ChartSmall, DailyReportPoster.CMuted, cx + 12f, cardTop + 12f);
                Font totalFont = count >= 6 ? f.AppTime : f.StatValue;
                DrawRight(g, TimeText(week.ActiveSeconds), totalFont, DailyReportPoster.CText,
                    cx + cardW - 12f, cardTop + 10f);

                float barY = cardTop + 62f;
                float barMax = cardW - 24f;
                float slot = week.Days.Count > 0 ? barMax / week.Days.Count : barMax;
                for (int k = 0; k < week.Days.Count; k++)
                {
                    float bh = (float)(week.Days[k].ActiveSeconds / dayMax) * 30f;
                    if (bh < 2f) bh = 2f;
                    float bx = cx + 12f + k * slot;
                    using (GraphicsPath bar = DailyReportPoster.Rounded(bx, barY + 30f - bh, Math.Max(2f, slot * 0.56f), bh, 2f))
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, accent))) g.FillPath(brush, bar);
                }
                double avg = week.Days.Count > 0 ? week.ActiveSeconds / 3600.0 / week.Days.Count : 0;
                DailyReportPoster.Draw(g, week.Days.Count + " 天 · 日均 " + avg.ToString("0.0", CultureInfo.InvariantCulture) + "h",
                    f.ChartSmall, DailyReportPoster.CMuted, cx + 12f, cardTop + cardH - 18f);
            }

            float bottom = cardTop + cardH + 16f;
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        // ---------------------------------------------------------------- 月报:应用 + 月度之最

        private static float PaintMonthAppsExtremes(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w, Color accent)
        {
            float top = y + 30f;
            float gap = 46f;
            float leftW = (w - gap) * 0.58f;
            float rightW = w - leftW - gap;
            float rightX = x + leftW + gap;

            DailyReportPoster.Draw(g, "应用版图", f.SectionTitle, DailyReportPoster.CText, x, top);
            DailyReportPoster.Draw(g, "TOP 5 · 按活跃时长", f.SectionSub, DailyReportPoster.CMuted, x, top + 21f);
            DrawRight(g, "02", f.Ghost, DailyReportPoster.CLine2, x + leftW, top - 8f);
            PaintApps(g, f, report, x, top + 52f, leftW, accent);

            DailyReportPoster.Draw(g, "月度之最", f.SectionTitle, DailyReportPoster.CText, rightX, top);
            DailyReportPoster.Draw(g, "被数字记住的四个瞬间", f.SectionSub, DailyReportPoster.CMuted, rightX, top + 21f);
            DrawRight(g, "03", f.Ghost, DailyReportPoster.CLine2, x + w, top - 8f);

            float exTop = top + 52f;
            float exGap = 10f;
            float exW = (rightW - exGap) / 2f;
            float exH = 74f;
            DrawExtreme(g, f, rightX, exTop, exW, exH, "最高单日",
                report.BestActiveSeconds > 0 ? TimeText(report.BestActiveSeconds) : "—",
                report.BestDay.Month + "." + report.BestDay.Day + " · " + Analysis.FmtCount(report.BestKeys) + " 次击键");
            DrawExtreme(g, f, rightX + exW + exGap, exTop, exW, exH, "最长会话",
                report.LongestSessionSeconds > 0 ? TimeText(report.LongestSessionSeconds) : "—",
                report.LongestSessionStart == DateTime.MinValue ? "—" : report.LongestSessionStart.ToString("MM.dd HH:mm", CultureInfo.InvariantCulture));
            DrawExtreme(g, f, rightX, exTop + exH + exGap, exW, exH, "最忙时段",
                report.ActiveDays > 0 && report.BusiestHourKeys > 0 ? report.BusiestHour + ":00" : "—",
                "整个月最密集的一小时");
            DrawExtreme(g, f, rightX + exW + exGap, exTop + exH + exGap, exW, exH, "上屏",
                report.Chars > 0 ? Analysis.FmtCount(report.Chars) + " 字" : "—", "Rime 提交记录");

            float bottom = Math.Max(top + 52f + Math.Max(1, report.Apps.Count) * 38f,
                exTop + exH * 2f + exGap + 14f);
            DailyReportPoster.HLine(g, x, bottom, w, DailyReportPoster.CLine);
            return bottom + 24f;
        }

        private static void DrawExtreme(Graphics g, DailyReportPoster.Fonts f, float x, float y, float w, float h,
            string label, string value, string note)
        {
            using (GraphicsPath box = DailyReportPoster.Rounded(x, y, w, h, 12f))
            {
                using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface2)) g.FillPath(fill, box);
                using (Pen border = new Pen(DailyReportPoster.CLine, 1f)) g.DrawPath(border, box);
            }
            DailyReportPoster.Draw(g, label, f.StatLabel, DailyReportPoster.CMuted, x + 12f, y + 10f);
            DailyReportPoster.Draw(g, value, f.StatValue, DailyReportPoster.CText, x + 12f, y + 28f);
            DailyReportPoster.Draw(g, DailyReportPoster.Fit(g, f.ChartSmall, note, w - 24f),
                f.ChartSmall, DailyReportPoster.CMuted, x + 12f, y + h - 18f);
        }

        // ---------------------------------------------------------------- 公共

        private static void DrawLegend(Graphics g, DailyReportPoster.Fonts f, float x, float y,
            Color accent, string first, string second)
        {
            using (Pen pen = new Pen(accent, 2f)) g.DrawLine(pen, x, y + 6f, x + 16f, y + 6f);
            DailyReportPoster.Draw(g, first, f.Legend, DailyReportPoster.CMuted, x + 22f, y);
            using (Pen pen = new Pen(Color.FromArgb(150, DailyReportPoster.CMuted), 1.4f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, x + 72f, y + 6f, x + 88f, y + 6f);
            }
            DailyReportPoster.Draw(g, second, f.Legend, DailyReportPoster.CMuted, x + 94f, y);
        }

        private static void DrawPeak(Graphics g, DailyReportPoster.Fonts f, PointF point, string text,
            float plotL, float plotR, float plotT, float plotB)
        {
            float tw = DailyReportPoster.Measure(g, text, f.Annotation) + 22f;
            float tx = point.X + 10f;
            if (tx + tw > plotR - 6f) tx = point.X - tw - 10f;
            if (tx < plotL + 4f) tx = plotL + 4f;
            float ty = Math.Max(plotT + 12f, point.Y - 30f);
            using (GraphicsPath path = DailyReportPoster.Rounded(tx, ty, tw, 24f, 7f))
            {
                using (SolidBrush fill = new SolidBrush(DailyReportPoster.CSurface3)) g.FillPath(fill, path);
                using (Pen border = new Pen(DailyReportPoster.CLine2, 1f)) g.DrawPath(border, path);
            }
            DailyReportPoster.DrawCentered(g, text, f.Annotation, DailyReportPoster.CText2,
                tx + tw / 2f, ty + 6f);
        }

        private static string TimeText(double seconds)
        {
            if (seconds <= 0) return "—";
            return ActivityMonitor.FormatDuration(seconds);
        }

        private static string RangeText(DateTime start, DateTime end)
        {
            if (start.Year != end.Year)
                return start.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + " — "
                    + end.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            return start.Year + " · " + start.ToString("MM.dd", CultureInfo.InvariantCulture)
                + " — " + end.ToString("MM.dd", CultureInfo.InvariantCulture);
        }

        private static string ShortDate(DateTime day)
        {
            return day.ToString("MM.dd", CultureInfo.InvariantCulture);
        }

        private static long NiceStep(long max)
        {
            if (max <= 2000) return 500;
            if (max <= 5000) return 1000;
            if (max <= 10000) return 2000;
            if (max <= 25000) return 5000;
            if (max <= 50000) return 10000;
            return 20000;
        }

        private static void PaintFooter(Graphics g, DailyReportPoster.Fonts f, PeriodReport report,
            float x, float y, float w)
        {
            float top = y + 22f;
            DailyReportPoster.Draw(g, "由本地数据生成 · " + DateTime.Now.ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture),
                f.Footer, DailyReportPoster.CMuted, x, top);
            string basis = report.Kind == PeriodKind.Week
                ? "范围：" + report.Start.ToString("MM.dd", CultureInfo.InvariantCulture) + " — " + report.End.ToString("MM.dd", CultureInfo.InvariantCulture)
                : (report.IsPartialMonth ? "统计至 " + report.End.ToString("MM.dd", CultureInfo.InvariantCulture) : "完整月份");
            DrawRight(g, basis, f.Footer, DailyReportPoster.CMuted, x + w, top);
        }
    }
}
