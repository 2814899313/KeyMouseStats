using System;
using System.IO;
using KeyMouseStats;

/// <summary>存储页口径:四个本程序数据块 + 外部字符日志,以及大小/占比文案。</summary>
internal static class StorageUsageTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void WriteSized(string path, long bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength(bytes);
    }

    private static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "StorageUsage-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        try
        {
            string exe = Path.Combine(root, "app.exe");
            WriteSized(exe, 1000);
            WriteSized(Path.Combine(data, "stats.txt"), 2000);
            WriteSized(Path.Combine(data, "stats.txt.bak"), 3000);
            WriteSized(Path.Combine(data, "backup", "a.txt"), 4000);
            WriteSized(Path.Combine(data, "backup", "nested", "b.txt"), 5000);
            WriteSized(Path.Combine(data, "日报", "2026-11-05.md"), 600);
            string rime = Path.Combine(root, "commit_log.csv");
            WriteSized(rime, 700);

            StorageUsage usage = StorageUsage.Measure(exe, data, rime);
            Check(usage.ProgramBytes == 1000, "program size is the exe length");
            Check(usage.CoreBytes == 2000, "core size is stats.txt");
            Check(usage.BackupBytes == 12000, "backup is .bak plus the backup folder, recursively");
            Check(usage.ReportBytes == 600, "report size is the 日报 folder");
            Check(usage.ProgramTotal == 15600, "total sums the four local blocks");
            Check(usage.CharacterExists && usage.CharacterBytes == 700, "external character log is measured separately");
            Check(usage.CharacterBytes < usage.ProgramTotal, "character log is not added to the app total");

            string missing = Path.Combine(root, "nope.csv");
            StorageUsage absent = StorageUsage.Measure(exe, data, missing);
            Check(!absent.CharacterExists && absent.CharacterBytes == 0, "missing character log is marked disabled");
            Check(StorageUsage.FolderBytes(Path.Combine(root, "no-folder")) == 0, "missing folder is zero");

            Check(StorageUsage.FormatSize(0) == "0 MB", "zero size");
            Check(StorageUsage.FormatSize(50 * 1024) == "< 0.1 MB", "small size");
            Check(StorageUsage.FormatSize(1024 * 1024) == "1.0 MB", "megabyte size");
            Check(StorageUsage.FormatSize(1536L * 1024 * 1024) == "1.50 GB", "gigabyte size");
            Check(StorageUsage.FormatSizePrecise(178000) == "0.17 MB", "precise footnote size");

            Check(StorageUsage.PercentText(0, 100) == "0%", "zero percent");
            Check(StorageUsage.PercentText(1, 100000) == "< 0.1%", "tiny percent");
            Check(StorageUsage.PercentText(50, 100) == "50.0%", "half percent");
            Check(StorageUsage.PercentText(100, 100) == "100%", "full percent");

            Console.WriteLine("Storage usage: " + checks + " checks passed");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("StorageUsageTests", Run);
    }
}
