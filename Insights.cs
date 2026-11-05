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
    internal static class TimeInsights
    {
        public static bool HasTime(DayRecord day)
        { return day != null && (day.ActiveSeconds > 0 || day.IdleSeconds > 0 || day.Sessions.Count > 0); }

        public static double MeanSession(DayRecord day)
        {
            if (day == null) return 0;
            double sum = 0; int count = 0;
            foreach (ActiveSession s in day.Sessions) if (s.Seconds > 0) { sum += s.Seconds; count++; }
            return count == 0 ? 0 : sum / count;
        }

        public static List<ActiveSession> LongGaps(DayRecord day, double threshold)
        {
            List<ActiveSession> result = new List<ActiveSession>();
            if (day != null) foreach (ActiveSession s in day.IdlePeriods)
                if (s.Seconds >= threshold) result.Add(s);
            result.Sort(delegate(ActiveSession a, ActiveSession b) { return b.Seconds.CompareTo(a.Seconds); });
            return result;
        }

        public static string Export(DayRecord day)
        {
            StringBuilder text = new StringBuilder("类型,开始,结束,秒数\r\n");
            if (day == null) return text.ToString();
            for (int kind = 0; kind < 2; kind++)
                foreach (ActiveSession s in kind == 0 ? day.Sessions : day.IdlePeriods)
                    text.Append(kind == 0 ? "活跃" : "观测空闲").Append(',')
                        .Append(s.Start.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(',')
                        .Append(s.End.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(',')
                        .AppendLine(s.Seconds.ToString("0.###", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }

    internal sealed partial class Dashboard
    {
        private int _insightView, _calendarMetric, _sessionPage;
        private int _calendarSpan = -1;   // 0 近3个月 1 近半年 2 全年,-1 按数据量自动
        private DateTime _insightDay = DateTime.Today;
        private readonly Dictionary<RectangleF, DateTime> _insightDates = new Dictionary<RectangleF, DateTime>();

        private void ApplyInsightChip(int id)
        {
            if (id == 203) { using (StatisticsReport report = new StatisticsReport(_insightDay)) report.ShowDialog(this); return; }
            if (id == 204) { using (StatisticsReport report = new StatisticsReport(_insightDay, 11)) report.ShowDialog(this); return; }
            if (id >= 200 && id <= 202) { _tab = TabId.Insights; _insightView = id - 200; }
            if (id == 210 || id == 211) _calendarMetric = id - 210;
        if (id >= 212 && id <= 214) { _calendarSpan = id - 212; return; }
            if (id == 220) _insightDay = _insightDay.AddDays(-1);
            if (id == 221) _insightDay = _insightDay.AddDays(1);
            if (id == 222) _insightDay = DateTime.Today;
            if (_insightDay < DateTime.Today.AddDays(-364)) _insightDay = DateTime.Today.AddDays(-364);
            if (_insightDay > DateTime.Today) _insightDay = DateTime.Today;
            if (id == 230) _sessionPage = Math.Max(0, _sessionPage - 1);
            else if (id == 231) _sessionPage++;
            else _sessionPage = 0;
        }

        private bool HandleInsightClick(PointF point)
        {
            foreach (KeyValuePair<RectangleF, DateTime> cell in _insightDates)
                if (cell.Key.Contains(point)) { _insightDay = cell.Value; _insightView = 1; _sessionPage = 0; return true; }
            return false;
        }

        private void InsightText(Graphics g, string text, float x, float y, float width, Font font, Color color)
        { AppText(g, text, font, color, new RectangleF(x, y, width, 24), false); }

        private Color Heat(double value, double maximum, bool known)
        {
            if (!known) return Cbg;
            if(ThemeArt.Active)return value<=0||maximum<=0?HeatScale.Zero:HeatScale.At(value/maximum);
            if (value <= 0 || maximum <= 0) return ArtTheme.Mix(Ccard, Ctext, 0.12);
            double fraction = Math.Sqrt(Math.Min(1, value / maximum));
            return ArtTheme.Mix(Ccard2, Cgreen, 0.2 + 0.8 * fraction);
        }

        private void PaintInsights(Graphics g)
        {
            _insightDates.Clear();
            string[] names = { "年度日历", "会话时间轴", "七日节奏" };
            for (int i = 0; i < 3; i++) AppChip(g, 200 + i, Cx + i * 114, 98, 104, names[i], _insightView == i);
            if (_insightView == 2)
            {
                AppChip(g, 203, Cx + 354, 98, 112, "统计报告", false);
                AppChip(g, 204, Cx + 474, 98, 118, "本周回顾", false);
            }
            if (_insightView == 0) PaintCalendar(g);
            else if (_insightView == 1) PaintSessionTimeline(g);
            else PaintWeekActivity(g);
        }

        private void PaintCalendar(Graphics g)
        {
            // 先按数据量决定默认窗口:不足 3 个月就先放大看近 3 个月,避免整屏斜线。
            int knownInYear = 0;
            DateTime yearFirst = DateTime.Today.AddDays(-364);
            for (DateTime d = yearFirst; d <= DateTime.Today; d = d.AddDays(1))
            {
                DayRecord probe = Analysis.GetDay(d);
                if (_calendarMetric == 0 ? TimeInsights.HasTime(probe) : probe != null && !probe.IsEmpty) knownInYear++;
            }
            if (_calendarSpan < 0) _calendarSpan = knownInYear < 90 ? 0 : knownInYear < 180 ? 1 : 2;

            int weeks = _calendarSpan == 0 ? 13 : _calendarSpan == 1 ? 26 : 53;
            DateTime first = DateTime.Today.AddDays(-(weeks * 7 - 1));
            DateTime monday = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
            int columns = Math.Max(1, (int)Math.Ceiling((DateTime.Today - monday).TotalDays + 1) / 7);
            float maxCell = _calendarSpan == 0 ? 34 : _calendarSpan == 1 ? 24 : 13;
            float gap = _calendarSpan == 2 ? 2 : 4;
            float cardH = _calendarSpan == 2 ? 340 : _calendarSpan == 1 ? 380 : 430;
            float cell = Math.Min(maxCell, (Cw - 110) / (float)columns - gap);
            if (cell < 8) cell = 8;
            float gridW = columns * (cell + gap) - gap;
            float gridX = Cx + Math.Max(42, (Cw - gridW) / 2);
            float gridY = 230;

            double maximum = 0, total = 0; int knownDays = 0, activeDays = 0, anomalies = 0;
            int bestStreak = 0, streak = 0, weekendKnown = 0, weekendActive = 0;
            double[] weekdaySum = new double[7];
            for (DateTime d = first; d <= DateTime.Today; d = d.AddDays(1))
            {
                DayRecord day = Analysis.GetDay(d);
                bool known = _calendarMetric == 0 ? TimeInsights.HasTime(day) : day != null && !day.IsEmpty;
                if (!known) { streak = 0; continue; }
                double value = _calendarMetric == 0 ? day.ActiveSeconds : day.Keys;
                maximum = Math.Max(maximum, value); total += value; knownDays++;
                if (value > 0) activeDays++;
                if (value > 0) { streak++; if (streak > bestStreak) bestStreak = streak; } else streak = 0;
                int weekday = ((int)d.DayOfWeek + 6) % 7;
                weekdaySum[weekday] += value;
                if (weekday >= 5) { weekendKnown++; if (value > 0) weekendActive++; }
                if (DailySignal.Get(d).High) anomalies++;
            }

            PaintCardBase(g, Cx, 145, Cw, cardH);
            AppChip(g, 210, Cx + Cw - 196, 98, 88, "活跃时长", _calendarMetric == 0);
            AppChip(g, 211, Cx + Cw - 100, 98, 92, "击键次数", _calendarMetric == 1);
            AppChip(g, 212, Cx + 352, 98, 82, "近3月", _calendarSpan == 0);
            AppChip(g, 213, Cx + 440, 98, 82, "近半年", _calendarSpan == 1);
            AppChip(g, 214, Cx + 528, 98, 82, "全年", _calendarSpan == 2);

            InsightText(g, first.ToString("yyyy.MM.dd") + " — " + DateTime.Today.ToString("yyyy.MM.dd"), Cx + 18, 157, 300, _fH2, Ctext);
            InsightText(g, "点击任意日期查看会话 · 悬停查看数值", Cx + 18, 184, 310, _fBody, Csub);

            // 原来占半屏的累计卡片,拆成日历右上角的三个紧凑指标标签。
            string totalText = _calendarMetric == 0 ? "累计活跃  " + ActivityMonitor.FormatDuration(total) : "累计击键  " + Analysis.FmtCount((long)total);
            string[] chipTexts = {
                totalText,
                "活跃 " + activeDays + " 天 / 记录 " + knownDays + " 天",
                "异常 " + anomalies + " 天" };
            Color[] chipInks = { Cgreen, Ctext, Corange };
            float chipW = 166, chipX = Cx + Cw - 3 * (chipW + 8) - 14, chipY = 150;
            for (int i = 0; i < chipTexts.Length; i++)
            {
                RectangleF pill = new RectangleF(chipX + i * (chipW + 8), chipY, chipW, 34);
                using (GraphicsPath path = RoundedRect(pill.X, pill.Y, pill.Width, pill.Height, 8))
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(14, Ctext))) g.FillPath(fill, path);
                    using (Pen pen = new Pen(Color.FromArgb(30, Ctext))) g.DrawPath(pen, path);
                }
                InsightText(g, chipTexts[i], pill.X + 10, pill.Y + 8, chipW - 20, _fSmall, chipInks[i]);
            }

            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            string detail = "";
            string[] weekdayNames = { "一", "二", "三", "四", "五", "六", "日" };
            for (int row = 0; row < 7; row++)
                InsightText(g, weekdayNames[row], gridX - 20, gridY + row * (cell + gap) + cell / 2 - 8, 20, _fAxis, Csub);

            string lastMonth = "";
            for (DateTime d = first; d <= DateTime.Today; d = d.AddDays(1))
            {
                int offset = (int)(d - monday).TotalDays;
                RectangleF rect = new RectangleF(gridX + offset / 7 * (cell + gap), gridY + offset % 7 * (cell + gap), cell, cell);
                if (!string.IsNullOrEmpty(lastMonth) || d.Day == 1 || d == first)
                {
                    if (d.Day == 1 || d == first)
                    {
                        string month = d.ToString("M月");
                        if (month != lastMonth)
                        {
                            // 月份标签左对齐到该月第一周所在列,而不是那一天所在的列。
                            float monthX = gridX + (offset / 7) * (cell + gap);
                            InsightText(g, month, monthX, gridY - 18, 46, _fAxis, Csub);
                            lastMonth = month;
                        }
                    }
                }
                DayRecord day = Analysis.GetDay(d);
                bool known = _calendarMetric == 0 ? TimeInsights.HasTime(day) : day != null && !day.IsEmpty;
                double value = !known ? 0 : _calendarMetric == 0 ? day.ActiveSeconds : day.Keys;
                using (GraphicsPath path = RoundedRect(rect.X, rect.Y, rect.Width, rect.Height, 2))
                using (SolidBrush b = new SolidBrush(Heat(value, maximum, known))) g.FillPath(b, path);
                DailySignal signal = DailySignal.Get(d);
                if (signal.High) using (Pen anomaly = new Pen(Corange, 1.6f)) g.DrawRectangle(anomaly, rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
                if (d == DateTime.Today)
                {
                    using (Pen glow = new Pen(Color.FromArgb(70, Cblue), 4f)) g.DrawRectangle(glow, rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
                    using (Pen todayPen = new Pen(Cblue, 2.2f)) g.DrawRectangle(todayPen, rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
                }
                if (!known) using (Pen p = new Pen(Color.FromArgb(65, Csub))) g.DrawLine(p, rect.Left + 2, rect.Bottom - 2, rect.Right - 2, rect.Top + 2);
                _insightDates[rect] = d;
                if (rect.Contains(mouse))
                {
                    using (Pen p = new Pen(Cblue, 2)) g.DrawRectangle(p, rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
                    detail = d.ToString("yyyy-MM-dd ddd") + "  ·  点击查看当天详情  ·  " + (!known ? "无记录" : _calendarMetric == 0
                        ? ActivityMonitor.FormatDuration(value) : Analysis.FmtCount((long)value) + " 次击键");
                    if (signal.High) detail += " · 击键" + signal.Note;
                }
            }

            float detailY = gridY + 7 * (cell + gap) - gap + 14;
            InsightText(g, detail.Length > 0 ? detail : "斜线=无记录 · 颜色越浓=数值越高 · 橙框=历史异常 · 蓝框=今天", Cx + 18, detailY, Cw - 36, _fSmall, Csub);
            int busiest = 0;
            for (int i = 1; i < 7; i++) if (weekdaySum[i] > weekdaySum[busiest]) busiest = i;
            if (knownDays > 0)
            {
                string weekend = weekendKnown > 0 ? (weekendActive * 100 / weekendKnown) + "%" : "--";
                string habit = "最长连续 " + bestStreak + " 天 · 周末活跃率 " + weekend + " · 主力 " + weekdayNames[busiest];
                InsightText(g, habit, Cx + 18, detailY + 26, Cw - 36, _fBody, Cgreen);
            }
        }

        private void PaintSessionTimeline(Graphics g)
        {
            AppChip(g, 220, 602, 98, 72, "前一天", false);
            AppChip(g, 222, 684, 98, 80, "回今天", false);
            AppChip(g, 221, 774, 98, 82, "后一天", false);
            DayRecord day = Analysis.GetDay(_insightDay);
            bool known = TimeInsights.HasTime(day);
            List<ActiveSession> gaps = TimeInsights.LongGaps(day, 15 * 60);
            string[] labels = { "活跃时长", "平均活跃段", "最长活跃段", "长空闲 ≥ 15 分钟" };
            double longest = 0;
            if (day != null) foreach (ActiveSession s in day.Sessions) longest = Math.Max(longest, s.Seconds);
            string[] values = { known ? ActivityMonitor.FormatDuration(day.ActiveSeconds) : "无时间记录",
                day != null && day.Sessions.Count > 0 ? ActivityMonitor.FormatDuration(TimeInsights.MeanSession(day)) : "--",
                longest > 0 ? ActivityMonitor.FormatDuration(longest) : "--",
                known && (day.IdlePeriods.Count > 0 || day.IdleSeconds == 0) ? gaps.Count + " 段" : "未定位" };
            for (int i = 0; i < 4; i++)
            {
                float x = Cx + i * 211;
                PaintCardBase(g, x, 145, 199, 73);
                InsightText(g, labels[i], x + 12, 151, 175, _fBody, Csub);
                InsightText(g, values[i], x + 12, 182, 175, _fH2, i == 3 ? Corange : Cgreen);
            }
            PaintCardBase(g, Cx, 232, Cw, 151);
            InsightText(g, _insightDay.ToString("yyyy.MM.dd ddd") + " · 全天时间轴", Cx + 16, 240, 420, _fH2, Ctext);
            InsightText(g, "绿：活跃  橙：观测空闲  底色：无区间记录", Cx + 420, 240, 395, _fSmall, Csub);
            RectangleF track = new RectangleF(Cx + 18, 286, Cw - 36, 34);
            using (SolidBrush b = new SolidBrush(Cbg)) g.FillRectangle(b, track);
            string detail = known ? "悬停色块查看起止时间与持续时长；短时段以最小 1 像素显示。" : "这一天尚无时间数据。";
            if (day != null)
            {
                PaintPeriods(g, day.IdlePeriods, track, Corange, "空闲", ref detail);
                PaintPeriods(g, day.Sessions, track, Cgreen, "活跃", ref detail);
            }
            // 加厚时间轴 + 每小时刻度线,让它更像一把 24 小时尺子。
            for (int h = 0; h <= 24; h++)
            {
                float tx = track.X + track.Width * h / 24f;
                using (Pen tick = new Pen(Color.FromArgb(h % 3 == 0 ? 55 : 22, Ctext), h % 3 == 0 ? 1.2f : 0.8f))
                    g.DrawLine(tick, tx, track.Bottom - (h % 3 == 0 ? 8 : 4), tx, track.Bottom);
            }
            for (int h = 0; h <= 24; h += 3)
                InsightText(g, h.ToString("00") + ":00", Math.Max(track.X, Math.Min(track.Right - 36, track.X + track.Width * h / 24 - 18)), 323, 42, _fAxis, Csub);
            InsightText(g, detail, Cx + 18, 352, Cw - 36, _fSmall, Csub);
            PaintCardBase(g, Cx, 397, 410, 189);
            InsightText(g, "活跃段明细", Cx + 16, 404, 160, _fH2, Ctext);
            int count = day == null ? 0 : day.Sessions.Count;
            _sessionPage = Math.Min(_sessionPage, Math.Max(0, (count - 1) / 4));
            AppChip(g, 230, Cx + 241, 404, 68, "上一页", false);
            AppChip(g, 231, Cx + 319, 404, 76, "下一页", false);
            for (int i = 0; i < 4 && _sessionPage * 4 + i < count; i++)
            {
                ActiveSession s = day.Sessions[_sessionPage * 4 + i];
                InsightText(g, s.Start.ToString("HH:mm:ss") + " — " + s.End.ToString("HH:mm:ss") + "    " + ActivityMonitor.FormatDuration(s.Seconds), Cx + 16, 437 + i * 27, 385, _fBody, Ctext);
            }
            InsightText(g, count == 0 ? "暂无会话记录" : "共 " + count + " 段 · 第 " + (_sessionPage + 1) + " 页", Cx + 16, 554, 370, _fSmall, Csub);
            PaintCardBase(g, Cx + 422, 397, 410, 189);
            InsightText(g, "最长空档 · 观测空闲 Top 3", Cx + 438, 404, 378, _fH2, Ctext);
            for (int i = 0; i < Math.Min(3, gaps.Count); i++)
                InsightText(g, gaps[i].Start.ToString("HH:mm") + " — " + gaps[i].End.ToString("HH:mm") + "    " + ActivityMonitor.FormatDuration(gaps[i].Seconds), Cx + 438, 437 + i * 27, 378, _fBody, Corange);
            if (gaps.Count == 0) InsightText(g, "暂无已定位的 15 分钟以上空闲", Cx + 438, 440, 378, _fBody, Csub);
            // 右下方补齐「今日工时节奏」两枚小指标,不再留一块空地。
            double observed = day == null ? 0 : day.ActiveSeconds + day.IdleSeconds;
            string focusText = observed > 0 ? (100.0 * day.ActiveSeconds / observed).ToString("0", CultureInfo.InvariantCulture) + "%" : "--";
            string restText = known ? gaps.Count + " 次" : "--";
            PaintCardBase(g, Cx + 438, 520, 186, 46);
            InsightText(g, "专注占比 · 活跃/观测", Cx + 448, 523, 160, _fSmall, Csub);
            InsightText(g, focusText, Cx + 448, 538, 160, _fBody, Cgreen);
            PaintCardBase(g, Cx + 634, 520, 198, 46);
            InsightText(g, "工间休息 · ≥15 分钟", Cx + 644, 523, 178, _fSmall, Csub);
            InsightText(g, restText, Cx + 644, 538, 178, _fBody, Corange);
            InsightText(g, "旧版只有空闲总量，无法定位；锁屏不计空闲。", Cx + 438, 570, 378, _fSmall, Csub);
        }

        private void PaintPeriods(Graphics g, List<ActiveSession> periods, RectangleF track, Color color, string label, ref string detail)
        {
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            foreach (ActiveSession s in periods)
            {
                double from = Math.Max(0, (s.Start - _insightDay).TotalSeconds);
                double until = Math.Min(86400, (s.End - _insightDay).TotalSeconds);
                if (until <= from) continue;
                RectangleF rect = new RectangleF(track.X + (float)(from / 86400 * track.Width), track.Y,
                    Math.Max(1, (float)((until - from) / 86400 * track.Width)), track.Height);
                using (SolidBrush b = new SolidBrush(color)) g.FillRectangle(b, rect);
                if (rect.Contains(mouse)) detail = label + "  " + s.Start.ToString("HH:mm:ss") + " — " + s.End.ToString("HH:mm:ss") + "  ·  " + ActivityMonitor.FormatDuration(s.Seconds);
            }
        }

        private void PaintWeekActivity(Graphics g)
        {
            PaintCardBase(g, Cx, 145, Cw, 321);
            InsightText(g, "近 7 天 × 24 小时 · 每格活跃分钟数", Cx + 18, 153, Cw - 36, _fH2, Ctext);
            InsightText(g, "悬停查看时段 · 点击日期查看会话", Cx + 18, 181, 370, _fBody, Csub);
            HeatScale.Bar(g,new RectangleF(Cx+489,184,220,9));
            InsightText(g,"0+",Cx+489,195,40,_fAxis,Csub);
            InsightText(g,"30",Cx+590,195,40,_fAxis,Csub);
            InsightText(g,"60 分钟",Cx+692,195,90,_fAxis,Csub);
            double busiest = 0, bestHour = 0; DateTime bestDay = DateTime.Today; int peak = 0, sampled = 0;
            double[] hours = new double[24];
            string detail = "蓝 → 红：活跃分钟数线性增加（0—60）；灰色：零值；斜线：无记录 / 尚未来到。";
            PointF mouse = ToBase(_mouse); mouse = new PointF(mouse.X - ContentX, mouse.Y - ContentY);
            for (int h = 0; h < 24; h += 3) InsightText(g, h.ToString("00"), Cx + 112 + h * 25, 213, 30, _fAxis, Csub);
            DateTime[] weekDates = new DateTime[7];
            DayRecord[] weekDays = new DayRecord[7];
            bool[] weekKnown = new bool[7];
            double maxDayActive = 0;
            for (int i = 0; i < 7; i++)
            {
                weekDates[i] = DateTime.Today.AddDays(i - 6);
                weekDays[i] = Analysis.GetDay(weekDates[i]);
                weekKnown[i] = TimeInsights.HasTime(weekDays[i]);
                if (weekKnown[i] && weekDays[i].ActiveSeconds > maxDayActive) maxDayActive = weekDays[i].ActiveSeconds;
            }
            for (int i = 0; i < 7; i++)
            {
                DateTime date = weekDates[i];
                DayRecord day = weekDays[i];
                bool known = weekKnown[i];
                float y = 244 + i * 25;
                InsightText(g, date.ToString("MM.dd ddd"), Cx + 16, y - 3, 94, _fSmall, Csub);
                _insightDates[new RectangleF(Cx + 12, y, 800, 22)] = date;
                if (known) { sampled++; if (day.ActiveSeconds > busiest) { busiest = day.ActiveSeconds; bestDay = date; } }
                for (int h = 0; h < 24; h++)
                {
                    bool observed = known && (date < DateTime.Today || h <= DateTime.Now.Hour);
                    double seconds = observed ? day.ActiveHours[h] : 0;
                    hours[h] += seconds;
                    RectangleF cell = new RectangleF(Cx + 112 + h * 25 + 1, y + 1, 19, 17);
                    using (GraphicsPath path = RoundedRect(cell.X, cell.Y, cell.Width, cell.Height, 3))
                    using (SolidBrush b = new SolidBrush(!observed ? Cbg : seconds <= 0 ? HeatScale.Zero : HeatScale.At(seconds / 3600)))
                        g.FillPath(b, path);
                    if (!observed) using (Pen p = new Pen(Color.FromArgb(65, Csub))) g.DrawLine(p, cell.Left + 3, cell.Bottom - 3, cell.Right - 3, cell.Top + 3);
                    if (cell.Contains(mouse)) using (Pen outline = new Pen(Cblue, 2)) g.DrawRectangle(outline, cell.X - 1, cell.Y - 1, cell.Width + 2, cell.Height + 2);
                    if (cell.Contains(mouse)) detail = date.ToString("MM.dd ddd") + "  " + h.ToString("00") + ":00—" + (h + 1).ToString("00") + ":00  ·  点击查看当天详情  ·  " + (observed ? (seconds / 60).ToString("0.#") + " 分钟活跃（已采集部分）" : "无记录");
                }
                InsightText(g, known ? Analysis.FmtHours(day.ActiveSeconds / 3600) : "无记录", Cx + 724, y - 3, 100, _fSmall, known ? Cgreen : Csub);
                // 右侧汇总加一条迷你条,横向对比哪天更累。
                RectangleF miniTrack = new RectangleF(Cx + 724, y + 12, 96, 4);
                using (SolidBrush track = new SolidBrush(Color.FromArgb(20, Ctext))) g.FillRectangle(track, miniTrack);
                if (known && maxDayActive > 0)
                {
                    float width = (float)(miniTrack.Width * day.ActiveSeconds / maxDayActive);
                    using (GraphicsPath bar = RoundedRect(miniTrack.X, miniTrack.Y, Math.Max(2, width), miniTrack.Height, 2))
                    using (SolidBrush fill = new SolidBrush(Cgreen)) g.FillPath(fill, bar);
                }
            }
            InsightText(g, detail, Cx + 18, 432, Cw - 36, _fSmall, Csub);
            for (int h = 0; h < 24; h++) if (hours[h] > bestHour) { bestHour = hours[h]; peak = h; }
            PaintCardBase(g, Cx, 480, Cw, 106);
            InsightText(g, busiest > 0 ? "最活跃的一天  " + bestDay.ToString("MM.dd ddd") + "  ·  " + ActivityMonitor.FormatDuration(busiest) : "暂无足够的活跃记录", Cx + 18, 487, Cw - 36, _fH2, Ctext);
            InsightText(g, bestHour > 0 ? "最忙时段  " + peak.ToString("00") + ":00—" + (peak + 1).ToString("00") + ":00  ·  七日累计 " + ActivityMonitor.FormatDuration(bestHour) : "继续使用后，这里会显示你的活跃高峰。", Cx + 18, 519, Cw - 36, _fBody, Cgreen);
            InsightText(g, "覆盖 " + sampled + " / 7 天；仅比较已采集部分，单周结果不代表长期星期偏好。", Cx + 18, 550, Cw - 36, _fSmall, Csub);
        }

        private void ExportInsights()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "时间区间_" + _insightDay.ToString("yyyyMMdd") + "_" + DateTime.Now.ToString("HHmmss_fff") + ".csv");
                File.WriteAllText(path, TimeInsights.Export(Analysis.GetDay(_insightDay)), new UTF8Encoding(true));
                ThemeMessage.Show(this, "已导出：\n" + path, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { ThemeMessage.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}

