// ============================================================================
//  键鼠统计 - 数据管理  DataManagement.cs
//
//  1.7.1 新增:
//    · 备份轮转:每天首次启动时留一份带日期的备份,按保留份数清理旧备份。
//    · 导出:带校验和与来源标记的单文件,导入前先校验,避免半个文件被吃进去。
//    · 导入 / 多机合并:合并策略明确(取较大值 / 相加),并在写入前给出冲突预览。
//    · 数据校验:逐日不变量检查,产出错误 / 警告清单。
//  只操作数据目录内的文件;不联网、不改数据格式版本。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KeyMouseStats
{
    /// <summary>多机合并时同一天都有记录的处理方式。</summary>
    internal enum MergeStrategy
    {
        /// <summary>保留击键更多的那一份:不会重复计数,但两台机器同时使用时可能低估。</summary>
        KeepLarger,
        /// <summary>两份相加:两台机器分开使用时正确,但同时使用时可能重复计数。</summary>
        Add
    }

    /// <summary>导入前的检查结果。只有 Valid 为 true 才能写入。</summary>
    internal sealed class ImportPreview
    {
        public bool Valid;
        public string Error = "";
        public string FileName = "";
        public Store.Parsed Data;
        public int Days, Conflicts;
        public long ImportedKeys, ConflictKeysLocal, ConflictKeysImported;
        public DateTime First, Last;
    }

    /// <summary>导入 / 合并的结果。</summary>
    internal sealed class ImportResult
    {
        public int Added, Merged, KeptLocal, Replaced, TotalDays;
        public long KeysBefore, KeysAfter;

        public string Summary()
        {
            if (Replaced > 0) return "已用导入文件替换本机数据:" + Replaced + " 天;当前共 " + TotalDays + " 天。";
            return "新增 " + Added + " 天,合并 " + Merged + " 天,保留本机 " + KeptLocal + " 天;当前共 " + TotalDays + " 天。";
        }
    }

    /// <summary>一条数据校验结果。</summary>
    internal sealed class ValidationIssue
    {
        public enum Level { Info, Warning, Error }
        public readonly Level Severity;
        public readonly DateTime Date;
        public readonly string Message;

        public ValidationIssue(Level severity, DateTime date, string message)
        {
            Severity = severity; Date = date; Message = message;
        }
        public string LevelText
        {
            get { return Severity == Level.Error ? "错误" : Severity == Level.Warning ? "警告" : "提示"; }
        }
    }

    internal static class DataManagement
    {
        private const string Magic = "# KeyMouseStats export v1";
        private const string Separator = "---payload---";
        private const string ChecksumPrefix = "# checksum=";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(true);

        public static string DataFile { get { return Path.Combine(Store.DataDirectory, "stats.txt"); } }
        public static string BackupFolder { get { return Path.Combine(Store.DataDirectory, "backup"); } }

        public static string Sha256(string text)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>导出为带校验和的单文件。校验和覆盖分隔线之后的全部内容。</summary>
        public static void Export(string path)
        {
            string payload = Store.ExportPayload();
            if (payload == null) throw new InvalidOperationException("当前数据无法序列化,请稍后重试。");
            StringBuilder text = new StringBuilder();
            text.AppendLine(Magic);
            text.AppendLine("# exported=" + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            text.AppendLine("# days=" + Store.History.Count.ToString(CultureInfo.InvariantCulture));
            text.AppendLine(ChecksumPrefix + Sha256(payload));
            text.AppendLine(Separator);
            text.Append(payload);
            File.WriteAllText(path, text.ToString(), Utf8);
        }

        /// <summary>校验并解析一个导出文件;不写入任何状态。</summary>
        public static ImportPreview Inspect(string path)
        {
            ImportPreview preview = new ImportPreview();
            preview.FileName = Path.GetFileName(path);
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                int separator = text.IndexOf(Separator, StringComparison.Ordinal);
                if (!text.StartsWith(Magic, StringComparison.Ordinal) || separator < 0)
                {
                    preview.Error = "这不是本程序导出的数据文件。";
                    return preview;
                }
                int newline = text.IndexOf('\n', separator);
                string payload = newline >= 0 ? text.Substring(newline + 1) : "";
                string checksum = "";
                foreach (string line in text.Substring(0, separator).Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith(ChecksumPrefix, StringComparison.Ordinal))
                        checksum = trimmed.Substring(ChecksumPrefix.Length).Trim();
                }
                if (checksum.Length == 0) { preview.Error = "文件缺少校验和。"; return preview; }
                if (!string.Equals(checksum, Sha256(payload), StringComparison.OrdinalIgnoreCase))
                {
                    preview.Error = "校验和不匹配,文件可能已损坏或被修改。";
                    return preview;
                }

                preview.Data = Store.ParseText(payload);
                preview.Days = preview.Data.History.Count;
                preview.First = DateTime.MaxValue;
                preview.Last = DateTime.MinValue;
                foreach (KeyValuePair<DateTime, DayRecord> day in preview.Data.History)
                {
                    if (day.Key < preview.First) preview.First = day.Key;
                    if (day.Key > preview.Last) preview.Last = day.Key;
                    preview.ImportedKeys += day.Value.Keys;
                    DayRecord local;
                    if (Store.History.TryGetValue(day.Key, out local))
                    {
                        preview.Conflicts++;
                        preview.ConflictKeysLocal += local.Keys;
                        preview.ConflictKeysImported += day.Value.Keys;
                    }
                }
                preview.Valid = true;
            }
            catch (Exception error)
            {
                preview.Error = error.GetType().Name + ": " + error.Message;
            }
            return preview;
        }

        /// <summary>应用导入结果。replace 为 true 时整体替换(含设置),否则只合并日记录。</summary>
        public static ImportResult Apply(ImportPreview preview, MergeStrategy strategy, bool replace)
        {
            if (preview == null || !preview.Valid || preview.Data == null)
                throw new InvalidOperationException("导入数据未通过校验,已中止。");
            ImportResult result = new ImportResult();
            result.KeysBefore = Store.Total.Keys;

            if (replace)
            {
                result.Replaced = preview.Data.History.Count;
                Store.ApplyParsed(preview.Data, true);
            }
            else
            {
                foreach (KeyValuePair<DateTime, DayRecord> pair in preview.Data.History)
                {
                    DayRecord local;
                    if (!Store.History.TryGetValue(pair.Key, out local))
                    {
                        Store.History[pair.Key] = pair.Value;
                        result.Added++;
                        continue;
                    }
                    if (strategy == MergeStrategy.Add)
                    {
                        local.AddFrom(pair.Value);
                        result.Merged++;
                    }
                    else if (pair.Value.Keys > local.Keys
                        || (pair.Value.Keys == local.Keys && pair.Value.ActiveSeconds > local.ActiveSeconds))
                    {
                        Store.History[pair.Key] = pair.Value;
                        result.Merged++;
                    }
                    else result.KeptLocal++;
                }
                // 累计总数是近似值:相加时扣掉冲突日的导入部分,避免明显重复。
                if (strategy == MergeStrategy.Add)
                {
                    Store.Total.Keys += Math.Max(0, preview.Data.Total.Keys - preview.ConflictKeysImported);
                    Store.Total.Clicks += preview.Data.Total.Clicks;
                    Store.Total.Wheel += preview.Data.Total.Wheel;
                    Store.Total.MovePx += preview.Data.Total.MovePx;
                    Store.Total.MoveMeters += preview.Data.Total.MoveMeters;
                }
                else
                {
                    if (preview.Data.Total.Keys > Store.Total.Keys) Store.Total.Keys = preview.Data.Total.Keys;
                    if (preview.Data.Total.Clicks > Store.Total.Clicks) Store.Total.Clicks = preview.Data.Total.Clicks;
                    if (preview.Data.Total.Wheel > Store.Total.Wheel) Store.Total.Wheel = preview.Data.Total.Wheel;
                    if (preview.Data.Total.MovePx > Store.Total.MovePx) Store.Total.MovePx = preview.Data.Total.MovePx;
                    if (preview.Data.Total.MoveMeters > Store.Total.MoveMeters) Store.Total.MoveMeters = preview.Data.Total.MoveMeters;
                }
                Store.RollDay(DateTime.Today);
            }

            Store.Save();
            result.TotalDays = Store.History.Count;
            result.KeysAfter = Store.Total.Keys;
            return result;
        }

        /// <summary>逐日不变量检查。返回按日期从新到旧排列的问题清单。</summary>
        public static List<ValidationIssue> Validate()
        {
            List<ValidationIssue> issues = new List<ValidationIssue>();
            DateTime today = DateTime.Today;
            List<DateTime> dates = new List<DateTime>(Store.History.Keys);
            dates.Sort();
            dates.Reverse();

            foreach (DateTime date in dates)
            {
                DayRecord day = Store.History[date];
                ValidationIssue.Level error = ValidationIssue.Level.Error;
                ValidationIssue.Level warning = ValidationIssue.Level.Warning;

                if (date > today)
                    issues.Add(new ValidationIssue(warning, date, "日期在未来,可能是系统时间被修改过。"));
                if (day.Keys < 0 || day.Clicks < 0 || day.Wheel < 0 || day.MovePx < 0 || day.MoveMeters < 0
                    || day.ActiveSeconds < 0 || day.IdleSeconds < 0)
                    issues.Add(new ValidationIssue(error, date, "存在负值计数。"));
                if (day.ActiveSeconds > 86400)
                    issues.Add(new ValidationIssue(error, date, "活跃时长超过 24 小时(" + ActivityMonitor.FormatDuration(day.ActiveSeconds) + ")。"));
                if (day.IdleSeconds > 86400)
                    issues.Add(new ValidationIssue(error, date, "空闲时长超过 24 小时。"));

                long hourKeys = Sum(day.HourKeys);
                if (hourKeys > day.Keys)
                    issues.Add(new ValidationIssue(warning, date, "小时击键分桶之和大于当日击键。"));
                long hourClicks = Sum(day.HourClicks);
                if (hourClicks > day.Clicks)
                    issues.Add(new ValidationIssue(warning, date, "小时点击分桶之和大于当日点击。"));
                long keyCounts = 0;
                foreach (long value in day.KeyCounts.Values) keyCounts += value;
                if (keyCounts > day.Keys)
                    issues.Add(new ValidationIssue(warning, date, "逐键计数之和大于当日击键。"));

                double sessionSeconds = 0;
                foreach (ActiveSession session in day.Sessions)
                {
                    sessionSeconds += session.Seconds;
                    if (session.End < session.Start)
                        issues.Add(new ValidationIssue(error, date, "连续使用段的结束时间早于开始时间。"));
                }
                if (sessionSeconds > day.ActiveSeconds + Math.Max(60, day.ActiveSeconds * 0.05))
                    issues.Add(new ValidationIssue(warning, date, "连续段时长之和明显大于活跃时长。"));
                if (day.AppObservedSeconds > day.ActiveSeconds + Math.Max(60, day.ActiveSeconds * 0.05))
                    issues.Add(new ValidationIssue(warning, date, "应用观测时长大于活跃时长。"));
                double appSeconds = 0;
                foreach (AppUsage app in day.Apps.Values) appSeconds += app.ActiveSeconds;
                if (day.AppObservedSeconds > 0 && appSeconds > day.AppObservedSeconds + Math.Max(60, day.AppObservedSeconds * 0.05))
                    issues.Add(new ValidationIssue(warning, date, "应用归因时长大于应用观测时长。"));

                double observedHours = 0;
                foreach (double value in day.Cross.ObservedHours) observedHours += value;
                if (observedHours > 86400)
                    issues.Add(new ValidationIssue(error, date, "交叉观测的小时之和超过 24 小时。"));
            }

            if (Store.History.Count == 0)
                issues.Add(new ValidationIssue(ValidationIssue.Level.Info, DateTime.Today, "数据文件里还没有任何日记录。"));
            return issues;
        }

        private static long Sum(long[] values)
        {
            long total = 0;
            foreach (long value in values) total += value;
            return total;
        }

        /// <summary>立即备份当前数据文件,返回备份路径。</summary>
        public static string BackupNow()
        {
            string source = DataFile;
            if (!File.Exists(source)) throw new InvalidOperationException("还没有数据文件可以备份。");
            Directory.CreateDirectory(BackupFolder);
            string stamp = "stats-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(BackupFolder, stamp + ".txt");
            int suffix = 1;
            while (File.Exists(target))
                target = Path.Combine(BackupFolder, stamp + "-" + (suffix++).ToString(CultureInfo.InvariantCulture) + ".txt");
            File.Copy(source, target);
            return target;
        }

        /// <summary>按文件名从新到旧列出备份。</summary>
        public static List<string> ListBackups()
        {
            List<string> paths = new List<string>();
            try
            {
                if (Directory.Exists(BackupFolder))
                    paths.AddRange(Directory.GetFiles(BackupFolder, "stats-*.txt"));
            }
            catch { }
            paths.Sort(delegate(string a, string b) { return string.CompareOrdinal(b, a); });
            return paths;
        }

        /// <summary>只保留最新的 keep 份备份,返回删除数量。</summary>
        public static int RotateBackups(int keep)
        {
            if (keep < 1) keep = 1;
            List<string> paths = ListBackups();
            int removed = 0;
            for (int i = keep; i < paths.Count; i++)
            {
                try { File.Delete(paths[i]); removed++; }
                catch { }
            }
            return removed;
        }

        /// <summary>每天最多留一份备份;今天已有则返回 null。</summary>
        public static string EnsureDailyBackup()
        {
            if (!File.Exists(DataFile)) return null;
            string today = "stats-" + DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            foreach (string path in ListBackups())
                if (Path.GetFileName(path).StartsWith(today, StringComparison.Ordinal)) return null;
            string created = BackupNow();
            RotateBackups(Store.BackupKeep);
            return created;
        }
    }
}
