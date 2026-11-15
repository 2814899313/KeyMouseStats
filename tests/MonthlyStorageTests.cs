using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 按月存储迁移与删除：旧 stats.txt 里的逐日明细应拆进 年/月.txt，
/// 加载时再合并；用户删除某个月份文件后，后续保存不能偷偷把它写回来。
/// </summary>
internal static class MonthlyStorageTests
{
    private static int checks;
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "MonthlyStorage-" + Guid.NewGuid().ToString("N"));

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.WriteLine("FAILED: " + error.Message); Environment.ExitCode = 1; return; }
        Console.WriteLine("Monthly storage: " + checks + " checks passed");
    }

    private static void Run()
    {
        Store.DataDirectory = Dir;
        Directory.CreateDirectory(Dir);
        try
        {
            DateTime today = DateTime.Today;
            DateTime old1 = new DateTime(today.Year - 1, 12, 31);
            DateTime old2 = new DateTime(today.Year, 1, 15);
            string legacy = "format=12\nkeep_days=365\ntheme=0\n"
                + "[day=" + old1.ToString("yyyy-MM-dd") + "]\nkeys=11\n"
                + "[day=" + old2.ToString("yyyy-MM-dd") + "]\nkeys=22\n"
                + "[day=" + today.ToString("yyyy-MM-dd") + "]\nkeys=33\n";
            File.WriteAllText(Path.Combine(Dir, "stats.txt"), legacy, Encoding.UTF8);

            Store.History.Clear();
            Store.Load();
            Check(Store.History.ContainsKey(old1) && Store.History.ContainsKey(old2), "legacy days are loaded");
            Check(Store.KeepDays == 0, "legacy default retention migrates to permanent");

            Store.Save();
            string year1 = Path.Combine(Dir, (today.Year - 1).ToString("0000"), (today.Year - 1).ToString("0000") + "-12.txt");
            string year2 = Path.Combine(Dir, today.Year.ToString("0000"), today.Year.ToString("0000") + "-01.txt");
            Check(File.Exists(year1), "December is written to its year folder");
            Check(File.Exists(year2), "January is written to its year folder");
            string current = File.ReadAllText(Path.Combine(Dir, "stats.txt"), Encoding.UTF8);
            Check(current.IndexOf("[day=" + old1.ToString("yyyy-MM-dd"), StringComparison.Ordinal) < 0, "old day is removed from stats.txt");
            Check(current.IndexOf("[day=" + today.ToString("yyyy-MM-dd"), StringComparison.Ordinal) >= 0, "current month stays in stats.txt");

            Store.History.Clear();
            Store.Load();
            Check(Store.History.ContainsKey(old1) && Store.History[old1].Keys == 11, "month file round trips");
            Check(Store.History.ContainsKey(old2) && Store.History[old2].Keys == 22, "second month file round trips");

            File.Delete(year2);
            Store.History.Clear();
            Store.Load();
            Check(!Store.History.ContainsKey(old2), "deleted month is not loaded back");
            Store.Save();
            Check(!File.Exists(year2), "saving after deletion does not recreate the month");
        }
        finally
        {
            try { Directory.Delete(Dir, true); } catch { }
        }
    }
}
