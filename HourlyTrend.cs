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
        private int _hourlyCompareDays = 1; // 1=昨天，7=上周同日

        private void ApplyHourlyTrendChip(int id)
        {
            if (id == 300 || id == 301) { _trendHourly = id == 301; _hover = -1; }
            if (id >= 310 && id <= 314) _hourlyTrendMetric = id - 310;
            if (id == 320) _hourlyTrendDay = _hourlyTrendDay.AddDays(-1);
            if (id == 321) _hourlyTrendDay = _hourlyTrendDay.AddDays(1);
            if (id == 322) _hourlyTrendDay = DateTime.Today;
            if (id == 324) _hourlyCompareDays = 1;
            if (id == 325) _hourlyCompareDays = 7;
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
        { return selected.Date.AddDays(compareDays == 7 ? -7 : -1); }

        private string HourlyValue(double value)
        { return double.IsNaN(value) ? "无记录" : _hourlyTrendMetric >= 3 ? value.ToString("0.##") + (_hourlyTrendMetric == 3 ? " 次/秒" : " 次/分") : _hourlyTrendMetric == 2 ? value.ToString("0.#") + " 分钟"
            : (value == Math.Floor(value) ? Analysis.FmtCount((long)value) : value.ToString("0.#")) + " 次"; }

        private void PaintHourlyTrend(Graphics g)
        {
            string[] names = { "击键", "点击", "活跃时长", "击键/秒", "点击/分" };
            for (int i = 0; i < names.Length; i++) AppChip(g, 310 + i, Cx + i * 78, 98, 70, names[i], _hourlyTrendMetric == i);
            AppChip(g, 324, Cx + 540, 98, 82, "对比昨天", _hourlyCompareDays == 1);
            AppChip(g, 325, Cx + 630, 98, 100, "对比上周同日", _hourlyCompareDays == 7);
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
            double[] comparison = HourlyValues(Analysis.GetDay(compareDay), compareDay, _hourlyTrendMetric, now);
            double max = 0, sum = 0, completeSum = 0; int count = 0, completeCount = 0, peak = -1;
            for (int h = 0; h < 24; h++)
            {
                if (double.IsNaN(values[h])) continue;
                count++; sum += values[h];
                if (_hourlyTrendDay < now.Date || h < now.Hour) { completeSum += values[h]; completeCount++; }
                if (values[h] > max) { max = values[h]; peak = h; }
            }
            for (int h = 0; h < 24; h++) if (!double.IsNaN(comparison[h]) && comparison[h] > max) max = comparison[h];
            double ceiling = Math.Max(1, Math.Ceiling(max * 1.15));
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
            using (Pen compareLine = new Pen(compareColor, 1.7f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (SolidBrush compareDot = new SolidBrush(compareColor))
                for (int h = 0; h < 24; h++)
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
            if(ThemeArt.Active && peak>=0)ThemeArt.Sticker(g,2,new RectangleF(Math.Max(left,Math.Min(right-30,px(peak)-15)),Math.Max(top-10,py(max)-32),30,30));
            string compareLabel = _hourlyCompareDays == 1 ? "昨天" : "上周同日";
            using (Pen legendMain = new Pen(color, 2.4f)) g.DrawLine(legendMain, Cx + 500, 205, Cx + 522, 205);
            AppText(g, "所选日", _fSmall, Ctext, new RectangleF(Cx + 528, 196, 52, 20), false);
            using (Pen legendCompare = new Pen(compareColor, 1.8f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(legendCompare, Cx + 588, 205, Cx + 610, 205);
            AppText(g, compareLabel, _fSmall, Csub, new RectangleF(Cx + 616, 196, 88, 20), false);
            string detail = count == 0 ? "所选日期没有此指标的小时记录。" : "实线为所选日，虚线为" + compareLabel + "；当前小时未结束，未来小时留空。";
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            if (new RectangleF(left - 8, top, right - left + 16, bottom - top + 24).Contains(mouse))
            {
                int h = Math.Max(0, Math.Min(23, (int)Math.Round((mouse.X - left) / (right - left) * 23)));
                using (Pen p = new Pen(Csub) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot }) g.DrawLine(p, px(h), top, px(h), bottom);
                detail = _hourlyTrendDay.ToString("MM-dd") + "  " + h.ToString("00") + ":00—" + (h + 1).ToString("00") + ":00  ·  "
                    + HourlyValue(values[h]) + (_hourlyTrendDay == now.Date && h == now.Hour ? "（累计中）" : "")
                    + "    " + compareDay.ToString("MM-dd") + " · " + HourlyValue(comparison[h]);
            }
            AppText(g, detail, _fSmall, Csub, new RectangleF(Cx + 18, 482, Cw - 36, 22), false);
            if (_hourlyTrendMetric >= 3)
            {
                DayRecord day = Analysis.GetDay(_hourlyTrendDay);
                sum = day == null ? double.NaN : PersonalStats.Ratio(_hourlyTrendMetric == 3 ? day.Keys : day.Clicks * 60.0, day.ActiveSeconds);
            }
            string[] labels = { _hourlyTrendMetric >= 3 ? "日整体密度" : "已记录合计", "已过小时均值", "最高小时量", "最忙时段" };
            string[] numbers = { count == 0 ? "--" : HourlyValue(sum), completeCount == 0 ? "--" : HourlyValue(completeSum / completeCount),
                count == 0 ? "--" : HourlyValue(max), peak < 0 ? "--" : peak.ToString("00") + ":00—" + (peak + 1).ToString("00") + ":00" };
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
                double[] comparison = HourlyValues(Analysis.GetDay(compareDay), compareDay, _hourlyTrendMetric, now);
                string name = _hourlyTrendMetric == 3 ? "每活跃秒击键" : _hourlyTrendMetric == 4 ? "每活跃分钟点击" : _hourlyTrendMetric == 2 ? "活跃分钟" : _hourlyTrendMetric == 1 ? "点击次数" : "击键次数";
                StringBuilder csv = new StringBuilder("日期,对比日期,小时,所选日" + name + ",对比日" + name + ",状态\r\n");
                for (int h = 0; h < 24; h++) csv.Append(_hourlyTrendDay.ToString("yyyy-MM-dd")).Append(',').Append(compareDay.ToString("yyyy-MM-dd")).Append(',').Append(h).Append(',')
                    .Append(double.IsNaN(values[h]) ? "" : values[h].ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(double.IsNaN(comparison[h]) ? "" : comparison[h].ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .AppendLine(double.IsNaN(values[h]) ? "无记录或尚未来到" : _hourlyTrendDay == now.Date && h == now.Hour ? "累计中" : "已记录部分");
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "小时趋势_" + _hourlyTrendDay.ToString("yyyyMMdd") + "_" + DateTime.Now.ToString("HHmmss_fff") + ".csv");
                File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
