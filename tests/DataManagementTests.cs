using System;
using System.Collections.Generic;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 数据管理回归:导出校验和、导入检查、两种合并策略、备份轮转与逐日校验。
/// 全程只在测试自己的临时目录读写,不接触用户的真实统计数据。
/// </summary>
internal static class DataManagementTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
    private static void Near(double actual, double expected, string name)
    {
        Check(Math.Abs(actual - expected) < 1e-9, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static bool HasIssue(List<ValidationIssue> issues, string fragment)
    {
        foreach (ValidationIssue issue in issues) if (issue.Message.Contains(fragment)) return true;
        return false;
    }

    private static DayRecord Day(DateTime date, long keys, double active)
    {
        DayRecord day = new DayRecord();
        day.Date = date;
        day.Keys = keys;
        day.Clicks = keys / 10;
        day.Wheel = 5;
        day.ActiveSeconds = active;
        day.HourKeys[9] = keys / 2;
        day.KeyCounts[65] = keys / 4;
        day.Sessions.Add(new ActiveSession { Start = date.AddHours(9), End = date.AddHours(10), Seconds = active / 2 });
        AppUsage app = new AppUsage { ProcessPath = @"C:\a.exe", Title = "a", Keys = keys / 2, Clicks = 1, ActiveSeconds = active / 2 };
        day.Apps[app.Id] = app;
        return day;
    }

    private static void Seed(Dictionary<DateTime, DayRecord> history, DateTime date, long keys, double active)
    {
        history[date] = Day(date, keys, active);
    }

    private static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "KeyMouseDataTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Store.DataDirectory = directory;
        string exportPath = Path.Combine(directory, "export.txt");
        try
        {
            // ---- 导出 / 检查 ----
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            Seed(Store.History, DateTime.Today.AddDays(-1), 1000, 3600);
            Seed(Store.History, DateTime.Today.AddDays(-2), 2000, 7200);
            Store.Total.Keys = 3000;
            Store.Save();
            DataManagement.Export(exportPath);
            Check(File.Exists(exportPath), "export writes a file");

            ImportPreview preview = DataManagement.Inspect(exportPath);
            Check(preview.Valid, "export passes its own checksum: " + preview.Error);
            // 保存会写入今日的空分节,因此导出里是「今天 + 两个历史日」。
            Check(preview.Days == 3, "export preview counts days");
            Check(preview.Conflicts == 3, "same days are reported as conflicts");
            Check(preview.ImportedKeys == 3000, "imported keys counted");
            Check(preview.First == DateTime.Today.AddDays(-2) && preview.Last == DateTime.Today, "date range reported");

            // ---- 校验和必须真的起作用 ----
            string tampered = File.ReadAllText(exportPath).Replace("keys=1000", "keys=9999");
            string tamperedPath = Path.Combine(directory, "tampered.txt");
            File.WriteAllText(tamperedPath, tampered);
            ImportPreview broken = DataManagement.Inspect(tamperedPath);
            Check(!broken.Valid && broken.Error.Contains("校验和"), "tampered payload is rejected");

            ImportPreview foreign = DataManagement.Inspect(DataManagement.DataFile);
            Check(!foreign.Valid, "a plain data file is not an export");

            // ---- 合并:保留较大值 ----
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            Seed(Store.History, DateTime.Today.AddDays(-1), 100, 3600);
            ImportPreview localPreview = DataManagement.Inspect(exportPath);
            ImportResult kept = DataManagement.Apply(localPreview, MergeStrategy.KeepLarger, false);
            Check(kept.Added == 1 && kept.Merged == 1 && kept.KeptLocal == 1, "keep-larger adds, resolves and keeps as expected");
            Check(Store.History[DateTime.Today.AddDays(-1)].Keys == 1000, "keep-larger keeps the larger record");
            Check(Store.History[DateTime.Today.AddDays(-2)].Keys == 2000, "keep-larger adds the missing day");

            // ---- 合并:相加 ----
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            Seed(Store.History, DateTime.Today.AddDays(-1), 100, 3600);
            ImportPreview addPreview = DataManagement.Inspect(exportPath);
            ImportResult added = DataManagement.Apply(addPreview, MergeStrategy.Add, false);
            Check(added.Merged == 2 && added.Added == 1, "add merges every conflicting day and adds the new one");
            DayRecord merged = Store.History[DateTime.Today.AddDays(-1)];
            Check(merged.Keys == 1100, "add sums keys");
            Check(merged.Clicks == 110, "add sums clicks");
            Near(merged.ActiveSeconds, 7200, "add sums active seconds");
            Check(merged.HourKeys[9] == 500 + 50, "add sums hourly buckets");
            Check(merged.KeyCounts[65] == 250 + 25, "add sums per-key counts");
            Check(merged.Sessions.Count == 2, "add concatenates sessions");
            Near(merged.Apps[@"c:\a.exe" + "\n" + "a"].ActiveSeconds, 1800 + 1800, "add sums per-app time");

            // ---- 整体替换 ----
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            Seed(Store.History, DateTime.Today.AddDays(-30), 7777, 100);
            ImportPreview replacePreview = DataManagement.Inspect(exportPath);
            ImportResult replaced = DataManagement.Apply(replacePreview, MergeStrategy.KeepLarger, true);
            Check(replaced.Replaced == 3, "replace reports the imported day count");
            Check(!Store.History.ContainsKey(DateTime.Today.AddDays(-30)), "replace drops the previous data");
            Check(Store.History.ContainsKey(DateTime.Today.AddDays(-1)), "replace installs the imported data");

            // ---- 备份轮转 ----
            Store.Save();
            Check(DataManagement.EnsureDailyBackup() != null, "first daily backup is created");
            Check(DataManagement.EnsureDailyBackup() == null, "a second daily backup is not created");
            for (int i = 0; i < 4; i++) DataManagement.BackupNow();
            Check(DataManagement.ListBackups().Count == 5, "five backups exist");
            int removed = DataManagement.RotateBackups(3);
            Check(removed == 2 && DataManagement.ListBackups().Count == 3, "rotation keeps the requested number");

            // ---- 逐日校验 ----
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            Store.History[DateTime.Today.AddDays(-1)] = Day(DateTime.Today.AddDays(-1), 1000, 3600);
            Check(DataManagement.Validate().Count == 0, "a consistent day reports no issues");

            DayRecord brokenDay = Day(DateTime.Today.AddDays(-2), 100, 3600);
            brokenDay.ActiveSeconds = 90000;
            Store.History[DateTime.Today.AddDays(-2)] = brokenDay;
            List<ValidationIssue> issues = DataManagement.Validate();
            bool activeError = false;
            foreach (ValidationIssue issue in issues)
                if (issue.Severity == ValidationIssue.Level.Error && issue.Message.Contains("24 小时")) activeError = true;
            Check(activeError, "active time beyond a day is reported as an error");

            DayRecord bucketDay = Day(DateTime.Today.AddDays(-3), 10, 60);
            bucketDay.HourKeys[9] = 999;
            Store.History[DateTime.Today.AddDays(-3)] = bucketDay;
            issues = DataManagement.Validate();
            bool bucketWarning = false;
            foreach (ValidationIssue issue in issues)
                if (issue.Severity == ValidationIssue.Level.Warning && issue.Message.Contains("小时击键")) bucketWarning = true;
            Check(bucketWarning, "hour buckets exceeding daily keys is reported as a warning");

            // ---- 逐日校验:按住时长自洽(1.7.5 新增这一维度) ----
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            DayRecord holdDay = Day(DateTime.Today.AddDays(-1), 200, 600);
            holdDay.HoldCount = 10;
            holdDay.HoldTotalMs = 10 * 120;
            holdDay.HoldMaxMs = 200;
            holdDay.HoldBuckets[2] = 10;
            holdDay.Holds[65] = new KeyHold { Count = 10, TotalMs = 10 * 120, MaxMs = 200 };
            Store.History[holdDay.Date] = holdDay;
            Check(DataManagement.Validate().Count == 0, "a consistent hold record reports no issues");

            holdDay.HoldBuckets[2] = 9;
            Check(HasIssue(DataManagement.Validate(), "分档之和"), "hold buckets diverging from the sample count is reported");
            holdDay.HoldBuckets[2] = 10;

            holdDay.Holds[65].Count = 11;
            Check(HasIssue(DataManagement.Validate(), "逐键样本之和"), "hold per-key detail diverging from the sample count is reported");
            holdDay.Holds[65].Count = 10;

            holdDay.HoldTotalMs = 10 * 90000;
            Check(HasIssue(DataManagement.Validate(), "均值超出"), "an impossible hold mean is reported");
            holdDay.HoldTotalMs = 10 * 120;

            holdDay.HoldMaxMs = 50;
            Check(HasIssue(DataManagement.Validate(), "均值大于最长一次"), "a hold mean above the longest sample is reported");
            holdDay.HoldMaxMs = 200;

            // ---- DayRecord 相加守恒 ----
            DayRecord left = Day(DateTime.Today, 100, 600);
            DayRecord right = Day(DateTime.Today, 250, 1200);
            left.AddFrom(right);
            Check(left.Keys == 350 && left.Clicks == 35, "AddFrom sums scalars");
            Near(left.ActiveSeconds, 1800, "AddFrom sums active seconds");
            Check(left.HourKeys[9] == 50 + 125, "AddFrom sums buckets");
            Check(left.KeyCounts[65] == 25 + 62, "AddFrom sums key counts");
            Check(left.Sessions.Count == 2, "AddFrom concatenates sessions");
            Check(!object.ReferenceEquals(left.Sessions[0], right.Sessions[0]), "AddFrom copies sessions instead of sharing them");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
        Console.WriteLine("PASS: " + _checks + " data management checks");
    }
}
