using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    // ============================================================================
    //  字符内容页(第 7 页)—— 键鼠统计的「上屏了什么」视角
    //
    //  数据源只有一个:Rime 输入法自己的 commit_log.csv(后处理,不挂钩子、不读剪贴板)。
    //  本页只读不写,和键鼠数据完全隔离;文件不存在时页面自己说明,不报错、不弹窗。
    // ============================================================================
    internal sealed partial class Dashboard
    {
        /// <summary>本页区间的四个选项。0 今日 / 1 近7天 / 2 近30天 / 3 全部。</summary>
        private static readonly string[] TextRangeNames = { "今日", "近 7 天", "近 30 天", "全部" };
        private static readonly int[] TextRangeDays = { 1, 7, 30, 0 };

        private int _textRange = 1;
        /// <summary>0 = 每日柱状图 / 1 = 24 小时时段分布。</summary>
        private int _textChart;
        /// <summary>右卡模式:0 词长 / 1 字符构成 / 2 输入节奏。</summary>
        private int _textWordMode;
        /// <summary>上一个等长区间的聚合,用于「较上期 ±%」。</summary>
        private RimeSlice _textPrev;
        /// <summary>当前帧登记的可悬停矩形与它们的编码(kind*1000+index)。</summary>
        private readonly List<RectangleF> _textBars = new List<RectangleF>();
        private readonly List<int> _textBarKeys = new List<int>();
        private int _textHoverKind = -1, _textHoverIndex = -1;
        private string _textHoverText = "";
        private RimeTextStats _rimeStats;
        private RimeSlice _rimeSlice;
        private DateTime _rimeLoadedAt = DateTime.MinValue;
        private DateTime _rimeFileStamp = DateTime.MinValue;
        private long _rimeFileSize = -1;
        private int _rimeSliceRange = -1;
        /// <summary>「刷新 N 秒」chip 的命中矩形(内容坐标)。</summary>
        private RectangleF _textRefreshRect;
        private ContextMenuStrip _textRefreshMenu;

        // ---------------------------------------------------------------- 数据

        /// <summary>
        /// 按需读取 + 按区间切片。缓存在三类情况下失效:第一次、超过设定的间隔
        /// (Store.TextRefreshSeconds,0 = 手动)、以及文件被改写(比较 mtime)。
        /// </summary>
        private void EnsureTextData()
        {
            DateTime now = DateTime.Now;
            string path = RimeTextStats.DefaultPath;
            DateTime stamp = DateTime.MinValue;
            long size = -1;
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    FileInfo info = new FileInfo(path);
                    stamp = info.LastWriteTime;
                    size = info.Length;
                }
            }
            catch (Exception) { }

            // 自动重读的间隔由设置决定。文件改动永远优先:一有新的上屏就重算,
            // 间隔只是「文件没变时也强制再读一次」的兜底。
            // 体积与时间戳一起比:文件系统可能缓存 mtime,而写入一定会改变长度。
            int interval = Store.TextRefreshSeconds;
            bool reload = _rimeStats == null
                || stamp != _rimeFileStamp
                || size != _rimeFileSize
                || (interval > 0 && (now - _rimeLoadedAt).TotalSeconds >= interval);
            if (reload)
            {
                _rimeStats = RimeTextStats.Load(path);
                _rimeLoadedAt = now;
                _rimeFileStamp = stamp;
                _rimeFileSize = size;
                _rimeSlice = null;
            }
            if (_rimeSlice != null && _rimeSliceRange == _textRange) return;

            DateTime from, to;
            TextRangeBounds(out from, out to);
            _rimeSlice = _rimeStats == null ? new RimeSlice() : _rimeStats.Slice(from, to);
            _rimeSliceRange = _textRange;
            // 环比区间在数据层算一次就够了,别放进每帧的重绘里。
            _textPrev = PreviousSlice();
        }

        private void TextRangeBounds(out DateTime from, out DateTime to)
        {
            to = DateTime.Today;
            int days = TextRangeDays[Math.Max(0, Math.Min(_textRange, TextRangeDays.Length - 1))];
            if (days > 0)
            {
                from = DateTime.Today.AddDays(-(days - 1));
                return;
            }
            from = DateTime.Today.AddDays(-6);
            if (_rimeStats != null)
            {
                if (_rimeStats.FirstDay != null) from = _rimeStats.FirstDay.Value;
                if (_rimeStats.LastDay != null && _rimeStats.LastDay.Value > to) to = _rimeStats.LastDay.Value;
            }
        }

        private void ApplyTextChip(int id)
        {
            if (id >= 400 && id <= 403)
            {
                _textRange = id - 400;
                _rimeSlice = null;
                Motion.Start(EnterMotionKey, 280, Ease.OutCubic);
                return;
            }
            if (id == 404)   // 重新读取
            {
                _rimeStats = null;
                _rimeSlice = null;
                _rimeSliceRange = -1;
                return;
            }
            if (id == 405)   // 刷新频率
            {
                ShowTextRefreshMenu(RefreshChipAnchor());
                return;
            }
            if (id == 406 || id == 407)   // 每日 / 时段
            {
                _textChart = id - 406;
                Motion.Start(EnterMotionKey, 280, Ease.OutCubic);
                return;
            }
            if (id >= 410 && id <= 412)   // 词长 / 构成 / 节奏
            {
                _textWordMode = id - 410;
                Motion.Start(EnterMotionKey, 280, Ease.OutCubic);
                return;
            }
        }

        /// <summary>刷新频率菜单:dirty 后就地重读,不需要重启面板。</summary>
        private void ShowTextRefreshMenu(Point screen)
        {
            ContextMenuStrip menu = PreparePopup(ref _textRefreshMenu);
            int selected = Store.TextRefreshSeconds;

            ToolStripMenuItem reloadNow = new ToolStripMenuItem("立即重新读取");
            reloadNow.Click += delegate
            {
                _rimeStats = null;
                _rimeSlice = null;
                _rimeSliceRange = -1;
                SetStatus("已重新读取 Rime 上屏日志");
                Invalidate();
            };
            menu.Items.Add(reloadNow);
            menu.Items.Add(new ToolStripSeparator());

            foreach (int option in RimeTextStats.RefreshOptions)
            {
                int value = option;
                ToolStripMenuItem item = new ToolStripMenuItem(
                    value <= 0 ? "手动（仅文件改动时重读）" : "每 " + value.ToString(CultureInfo.InvariantCulture) + " 秒");
                item.Checked = value == selected;
                item.Click += delegate
                {
                    Store.TextRefreshSeconds = value;
                    Store.Save();
                    _rimeStats = null;
                    _rimeSlice = null;
                    _rimeSliceRange = -1;
                    SetStatus("刷新间隔：" + RimeTextStats.RefreshLabel(value));
                    Invalidate();
                };
                menu.Items.Add(item);
            }
            menu.Show(this, screen);
        }

        /// <summary>菜单弹出位置:chip 的左下角(窗口坐标)。键盘激活时也走这里。</summary>
        private Point RefreshChipAnchor()
        {
            RectangleF r = _textRefreshRect;
            return PointToScreen(new Point(
                (int)((r.X + ContentX) * _s),
                (int)((r.Y + ContentY + r.Height) * _s)));
        }

        private void ExportText()
        {
            DateTime from, to;
            TextRangeBounds(out from, out to);
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string path = Path.Combine(desktop,
                    string.Format("字符内容_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now));
                File.WriteAllText(path, RimeTextStats.Export(_rimeStats, from, to), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出：\n" + path, "导出成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------------------------------------------------------- 绘制

        private void PaintText(Graphics g)
        {
            EnsureTextData();
            RimeSlice slice = _rimeSlice ?? new RimeSlice();

            if (_rimeStats != null && _rimeStats.Missing)
            {
                PaintTextMissing(g);
                return;
            }

            // 命中区每帧重建:悬停明细必须和眼睛看到的矩形严格同一套坐标。
            _textBars.Clear();
            _textBarKeys.Clear();

            // 日期筛选作用于全页，独立放在指标上方。
            AppText(g, "统计区间", _fSmall, Csub, new RectangleF(Cx, Cy - 28, 70, 24), false);
            PaintTextRangeChips(g, Cx, Cy - 37, Cw);
            // ---- 第一行:五张数字卡 ----
            float gap = 12;
            float tileW = (Cw - 4 * gap) / 5f;
            DateTime todayFrom, todayTo;
            TextRangeBounds(out todayFrom, out todayTo);
            long todayChars = 0;
            if (_rimeStats != null)
            {
                long value;
                _rimeStats.DayChars.TryGetValue(DateTime.Today, out value);
                todayChars = value;
            }
            _textPrev = PreviousSlice();
            PaintTextMetric(g, Cx, Cy + 8, tileW, "区间上屏字数", Analysis.FmtCount(slice.Chars), Cblue,
                DeltaText(_textPrev, slice.Chars, false));
            PaintTextMetric(g, Cx + (tileW + gap), Cy + 8, tileW, "上屏次数", Analysis.FmtCount(slice.Commits), Cgreen,
                DeltaText(_textPrev, slice.Commits, true));
            PaintTextMetric(g, Cx + 2 * (tileW + gap), Cy + 8, tileW, "平均每次字数",
                slice.Commits > 0 ? slice.AverageChars.ToString("0.00", CultureInfo.InvariantCulture) : "--", Cblue, "字 / 次上屏");
            PaintTextMetric(g, Cx + 3 * (tileW + gap), Cy + 8, tileW, "今日字数", Analysis.FmtCount(todayChars), Cblue, DateTime.Today.ToString("MM 月 dd 日"));
            PaintTextMetric(g, Cx + 4 * (tileW + gap), Cy + 8, tileW, "不同字符",
                Analysis.FmtCount(slice.DistinctChars), Cpurple,
                slice.DistinctChars > 0 ? "汉字 " + Analysis.FmtCount(slice.DistinctHan) + " 种" : "没有上屏");

            // ---- 左大卡:每日字数 / 时段分布 ----
            float dayY = Cy + 112;
            PaintCard(g, Cx, dayY, 520, 192, _textChart == 1 ? "时段分布 · 区间累计" : "每日上屏字数");
            PaintTextChartChips(g, Cx, dayY, 520);
            if (_textChart == 1) PaintTextHourChart(g, Cx + 18, dayY + 52, 520 - 36, 192 - 70, slice);
            else PaintTextDayChart(g, Cx + 18, dayY + 52, 520 - 36, 192 - 70);

            // ---- 右卡:词长 / 字符构成 / 输入节奏 ----
            PaintCard(g, Cx + 537, dayY, 295, 192, _textWordMode == 0 ? "词长分布" : _textWordMode == 1 ? "字符构成" : "输入节奏");
            PaintTextModeChips(g, Cx + 537, dayY, 295, 410, _textWordMode, WordModeNames);
            float bx = Cx + 537 + 18, by = dayY + 52, bw = 295 - 36, bh = 192 - 70;
            if (_textWordMode == 1) PaintTextComposition(g, bx, by, bw, bh, slice);
            else if (_textWordMode == 2) PaintTextRhythm(g, bx, by, bw, bh, slice);
            else PaintTextWordLength(g, bx, by, bw, bh, slice);

            // ---- 左下:高频字符 Top 20 ----
            PaintCard(g, Cx, Cy + 320, 520, 168, "高频字符 · TOP 20");
            PaintTextTopChars(g, Cx + 18, Cy + 320 + 40, 520 - 36, 168 - 50, slice);

            // ---- 右下:最近上屏 ----
            PaintCard(g, Cx + 537, Cy + 320, 295, 168, "最近上屏");
            _textRefreshRect = new RectangleF(Cx + Cw - 16 - 108, Cy + 320 + 9, 108, 24);
            _chips.Add(_textRefreshRect);
            _chipIds.Add(405);
            PaintTextControl(g, _textRefreshRect, "刷新 " + RimeTextStats.RefreshLabel(Store.TextRefreshSeconds) + " ▾", false);
            PaintTextRecent(g, Cx + 537 + 18, Cy + 320 + 40, 295 - 36, 168 - 50, slice);

            // ---- 悬停:高亮 + 浮层,同时把明细交给 Ctrl+C ----
            // 每帧按当前鼠标位置重算一次:换页、换筛选之后不会残留旧元素的提示。
            UpdateTextHover(false);
            _hoverDetail = _textHoverKind >= 0 && _textHoverText.Length > 0
                ? _textHoverText
                : BuildTextDetail(slice);
            if (_textHoverKind >= 0 && _textHoverText.Length > 0) PaintTextTooltip(g);
        }

        private static readonly string[] WordModeNames = { "词长", "构成", "节奏" };

        private void PaintTextMetric(Graphics g, float x, float y, float w, string label, string value, Color accent, string note)
        {
            PaintCardBase(g, x, y, w, 88);
            using (SolidBrush dot = new SolidBrush(accent)) g.FillEllipse(dot, x + w - 24, y + 18, 5, 5);
            AppText(g, label, _fSmall, Csub, new RectangleF(x + 18, y + 10, w - 50, 20), false);
            AppText(g, value, _fNum, Ctext, new RectangleF(x + 16, y + 29, w - 32, 35), false);
            AppText(g, note, _fAxis, Csub, new RectangleF(x + 18, y + 66, w - 36, 16), false);
        }

        private void PaintTextControl(Graphics g, RectangleF r, string label, bool selected)
        {
            PointF mouse = ToBase(_mouse);
            bool hover = r.Contains(new PointF(mouse.X - ContentX, mouse.Y - ContentY));
            using (GraphicsPath path = RoundedRect(r.X, r.Y, r.Width, r.Height, 7))
            using (SolidBrush fill = new SolidBrush(selected ? ArtTheme.Mix(Ccard, Cblue, 0.22) : hover ? Ccard2 : Ccard))
                g.FillPath(fill, path);
            using (SolidBrush ink = new SolidBrush(selected ? Cblue : Csub))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(label, _fSmall, ink, r, format);
        }

        /// <summary>卡片右上角的一组模式 chip。baseId 起头,按顺序 +0/+1/…。</summary>
        private void PaintTextModeChips(Graphics g, float x, float y, float w, int baseId, int selected, string[] names)
        {
            const float chipW = 44, chipH = 24, chipGap = 6;
            float groupWidth = chipW * names.Length + chipGap * (names.Length - 1);
            float left = x + w - 8 - groupWidth;
            for (int i = 0; i < names.Length; i++)
            {
                RectangleF rect = new RectangleF(left + i * (chipW + chipGap), y + 9, chipW, chipH);
                _chips.Add(rect);
                _chipIds.Add(baseId + i);
                PaintTextControl(g, rect, names[i], selected == i);
            }
        }

        /// <summary>与「上一个等长区间」的差异文案。没有可比区间时明确写「--」,而不是悄悄不显示。</summary>
        private static string DeltaText(RimeSlice previous, long current, bool byCommits)
        {
            if (previous == null) return "暂无上期数据";
            long prev = byCommits ? previous.Commits : previous.Chars;
            if (prev <= 0) return "暂无上期数据";
            double change = (current - prev) / (double)prev;
            if (Math.Abs(change) < 0.005) return "较上期 持平";
            string sign = change > 0 ? "+" : "−";
            return "较上期 " + sign + Math.Abs(change * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>上一个等长区间。区间是「全部」或没有更早数据时返回 null。</summary>
        private RimeSlice PreviousSlice()
        {
            if (_rimeStats == null) return null;
            int days = TextRangeDays[Math.Max(0, Math.Min(_textRange, TextRangeDays.Length - 1))];
            if (days <= 0) return null;
            DateTime from, to;
            TextRangeBounds(out from, out to);
            DateTime prevTo = from.AddDays(-1);
            DateTime prevFrom = prevTo.AddDays(-(days - 1));
            if (_rimeStats.FirstDay == null || prevTo < _rimeStats.FirstDay.Value) return null;
            RimeSlice previous = _rimeStats.Slice(prevFrom, prevTo);
            return previous.HasData ? previous : null;
        }

        /// <summary>区间筛选 chip:画在「每日字数」卡片右上角。</summary>
        private void PaintTextRangeChips(Graphics g, float x, float y, float w)
        {
            const float chipW = 56, chipH = 24, chipGap = 6;
            float left = x + w - 8 - (chipW * TextRangeNames.Length + chipGap * (TextRangeNames.Length - 1));
            for (int i = 0; i < TextRangeNames.Length; i++)
            {
                RectangleF rect = new RectangleF(left + i * (chipW + chipGap), y + 9, chipW, chipH);
                _chips.Add(rect);
                _chipIds.Add(400 + i);
                PaintTextControl(g, rect, TextRangeNames[i], _textRange == i);
            }
        }

        /// <summary>图表切换 chip:每日 / 时段。放在区间筛选左边。</summary>
        private void PaintTextChartChips(Graphics g, float x, float y, float w)
        {
            const float chipW = 44, chipH = 24, chipGap = 6;
            // 区间 chip 占右侧:4 个 56 宽 + 3 个间隙。图表切换排在它们左边,留 10 px 间隔。
            float left = x + w - 16 - (chipW * 2 + chipGap);
            for (int i = 0; i < 2; i++)
            {
                RectangleF rect = new RectangleF(left + i * (chipW + chipGap), y + 9, chipW, chipH);
                _chips.Add(rect);
                _chipIds.Add(406 + i);
                PaintTextControl(g, rect, i == 0 ? "每日" : "时段", _textChart == i);
            }
        }

        /// <summary>
        /// 24 小时字数分布(跨整个区间累计)。横轴是「一天里的第几个小时」,
        /// 所以它回答的是「我一般几点写得多」,不随日期变化。
        /// </summary>
        private void PaintTextHourChart(Graphics g, float x, float y, float w, float h, RimeSlice slice)
        {
            long max = 0;
            for (int i = 0; i < 24; i++) if (slice.HourChars[i] > max) max = slice.HourChars[i];
            if (max <= 0)
            {
                AppText(g, "本区间没有上屏记录", _fSmall, Csub, new RectangleF(x, y + h / 2 - 10, w, 20), false);
                return;
            }

            // 顶部留一行画「最活跃 N 时」,否则它会把横轴刻度盖掉。
            float left = x + 42, right = x + w, top = y + 14, bottom = y + h - 16;
            float plotWidth = right - left, plotHeight = bottom - top;

            for (int i = 0; i <= 2; i++)
            {
                float gy = bottom - plotHeight * i / 2f;
                using (Pen pen = new Pen(Color.FromArgb(i == 0 ? 34 : 14, Ctext)))
                {
                    if (i != 0) pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, left, gy, right, gy);
                }
                using (SolidBrush brush = new SolidBrush(Csub))
                {
                    string label = Analysis.FmtAxis(max * i / 2.0);
                    SizeF size = g.MeasureString(label, _fAxis);
                    g.DrawString(label, _fAxis, brush, left - size.Width - 6, gy - size.Height / 2);
                }
            }

            int busiest = slice.BusiestHour();
            float slot = plotWidth / 24f;
            float barWidth = Math.Max(3f, Math.Min(18f, slot * 0.62f));
            double entrance = Entrance();
            for (int hour = 0; hour < 24; hour++)
            {
                long value = slice.HourChars[hour];
                AddTextHit(new RectangleF(left + slot * hour, top, slot, plotHeight), 1, hour);
                float barHeight = (float)(value / (double)max) * plotHeight * (float)entrance;
                float bx = left + slot * hour + (slot - barWidth) / 2;
                if (value <= 0)
                {
                    using (SolidBrush empty = new SolidBrush(Color.FromArgb(26, Csub)))
                        g.FillRectangle(empty, bx, bottom - 2, barWidth, 2);
                }
                else
                {
                    if (barHeight < 2f) barHeight = 2f;
                    Color fill = hour == busiest ? Cblue : Color.FromArgb(150, Cblue);
                    using (GraphicsPath path = RoundedRect(bx, bottom - barHeight, barWidth, barHeight, 3))
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                }
                // 每 3 小时标一个刻度,避免 24 个数字挤成一排
                if (hour % 3 == 0)
                {
                    using (SolidBrush brush = new SolidBrush(hour == busiest ? Ctext : Csub))
                    {
                        string label = hour.ToString(CultureInfo.InvariantCulture);
                        SizeF size = g.MeasureString(label, _fAxis);
                        g.DrawString(label, _fAxis, brush, bx + (barWidth - size.Width) / 2, bottom + 3);
                    }
                }
            }

            using (SolidBrush brush = new SolidBrush(Csub))
            {
                string peak = busiest >= 0
                    ? "最活跃 " + busiest + " 时 · " + Analysis.FmtCount(slice.HourChars[busiest]) + " 字"
                    : "没有数据";
                SizeF size = g.MeasureString(peak, _fSmall);
                // 放在绘图区上方的空白里:横轴那行要留给整点刻度,挤在一起会互相盖住。
                g.DrawString(peak, _fSmall, brush, right - size.Width, y);
            }
        }

        /// <summary>每日字数柱状图。区间超过 31 天时只画最后 31 天,免得柱子细成一条线。</summary>
        private void PaintTextDayChart(Graphics g, float x, float y, float w, float h)
        {
            DateTime from, to;
            TextRangeBounds(out from, out to);
            List<RimeDay> series = _rimeStats == null
                ? new List<RimeDay>()
                : _rimeStats.DaySeries(from, to);
            if (series.Count > 31) series.RemoveRange(0, series.Count - 31);

            if (series.Count == 0)
            {
                AppText(g, "还没有可显示的日期", _fSmall, Csub, new RectangleF(x, y + h / 2 - 10, w, 20), false);
                return;
            }

            long max = 0;
            for (int i = 0; i < series.Count; i++) if (series[i].Chars > max) max = series[i].Chars;
            if (max <= 0) max = 1;

            float left = x + 42, right = x + w, top = y, bottom = y + h - 16;
            float plotWidth = right - left, plotHeight = bottom - top;

            // 三条网格线,刻度取整到「好看的数」
            for (int i = 0; i <= 2; i++)
            {
                float gy = bottom - plotHeight * i / 2f;
                using (Pen pen = new Pen(Color.FromArgb(i == 0 ? 34 : 14, Ctext)))
                {
                    if (i != 0) pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, left, gy, right, gy);
                }
                double value = max * i / 2.0;
                using (SolidBrush brush = new SolidBrush(Csub))
                {
                    string label = Analysis.FmtAxis(value);
                    SizeF size = g.MeasureString(label, _fAxis);
                    g.DrawString(label, _fAxis, brush, left - size.Width - 6, gy - size.Height / 2);
                }
            }

            float slot = plotWidth / series.Count;
            float barWidth = Math.Max(3f, Math.Min(26f, slot * 0.62f));
            double entrance = Entrance();
            for (int i = 0; i < series.Count; i++)
            {
                AddTextHit(new RectangleF(left + slot * i, top, slot, plotHeight), 0, i);
                float barHeight = (float)(series[i].Chars / (double)max) * plotHeight * (float)entrance;
                if (series[i].Chars > 0 && barHeight < 2f) barHeight = 2f;
                float bx = left + slot * i + (slot - barWidth) / 2;
                bool today = series[i].Day == DateTime.Today;
                Color fill = today ? Cblue : Color.FromArgb(190, Cblue);
                if (series[i].Chars == 0)
                {
                    using (SolidBrush empty = new SolidBrush(Color.FromArgb(28, Csub)))
                        g.FillRectangle(empty, bx, bottom - 2, barWidth, 2);
                    continue;
                }
                using (GraphicsPath path = RoundedRect(bx, bottom - barHeight, barWidth, barHeight, 3))
                using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
            }

            // 横轴:首尾日期各标一个
            using (SolidBrush brush = new SolidBrush(Csub))
            {
                string first = series[0].Day.ToString("M/d", CultureInfo.InvariantCulture);
                string last = series[series.Count - 1].Day.ToString("M/d", CultureInfo.InvariantCulture);
                g.DrawString(first, _fAxis, brush, left, bottom + 3);
                SizeF size = g.MeasureString(last, _fAxis);
                g.DrawString(last, _fAxis, brush, right - size.Width, bottom + 3);
                if (series.Count > 1)
                {
                    // 峰值放进横轴中段:顶上那块留给卡片标题与区间 chip,挤在那里会和网格标注重叠。
                    string peak = "峰值 " + Analysis.FmtCount(max) + " 字";
                    SizeF peakSize = g.MeasureString(peak, _fSmall);
                    g.DrawString(peak, _fSmall, brush, (left + right - peakSize.Width) / 2, bottom + 3);
                }
            }
        }

        /// <summary>词长分布:1..7 与「8+」八根竖条。</summary>
        private void PaintTextWordLength(Graphics g, float x, float y, float w, float h, RimeSlice slice)
        {
            long total = 0;
            for (int i = 1; i <= 8; i++) total += slice.WordLen[i];
            if (total <= 0)
            {
                AppText(g, "本区间没有上屏记录", _fSmall, Csub, new RectangleF(x, y + h / 2 - 10, w, 20), false);
                return;
            }

            long max = 1;
            for (int i = 1; i <= 8; i++) if (slice.WordLen[i] > max) max = slice.WordLen[i];

            float slot = w / 8f;
            float bottom = y + h - 18, top = y + 4;
            float plotHeight = bottom - top;
            for (int i = 1; i <= 8; i++)
            {
                long count = slice.WordLen[i];
                AddTextHit(new RectangleF(x + slot * (i - 1), top, slot, plotHeight), 2, i);
                float barHeight = (float)(count / (double)max) * plotHeight * (float)Entrance();
                if (count > 0 && barHeight < 2f) barHeight = 2f;
                float bx = x + slot * (i - 1) + slot * 0.18f;
                float barWidth = slot * 0.64f;
                using (GraphicsPath path = RoundedRect(bx, bottom - barHeight, barWidth, barHeight, 4))
                using (SolidBrush brush = new SolidBrush(i >= 6 ? Corange : Cgreen)) g.FillPath(brush, path);
                using (SolidBrush label = new SolidBrush(Csub))
                {
                    string caption = i == 8 ? "8+" : i.ToString(CultureInfo.InvariantCulture);
                    SizeF size = g.MeasureString(caption, _fAxis);
                    g.DrawString(caption, _fAxis, label, bx + (barWidth - size.Width) / 2, bottom + 3);
                }
                if (count > 0)
                {
                    using (SolidBrush num = new SolidBrush(Ctext))
                    {
                        string text = count >= 10000
                            ? (count / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k"
                            : count.ToString(CultureInfo.InvariantCulture);
                        SizeF size = g.MeasureString(text, _fAxis);
                        g.DrawString(text, _fAxis, num, bx + (barWidth - size.Width) / 2, bottom - barHeight - 15);
                    }
                }
            }
        }

        /// <summary>
        /// 高频字符 Top 20：10 列 × 2 行独立卡片，按频次从左向右排列。
        /// 字符、次数和相对频次分层展示，整张卡片可悬停查看明细。
        /// </summary>
        private void PaintTextTopChars(Graphics g, float x, float y, float w, float h, RimeSlice slice)
        {
            List<KeyValuePair<int, long>> rows = slice.TopChars(20);
            if (rows.Count == 0)
            {
                AppText(g, "本区间没有上屏记录", _fSmall, Csub, new RectangleF(x, y + h / 2 - 10, w, 20), false);
                return;
            }

            long sum = 0;
            foreach (KeyValuePair<int, long> row in rows) sum += row.Value;

            // 两行字符卡片，从左到右按频次排序。字符、次数与相对频次分层展示。
            Font glyphFont = TextCharFont();
            const int columns = 10;
            const float gap = 6;
            float cellW = (w - (columns - 1) * gap) / columns;
            float cellH = (h - 20 - gap) / 2;
            long highest = Math.Max(1, rows[0].Value);
            PointF mouse = ToBase(_mouse);
            mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);

            for (int i = 0; i < rows.Count; i++)
            {
                float cx = x + (i % columns) * (cellW + gap);
                float cy = y + (i / columns) * (cellH + gap);
                RectangleF cell = new RectangleF(cx, cy, cellW, cellH);
                AddTextHit(cell, 4, i);
                bool hover = cell.Contains(mouse);
                using (GraphicsPath card = RoundedRect(cx, cy, cellW, cellH, 6))
                {
                    using (SolidBrush fill = new SolidBrush(ArtTheme.Mix(Ccard, Cblue, hover ? 0.22 : i < 3 ? 0.10 : 0.035)))
                        g.FillPath(fill, card);
                    if (hover)
                        using (Pen outline = new Pen(Color.FromArgb(140, Cblue))) g.DrawPath(outline, card);
                }
                string name = RimeText.DisplayName(rows[i].Key);
                Font nameFont = RimeText.CountCodePoints(name) == 1 ? glyphFont : _fSmall;
                using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    using (SolidBrush glyph = new SolidBrush(i < 3 ? Cblue : Ctext))
                        g.DrawString(name, nameFont, glyph, new RectangleF(cx + 2, cy + 2, cellW - 4, 24), center);
                    using (SolidBrush count = new SolidBrush(Csub))
                        g.DrawString(Analysis.FmtCount(rows[i].Value) + " 次", _fAxis, count,
                            new RectangleF(cx + 2, cy + 25, cellW - 4, 14), center);
                }
                float trackW = cellW - 14;
                using (SolidBrush track = new SolidBrush(Color.FromArgb(18, Csub)))
                    g.FillRectangle(track, cx + 7, cy + cellH - 4, trackW, 2);
                using (SolidBrush frequency = new SolidBrush(Color.FromArgb(i < 3 ? 180 : 85, Cblue)))
                    g.FillRectangle(frequency, cx + 7, cy + cellH - 4,
                        Math.Max(1f, trackW * rows[i].Value / highest), 2);
            }

            AppText(g, "按频次排序 · 前 " + rows.Count + " 个字符占总字数 "
                + (sum > 0 && slice.Chars > 0 ? (sum * 100.0 / slice.Chars).ToString("0", CultureInfo.InvariantCulture) : "0") + "%",
                _fAxis, Csub, new RectangleF(x, y + h - 12, w, 12), false);
        }

        /// <summary>最近上屏:时间 + 文本 + 字数。</summary>
        private void PaintTextRecent(Graphics g, float x, float y, float w, float h, RimeSlice slice)
        {
            if (slice.Recent.Count == 0)
            {
                AppText(g, "本区间没有上屏记录", _fSmall, Csub, new RectangleF(x, y + h / 2 - 10, w, 20), false);
                return;
            }
            int rows = Math.Min(6, slice.Recent.Count);
            float rowHeight = Math.Min(20f, h / rows);
            for (int i = 0; i < rows; i++)
            {
                RimeCommit commit = slice.Recent[i];
                float ry = y + i * rowHeight;
                AddTextHit(new RectangleF(x, ry, w, rowHeight), 6, i);
                if (i % 2 == 0)
                    using (SolidBrush rowFill = new SolidBrush(Color.FromArgb(9, Csub)))
                        g.FillRectangle(rowFill, x - 5, ry, w + 10, rowHeight);
                AppText(g, commit.Time.ToString(commit.Time.Date == DateTime.Today ? "HH:mm" : "MM/dd HH:mm", CultureInfo.InvariantCulture), _fAxis, Csub,
                    new RectangleF(x, ry, 74, rowHeight), false);
                AppText(g, commit.Text, _fBody, Ctext,
                    new RectangleF(x + 80, ry, w - 80 - 46, rowHeight), false);
                AppText(g, commit.Chars.ToString(CultureInfo.InvariantCulture) + " 字", _fAxis, Csub,
                    new RectangleF(x + w - 42, ry, 42, rowHeight), true);
            }
        }

        /// <summary>找不到 commit_log.csv 时的整页说明。这不是错误状态,只是没开这个功能。</summary>
        private void PaintTextMissing(Graphics g)
        {
            PaintCard(g, Cx, Cy, Cw, 288, "字符内容：还没有数据源");
            float x = Cx + 24, y = Cy + 52, w = Cw - 48;

            AppText(g, "本页只读一个文件——Rime 输入法自己写的上屏日志。程序不挂钩子、不读剪贴板、不记录任何输入文字。",
                _fBody, Ctext, new RectangleF(x, y, w, 22), false);
            string path = RimeTextStats.DefaultPath;
            AppText(g, "期望位置：" + (string.IsNullOrEmpty(path) ? "(无法解析 %APPDATA%)" : path),
                _fSmall, Csub, new RectangleF(x, y + 30, w, 20), false);
            AppText(g, "让输入法按「时间点,汉字个数,汉字明文」三列追加这个 CSV，本页会自动出现统计；",
                _fSmall, Csub, new RectangleF(x, y + 54, w, 20), false);
            AppText(g, "记录一次上屏追加一行，写完不用重启面板。",
                _fSmall, Csub, new RectangleF(x, y + 78, w, 20), false);

            using (Pen pen = new Pen(Cline)) g.DrawLine(pen, x, y + 110, x + w, y + 110);

            AppText(g, "为什么单独做一页", _fH2, Ctext, new RectangleF(x, y + 122, w, 22), false);
            AppText(g, "键鼠数据回答「按了多少次」，字符数据回答「写了什么」。两者同机同时产生，但性质不同：",
                _fSmall, Csub, new RectangleF(x, y + 150, w, 20), false);
            AppText(g, "字符内容更敏感，所以它不进入 stats.txt、不参与导出合并、也不与应用或窗口关联。",
                _fSmall, Csub, new RectangleF(x, y + 172, w, 20), false);

            RectangleF refresh = new RectangleF(Cx + Cw - 132, Cy + 14, 108, 26);
            _chips.Add(refresh);
            _chipIds.Add(404);
            PaintChip(g, refresh, "重新读取", false, null);
        }

        // ---------------------------------------------------------------- 构成 / 节奏 / 词组

        /// <summary>构成分类的配色。顺序与 RimeSlice.CompositionNames 一致。</summary>
        private Color CompositionColor(int index)
        {
            switch (index)
            {
                case 0: return Cblue;
                case 1: return Corange;
                case 2: return Cgreen;
                case 3: return Ccyan;
                default: return Cpurple;
            }
        }

        /// <summary>字符构成:汉字 / 标点 / 字母数字 / 空白 / 其他,各一行。</summary>
        private void PaintTextComposition(Graphics g, float x, float y, float w, float h, RimeSlice slice)
        {
            long total = slice.CompositionTotal;
            if (total <= 0)
            {
                AppText(g, "本区间没有上屏记录", _fSmall, Csub, new RectangleF(x, y + h / 2 - 10, w, 20), false);
                return;
            }

            long max = 0;
            for (int i = 0; i < RimeSlice.CompositionNames.Length; i++)
                if (slice.Composition[i] > max) max = slice.Composition[i];

            float rowHeight = Math.Min(24f, h / 5f);
            for (int i = 0; i < RimeSlice.CompositionNames.Length; i++)
            {
                float ry = y + i * rowHeight;
                AddTextHit(new RectangleF(x, ry, w, rowHeight), 3, i);

                long value = slice.Composition[i];
                AppText(g, RimeSlice.CompositionNames[i], _fSmall, Csub, new RectangleF(x, ry, 62, rowHeight), false);

                float barLeft = x + 60, barMax = w - 60 - 108;
                float length = max > 0 ? (float)(value / (double)max) * barMax : 0f;
                if (length < 2f) length = 2f;
                float barTop = ry + rowHeight / 2 - 4;
                using (GraphicsPath track = RoundedRect(barLeft, barTop, barMax, 8, 4))
                using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(20, Ctext))) g.FillPath(trackBrush, track);
                if (value > 0)
                    using (GraphicsPath path = RoundedRect(barLeft, barTop, length, 8, 4))
                    using (SolidBrush brush = new SolidBrush(CompositionColor(i))) g.FillPath(brush, path);

                string share = (slice.CompositionShare(i) * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
                AppText(g, share, _fAxis, Ctext, new RectangleF(x + w - 104, ry, 50, rowHeight), true);
                AppText(g, Analysis.FmtCount(value), _fAxis, Csub, new RectangleF(x + w - 48, ry, 48, rowHeight), true);
            }
        }

        /// <summary>输入节奏:把「写了多久、写多快、能连多久」摊成四个数。</summary>
        private void PaintTextRhythm(Graphics g, float x, float y, float w, float h, RimeSlice slice)
        {
            if (slice.Commits <= 0)
            {
                AppText(g, "本区间没有上屏记录", _fSmall, Csub, new RectangleF(x, y + h / 2 - 10, w, 20), false);
                return;
            }

            string[] labels = { "平均每分钟", "最快一分钟", "有上屏的分钟", "最长连续输入" };
            string[] values =
            {
                slice.CharsPerActiveMinute.ToString("0.0", CultureInfo.InvariantCulture) + " 字",
                Analysis.FmtCount((long)Math.Round(slice.PeakPerMinute)) + " 字",
                slice.ActiveMinutes.ToString("N0", CultureInfo.InvariantCulture) + " 分钟",
                slice.LongestBurstMinutes.ToString(CultureInfo.InvariantCulture) + " 分钟 · "
                    + Analysis.FmtCount(slice.LongestBurstChars) + " 字"
            };

            float rowHeight = Math.Min(30f, (h - 18) / 4f);
            for (int i = 0; i < labels.Length; i++)
            {
                float ry = y + i * rowHeight;
                AppText(g, labels[i], _fSmall, Csub, new RectangleF(x, ry, 96, rowHeight), false);
                AppText(g, values[i], _fBody, i == 0 ? Cblue : Ctext, new RectangleF(x + 92, ry, w - 92, rowHeight), true);
            }
            AppText(g, "按有上屏的分钟算 · 间隔 >2 分钟断开", _fAxis, Csub,
                new RectangleF(x, y + h - 12, w, 12), false);
        }
        // ---------------------------------------------------------------- 悬停

        private void AddTextHit(RectangleF rect, int kind, int index)
        {
            _textBars.Add(rect);
            _textBarKeys.Add(kind * 1000 + index);
        }

        /// <summary>
        /// 按当前鼠标位置重算悬停元素。命中区是上一帧画出来的,和视觉完全同源。
        /// 绘制期间调用时必须 invalidate=false,否则会自己触发重绘死循环。
        /// </summary>
        internal void UpdateTextHover(bool invalidate)
        {
            PointF mouse = ToBase(_mouse);
            PointF point = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            int kind = -1, index = -1;
            for (int i = 0; i < _textBars.Count; i++)
            {
                if (!_textBars[i].Contains(point)) continue;
                int key = _textBarKeys[i];
                kind = key / 1000;
                index = key % 1000;
                break;
            }
            bool changed = kind != _textHoverKind || index != _textHoverIndex;
            _textHoverKind = kind;
            _textHoverIndex = index;
            _textHoverText = kind < 0 ? "" : TextHoverText(kind, index);
            if (changed && kind >= 0 && invalidate) Invalidate();
        }

        /// <summary>悬停到某个元素时显示的一行明细。</summary>
        private string TextHoverText(int kind, int index)
        {
            RimeSlice slice = _rimeSlice;
            if (slice == null || index < 0) return "";
            switch (kind)
            {
                case 0:
                {
                    DateTime from, to;
                    TextRangeBounds(out from, out to);
                    List<RimeDay> series = _rimeStats == null ? new List<RimeDay>() : _rimeStats.DaySeries(from, to);
                    if (series.Count > 31) series.RemoveRange(0, series.Count - 31);
                    if (index >= series.Count) return "";
                    RimeDay day = series[index];
                    return day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " · "
                        + Analysis.FmtCount(day.Chars) + " 字 / " + day.Commits.ToString("N0", CultureInfo.InvariantCulture) + " 次";
                }
                case 1:
                    return index.ToString(CultureInfo.InvariantCulture) + " 时 · "
                        + Analysis.FmtCount(slice.HourChars[index]) + " 字 / "
                        + slice.HourCommits[index].ToString("N0", CultureInfo.InvariantCulture) + " 次";
                case 2:
                    return (index >= 8 ? "8 字以上" : index.ToString(CultureInfo.InvariantCulture) + " 字词") + " · "
                        + Analysis.FmtCount(slice.WordLen[index]) + " 次";
                case 3:
                    return RimeSlice.CompositionNames[index] + " · " + Analysis.FmtCount(slice.Composition[index])
                        + " (" + (slice.CompositionShare(index) * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%)";
                case 4:
                {
                    List<KeyValuePair<int, long>> chars = slice.TopChars(20);
                    if (index >= chars.Count) return "";
                    double share = slice.Chars > 0 ? chars[index].Value * 100.0 / slice.Chars : 0;
                    return RimeText.DisplayName(chars[index].Key) + " · " + Analysis.FmtCount(chars[index].Value)
                        + " 次 (" + share.ToString("0.#", CultureInfo.InvariantCulture) + "%)";
                }
                default:
                {
                    if (index >= slice.Recent.Count) return "";
                    RimeCommit commit = slice.Recent[index];
                    string text = commit.Text == null ? "" : commit.Text;
                    if (text.Length > 18) text = text.Substring(0, 18) + "…";
                    return commit.Time.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        + " · " + commit.Chars.ToString(CultureInfo.InvariantCulture) + " 字 · " + text;
                }
            }
        }

        /// <summary>悬停浮层:跟着鼠标走,贴边时向内收。</summary>
        private void PaintTextTooltip(Graphics g)
        {
            if (_textHoverText.Length == 0) return;
            PointF mouse = ToBase(_mouse);
            float x = mouse.X - ContentX + 16;
            float y = mouse.Y - ContentY + 18;
            SizeF size = g.MeasureString(_textHoverText, _fSmall);
            float w = size.Width + 16, h = size.Height + 10;
            x = Math.Max(Cx, Math.Min(x, Cx + Cw - w));
            y = Math.Max(Cy, Math.Min(y, Cy + 488 - h));

            using (GraphicsPath path = RoundedRect(x, y, w, h, 6))
            {
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(238, Ccard2))) g.FillPath(fill, path);
                using (Pen pen = new Pen(Cline)) g.DrawPath(pen, path);
            }
            using (SolidBrush ink = new SolidBrush(Ctext)) g.DrawString(_textHoverText, _fSmall, ink, x + 8, y + 5);
        }

        /// <summary>
        /// 字符列专用字体:12 px 的正文对汉字太小,会糊成一团。按需创建、随窗口释放,
        /// 不在每帧 new(那会在打字时制造一堆 GDI 句柄)。
        /// </summary>
        private Font _fChar;
        private Font TextCharFont()
        {
            if (_fChar == null) _fChar = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            return _fChar;
        }

        /// <summary>页脚一句话:这页在看什么区间、数据从哪来、有没有被截断。</summary>
        private string TextFooterHint()
        {
            string range = TextRangeNames[Math.Max(0, Math.Min(_textRange, TextRangeNames.Length - 1))];
            if (_rimeStats == null) return "正在读取 Rime 上屏日志";
            if (_rimeStats.Missing) return "未找到 commit_log.csv · 本页只读该文件，程序不记录任何输入文字";
            if (_rimeStats.Error.Length > 0) return "读取失败：" + _rimeStats.Error;

            // 这一行的职责是「自证读了多少」:条数、字数、文件行数三者并排,
            // 任何一个数对不上都能当场看出来,不用再猜是页面没刷新还是解析漏了。
            string hint = range;
            if (_rimeSlice != null)
                hint += " · 已读 " + _rimeSlice.Commits.ToString("N0", CultureInfo.InvariantCulture) + " 条 / "
                    + Analysis.FmtCount(_rimeSlice.Chars) + " 字";
            hint += " · 文件 " + _rimeStats.RecordLines.ToString("N0", CultureInfo.InvariantCulture) + " 行 "
                + FormatBytes(_rimeStats.FileBytes);
            if (_rimeStats.LastCommit != null)
                hint += " · 最新 " + _rimeStats.LastCommit.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            hint += " · 刷新 " + RimeTextStats.RefreshLabel(Store.TextRefreshSeconds);
            if (_rimeStats.BadLines > 0) hint += " · " + _rimeStats.BadLines + " 行无法解析";
            if (_rimeStats.Truncated) hint += " · 只解析了最近部分";
            return hint;
        }

        /// <summary>字节数只用来对比「文件有没有变」,不需要精确到字节。</summary>
        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "--";
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>Ctrl+C 复制的明细:一句话说清这页现在在看什么。</summary>
        private string BuildTextDetail(RimeSlice slice)
        {
            if (slice == null || !slice.HasData) return "字符内容：" + TextRangeNames[Math.Max(0, Math.Min(_textRange, 3))] + " 没有上屏记录";
            List<KeyValuePair<int, long>> chars = slice.TopChars(3);
            StringBuilder text = new StringBuilder();
            text.Append("字符内容 ").Append(TextRangeNames[Math.Max(0, Math.Min(_textRange, 3))]);
            text.Append("：").Append(Analysis.FmtCount(slice.Chars)).Append(" 字 / ")
                .Append(Analysis.FmtCount(slice.Commits)).Append(" 次上屏");
            if (slice.ActiveDays > 0)
                text.Append("，活跃 ").Append(slice.ActiveDays).Append(" 天，日均 ")
                    .Append(slice.AverageCharsPerActiveDay.ToString("N0", CultureInfo.InvariantCulture)).Append(" 字");
            if (chars.Count > 0)
            {
                text.Append("；最高频 ");
                for (int i = 0; i < chars.Count; i++)
                {
                    if (i > 0) text.Append("、");
                    text.Append(RimeText.DisplayName(chars[i].Key)).Append(' ').Append(chars[i].Value);
                }
            }
            int busiest = slice.BusiestHour();
            if (busiest >= 0) text.Append("；最活跃 ").Append(busiest).Append(" 时");
            text.Append("；来源 ").Append(_rimeStats == null ? "" : _rimeStats.Source);
            return text.ToString();
        }
    }
}
