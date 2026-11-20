// ============================================================================
//  KeyMouseStats v3.0.1 · PeriodReport.cs
//
//  周报 / 月报的数据聚合。只读 DayRecord 与 Rime 快照,不碰 UI。
//  「最近 7 天」以选中日为最后一天;月报取选中日所在月份,当月只统计到今天。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeyMouseStats
{
    internal enum PeriodKind { Week, Month }

    internal sealed class PeriodDay
    {
        public DateTime Date;
        public bool HasData;
        public long Keys, Clicks, Chars;
        public double ActiveSeconds;
    }

    internal sealed class PeriodApp
    {
        public string Name = "";
        public string Path = "";
        public double Seconds;
        public long Keys;
    }

    internal sealed class PeriodWeek
    {
        public string Label = "";
        public DateTime Start, End;
        public double ActiveSeconds;
        public List<PeriodDay> Days = new List<PeriodDay>();
    }

    internal sealed class PeriodReport
    {
        private static readonly string[] WeekNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        private static readonly string[] MonthNames =
        {
            "", "一月", "二月", "三月", "四月", "五月", "六月",
            "七月", "八月", "九月", "十月", "十一月", "十二月"
        };

        public PeriodKind Kind;
        public DateTime Anchor, Start, End;
        public int DayCount, ActiveDays;
        public long Keys, Clicks, Chars;
        public double ActiveSeconds;
        public long PrevKeys, PrevClicks, PrevChars;
        public double PrevActiveSeconds;
        public int PrevDayCount;
        public int LongestStreak;
        public DateTime LongestStreakStart;

        public readonly List<PeriodDay> Days = new List<PeriodDay>();
        public readonly List<PeriodDay> PreviousDays = new List<PeriodDay>();
        public readonly List<PeriodApp> Apps = new List<PeriodApp>();
        public readonly List<PeriodWeek> Weeks = new List<PeriodWeek>();

        public DateTime BestDay;
        public double BestActiveSeconds;
        public long BestKeys;
        public int BusiestHour;
        public long BusiestHourKeys;
        public double LongestSessionSeconds;
        public DateTime LongestSessionStart;

        public double MorningShare, AfternoonShare, EveningShare;
        public double WorkdayAvgSeconds, WeekendAvgSeconds;

        public string Headline = "";
        public string Lede = "";
        public string ComparisonLabel = "";

        private DateTime PrevStart, PrevEnd;
        private bool PartialMonth;

        // ---------------------------------------------------------------- 入口

        public static PeriodReport BuildWeek(DateTime anchor)
        {
            PeriodReport report = new PeriodReport();
            report.Kind = PeriodKind.Week;
            report.Anchor = anchor.Date;
            report.End = anchor.Date;
            report.Start = report.End.AddDays(-6);
            report.DayCount = 7;
            report.PrevEnd = report.Start.AddDays(-1);
            report.PrevStart = report.Start.AddDays(-7);
            report.PrevDayCount = 7;
            report.ComparisonLabel = "较上一周";
            report.Build();
            return report;
        }

        public static PeriodReport BuildMonth(DateTime anchor)
        {
            PeriodReport report = new PeriodReport();
            report.Kind = PeriodKind.Month;
            report.Anchor = anchor.Date;
            DateTime first = new DateTime(anchor.Year, anchor.Month, 1);
            DateTime last = first.AddMonths(1).AddDays(-1);
            DateTime end = last > DateTime.Today ? DateTime.Today : last;
            if (end < first) end = first;
            report.Start = first;
            report.End = end;
            report.DayCount = (end - first).Days + 1;
            report.PrevStart = first.AddMonths(-1);
            report.PrevEnd = first.AddDays(-1);
            report.PartialMonth = end < last;
            if (report.PartialMonth)
            {
                // 当月未结束时和上月同期比,否则「总击键/总时长」会被天数直接拉低。
                DateTime comparableEnd = report.PrevStart.AddDays((end - first).Days);
                if (comparableEnd > report.PrevEnd) comparableEnd = report.PrevEnd;
                report.PrevEnd = comparableEnd;
                report.ComparisonLabel = "较上月同期";
            }
            else
            {
                report.ComparisonLabel = "较上月";
            }
            report.PrevDayCount = (report.PrevEnd - report.PrevStart).Days + 1;
            report.Build();
            return report;
        }

        public bool IsPartialMonth { get { return PartialMonth; } }

        public string MonthName
        {
            get { return MonthNames[Anchor.Month]; }
        }

        // ---------------------------------------------------------------- 聚合

        private void Build()
        {
            RimeTextStats rime = null;
            try { rime = RimeTextStats.Load(); }
            catch (Exception) { }

            Dictionary<string, PeriodApp> apps = new Dictionary<string, PeriodApp>(StringComparer.OrdinalIgnoreCase);
            long[] hourKeys = new long[24];
            double[] activeHours = new double[24];
            long streak = 0;

            for (DateTime day = Start; day <= End; day = day.AddDays(1))
            {
                DayRecord record = null;
                try { record = Analysis.GetDay(day); }
                catch (Exception) { }

                long dayChars = 0;
                if (rime != null && !rime.Missing)
                    rime.DayChars.TryGetValue(day, out dayChars);

                bool has = (record != null && !record.IsEmpty) || dayChars > 0;
                PeriodDay periodDay = new PeriodDay();
                periodDay.Date = day;
                periodDay.HasData = has;
                periodDay.Chars = dayChars;
                if (record != null && !record.IsEmpty)
                {
                    periodDay.Keys = record.Keys;
                    periodDay.Clicks = record.Clicks;
                    periodDay.ActiveSeconds = record.ActiveSeconds;
                    Keys += record.Keys;
                    Clicks += record.Clicks;
                    ActiveSeconds += record.ActiveSeconds;

                    if (record.HourKeys != null)
                        for (int i = 0; i < 24 && i < record.HourKeys.Length; i++) hourKeys[i] += record.HourKeys[i];
                    if (record.ActiveHours != null)
                        for (int i = 0; i < 24 && i < record.ActiveHours.Length; i++) activeHours[i] += record.ActiveHours[i];

                    AddApps(apps, record);
                    AddSessions(record);

                    if (periodDay.ActiveSeconds > BestActiveSeconds
                        || (periodDay.ActiveSeconds == BestActiveSeconds && periodDay.Keys > BestKeys))
                    {
                        BestDay = day;
                        BestActiveSeconds = periodDay.ActiveSeconds;
                        BestKeys = periodDay.Keys;
                    }
                }
                Days.Add(periodDay);
                if (has)
                {
                    ActiveDays++;
                    streak++;
                    if (streak > LongestStreak)
                    {
                        LongestStreak = (int)streak;
                        LongestStreakStart = day.AddDays(-(streak - 1));
                    }
                }
                else streak = 0;
            }

            if (BestDay == DateTime.MinValue) BestDay = Start;
            if (BestActiveSeconds <= 0 && BestKeys <= 0)
            {
                for (int i = 0; i < Days.Count; i++)
                {
                    if (Days[i].Keys <= BestKeys) continue;
                    BestDay = Days[i].Date;
                    BestKeys = Days[i].Keys;
                }
            }

            for (int i = 0; i < 24; i++)
            {
                if (hourKeys[i] > BusiestHourKeys) { BusiestHourKeys = hourKeys[i]; BusiestHour = i; }
            }
            if (BusiestHourKeys <= 0)
            {
                double max = 0;
                for (int i = 0; i < 24; i++)
                {
                    if (activeHours[i] <= max) continue;
                    max = activeHours[i];
                    BusiestHour = i;
                }
            }

            Apps.AddRange(apps.Values);
            Apps.Sort(delegate(PeriodApp a, PeriodApp b) { return b.Seconds.CompareTo(a.Seconds); });
            if (Apps.Count > 5) Apps.RemoveRange(5, Apps.Count - 5);

            if (rime != null && !rime.Missing)
            {
                try
                {
                    RimeSlice slice = rime.Slice(Start, End);
                    if (slice != null) Chars = slice.Chars;
                }
                catch (Exception) { }
            }

            BuildPrevious(rime);
            BuildRhythm(activeHours, hourKeys);
            if (Kind == PeriodKind.Month) BuildWeeks();
            BuildCopy();
        }

        private void AddApps(Dictionary<string, PeriodApp> apps, DayRecord record)
        {
            if (record.Apps == null) return;
            foreach (AppUsage usage in record.Apps.Values)
            {
                if (usage == null || usage.ActiveSeconds <= 0) continue;
                string path = usage.ProcessPath ?? "";
                string title = usage.Title ?? "";
                if (path.Length == 0 && title.Length == 0) continue;
                string key = path.Length > 0 ? path : "title:" + title;

                PeriodApp app;
                if (!apps.TryGetValue(key, out app))
                {
                    app = new PeriodApp();
                    app.Path = path;
                    app.Name = path.Length > 0 ? AppActivity.FriendlyName(path)
                        : (title.Length > 0 ? title : "未识别应用");
                    apps[key] = app;
                }
                app.Seconds += usage.ActiveSeconds;
                app.Keys += usage.Keys;
            }
        }

        private void AddSessions(DayRecord record)
        {
            if (record.Sessions == null) return;
            for (int i = 0; i < record.Sessions.Count; i++)
            {
                ActiveSession session = record.Sessions[i];
                if (session == null || session.Seconds <= 0) continue;
                if (session.Seconds > LongestSessionSeconds)
                {
                    LongestSessionSeconds = session.Seconds;
                    LongestSessionStart = session.Start;
                }
            }
        }

        private void BuildPrevious(RimeTextStats rime)
        {
            for (DateTime day = PrevStart; day <= PrevEnd; day = day.AddDays(1))
            {
                DayRecord record = null;
                try { record = Analysis.GetDay(day); }
                catch (Exception) { }
                PeriodDay previous = new PeriodDay();
                previous.Date = day;
                if (record != null && !record.IsEmpty)
                {
                    previous.HasData = true;
                    previous.Keys = record.Keys;
                    previous.Clicks = record.Clicks;
                    previous.ActiveSeconds = record.ActiveSeconds;
                    PrevKeys += record.Keys;
                    PrevClicks += record.Clicks;
                    PrevActiveSeconds += record.ActiveSeconds;
                }
                PreviousDays.Add(previous);
            }
            if (rime != null && !rime.Missing)
            {
                try
                {
                    RimeSlice slice = rime.Slice(PrevStart, PrevEnd);
                    if (slice != null) PrevChars = slice.Chars;
                }
                catch (Exception) { }
            }
        }

        private void BuildRhythm(double[] activeHours, long[] hourKeys)
        {
            double morning = 0, afternoon = 0, evening = 0;
            for (int i = 0; i < 24; i++)
            {
                double value = activeHours[i];
                if (value <= 0) value = hourKeys[i];   // 旧记录没有 active_hours 时用击键分布兜底
                if (i >= 6 && i < 12) morning += value;
                else if (i >= 12 && i < 18) afternoon += value;
                else evening += value;                 // 18–24 + 0–6 都算晚上
            }
            double total = morning + afternoon + evening;
            if (total > 0)
            {
                MorningShare = morning / total;
                AfternoonShare = afternoon / total;
                EveningShare = evening / total;
            }

            double workday = 0, weekend = 0;
            int workdays = 0, weekends = 0;
            for (int i = 0; i < Days.Count; i++)
            {
                PeriodDay day = Days[i];
                bool weekday = day.Date.DayOfWeek != DayOfWeek.Saturday && day.Date.DayOfWeek != DayOfWeek.Sunday;
                if (weekday) { workdays++; workday += day.ActiveSeconds; }
                else { weekends++; weekend += day.ActiveSeconds; }
            }
            if (workdays > 0) WorkdayAvgSeconds = workday / workdays;
            if (weekends > 0) WeekendAvgSeconds = weekend / weekends;
        }

        private void BuildWeeks()
        {
            Dictionary<DateTime, PeriodWeek> map = new Dictionary<DateTime, PeriodWeek>();
            for (int i = 0; i < Days.Count; i++)
            {
                PeriodDay day = Days[i];
                DateTime weekStart = WeekStart(day.Date);
                PeriodWeek week;
                if (!map.TryGetValue(weekStart, out week))
                {
                    week = new PeriodWeek();
                    week.Label = "W" + WeekOfYear(weekStart);
                    week.Start = weekStart < Start ? Start : weekStart;
                    DateTime weekEnd = weekStart.AddDays(6);
                    week.End = weekEnd > End ? End : weekEnd;
                    map[weekStart] = week;
                    Weeks.Add(week);
                }
                week.Days.Add(day);
                week.ActiveSeconds += day.ActiveSeconds;
            }
            Weeks.Sort(delegate(PeriodWeek a, PeriodWeek b) { return a.Start.CompareTo(b.Start); });
        }

        private void BuildCopy()
        {
            if (ActiveDays == 0)
            {
                Headline = Kind == PeriodKind.Week ? "这 7 天没有记录。" : MonthName + "没有记录。";
                Lede = "没有数据：可能没开机，或统计被暂停过。";
                return;
            }

            string best = BestActiveSeconds > 0
                ? ActivityMonitor.FormatDuration(BestActiveSeconds)
                : Analysis.FmtCount(BestKeys) + " 次击键";
            string bestDate = BestDay.Month + "." + BestDay.Day;

            if (Kind == PeriodKind.Week)
            {
                string bestWeekday = WeekNames[(int)BestDay.DayOfWeek];
                Headline = "这 7 天，你把最好的状态留给了 " + bestWeekday + "。";
                Lede = ActiveDays + " 天有记录，最高单日 " + best + "（" + bestDate + "）。";
                if (WorkdayAvgSeconds > 0 && WeekendAvgSeconds > 0)
                {
                    double delta = WeekendAvgSeconds / WorkdayAvgSeconds - 1;
                    if (delta < -0.02)
                        Lede += " 周末降速 " + Math.Abs(delta * 100).ToString("0", CultureInfo.InvariantCulture) + "%。";
                    else if (delta > 0.02)
                        Lede += " 周末反而多坐了 " + (delta * 100).ToString("0", CultureInfo.InvariantCulture) + "%。";
                }
            }
            else
            {
                Headline = MonthName + "，键盘敲了 " + Analysis.FmtCount(Keys) + " 次，最长连续 "
                    + LongestStreak + " 天。";
                Lede = DayCount + " 天里有 " + ActiveDays + " 天留下记录。";
                if (Apps.Count > 0) Lede += "最常打开 " + Apps[0].Name + "。";
                Lede += "最高单日 " + best + "（" + bestDate + "）。";
                if (PartialMonth) Lede += "数据截至 " + End.Month + "." + End.Day + "。";
            }
        }

        // ---------------------------------------------------------------- 工具

        private static DateTime WeekStart(DateTime day)
        {
            int offset = ((int)day.DayOfWeek + 6) % 7;   // 周一为一周起点
            return day.Date.AddDays(-offset);
        }

        private static int WeekOfYear(DateTime day)
        {
            try
            {
                return CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(day,
                    CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            }
            catch (Exception) { return 1; }
        }
    }
}
