using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed partial class Dashboard
    {
        private bool _trendHourly;
        private int _hourlyTrendMetric;
        private DateTime _hourlyTrendDay = DateTime.Today;
        private int _hourlyCompareDays = 1;
        private bool _hourlyCompareAverage;

        private void ApplyHourlyTrendChip(int id)
        {
            if (id == 300 || id == 301) { _trendHourly = id == 301; _hover = -1; }
            if (id >= 310 && id <= 314) _hourlyTrendMetric = id - 310;
            if (id == 320) _hourlyTrendDay = _hourlyTrendDay.AddDays(-1);
            if (id == 321) _hourlyTrendDay = _hourlyTrendDay.AddDays(1);
            if (id == 322) _hourlyTrendDay = DateTime.Today;
            if (id == 326) ShowHourlyComparisonDialog();
            if (id == 323)
            {
                using (Form dialog = new ThemedDialog { Text = "选择小时趋势日期", ClientSize = new Size(304, 102),
                    FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi })
                using (DateTimePicker date = new DateTimePicker { Location = new Point(18, 16), Width = 268,
                    MinDate = DateTime.Today.AddDays(-364), MaxDate = DateTime.Today, Value = _hourlyTrendDay })
                using (Button ok = new Button { Text = "查看", Location = new Point(206, 60), DialogResult = DialogResult.OK })
                {
                    dialog.Controls.Add(date); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
                    if (dialog.ShowDialog(this) == DialogResult.OK) _hourlyTrendDay = date.Value.Date;
                }
            }
            if (_hourlyTrendDay < DateTime.Today.AddDays(-364)) _hourlyTrendDay = DateTime.Today.AddDays(-364);
            if (_hourlyTrendDay > DateTime.Today) _hourlyTrendDay = DateTime.Today;
        }

        private void ShowHourlyComparisonDialog()
        {
            using (Form dialog = new ThemedDialog { Text = "小时趋势对比", ClientSize = new Size(340, 154),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi })
            using (ComboBox mode = new ComboBox { Location = new Point(18, 18), Width = 304, DropDownStyle = ComboBoxStyle.DropDownList })
            using (Label daysLabel = new Label { Text = "向前取", AutoSize = true, Location = new Point(18, 62) })
            using (NumericUpDown days = new NumericUpDown { Location = new Point(82, 58), Width = 82, Minimum = 2, Maximum = 90, Value = Math.Max(2, Math.Min(90, _hourlyCompareDays)) })
            using (Label suffix = new Label { Text = "天，按小时求有效样本均值", AutoSize = true, Location = new Point(174, 62) })
            using (Button cancel = new Button { Text = "取消", Location = new Point(166, 108), Size = new Size(74, 30), DialogResult = DialogResult.Cancel })
            using (Button ok = new Button { Text = "应用", Location = new Point(248, 108), Size = new Size(74, 30), DialogResult = DialogResult.OK })
            {
                mode.Items.AddRange(new object[] { "不对比", "昨天", "上周同日", "近 N 天均值" });
                mode.SelectedIndex = _hourlyCompareDays == 0 ? 0 : _hourlyCompareAverage ? 3 : _hourlyCompareDays == 7 ? 2 : 1;
                Action update = delegate { bool enabled = mode.SelectedIndex == 3; days.Enabled = enabled; daysLabel.Enabled = enabled; suffix.Enabled = enabled; };
                mode.SelectedIndexChanged += delegate { update(); }; update();
                dialog.Controls.Add(mode); dialog.Controls.Add(daysLabel); dialog.Controls.Add(days); dialog.Controls.Add(suffix);
                dialog.Controls.Add(cancel); dialog.Controls.Add(ok); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _hourlyCompareAverage = mode.SelectedIndex == 3;
                _hourlyCompareDays = mode.SelectedIndex == 0 ? 0 : mode.SelectedIndex == 1 ? 1 : mode.SelectedIndex == 2 ? 7 : (int)days.Value;
            }
        }

        internal static double[] HourlyValues(DayRecord day, DateTime date, int metric, DateTime now)
        {
            double[] values = new double[24];
            bool available = day != null && (metric < 2 || TimeInsights.HasTime(day));
            for (int h = 0; h < 24; h++)
                values[h] = !available || date.Date > now.Date || date.Date == now.Date && h > now.Hour ? double.NaN
                    : metric == 0 ? day.HourKeys[h] : metric == 1 ? day.HourClicks[h] : metric == 2 ? day.ActiveHours[h] / 60
                    : PersonalStats.Ratio(metric == 3 ? day.HourKeys[h] : day.HourClicks[h] * 60.0, day.ActiveHours[h]);
            return values;
        }

        internal static DateTime HourlyComparisonDate(DateTime selected, int compareDays)
        { return selected.Date.AddDays(-Math.Max(1, compareDays)); }

        internal static double[] AverageHourlyValues(DateTime selected, int days, int metric, DateTime now)
        {
            double[] sums = new double[24]; int[] counts = new int[24];
            for (int d = 1; d <= Math.Max(1, days); d++)
            {
                DateTime date = selected.Date.AddDays(-d);
                double[] row = HourlyValues(Analysis.GetDay(date), date, metric, now);
                for (int h = 0; h < 24; h++) if (!double.IsNaN(row[h])) { sums[h] += row[h]; counts[h]++; }
            }
            double[] result = new double[24];
            for (int h = 0; h < 24; h++) result[h] = counts[h] == 0 ? double.NaN : sums[h] / counts[h];
            return result;
        }

        private string HourlyValue(double value)
        { return double.IsNaN(value) ? "无记录" : _hourlyTrendMetric >= 3 ? value.ToString("0.##") + (_hourlyTrendMetric == 3 ? " 次/秒" : " 次/分") : _hourlyTrendMetric == 2 ? value.ToString("0.#") + " 分钟"
            : (value == Math.Floor(value) ? Analysis.FmtCount((long)value) : value.ToString("0.#")) + " 次"; }

        private void PaintHourlyTrend(Graphics g)
        {
            string[] names = { "击键", "点击", "活跃时长", "击键/秒", "点击/分" };
            for (int i = 0; i < names.Length; i++) AppChip(g, 310 + i, Cx + i * 78, 98, 70, names[i], _hourlyTrendMetric == i);
            string compareButton = _hourlyCompareDays == 0 ? "对比：关闭" : _hourlyCompareAverage ? "对比：近" + _hourlyCompareDays + "天均值" : _hourlyCompareDays == 7 ? "对比：上周同日" : "对比：昨天";
            AppChip(g, 326, Cx + 540, 98, 190, compareButton, _hourlyCompareDays > 0);
            AppChip(g, 322, Cx + Cw - 88, 98, 88, "回今天", false);
            PaintCardBase(g, Cx, 150, Cw, 360);
            AppChip(g, 323, Cx + 16, 160, 170, _hourlyTrendDay.ToString("yyyy-MM-dd ddd"), false);
            AppChip(g, 320, Cx + Cw - 180, 160, 76, "前一天", false);
            AppChip(g, 321, Cx + Cw - 94, 160, 76, "后一天", false);
            AppText(g, "每小时" + names[_hourlyTrendMetric] + (_hourlyTrendMetric >= 3 ? " · 按活跃时间归一化" : _hourlyTrendMetric == 2 ? " · 单位：分钟" : " · 单位：次"), _fH2, Ctext,
                new RectangleF(Cx + 208, 160, 300, 30), false);
            DateTime now = DateTime.Now;
            double[] values = HourlyValues(Analysis.GetDay(_hourlyTrendDay), _hourlyTrendDay, _hourlyTrendMetric, now);
            DateTime compareDay = HourlyComparisonDate(_hourlyTrendDay, _hourlyCompareDays);
            double[] comparison = _hourlyCompareDays == 0 ? null : _hourlyCompareAverage
                ? AverageHourlyValues(_hourlyTrendDay, _hourlyCompareDays, _hourlyTrendMetric, now)
                : HourlyValues(Analysis.GetDay(compareDay), compareDay, _hourlyTrendMetric, now);
            double selectedMax = 0, comparisonMax = 0, sum = 0, compareSum = 0, completeSum = 0;
            int count = 0, compareCount = 0, completeCount = 0, peak = -1;
            for (int h = 0; h < 24; h++)
            {
                if (double.IsNaN(values[h])) continue;
                count++; sum += values[h];
                if (_hourlyTrendDay < now.Date || h < now.Hour) { completeSum += values[h]; completeCount++; }
                if (values[h] > selectedMax) { selectedMax = values[h]; peak = h; }
            }
            if (comparison != null) for (int h = 0; h < 24; h++) if (!double.IsNaN(comparison[h]))
            {
                if (comparison[h] > comparisonMax) comparisonMax = comparison[h];
                if (!double.IsNaN(values[h])) { compareCount++; compareSum += comparison[h]; }
            }
            double ceiling = Math.Max(1, Math.Ceiling(Math.Max(selectedMax, comparisonMax) * 1.15));
            float left = Cx + 56, right = Cx + Cw - 28, top = 222, bottom = 451;
            Func<int, float> px = delegate(int h) { return left + (right - left) * h / 23; };
            Func<double, float> py = delegate(double v) { return bottom - (float)(v / ceiling) * (bottom - top); };
            for (int i = 0; i <= 4; i++)
            {
                float y = bottom - (bottom - top) * i / 4;
                using (Pen p = new Pen(Cline)) g.DrawLine(p, left, y, right, y);
                AppText(g, Analysis.FmtAxis(ceiling * i / 4), _fAxis, Csub, new RectangleF(Cx + 4, y - 10, 46, 20), true);
            }
            for (int h = 0; h < 24; h++)
                if (h % 3 == 0 || h == 23) AppText(g, h.ToString("00") + ":00", _fAxis, Csub, new RectangleF(px(h) - 17, bottom + 6, 45, 20), false);
            Color color = _hourlyTrendMetric == 2 ? Cpurple : _hourlyTrendMetric == 1 ? Cgreen : Cblue;
            Color compareColor = Corange;
            if (comparison != null)
                for (int h = 1; h < 24; h++)
                    if (!double.IsNaN(values[h - 1]) && !double.IsNaN(values[h]) && !double.IsNaN(comparison[h - 1]) && !double.IsNaN(comparison[h]))
                    {
                        PointF[] band = { new PointF(px(h - 1), py(values[h - 1])), new PointF(px(h), py(values[h])),
                            new PointF(px(h), py(comparison[h])), new PointF(px(h - 1), py(comparison[h - 1])) };
                        Color bandColor = values[h - 1] + values[h] >= comparison[h - 1] + comparison[h]
                            ? Color.FromArgb(24, 238, 102, 92) : Color.FromArgb(24, 73, 154, 232);
                        using (SolidBrush fill = new SolidBrush(bandColor)) g.FillPolygon(fill, band);
                    }
            using (Pen compareLine = new Pen(compareColor, 1.7f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (SolidBrush compareDot = new SolidBrush(compareColor))
                if (comparison != null) for (int h = 0; h < 24; h++)
                {
                    if (double.IsNaN(comparison[h])) continue;
                    if (h > 0 && !double.IsNaN(comparison[h - 1])) g.DrawLine(compareLine, px(h - 1), py(comparison[h - 1]), px(h), py(comparison[h]));
                    g.FillEllipse(compareDot, px(h) - 2.2f, py(comparison[h]) - 2.2f, 4.4f, 4.4f);
                }
            using (Pen line = new Pen(color, 2))
            using (SolidBrush dot = new SolidBrush(color))
                for (int h = 0; h < 24; h++)
                {
                    if (double.IsNaN(values[h])) continue;
                    if (h > 0 && !double.IsNaN(values[h - 1])) g.DrawLine(line, px(h - 1), py(values[h - 1]), px(h), py(values[h]));
                    float radius = _hourlyTrendDay == now.Date && h == now.Hour ? 4 : 2.5f;
                    g.FillEllipse(dot, px(h) - radius, py(values[h]) - radius, radius * 2, radius * 2);
                }
            if (_hourlyTrendDay == now.Date && now.Hour < 23)
            {
                float futureX = px(now.Hour) + (px(now.Hour + 1) - px(now.Hour)) / 2;
                using (SolidBrush future = new SolidBrush(Color.FromArgb(36, Cbg))) g.FillRectangle(future, futureX, top, right - futureX, bottom - top);
            }
            if(ThemeArt.Active && peak>=0)ThemeArt.Sticker(g,2,new RectangleF(Math.Max(left,Math.Min(right-30,px(peak)-15)),Math.Max(top-10,py(selectedMax)-32),30,30));
            string compareLabel = _hourlyCompareAverage ? "近" + _hourlyCompareDays + "天均值" : _hourlyCompareDays == 7 ? "上周同日" : "昨天";
            using (Pen legendMain = new Pen(color, 2.4f)) g.DrawLine(legendMain, Cx + 500, 205, Cx + 522, 205);
            AppText(g, "所选日", _fSmall, Ctext, new RectangleF(Cx + 528, 196, 52, 20), false);
            if (comparison != null)
            {
                using (Pen legendCompare = new Pen(compareColor, 1.8f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(legendCompare, Cx + 588, 205, Cx + 610, 205);
                AppText(g, compareLabel, _fSmall, Csub, new RectangleF(Cx + 616, 196, 120, 20), false);
            }
            string detail = count == 0 ? "所选日期没有此指标的小时记录。" : comparison == null
                ? "当前小时使用加大圆点，阴影区域尚未来到。" : "实线为所选日，虚线为" + compareLabel + "；红蓝色带表示高低差，摘要按已记录同期计算。";
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            if (new RectangleF(left - 8, top, right - left + 16, bottom - top + 24).Contains(mouse))
            {
                int h = Math.Max(0, Math.Min(23, (int)Math.Round((mouse.X - left) / (right - left) * 23)));
                using (Pen p = new Pen(Csub) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot }) g.DrawLine(p, px(h), top, px(h), bottom);
                detail = _hourlyTrendDay.ToString("MM-dd") + "  " + h.ToString("00") + ":00—" + (h + 1).ToString("00") + ":00  ·  "
                    + HourlyValue(values[h]) + (_hourlyTrendDay == now.Date && h == now.Hour ? "（累计中）" : "");
                if (comparison != null)
                {
                    double delta = double.IsNaN(values[h]) || double.IsNaN(comparison[h]) ? double.NaN : values[h] - comparison[h];
                    string deltaText = double.IsNaN(delta) ? "无法比较" : (delta >= 0 ? "+" : "") + HourlyValue(delta)
                        + (comparison[h] == 0 ? "" : "（" + (delta >= 0 ? "+" : "") + (delta / comparison[h] * 100).ToString("0.#") + "%）");
                    detail += "    " + compareLabel + " · " + HourlyValue(comparison[h]) + "    差值 " + deltaText;
                }
            }
            AppText(g, detail, _fSmall, Csub, new RectangleF(Cx + 18, 482, Cw - 36, 22), false);
            if (_hourlyTrendMetric >= 3)
            {
                sum = count == 0 ? double.NaN : sum / count;
                if (comparison != null) compareSum = compareCount == 0 ? double.NaN : compareSum / compareCount;
            }
            double summaryDelta = count == 0 || comparison == null || compareCount == 0 ? double.NaN : sum - compareSum;
            string deltaSummary = double.IsNaN(summaryDelta) ? "--" : (summaryDelta >= 0 ? "+" : "") + HourlyValue(summaryDelta);
            string pctSummary = double.IsNaN(summaryDelta) || compareSum == 0 ? "--" : (summaryDelta >= 0 ? "+" : "") + (summaryDelta / compareSum * 100).ToString("0.#") + "%";
            string[] labels = { _hourlyTrendMetric >= 3 ? "所选日指标" : "所选日合计", comparison == null ? "已过小时均值" : compareLabel + "同期",
                comparison == null ? "最高小时量" : "相差数量", comparison == null ? "最忙时段" : "相差比例" };
            string[] numbers = { count == 0 ? "--" : HourlyValue(sum), comparison == null ? (completeCount == 0 ? "--" : HourlyValue(completeSum / completeCount)) : (compareCount == 0 ? "--" : HourlyValue(compareSum)),
                comparison == null ? (count == 0 ? "--" : HourlyValue(selectedMax)) : deltaSummary,
                comparison == null ? (peak < 0 ? "--" : peak.ToString("00") + ":00—" + (peak + 1).ToString("00") + ":00") : pctSummary };
            for (int i = 0; i < 4; i++)
            {
                float x = Cx + i * 212;
                PaintCardBase(g, x, 524, 196, 62);
                AppText(g, labels[i], _fSmall, Csub, new RectangleF(x + 14, 530, 168, 18), false);
                AppText(g, numbers[i], _fNum2, Ctext, new RectangleF(x + 14, 549, 176, 28), false);
            }
        }

        private void ExportHourlyTrend()
        {
            try
            {
                DateTime now = DateTime.Now;
                double[] values = HourlyValues(Analysis.GetDay(_hourlyTrendDay), _hourlyTrendDay, _hourlyTrendMetric, now);
                DateTime compareDay = HourlyComparisonDate(_hourlyTrendDay, _hourlyCompareDays);
                double[] comparison = _hourlyCompareDays == 0 ? null : _hourlyCompareAverage
                    ? AverageHourlyValues(_hourlyTrendDay, _hourlyCompareDays, _hourlyTrendMetric, now)
                    : HourlyValues(Analysis.GetDay(compareDay), compareDay, _hourlyTrendMetric, now);
                string compareName = _hourlyCompareDays == 0 ? "不对比" : _hourlyCompareAverage ? "近" + _hourlyCompareDays + "天均值" : compareDay.ToString("yyyy-MM-dd");
                string name = _hourlyTrendMetric == 3 ? "每活跃秒击键" : _hourlyTrendMetric == 4 ? "每活跃分钟点击" : _hourlyTrendMetric == 2 ? "活跃分钟" : _hourlyTrendMetric == 1 ? "点击次数" : "击键次数";
                StringBuilder csv = new StringBuilder("日期,对比方式,小时,所选日" + name + ",对比值" + name + ",差值,状态\r\n");
                for (int h = 0; h < 24; h++) csv.Append(_hourlyTrendDay.ToString("yyyy-MM-dd")).Append(',').Append(compareName).Append(',').Append(h).Append(',')
                    .Append(double.IsNaN(values[h]) ? "" : values[h].ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(comparison == null || double.IsNaN(comparison[h]) ? "" : comparison[h].ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(comparison == null || double.IsNaN(values[h]) || double.IsNaN(comparison[h]) ? "" : (values[h] - comparison[h]).ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .AppendLine(double.IsNaN(values[h]) ? "无记录或尚未来到" : _hourlyTrendDay == now.Date && h == now.Hour ? "累计中" : "已记录部分");
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "小时趋势_" + _hourlyTrendDay.ToString("yyyyMMdd") + "_" + DateTime.Now.ToString("HHmmss_fff") + ".csv");
                File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
