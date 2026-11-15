using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace KeyMouseStats
{
    /// <summary>
    /// 小组件「存储」页的只读占用快照。
    ///
    /// 口径刻意分成四块,且只统计本程序自己管理的文件:
    ///   程序本体 = 当前 exe;
    ///   历史备份 = stats.txt.bak + backup\ 每日备份;
    ///   核心统计 = stats.txt;
    ///   日报归档 = 日报\ 下的 Markdown。
    /// Rime 的 commit_log.csv 是外部只读文件,单独列出,不计入本程序占用。
    /// </summary>
    internal sealed class StorageUsage
    {
        public long ProgramBytes;
        public long BackupBytes;
        public long CoreBytes;
        public long ReportBytes;
        /// <summary>外部 Rime 日志;不存在时为 false、字节为 0。</summary>
        public bool CharacterExists;
        public long CharacterBytes;
        public string CharacterPath = "";
        /// <summary>相对昨天快照的今日增量；首次记录没有昨天快照时 HasTodayGrowth=false。</summary>
        public bool HasTodayGrowth, HasYesterdayGrowth;
        public long TodayGrowthBytes, YesterdayGrowthBytes;

        private static readonly object HistoryLock = new object();

        public long ProgramTotal
        {
            get { return ProgramBytes + BackupBytes + CoreBytes + ReportBytes; }
        }

        /// <summary>数据区占用，不含程序本体；日增量用这个口径，避免换 exe 把增长算进数据。</summary>
        public long DataBytes
        {
            get { return BackupBytes + CoreBytes + ReportBytes; }
        }

        public static StorageUsage Measure()
        {
            return Measure(Application.ExecutablePath, Store.DataDirectory, RimeTextStats.DefaultPath);
        }

        /// <summary>可注入路径,便于测试与预览;不读写文件内容,只取长度。</summary>
        public static StorageUsage Measure(string exePath, string dataDirectory, string characterPath)
        {
            StorageUsage usage = new StorageUsage();
            usage.ProgramBytes = FileBytes(exePath);
            usage.CoreBytes = FileBytes(Path.Combine(dataDirectory, "stats.txt")) + MonthlyBytes(dataDirectory);
            usage.BackupBytes = FileBytes(Path.Combine(dataDirectory, "stats.txt.bak"))
                + FolderBytes(Path.Combine(dataDirectory, "backup"));
            usage.ReportBytes = FolderBytes(Path.Combine(dataDirectory, "日报"));
            usage.CharacterPath = characterPath ?? "";
            usage.CharacterBytes = FileBytes(usage.CharacterPath);
            usage.CharacterExists = usage.CharacterBytes > 0;
            TrackDaily(usage, dataDirectory);
            return usage;
        }

        /// <summary>
        /// 记录每日总占用，并计算今天/昨天相对前一天的增量。
        /// 只写本程序数据目录下的一行一天快照；从首次使用该版本开始积累，缺失的前一天显示 —。
        /// </summary>
        public static void TrackDaily(StorageUsage usage, string dataDirectory)
        {
            if (usage == null || string.IsNullOrEmpty(dataDirectory)) return;
            if (!Store.CanPersist) return;   // 测试/预览进程不写用户的存储历史
            lock (HistoryLock)
            {
                try
                {
                    string path = Path.Combine(dataDirectory, "storage-history-v2.txt");
                    SortedDictionary<DateTime, long> history = new SortedDictionary<DateTime, long>();
                    if (File.Exists(path))
                    {
                        foreach (string line in File.ReadAllLines(path))
                        {
                            int split = line.IndexOf('|');
                            if (split <= 0) continue;
                            DateTime date;
                            long bytes;
                            if (DateTime.TryParseExact(line.Substring(0, split), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out date)
                                && long.TryParse(line.Substring(split + 1), System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture, out bytes)
                                && bytes >= 0)
                                history[date.Date] = bytes;
                        }
                    }
                    DateTime today = DateTime.Today;
                    long yesterday = 0, dayBefore = 0;
                    // 首次升级时没有历史快照：用现有备份/日报的文件日期回填昨天与前天，
                    // 这样用户第一次打开也能看到日增量，而不是先等一天。
                    if (!history.ContainsKey(today.AddDays(-1)) && EstimateAt(dataDirectory, today, out yesterday))
                        history[today.AddDays(-1)] = yesterday;
                    if (!history.ContainsKey(today.AddDays(-2)) && EstimateAt(dataDirectory, today.AddDays(-1), out dayBefore))
                        history[today.AddDays(-2)] = dayBefore;
                    history[today] = usage.DataBytes;
                    if (history.TryGetValue(today.AddDays(-1), out yesterday))
                    {
                        usage.HasTodayGrowth = true;
                        usage.TodayGrowthBytes = usage.DataBytes - yesterday;
                    }
                    if (history.TryGetValue(today.AddDays(-2), out dayBefore) && history.TryGetValue(today.AddDays(-1), out yesterday))
                    {
                        usage.HasYesterdayGrowth = true;
                        usage.YesterdayGrowthBytes = yesterday - dayBefore;
                    }
                    DateTime oldest = today.AddDays(-89);
                    StringBuilder text = new StringBuilder();
                    foreach (KeyValuePair<DateTime, long> pair in history)
                        if (pair.Key >= oldest)
                            text.Append(pair.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                                .Append('|').Append(pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
                    Directory.CreateDirectory(dataDirectory);
                    File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
                }
                catch (Exception) { }
            }
        }

        public static long FileBytes(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;
                return new FileInfo(path).Length;
            }
            catch (Exception) { return 0; }
        }

        public static long FolderBytes(string path)
        {
            long total = 0;
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    total += FileBytes(file);
            }
            catch (Exception) { }
            return total;
        }

        /// <summary>年目录下的月度数据文件；只认 yyyy 目录和 yyyy-MM.txt。</summary>
        private static long MonthlyBytes(string dataDirectory)
        {
            long total = 0;
            try
            {
                if (string.IsNullOrEmpty(dataDirectory) || !Directory.Exists(dataDirectory)) return 0;
                foreach (string yearDir in Directory.GetDirectories(dataDirectory))
                {
                    string year = Path.GetFileName(yearDir);
                    int parsedYear;
                    if (year.Length != 4 || !int.TryParse(year, out parsedYear)) continue;
                    foreach (string file in Directory.GetFiles(yearDir, "*.txt"))
                    {
                        DateTime month;
                        if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file) + "-01", "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out month))
                            total += FileBytes(file);
                    }
                }
            }
            catch (Exception) { }
            return total;
        }

        /// <summary>
        /// 没有历史快照时的回填估算：程序本体取当前值，核心统计取截止日前最后一
        /// 份 stats 备份，历史备份/日报按文件日期累加。只用于首日展示，之后以真实快照为准。
        /// </summary>
        private static bool EstimateAt(string dataDirectory, DateTime cutoff, out long total)
        {
            total = 0;
            long backups = 0, core = 0;
            DateTime coreDate = DateTime.MinValue;
            bool hasBackup = false;
            try
            {
                string backupDir = Path.Combine(dataDirectory, "backup");
                if (Directory.Exists(backupDir))
                {
                    foreach (string file in Directory.EnumerateFiles(backupDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        DateTime date = BackupDate(file);
                        if (date >= cutoff) continue;
                        long bytes = FileBytes(file);
                        backups += bytes;
                        hasBackup = true;
                        if (date >= coreDate) { coreDate = date; core = bytes; }
                    }
                }
                if (!hasBackup) return false;
                long reports = 0;
                string reportDir = Path.Combine(dataDirectory, "日报");
                if (Directory.Exists(reportDir))
                    foreach (string file in Directory.EnumerateFiles(reportDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { if (File.GetLastWriteTime(file).Date < cutoff) reports += FileBytes(file); }
                        catch (Exception) { }
                    }
                total = core + backups + reports;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static DateTime BackupDate(string path)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("stats-", StringComparison.OrdinalIgnoreCase) && name.Length >= 14)
                {
                    DateTime parsed;
                    if (DateTime.TryParseExact(name.Substring(6, 8), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out parsed)) return parsed.Date;
                }
                return File.GetLastWriteTime(path).Date;
            }
            catch (Exception) { return DateTime.MaxValue; }
        }

        /// <summary>紧凑的人类可读大小;0 显示 0 MB,极小值显示 &lt; 0.1 MB。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 100L * 1024) return "< 0.1 MB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }

        /// <summary>存储增量文案;小于 0.1 MB 也明确显示，而不是四舍五入成 0。</summary>
        public static string FormatDelta(long bytes)
        {
            if (bytes == 0) return "0 MB";
            long magnitude = Math.Abs(bytes);
            string sign = bytes > 0 ? "+" : "-";
            if (magnitude < 100L * 1024) return sign + "< 0.1 MB";
            return sign + FormatSize(magnitude);
        }

        /// <summary>占比文案;低于 0.1% 时明确写 &lt; 0.1%,不硬凑成 0.0%。</summary>
        public static string PercentText(long part, long total)        {
            if (part <= 0 || total <= 0) return "0%";
            double percent = part * 100.0 / total;
            if (percent < 0.05) return "< 0.1%";
            if (percent >= 100) return "100%";
            return percent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>脚注需要保留两位小数:0.17 MB 不该被四舍五入成 0.2 MB。</summary>
        public static string FormatSizePrecise(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 1024) return bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";
            if (bytes < 100L * 1024) return (bytes / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }
    }
}
