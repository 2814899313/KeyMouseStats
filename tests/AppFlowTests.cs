using System;
using System.Collections.Generic;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 应用 × APM / 时长占比的纯计算。口径错一位,用户看到的就是「VS Code 比游戏还费手」。
/// </summary>
internal static class AppFlowTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("AppFlowTests", Run);
    }

    private static AppActivity.Row Row(string name, long keys, long clicks, double seconds, int category, bool legacy)
    {
        return new AppActivity.Row
        {
            Id = name, Name = name, Detail = name + ".exe", ProcessPath = name + ".exe",
            Keys = keys, Clicks = clicks, ActiveSeconds = seconds, Category = category,
            Source = "自动推测", Reason = "测试", Legacy = legacy
        };
    }

    private static void Run()
    {
        AppActivity.Row editor = Row("VS Code", 600, 200, 600, 0, false);
        AppActivity.Row game = Row("Steam", 300, 300, 120, 1, false);
        AppActivity.Row browser = Row("Chrome", 40, 20, 30, 4, false);
        AppActivity.Row legacy = Row("旧数据", 9999, 9999, 0, 4, true);

        // ---- 1. 单应用 APM:每分钟击键 + 点击 ----
        Check(Math.Abs(AppFlow.ApmOf(editor) - 80) < 1e-9, "APM uses keys plus clicks per minute");
        Check(Math.Abs(AppFlow.ApmOf(game) - 300) < 1e-9, "a short gaming session has a high APM");
        Check(AppFlow.ApmOf(Row("no-time", 10, 10, 0, 0, false)) == 0, "no active seconds means no APM");
        Check(AppFlow.ApmOf(null) == 0, "null row is safe");

        List<AppFlow.Item> items = AppFlow.Build(new AppActivity.Row[] { editor, game, browser, legacy, null });
        Check(items.Count == 4, "build skips null rows and keeps every real app");
        Check(items[0].Inputs == 800 && Math.Abs(items[0].Apm - 80) < 1e-9, "item keeps inputs and APM");

        // ---- 2. 全局 APM:按输入总量 ÷ 总活跃时长,不是各应用 APM 的平均 ----
        AppFlow.Totals totals = AppFlow.Sum(items);
        Check(totals.Apps == 3, "legacy rows without time do not join the totals");
        Check(totals.Inputs == 1460 && Math.Abs(totals.Seconds - 750) < 1e-9, "totals sum inputs and seconds");
        Check(Math.Abs(totals.Apm - 1460 * 60.0 / 750) < 1e-9, "global APM is a weighted rate, not an average of rates");

        // ---- 3. APM 排名:活跃不足 60 秒不参与,同分按输入量稳定排序 ----
        int excluded;
        List<AppFlow.Item> ranked = AppFlow.RankByApm(items, AppFlow.MinApmSeconds, out excluded);
        Check(excluded == 1, "the short browser session is excluded from ranking");
        Check(ranked.Count == 2 && ranked[0].Name == "Steam" && ranked[1].Name == "VS Code", "ranking is by APM, not by total input");
        AppFlow.Item tieA = new AppFlow.Item { Name = "A", Keys = 100, Seconds = 120 };
        AppFlow.Item tieB = new AppFlow.Item { Name = "B", Keys = 200, Seconds = 240 };
        int ignored;
        List<AppFlow.Item> ties = AppFlow.RankByApm(new AppFlow.Item[] { tieA, tieB }, 60, out ignored);
        Check(ties.Count == 2 && ties[0].Name == "B", "equal APM falls back to input volume");
        Check(AppFlow.RankByApm(null, 60, out ignored).Count == 0 && ignored == 0, "null input is safe");
        Check(AppFlow.RankByApm(items, 1, out ignored).Count == 3, "a lower threshold admits short sessions");

        // ---- 4. 时长占比:前 N 个 + 其他,份额合计为 1 ----
        double totalSeconds;
        int merged;
        List<AppFlow.Slice> slices = AppFlow.Slices(items, 2, out totalSeconds, out merged);
        Check(Math.Abs(totalSeconds - 750) < 1e-9, "share total excludes legacy and zero-time rows");
        Check(slices.Count == 3 && merged == 1, "top two apps plus one merged slice");
        Check(slices[0].Name == "VS Code" && slices[1].Name == "Steam" && slices[2].Other, "slices are sorted by time, other last");
        double shareSum = 0;
        foreach (AppFlow.Slice slice in slices) shareSum += slice.Share;
        Check(Math.Abs(shareSum - 1) < 1e-9, "shares add up to 100%");
        Check(Math.Abs(slices[0].Share - 600.0 / 750) < 1e-9, "the biggest slice is 80% of the observed time");
        Check(Math.Abs(slices[2].Seconds - 30) < 1e-9 && slices[2].Inputs == 60, "the merged slice keeps seconds and inputs");
        Check(Math.Abs(slices[2].Apm - 120) < 1e-9, "the merged slice still has a meaningful APM");
        Check(AppFlow.Slices(new AppFlow.Item[0], 2, out totalSeconds, out merged).Count == 0,
            "no observations make an empty share chart");
        Check(AppFlow.Slices(items, 10, out totalSeconds, out merged).Count == 3 && merged == 0,
            "a top larger than the list does not invent an other slice");

        // ---- 5. 显示格式 ----
        Check(AppFlow.ApmText(0) == "--" && AppFlow.ApmText(9.25) == "9.3" && AppFlow.ApmText(88.6) == "89",
            "APM text keeps a decimal only for small values");

        // ---- 6. 应用 CSV 也带上 APM 列 ----
        DayRecord day = new DayRecord();
        day.AppObservedSeconds = 600;
        day.Apps[editor.Id] = new AppUsage { ProcessPath = editor.ProcessPath, Title = "main.cs", Keys = 600, Clicks = 200, ActiveSeconds = 600 };
        string csv = AppActivity.Export(new DayRecord[] { day });
        Check(csv.IndexOf("每分钟输入(APM)") >= 0, "app CSV has an APM column");
        Check(csv.IndexOf(",80,自动推测") >= 0 || csv.IndexOf(",80,") >= 0, "app CSV writes the computed APM");
        Check(csv.IndexOf("分类来源,分类依据") >= 0, "existing evidence columns stay adjacent");

        Console.WriteLine("App APM / time share: " + checks + " checks passed");
    }
}
