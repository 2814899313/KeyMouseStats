using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
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
        private bool _dayReportArchived;
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
            _dayReportArchived = _dayReport != null && File.Exists(_dayReport.FilePath);
        }

        /// <summary>切到日报页/换日期时调用;绘制路径只消费已建好的报告。</summary>
        internal void PrepareDayPage()
        {
            EnsureDayReport();
        }

        private void ApplyDayChip(int id)
        {
            if (id == 425) { ToggleBattleMask(); return; }
            if (id == 426) { ShowReportExportMenu(); return; }
            if (id == 420)
            {
                if (!CanStepDay(-1)) return;      // 到 364 天下界就停,不再翻进没有数据的年份
                _dayShown = _dayShown.AddDays(-1); _dayReport = null; Motion.Start(EnterMotionKey, 240, Ease.OutCubic); return;
            }
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
            _dayReportArchived = path.Length > 0;
            if (path.Length > 0) SetStatus("已保存至本地日报目录");
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
            DailyReport r = _dayReport;
            if (r == null) return;
            _dayHoverRects.Clear();
            _dayHoverTexts.Clear();

            PaintDayHero(g, r);

            float row2 = Cy + 140;
            // 3.0:第二排高度也跟随 ElasticH(基准 160 不变);逻辑画布矮于设计最小时,
            // 它是负数,第二排压缩后第三排才收口得进 PageBottom。
            float row2H = Math.Max(110f, 160f + ElasticH * 0.12f);
            float dayLeft = Cw * 0.493f; float dayGap = 14f; PaintDayHours(g, Cx, row2, dayLeft, row2H, r);
            PaintDayApps(g, Cx + dayLeft + dayGap, row2, Cw - dayLeft - dayGap, row2H, r);

            // 第三排等宽三列:Cw=832 时正好 268 宽、x=24/306/588,与旧版逐像素一致;
            // 放大到 2K 时三张卡一起长,不再让 0.678 的比例在卡 2 与卡 3 之间留出 1000px 的黑洞。
            // 底边用 PageBottom(已扣掉页眉 ContentY 与页脚块):Cy + Cb 会越过窗口底 82px,
            // 基准窗口下第三排的按键/鼠标两行会被页脚按钮压住。
            float row3 = row2 + row2H + 14f;
            float row3H = Math.Max(56f, PageBottom - row3);
            RectangleF[] row3Cols = LayoutFrame.SplitColumns(
                new RectangleF(Cx, row3, Cw, row3H), new float[] { 1f, 1f, 1f }, 14f);
            PaintDayHabits(g, row3Cols[0].X, row3, row3Cols[0].Width, row3H, r);
            PaintDayChars(g, row3Cols[1].X, row3, row3Cols[1].Width, row3H, r);
            PaintDayReminders(g, row3Cols[2].X, row3, row3Cols[2].Width, row3H, r);
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

            // 前后一天:与全站步进器同一套几何(h30 / r8),基于 Cx+Cw 相对定位。
            // 旧版是 26×22 的私有小方块、radius 7、无描边,而且左箭头永远 enable —— 可以一直翻到没有数据的年份。
            _dayPrevRect = new RectangleF(x + w - 78, y + 8, StepChipHeight, StepChipHeight);
            _dayNextRect = new RectangleF(x + w - 40, y + 8, StepChipHeight, StepChipHeight);
            PaintStepArrow(g, _dayPrevRect, false, CanStepDay(-1), 420);
            PaintStepArrow(g, _dayNextRect, true, CanStepDay(1), 421);

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

        /// <summary>日报能翻到的最早一天:与「使用洞察」的 364 天窗口、页脚「本地记录 365 天」对齐。</summary>
        internal const int DayHistoryDays = 364;

        /// <summary>这一天是否可翻。到边界时按钮灰化且不登记命中区,不会再出现「点了没反应」。</summary>
        internal bool CanStepDay(int delta)
        {
            DateTime next = _dayShown.AddDays(delta);
            return next >= DateTime.Today.AddDays(-DayHistoryDays) && next <= DateTime.Today;
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
                // 每个小时都保留极淡的底槽，空时段也不会让横轴断开。
                using (GraphicsPath emptyTrack = RoundedRect(bx, top, bw, plotHeight, 3))
                using (SolidBrush empty = new SolidBrush(Color.FromArgb(13, Csub)))
                    g.FillPath(empty, emptyTrack);
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
                AppText(g, (i + 1).ToString(CultureInfo.InvariantCulture), _fAxis, i == 0 ? Cblue : Csub,
                    new RectangleF(x + 18, ry, 18, rowHeight), false);
                Bitmap icon = AppIcon(r.Apps[i].ProcessPath);
                if (icon != null)
                    g.DrawImage(icon, x + 36, ry + Math.Max(0, (rowHeight - 16) / 2), 16, 16);
                else
                {
                    using (GraphicsPath iconPath = RoundedRect(x + 36, ry + Math.Max(0, (rowHeight - 16) / 2), 16, 16, 4))
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(i == 0 ? 55 : 30, Cblue)))
                        g.FillPath(fill, iconPath);
                }
                AppText(g, r.Apps[i].Name, _fSmall, i == 0 ? Ctext : Csub, new RectangleF(x + 56, ry, 116, rowHeight), false);
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
            // 榜单从「组合键 Top」标签下沿开始,底部两行固定摘要的位置一个像素都不动。
            float listTop = y + 38f;
            int total = r.Combos.Count + r.TopKeys.Count;
            if (total > 0)
                AppText(g, r.Combos.Count > 0 ? "组合键 Top" : "按键 Top", _fAxis, Csub,
                    new RectangleF(x + 18, listTop, w - 36, 14), false);
            listTop += 17f;

            // 3.0 内容密度:中段能排几条由可用高度反推,而不是写死 4 条(基准 260 高时能排 9 行),
            // 2K 上把「组合键 5 + 按键 5」十条全铺开,不再「四条排完,下面一大片黑」。
            float pinnedH = (r.TopKeys.Count > 0 ? 43f : 24f) + 6f;   // 摘要两行 + 净空
            float body = h - (listTop - y) - pinnedH;
            if (body < 17f) body = 17f;
            int rows = LayoutFrame.CalcVisibleRows(body, 0f, 0f, 17f, 30f, total);
            float rowH = LayoutFrame.CalcRowHeight(body, 0f, 0f, 17f, 30f, rows);
            if (rowH < 17f) rowH = 17f;
            // 行高封顶 30(2K 上不把 10 条摊成 90px 的稀疏表格);条数排不满时把余量
            // 均摊进行距,让中段整块被填满,而不是贴着标签堆在顶部。
            float pitch = rowH;
            if (rows > 0 && body > rows * rowH) pitch = body / rows;
            for (int i = 0; i < rows; i++)
            {
                // 先列组合键,再接按键;两者合在同一条榜里,顺序与卡底摘要一致。
                string text = i < r.Combos.Count ? r.Combos[i] : r.TopKeys[i - r.Combos.Count];
                float ry = listTop + i * pitch + (pitch - rowH) / 2f;
                AppText(g, text, _fSmall, Ctext, new RectangleF(x + 18, ry, w - 36, rowH), false);
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
            PaintCard(g, x, y, w, h, "字符构成");
            if (!r.HasChars)
            {
                AppText(g, "这一天没有上屏记录", _fSmall, Csub, new RectangleF(x + 18, y + h / 2 - 8, w - 36, 20), false);
                AppText(g, "（需要 Rime 的 commit_log.csv）", _fAxis, Csub, new RectangleF(x + 18, y + h / 2 + 12, w - 36, 14), false);
                return;
            }

            AppText(g, "均 " + r.AvgCharsPerCommit.ToString("0.0", CultureInfo.InvariantCulture) + " 字/次",
                _fNum2, Ctext, new RectangleF(x + 18, y + 35, 170, 26), false);
            AppText(g, Analysis.FmtCount(r.Chars) + " 字 · " + Analysis.FmtCount(r.Commits) + " 次 · 不同字 "
                + Analysis.FmtCount(r.DistinctChars) + " 种",
                _fAxis, Csub, new RectangleF(x + 18, y + 62, w - 36, 16), false);

            // 构成条 + 图例:基准 260 高时仍在 y+84(与旧版逐像素一致);卡片长高后整块
            // 按比例下移,最终贴到底部「高频 / 最活跃」两行之上,不再浮在卡片中段。
            long total = 0;
            for (int i = 0; i < r.Composition.Length; i++) total += r.Composition[i];
            float barW = w - 36;
            List<string> legendLines = new List<string>();
            if (total > 0)
            {
                string legend = "";
                for (int i = 0; i < r.Composition.Length; i++)
                {
                    if (r.Composition[i] <= 0) continue;
                    if (legend.Length > 0) legend += " · ";
                    legend += RimeSlice.CompositionNames[i] + " " + (r.Composition[i] * 100.0 / total).ToString("0", CultureInfo.InvariantCulture) + "%";
                }
                legendLines = WrapText(g, legend, _fAxis, barW);
            }
            float growth = h > 260f ? h - 260f : 0f;
            float lineStep = LayoutFrame.Clamp(14f + growth * 0.02f, 14f, 26f);
            float legendGap = LayoutFrame.Clamp(12f + growth * 0.014f, 12f, 22f);
            float blockH = 8f + legendGap + Math.Min(2, legendLines.Count) * lineStep;
            float baseBarY = y + 84f;
            float hugBarY = (y + h - 40f) - 12f - blockH;      // 摘要两行之上
            float t = LayoutFrame.Clamp(growth / 420f, 0f, 1f);
            float barY = baseBarY + (hugBarY - baseBarY) * t;
            if (barY < baseBarY) barY = baseBarY;
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
                for (int i = 0; i < legendLines.Count && i < 2; i++)
                    AppText(g, legendLines[i], _fAxis, Csub,
                        new RectangleF(x + 18, barY + legendGap + i * lineStep, barW, lineStep), false);

                // 2K 下把条形图与页脚之间的余量交给「构成明细」:每类字数与占比逐行列清,
                // 不再让「字符构成」卡下半张空着。基准高度不进入这段代码,旧截图逐像素不变。
                if (growth > 80f)
                {
                    float tableTop = barY + 8f + legendGap + Math.Min(2, legendLines.Count) * lineStep + 22f;
                    float tableBottom = y + h - 46f;
                    int rows = 0;
                    for (int i = 0; i < r.Composition.Length; i++) if (r.Composition[i] > 0) rows++;
                    if (rows > 0 && tableBottom - tableTop >= rows * 20f)
                    {
                        float rowH = Math.Min(30f, (tableBottom - tableTop) / rows);
                        int row = 0;
                        for (int i = 0; i < r.Composition.Length; i++)
                        {
                            if (r.Composition[i] <= 0) continue;
                            float ry = tableTop + row * rowH;
                            using (SolidBrush dot = new SolidBrush(CompositionColor(i)))
                                g.FillEllipse(dot, x + 18, ry + rowH / 2f - 3f, 6, 6);
                            AppText(g, RimeSlice.CompositionNames[i], _fSmall, Ctext,
                                new RectangleF(x + 32, ry, 90, rowH), false);
                            AppText(g, Analysis.FmtCount(r.Composition[i]) + " 字", _fSmall, Csub,
                                new RectangleF(x + 128, ry, 100, rowH), false);
                            AppText(g, (r.Composition[i] * 100.0 / total).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                                _fSmall, i == 0 ? Cblue : Csub, new RectangleF(x + w - 74, ry, 56, rowH), true);
                            row++;
                        }
                    }
                }
            }

            if (growth > 80f)
            {
                string rhythm = "最长连续 " + r.LongestBurstMinutes + " 分钟 · "
                    + Analysis.FmtCount(r.LongestBurstChars) + " 字 · 峰值 "
                    + (r.BusiestCharHour >= 0 ? r.BusiestCharHour + " 时 " + Analysis.FmtCount(r.BusiestCharHourChars) + " 字" : "—")
                    + " · 平均 " + r.AvgCharsPerCommit.ToString("0.0", CultureInfo.InvariantCulture) + " 字/次";
                AppText(g, rhythm, _fAxis, Csub, new RectangleF(x + 18, y + h - 92, w - 36, 16), false);
                using (Pen pen = new Pen(Cline)) g.DrawLine(pen, x + 18, y + h - 68, x + w - 18, y + h - 68);
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
            int items = r.Reminders.Count;
            if (items == 0) return;
            float top = y + 38f, bottom = y + h - 12f;
            float body = bottom - top;
            if (body < 16f) body = 16f;

            // 行高与条目间距都得按「折行后的总行数」算:只数条目会把两行文案挤成一行。
            List<List<string>> wraps = new List<List<string>>(items);
            int totalLines = 0;
            for (int i = 0; i < items; i++)
            {
                List<string> lines = WrapText(g, r.Reminders[i], _fSmall, w - 48);
                if (lines.Count == 0) lines.Add("");
                wraps.Add(lines);
                totalLines += lines.Count;
            }

            // 行高 16..28、条目间距 6..18 随卡片高度呼吸:基准 260 高时仍是 16 / 6(与旧版逐像素一致),
            // 卡片越高行距越松;再有余量就继续摊进条目之间 —— 2K 上四条提醒铺满整张卡,
            // 而不是一条贴顶、下面留 700px 的黑。
            float growth = h > 260f ? h - 260f : 0f;
            float fitH = LayoutFrame.CalcRowHeight(body, 0f, (items - 1) * 6f, 16f, 28f, totalLines);
            float lineH = LayoutFrame.Clamp(16f + growth * 0.017f, 16f, 28f);
            if (fitH < lineH) lineH = fitH;                    // 行数多到排不下时先压行高,再谈铺开
            if (lineH < 16f) lineH = 16f;
            float fillGap = items > 1 ? (body - totalLines * lineH) / (items - 1) : 6f;
            float spread = LayoutFrame.Clamp(growth / 300f, 0f, 1f);
            float gap = 6f + (fillGap - 6f) * spread;
            if (gap < 6f) gap = 6f;
            if (gap > 40f) gap = 40f;   // 条目间距有上限,余量交给整体居中,不再把两条提醒拉到卡片两端

            float ry = top;
            float slack = body - (totalLines * lineH + (items - 1) * gap);
            if (slack > 0f && growth > 0f) ry += slack / 2f;    // 只有一条时居中,别孤零零贴在卡顶
            for (int i = 0; i < items && ry + lineH <= bottom + 0.5f; i++)
            {
                List<string> lines = wraps[i];
                bool good = r.Reminders[i].IndexOf("达成", StringComparison.Ordinal) >= 0
                    || r.Reminders[i].IndexOf("平稳", StringComparison.Ordinal) >= 0;
                using (SolidBrush dot = new SolidBrush(good ? Cgreen : Corange))
                    g.FillEllipse(dot, x + 18, ry + lineH / 2f - 2f, 4, 4);
                int available = (int)Math.Floor((bottom - ry) / lineH);
                if (available < 1) available = 1;
                int count = Math.Min(lines.Count, available);
                for (int k = 0; k < count; k++)
                {
                    // 末行把剩下的字并进来(AppText 自带省略号),绝不把半行裁到卡片外。
                    string line = k == count - 1 && count < lines.Count
                        ? string.Join("", lines.GetRange(k, lines.Count - k).ToArray()) : lines[k];
                    AppText(g, line, _fSmall, Ctext, new RectangleF(x + 30, ry, w - 48, lineH), false);
                    ry += lineH;
                }
                ry += gap;
            }

            // 大屏上把卡片底部余量交给「今日主题词」,与日报/长图使用同一套实词过滤结果。
            if (growth > 80f && ry < y + h - 96f)
            {
                List<string> words = r.TopPhrases.Count > 0 ? r.TopPhrases : r.TopContentChars;
                if (words.Count > 0)
                {
                    AppText(g, "今日主题词", _fSmall, Csub, new RectangleF(x + 18, y + h - 74, w - 36, 16), false);
                    float cx = x + 18;
                    for (int i = 0; i < words.Count && i < 3; i++)
                    {
                        float cw = Math.Min(w - 36, g.MeasureString(words[i], _fAxis).Width + 16);
                        if (cx + cw > x + w - 18) break;
                        using (GraphicsPath chip = RoundedRect(cx, y + h - 52, cw, 22, 11))
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(26, Cblue))) g.FillPath(fill, chip);
                        AppText(g, words[i], _fAxis, Ctext, new RectangleF(cx, y + h - 52, cw, 22), true);
                        cx += cw + 6;
                    }
                }
                AppText(g, "提醒按当日数据生成 · 仅本地", _fAxis, Csub,
                    new RectangleF(x + 18, y + h - 24, w - 36, 14), false);
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
                _dailyReportPath = path;
                if (path.Length > 0) SetStatus("昨日日报已生成:" + System.IO.Path.GetFileName(path));
            }
            catch (Exception) { /* 日报是附加价值,不能影响面板打开 */ }
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>页脚一句话:看的是哪一天、跟谁比、存到哪。</summary>
        private string DayFooterHint()
        {
            DailyReport r = _dayReport;
            if (r == null) return "日报 · 正在生成";
            string hint = "日报 · " + r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            hint += r.HasData ? (r.InProgress ? " · 进行中" : " · 完整") : " · 无数据";
            if (r.BaseDays > 0) hint += " · 对比" + r.BaselineLabel;
            hint += " · 已归档至本地日报目录";
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
