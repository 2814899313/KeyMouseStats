using System;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 存储可靠性回归:同步 / 异步保存、合并写入、新旧快照顺序、原子替换备份与保留窗口。
/// 全程只在测试自己的临时目录读写,不接触用户的真实统计数据。
/// </summary>
internal static class SaveReliabilityTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
    private static void Seed(int days, long baseKeys)
    {
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        for (int i = 0; i < days; i++)
        {
            DateTime date = DateTime.Today.AddDays(-i);
            DayRecord day = i == 0 ? Store.Today : new DayRecord();
            day.Date = date;
            day.Keys = baseKeys + i;
            day.Clicks = 100 + i;
            day.Wheel = 7 + i;
            day.ActiveSeconds = 3600 + i;
            day.HourKeys[9] = 5 + i;
            day.KeyCounts[65] = 10 + i;
            Store.History[date] = day;
            Store.Total.Keys += day.Keys;
        }
    }
    private static void Reset()
    {
        Store.History.Clear();
        Store.Total = new Counters();
        Store.MouseDpi = 0;
        Store.RollDay(DateTime.Today);
    }
    private static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "KeyMouseSaveTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "stats.txt");
        Store.DataDirectory = directory;
        try
        {
            Check(Store.DataDirectory == directory, "data directory redirect");
            Check(file == Path.Combine(Store.DataDirectory, "stats.txt"), "file path follows the data directory");

            // 同步保存:首次写入没有备份,第二次写入保留上一份快照。
            Seed(3, 1000);
            Store.Save();
            Check(File.Exists(file), "sync save writes stats.txt");
            Check(!File.Exists(file + ".bak"), "first save has no previous file to back up");
            Check(!File.Exists(file + ".tmp"), "temp file is consumed by the atomic replace");
            Seed(3, 2000);
            Store.Save();
            Check(File.Exists(file + ".bak"), "second save keeps the previous file as backup");
            Check(File.ReadAllText(file + ".bak").Contains("keys=1000"), "backup holds the previous snapshot");

            // 异步保存:Flush 之后内容可被完整读回。
            Seed(5, 7000);
            Store.SaveAsync();
            Check(Store.Flush(5000), "flush waits for the async write");
            Reset();
            Store.Load();
            Check(Store.Today.Keys == 7000, "async save round trips today keys");
            Check(Store.Today.Clicks == 100, "async save round trips clicks");
            Check(Store.Today.Wheel == 7, "async save round trips wheel");
            Check(Store.Today.ActiveSeconds == 3600, "async save round trips active seconds");
            Check(Store.Today.HourKeys[9] == 5, "async save round trips hourly buckets");
            Check(Store.Today.KeyCounts[65] == 10, "async save round trips key counts");
            Check(Store.History.Count == 5, "async save keeps every seeded day");

            // 合并写入:连续多次 SaveAsync 只落盘最新快照。
            Seed(2, 100);
            Store.SaveAsync();
            Seed(2, 200);
            Store.SaveAsync();
            Seed(2, 300);
            Store.SaveAsync();
            Check(Store.Flush(5000), "flush after coalesced saves");
            Reset();
            Store.Load();
            Check(Store.Today.Keys == 300, "coalesced saves keep the newest snapshot");
            Check(Store.History.Count == 2, "coalesced saves do not resurrect older snapshots");

            // 顺序保证:后台旧快照不能覆盖之后完成的同步保存。
            Seed(2, 400);
            Store.SaveAsync();
            Seed(2, 500);
            Store.Save();
            Check(Store.Flush(5000), "flush after mixed async and sync saves");
            Reset();
            Store.Load();
            Check(Store.Today.Keys == 500, "newer synchronous snapshot wins");

            // 没有待写内容时 Flush 立即成功。
            Check(Store.Flush(0), "flush is a no-op when nothing is pending");

            // 365 天保留窗口仍然生效,且被裁剪的日期不会出现在文件里。
            Seed(1, 42);
            DateTime ancient = DateTime.Today.AddDays(-400);
            DayRecord old = new DayRecord();
            old.Date = ancient;
            old.Keys = 999;
            Store.History[ancient] = old;
            Store.Save();
            Check(!File.ReadAllText(file).Contains(ancient.ToString("yyyy-MM-dd")), "save prunes days outside the retention window");

            LongPathAndVisibleFailure(directory);
        }
        finally
        {
            try
            {
                foreach (string name in new string[] { "stats.txt", "stats.txt.tmp", "stats.txt.bak" })
                {
                    string owned = Path.Combine(directory, name);
                    if (File.Exists(owned)) File.Delete(owned);
                }
                Directory.Delete(directory);
            }
            catch { }
        }
        Console.WriteLine("PASS: " + _checks + " storage reliability checks");
    }

    private static void LongPathAndVisibleFailure(string normalDirectory)
    {
        string root = Path.Combine(Path.GetTempPath(), "KeyMouseLong-" + Guid.NewGuid().ToString("N"));
        while (root.Length < 250) root += "\\" + new string('r', 18);
        string longDirectory = root + "\\data";
        string blocker = Path.Combine(Path.GetTempPath(), "KeyMouseBlock-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(Store.LongPath(@"C:\short") == @"C:\short", "短路径不应加前缀");
            Check(Store.LongPath(@"C:\" + new string('x', 260)).StartsWith(@"\\?\"), "长本地路径应加 \\?\\ 前缀");
            Check(Store.LongPath(@"\\server\share\" + new string('x', 260)).StartsWith(@"\\?\UNC\"),
                "长 UNC 路径应加 UNC 长路径前缀");

            Store.DataDirectory = longDirectory;
            Store.History.Clear();
            Store.Total = new Counters();
            Store.RollDay(DateTime.Today);
            Store.Today.Keys = 7;
            Store.LastSaveError = "";
            Store.Save();
            Check(Store.LastSaveError == "", "长路径保存应成功: " + Store.LastSaveError);
            Check(LongPathIO.FileExists(Store.LongPath(Path.Combine(longDirectory, "stats.txt"))),
                "长路径下 stats.txt 应真实存在");
            LongPathIO.CreateDirectory(Store.LongPath(Path.Combine(longDirectory, "2025")));
            string[] longDirs = LongPathIO.GetDirectories(Store.LongPath(longDirectory));
            Check(longDirs.Length >= 1, "长路径目录枚举应能看到子目录");
            Store.History.Clear();
            Store.RollDay(DateTime.Today);
            Store.Load();
            Check(Store.Today.Keys == 7, "长路径下 stats.txt 应能读回");

            // 父级是文件时,Directory.CreateDirectory 必然失败;错误必须可见。
            File.WriteAllText(blocker, "not a directory");
            Store.DataDirectory = Path.Combine(blocker, "data");
            Store.Today.Keys = 1;
            Store.LastSaveError = "";
            Store.Save();
            Check(!string.IsNullOrEmpty(Store.LastSaveError), "写入失败必须留下用户可见错误");
            Check(Store.SaveFailureVersion > 0, "写入失败必须递增可见状态版本");

            Store.DataDirectory = normalDirectory;
            Store.History.Clear();
            Store.RollDay(DateTime.Today);
            Store.Today.Keys = 1;
            Store.Save();
            Check(Store.LastSaveError == "", "后续成功保存应清除错误状态");
        }
        finally
        {
            Store.DataDirectory = normalDirectory;
            try { File.Delete(blocker); } catch { }
            try { LongPathIO.DeleteDirectory(root); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
