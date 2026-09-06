using System;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
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
        private int _hourlyCompareMode = 1; // 0=关闭 1=昨天 2=上周同日 3=近N天 4=本周
        private int _hourlyCompareDays = 3;

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
            using (Form dialog = new ThemedDialog { Text = "小时趋势多曲线", ClientSize = new Size(360, 164),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi })
            using (ComboBox mode = new ComboBox { Location = new Point(18, 18), Width = 324, DropDownStyle = ComboBoxStyle.DropDownList })
            using (Label daysLabel = new Label { Text = "近", AutoSize = true, Location = new Point(18, 66) })
            using (NumericUpDown days = new NumericUpDown { Location = new Point(48, 61), Width = 76, Minimum = 2, Maximum = 14, Value = Math.Max(2, Math.Min(14, _hourlyCompareDays)) })
            using (Label suffix = new Label { Text = "天（每天一条曲线，包含所选日）", AutoSize = true, Location = new Point(134, 66) })
            using (Button cancel = new Button { Text = "取消", Location = new Point(186, 116), Size = new Size(74, 30), DialogResult = DialogResult.Cancel })
            using (Button ok = new Button { Text = "应用", Location = new Point(268, 116), Size = new Size(74, 30), DialogResult = DialogResult.OK })
            {
                mode.Items.AddRange(new object[] { "不对比", "昨天", "上周同日", "近 N 天 · 多曲线", "本周 7 天 · 多曲线" });
                mode.SelectedIndex = Math.Max(0, Math.Min(4, _hourlyCompareMode));
                Action update = delegate { bool enabled = mode.SelectedIndex == 3; days.Enabled = enabled; daysLabel.Enabled = enabled; suffix.Enabled = enabled; };
                mode.SelectedIndexChanged += delegate { update(); }; update();
                dialog.Controls.Add(mode); dialog.Controls.Add(daysLabel); dialog.Controls.Add(days); dialog.Controls.Add(suffix);
                dialog.Controls.Add(cancel); dialog.Controls.Add(ok); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _hourlyCompareMode = mode.SelectedIndex;
                if (_hourlyCompareMode == 3) _hourlyCompareDays = (int)days.Value;
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

        internal static DateTime[] HourlyComparisonDates(DateTime selected, int mode, int days)
        {
            List<DateTime> result = new List<DateTime>();
            if (mode == 1) result.Add(selected.Date.AddDays(-1));
            else if (mode == 2) result.Add(selected.Date.AddDays(-7));
            else if (mode == 3)
                for (int d = 1; d < Math.Max(2, Math.Min(14, days)); d++) result.Add(selected.Date.AddDays(-d));
            else if (mode == 4)
            {
                int offset = ((int)selected.DayOfWeek + 6) % 7;
                DateTime monday = selected.Date.AddDays(-offset);
                for (int d = 0; d < 7; d++) if (monday.AddDays(d) != selected.Date) result.Add(monday.AddDays(d));
            }
            return result.ToArray();
        }

        private sealed class HourlyLine
        {
            internal DateTime Day;
            internal string Label;
            internal double[] Values;
            internal Color Color;
            internal bool Selected;
        }

        private static Color HourlyLineColor(int index)
        {
            Color[] palette = { Color.FromArgb(239, 115, 103), Color.FromArgb(91, 192, 222), Color.FromArgb(246, 188, 92),
                Color.FromArgb(132, 204, 166), Color.FromArgb(185, 143, 214), Color.FromArgb(105, 178, 194),
                Color.FromArgb(218, 139, 184), Color.FromArgb(167, 185, 105), Color.FromArgb(226, 151, 86),
                Color.FromArgb(118, 156, 220), Color.FromArgb(204, 121, 121), Color.FromArgb(123, 194, 156),
                Color.FromArgb(197, 163, 98), Color.FromArgb(151, 137, 207) };
            return palette[Math.Abs(index) % palette.Length];
        }

        private List<HourlyLine> BuildHourlyLines(DateTime now, Color selectedColor)
        {
            List<HourlyLine> lines = new List<HourlyLine>();
            lines.Add(new HourlyLine { Day = _hourlyTrendDay, Label = _hourlyTrendDay.ToString("MM-dd ddd"),
                Values = HourlyValues(Analysis.GetDay(_hourlyTrendDay), _hourlyTrendDay, _hourlyTrendMetric, now),
                Color = selectedColor, Selected = true });
            DateTime[] dates = HourlyComparisonDates(_hourlyTrendDay, _hourlyCompareMode, _hourlyCompareDays);
            for (int i = 0; i < dates.Length; i++)
                lines.Add(new HourlyLine { Day = dates[i], Label = dates[i].ToString("MM-dd ddd"),
                    Values = HourlyValues(Analysis.GetDay(dates[i]), dates[i], _hourlyTrendMetric, now),
                    Color = HourlyLineColor(i), Selected = false });
            return lines;
        }

        private static double[] HourlyBaseline(List<HourlyLine> lines)
        {
            double[] baseline = new double[24];
            for (int h = 0; h < 24; h++)
            {
                double sum = 0; int count = 0;
                for (int i = 1; i < lines.Count; i++) if (!double.IsNaN(lines[i].Values[h])) { sum += lines[i].Values[h]; count++; }
                baseline[h] = count == 0 ? double.NaN : sum / count;
            }
            return baseline;
        }
        private string HourlyValue(double value)
        { return double.IsNaN(value) ? "无记录" : _hourlyTrendMetric >= 3 ? value.ToString("0.##") + (_hourlyTrendMetric == 3 ? " 次/秒" : " 次/分") : _hourlyTrendMetric == 2 ? value.ToString("0.#") + " 分钟"
            : (value == Math.Floor(value) ? Analysis.FmtCount((long)value) : value.ToString("0.#")) + " 次"; }

        private void PaintHourlyTrend(Graphics g)
        {
            string[] names = { "击键", "点击", "活跃时长", "击键/秒", "点击/分" };
            for (int i = 0; i < names.Length; i++) AppChip(g, 310 + i, Cx + i * 78, 98, 70, names[i], _hourlyTrendMetric == i);
            string compareButton = _hourlyCompareMode == 0 ? "曲线：仅所选日" : _hourlyCompareMode == 1 ? "曲线：昨天" :
                _hourlyCompareMode == 2 ? "曲线：上周同日" : _hourlyCompareMode == 4 ? "曲线：本周 7 天" : "曲线：近" + _hourlyCompareDays + "天";
            AppChip(g, 326, Cx + 540, 98, 190, compareButton, _hourlyCompareMode > 0);
            AppChip(g, 322, Cx + Cw - 88, 98, 88, "回今天", false);
            PaintCardBase(g, Cx, 150, Cw, 360);
            AppChip(g, 323, Cx + 16, 160, 170, _hourlyTrendDay.ToString("yyyy-MM-dd ddd"), false);
            AppChip(g, 320, Cx + Cw - 180, 160, 76, "前一天", false);
            AppChip(g, 321, Cx + Cw - 94, 160, 76, "后一天", false);
            AppText(g, "每小时" + names[_hourlyTrendMetric] + (_hourlyTrendMetric >= 3 ? " · 按活跃时间归一化" : _hourlyTrendMetric == 2 ? " · 单位：分钟" : " · 单位：次"),
                _fH2, Ctext, new RectangleF(Cx + 208, 160, 300, 30), false);

            DateTime now = DateTime.Now;
            Color selectedColor = _hourlyTrendMetric == 2 ? Cpurple : _hourlyTrendMetric == 1 ? Cgreen : Cblue;
            List<HourlyLine> lines = BuildHourlyLines(now, selectedColor);
            double[] values = lines[0].Values;
            double[] baseline = HourlyBaseline(lines);
            double selectedMax = 0, axisMax = 0, sum = 0, completeSum = 0;
            int count = 0, completeCount = 0, peak = -1;
            for (int i = 0; i < lines.Count; i++) for (int h = 0; h < 24; h++)
                if (!double.IsNaN(lines[i].Values[h]) && lines[i].Values[h] > axisMax) axisMax = lines[i].Values[h];
            for (int h = 0; h < 24; h++)
            {
                if (double.IsNaN(values[h])) continue;
                count++; sum += values[h];
                if (_hourlyTrendDay < now.Date || h < now.Hour) { completeSum += values[h]; completeCount++; }
                if (values[h] > selectedMax) { selectedMax = values[h]; peak = h; }
            }
            double ceiling = Math.Max(1, Math.Ceiling(axisMax * 1.15));
            float left = Cx + 56, right = Cx + Cw - 28, top = 222, bottom = 451;
            Func<int, float> px = delegate(int h) { return left + (right - left) * h / 23; };
            Func<double, float> py = delegate(double v) { return bottom - (float)(v / ceiling) * (bottom - top); };
            for (int i = 0; i <= 4; i++)
            {
                float y = bottom - (bottom - top) * i / 4;
                using (Pen p = new Pen(Cline)) g.DrawLine(p, left, y, right, y);
                AppText(g, Analysis.FmtAxis(ceiling * i / 4), _fAxis, Csub, new RectangleF(Cx + 4, y - 10, 46, 20), true);
            }
            for (int h = 0; h < 24; h++) if (h % 3 == 0 || h == 23)
                AppText(g, h.ToString("00") + ":00", _fAxis, Csub, new RectangleF(px(h) - 17, bottom + 6, 45, 20), false);

            if (lines.Count == 2)
                for (int h = 1; h < 24; h++)
                    if (!double.IsNaN(values[h - 1]) && !double.IsNaN(values[h]) && !double.IsNaN(baseline[h - 1]) && !double.IsNaN(baseline[h]))
                    {
                        PointF[] band = { new PointF(px(h - 1), py(values[h - 1])), new PointF(px(h), py(values[h])),
                            new PointF(px(h), py(baseline[h])), new PointF(px(h - 1), py(baseline[h - 1])) };
                        Color bandColor = values[h - 1] + values[h] >= baseline[h - 1] + baseline[h]
                            ? Color.FromArgb(24, 238, 102, 92) : Color.FromArgb(24, 73, 154, 232);
                        using (SolidBrush fill = new SolidBrush(bandColor)) g.FillPolygon(fill, band);
                    }

            for (int i = lines.Count - 1; i >= 1; i--)
            {
                HourlyLine lineData = lines[i];
                using (Pen linePen = new Pen(Color.FromArgb(205, lineData.Color), 1.55f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                using (SolidBrush dot = new SolidBrush(lineData.Color))
                    for (int h = 0; h < 24; h++)
                    {
                        if (double.IsNaN(lineData.Values[h])) continue;
                        if (h > 0 && !double.IsNaN(lineData.Values[h - 1])) g.DrawLine(linePen, px(h - 1), py(lineData.Values[h - 1]), px(h), py(lineData.Values[h]));
                        g.FillEllipse(dot, px(h) - 1.8f, py(lineData.Values[h]) - 1.8f, 3.6f, 3.6f);
                    }
            }
            if (_hourlyTrendDay == now.Date && now.Hour < 23)
            {
                float futureX = px(now.Hour) + (px(now.Hour + 1) - px(now.Hour)) / 2;
                using (SolidBrush future = new SolidBrush(Color.FromArgb(36, Cbg))) g.FillRectangle(future, futureX, top, right - futureX, bottom - top);
            }
            using (Pen linePen = new Pen(selectedColor, 2.6f))
            using (SolidBrush dot = new SolidBrush(selectedColor))
                for (int h = 0; h < 24; h++)
                {
                    if (double.IsNaN(values[h])) continue;
                    if (h > 0 && !double.IsNaN(values[h - 1])) g.DrawLine(linePen, px(h - 1), py(values[h - 1]), px(h), py(values[h]));
                    float radius = _hourlyTrendDay == now.Date && h == now.Hour ? 4.5f : 2.7f;
                    g.FillEllipse(dot, px(h) - radius, py(values[h]) - radius, radius * 2, radius * 2);
                }
            if (ThemeArt.Active && peak >= 0) ThemeArt.Sticker(g, 2, new RectangleF(Math.Max(left, Math.Min(right - 30, px(peak) - 15)), Math.Max(top - 10, py(selectedMax) - 32), 30, 30));

            using (Pen legendMain = new Pen(selectedColor, 2.6f)) g.DrawLine(legendMain, Cx + 500, 205, Cx + 522, 205);
            AppText(g, "所选日", _fSmall, Ctext, new RectangleF(Cx + 528, 196, 52, 20), false);
            if (lines.Count > 1)
            {
                using (Pen legendOthers = new Pen(lines[1].Color, 1.7f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(legendOthers, Cx + 588, 205, Cx + 610, 205);
                AppText(g, "另 " + (lines.Count - 1) + " 日 · 悬停看明细", _fSmall, Csub, new RectangleF(Cx + 616, 196, 150, 20), false);
            }

            string detail = count == 0 ? "所选日期没有此指标的小时记录。" : lines.Count == 1
                ? "当前小时使用加大圆点，阴影区域尚未来到。" : "每个日期一条曲线；所选日加粗，摘要以其余日期的同期均值为基准。";
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            if (new RectangleF(left - 8, top, right - left + 16, bottom - top + 24).Contains(mouse))
            {
                int h = Math.Max(0, Math.Min(23, (int)Math.Round((mouse.X - left) / (right - left) * 23)));
                using (Pen p = new Pen(Csub) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot }) g.DrawLine(p, px(h), top, px(h), bottom);
                detail = h.ToString("00") + ":00—" + (h + 1).ToString("00") + ":00 · " + lines.Count + " 条曲线";
                int shown = Math.Min(7, lines.Count);
                float boxW = 176, boxH = 28 + shown * 18;
                float boxX = mouse.X < (left + right) / 2 ? right - boxW - 10 : left + 10;
                float boxY = top + 10;
                PaintCardBase(g, boxX, boxY, boxW, boxH);
                AppText(g, h.ToString("00") + ":00 小时明细", _fSmall, Ctext, new RectangleF(boxX + 10, boxY + 6, boxW - 20, 18), false);
                for (int i = 0; i < shown; i++)
                {
                    using (SolidBrush b = new SolidBrush(lines[i].Color)) g.FillEllipse(b, boxX + 10, boxY + 31 + i * 18, 7, 7);
                    AppText(g, lines[i].Label, _fAxis, Csub, new RectangleF(boxX + 23, boxY + 25 + i * 18, 82, 18), false);
                    AppText(g, HourlyValue(lines[i].Values[h]), _fAxis, i == 0 ? Ctext : Csub, new RectangleF(boxX + 102, boxY + 25 + i * 18, 66, 18), true);
                }
            }
            AppText(g, detail, _fSmall, Csub, new RectangleF(Cx + 18, 482, Cw - 36, 22), false);

            double baselineSum = 0; int baselineCount = 0;
            for (int h = 0; h < 24; h++) if (!double.IsNaN(values[h]) && !double.IsNaN(baseline[h])) { baselineSum += baseline[h]; baselineCount++; }
            if (_hourlyTrendMetric >= 3)
            {
                sum = count == 0 ? double.NaN : sum / count;
                if (baselineCount > 0) baselineSum /= baselineCount;
            }
            double delta = lines.Count <= 1 || baselineCount == 0 ? double.NaN : sum - baselineSum;
            string deltaText = double.IsNaN(delta) ? "--" : (delta >= 0 ? "+" : "") + HourlyValue(delta);
            string percentText = double.IsNaN(delta) || baselineSum == 0 ? "--" : (delta >= 0 ? "+" : "") + (delta / baselineSum * 100).ToString("0.#") + "%";
            string baselineLabel = lines.Count == 2 ? lines[1].Label : "其余 " + (lines.Count - 1) + " 日同期均值";
            string[] labels = { _hourlyTrendMetric >= 3 ? "所选日小时均值" : "所选日合计", lines.Count == 1 ? "已过小时均值" : baselineLabel,
                lines.Count == 1 ? "最高小时量" : "相差数量", lines.Count == 1 ? "最忙时段" : "相差比例" };
            string[] numbers = { count == 0 ? "--" : HourlyValue(sum), lines.Count == 1 ? (completeCount == 0 ? "--" : HourlyValue(completeSum / completeCount)) : (baselineCount == 0 ? "--" : HourlyValue(baselineSum)),
                lines.Count == 1 ? (count == 0 ? "--" : HourlyValue(selectedMax)) : deltaText,
                lines.Count == 1 ? (peak < 0 ? "--" : peak.ToString("00") + ":00—" + (peak + 1).ToString("00") + ":00") : percentText };
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
                Color selectedColor = _hourlyTrendMetric == 2 ? Cpurple : _hourlyTrendMetric == 1 ? Cgreen : Cblue;
                List<HourlyLine> lines = BuildHourlyLines(now, selectedColor);
                string name = _hourlyTrendMetric == 3 ? "每活跃秒击键" : _hourlyTrendMetric == 4 ? "每活跃分钟点击" :
                    _hourlyTrendMetric == 2 ? "活跃分钟" : _hourlyTrendMetric == 1 ? "点击次数" : "击键次数";
                StringBuilder csv = new StringBuilder("小时");
                for (int i = 0; i < lines.Count; i++) csv.Append(',').Append(lines[i].Day.ToString("yyyy-MM-dd")).Append(name);
                csv.AppendLine();
                for (int h = 0; h < 24; h++)
                {
                    csv.Append(h.ToString("00")).Append(":00");
                    for (int i = 0; i < lines.Count; i++) csv.Append(',').Append(double.IsNaN(lines[i].Values[h]) ? "" : lines[i].Values[h].ToString("0.###", CultureInfo.InvariantCulture));
                    csv.AppendLine();
                }
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "小时多曲线_" + _hourlyTrendDay.ToString("yyyyMMdd") + "_" + DateTime.Now.ToString("HHmmss_fff") + ".csv");
                File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出：" + Environment.NewLine + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
