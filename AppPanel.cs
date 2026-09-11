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
        private bool _appsByWindow;
        private bool _onlyUnclassified;
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
                if (ReferenceEquals(_comparePicked[i], row)) { _comparePicked.RemoveAt(i); Invalidate(); return; }
            if (_comparePicked.Count >= 2) _comparePicked.RemoveAt(0);
            _comparePicked.Add(row);
            Motion.Start("appcompare", 180, Ease.OutCubic);
            Invalidate();
        }
        private bool IsCompared(AppActivity.Row row)
        {
            foreach (AppActivity.Row picked in _comparePicked) if (ReferenceEquals(picked, row)) return true;
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
            if (id == 40) _appPage = Math.Max(0, _appPage - 1);
            else if (id == 41) _appPage = Math.Min(_appPageCount - 1, _appPage + 1);
            else if (id == 50 || id == 51) { _appsByWindow = id == 51; if (id == 50) _onlyUnclassified = false; _appPage = 0; }
            else if (id == 63) { _onlyUnclassified = !_onlyUnclassified; if (_onlyUnclassified) _appsByWindow = true; _appPage = 0; }
            else if (id >= 60 && id <= 62) { _appDays = id == 60 ? 1 : id == 61 ? 7 : 90; _appPage = 0; }
        }

        private bool HandleAppClick(PointF point)
        {
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
            AppChip(g, 60, Cx, 98, 80, "今日", _appDays == 1);
            AppChip(g, 61, Cx + 90, 98, 80, "近 7 天", _appDays == 7);
            AppChip(g, 62, Cx + 180, 98, 90, "近 90 天", _appDays == 90);
            AppChip(g, 63, Cx + 286, 98, 106, "只看待标注", _onlyUnclassified);
            AppChip(g, 64, Cx + 404, 98, 106, "今日分析", false);
            AppChip(g, 65, Cx + 520, 98, 106, "键位构成", _appKeyGroups);
            AppChip(g, 50, Cx + Cw - 190, 98, 90, "按应用", !_appsByWindow);
            AppChip(g, 51, Cx + Cw - 90, 98, 90, "按窗口", _appsByWindow);

            long[] categoryKeys;
            Dictionary<string, long[]> keyGroups = _appKeyGroups ? AppKeyGroupsForRange() : null;
            List<AppActivity.Row> rows = AppActivity.Aggregate(Analysis.RangeDays(_appDays), _appsByWindow, out categoryKeys);
            if (_onlyUnclassified) rows.RemoveAll(delegate(AppActivity.Row row) { return row.Legacy || row.Category != 4; });
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
                AppText(g, AppActivity.Categories[i], _fBody, Csub, new RectangleF(x + 12, 150, width - 65, 20), false);
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
            AppText(g, _appsByWindow ? "窗口标题 / 应用" : "应用 / 进程路径", _fH2, Ctext, new RectangleF(Cx + 18, 244, 300, 24), false);
            string[] headings = _appKeyGroups ? new[]{ "移动", "技能", "交互", "功能", "文字", "未归位" } : new[]{ "分类", "击键", "点击", "滚轮" };
            float[] columnX = { 349, 446, 552, 648 };
            const float keyBarX = 430f, keyBarW = 318f;
            if (_appKeyGroups)
            {
                // 键位构成列用堆叠段表达：六类键组共用一条 100% 宽的条，图例代替表头。
                for (int i = 0; i < headings.Length; i++)
                {
                    float legendX = keyBarX + i * (keyBarW / headings.Length);
                    using (SolidBrush brush = new SolidBrush(colors[i]))
                        g.FillRectangle(brush, legendX, 249, 8, 8);
                    AppText(g, headings[i], _fSmall, Csub, new RectangleF(legendX + 12, 245, 60, 20), false);
                }
            }
            else
            {
                for (int i = 0; i < headings.Length; i++)
                    AppText(g, headings[i], _fSmall, Csub, new RectangleF(columnX[i], 245, 80, 22), i > 0);
            }
            using (Pen pen = new Pen(Cline)) g.DrawLine(pen, Cx + 16, 273, Cx + Cw - 16, 273);

            const int pageSize = 7;
            _appPageCount = Math.Max(1, (rows.Count + pageSize - 1) / pageSize);
            _appPage = Math.Min(_appPage, _appPageCount - 1);
            if (rows.Count == 0)
            {
                AppText(g, _onlyUnclassified ? "当前范围没有待标注窗口" : "还没有应用记录", _fNum2, Ctext, new RectangleF(Cx + 100, 365, Cw - 200, 40), false);
                AppText(g, _onlyUnclassified ? "关闭“只看待标注”可查看全部；无法追溯的旧数据不列入此处。" : "使用键盘或鼠标后，这里会显示输入发生在哪个应用。", _fBody, Csub,
                    new RectangleF(Cx + 100, 410, Cw - 200, 30), false);
            }
            string reason = "点击“手动分类”直接选用途；高级规则支持标题关键词。";
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            for (int i = 0; i < pageSize && _appPage * pageSize + i < rows.Count; i++)
            {
                AppActivity.Row row = rows[_appPage * pageSize + i];
                float y = 278 + i * 37;
                if (i % 2 == 0)
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(12, Ctext)))
                        g.FillRectangle(brush, Cx + 8, y, Cw - 16, 37);
                AppText(g, row.Name, _fBody, Ctext, new RectangleF(Cx + 18, y, 290, 20), false);
                AppText(g, row.Detail, _fSmall, Csub, new RectangleF(Cx + 18, y + 19, 290, 16), false);
                Color color = row.Category < 0 ? Csub : colors[row.Category];
                AppText(g, row.Category < 0 ? "混合分类" : AppActivity.Categories[row.Category], _fBody, color,
                    new RectangleF(349, y, 90, 20), false);
                AppText(g, row.Source, _fAxis, Csub, new RectangleF(349, y + 20, 90, 16), false);
                if (new RectangleF(349, y, 90, 37).Contains(mouse)) reason = row.Reason;
                if (_appKeyGroups)
                {
                    long[] groups = null;
                    long groupTotal = 0;
                    if (keyGroups != null) keyGroups.TryGetValue(row.ProcessPath ?? "", out groups);
                    if (groups != null)
                        foreach (long value in groups) groupTotal += value;
                    if (groupTotal > 0)
                    {
                        RectangleF bar = new RectangleF(keyBarX, y + 11, keyBarW, 14);
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
                    else AppText(g, "该区间无此维度", _fSmall, Csub, new RectangleF(keyBarX, y + 7, 220, 22), false);
                }
                else
                {
                    long[] values = { row.Keys, row.Clicks, row.Wheel };
                    for (int j = 0; j < 3; j++)
                        AppText(g, Analysis.FmtCount(values[j]), _fBody, Ctext, new RectangleF(columnX[j + 1], y + 7, 80, 22), true);
                }
                if (!row.Legacy)
                {
                    RectangleF edit = new RectangleF(766, y + 5, 70, 27);
                    _appEditRects.Add(edit); _appEditRows.Add(row);
                    PaintChip(g, edit, "手动分类", false, null);
                    // 1.8.0:对比选择(最多两个,选中的行左侧加一条高亮)。
                    RectangleF compare = new RectangleF(694, y + 5, 64, 27);
                    _appCompareRects.Add(compare); _appCompareRows.Add(row);
                    PaintChip(g, compare, "对比", IsCompared(row), null);
                    if (IsCompared(row))
                    {
                        double pop = 1;
                        if (Motion.Running("appcompare")) pop = Ease.Apply(Ease.OutQuad, Motion.Progress("appcompare"));
                        using (SolidBrush mark = new SolidBrush(ArtTheme.Mix(Cblue, Ctext, (float)(0.25 + 0.35 * pop))))
                            g.FillRectangle(mark, Cx + 4, y, 3, 37);
                    }
                }
            }
            // 1.8.0:选满两个应用时,把提示行换成并排对比结果。
            string summary = CompareSummary();
            _appCompareClearRect = RectangleF.Empty;
            if (summary != null)
            {
                // 文本不换行,所以宽度要卡死在「清除对比」chip 左侧,不能让它压到 chip 上。
                AppText(g, summary, _fSmall, Ctext, new RectangleF(Cx + 18, 536, Cw - 274, 22), false);
                _appCompareClearRect = new RectangleF(Cx + Cw - 250, 532, 96, 27);
                PaintChip(g, _appCompareClearRect, "清除对比", false, null);
            }
            else AppText(g, reason, _fSmall, Csub, new RectangleF(Cx + 18, 538, Cw - 230, 18), false);
            AppText(g, "共 " + rows.Count + " 项 · 按击键排序 · " + (_appPage + 1) + " / " + _appPageCount + " 页"
                + (_comparePicked.Count == 1 ? " · 再选一个应用即可对比" : ""),
                _fSmall, Csub, new RectangleF(Cx + 18, 558, 460, 24), false);
            AppChip(g, 40, Cx + Cw - 186, 547, 80, "上一页", false);
            AppChip(g, 41, Cx + Cw - 96, 547, 80, "下一页", false);
        }

        private void ExportApps()
        {
            try
            {
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
