using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeyMouseStats
{
    /// <summary>
    /// 每日简报。输入只有两样东西:当天那条 <see cref="DayRecord"/> 与前 7 个有数据的日子,
    /// 加上(可选)当天的 Rime 上屏日志。输出是一个可渲染、可落盘、可复制的纯数据对象。
    ///
    /// 设计原则:
    ///  * 所有数字都能追到原始字段,不做估算;
    ///  * 对比基准用「前 7 个有数据的日子」的日均,而不是「昨天」—— 单日噪音太大;
    ///  * 提醒文案允许揶揄,但不许编造:每条提醒都必须由某个具体数字触发。
    /// </summary>
    internal sealed class DailyReport
    {
        public DateTime Day;
        public bool HasData;
        /// <summary>当天还没过完(生成时就是今天),标题上会标「进行中」。</summary>
        public bool InProgress;

        // ---- 核心数字 ----
        public long Keys, Clicks, Wheel;
        public double MoveMeters, ActiveSeconds, IdleSeconds;
        public long PeakApm;
        public int SessionCount;
        public double LongestSessionSeconds;
        public DateTime LongestSessionStart;

        // ---- 时段 ----
        public readonly long[] HourKeys = new long[24];
        public int BusiestHour = -1;
        public long BusiestHourKeys;

        // ---- 应用 / 操作 ----
        public readonly List<AppLine> Apps = new List<AppLine>();
        public readonly List<string> Combos = new List<string>();
        public readonly List<string> TopKeys = new List<string>();

        // ---- 字符内容(当天有 Rime 日志才有) ----
        public bool HasChars;
        public long Chars, Commits;
        public int BusiestCharHour = -1;
        public long BusiestCharHourChars;
        public readonly long[] Composition = new long[5];
        public readonly List<string> TopChars = new List<string>();
        public double AvgCharsPerCommit;
        public int LongestBurstMinutes;
        public long LongestBurstChars;
        /// <summary>当天一共输入了多少种不同的字符(去重)与其中汉字种类。</summary>
        public int DistinctChars, DistinctHan;

        // ---- 基准:前 7 个有数据的日子 ----
        public int BaseDays;
        public double BaseKeys, BaseClicks, BaseWheel, BaseMoveMeters, BaseActiveSeconds, BaseChars;

        // ---- 文案 ----
        public string Headline = "";
        public readonly List<string> Reminders = new List<string>();

        /// <summary>基准列的表头:只有一天可比时直接写「前一天」,别硬叫「日均」。</summary>
        public string BaselineLabel
        {
            get
            {
                if (BaseDays <= 0) return "无基准";
                if (BaseDays == 1) return "前一天";
                return "前 " + BaseDays + " 天日均";
            }
        }

        internal sealed class AppLine
        {
            public string Name = "";
            public double Seconds;
            public long Keys;
        }

        // ---------------------------------------------------------------- 组装

        public static DailyReport Build(DateTime day)
        {
            DailyReport report = new DailyReport();
            report.Day = day.Date;
            report.InProgress = report.Day >= DateTime.Today;

            DayRecord record = Analysis.GetDay(report.Day);
            report.HasData = record != null && !record.IsEmpty;

            if (report.HasData) ReadRecord(report, record);

            RimeTextStats rime = RimeTextStats.Load();
            ReadCharacters(report, rime);
            ReadBaseline(report, rime);
            BuildCopy(report);
            return report;
        }

        private static void ReadRecord(DailyReport report, DayRecord record)
        {
            report.Keys = record.Keys;
            report.Clicks = record.Clicks;
            report.Wheel = record.Wheel;
            report.MoveMeters = record.MoveMeters;
            report.ActiveSeconds = record.ActiveSeconds;
            report.IdleSeconds = record.IdleSeconds;
            report.PeakApm = record.PeakApm;
            report.SessionCount = record.Sessions == null ? 0 : record.Sessions.Count;

            if (record.Sessions != null)
            {
                foreach (ActiveSession session in record.Sessions)
                {
                    if (session.Seconds <= report.LongestSessionSeconds) continue;
                    report.LongestSessionSeconds = session.Seconds;
                    report.LongestSessionStart = session.Start;
                }
            }

            long[] hours = record.HourKeys ?? new long[24];
            for (int i = 0; i < 24 && i < hours.Length; i++)
            {
                report.HourKeys[i] = hours[i];
                if (hours[i] <= report.BusiestHourKeys) continue;
                report.BusiestHourKeys = hours[i];
                report.BusiestHour = i;
            }

            // 应用 Top 5(按活跃时长)。同一个 exe 的不同窗口要合并 —— 否则「msedge.exe」会占满五名。
            Dictionary<string, AppLine> byProcess = new Dictionary<string, AppLine>(StringComparer.OrdinalIgnoreCase);
            if (record.Apps != null)
            {
                foreach (AppUsage usage in record.Apps.Values)
                {
                    if (usage == null || usage.ActiveSeconds <= 0) continue;
                    string key = string.IsNullOrEmpty(usage.ProcessPath) ? "" : usage.ProcessPath;
                    AppLine line;
                    if (!byProcess.TryGetValue(key, out line))
                    {
                        line = new AppLine();
                        line.Name = key.Length == 0 ? "未识别应用" : Path.GetFileName(key);
                        byProcess[key] = line;
                    }
                    line.Seconds += usage.ActiveSeconds;
                    line.Keys += usage.Keys;
                }
            }
            List<AppLine> apps = new List<AppLine>(byProcess.Values);
            apps.Sort(delegate(AppLine a, AppLine b) { return b.Seconds.CompareTo(a.Seconds); });
            for (int i = 0; i < apps.Count && i < 5; i++) report.Apps.Add(apps[i]);

            // 组合键 Top 5
            List<DayRecord> oneDay = new List<DayRecord>();
            oneDay.Add(record);
            List<KeyValuePair<string, long>> combos = ShortcutStats.Ranking(oneDay);
            for (int i = 0; i < combos.Count && i < 5; i++)
                report.Combos.Add(ShortcutStats.Display(combos[i].Key) + " " + Analysis.FmtCount(combos[i].Value));

            // 按键 Top 5
            List<KeyValuePair<int, long>> keys = new List<KeyValuePair<int, long>>(record.KeyCounts);
            keys.Sort(delegate(KeyValuePair<int, long> a, KeyValuePair<int, long> b)
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : a.Key.CompareTo(b.Key);
            });
            for (int i = 0; i < keys.Count && i < 5; i++)
                report.TopKeys.Add(Analysis.KeyName(keys[i].Key) + " " + Analysis.FmtCount(keys[i].Value));
        }

        private static void ReadCharacters(DailyReport report, RimeTextStats rime)
        {
            if (rime == null || rime.Missing) return;
            long chars, commits;
            if (!rime.DayChars.TryGetValue(report.Day, out chars)) return;
            rime.DayCommits.TryGetValue(report.Day, out commits);
            if (chars <= 0) return;

            report.HasChars = true;
            report.Chars = chars;
            report.Commits = commits;

            RimeSlice slice = rime.Slice(report.Day, report.Day);
            report.AvgCharsPerCommit = slice.AverageChars;
            report.DistinctChars = slice.DistinctChars;
            report.DistinctHan = slice.DistinctHan;
            report.BusiestCharHour = slice.BusiestHour();
            if (report.BusiestCharHour >= 0) report.BusiestCharHourChars = slice.HourChars[report.BusiestCharHour];
            for (int i = 0; i < report.Composition.Length && i < slice.Composition.Length; i++)
                report.Composition[i] = slice.Composition[i];
            report.LongestBurstMinutes = slice.LongestBurstMinutes;
            report.LongestBurstChars = slice.LongestBurstChars;

            List<KeyValuePair<int, long>> top = slice.TopChars(5);
            for (int i = 0; i < top.Count; i++)
                report.TopChars.Add(RimeText.DisplayName(top[i].Key) + " " + Analysis.FmtCount(top[i].Value));
        }

        /// <summary>前 7 个「有数据」的日子做日均;缺失的日子不计入分母,免得休假被算成退步。</summary>
        private static void ReadBaseline(DailyReport report, RimeTextStats rime)
        {
            double keys = 0, clicks = 0, wheel = 0, meters = 0, active = 0, chars = 0;
            int days = 0, charDays = 0;
            for (int offset = 1; offset <= 7; offset++)
            {
                DateTime day = report.Day.AddDays(-offset);
                if (rime != null && !rime.Missing)
                {
                    long dayChars;
                    if (rime.DayChars.TryGetValue(day, out dayChars) && dayChars > 0)
                    {
                        chars += dayChars;
                        charDays++;
                    }
                }
                DayRecord record = Analysis.GetDay(day);
                if (record == null || record.IsEmpty) continue;
                keys += record.Keys;
                clicks += record.Clicks;
                wheel += record.Wheel;
                meters += record.MoveMeters;
                active += record.ActiveSeconds;
                days++;
            }
            if (charDays > 0) report.BaseChars = chars / charDays;
            if (days == 0) return;

            report.BaseDays = days;
            report.BaseKeys = keys / days;
            report.BaseClicks = clicks / days;
            report.BaseWheel = wheel / days;
            report.BaseMoveMeters = meters / days;
            report.BaseActiveSeconds = active / days;
        }

        // ---------------------------------------------------------------- 文案

        private static void BuildCopy(DailyReport report)
        {
            report.Headline = BuildHeadline(report);
            BuildReminders(report);
        }

        private static string BuildHeadline(DailyReport report)
        {
            if (!report.HasData) return "这一天没有记录——是没开机，还是在别的设备上忙？";

            StringBuilder text = new StringBuilder();
            text.Append("敲了 ").Append(Analysis.FmtCount(report.Keys))
                .Append(" 次键、点了 ").Append(Analysis.FmtCount(report.Clicks)).Append(" 次");
            if (report.ActiveSeconds > 0)
                text.Append("，有效使用 ").Append(ActivityMonitor.FormatDuration(report.ActiveSeconds));
            if (report.Apps.Count > 0 && report.ActiveSeconds > 0)
            {
                double share = report.Apps[0].Seconds / Math.Max(1, report.ActiveSeconds);
                text.Append("；").Append(report.Apps[0].Name).Append(" 占了 ")
                    .Append((share * 100).ToString("0", CultureInfo.InvariantCulture)).Append("%");
            }
            text.Append("。");
            return text.ToString();
        }

        private static void BuildReminders(DailyReport report)
        {
            if (!report.HasData)
            {
                report.Reminders.Add("没有数据:可能没开机,也可能当天统计被暂停过。");
                return;
            }

            // 与日均比
            if (report.BaseDays > 0 && report.BaseKeys > 0)
            {
                double ratio = report.Keys / report.BaseKeys;
                string pct = ((ratio - 1) * 100).ToString("+0;-0;0", CultureInfo.InvariantCulture) + "%";
                if (ratio < 0.6)
                    report.Reminders.Add("击键只有最近 " + report.BaseDays + " 天日均的 "
                        + (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%，今天手有点懒？");
                else if (ratio > 1.4)
                    report.Reminders.Add("击键比日均高 " + pct.TrimStart('+') + "，今天是真在干活。");
            }

            // 连续使用
            if (report.LongestSessionSeconds >= 90 * 60)
                report.Reminders.Add("最长一段连续使用 " + ActivityMonitor.FormatDuration(report.LongestSessionSeconds)
                    + "（" + report.LongestSessionStart.ToString("HH:mm", CultureInfo.InvariantCulture)
                    + " 起），键盘替你喊累。");

            // 深夜
            double lateSeconds = 0;
            for (int hour = 22; hour < 24; hour++)
            {
                DayRecord record = Analysis.GetDay(report.Day);
                if (record != null && record.ActiveHours != null) lateSeconds += record.ActiveHours[hour];
            }
            if (lateSeconds >= 20 * 60)
                report.Reminders.Add("22 点后还活跃了 " + ActivityMonitor.FormatDuration(lateSeconds)
                    + "，明天的你会感谢今天的你吗。");

            // 主应用集中度
            if (report.Apps.Count > 0 && report.ActiveSeconds > 0)
            {
                double share = report.Apps[0].Seconds / report.ActiveSeconds;
                if (share >= 0.45)
                    report.Reminders.Add(report.Apps[0].Name + " 占了 "
                        + (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%，今天基本住在那儿。");
            }

            // 字符
            if (report.HasChars)
            {
                if (report.Chars < 200)
                    report.Reminders.Add("全天只上屏 " + Analysis.FmtCount(report.Chars) + " 字，手速快生锈了。");
                else if (report.Chars > 3000)
                    report.Reminders.Add("上屏 " + Analysis.FmtCount(report.Chars) + " 字，产出不小。");
            }

            // 点击 vs 击键
            if (report.Clicks > report.Keys && report.Keys > 0)
                report.Reminders.Add("点击比击键还多，今天主要在点，不在写。");

            // 目标
            DayRecord today = Analysis.GetDay(report.Day);
            if (WellbeingSettings.GoalEnabled && today != null)
            {
                double progress = WellbeingSettings.Progress(today);
                if (!double.IsNaN(progress))
                    report.Reminders.Add("目标（" + WellbeingSettings.GoalName + "）"
                        + (progress >= 1 ? "已达成 " : "还差 ")
                        + (Math.Min(progress, 9.99) * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
            }

            if (report.Reminders.Count == 0)
                report.Reminders.Add("平稳的一天，没什么好挑刺的。");
        }

        // ---------------------------------------------------------------- 落盘

        /// <summary>日报目录:%APPDATA%\键鼠统计\日报。</summary>
        public static string Directory
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "键鼠统计"),
                    "日报");
            }
        }

        public string FilePath
        {
            get { return Path.Combine(Directory, Day.ToString("yyyy-MM-dd") + ".md"); }
        }

        /// <summary>写一份 Markdown。失败返回空串,不抛——日报写不出来不该影响统计。</summary>
        public string Save()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(FilePath, ToMarkdown(this), new UTF8Encoding(true));
                return FilePath;
            }
            catch (Exception) { return ""; }
        }

        // ---------------------------------------------------------------- Markdown

        public static string ToMarkdown(DailyReport report)
        {
            StringBuilder text = new StringBuilder();
            string[] week = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

            text.Append("# 键鼠统计 · 日报").AppendLine();
            text.Append(report.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(' ').Append(week[(int)report.Day.DayOfWeek])
                .Append(report.InProgress ? " · 进行中" : (report.HasData ? " · 完整" : " · 无数据"))
                .AppendLine().AppendLine();

            if (!report.HasData)
            {
                text.Append("> 这一天没有任何记录。").AppendLine().AppendLine();
                text.Append("可能的原因：没开机、统计被暂停、或当天数据被清理。").AppendLine();
                return text.ToString();
            }

            text.Append("> ").Append(report.Headline).AppendLine().AppendLine();

            text.Append("## 核心数字").AppendLine().AppendLine();
            text.Append("| 项 | 今天 | ").Append(report.BaselineLabel).Append(" | 变化 |").AppendLine();
            text.Append("|---|---|---|---|").AppendLine();
            AppendRow(text, "击键", Analysis.FmtCount(report.Keys), report.BaseDays > 0 ? Analysis.FmtCount((long)Math.Round(report.BaseKeys)) : "—", report.Keys, report.BaseKeys);
            AppendRow(text, "点击", Analysis.FmtCount(report.Clicks), report.BaseDays > 0 ? Analysis.FmtCount((long)Math.Round(report.BaseClicks)) : "—", report.Clicks, report.BaseClicks);
            AppendRow(text, "滚轮", Analysis.FmtCount(report.Wheel), report.BaseDays > 0 ? Analysis.FmtCount((long)Math.Round(report.BaseWheel)) : "—", report.Wheel, report.BaseWheel);
            AppendRow(text, "鼠标路程", Analysis.FmtMeters(report.MoveMeters), report.BaseDays > 0 ? Analysis.FmtMeters(report.BaseMoveMeters) : "—", report.MoveMeters, report.BaseMoveMeters);
            AppendRow(text, "有效使用", ActivityMonitor.FormatDuration(report.ActiveSeconds),
                report.BaseDays > 0 ? ActivityMonitor.FormatDuration(report.BaseActiveSeconds) : "—", report.ActiveSeconds, report.BaseActiveSeconds);
            if (report.HasChars)
                AppendRow(text, "上屏字数", Analysis.FmtCount(report.Chars),
                    report.BaseChars > 0 ? Analysis.FmtCount((long)Math.Round(report.BaseChars)) : "—", report.Chars, report.BaseChars);
            text.AppendLine();

            text.Append("## 时间分布").AppendLine().AppendLine();
            if (report.BusiestHour >= 0)
                text.Append("- 最忙 ").Append(report.BusiestHour).Append(" 时（")
                    .Append(Analysis.FmtCount(report.BusiestHourKeys)).Append(" 次击键）").AppendLine();
            text.Append("- 连续使用 ").Append(report.SessionCount).Append(" 段，最长 ")
                .Append(ActivityMonitor.FormatDuration(report.LongestSessionSeconds));
            if (report.LongestSessionStart != DateTime.MinValue)
                text.Append("（").Append(report.LongestSessionStart.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(" 起）");
            text.AppendLine();
            text.Append("- 空闲 ").Append(ActivityMonitor.FormatDuration(report.IdleSeconds)).AppendLine().AppendLine();

            if (report.Apps.Count > 0)
            {
                text.Append("## 应用（按使用时长）").AppendLine().AppendLine();
                for (int i = 0; i < report.Apps.Count; i++)
                    text.Append("- ").Append(ActivityMonitor.FormatDuration(report.Apps[i].Seconds))
                        .Append(' ').Append(report.Apps[i].Name).AppendLine();
                text.AppendLine();
            }

            if (report.Combos.Count > 0 || report.TopKeys.Count > 0)
            {
                text.Append("## 操作习惯").AppendLine().AppendLine();
                if (report.Combos.Count > 0)
                    text.Append("- 组合键：").Append(string.Join(" · ", report.Combos.ToArray())).AppendLine();
                if (report.TopKeys.Count > 0)
                    text.Append("- 按键：").Append(string.Join(" · ", report.TopKeys.ToArray())).AppendLine();
                text.Append("- 鼠标路程 ").Append(Analysis.FmtMeters(report.MoveMeters)).AppendLine();
                text.AppendLine();
            }

            if (report.HasChars)
            {
                text.Append("## 字符内容").AppendLine().AppendLine();
                text.Append("- 上屏 ").Append(Analysis.FmtCount(report.Chars)).Append(" 字 / ")
                    .Append(Analysis.FmtCount(report.Commits)).Append(" 次，平均 ")
                    .Append(report.AvgCharsPerCommit.ToString("0.0", CultureInfo.InvariantCulture)).Append(" 字/次").AppendLine();
                text.Append("- 不同字符 ").Append(Analysis.FmtCount(report.DistinctChars)).Append(" 种")
                    .Append(report.DistinctHan > 0 ? "（其中汉字 " + Analysis.FmtCount(report.DistinctHan) + " 种）" : "")
                    .AppendLine();
                text.Append("- 构成：").Append(CompositionLine(report)).AppendLine();
                if (report.BusiestCharHour >= 0)
                    text.Append("- 最活跃 ").Append(report.BusiestCharHour).Append(" 时（")
                        .Append(Analysis.FmtCount(report.BusiestCharHourChars)).Append(" 字）").AppendLine();
                if (report.LongestBurstMinutes > 0)
                    text.Append("- 最长连续输入 ").Append(report.LongestBurstMinutes).Append(" 分钟 / ")
                        .Append(Analysis.FmtCount(report.LongestBurstChars)).Append(" 字").AppendLine();
                if (report.TopChars.Count > 0)
                    text.Append("- 高频字：").Append(string.Join(" · ", report.TopChars.ToArray())).AppendLine();
                text.AppendLine();
            }

            text.Append("## 值得注意").AppendLine().AppendLine();
            for (int i = 0; i < report.Reminders.Count; i++)
                text.Append("- ").Append(report.Reminders[i]).AppendLine();

            text.AppendLine().Append("*由键鼠统计生成 · 数据只在本机*").AppendLine();
            return text.ToString();
        }

        private static void AppendRow(StringBuilder text, string label, string value, string baseline, double current, double baseValue)
        {
            string delta = "—";
            if (baseValue > 0)
            {
                double ratio = current / baseValue - 1;
                if (Math.Abs(ratio) < 0.005) delta = "持平";
                else delta = (ratio > 0 ? "▲ " : "▼ ") + Math.Abs(ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            }
            text.Append("| ").Append(label).Append(" | ").Append(value).Append(" | ").Append(baseline)
                .Append(" | ").Append(delta).Append(" |").AppendLine();
        }

        private static string CompositionLine(DailyReport report)
        {
            StringBuilder text = new StringBuilder();
            long total = 0;
            for (int i = 0; i < report.Composition.Length; i++) total += report.Composition[i];
            if (total <= 0) return "—";
            for (int i = 0; i < report.Composition.Length; i++)
            {
                if (report.Composition[i] <= 0) continue;
                if (text.Length > 0) text.Append(" · ");
                text.Append(RimeSlice.CompositionNames[i]).Append(' ')
                    .Append((report.Composition[i] * 100.0 / total).ToString("0", CultureInfo.InvariantCulture)).Append('%');
            }
            return text.ToString();
        }
    }
}
