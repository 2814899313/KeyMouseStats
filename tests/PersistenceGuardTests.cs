using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 持久化护栏回归:测试 / 预览进程不得写用户数据目录,读取失败不得静默覆盖。
/// 事故现场:渲染测试没有重定向 Store.DataDirectory 就灌了 80 天夹具,
/// 构造并关闭面板时 Store.SaveAsync() 把夹具写进了 %APPDATA%\键鼠统计\stats.txt。
/// 因此这里的判据刻意与文件沙箱无关:即使运行环境本身不允许写 AppData,
/// 只要 Store 真的尝试过保存,内部的保存序号就会变化,这里必须发现它。
/// </summary>
internal static class PersistenceGuardTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    /// <summary>读取 Store 的私有静态字段,用来证明保存流程根本没启动。</summary>
    private static object Private(string name)
    {
        FieldInfo field = typeof(Store).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
        if (field == null) throw new Exception("Store 缺少内部字段 " + name);
        return field.GetValue(null);
    }

    private static long SaveSequence() { return (long)Private("_saveSequence"); }
    private static long WrittenSequence() { return (long)Private("_writtenSequence"); }
    private static bool WriterActive() { return (bool)Private("_writerActive"); }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null) return left == right;
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    /// <summary>真实 stats.txt 的 size / 时间戳 / 内容指纹。读不到(被占用)时返回 null 并重试。</summary>
    private static string FileSignature(string path, out long length, out DateTime writeUtc)
    {
        length = -1;
        writeUtc = DateTime.MinValue;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    length = stream.Length;
                    using (SHA256 sha = SHA256.Create())
                    {
                        byte[] hash = sha.ComputeHash(stream);
                        writeUtc = File.GetLastWriteTimeUtc(path);
                        StringBuilder hex = new StringBuilder(hash.Length * 2);
                        foreach (byte value in hash) hex.Append(value.ToString("x2"));
                        return hex.ToString();
                    }
                }
            }
            catch (IOException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
        return null;
    }

    /// <summary>与 AnomalyRenderTests 相同的 80 天夹具(未重定向时必须一个字节都不能落盘)。</summary>
    private static void SeedRenderFixture()
    {
        Store.History.Clear();
        Store.RollDay(DateTime.Today);
        for (int i = 0; i < 80; i++)
        {
            DayRecord day = new DayRecord();
            day.Date = DateTime.Today.AddDays(-i);
            day.Keys = i == 1 ? 6000 : 1000 + i % 5 * 30;
            day.ActiveSeconds = 3600;
            Store.History[day.Date] = day;
        }
    }

    private static int SeedRoundTrip()
    {
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        // 只取本月的日期:stats.txt 只记录当月,跨月会写进年目录,读回条数会随月份变少。
        int back = Math.Min(2, DateTime.Today.Day - 1);
        for (int i = 0; i <= back; i++)
        {
            DateTime date = DateTime.Today.AddDays(-i);
            DayRecord day = i == 0 ? Store.Today : new DayRecord();
            day.Date = date;
            day.Keys = 900 + i;
            day.Clicks = 90 + i;
            day.Wheel = 9 + i;
            day.ActiveSeconds = 1200 + i;
            day.HourKeys[10] = 3 + i;
            day.KeyCounts[66] = 7 + i;
            Store.History[date] = day;
            Store.Total.Keys += day.Keys;
        }
        return back + 1;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) File.Delete(file);
            Directory.Delete(directory, true);
        }
        catch { }
    }

    private static void Main()
    {
        string defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "键鼠统计");
        string defaultFile = Path.Combine(defaultDirectory, "stats.txt");
        string roundTripDirectory = Path.Combine(Path.GetTempPath(), "KeyMouseGuardRoundTrip-" + Guid.NewGuid().ToString("N"));
        string badDirectory = Path.Combine(Path.GetTempPath(), "KeyMouseGuardBad-" + Guid.NewGuid().ToString("N"));
        string lockedDirectory = Path.Combine(Path.GetTempPath(), "KeyMouseGuardLocked-" + Guid.NewGuid().ToString("N"));
        try
        {
            // ---- a) 未 armed + 未重定向:保存必须完全不动,尤其不能写真实 stats.txt ----
            // 这一段必须最先跑:DataDirectory 一旦重定向,_redirected 就会一直为 true。
            Check(Store.DataDirectory == defaultDirectory, "测试进程默认读的仍是用户数据目录");
            Check(!Store.PersistenceArmed, "普通测试进程默认没有 armed");
            Check(!Store.CanPersist, "未重定向时禁止写盘");

            SeedRenderFixture();   // 事故场景:80 天夹具
            long saveBefore = SaveSequence();
            long writtenBefore = WrittenSequence();
            Check(saveBefore == 0 && writtenBefore == 0, "保存序号从零开始");

            bool fileUnchanged = false;
            string changeDetail = "";
            for (int attempt = 0; attempt < 3 && !fileUnchanged; attempt++)
            {
                long sizeBefore;
                DateTime timeBefore;
                string hashBefore = FileSignature(defaultFile, out sizeBefore, out timeBefore);
                Store.Save();
                Store.SaveAsync();
                Check(Store.Flush(5000), "没有待写快照时 Flush 立即成功");
                long sizeAfter;
                DateTime timeAfter;
                string hashAfter = FileSignature(defaultFile, out sizeAfter, out timeAfter);
                if (hashBefore == null && hashAfter == null) fileUnchanged = true;
                else
                {
                    fileUnchanged = sizeBefore == sizeAfter && timeBefore == timeAfter
                        && string.Equals(hashBefore, hashAfter, StringComparison.Ordinal);
                    changeDetail = "size " + sizeBefore + "->" + sizeAfter
                        + ", mtime " + timeBefore.ToString("O") + "->" + timeAfter.ToString("O");
                }
                if (!fileUnchanged)
                {
                    // 产品进程可能正在每 30 秒写一次真实 stats.txt:等它写完再重试,
                    // 但下面与沙箱无关的序号断言始终是主判据。
                    Thread.Sleep(1500);
                }
            }
            Check(SaveSequence() == saveBefore, "未 armed 时 Save/SaveAsync 不启动保存流程");
            Check(WrittenSequence() == writtenBefore, "未 armed 时 WriteSnapshot 不执行");
            Check(!WriterActive(), "未 armed 时没有后台写入线程");
            Check(Private("_pendingText") == null, "未 armed 时没有排队快照");
            Check(fileUnchanged, "未 armed 时真实 stats.txt 的 size/mtime/内容不变 " + changeDetail);

            // 真实事故路径(AnomalyRenderTests 同款):差 80 天夹具 + 打开/关闭面板。
            // 面板打开时会生成昨天的日报并调用 Store.Save(),关闭时也可能排队保存:
            // 护栏必须让这些调用全部变成空操作,否则夹具就会覆盖用户历史。
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using (Dashboard form = new Dashboard())
            {
                typeof(Dashboard).GetMethod("OnLoad", flags).Invoke(form, new object[] { EventArgs.Empty });
                typeof(Dashboard).GetMethod("OnFormClosed", flags).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
            }
            Check(SaveSequence() == saveBefore, "面板打开/关闭不启动保存流程");
            Check(WrittenSequence() == writtenBefore, "面板打开/关闭不执行 WriteSnapshot");

            // ---- b) 显式重定向 DataDirectory 后照常可以保存 / 读回 ----
            Directory.CreateDirectory(roundTripDirectory);
            string roundTripFile = Path.Combine(roundTripDirectory, "stats.txt");
            Store.DataDirectory = roundTripDirectory;
            Check(Store.CanPersist, "重定向到测试目录后允许写盘");
            int seededDays = SeedRoundTrip();
            Store.Save();
            Check(File.Exists(roundTripFile), "重定向后 Save 写出 stats.txt");
            Store.SaveAsync();
            Check(Store.Flush(5000), "重定向后异步保存可等待完成");
            Store.History.Clear();
            Store.Total = new Counters();
            Store.RollDay(DateTime.Today);
            Store.Load();
            Check(!Store.LoadFailed, "正常文件读取不置失败标记");
            Check(Store.Today.Keys == 900, "重定向后保存的今日击键可读回");
            Check(Store.Today.Clicks == 90, "重定向后保存的今日点击可读回");
            Check(Store.History.Count == seededDays, "重定向后保存的每一天都可读回");

            // ---- b2) 日报也必须跟随重定向后的数据目录 ----
            // 事故补充:只挡住 CanPersist 还不够。渲染测试重定向 DataDirectory 之后 CanPersist 为 true,
            // 而旧版日报目录写死 %APPDATA%\键鼠统计\日报,于是夹具日报照样落进用户目录
            // (2026-11-14.md「无数据」就是这么来的)。日报目录必须跟随 Store.DataDirectory。
            DailyReport sampleReport = new DailyReport();
            sampleReport.Day = DateTime.Today.AddDays(-1);
            string reportPath = sampleReport.Save();
            Check(reportPath != null && reportPath.Length > 0, "重定向后日报可以写盘");
            Check(reportPath.StartsWith(roundTripDirectory, StringComparison.OrdinalIgnoreCase),
                "日报必须写进重定向后的数据目录,而不是用户目录: " + reportPath);
            Check(DailyReport.Directory.StartsWith(roundTripDirectory, StringComparison.OrdinalIgnoreCase),
                "日报目录跟随 Store.DataDirectory");

            // ---- c1) 文件里明明有 [day=] 却解析不出日记录:读取失败并拒绝覆盖 ----
            Directory.CreateDirectory(badDirectory);
            string badFile = Path.Combine(badDirectory, "stats.txt");
            File.WriteAllText(badFile, "format=13\n[day=2026-13-45]\nkeys=123\n", new UTF8Encoding(false));
            byte[] badBefore = File.ReadAllBytes(badFile);
            Store.DataDirectory = badDirectory;
            Check(Store.CanPersist, "失败场景里也先重定向(否则拒绝保存可能只是因为没 armed)");
            Store.History.Clear();
            Store.RollDay(DateTime.Today);
            Store.Load();
            Check(Store.LoadFailed, "有 [day=] 但解析为空 → 读取失败标记");
            Store.Save();
            Store.SaveAsync();
            Check(Store.Flush(2000), "读取失败时 Flush 不等待任何写入");
            Check(BytesEqual(badBefore, File.ReadAllBytes(badFile)), "读取失败后 Save 不覆盖原文件");
            Check(!File.Exists(badFile + ".tmp"), "读取失败后没有留下临时文件");

            // ---- c2) 文件读不出来(被独占占用):同样拒绝覆盖 ----
            Directory.CreateDirectory(lockedDirectory);
            string lockedFile = Path.Combine(lockedDirectory, "stats.txt");
            File.WriteAllText(lockedFile, "format=13\n[day=2026-01-01]\nkeys=777\n", new UTF8Encoding(false));
            byte[] lockedBefore = File.ReadAllBytes(lockedFile);
            Store.DataDirectory = lockedDirectory;
            using (new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Store.Load();
            }
            Check(Store.LoadFailed, "文件被占用读不出来 → 读取失败标记");
            Store.Save();
            Store.SaveAsync();
            Check(Store.Flush(2000), "文件被占用时 Flush 不等待任何写入");
            Check(BytesEqual(lockedBefore, File.ReadAllBytes(lockedFile)), "读取失败后不覆盖被占用文件");

            // ---- d) 之后的成功读取必须清掉失败标记,不能把程序永久卡在只读状态 ----
            Store.DataDirectory = roundTripDirectory;
            Store.History.Clear();
            Store.RollDay(DateTime.Today);
            Store.Load();
            Check(!Store.LoadFailed, "成功读取后清除读取失败标记");
            Check(Store.Today.Keys == 900, "清除失败标记后仍能读回数据");
        }
        finally
        {
            DeleteDirectory(roundTripDirectory);
            DeleteDirectory(badDirectory);
            DeleteDirectory(lockedDirectory);
        }
        Console.WriteLine("PASS: " + _checks + " persistence guard checks");
    }
}
