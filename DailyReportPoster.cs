// ============================================================================
//  KeyMouseStats v3.0 · DailyReportPoster.cs
//
//  「生成战报长图」背后的新渲染器:把 DailyReport 画成一张 3× 高清报告纸。
//  纯绘制:输入日报模型 + 当天记录,输出 Bitmap;不读存档、不碰 UI 单例。
//  固定午夜深色(与 HTML 设计稿一致),不含左侧导航和顶栏。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeyMouseStats
{
    internal static class DailyReportPoster
    {
        internal const int LogicalWidth = 1440;
        internal const int OutputScale = 3;          // 4320 px wide

        private const int CanvasMargin = 60;
        private const int PadX = 56;
        private const int PadTop = 46;
        private const int PadBottom = 30;
        private const int ContentX = CanvasMargin + PadX;                       // 116
        private const int ContentW = LogicalWidth - CanvasMargin * 2 - PadX * 2; // 1208

        // ---- 午夜深色(与 design/report-redesign 的 CSS 变量一致) ----
        internal static readonly Color CCanvas = Color.FromArgb(8, 11, 17);
        internal static readonly Color CSurface = Color.FromArgb(15, 19, 28);
        internal static readonly Color CSurface2 = Color.FromArgb(21, 26, 38);
        internal static readonly Color CSurface3 = Color.FromArgb(28, 35, 49);
        internal static readonly Color CText = Color.FromArgb(238, 241, 247);
        internal static readonly Color CText2 = Color.FromArgb(199, 205, 218);
        internal static readonly Color CMuted = Color.FromArgb(146, 156, 177);
        internal static readonly Color CLine = Color.FromArgb(20, 255, 255, 255);
        internal static readonly Color CLine2 = Color.FromArgb(38, 255, 255, 255);
        internal static readonly Color CAccent = Color.FromArgb(243, 178, 102);
        internal static readonly Color CMint = Color.FromArgb(123, 224, 195);
        internal static readonly Color CViolet = Color.FromArgb(169, 166, 255);
        internal static readonly Color CWarm = Color.FromArgb(240, 138, 114);
        internal static readonly Color CBad = Color.FromArgb(255, 140, 140);

        private static readonly string[] WeekNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        private static readonly string[] MonthNames =
        {
            "", "一月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "十一月", "十二月"
        };
        private static readonly string[] CompositionNames = { "汉字", "标点", "字母数字", "空白", "其他" };
        private static readonly Color[] CompositionColors = { CAccent, CMint, CViolet, CWarm, CMuted };

        private static readonly Color[] AppColors =
        {
            Color.FromArgb(91, 141, 239),
            Color.FromArgb(78, 201, 176),
            Color.FromArgb(116, 170, 156),
            Color.FromArgb(224, 177, 90),
            Color.FromArgb(169, 166, 255)
        };

        // ---------------------------------------------------------------- 对外

        /// <summary>渲染 3× 高清 PNG(逻辑 1440 → 输出 4320)。</summary>
        internal static Bitmap Render(DailyReport report, DayRecord day, bool mask)
        {
            if (report == null) report = new DailyReport();

            bool previous = PrivacyMask.Enabled;
            PrivacyMask.Enabled = mask;
            try
            {
                float contentBottom;
                using (Bitmap measure = new Bitmap(LogicalWidth, 2800, PixelFormat.Format32bppArgb))
                using (Graphics mg = Graphics.FromImage(measure))
                {
                    Prepare(mg);
                    contentBottom = PaintContent(mg, report, day);
                }

                int canvasHeight = (int)Math.Ceiling(contentBottom + PadBottom + CanvasMargin);
                if (canvasHeight < 900) canvasHeight = 900;
                if (canvasHeight > 2400) canvasHeight = 2400;

                Bitmap bitmap = new Bitmap(LogicalWidth * OutputScale, canvasHeight * OutputScale,
                    PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    Prepare(g);
                    g.Clear(CCanvas);
                    g.ScaleTransform(OutputScale, OutputScale);

                    RectangleF sheet = new RectangleF(CanvasMargin, CanvasMargin,
                        LogicalWidth - CanvasMargin * 2f, canvasHeight - CanvasMargin * 2f);
                    using (GraphicsPath path = Rounded(sheet.X, sheet.Y, sheet.Width, sheet.Height, 30f))
                    {
                        using (SolidBrush fill = new SolidBrush(CSurface)) g.FillPath(fill, path);
                        using (Pen border = new Pen(CLine2, 1f)) g.DrawPath(border, path);
                        g.SetClip(path);
                        PaintContent(g, report, day);
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

        /// <summary>保存到日报目录;文件名 2026-11-17-日报.png。失败会抛异常,由调用方兜底。</summary>
        internal static string Save(Bitmap bitmap, DateTime day)
        {
            if (!Store.CanPersist) return "";   // 测试/预览/示例数据不往用户日报目录写东西
            string dir = DailyReport.Directory;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-日报.png");
            try { bitmap.SetResolution(96f * OutputScale, 96f * OutputScale); }
            catch (Exception) { /* 某些 GDI+ 驱动会拒绝设置分辨率,不影响像素 */ }
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        // ---------------------------------------------------------------- 画布

        internal static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
        }

        // ---------------------------------------------------------------- 主体

        private static float PaintContent(Graphics g, DailyReport report, DayRecord day)
        {
            using (Fonts f = new Fonts())
            {
                float x = ContentX;
                float y = CanvasMargin + PadTop;
                float w = ContentW;

                y = PaintHeader(g, f, report, x, y, w);
                y = PaintMetrics(g, f, report, x, y, w);
                y = PaintPulse(g, f, report, day, x, y, w);
                y = PaintMiddle(g, f, report, x, y, w);
                y = PaintBottom(g, f, report, x, y, w);
                PaintFooter(g, f, report, x, y, w);
                return y + 46f;
            }
        }

        // ---------------------------------------------------------------- 页头

        private static float PaintHeader(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            bool has = report.HasData;
            string week = WeekNames[(int)report.Day.DayOfWeek];
            string dateText = report.Day.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);

            DrawRight(g, "键鼠统计 · ACTIVITY MONITOR", f.Brand, CMuted, x + w, y + 1);

            FillDot(g, x, y + 5f, 5f, CAccent);
            Draw(g, "DAILY / 日报  ·  第 " + WeekOfYear(report.Day) + " 周", f.Eyebrow, CMuted, x + 14f, y);
            y += 30f;

            float dateTop = y;
            string dayText = report.Day.Day.ToString(CultureInfo.InvariantCulture);
            Draw(g, dayText, f.DateBig, CAccent, x, dateTop - 10f);
            float dateW = Measure(g, dayText, f.DateBig);
            Draw(g, MonthNames[report.Day.Month], f.DateMonth, CText, x + dateW + 16f, dateTop + 6f);
            Draw(g, report.Day.Year + " · " + week, f.DateSub, CMuted, x + dateW + 16f, dateTop + 32f);

            string status = !has ? "无数据" : (report.InProgress ? "进行中" : "已结束");
            Color statusColor = has ? CMint : CMuted;
            float statusW = Measure(g, status, f.Status) + 26f;
            Pill(g, x + w - statusW, dateTop + 4f, statusW, 24f, status, f.Status, statusColor, 16, 42, statusColor);
            DrawRight(g, dateText, f.DateSub, CMuted, x + w, dateTop + 38f);

            y = dateTop + 96f;

            if (has)
            {
                float cx = x;
                cx += Draw(g, "敲了 ", f.Headline, CText, cx, y);
                cx += DrawCount(g, f, Analysis.FmtCount(report.Keys), cx, y, CAccent);
                Draw(g, " 次键，", f.Headline, CText, cx, y);
                y += 60f;

                cx = x;
                cx += Draw(g, "把 ", f.Headline, CText, cx, y);
                cx += Draw(g, ActivityMonitor.FormatDuration(report.ActiveSeconds), f.HeadlineCjk, CAccent, cx, y);
                Draw(g, " 留在屏幕前。", f.Headline, CText, cx, y);
                y += 64f;
            }
            else
            {
                Draw(g, "这一天没有记录。", f.Headline, CText, x, y);
                y += 64f;
            }

            List<string> lede = Wrap(g, f.Lede, BuildLede(report), Math.Min(w, 980f));
            for (int i = 0; i < lede.Count && i < 2; i++)
                Draw(g, lede[i], f.Lede, CMuted, x, y + i * 23f);
            y += Math.Min(2, Math.Max(1, lede.Count)) * 23f + 24f;

            HLine(g, x, y, w, CLine);
            return y + 24f;
        }

        private static string BuildLede(DailyReport report)
        {
            if (!report.HasData) return "没有数据：可能没开机，或当天统计被暂停过。";

            string text = "";
            if (report.Apps.Count > 0 && report.ActiveSeconds > 0)
            {
                DailyReport.AppLine app = report.Apps[0];
                int share = (int)Math.Round(app.Seconds / Math.Max(1.0, report.ActiveSeconds) * 100.0);
                string name = PrivacyMask.Enabled ? "首要应用 · 已隐藏" : app.Name;
                text += "主要时间花在 " + name + "（" + share + "%）。";
            }
            if (report.BaseDays > 0 && report.BaseKeys > 0)
            {
                double ratio = report.Keys / report.BaseKeys;
                if (Math.Abs(ratio - 1.0) >= 0.005)
                    text += "击键较" + report.BaselineLabel + (ratio > 1 ? "高 " : "低 ")
                        + Math.Abs(ratio * 100 - 100).ToString("0", CultureInfo.InvariantCulture) + "%。";
            }
            if (text.Length == 0) text = "这一天的主要数据都在这张卡片里。";
            return text;
        }

        // ---------------------------------------------------------------- 指标

        private static float PaintMetrics(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            const float h = 122f;
            float col = w / 4f;
            bool has = report.HasData;
            bool hasChars = has && report.HasChars;
            // 主标题已经有「击键 / 有效使用」,这里只放增量信息,避免首屏重复。
            DrawMetric(g, f, report, x, y, col, 0, "点击", "CLICKS",
                has ? Analysis.FmtCount(report.Clicks) : "0", has ? report.Clicks : 0, has ? report.BaseClicks : 0, false, "");
            DrawMetric(g, f, report, x, y, col, 1, "上屏", "TEXT",
                hasChars ? Analysis.FmtCount(report.Chars) + " 字" : "—",
                hasChars ? report.Chars : 0, hasChars ? report.BaseChars : 0, false, "");
            DrawMetric(g, f, report, x, y, col, 2, "峰值 APM", "PEAK APM",
                has && report.PeakApm > 0 ? report.PeakApm.ToString("N0", CultureInfo.InvariantCulture) : "—",
                0, 0, false, "单日峰值");
            DrawMetric(g, f, report, x, y, col, 3, "最长连续", "LONGEST",
                has && report.LongestSessionSeconds > 0
                    ? ActivityMonitor.FormatDuration(report.LongestSessionSeconds) : "—",
                0, 0, true, "最长一段");

            for (int i = 1; i < 4; i++)
                VLine(g, x + col * i, y + 12f, y + h - 14f, CLine);

            HLine(g, x, y + h, w, CLine);
            return y + h + 24f;
        }

        private static void DrawMetric(Graphics g, Fonts f, DailyReport report, float x, float y, float col,
            int index, string label, string en, string value, double current, double baseValue, bool mixed, string hint)
        {
            float px = x + col * index + (index == 0 ? 0f : 26f);
            float avail = col - (index == 0 ? 0f : 26f) - 12f;

            Draw(g, label, f.MetricLabel, CMuted, px, y + 14f);
            DrawRight(g, en, f.MetricLabelEn, Color.FromArgb(210, CMuted), x + col * (index + 1) - 12f, y + 16f);

            if (mixed)
                Draw(g, value, f.MetricValueCjk, CText, px, y + 38f);
            else
                DrawMetricCount(g, f, value, px, y + 34f, avail);

            string delta = DeltaText(current, baseValue);
            if (delta.Length > 0)
            {
                Color color = delta.StartsWith("▲", StringComparison.Ordinal) ? CMint
                    : delta.StartsWith("▼", StringComparison.Ordinal) ? CBad : CMuted;
                Draw(g, delta, f.Delta, color, px, y + 87f);
                float dw = Measure(g, delta, f.Delta);
                string vs = report.BaseDays > 0 ? "较" + report.BaselineLabel : "暂无对比";
                Draw(g, vs, f.Micro, CMuted, px + dw + 8f, y + 89f);
            }
            else
            {
                Draw(g, string.IsNullOrEmpty(hint) ? "暂无对比" : hint, f.Micro, CMuted, px, y + 89f);
            }
        }

        private static void DrawMetricCount(Graphics g, Fonts f, string text, float x, float y, float avail)
        {
            if (string.IsNullOrEmpty(text)) return;
            string number = text;
            string unit = "";
            int cut = SuffixIndex(text);
            if (cut < 0)
            {
                int space = text.LastIndexOf(' ');
                if (space > 0) cut = space;
            }
            if (cut > 0) { number = text.Substring(0, cut); unit = text.Substring(cut).TrimStart(); }

            float numberSize = 38f;
            if (Measure(g, number, f.MetricValue) > avail - 20f) numberSize = 28f;
            Font numberFont = numberSize == 38f ? f.MetricValue : f.MetricValueCjk;
            float nx = x;
            nx += Draw(g, number, numberFont, CText, nx, y);
            if (unit.Length > 0)
            {
                Draw(g, unit, f.MetricUnit, CMuted, nx + 4f, y + 20f);
            }
        }

        internal static string DeltaText(double current, double baseValue)
        {
            if (baseValue <= 0) return "";
            double ratio = current / baseValue - 1;
            if (Math.Abs(ratio) < 0.005) return "持平";
            return (ratio > 0 ? "▲ " : "▼ ")
                + Math.Abs(ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        // ---------------------------------------------------------------- 24 小时脉搏

        private static float PaintPulse(Graphics g, Fonts f, DailyReport report, DayRecord day, float x, float y, float w)
        {
            float contentTop = y + 30f;
            Draw(g, "一天的活动脉搏", f.SectionTitle, CText, x, contentTop);
            Draw(g, "每小时击键次数 · 24 小时", f.SectionSub, CMuted, x, contentTop + 21f);

            float legendX = x + w - 150f;
            FillDot(g, legendX, contentTop + 7f, 6f, CAccent);
            Draw(g, "击键", f.Legend, CMuted, legendX + 10f, contentTop + 1f);
            FillDot(g, legendX + 62f, contentTop + 7f, 6f, CMint);
            Draw(g, "活跃时段", f.Legend, CMuted, legendX + 72f, contentTop + 1f);

            float plotL = x + 44f;
            float plotR = x + w;
            float plotT = contentTop + 46f;
            float plotB = plotT + 250f;
            float plotW = plotR - plotL;
            float plotH = plotB - plotT;

            long maxValue = 0;
            for (int i = 0; i < 24; i++) if (report.HourKeys[i] > maxValue) maxValue = report.HourKeys[i];
            long stepValue = NiceStep(maxValue);
            long scaleMax = stepValue;
            while (scaleMax < maxValue) scaleMax += stepValue;
            if (scaleMax <= 0) scaleMax = 1000;

            for (long v = 0; v <= scaleMax; v += stepValue)
            {
                float gy = plotB - (float)v / scaleMax * plotH;
                HLine(g, plotL, gy, plotW, CLine);
                DrawRight(g, v == 0 ? "0" : v.ToString("N0", CultureInfo.InvariantCulture),
                    f.Chart, CMuted, plotL - 9f, gy - 5f);
            }

            float slot = plotW / 24f;
            for (int hour = 0; hour < 24; hour++)
            {
                long value = report.HourKeys[hour];
                if (value <= 0) continue;
                float barW = Math.Max(4f, slot * 0.54f);
                float bx = plotL + slot * hour + (slot - barW) / 2f;
                float bh = Math.Max(2f, (float)value / scaleMax * plotH);
                float by = plotB - bh;

                using (GraphicsPath path = Rounded(bx, by, barW, bh, 4f))
                {
                    if (hour == report.BusiestHour)
                    {
                        using (SolidBrush solid = new SolidBrush(CAccent)) g.FillPath(solid, path);
                    }
                    else
                    {
                        RectangleF rect = new RectangleF(bx, by, barW, bh);
                        if (rect.Height < 2f) rect.Height = 2f;
                        using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                            Color.FromArgb(232, CAccent), Color.FromArgb(20, CAccent), LinearGradientMode.Vertical))
                            g.FillPath(brush, path);
                    }
                }
            }

            double avg = 0;
            int activeHours = 0;
            for (int i = 0; i < 24; i++)
            {
                if (report.HourKeys[i] <= 0) continue;
                avg += report.HourKeys[i];
                activeHours++;
            }
            if (activeHours > 0)
            {
                avg /= activeHours;
                float avgY = plotB - (float)(avg / scaleMax) * plotH;
                using (Pen pen = new Pen(Color.FromArgb(120, CMuted), 1f))
                {
                    pen.DashPattern = new float[] { 3f, 5f };
                    g.DrawLine(pen, plotL, avgY, plotR, avgY);
                }
                DrawRight(g, "活跃小时均值 " + Math.Round(avg).ToString("0", CultureInfo.InvariantCulture),
                    f.ChartSmall, CMuted, plotR, avgY - 14f);
            }

            if (report.BusiestHour >= 0 && report.BusiestHourKeys > 0)
            {
                float px = plotL + slot * (report.BusiestHour + 0.5f);
                float py = plotB - (float)report.BusiestHourKeys / scaleMax * plotH;
                using (Pen pen = new Pen(Color.FromArgb(120, CAccent), 1f))
                {
                    pen.DashPattern = new float[] { 3f, 5f };
                    g.DrawLine(pen, px, py + 8f, px, plotB);
                }
                string tag = report.BusiestHour + ":00 · "
                    + Analysis.FmtCount(report.BusiestHourKeys) + " 次";
                float tw = Measure(g, tag, f.Annotation) + 22f;
                float tx = px + 10f;
                if (tx + tw > plotR - 6f) tx = px - tw - 10f;
                if (tx < plotL + 4f) tx = plotL + 4f;
                float ty = Math.Max(plotT + 12f, py - 30f);
                using (GraphicsPath path = Rounded(tx, ty, tw, 24f, 7f))
                {
                    using (SolidBrush fill = new SolidBrush(CSurface3)) g.FillPath(fill, path);
                    using (Pen border = new Pen(CLine2, 1f)) g.DrawPath(border, path);
                }
                DrawCentered(g, tag, f.Annotation, CText2, tx + tw / 2f, ty + 6f);
            }

            HLine(g, plotL, plotB, plotW, CLine2);

            // 会话色带紧贴柱状图,共用下方同一套 3 小时刻度,不再画第二套时间轴。
            float stripT = plotB + 10f;
            float stripH = 9f;
            using (GraphicsPath track = Rounded(plotL, stripT, plotW, stripH, 4f))
            using (SolidBrush fill = new SolidBrush(CSurface2)) g.FillPath(fill, track);

            if (day != null && day.Sessions != null)
            {
                for (int i = 0; i < day.Sessions.Count; i++)
                {
                    ActiveSession session = day.Sessions[i];
                    if (session == null || session.Seconds <= 0) continue;
                    double start = (session.Start - report.Day).TotalHours;
                    if (start < 0) start = 0;
                    if (start > 24) start = 24;
                    double dur = session.Seconds / 3600.0;
                    double end = start + dur;
                    if (end > 24) end = 24;
                    if (end <= start) continue;
                    float sx = plotL + (float)(start / 24.0) * plotW;
                    float sw = Math.Max(2f, (float)((end - start) / 24.0) * plotW);
                    using (GraphicsPath block = Rounded(sx, stripT, sw, stripH, 3f))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, CMint))) g.FillPath(fill, block);
                }
            }

            for (int hour = 0; hour < 24; hour += 3)
                DrawCentered(g, hour < 10 ? "0" + hour : hour.ToString(CultureInfo.InvariantCulture),
                    f.Chart, CMuted, plotL + slot * (hour + 0.5f), stripT + stripH + 9f);

            if (report.Keys <= 0)
                DrawCentered(g, "这一天没有记录", f.SectionSub, CMuted, (plotL + plotR) / 2f, (plotT + plotB) / 2f);

            float bottom = stripT + stripH + 26f;
            HLine(g, x, bottom, w, CLine);
            return bottom + 24f;
        }

        private static long NiceStep(long max)
        {
            if (max <= 1000) return 200;
            if (max <= 2500) return 500;
            if (max <= 5000) return 1000;
            if (max <= 10000) return 2000;
            if (max <= 25000) return 5000;
            if (max <= 50000) return 10000;
            return 20000;
        }

        // ---------------------------------------------------------------- 中段:应用 / 注脚

        private static float PaintMiddle(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            float top = y + 30f;
            float gap = 46f;
            float leftW = (w - gap) * 0.58f;
            float rightW = w - leftW - gap;
            float rightX = x + leftW + gap;

            Draw(g, "时间去向", f.SectionTitle, CText, x, top);
            Draw(g, "TOP 5 · 按活跃时长", f.SectionSub, CMuted, x, top + 21f);
            DrawRight(g, "01", f.Ghost, CLine2, x + leftW, top - 8f);

            float listTop = top + 52f;
            float rowH = 38f;
            int rows = report.Apps.Count;
            if (rows > 5) rows = 5;
            if (rows == 0)
            {
                Draw(g, "没有应用归因数据", f.Note, CMuted, x, listTop + 16f);
            }
            else
            {
                double maxApp = 1.0;
                for (int i = 0; i < rows; i++)
                    if (report.Apps[i].Seconds > maxApp) maxApp = report.Apps[i].Seconds;

                for (int i = 0; i < rows; i++)
                {
                    DailyReport.AppLine app = report.Apps[i];
                    float ry = listTop + i * rowH;
                    string name = PrivacyMask.Enabled ? "应用 " + (i + 1) + " · 已隐藏" : app.Name;
                    Draw(g, (i + 1).ToString("00", CultureInfo.InvariantCulture), f.AppRank, CMuted, x, ry + 9f);
                    DrawAppMark(g, f, name, x + 24f, ry + 5f, i);
                    Draw(g, Fit(g, f.AppName, name, 150f), f.AppName, i == 0 ? CText : CText2, x + 62f, ry + 9f);

                    float tx = x + 224f;
                    float timeW = 48f;
                    float pctW = 38f;
                    float trackW = x + leftW - tx - timeW - pctW - 24f;
                    DrawTrack(g, tx, ry + rowH / 2f - 2f, trackW, i,
                        (float)(app.Seconds / maxApp), CAccent);

                    DrawRight(g, ShortDuration(app.Seconds), f.AppTime, CText2, x + leftW - pctW - 10f, ry + 9f);
                    int pct = (int)Math.Round(app.Seconds / Math.Max(1.0, report.ActiveSeconds) * 100.0);
                    DrawRight(g, pct + "%", f.AppPct, CMuted, x + leftW, ry + 10f);

                    if (i < rows - 1)
                    {
                        using (Pen pen = new Pen(CLine, 1f))
                        {
                            pen.DashStyle = DashStyle.Dot;
                            g.DrawLine(pen, x + 62f, ry + rowH - 1f, x + leftW, ry + rowH - 1f);
                        }
                    }
                }
            }
            float leftBottom = listTop + Math.Max(1, rows) * rowH;

            Draw(g, "今日注脚", f.SectionTitle, CText, rightX, top);
            Draw(g, "由这一天的数字生成", f.SectionSub, CMuted, rightX, top + 21f);
            DrawRight(g, "02", f.Ghost, CLine2, x + w, top - 8f);

            float notesTop = top + 52f;
            float noteY = notesTop;
            int shown = 0;
            for (int i = 0; i < report.Reminders.Count && shown < 4; i++)
            {
                string text = report.Reminders[i];
                if (PrivacyMask.Enabled) text = MaskMentions(text, report);
                List<string> lines = Wrap(g, f.Note, text, rightW - 22f);
                if (lines.Count == 0) continue;
                Color dot = NoteColor(text);
                FillDot(g, rightX + 2f, noteY + 8f, 6f, dot);
                for (int k = 0; k < lines.Count && k < 2; k++)
                    Draw(g, lines[k], f.Note, CText2, rightX + 20f, noteY + k * 21f);
                noteY += Math.Min(2, lines.Count) * 21f + 12f;
                shown++;
            }
            if (report.Reminders.Count > shown)
                Draw(g, "+" + (report.Reminders.Count - shown) + " 条细节", f.Micro, CMuted, rightX + 20f, noteY);

            float quoteY = Math.Max(noteY + 16f, leftBottom - 34f);
            HLine(g, rightX, quoteY, rightW, CLine);
            Draw(g, "“把时间花在哪里，是看得见的。”", f.Quote, CText, rightX, quoteY + 14f);

            float bottom = Math.Max(leftBottom, quoteY + 40f);
            HLine(g, x, bottom, w, CLine);
            return bottom + 24f;
        }

        internal static void DrawAppMark(Graphics g, Fonts f, string name, float x, float y, int index)
        {
            Color color = AppColors[index % AppColors.Length];
            using (GraphicsPath path = Rounded(x, y, 28f, 28f, 8f))
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(34, color))) g.FillPath(fill, path);
                using (Pen border = new Pen(Color.FromArgb(50, color), 1f)) g.DrawPath(border, path);
            }
            string letter = PrivacyMask.Enabled
                ? (index + 1).ToString(CultureInfo.InvariantCulture)
                : (string.IsNullOrEmpty(name) ? "·" : name.Substring(0, 1));
            DrawCentered(g, letter, f.AppMark, color, x + 14f, y + 7f);
        }

        internal static void DrawTrack(Graphics g, float x, float y, float w, int index, float ratio, Color accent)
        {
            using (GraphicsPath track = Rounded(x, y, w, 4f, 2f))
            using (SolidBrush fill = new SolidBrush(CSurface3)) g.FillPath(fill, track);

            int[] alphas = { 235, 145, 115, 92, 72 };
            if (ratio < 0.01f) ratio = 0.01f;
            if (ratio > 1f) ratio = 1f;
            float len = Math.Max(2f, w * ratio);
            using (GraphicsPath fill = Rounded(x, y, len, 4f, 2f))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(alphas[Math.Min(index, alphas.Length - 1)], accent)))
                g.FillPath(brush, fill);
        }

        private static Color NoteColor(string text)
        {
            if (text.IndexOf("达成", StringComparison.Ordinal) >= 0
                || text.IndexOf("平稳", StringComparison.Ordinal) >= 0) return CMint;
            if (text.IndexOf("浏览器", StringComparison.Ordinal) >= 0
                || text.IndexOf("占了", StringComparison.Ordinal) >= 0) return CBad;
            return CAccent;
        }

        private static string MaskMentions(string text, DailyReport report)
        {
            if (string.IsNullOrEmpty(text)) return text;
            for (int i = 0; i < report.Apps.Count; i++)
            {
                string name = report.Apps[i].Name;
                if (!string.IsNullOrEmpty(name))
                    text = text.Replace(name, "某个应用");
            }
            return text;
        }

        // ---------------------------------------------------------------- 下段:习惯 / 文字 / 效率

        private static float PaintBottom(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            float top = y + 30f;
            float gap = 34f;
            float colW = (w - gap * 2f) / 3f;

            float c1 = PaintHabits(g, f, report, x, top, colW);
            float c2 = PaintText(g, f, report, x + colW + gap, top, colW);
            float c3 = PaintEfficiency(g, f, report, x + (colW + gap) * 2f, top, colW);

            float bottom = Math.Max(c1, Math.Max(c2, c3));
            HLine(g, x, bottom, w, CLine);
            return bottom + 24f;
        }

        private static float PaintHabits(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            Draw(g, "操作习惯", f.SectionTitle, CText, x, y);
            Draw(g, "组合键与按键", f.SectionSub, CMuted, x, y + 21f);

            float top = y + 50f;
            int rows = 0;
            if (report.Combos.Count > 0)
            {
                for (int i = 0; i < report.Combos.Count && rows < 4; i++, rows++)
                {
                    string key;
                    string count;
                    SplitCombo(report.Combos[i], out key, out count);
                    float ry = top + rows * 30f;
                    Draw(g, key, f.HabitKey, CText2, x, ry + 7f);
                    DrawRight(g, count, f.HabitCount, CText, x + w, ry + 4f);
                    HLine(g, x, ry + 28f, w, CLine);
                }
            }
            else if (report.TopKeys.Count > 0)
            {
                for (int i = 0; i < report.TopKeys.Count && rows < 4; i++, rows++)
                {
                    Draw(g, report.TopKeys[i], f.HabitKey, CText2, x, top + rows * 30f + 7f);
                    HLine(g, x, top + rows * 30f + 28f, w, CLine);
                }
            }
            else
            {
                Draw(g, "这一天没有记录", f.Micro, CMuted, x, top + 8f);
            }

            float microY = top + Math.Max(1, rows) * 30f + 22f;
            if (report.Combos.Count == 0) return microY + 14f;

            string keys = "按键 Top：" + JoinTop(report.TopKeys, 3);
            List<string> lines = Wrap(g, f.Micro, keys, w);
            for (int i = 0; i < lines.Count && i < 2; i++)
                Draw(g, lines[i], f.Micro, CMuted, x, microY + i * 15f);

            return microY + Math.Min(2, Math.Max(1, lines.Count)) * 15f + 20f;
        }

        private static float PaintText(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            Draw(g, "文字产出", f.SectionTitle, CText, x, y);
            Draw(g, report.HasChars ? "上屏 " + Analysis.FmtCount(report.Chars) + " 字 · "
                + Analysis.FmtCount(report.Commits) + " 次" : "需要 Rime 的 commit_log.csv",
                f.SectionSub, CMuted, x, y + 21f);

            if (!report.HasChars)
            {
                Draw(g, "这一天没有上屏记录", f.Note, CMuted, x, y + 56f);
                Draw(g, "（需要 Rime 的 commit_log.csv）", f.Micro, CMuted, x, y + 80f);
                return y + 128f;
            }

            float top = y + 52f;
            float nx = x;
            nx += Draw(g, report.AvgCharsPerCommit.ToString("0.0", CultureInfo.InvariantCulture),
                f.InlineBig, CText, nx, top);
            Draw(g, "字 / 次", f.InlineUnit, CMuted, nx + 6f, top + 18f);

            string line = Analysis.FmtCount(report.Chars) + " 字 · " + Analysis.FmtCount(report.Commits)
                + " 次 · 不同字 " + Analysis.FmtCount(report.DistinctChars) + " 种";
            Draw(g, Fit(g, f.Micro, line, w), f.Micro, CMuted, x, top + 44f);

            long total = 0;
            for (int i = 0; i < report.Composition.Length; i++) total += report.Composition[i];
            float barY = top + 66f;
            if (total > 0)
            {
                float bx = x;
                for (int i = 0; i < report.Composition.Length; i++)
                {
                    if (report.Composition[i] <= 0) continue;
                    float sw = (float)(report.Composition[i] / (double)total) * w;
                    using (SolidBrush brush = new SolidBrush(CompositionColors[i]))
                        g.FillRectangle(brush, bx, barY, Math.Max(1f, sw - 1f), 8f);
                    bx += sw;
                }
            }

            float legendY = barY + 18f;
            float lx = x;
            float lineH = 16f;
            for (int i = 0; i < CompositionNames.Length; i++)
            {
                string item = CompositionNames[i] + " "
                    + (total > 0 ? (report.Composition[i] * 100.0 / total).ToString("0", CultureInfo.InvariantCulture) + "%" : "0%");
                float itemW = Measure(g, item, f.Micro) + 14f;
                if (lx + itemW > x + w)
                {
                    lx = x;
                    legendY += lineH;
                }
                FillDot(g, lx, legendY + 4f, 6f, CompositionColors[i]);
                Draw(g, item, f.Micro, CMuted, lx + 10f, legendY);
                lx += itemW + 8f;
            }

            float highY = legendY + 27f;
            bool hasPhrases = report.TopPhrases.Count > 0;
            List<string> highlights = hasPhrases
                ? report.TopPhrases
                : (report.TopContentChars.Count > 0 ? report.TopContentChars : report.TopChars);
            string high = (hasPhrases ? "高频词：" : "高频字：")
                + (PrivacyMask.Enabled ? JoinMaskedTop(highlights, 3) : JoinTop(highlights, 3));
            Draw(g, Fit(g, f.Micro, high, w), f.Micro, CMuted, x, highY);
            return highY + 30f;
        }

        private static float PaintEfficiency(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            Draw(g, "会话侧写", f.SectionTitle, CText, x, y);
            Draw(g, "鼠标与分段", f.SectionSub, CMuted, x, y + 21f);

            float top = y + 50f;
            float boxGap = 8f;
            float boxW = (w - boxGap * 2f) / 3f;
            float boxH = 78f;
            double avgMinutes = report.SessionCount > 0
                ? report.ActiveSeconds / Math.Max(1, report.SessionCount) / 60.0 : 0;

            DrawStatBox(g, f, x, top, boxW, boxH,
                report.MoveMeters > 0
                    ? Math.Round(report.MoveMeters).ToString("0", CultureInfo.InvariantCulture) + " m" : "—",
                "鼠标移动");
            DrawStatBox(g, f, x + boxW + boxGap, top, boxW, boxH,
                report.SessionCount > 0 ? report.SessionCount + " 段" : "—", "会话段数");
            DrawStatBox(g, f, x + (boxW + boxGap) * 2f, top, boxW, boxH,
                avgMinutes > 0 ? Math.Round(avgMinutes).ToString("0", CultureInfo.InvariantCulture) + " 分" : "—", "平均每段");

            Draw(g, "鼠标距离按当前 DPI 估算", f.Micro, CMuted, x, top + boxH + 12f);
            return top + boxH + 34f;
        }

        internal static void DrawStatBox(Graphics g, Fonts f, float x, float y, float w, float h, string value, string label)
        {
            using (GraphicsPath path = Rounded(x, y, w, h, 12f))
            {
                using (SolidBrush fill = new SolidBrush(CSurface2)) g.FillPath(fill, path);
                using (Pen border = new Pen(CLine, 1f)) g.DrawPath(border, path);
            }
            Draw(g, Fit(g, f.StatValue, value, w - 20f), f.StatValue, CText, x + 12f, y + 10f);
            Draw(g, label, f.StatLabel, CMuted, x + 12f, y + h - 22f);
        }

        // ---------------------------------------------------------------- 页脚

        private static void PaintFooter(Graphics g, Fonts f, DailyReport report, float x, float y, float w)
        {
            float top = y + 22f;
            Draw(g, "由本地数据生成 · " + DateTime.Now.ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture),
                f.Footer, CMuted, x, top);
            DrawRight(g, report.BaseDays > 0 ? "对比基准：" + report.BaselineLabel : "对比基准：暂无",
                f.Footer, CMuted, x + w, top);
        }

        // ---------------------------------------------------------------- 绘制工具

        internal static float Draw(Graphics g, string text, Font font, Color color, float x, float y)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            using (SolidBrush brush = new SolidBrush(color))
                g.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
            return Measure(g, text, font);
        }

        internal static void DrawRight(Graphics g, string text, Font font, Color color, float right, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            Draw(g, text, font, color, right - Measure(g, text, font), y);
        }

        internal static void DrawCentered(Graphics g, string text, Font font, Color color, float center, float y)
        {
            if (string.IsNullOrEmpty(text)) return;
            Draw(g, text, font, color, center - Measure(g, text, font) / 2f, y);
        }

        internal static float Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            return g.MeasureString(text, font, new SizeF(100000f, 10000f), StringFormat.GenericTypographic).Width;
        }

        internal static float DrawCount(Graphics g, Fonts f, string count, float x, float y, Color color)
        {
            string number = count;
            string suffix = "";
            int cut = SuffixIndex(count);
            if (cut > 0) { number = count.Substring(0, cut); suffix = count.Substring(cut); }
            float cx = x + Draw(g, number, f.HeadlineNum, color, x, y);
            if (suffix.Length > 0)
            {
                Draw(g, suffix, f.HeadlineCjkSmall, color, cx + 4f, y + 6f);
                cx += Measure(g, suffix, f.HeadlineCjkSmall) + 4f;
            }
            return cx - x;
        }

        private static int SuffixIndex(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            int wan = text.IndexOf('万');
            int yi = text.IndexOf('亿');
            if (wan >= 0 && yi >= 0) return Math.Min(wan, yi);
            return wan >= 0 ? wan : yi;
        }

        private static void SplitCombo(string text, out string key, out string count)
        {
            key = text;
            count = "";
            if (string.IsNullOrEmpty(text)) return;
            int cut = text.LastIndexOf(' ');
            if (cut <= 0 || cut >= text.Length - 1) return;
            string tail = text.Substring(cut + 1);
            bool numeric = tail.Length > 0;
            for (int i = 0; i < tail.Length; i++)
            {
                if (!char.IsDigit(tail[i]) && tail[i] != ',' && tail[i] != '.') { numeric = false; break; }
            }
            if (!numeric) return;
            key = text.Substring(0, cut);
            count = tail;
        }

        private static string JoinTop(List<string> items, int count)
        {
            if (items == null || items.Count == 0) return "—";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < items.Count && i < count; i++)
            {
                if (text.Length > 0) text.Append(" · ");
                text.Append(items[i]);
            }
            return text.ToString();
        }

        private static string JoinMaskedTop(List<string> items, int count)
        {
            if (items == null || items.Count == 0) return "—";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < items.Count && i < count; i++)
            {
                if (text.Length > 0) text.Append(" · ");
                string entry = items[i] ?? "";
                int cut = entry.Length;
                while (cut > 0)
                {
                    char c = entry[cut - 1];
                    if (!char.IsDigit(c) && c != ',' && c != '.') break;
                    cut--;
                }
                string label = entry.Substring(0, cut);
                string tail = entry.Substring(cut);
                text.Append(PrivacyMask.MaskText(label)).Append(tail);
            }
            return text.ToString();
        }

        internal static string Fit(Graphics g, Font font, string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (Measure(g, text, font) <= maxWidth) return text;
            string value = text;
            while (value.Length > 1 && Measure(g, value + "…", font) > maxWidth)
                value = value.Substring(0, value.Length - 1);
            return value + "…";
        }

        internal static List<string> Wrap(Graphics g, Font font, string text, float maxWidth)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    if (line.Length > 0) lines.Add(line.ToString());
                    line.Length = 0;
                    continue;
                }
                line.Append(c);
                if (line.Length > 1 && Measure(g, line.ToString(), font) > maxWidth)
                {
                    char last = line[line.Length - 1];
                    line.Length = line.Length - 1;
                    if (line.Length > 0) lines.Add(line.ToString());
                    line.Length = 0;
                    line.Append(last);
                }
            }
            if (line.Length > 0) lines.Add(line.ToString());
            return lines;
        }

        internal static void HLine(Graphics g, float x, float y, float w, Color color)
        {
            using (Pen pen = new Pen(color, 1f)) g.DrawLine(pen, x, y, x + w, y);
        }

        internal static void VLine(Graphics g, float x, float y1, float y2, Color color)
        {
            using (Pen pen = new Pen(color, 1f)) g.DrawLine(pen, x, y1, x, y2);
        }

        internal static void FillDot(Graphics g, float x, float y, float size, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, x, y, size, size);
        }

        internal static void Pill(Graphics g, float x, float y, float w, float h, string text,
            Font font, Color color, int fillAlpha, int borderAlpha, Color accent)
        {
            using (GraphicsPath path = Rounded(x, y, w, h, h / 2f))
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(fillAlpha, accent))) g.FillPath(fill, path);
                using (Pen border = new Pen(Color.FromArgb(borderAlpha, accent), 1f)) g.DrawPath(border, path);
            }
            DrawCentered(g, text, font, color, x + w / 2f, y + (h - 16f) / 2f);
        }

        internal static GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            if (w <= 0.5f || h <= 0.5f) return path;
            if (r > Math.Min(w, h) / 2f) r = Math.Min(w, h) / 2f;
            if (r < 0.5f) r = 0.5f;
            path.AddArc(x, y, r * 2f, r * 2f, 180f, 90f);
            path.AddArc(x + w - r * 2f, y, r * 2f, r * 2f, 270f, 90f);
            path.AddArc(x + w - r * 2f, y + h - r * 2f, r * 2f, r * 2f, 0f, 90f);
            path.AddArc(x, y + h - r * 2f, r * 2f, r * 2f, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        internal static int WeekOfYear(DateTime day)
        {
            try
            {
                return CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(day,
                    CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            }
            catch (Exception) { return 1; }
        }

        internal static string ShortDuration(double seconds)
        {
            if (seconds < 60) return Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture) + "s";
            if (seconds < 3600) return Math.Round(seconds / 60.0).ToString("0", CultureInfo.InvariantCulture) + "m";
            return (seconds / 3600.0).ToString("0.0", CultureInfo.InvariantCulture) + "h";
        }

        // ---------------------------------------------------------------- 字体

        private static readonly string Sans = PickFamily("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Arial");
        private static readonly string SerifLatin = PickFamily("Georgia", "Times New Roman", "Cambria", "Arial");
        private static readonly string SerifCjk = PickFamily("SimSun", "宋体", "NSimSun", "Microsoft YaHei");

        private static string PickFamily(params string[] names)
        {
            try
            {
                FontFamily[] families = FontFamily.Families;
                for (int i = 0; i < names.Length; i++)
                {
                    for (int k = 0; k < families.Length; k++)
                    {
                        if (string.Equals(families[k].Name, names[i], StringComparison.OrdinalIgnoreCase))
                            return families[k].Name;
                    }
                }
            }
            catch (Exception) { }
            return FontFamily.GenericSansSerif.Name;
        }

        private static Font MakeFont(string family, float size, FontStyle style)
        {
            try { return new Font(family, size, style, GraphicsUnit.Pixel); }
            catch (Exception)
            {
                try { return new Font("Microsoft Sans Serif", size, style, GraphicsUnit.Pixel); }
                catch (Exception) { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel); }
            }
        }

        internal sealed class Fonts : IDisposable
        {
            internal readonly Font Brand = MakeFont(Sans, 9f, FontStyle.Regular);
            internal readonly Font Eyebrow = MakeFont(Sans, 10.5f, FontStyle.Regular);
            internal readonly Font DateBig = MakeFont(SerifLatin, 86f, FontStyle.Regular);
            internal readonly Font DateMonth = MakeFont(Sans, 15f, FontStyle.Bold);
            internal readonly Font DateSub = MakeFont(Sans, 10.5f, FontStyle.Regular);
            internal readonly Font Status = MakeFont(Sans, 10.5f, FontStyle.Bold);
            internal readonly Font Headline = MakeFont(Sans, 43f, FontStyle.Bold);
            internal readonly Font HeadlineNum = MakeFont(SerifLatin, 50f, FontStyle.Regular);
            internal readonly Font HeadlineCjk = MakeFont(SerifCjk, 42f, FontStyle.Regular);
            internal readonly Font HeadlineCjkSmall = MakeFont(SerifCjk, 36f, FontStyle.Regular);
            internal readonly Font Lede = MakeFont(Sans, 13f, FontStyle.Regular);
            internal readonly Font MetricLabel = MakeFont(Sans, 10.5f, FontStyle.Regular);
            internal readonly Font MetricLabelEn = MakeFont(Sans, 8f, FontStyle.Regular);
            internal readonly Font MetricValue = MakeFont(SerifLatin, 38f, FontStyle.Regular);
            internal readonly Font MetricValueCjk = MakeFont(SerifCjk, 33f, FontStyle.Regular);
            internal readonly Font MetricUnit = MakeFont(Sans, 12f, FontStyle.Regular);
            internal readonly Font Delta = MakeFont(Sans, 10.5f, FontStyle.Bold);
            internal readonly Font Micro = MakeFont(Sans, 9.5f, FontStyle.Regular);
            internal readonly Font SectionTitle = MakeFont(Sans, 15f, FontStyle.Bold);
            internal readonly Font SectionSub = MakeFont(Sans, 10.5f, FontStyle.Regular);
            internal readonly Font Ghost = MakeFont(SerifLatin, 34f, FontStyle.Regular);
            internal readonly Font AppRank = MakeFont(SerifLatin, 10.5f, FontStyle.Regular);
            internal readonly Font AppMark = MakeFont(Sans, 10.5f, FontStyle.Bold);
            internal readonly Font AppName = MakeFont(Sans, 12f, FontStyle.Regular);
            internal readonly Font AppTime = MakeFont(Sans, 11.5f, FontStyle.Regular);
            internal readonly Font AppPct = MakeFont(Sans, 10f, FontStyle.Regular);
            internal readonly Font Note = MakeFont(Sans, 12.5f, FontStyle.Regular);
            internal readonly Font Quote = MakeFont(SerifCjk, 19f, FontStyle.Regular);
            internal readonly Font HabitKey = MakeFont(Sans, 11.5f, FontStyle.Regular);
            internal readonly Font HabitCount = MakeFont(SerifLatin, 13.5f, FontStyle.Regular);
            internal readonly Font InlineBig = MakeFont(SerifLatin, 34f, FontStyle.Regular);
            internal readonly Font InlineUnit = MakeFont(Sans, 9.5f, FontStyle.Regular);
            internal readonly Font StatValue = MakeFont(SerifLatin, 21f, FontStyle.Regular);
            internal readonly Font StatLabel = MakeFont(Sans, 9.5f, FontStyle.Regular);
            internal readonly Font Footer = MakeFont(Sans, 9f, FontStyle.Regular);
            internal readonly Font Chart = MakeFont(Sans, 10f, FontStyle.Regular);
            internal readonly Font ChartSmall = MakeFont(Sans, 9f, FontStyle.Regular);
            internal readonly Font Annotation = MakeFont(Sans, 10f, FontStyle.Regular);
            internal readonly Font Legend = MakeFont(Sans, 10f, FontStyle.Regular);

            public void Dispose()
            {
                Font[] all =
                {
                    Brand, Eyebrow, DateBig, DateMonth, DateSub, Status, Headline, HeadlineNum,
                    HeadlineCjk, HeadlineCjkSmall, Lede, MetricLabel, MetricLabelEn, MetricValue,
                    MetricValueCjk, MetricUnit, Delta, Micro, SectionTitle, SectionSub, Ghost,
                    AppRank, AppMark, AppName, AppTime, AppPct, Note, Quote, HabitKey, HabitCount,
                    InlineBig, InlineUnit, StatValue, StatLabel, Footer, Chart, ChartSmall,
                    Annotation, Legend
                };
                for (int i = 0; i < all.Length; i++)
                {
                    try { all[i].Dispose(); } catch (Exception) { }
                }
            }
        }
    }
}
