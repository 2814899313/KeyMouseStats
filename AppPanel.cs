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
    internal sealed partial class Dashboard
    {
        private int _appDays = 1, _appPage;
        /// <summary>0 次数列表 / 1 APM 效率 / 2 时长占比。</summary>
        private int _appView;
        private bool _appsByWindow;
        private bool _onlyUnclassified;
        private int _appCategory = -1;
        private readonly List<RectangleF> _appCategoryRects = new List<RectangleF>();
        private readonly List<int> _appCategoryValues = new List<int>();
        private readonly Dictionary<string, Bitmap> _appIconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private int _appPageCount = 1;
        private readonly List<RectangleF> _appEditRects = new List<RectangleF>();
        private readonly List<AppActivity.Row> _appEditRows = new List<AppActivity.Row>();
        /// <summary>1.8.0:应用对比——最多选两个应用做并排对比(逐应用小时维度还不存在,所以对比的是现有指标)。</summary>
        private readonly List<RectangleF> _appCompareRects = new List<RectangleF>();
        private readonly List<AppActivity.Row> _appCompareRows = new List<AppActivity.Row>();
        private readonly List<AppActivity.Row> _comparePicked = new List<AppActivity.Row>();
        private RectangleF _appCompareClearRect;

        private void ToggleCompare(AppActivity.Row row)
        {
            if (row == null) return;
            // 同一个应用再点一次就取消;已有两个时替换掉最早选的那个。
            for (int i = 0; i < _comparePicked.Count; i++)
                if (_comparePicked[i].Id == row.Id) { _comparePicked.RemoveAt(i); Invalidate(); return; }
            if (_comparePicked.Count >= 2) _comparePicked.RemoveAt(0);
            _comparePicked.Add(row);
            Motion.Start("appcompare", 180, Ease.OutCubic);
            Invalidate();
        }
        private bool IsCompared(AppActivity.Row row)
        {
            foreach (AppActivity.Row picked in _comparePicked) if (picked.Id == row.Id) return true;
            return false;
        }
        /// <summary>对比摘要:两行应用的四项现有指标 + 倍数。没有选满两个时返回 null。</summary>
        internal string CompareSummary()
        {
            if (_comparePicked.Count < 2) return null;
            AppActivity.Row a = _comparePicked[0], b = _comparePicked[1];
            StringBuilder text = new StringBuilder();
            text.Append(a.Name).Append(" ↔ ").Append(b.Name).Append("：");
            text.Append("击键 ").Append(Analysis.FmtCount(a.Keys)).Append(" / ").Append(Analysis.FmtCount(b.Keys)).Append(Ratio(a.Keys, b.Keys));
            text.Append(" · 点击 ").Append(Analysis.FmtCount(a.Clicks)).Append(" / ").Append(Analysis.FmtCount(b.Clicks));
            text.Append(" · 滚轮 ").Append(Analysis.FmtCount(a.Wheel)).Append(" / ").Append(Analysis.FmtCount(b.Wheel));
            // 活跃时长要两边都有观测才值得比;老数据 AppObservedSeconds 为 0,显示「0 秒 / 0 秒」只是噪音。
            if (a.ActiveSeconds > 0 || b.ActiveSeconds > 0)
                text.Append(" · 时长 ").Append(ActivityMonitor.FormatDuration(a.ActiveSeconds)).Append(" / ").Append(ActivityMonitor.FormatDuration(b.ActiveSeconds));
            if (a.Keys == 0 && b.Keys == 0) text.Append("（两者都没有击键记录）");
            // 应用页的 APM 只有两边都有前台时长观测时才有意义。
            if (a.ActiveSeconds >= AppFlow.MinApmSeconds && b.ActiveSeconds >= AppFlow.MinApmSeconds)
                text.Append(" · APM ").Append(AppFlow.ApmText(AppFlow.ApmOf(a))).Append(" / ").Append(AppFlow.ApmText(AppFlow.ApmOf(b)));
            return text.ToString();
        }
        private static string Ratio(long a, long b)
        {
            return b > 0 ? "（×" + (a / (double)b).ToString("0.##", CultureInfo.InvariantCulture) + "）" : "";
        }
        private void ApplyAppChip(int id)
        {
            if (id == 64) { using (StatisticsReport report = new StatisticsReport(DateTime.Today, 2)) report.ShowDialog(this); return; }
            if (id == 65) { _appKeyGroups = !_appKeyGroups; _appPage = 0; return; }
            if (id >= 66 && id <= 68) { _appView = id - 66; _appPage = 0; _comparePicked.Clear(); return; }
            if (id == 40) _appPage = Math.Max(0, _appPage - 1);
            else if (id == 41) _appPage = Math.Min(_appPageCount - 1, _appPage + 1);
            else if (id == 50 || id == 51) { _appsByWindow = id == 51; if (id == 50) _onlyUnclassified = false; _appPage = 0; }
            else if (id == 63) { _onlyUnclassified = !_onlyUnclassified; if (_onlyUnclassified) { _appsByWindow = true; _appCategory = -1; } _appPage = 0; _comparePicked.Clear(); }
            else if (id >= 60 && id <= 62) { _appDays = id == 60 ? 1 : id == 61 ? 7 : 90; _appPage = 0; _comparePicked.Clear(); }
        }

        private bool HandleAppClick(PointF point)
        {
            for (int i = 0; i < _appCategoryRects.Count; i++)
                if (_appCategoryRects[i].Contains(point))
                {
                    int category = i < _appCategoryValues.Count ? _appCategoryValues[i] : i;
                    _appCategory = _appCategory == category ? -1 : category;
                    if (_appCategory >= 0) _onlyUnclassified = false;
                    _appPage = 0;
                    _comparePicked.Clear();
                    Invalidate();
                    return true;
                }
            for (int i = 0; i < _appCompareRects.Count; i++)
                if (_appCompareRects[i].Contains(point))
                {
                    ToggleCompare(i < _appCompareRows.Count ? _appCompareRows[i] : null);
                    return true;
                }
            if (_appCompareClearRect.Width > 0 && _appCompareClearRect.Contains(point))
            {
                _comparePicked.Clear();
                Motion.Start("appcompare", 180, Ease.OutCubic);
                Invalidate();
                return true;
            }
            for (int i = 0; i < _appEditRects.Count; i++)
                if (_appEditRects[i].Contains(point))
                {
                    ShowManualCategoryMenu(_appEditRows[i]);
                    return true;
                }
            return false;
        }

        private void ShowManualCategoryMenu(AppActivity.Row row)
        {
            string key = row.Window == null ? AppActivity.AppRuleKey(row.ProcessPath ?? "") : AppActivity.WindowRuleKey(row.Window);
            int selected;
            if (!AppActivity.Rules.TryGetValue(key, out selected)) selected = -1;
            ContextMenuStrip menu = PreparePopup(ref _categoryMenu);

            menu.Items.Add(new ToolStripMenuItem(row.Window == null ? "应用默认用途（保留窗口规则）" : "此窗口的用途") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            foreach (int category in new int[] { 0, 5, 1, 2, 3, 4 })
            {
                int choice = category;
                ToolStripMenuItem item = new ToolStripMenuItem(AppActivity.Categories[choice]) { Checked = selected == choice };
                item.Click += delegate { AppActivity.Rules[key] = choice; Store.Save(); Invalidate(); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem automatic = new ToolStripMenuItem("恢复自动判断（移除此规则）") { Checked = selected < 0 };
            automatic.Click += delegate { AppActivity.Rules.Remove(key); Store.Save(); Invalidate(); };
            menu.Items.Add(automatic);
            ToolStripMenuItem advanced = new ToolStripMenuItem("高级规则：标题关键词 / 适用范围…");
            advanced.Click += delegate
            {
                // Open after the popup has closed, so it does not cover the rule editor.
                BeginInvoke((Action)delegate
                {
                    if (IsDisposed) return;
                    using (AppCategoryDialog dialog = new AppCategoryDialog(row))
                        if (dialog.ShowDialog(this) == DialogResult.OK) { Store.Save(); Invalidate(); }
                });
            };
            menu.Items.Add(advanced);

            menu.Show(this, PointToClient(Cursor.Position));
        }

        private void AppChip(Graphics g, int id, float x, float y, float width, string text, bool selected)
        {
            RectangleF rect = new RectangleF(x, y, width, 30);
            _chips.Add(rect); _chipIds.Add(id);
            PaintChip(g, rect, text, selected, null);
        }

        private void AppText(Graphics g, string text, Font font, Color color, RectangleF rect, bool right)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap, Alignment = right ? StringAlignment.Far : StringAlignment.Near,
                LineAlignment = StringAlignment.Center })
                g.DrawString(text, font, brush, rect, format);
        }

        /// <summary>按当前应用页区间汇总每个应用的键位构成（键组计数）。</summary>
        private Dictionary<string, long[]> AppKeyGroupsForRange()
        {
            Dictionary<string, long[]> map = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
            DateTime from = DateTime.Today.AddDays(-(_appDays - 1));
            for (DateTime date = from; date <= DateTime.Today; date = date.AddDays(1))
            {
                DayRecord day = Analysis.GetDay(date);
                if (day == null || day.IsEmpty || day.Apps == null) continue;
                foreach (AppUsage app in day.Apps.Values)
                {
                    if (app == null || !app.HasKeyGroups) continue;
                    string path = app.ProcessPath ?? "";
                    long[] groups;
                    if (!map.TryGetValue(path, out groups)) { groups = new long[6]; map[path] = groups; }
                    for (int i = 0; i < 6 && i < app.KeyGroups.Length; i++) groups[i] += app.KeyGroups[i];
                }
            }
            return map;
        }

        private void PaintApps(Graphics g)
        {
            _appEditRects.Clear(); _appEditRows.Clear();
            _appCompareRects.Clear(); _appCompareRows.Clear();
            _appCategoryRects.Clear(); _appCategoryValues.Clear();
            // 第一级:时间范围(左) + 对象粒度(右) + 明确的跳转动作(独立描边样式)。
            AppChip(g, 60, Cx, 98, 80, "今日", _appDays == 1);
            AppChip(g, 61, Cx + 90, 98, 80, "近 7 天", _appDays == 7);
            AppChip(g, 62, Cx + 180, 98, 90, "近 90 天", _appDays == 90);
            AppActionChip(g, 64, Cx + 290, 98, 120, "今日分析 ↗");
            AppChip(g, 50, Cx + Cw - 180, 98, 80, "按应用", !_appsByWindow);
            AppChip(g, 51, Cx + Cw - 90, 98, 90, "按窗口", _appsByWindow);

            long[] categoryKeys;
            Dictionary<string, long[]> keyGroups = _appKeyGroups ? AppKeyGroupsForRange() : null;
            List<AppActivity.Row> rows = AppActivity.Aggregate(Analysis.RangeDays(_appDays), _appsByWindow, out categoryKeys);
            int pending = 0;
            foreach (AppActivity.Row row in rows) if (!row.Legacy && row.Category == 4) pending++;
            if (_onlyUnclassified) rows.RemoveAll(delegate(AppActivity.Row row) { return row.Legacy || row.Category != 4; });
            else if (_appCategory >= 0) rows.RemoveAll(delegate(AppActivity.Row row) { return row.Legacy || row.Category != _appCategory; });
            long totalKeys = 0;
            foreach (long value in categoryKeys) totalKeys += value;
            Color[] colors = { Cblue, Cpurple, Corange, Cgreen, Csub, Ccyan };
            int[] order = { 0, 5, 1, 2, 3, 4 };
            float width = (Cw - (order.Length - 1) * 12) / order.Length;
            for (int position = 0; position < order.Length; position++)
            {
                int i = order[position];
                float x = Cx + position * (width + 12);
                PaintCardBase(g, x, 143, width, 78);
                if (_appCategory == i)
                {
                    using (Pen pen = new Pen(Cblue, 1.6f))
                        using (GraphicsPath path = RoundedRect(x + 1, 144, width - 2, 76, 12))
                            g.DrawPath(pen, path);
                }
                _appCategoryRects.Add(new RectangleF(x, 143, width, 78));
                _appCategoryValues.Add(i);
                AppText(g, AppActivity.Categories[i], _fBody, _appCategory == i ? Ctext : Csub, new RectangleF(x + 12, 150, width - 65, 20), false);
                AppText(g, Analysis.FmtCount(categoryKeys[i]), _fNum2, colors[i], new RectangleF(x + 12, 173, width - 24, 27), false);
                string percent = totalKeys > 0 ? (100.0 * categoryKeys[i] / totalKeys).ToString("0.#") + "%" : "--";
                AppText(g, percent, _fAxis, Csub, new RectangleF(x + width - 59, 151, 47, 18), true);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(30, colors[i])))
                    g.FillRectangle(brush, x + 12, 208, width - 24, 3);
                if (categoryKeys[i] > 0 && totalKeys > 0)
                    using (SolidBrush brush = new SolidBrush(colors[i]))
                        g.FillRectangle(brush, x + 12, 208, (float)((width - 24) * categoryKeys[i] / totalKeys), 3);
            }

            PaintCardBase(g, Cx, 235, Cw, 351);
            AppText(g, _appsByWindow ? "窗口标题 / 应用" : "应用 / 进程路径", _fH2, Ctext, new RectangleF(Cx + 18, 242, 260, 24), false);
            string[] views = { "次数", "APM 效率", "时长占比" };
            int[] viewIds = { 66, 67, 68 };
            for (int i = 0; i < views.Length; i++)
                AppChip(g, viewIds[i], Cx + 286 + i * 86, 238, 80, views[i], _appView == i);
            if (_appView == 0)
                AppChip(g, 65, Cx + 552, 238, 88, "键位构成", _appKeyGroups);
            AppChip(g, 63, Cx + Cw - 106, 238, 106, "待标注 " + pending, _onlyUnclassified);

            if (_appView == 1) { PaintAppsApm(g, rows); return; }
            if (_appView == 2) { PaintAppsShare(g, rows); return; }
            PaintAppsTable(g, rows, keyGroups);
        }

        /// <summary>带跳转箭头/描边的次级动作,与普通筛选胶囊区分开。</summary>
        private void AppActionChip(Graphics g, int id, float x, float y, float width, string text)
        {
            RectangleF rect = new RectangleF(x, y, width, 30);
            _chips.Add(rect); _chipIds.Add(id);
            PointF mouse = ToBase(_mouse);
            bool hover = Hit(rect, new PointF(mouse.X - ContentX, mouse.Y - ContentY));
            using (GraphicsPath path = RoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 8))
            {
                using (SolidBrush fill = new SolidBrush(hover ? Color.FromArgb(30, Cblue) : Color.FromArgb(10, Cblue))) g.FillPath(fill, path);
                using (Pen pen = new Pen(Cblue, 1.2f)) g.DrawPath(pen, path);
            }
            using (SolidBrush b = new SolidBrush(Cblue))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, _fChip, b, rect, format);
        }

        /// <summary>原有列表:分类、击键、点击、滚轮 / 键位构成。</summary>
        private void PaintAppsTable(Graphics g, List<AppActivity.Row> rows, Dictionary<string, long[]> keyGroups)
        {
            Color[] colors = { Cblue, Cpurple, Corange, Cgreen, Csub, Ccyan };
            string[] headings = _appKeyGroups ? new[] { "移动", "技能", "交互", "功能", "文字", "未归位" } : new[] { "分类", "击键", "点击", "滚轮" };
            float[] columnX = { 349, 446, 552, 648 };
            const float keyBarX = 430f, keyBarW = 318f;
            if (_appKeyGroups)
            {
                for (int i = 0; i < headings.Length; i++)
                {
                    float legendX = keyBarX + i * (keyBarW / headings.Length);
                    using (SolidBrush brush = new SolidBrush(colors[i]))
                        g.FillRectangle(brush, legendX, 278, 8, 8);
                    AppText(g, headings[i], _fSmall, Csub, new RectangleF(legendX + 12, 274, 60, 20), false);
                }
            }
            else
            {
                AppText(g, "对比", _fSmall, Csub, new RectangleF(Cx + 4, 274, 40, 22), false);
                for (int i = 0; i < headings.Length; i++)
                    AppText(g, headings[i], _fSmall, Csub, new RectangleF(columnX[i], 274, 80, 22), i > 0);
            }
            using (Pen pen = new Pen(Cline)) g.DrawLine(pen, Cx + 16, 296, Cx + Cw - 16, 296);

            const int pageSize = 7;
            _appPageCount = Math.Max(1, (rows.Count + pageSize - 1) / pageSize);
            _appPage = Math.Min(_appPage, _appPageCount - 1);
            if (rows.Count == 0)
            {
                AppText(g, _onlyUnclassified ? "当前范围没有待标注窗口" : _appCategory >= 0 ? "该分类下没有应用" : "还没有应用记录", _fNum2, Ctext, new RectangleF(Cx + 100, 355, Cw - 200, 40), false);
                AppText(g, _onlyUnclassified || _appCategory >= 0 ? "关闭表头的筛选标签可查看全部；无法追溯的旧数据不列入此处。" : "使用键盘或鼠标后，这里会显示输入发生在哪个应用。", _fBody, Csub,
                    new RectangleF(Cx + 100, 400, Cw - 200, 30), false);
            }
            string reason = "点击“手动分类”直接选用途；高级规则支持标题关键词。APM 页可看各应用的输入效率。";
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            for (int i = 0; i < pageSize && _appPage * pageSize + i < rows.Count; i++)
            {
                AppActivity.Row row = rows[_appPage * pageSize + i];
                float y = 300 + i * 33;
                if (i % 2 == 0)
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(12, Ctext)))
                        g.FillRectangle(brush, Cx + 8, y, Cw - 16, 33);
                RectangleF check = new RectangleF(Cx + 6, y + 9, 14, 14);
                _appCompareRects.Add(check); _appCompareRows.Add(row);
                AppCheckbox(g, check, IsCompared(row));
                Bitmap icon = row.Legacy ? null : AppIcon(row.ProcessPath);
                if (icon != null) g.DrawImage(icon, Cx + 26, y + 8, 16, 16);
                else
                {
                    Color fallback = row.Category < 0 ? Csub : colors[row.Category];
                    using (GraphicsPath path = RoundedRect(Cx + 26, y + 8, 16, 16, 4))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(40, fallback))) g.FillPath(b, path);
                }
                AppText(g, row.Name, _fBody, Ctext, new RectangleF(Cx + 48, y, 180, 20), false);
                string detailText = row.Legacy ? "" : Path.GetFileName(row.ProcessPath ?? "");
                AppText(g, detailText, _fSmall, Csub, new RectangleF(Cx + 48, y + 17, 180, 15), false);
                if (new RectangleF(Cx + 48, y, 180, 33).Contains(mouse))
                    reason = row.Legacy ? row.Reason : "完整路径：" + row.ProcessPath;
                if (check.Contains(mouse)) reason = "勾选两个应用即可并排对比（最多两个）";
                Color color = row.Category < 0 ? Csub : colors[row.Category];
                AppText(g, row.Category < 0 ? "混合分类" : AppActivity.Categories[row.Category], _fBody, color,
                    new RectangleF(349, y, 90, 20), false);
                AppText(g, row.Source, _fAxis, Csub, new RectangleF(349, y + 17, 90, 15), false);
                if (new RectangleF(349, y, 90, 33).Contains(mouse)) reason = row.Reason;
                if (_appKeyGroups)
                {
                    long[] groups = null;
                    long groupTotal = 0;
                    if (keyGroups != null) keyGroups.TryGetValue(row.ProcessPath ?? "", out groups);
                    if (groups != null)
                        foreach (long value in groups) groupTotal += value;
                    if (groupTotal > 0)
                    {
                        RectangleF bar = new RectangleF(keyBarX, y + 9, keyBarW, 13);
                        using (SolidBrush track = new SolidBrush(Color.FromArgb(28, Ctext))) g.FillRectangle(track, bar);
                        float cursor = bar.X;
                        for (int j = 0; j < 6; j++)
                        {
                            float segment = (float)(bar.Width * groups[j] / (double)groupTotal);
                            if (segment <= 0) continue;
                            using (SolidBrush brush = new SolidBrush(colors[j])) g.FillRectangle(brush, cursor, bar.Y, segment, bar.Height);
                            cursor += segment;
                        }
                        using (Pen pen = new Pen(Color.FromArgb(60, Ctext))) g.DrawRectangle(pen, bar.X, bar.Y, bar.Width, bar.Height);
                        if (bar.Contains(mouse))
                        {
                            StringBuilder detail = new StringBuilder();
                            detail.Append(row.Name).Append("：");
                            for (int j = 0; j < 6; j++)
                            {
                                if (j > 0) detail.Append(" · ");
                                detail.Append(headings[j]).Append(' ')
                                    .Append((groups[j] * 100.0 / groupTotal).ToString("0.#", CultureInfo.InvariantCulture)).Append('%');
                            }
                            reason = detail.ToString();
                        }
                    }
                    else AppText(g, "该区间无此维度", _fSmall, Csub, new RectangleF(keyBarX, y + 6, 220, 22), false);
                }
                else
                {
                    long[] values = { row.Keys, row.Clicks, row.Wheel };
                    for (int j = 0; j < 3; j++)
                        AppText(g, Analysis.FmtCount(values[j]), _fBody, Ctext, new RectangleF(columnX[j + 1], y + 6, 80, 22), true);
                }
                if (!row.Legacy)
                {
                    RectangleF edit = new RectangleF(766, y + 3, 70, 25);
                    _appEditRects.Add(edit); _appEditRows.Add(row);
                    PaintChip(g, edit, "手动分类", false, null);
                    if (IsCompared(row))
                    {
                        double pop = 1;
                        if (Motion.Running("appcompare")) pop = Ease.Apply(Ease.OutQuad, Motion.Progress("appcompare"));
                        using (SolidBrush mark = new SolidBrush(ArtTheme.Mix(Cblue, Ctext, (float)(0.25 + 0.35 * pop))))
                            g.FillRectangle(mark, Cx + 4, y, 3, 33);
                    }
                }
            }
            string summary = CompareSummary();
            _appCompareClearRect = RectangleF.Empty;
            if (summary != null)
            {
                // 文本不换行,所以宽度要卡死在「清除对比」chip 左侧,不能让它压到 chip 上。
                AppText(g, summary, _fSmall, Ctext, new RectangleF(Cx + 18, 536, Cw - 274, 22), false);
                _appCompareClearRect = new RectangleF(Cx + Cw - 250, 532, 96, 27);
                PaintChip(g, _appCompareClearRect, "清除对比", false, null);
            }
            else AppText(g, reason, _fSmall, Csub, new RectangleF(Cx + 18, 538, Cw - 40, 18), false);
            AppText(g, "共 " + rows.Count + " 项 · 按击键排序 · " + (_appPage + 1) + " / " + _appPageCount + " 页"
                + (_comparePicked.Count == 1 ? " · 再勾选一个应用即可对比" : ""),
                _fSmall, Csub, new RectangleF(Cx + 18, 558, 600, 24), false);
            AppChip(g, 40, Cx + Cw - 186, 547, 80, "上一页", false);
            AppChip(g, 41, Cx + Cw - 96, 547, 80, "下一页", false);
        }

        /// <summary>
        /// APM 交叉分析:每个应用的前台活跃时长与(击键+点击),算出每分钟输入量。
        /// 只统计活跃 ≥60 秒的应用;时间太短的应用 APM 会离谱,放在页脚说明里。
        /// </summary>
        private void PaintAppsApm(Graphics g, List<AppActivity.Row> rows)
        {
            Color[] colors = { Cblue, Cpurple, Corange, Cgreen, Csub, Ccyan };
            List<AppFlow.Item> items = AppFlow.Build(rows);
            AppFlow.Totals totals = AppFlow.Sum(items);
            int excluded;
            List<AppFlow.Item> ranked = AppFlow.RankByApm(items, AppFlow.MinApmSeconds, out excluded);
            string top = ranked.Count > 0 ? ranked[0].Name + " " + AppFlow.ApmText(ranked[0].Apm) : "--";
            AppText(g, "全局 APM " + AppFlow.ApmText(totals.Apm) + " · 最高 " + top
                + " · 只统计活跃 ≥" + AppFlow.MinApmSeconds.ToString("0", CultureInfo.InvariantCulture) + " 秒的应用",
                _fSmall, Csub, new RectangleF(Cx + 18, 274, Cw - 36, 20), false);

            AppText(g, "应用", _fSmall, Csub, new RectangleF(Cx + 34, 300, 180, 18), false);
            AppText(g, "活跃时长", _fSmall, Csub, new RectangleF(340, 300, 100, 18), true);
            AppText(g, "输入", _fSmall, Csub, new RectangleF(440, 300, 90, 18), true);
            AppText(g, "APM", _fSmall, Csub, new RectangleF(540, 300, 70, 18), true);
            AppText(g, "相对全局", _fSmall, Csub, new RectangleF(620, 300, 140, 18), false);
            using (Pen pen = new Pen(Cline)) g.DrawLine(pen, Cx + 16, 320, Cx + Cw - 16, 320);

            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            string reason = "APM = 每分钟击键 + 点击。切到「时长占比」可以看时间都花在哪儿。";
            for (int i = 0; i < 7 && i < ranked.Count; i++)
            {
                AppFlow.Item item = ranked[i];
                float y = 322 + i * 31;
                if (i % 2 == 0)
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(12, Ctext)))
                        g.FillRectangle(brush, Cx + 8, y, Cw - 16, 31);
                Color dot = item.Category >= 0 && item.Category < colors.Length ? colors[item.Category] : Csub;
                using (SolidBrush brush = new SolidBrush(dot)) g.FillEllipse(brush, Cx + 18, y + 11, 6, 6);
                AppText(g, item.Name, _fBody, Ctext, new RectangleF(Cx + 32, y, 240, 20), false);
                AppText(g, item.Detail, _fSmall, Csub, new RectangleF(Cx + 32, y + 17, 240, 14), false);
                AppText(g, ActivityMonitor.FormatDuration(item.Seconds), _fBody, Csub, new RectangleF(340, y + 5, 100, 22), true);
                AppText(g, Analysis.FmtCount((long)item.Inputs), _fBody, Ctext, new RectangleF(440, y + 5, 90, 22), true);
                AppText(g, AppFlow.ApmText(item.Apm), _fBody, i < 3 ? Cblue : Ctext, new RectangleF(540, y + 5, 70, 22), true);
                RectangleF bar = new RectangleF(620, y + 12, 180, 8);
                using (SolidBrush track = new SolidBrush(Color.FromArgb(28, Ctext))) g.FillRectangle(track, bar);
                double fraction = totals.Apm > 0 ? item.Apm / (totals.Apm * 2) : 0;
                fraction = Math.Max(0, Math.Min(1, fraction));
                using (SolidBrush brush = new SolidBrush(i == 0 ? Cblue : Ccyan))
                    g.FillRectangle(brush, bar.X, bar.Y, Math.Max(2f, (float)(bar.Width * fraction)), bar.Height);
                if (new RectangleF(Cx + 8, y, Cw - 16, 31).Contains(mouse))
                {
                    double share = totals.Inputs > 0 ? item.Inputs * 100.0 / totals.Inputs : 0;
                    reason = item.Name + "：击键 " + Analysis.FmtCount(item.Keys) + " · 点击 " + Analysis.FmtCount(item.Clicks)
                        + " · 活跃 " + ActivityMonitor.FormatDuration(item.Seconds) + " · APM " + AppFlow.ApmText(item.Apm)
                        + " · 占全部输入 " + share.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                }
            }
            if (ranked.Count == 0)
            {
                AppText(g, "当前范围没有足够的前台活跃时长", _fNum2, Ctext, new RectangleF(Cx + 60, 380, Cw - 120, 40), false);
                AppText(g, "应用时长从带前台观测的版本开始累计；样本不足 60 秒的应用不参与 APM 排名。", _fBody, Csub,
                    new RectangleF(Cx + 60, 425, Cw - 120, 30), false);
            }
            AppText(g, reason, _fSmall, Csub, new RectangleF(Cx + 18, 548, Cw - 36, 18), false);
            AppText(g, "共 " + ranked.Count + " 个应用参与 · 另有 " + excluded + " 个活跃不足 "
                + AppFlow.MinApmSeconds.ToString("0", CultureInfo.InvariantCulture) + " 秒未参与 · 与「键位构成」共用当前筛选",
                _fSmall, Csub, new RectangleF(Cx + 18, 566, Cw - 36, 18), false);
        }

        private Color[] AppShareColors()
        {
            return new Color[] { Cblue, Cpurple, Corange, Cgreen, Ccyan, Cred, Csub };
        }

        /// <summary>使用时长占比:环图 + 右侧图例(图例同时给出时长、占比与 APM)。</summary>
        private void PaintAppsShare(Graphics g, List<AppActivity.Row> rows)
        {
            List<AppFlow.Item> items = AppFlow.Build(rows);
            double totalSeconds;
            int merged;
            List<AppFlow.Slice> slices = AppFlow.Slices(items, 6, out totalSeconds, out merged);
            Color[] colors = AppShareColors();
            PointF center = new PointF(Cx + 150, 424);

            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            int hover = ShareHoverIndex(center, 92, 24, slices, mouse);
            int legendHover = -1;
            for (int i = 0; i < slices.Count; i++)
                if (new RectangleF(Cx + 300, 280 + i * 33 - 2, Cw - 316, 30).Contains(mouse)) legendHover = i;
            int active = hover >= 0 ? hover : legendHover;
            PaintAppShareDonut(g, center, 92, 24, slices, colors, active);
            string reason = "环图按前台活跃时长切分；右侧图例同时给出 APM，方便把「花时间多」和「输入密集」分开看。";
            if (slices.Count == 0)
            {
                AppText(g, "当前范围没有应用时长观测", _fNum2, Ctext, new RectangleF(Cx + 60, 370, 300, 40), false);
                AppText(g, "应用时长从带前台观测的版本开始累计；旧数据只有次数，没有时长。", _fBody, Csub,
                    new RectangleF(Cx + 60, 415, 320, 50), false);
            }
            else
            {
                string duration = ActivityMonitor.FormatDuration(active >= 0 ? slices[active].Seconds : totalSeconds);
                string caption = active >= 0 ? slices[active].Name : "活跃时长";
                using (SolidBrush ink = new SolidBrush(Ctext))
                using (SolidBrush sub = new SolidBrush(Csub))
                using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    SizeF size = g.MeasureString(duration, _fNum2);
                    g.DrawString(duration, _fNum2, ink, center.X - size.Width / 2, center.Y - 26);
                    g.DrawString(caption, _fSmall, sub, new RectangleF(center.X - 72, center.Y + 2, 144, 18), centered);
                }
            }

            float rowY = 280;
            for (int i = 0; i < slices.Count; i++)
            {
                AppFlow.Slice slice = slices[i];
                if (i % 2 == 0)
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(12, Ctext)))
                        g.FillRectangle(brush, Cx + 300, rowY - 2, Cw - 316, 30);
                using (SolidBrush brush = new SolidBrush(colors[Math.Min(i, colors.Length - 1)]))
                    g.FillEllipse(brush, Cx + 310, rowY + 8, 8, 8);
                AppText(g, slice.Name, _fBody, i == active ? Cblue : Ctext, new RectangleF(Cx + 326, rowY, 160, 20), false);
                string sub = slice.Other ? "已合并" : slice.Category >= 0 && slice.Category < AppActivity.Categories.Length
                    ? AppActivity.Categories[slice.Category] : "未分类";
                AppText(g, sub, _fSmall, Csub, new RectangleF(Cx + 326, rowY + 16, 150, 14), false);
                AppText(g, ActivityMonitor.FormatDuration(slice.Seconds), _fBody, Csub, new RectangleF(Cx + 490, rowY + 3, 112, 22), true);
                AppText(g, (slice.Share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%", _fBody, Ctext,
                    new RectangleF(Cx + 610, rowY + 3, 74, 22), true);
                AppText(g, "APM " + AppFlow.ApmText(slice.Apm), _fSmall, i == 0 ? Cblue : Csub,
                    new RectangleF(Cx + 694, rowY + 3, 120, 22), true);
                if (new RectangleF(Cx + 300, rowY - 2, Cw - 316, 30).Contains(mouse) || i == active)
                {
                    reason = slice.Name + " · 时长 " + ActivityMonitor.FormatDuration(slice.Seconds)
                        + " · 占 " + (slice.Share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"
                        + " · 输入 " + Analysis.FmtCount(slice.Inputs) + " · APM " + AppFlow.ApmText(slice.Apm);
                }
                rowY += 33;
            }
            AppText(g, reason, _fSmall, Csub, new RectangleF(Cx + 18, 542, Cw - 36, 18), false);
            AppText(g, "时长占比 = 该应用前台活跃 ÷ 全部应用前台活跃 · 后 " + merged + " 个应用合并为「其他」 · APM = 每分钟击键+点击",
                _fSmall, Csub, new RectangleF(Cx + 18, 562, Cw - 36, 20), false);
        }

        /// <summary>环形图:每个切片画一段弧;鼠标落在环上时对应切片加亮。</summary>
        private void PaintAppShareDonut(Graphics g, PointF center, float radius, float thickness, List<AppFlow.Slice> slices, Color[] colors, int hover)
        {
            RectangleF outer = new RectangleF(center.X - radius + thickness / 2, center.Y - radius + thickness / 2,
                (radius - thickness / 2) * 2, (radius - thickness / 2) * 2);
            using (Pen track = new Pen(Color.FromArgb(26, Ctext), thickness)) g.DrawArc(track, outer, 0, 360);
            if (slices.Count == 0) return;
            float start = -90;
            for (int i = 0; i < slices.Count; i++)
            {
                float sweep = (float)(slices[i].Share * 360);
                if (sweep <= 0) continue;
                Color color = colors[Math.Min(i, colors.Length - 1)];
                float width = i == hover ? thickness + 5 : thickness;
                // 相邻弧段多画一点点:后画的段盖住前一段的抗锯齿边缘,
                // 避免环上出现一圈细黑缝;之前是每段减 0.3° 反而主动留了缝。
                float overlap = slices.Count > 1 ? Math.Min(2f, sweep * 0.35f + 0.6f) : 0f;
                using (Pen pen = new Pen(i == hover ? ArtTheme.Mix(color, Ctext, 0.25) : color, width))
                    g.DrawArc(pen, outer, start, sweep + overlap);
                start += sweep;
            }
        }

        /// <summary>环图命中:只有落在圆环半径内且角度对应的那个切片算命中。</summary>
        private int ShareHoverIndex(PointF center, float radius, float thickness, List<AppFlow.Slice> slices, PointF point)
        {
            if (slices == null || slices.Count == 0) return -1;
            double dx = point.X - center.X, dy = point.Y - center.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < radius - thickness - 2 || distance > radius + 2) return -1;
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI + 90;
            if (angle < 0) angle += 360;
            double cursor = 0;
            for (int i = 0; i < slices.Count; i++)
            {
                cursor += slices[i].Share * 360;
                if (angle <= cursor) return i;
            }
            return slices.Count - 1;
        }

        /// <summary>行首复选框:勾两个应用即可对比,不再每行放一个按钮占横向空间。</summary>
        private void AppCheckbox(Graphics g, RectangleF r, bool on)
        {
            using (GraphicsPath path = RoundedRect(r.X, r.Y, r.Width, r.Height, 3))
            {
                using (SolidBrush fill = new SolidBrush(on ? Cblue : Color.FromArgb(18, Ctext))) g.FillPath(fill, path);
                using (Pen pen = new Pen(on ? Cblue : Color.FromArgb(70, Ctext))) g.DrawPath(pen, path);
            }
            if (!on) return;
            using (Pen check = new Pen(ArtTheme.Current.OnAccent, 1.8f))
            {
                g.DrawLine(check, r.X + 3, r.Y + 7, r.X + 6, r.Y + 10);
                g.DrawLine(check, r.X + 6, r.Y + 10, r.X + 11, r.Y + 4);
            }
        }

        /// <summary>从 exe 提取 16×16 图标;取不到时返回 null,由调用方画分类色块兜底。</summary>
        private Bitmap AppIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Bitmap icon;
            if (_appIconCache.TryGetValue(path, out icon)) return icon;
            try
            {
                using (Icon extracted = Icon.ExtractAssociatedIcon(path))
                    if (extracted != null) icon = extracted.ToBitmap();
            }
            catch { icon = null; }
            _appIconCache[path] = icon;
            return icon;
        }

        private void ExportAppsApm()
        {
            try
            {
                long[] ignored;
                List<AppFlow.Item> items = AppFlow.Build(AppActivity.Aggregate(Analysis.RangeDays(_appDays), _appsByWindow, out ignored));
                AppFlow.Totals totals = AppFlow.Sum(items);
                int excluded;
                List<AppFlow.Item> ranked = AppFlow.RankByApm(items, AppFlow.MinApmSeconds, out excluded);
                StringBuilder csv = new StringBuilder("应用,进程路径,活跃秒,击键,点击,滚轮,每分钟输入(APM),占全部输入\r\n");
                foreach (AppFlow.Item item in ranked)
                {
                    double share = totals.Inputs > 0 ? item.Inputs * 100.0 / totals.Inputs : 0;
                    csv.Append(AppActivity.CsvCell(item.Name)).Append(',')
                       .Append(AppActivity.CsvCell(item.ProcessPath)).Append(',')
                       .Append(item.Seconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                       .Append(item.Keys).Append(',')
                       .Append(item.Clicks).Append(',')
                       .Append(item.Wheel).Append(',')
                       .Append(item.Apm.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                       .Append(share.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine();
                }
                csv.AppendLine();
                csv.AppendLine("# 全局 APM," + totals.Apm.ToString("0.##", CultureInfo.InvariantCulture)
                    + ",参与应用," + ranked.Count + ",活跃不足 " + AppFlow.MinApmSeconds.ToString("0", CultureInfo.InvariantCulture) + " 秒," + excluded);
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "应用APM_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
                File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出 APM 交叉分析：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ExportAppsShare()
        {
            try
            {
                long[] ignored;
                List<AppFlow.Item> items = AppFlow.Build(AppActivity.Aggregate(Analysis.RangeDays(_appDays), _appsByWindow, out ignored));
                double totalSeconds;
                int merged;
                List<AppFlow.Slice> slices = AppFlow.Slices(items, 200, out totalSeconds, out merged);
                StringBuilder csv = new StringBuilder("应用,活跃秒,占全部时长,输入次数,每分钟输入(APM)\r\n");
                foreach (AppFlow.Slice slice in slices)
                {
                    csv.Append(AppActivity.CsvCell(slice.Name)).Append(',')
                       .Append(slice.Seconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                       .Append((slice.Share * 100).ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                       .Append(slice.Inputs).Append(',')
                       .Append(slice.Apm.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine();
                }
                csv.AppendLine();
                csv.AppendLine("# 全部应用前台活跃秒," + totalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "应用时长占比_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
                File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出应用时长占比：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ExportApps()
        {
            try
            {
                if (_appView == 1) { ExportAppsApm(); return; }
                if (_appView == 2) { ExportAppsShare(); return; }
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "应用统计_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
                File.WriteAllText(path, AppActivity.Export(Analysis.RangeDays(_appDays)), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出当前时间范围的窗口明细：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class AppCategoryDialog : ThemedDialog
    {
        public AppCategoryDialog(AppActivity.Row row)
        {
            Text = "用途规则 · 一次设置，自动沿用"; Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(610, 490);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            string path = row.ProcessPath ?? "";
            Controls.Add(new TextBox { Text = row.Name + "\r\n" + path + "\r\n当前依据：" + row.Source + " · " + row.Reason,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(18, 18), Size = new Size(574, 78) });
            Controls.Add(new Label { Text = "规则范围", Location = new Point(18, 114), AutoSize = true });
            ComboBox scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(110, 110), Width = 482 };
            scope.Items.Add("整个应用的默认用途（保留更细的规则）");
            scope.Items.Add("此应用的标题包含关键词（标题变化后仍生效）");
            if (row.Window != null) scope.Items.Add("仅此精确窗口标题（优先级最高）");
            scope.SelectedIndex = row.Window == null ? 0 : 1; Controls.Add(scope);
            Controls.Add(new Label { Text = "标题关键词", Location = new Point(18, 158), AutoSize = true });
            ComboBox keyword = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Location = new Point(110, 154), Width = 482, MaxLength = 120 };
            string prefix = "title:" + path.ToLowerInvariant() + "\n";
            List<string> existingWords = new List<string>();
            foreach (string key in AppActivity.Rules.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) existingWords.Add(key.Substring(prefix.Length));
            existingWords.Sort(StringComparer.Ordinal);
            foreach (string word in existingWords) keyword.Items.Add(word);
            Controls.Add(keyword);
            Controls.Add(new Label { Text = "例如：项目名、GitHub、论文、哔哩哔哩。下拉可编辑已有关键词。", Location = new Point(110, 186), Size = new Size(482, 23) });
            Controls.Add(new Label { Text = "归入用途", Location = new Point(18, 222), AutoSize = true });
            ComboBox category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(110, 218), Width = 482 };
            category.Items.Add("不设此规则 / 删除此规则（恢复下一级判断）");
            foreach (string name in AppActivity.Categories) category.Items.Add(name);
            Controls.Add(category);
            Label preview = new Label { Location = new Point(18, 262), Size = new Size(574, 75) }; Controls.Add(preview);
            Controls.Add(new Label { Text = "优先级：精确窗口 > 标题关键词 > 应用默认 > 自动推测。\n多个关键词命中时采用最长的；同长按字典序确定。\n保存后重新汇总已有记录，也用于后续输入；不删除其他规则。",
                Location = new Point(18, 350), Size = new Size(574, 78) });
            Button save = new Button { Text = "保存规则", Location = new Point(366, 441), Size = new Size(108, 30) };
            Controls.Add(save);
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(486, 441), Size = new Size(106, 30) };
            Controls.Add(cancel); CancelButton = cancel; AcceptButton = save;
            Func<string> getKey = delegate
            {
                if (scope.SelectedIndex == 2) return AppActivity.WindowRuleKey(row.Window);
                if (scope.SelectedIndex == 1) return AppActivity.TitleRuleKey(path, keyword.Text);
                return AppActivity.AppRuleKey(path);
            };
            Action refresh = delegate
            {
                keyword.Enabled = scope.SelectedIndex == 1;
                bool valid = scope.SelectedIndex != 1 || keyword.Text.Trim().Length > 0;
                save.Enabled = valid;
                int current;
                category.SelectedIndex = AppActivity.Rules.TryGetValue(getKey(), out current) ? current + 1 : 0;
                if (!valid) { preview.Text = "输入一个稳定关键词，同一应用中包含它的标题都能匹配。\n精确窗口或更长关键词规则仍优先。"; return; }
                HashSet<string> titles = new HashSet<string>(StringComparer.Ordinal);
                long keys = 0; List<string> examples = new List<string>();
                foreach (DayRecord day in Store.History.Values) foreach (AppUsage app in day.Apps.Values)
                {
                    if (!string.Equals(app.ProcessPath, path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (scope.SelectedIndex == 1 && app.Title.IndexOf(keyword.Text.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (scope.SelectedIndex == 2 && app.Title != row.Window.Title) continue;
                    keys += app.Keys;
                    if (titles.Add(app.Title) && examples.Count < 2) examples.Add(app.Title);
                }
                preview.Text = "范围预览：已有 " + titles.Count + " 个标题，" + Analysis.FmtCount(keys) + " 次击键（更高优先级规则除外）。\n" + string.Join("\n", examples.ToArray());
            };
            scope.SelectedIndexChanged += delegate { refresh(); };
            keyword.TextChanged += delegate { refresh(); };
            refresh();
            save.Click += delegate
            {
                if (scope.SelectedIndex == 1 && keyword.Text.Trim().Length == 0) return;
                string key = getKey();
                if (category.SelectedIndex <= 0) AppActivity.Rules.Remove(key);
                else AppActivity.Rules[key] = category.SelectedIndex - 1;
                DialogResult = DialogResult.OK; Close();
            };
        }
    }
}
