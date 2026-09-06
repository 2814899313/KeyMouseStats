using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KeyMouseStats;

internal static class AppActivityTests
{
    private static int _checks;
    private static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception(name);
        _checks++;
    }
    private static void Reset()
    {
        Store.History.Clear(); Store.Total = new Counters(); AppActivity.Rules.Clear();
        Store.RollDay(DateTime.Today);
    }
    private static void Main()
    {
        Reset();
        DayRecord day = Store.Today;
        AppUsage editor = new AppUsage { ProcessPath = @"C:\tools\Code.exe", Title = "main.cs — 编辑器" };
        AppUsage video = new AppUsage { ProcessPath = @"C:\browser\chrome.exe", Title = "视频 | 课程 = 第1集" };
        AppUsage docs = new AppUsage { ProcessPath = video.ProcessPath, Title = "开发文档\n换行,\"引号\"" };
        for (int i = 0; i < 10; i++) AppActivity.Record(day, editor, 0);
        for (int i = 0; i < 3; i++) AppActivity.Record(day, video, 0);
        for (int i = 0; i < 2; i++) AppActivity.Record(day, docs, 1);
        AppActivity.Record(day, docs, 2);
        day.Keys = 20; day.Clicks = 5; day.Wheel = 4; // Includes pre-upgrade, unattributed input.
        Check(day.Apps.Count == 3, "same process different titles remain separate");
        Check(day.Apps[docs.Id].Clicks == 2 && day.Apps[docs.Id].Wheel == 1 && day.Apps[docs.Id].Keys == 0, "wheel and click separate");
        Check(AppActivity.Category(editor) == 0 && AppActivity.Category(video) == 4, "conservative defaults");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\tools\OpenSquilla.exe" }) == 5, "general AI assistant default office");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\Program Files\Kingsoft\wps.exe" }) == 5, "WPS office");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1_x64__test\app\ChatGPT.exe" }) == 0, "Codex package path disambiguates ChatGPT executable");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\Apps\ChatGPT.exe" }) == 5, "ordinary ChatGPT office default");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\Windows\explorer.exe" }) == 3, "file explorer other");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\tools\KeyMouseStats.shortcuts.exe" }) == 3, "versioned self executable other");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\tools\KeyMouseStats.exe" }) == 3, "canonical self executable other");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"C:\tools\never-seen.exe" }) == 4, "unknown application remains unclassified");
        AppUsage office = new AppUsage { ProcessPath = @"C:\tools\wps.exe", Title = "文章" };
        AppActivity.Rules[AppActivity.AppRuleKey(office.ProcessPath)] = 0;
        Check(AppActivity.Category(office) == 0, "manual category overrides expanded defaults");
        AppActivity.Rules.Remove(AppActivity.AppRuleKey(office.ProcessPath));
        DayRecord officeDay = new DayRecord { Keys = 1 };
        AppActivity.Record(officeDay, office, 0);
        long[] officeCategories;
        AppActivity.Aggregate(new DayRecord[] { officeDay }, false, out officeCategories);
        Check(officeCategories.Length == 6 && officeCategories[5] == 1, "new category included in totals");
        AppActivity.LoadRule(AppActivity.Encode(AppActivity.AppRuleKey(office.ProcessPath)) + "|5");
        Check(AppActivity.Rules[AppActivity.AppRuleKey(office.ProcessPath)] == 5, "office category ID loads without changing existing IDs");
        AppActivity.Rules[AppActivity.WindowRuleKey(video)] = 2;
        AppActivity.Rules[AppActivity.WindowRuleKey(docs)] = 0;
        long[] categories;
        List<AppActivity.Row> rows = AppActivity.Aggregate(new DayRecord[] { day }, false, out categories);
        Check(rows.Count == 3, "application aggregation plus legacy row");
        long keys = 0, clicks = 0, wheel = 0;
        bool mixed = false, legacy = false;
        foreach (AppActivity.Row row in rows)
        {
            keys += row.Keys; clicks += row.Clicks; wheel += row.Wheel;
            mixed |= row.ProcessPath == video.ProcessPath && row.Category == -1;
            legacy |= row.Legacy && row.Keys == 7 && row.Clicks == 3 && row.Wheel == 3;
        }
        Check(keys == day.Keys && clicks == day.Clicks && wheel == day.Wheel, "all three totals reconcile including legacy");
        Check(mixed && legacy, "mixed app categories and explicit unattributed counts");
        Check(categories[0] == 10 && categories[2] == 3 && categories[4] == 7, "category key totals reconcile");
        AppActivity.Rules[AppActivity.AppRuleKey(video.ProcessPath)] = 1;
        Check(AppActivity.Category(video) == 2, "window rule overrides application rule");
        AppUsage future = new AppUsage { ProcessPath = video.ProcessPath, Title = "另一个窗口" };
        Check(AppActivity.Category(future) == 1, "application rule applies to new titles");
        AppUsage otherPath = new AppUsage { ProcessPath = @"D:\browser\chrome.exe", Title = video.Title };
        Check(AppActivity.Category(otherPath) == 4, "same exe different paths do not share rules");
        AppUsage casing = new AppUsage { ProcessPath = video.ProcessPath.ToUpperInvariant(), Title = video.Title };
        Check(AppActivity.Category(casing) == 2, "path case normalization");
        rows = AppActivity.Aggregate(new DayRecord[] { day }, true, out categories);
        Check(rows.Count == 4, "window view preserves titles plus legacy");
        DayRecord decoded = new DayRecord();
        AppActivity.LoadRecord(decoded, AppActivity.Serialize(day.Apps[docs.Id]));
        Check(decoded.Apps[docs.Id].Clicks == 2 && decoded.Apps[docs.Id].Title == docs.Title, "unicode delimiters and newline round trip");
        AppActivity.LoadRecord(decoded, "bad|data|1|2|3");
        Check(decoded.Apps.Count == 1, "malformed base64 ignored");
        AppActivity.LoadRecord(decoded, AppActivity.Encode("x") + "|" + AppActivity.Encode("x") + "|-1|0|0");
        Check(decoded.Apps.Count == 1, "negative event count rejected");
        string csv = AppActivity.Export(new DayRecord[] { day });
        Check(csv.Contains("开发文档\n换行,\"\"引号\"\"") && csv.Contains("旧数据 / 未归因"), "CSV escaping and legacy export");
        Check(AppActivity.CsvCell(" =HYPERLINK(\"x\")").StartsWith("\"'"), "formula title neutralized");

        DayRecord full = new DayRecord();
        for (int i = 0; i < 2005; i++) AppActivity.Record(full, new AppUsage { Title = "title-" + i }, 0);
        long count = 0;
        foreach (AppUsage app in full.Apps.Values) count += app.Keys;
        Check(full.Apps.Count == 2001 && count == 2005, "title cap retains event totals");
        AppActivity.Record(full, new AppUsage { Title = "title-0" }, 0);
        Check(full.Apps["\ntitle-0"].Keys == 2, "existing title still counted after cap");

        AppUsage task = new AppUsage { ProcessPath = @"D:\testbrowser\chrome.exe", Title = "论文讨论 - GitHub - Google Chrome" };
        Check(AppActivity.Category(task) == 0 && AppActivity.Explain(task).Source == "站点推测", "browser site title is explicitly a heuristic");
        Check(AppActivity.Category(new AppUsage { ProcessPath = task.ProcessPath, Title = "音乐 - YouTube - Google Chrome" }) == 2, "video site recognized");
        Check(AppActivity.Category(new AppUsage { ProcessPath = task.ProcessPath, Title = "我用 GitHub 看视频" }) == 4, "generic words do not force classification");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"D:\testbrowser\random.exe", Title = "音乐 - YouTube" }) == 4, "site rule limited to browsers");
        string keywordRule = AppActivity.TitleRuleKey(task.ProcessPath, "  论文  ");
        AppActivity.Rules[AppActivity.AppRuleKey(task.ProcessPath)] = 1;
        AppActivity.Rules[keywordRule] = 5;
        Check(AppActivity.Category(task) == 5, "keyword overrides app default and browser heuristic");
        Check(AppActivity.Category(new AppUsage { ProcessPath = task.ProcessPath, Title = "另一篇论文 - Google Chrome" }) == 5, "keyword survives changing window titles");
        Check(AppActivity.Category(new AppUsage { ProcessPath = @"D:\elsewhere\chrome.exe", Title = "另一篇论文" }) == 4, "keyword scoped to process path");
        string longer = AppActivity.TitleRuleKey(task.ProcessPath, "论文讨论");
        AppActivity.Rules[longer] = 0;
        Check(AppActivity.Category(task) == 0, "longest keyword wins");
        AppActivity.Rules[AppActivity.WindowRuleKey(task)] = 2;
        Check(AppActivity.Category(task) == 2, "exact window overrides keyword");
        AppActivity.Rules[AppActivity.AppRuleKey(task.ProcessPath)] = 3;
        Check(AppActivity.Category(task) == 2, "changing app default preserves exact window exception");
        AppActivity.Rules.Remove(AppActivity.WindowRuleKey(task)); AppActivity.Rules.Remove(longer);
        Check(AppActivity.Category(task) == 5 && AppActivity.Explain(task).Reason.Contains("论文"), "removing a specific rule exposes keyword with explanation");
        string english = AppActivity.TitleRuleKey(task.ProcessPath, "Project-X");
        AppActivity.Rules[english] = 0;
        Check(AppActivity.Category(new AppUsage { ProcessPath = task.ProcessPath.ToUpperInvariant(), Title = "PROJECT-x 新窗口" }) == 0, "keyword and app matching case insensitive");
        AppActivity.Rules.Remove(english);
        Check(AppActivity.Export(new DayRecord[] { day }).Contains("分类来源,分类依据"), "export includes classification evidence");

        string directory = Path.Combine(Path.GetTempPath(), "AppActivityTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "stats.txt");
        typeof(Store).GetField("Dir", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, directory);
        typeof(Store).GetField("FilePath", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, file);
        try
        {
            Store.Save(); Reset(); Store.Load();
            Check(Store.Today.Apps.Count == 3 && Store.Today.Apps[editor.Id].Keys == 10, "v4 persistence");
            Check(AppActivity.Category(video) == 2 && AppActivity.Category(future) == 1, "rules persistence");
            Check(AppActivity.Category(task) == 5 && AppActivity.Rules.ContainsKey(keywordRule), "keyword rule persistence uses existing format");
            Check(Store.Today.Keys == 20 && Store.Today.Clicks == 5, "existing totals preserved");
            Store.RollDay(DateTime.Today.AddDays(1));
            AppActivity.Record(Store.Today, editor, 0); Store.Today.Keys++;
            Check(Store.Today.Apps[editor.Id].Keys == 1 && Store.History[DateTime.Today].Apps[editor.Id].Keys == 10, "day boundaries separated");
            Reset();
            File.WriteAllText(file, "total_keys=20\n[day=" + DateTime.Today.ToString("yyyy-MM-dd") + "]\nkeys=20\n");
            Store.Load();
            rows = AppActivity.Aggregate(new DayRecord[] { Store.Today }, false, out categories);
            Check(rows.Count == 1 && rows[0].Legacy && rows[0].Keys == 20, "legacy data not falsely attributed");
        }
        finally
        {
            foreach (string name in new string[] { "stats.txt", "stats.txt.tmp", "stats.txt.bak" })
                if (File.Exists(Path.Combine(directory, name))) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }
        // Read-only native smoke check: never print titles or paths, nor install hooks.
        Check(ForegroundApp.Capture() != null, "native foreground capture returns snapshot or explicit fallback");
        Console.WriteLine("PASS: " + _checks + " app attribution checks; " + (IntPtr.Size * 8) + "-bit");
    }
}
