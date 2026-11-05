using System;
using System.Collections.Generic;
using System.IO;
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

        public long ProgramTotal
        {
            get { return ProgramBytes + BackupBytes + CoreBytes + ReportBytes; }
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
            usage.CoreBytes = FileBytes(Path.Combine(dataDirectory, "stats.txt"));
            usage.BackupBytes = FileBytes(Path.Combine(dataDirectory, "stats.txt.bak"))
                + FolderBytes(Path.Combine(dataDirectory, "backup"));
            usage.ReportBytes = FolderBytes(Path.Combine(dataDirectory, "日报"));
            usage.CharacterPath = characterPath ?? "";
            usage.CharacterBytes = FileBytes(usage.CharacterPath);
            usage.CharacterExists = usage.CharacterBytes > 0;
            return usage;
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

        /// <summary>紧凑的人类可读大小;0 显示 0 MB,极小值显示 &lt; 0.1 MB。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            if (bytes < 100L * 1024) return "< 0.1 MB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }

        /// <summary>占比文案;低于 0.1% 时明确写 &lt; 0.1%,不硬凑成 0.0%。</summary>
        public static string PercentText(long part, long total)
        {
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
