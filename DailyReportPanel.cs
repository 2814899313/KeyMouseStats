using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace KeyMouseStats
{
    // ============================================================================
    //  第 8 页:日报
    //
    //  一天一张卡片式的简报。数据全部来自 DailyReport(它自己读 stats.txt 与 Rime 日志),
    //  这一层只负责把它画好看:左侧导航、顶部一句话、时段/应用/习惯/字符/提醒五张卡。
    // ============================================================================
    internal sealed partial class Dashboard
    {
        private DailyReport _dayReport;
        private DateTime _dayShown = DateTime.MinValue;
        private RectangleF _dayPrevRect, _dayNextRect;
        private readonly List<RectangleF> _dayHoverRects = new List<RectangleF>();
        private readonly List<string> _dayHoverTexts = new List<string>();

        private static readonly string[] WeekNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

        /// <summary>默认看昨天:日报的意义就是「昨天过得怎么样」。</summary>
        private DateTime DefaultDay { get { return DateTime.Today.AddDays(-1); } }

        private void EnsureDayReport()
        {
            if (_dayShown == DateTime.MinValue) _dayShown = DefaultDay;
            if (_dayReport != null && _dayReport.Day == _dayShown) return;
            _dayReport = DailyReport.Build(_dayShown);
        }

        private void ApplyDayChip(int id)
        {
            if (id == 420) { _dayShown = _dayShown.AddDays(-1); _dayReport = null; Motion.Start(EnterMotionKey, 240, Ease.OutCubic); return; }
            if (id == 421)
            {
                DateTime next = _dayShown.AddDays(1);
                if (next > DateTime.Today) return;
                _dayShown = next; _dayReport = null;
                Motion.Start(EnterMotionKey, 240, Ease.OutCubic);
                return;
            }
            if (id == 422) { _dayShown = DefaultDay; _dayReport = null; return; }
            if (id == 423) { SaveDayReport(); return; }
            if (id == 424) { CopyDayReport(); return; }
        }

        private void SaveDayReport()
        {
            EnsureDayReport();
            string path = _dayReport.Save();
            if (path.Length > 0) SetStatus("已保存：" + path);
            else ThemeMessage.Show(this, "写文件失败，检查数据目录权限。", "日报保存失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void CopyDayReport()
        {
            EnsureDayReport();
            try
            {
                Clipboard.SetText(DailyReport.ToMarkdown(_dayReport));
                SetStatus("日报已复制到剪贴板");
            }
            catch (Exception) { SetStatus("剪贴板不可用"); }
        }

        // ---------------------------------------------------------------- 绘制

        private void PaintDay(Graphics g)
        {
            EnsureDayReport();
            DailyReport r = _dayReport;
            _dayHoverRects.Clear();
            _dayHoverTexts.Clear();

            PaintDayHero(g, r);

            float row2 = Cy + 140;
            PaintDayHours(g, Cx, row2, 410, 160, r);
            PaintDayApps(g, Cx + 424, row2, 408, 160, r);

            float row3 = Cy + 314;
            PaintDayHabits(g, Cx, row3, 268, 174, r);
            PaintDayChars(g, Cx + 282, row3, 268, 174, r);
            PaintDayReminders(g, Cx + 564, row3, 268, 174, r);
        }

        // ---- 顶部:日期 + 一句话 + 四个关键数字 ----
        private void PaintDayHero(Graphics g, DailyReport r)
        {
            float x = Cx, y = Cy, w = Cw, h = 128;
            PaintCardBase(g, x, y, w, h);

            string status = r.InProgress ? "进行中" : r.HasData ? "已结束" : "无数据";
            using (SolidBrush title = new SolidBrush(Ctext))
                g.DrawString(r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "  " + WeekNames[(int)r.Day.DayOfWeek],
                    _fH2, title, x + 18, y + 10);

            // 状态小标签
            SizeF titleSize = g.MeasureString(r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "  " + WeekNames[(int)r.Day.DayOfWeek], _fH2);
            RectangleF pill = new RectangleF(x + 24 + titleSize.Width, y + 9, 52, 18);
            using (GraphicsPath path = RoundedRect(pill.X, pill.Y, pill.Width, pill.Height, 9))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(r.HasData ? 40 : 24, r.HasData ? Cgreen : Csub)))
                g.FillPath(fill, path);
            AppText(g, status, _fAxis, r.HasData ? Cgreen : Csub, pill, true);

            // 前后一天
            _dayPrevRect = new RectangleF(x + w - 76, y + 9, 26, 22);
            _dayNextRect = new RectangleF(x + w - 44, y + 9, 26, 22);
            PaintDayArrow(g, _dayPrevRect, false);
            PaintDayArrow(g, _dayNextRect, true);

            // 对比口径放在标题行，摘要与数字各自占用固定区域。
            AppText(g, r.BaseDays > 0 ? "较" + r.BaselineLabel : "暂无对比基准", _fAxis, Csub,
                new RectangleF(x + 380, y + 12, w - 480, 16), true);

            // 一句话(最多两行)
            List<string> lines = WrapText(g, r.Headline, _fBody, w - 36);
            for (int i = 0; i < lines.Count && i < 2; i++)
                AppText(g, lines[i], _fBody, Ctext, new RectangleF(x + 18, y + 35 + i * 16, w - 36, 16), false);

            // 四个关键数字
            float metricY = y + 74;
            float metricW = (w - 36) / 4f;
            using (Pen divider = new Pen(Cline))
                for (int i = 1; i < 4; i++)
                    g.DrawLine(divider, x + 8 + metricW * i, metricY + 2, x + 8 + metricW * i, y + h - 14);
            DrawDayMetric(g, x + 18, metricY, metricW, "击键", Analysis.FmtCount(r.Keys), r.Keys, r.BaseKeys);
            DrawDayMetric(g, x + 18 + metricW, metricY, metricW, "点击", Analysis.FmtCount(r.Clicks), r.Clicks, r.BaseClicks);
            DrawDayMetric(g, x + 18 + metricW * 2, metricY, metricW, "有效使用", ActivityMonitor.FormatDuration(r.ActiveSeconds), r.ActiveSeconds, r.BaseActiveSeconds);
            DrawDayMetric(g, x + 18 + metricW * 3, metricY, metricW, "上屏", r.HasChars ? Analysis.FmtCount(r.Chars) + " 字" : "—", r.Chars, r.BaseChars);
        }

        private void PaintDayArrow(Graphics g, RectangleF rect, bool right)
        {
            PointF p = ToBase(_mouse);
            bool enabled = !right || _dayShown < DateTime.Today;
            bool hov = enabled && rect.Contains(new PointF(p.X - ContentX, p.Y - ContentY));
            using (GraphicsPath path = RoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 7))
            using (SolidBrush fill = new SolidBrush(hov ? Ccard2 : Color.FromArgb(20, Ctext))) g.FillPath(fill, path);
            using (Pen pen = new Pen(enabled ? (hov ? Ctext : Csub) : Color.FromArgb(65, Csub), 1.6f))
            {
                float cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2;
                float dir = right ? 1 : -1;
                g.DrawLine(pen, cx - 3 * dir, cy - 5, cx + 2 * dir, cy);
                g.DrawLine(pen, cx + 2 * dir, cy, cx - 3 * dir, cy + 5);
            }
            if (!enabled) return;
            _chips.Add(rect);
            _chipIds.Add(right ? 421 : 420);
        }

        private void DrawDayMetric(Graphics g, float x, float y, float w, string label, string value, double current, double baseValue)
        {
            AppText(g, label, _fAxis, Csub, new RectangleF(x, y, 70, 16), false);
            string delta = DeltaText(current, baseValue);
            AppText(g, delta, _fAxis, delta.StartsWith("▲", StringComparison.Ordinal) ? Cblue : Csub,
                new RectangleF(x + 72, y, w - 92, 16), true);
            Font font = g.MeasureString(value, _fNum2).Width <= w - 22 ? _fNum2 : _fH2;
            AppText(g, value, font, Ctext, new RectangleF(x, y + 18, w - 22, 26), false);
        }
        /// <summary>与前 N 天日均的变化。基准为 0 或没有基准时返回空串(页面不显示,而不是假装持平)。</summary>
        private static string DeltaText(double current, double baseValue)
        {
            if (baseValue <= 0) return "";
            double ratio = current / baseValue - 1;
            if (Math.Abs(ratio) < 0.005) return "持平";
            return (ratio > 0 ? "▲" : "▼") + Math.Abs(ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>应用卡里的紧凑时长:56 px 装不下「59 分 45 秒」。</summary>
        private static string ShortDuration(double seconds)
        {
            if (seconds < 60) return ((int)Math.Round(seconds)).ToString(CultureInfo.InvariantCulture) + " 秒";
            if (seconds < 3600) return ((int)Math.Round(seconds / 60)).ToString(CultureInfo.InvariantCulture) + " 分";
            return (seconds / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " 时";
        }

        // ---- 时段分布 ----
        private void PaintDayHours(Graphics g, float x, float y, float w, float h, DailyReport r)
        {
            PaintCard(g, x, y, w, h, "时间分布");
            if (!r.HasData)
            {
                AppText(g, "这一天没有记录", _fSmall, Csub, new RectangleF(x + 18, y + h / 2 - 8, w - 36, 20), false);
                return;
            }

            long max = 1;
            for (int i = 0; i < 24; i++) if (r.HourKeys[i] > max) max = r.HourKeys[i];

            float left = x + 22, right = x + w - 22, top = y + 44, bottom = y + h - 40;
            float slot = (right - left) / 24f, plotHeight = bottom - top;
            for (int hour = 0; hour < 24; hour++)
            {
                long value = r.HourKeys[hour];
                float barHeight = (float)(value / (double)max) * plotHeight;
                float bx = left + slot * hour + slot * 0.16f;
                float bw = Math.Max(3f, slot * 0.68f);
                if (value <= 0)
                {
                    using (SolidBrush empty = new SolidBrush(Color.FromArgb(22, Csub)))
                        g.FillRectangle(empty, bx, bottom - 2, bw, 2);
                }
                else
                {
                    if (barHeight < 2f) barHeight = 2f;
                    Color fill = hour == r.BusiestHour ? Cblue : Color.FromArgb(150, Cblue);
                    using (GraphicsPath path = RoundedRect(bx, bottom - barHeight, bw, barHeight, 3))
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                }
                if (hour % 3 == 0)
                {
                    using (SolidBrush brush = new SolidBrush(Csub))
                    {
                        string label = hour.ToString(CultureInfo.InvariantCulture);
                        SizeF size = g.MeasureString(label, _fAxis);
                        g.DrawString(label, _fAxis, brush, bx + (bw - size.Width) / 2, bottom + 4);
                    }
                }
            }

            string peak = r.BusiestHour >= 0
                ? "最忙 " + r.BusiestHour + " 时 · " + Analysis.FmtCount(r.BusiestHourKeys) + " 次"
                : "";
            if (peak.Length > 0)
            {
                // 贴在标题那一行的右侧,别压在卡片标题上。
                SizeF peakSize = g.MeasureString(peak, _fSmall);
                AppText(g, peak, _fSmall, Ctext, new RectangleF(x + w - 18 - peakSize.Width, y + 12, peakSize.Width, 16), false);
            }
            AppText(g, "最长连续 " + ActivityMonitor.FormatDuration(r.LongestSessionSeconds) + " · 共 " + r.SessionCount + " 段",
                _fAxis, Csub, new RectangleF(x + 18, y + h - 22, w - 36, 14), false);
        }

        // ---- 应用 ----
        private void PaintDayApps(Graphics g, float x, float y, float w, float h, DailyReport r)
        {
            PaintCard(g, x, y, w, h, "应用排行");
            if (r.Apps.Count == 0)
            {
                AppText(g, "没有应用归因数据", _fSmall, Csub, new RectangleF(x + 18, y + h / 2 - 8, w - 36, 20), false);
                return;
            }

            AppText(g, "使用时长 · TOP 5", _fAxis, Csub, new RectangleF(x + 180, y + 12, w - 198, 16), true);
            double max = 1;
            for (int i = 0; i < r.Apps.Count; i++) if (r.Apps[i].Seconds > max) max = r.Apps[i].Seconds;

            float top = y + 38, rowHeight = Math.Min(22f, (h - 48) / r.Apps.Count);
            for (int i = 0; i < r.Apps.Count; i++)
            {
                float ry = top + i * rowHeight;
                AppText(g, r.Apps[i].Name, _fSmall, i == 0 ? Ctext : Csub, new RectangleF(x + 38, ry, 132, rowHeight), false);
                AppText(g, (i + 1).ToString(CultureInfo.InvariantCulture), _fAxis, i == 0 ? Cblue : Csub,
                    new RectangleF(x + 18, ry, 18, rowHeight), false);
                float barLeft = x + 178, barMax = w - 178 - 76;
                using (GraphicsPath track = RoundedRect(barLeft, ry + rowHeight / 2 - 3, barMax, 6, 3))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(24, Csub))) g.FillPath(brush, track);
                float length = (float)(r.Apps[i].Seconds / max) * barMax;
                if (length < 2f) length = 2f;
                using (GraphicsPath path = RoundedRect(barLeft, ry + rowHeight / 2 - 3, length, 6, 3))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(i == 0 ? 210 : 110, Cblue))) g.FillPath(brush, path);
                AppText(g, ShortDuration(r.Apps[i].Seconds), _fAxis, Csub,
                    new RectangleF(x + w - 70, ry, 56, rowHeight), true);
            }
        }

        // ---- 操作习惯 ----
        private void PaintDayHabits(Graphics g, float x, float y, float w, float h, DailyReport r)
        {
            PaintCard(g, x, y, w, h, "操作习惯");
            if (!r.HasData)
            {
                AppText(g, "这一天没有记录", _fSmall, Csub, new RectangleF(x + 18, y + h / 2 - 8, w - 36, 20), false);
                return;
            }
            float ry = y + 38;
            if (r.Combos.Count > 0)
            {
                AppText(g, "组合键 Top", _fAxis, Csub, new RectangleF(x + 18, ry, w - 36, 14), false);
                ry += 17;
                for (int i = 0; i < r.Combos.Count && i < 4; i++)
                {
                    AppText(g, r.Combos[i], _fSmall, Ctext, new RectangleF(x + 18, ry, w - 36, 16), false);
                    ry += 17;
                }
            }
            if (r.TopKeys.Count > 0)
            {
                AppText(g, "按键 Top：" + string.Join(" · ", r.TopKeys.GetRange(0, Math.Min(3, r.TopKeys.Count)).ToArray()),
                    _fAxis, Csub, new RectangleF(x + 18, y + h - 43, w - 36, 16), false);
            }
            AppText(g, "鼠标 " + Analysis.FmtMeters(r.MoveMeters) + " · 峰值 " + Analysis.FmtCount(r.PeakApm) + " APM",
                _fAxis, Csub, new RectangleF(x + 18, y + h - 24, w - 36, 16), false);
        }

        // ---- 字符内容 ----
        private void PaintDayChars(Graphics g, float x, float y, float w, float h, DailyReport r)
        {
            PaintCard(g, x, y, w, h, "字符内容");
            if (!r.HasChars)
            {
                AppText(g, "这一天没有上屏记录", _fSmall, Csub, new RectangleF(x + 18, y + h / 2 - 8, w - 36, 20), false);
                AppText(g, "（需要 Rime 的 commit_log.csv）", _fAxis, Csub, new RectangleF(x + 18, y + h / 2 + 12, w - 36, 14), false);
                return;
            }

            AppText(g, Analysis.FmtCount(r.Chars) + " 字", _fNum2, Ctext, new RectangleF(x + 18, y + 35, 120, 26), false);
            AppText(g, Analysis.FmtCount(r.Commits) + " 次 · 均 " + r.AvgCharsPerCommit.ToString("0.0", CultureInfo.InvariantCulture) + " 字 · 不同字 "
                + Analysis.FmtCount(r.DistinctChars) + " 种",
                _fAxis, Csub, new RectangleF(x + 18, y + 62, w - 36, 16), false);

            // 构成条
            long total = 0;
            for (int i = 0; i < r.Composition.Length; i++) total += r.Composition[i];
            float barY = y + 84, barW = w - 36;
            if (total > 0)
            {
                float bx = x + 18;
                for (int i = 0; i < r.Composition.Length; i++)
                {
                    if (r.Composition[i] <= 0) continue;
                    float segW = (float)(r.Composition[i] / (double)total) * barW;
                    using (SolidBrush brush = new SolidBrush(CompositionColor(i)))
                        g.FillRectangle(brush, bx, barY, Math.Max(1f, segW - 1), 8);
                    bx += segW;
                }
                string legend = "";
                for (int i = 0; i < r.Composition.Length; i++)
                {
                    if (r.Composition[i] <= 0) continue;
                    if (legend.Length > 0) legend += " · ";
                    legend += RimeSlice.CompositionNames[i] + " " + (r.Composition[i] * 100.0 / total).ToString("0", CultureInfo.InvariantCulture) + "%";
                }
                List<string> legendLines = WrapText(g, legend, _fAxis, barW);
                for (int i = 0; i < legendLines.Count && i < 2; i++)
                    AppText(g, legendLines[i], _fAxis, Csub, new RectangleF(x + 18, barY + 12 + i * 14, barW, 14), false);
            }

            if (r.TopChars.Count > 0)
                AppText(g, "高频：" + string.Join(" · ", r.TopChars.GetRange(0, Math.Min(3, r.TopChars.Count)).ToArray()),
                    _fAxis, Csub, new RectangleF(x + 18, y + h - 40, w - 36, 14), false);
            AppText(g, "最活跃 " + (r.BusiestCharHour >= 0 ? r.BusiestCharHour + " 时" : "—")
                + " · 连续 " + r.LongestBurstMinutes + " 分钟", _fAxis, Csub,
                new RectangleF(x + 18, y + h - 22, w - 36, 14), false);
        }

        // ---- 提醒 ----
        private void PaintDayReminders(Graphics g, float x, float y, float w, float h, DailyReport r)
        {
            PaintCard(g, x, y, w, h, "值得注意");
            float ry = y + 38;
            float bottom = y + h - 12;
            for (int i = 0; i < r.Reminders.Count && ry + 16 <= bottom; i++)
            {
                List<string> lines = WrapText(g, r.Reminders[i], _fSmall, w - 48);
                bool good = r.Reminders[i].IndexOf("达成", StringComparison.Ordinal) >= 0
                    || r.Reminders[i].IndexOf("平稳", StringComparison.Ordinal) >= 0;
                using (SolidBrush dot = new SolidBrush(good ? Cgreen : Corange))
                    g.FillEllipse(dot, x + 18, ry + 6, 4, 4);
                int available = (int)((bottom - ry) / 16);
                int count = Math.Min(lines.Count, available);
                for (int k = 0; k < count; k++)
                {
                    string line = k == count - 1 && count < lines.Count
                        ? string.Join("", lines.GetRange(k, lines.Count - k).ToArray()) : lines[k];
                    AppText(g, line, _fSmall, Ctext, new RectangleF(x + 30, ry, w - 48, 16), false);
                    ry += 16;
                }
                ry += 6;
            }
        }
        // ---------------------------------------------------------------- 每日自动生成

        /// <summary>
        /// 每天第一次打开面板时,把「昨天」的日报生成出来并落盘。
        /// 用数据文件里的一行日期去重:同一天重开面板不会再写一遍,也不会把用户手改过的文件覆盖掉。
        /// </summary>
        private void TryGenerateDailyReport()
        {
            try
            {
                if (Store.DailyReportDay.Date >= DateTime.Today) return;
                DailyReport report = DailyReport.Build(DateTime.Today.AddDays(-1));
                string path = report.Save();
                Store.DailyReportDay = DateTime.Today;
                Store.Save();
                if (path.Length > 0) SetStatus("昨日日报已生成:" + System.IO.Path.GetFileName(path));
            }
            catch (Exception) { /* 日报是附加价值,不能影响面板打开 */ }
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>页脚一句话:看的是哪一天、跟谁比、存到哪。</summary>
        private string DayFooterHint()
        {
            EnsureDayReport();
            DailyReport r = _dayReport;
            if (r == null) return "日报 · 正在生成";
            string hint = "日报 · " + r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            hint += r.HasData ? (r.InProgress ? " · 进行中" : " · 完整") : " · 无数据";
            if (r.BaseDays > 0) hint += " · 对比" + r.BaselineLabel;
            hint += " · 保存为 " + DailyReport.Directory + "\\" + r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md";
            return hint;
        }

        /// <summary>按像素宽度折行(中文按字断,英文不拆词太麻烦,这里一律按字断)。</summary>
        private static List<string> WrapText(Graphics g, string text, Font font, float maxWidth)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text) || maxWidth <= 20) { if (!string.IsNullOrEmpty(text)) lines.Add(text); return lines; }
            int start = 0;
            while (start < text.Length)
            {
                int length = 1;
                while (start + length < text.Length &&
                       g.MeasureString(text.Substring(start, length + 1), font).Width <= maxWidth)
                    length++;
                lines.Add(text.Substring(start, length));
                start += length;
            }
            return lines;
        }
    }
}
