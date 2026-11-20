using System;
using System.Collections.Generic;
using KeyMouseStats;

/// <summary>高频词组:连续汉字的 2–4 字滑窗,标点/字母处断开。</summary>
internal static class RimePhraseTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Run()
    {
        RimeSlice slice = new RimeSlice();
        RimeSlice.AddPhrases("键盘", slice);
        Check(slice.PhraseCounts.ContainsKey("键盘") && slice.PhraseCounts["键盘"] == 1, "a two-character commit becomes one phrase");

        RimeSlice.AddPhrases("使用洞察界面", slice);
        Check(slice.PhraseCounts.ContainsKey("洞察") && slice.PhraseCounts["洞察"] == 1, "bigram inside a longer commit");
        Check(slice.PhraseCounts.ContainsKey("界面") && slice.PhraseCounts["界面"] == 1, "the tail bigram is kept");
        Check(slice.PhraseCounts.ContainsKey("使用洞察") && slice.PhraseCounts["使用洞察"] == 1, "four-character windows are kept");
        Check(!slice.PhraseCounts.ContainsKey("盘界"), "windows do not jump across a gap");

        RimeSlice.AddPhrases("键盘，灯", slice);
        Check(slice.PhraseCounts.ContainsKey("键盘") && slice.PhraseCounts["键盘"] == 2, "the same phrase aggregates");
        Check(!slice.PhraseCounts.ContainsKey("盘灯"), "punctuation breaks the phrase run");

        RimeSlice.AddPhrases("abc键盘", slice);
        Check(slice.PhraseCounts["键盘"] == 3, "letters break the run but do not swallow the Han part");

        RimeSlice.AddPhrases(null, slice);
        RimeSlice.AddPhrases("", slice);
        Check(slice.PhraseCounts["键盘"] == 3, "null and empty text are safe");

        RimeSlice.AddPhrases("好的好的", slice);
        List<KeyValuePair<string, long>> top = slice.TopPhrases(2);
        Check(top.Count == 2 && top[0].Key == "键盘" && top[1].Key == "好的", "TopPhrases sorts by count and respects the limit");
        Check(slice.TopPhrases(0).Count >= 6, "no limit returns every phrase");

        // 输出应用特征词:词组优先、虚词过滤、n-gram 去重、实词单字兜底。
        RimeAppSummary app = new RimeAppSummary();
        app.PhraseCounts["审计报告"] = 5;
        app.PhraseCounts["审计"] = 4;       // 被「审计报告」包含,不应同时出现
        app.PhraseCounts["性能优化"] = 3;
        app.PhraseCounts["组合键"] = 2;
        app.PhraseCounts["今天"] = 9;       // 全虚词,必须被过滤
        app.CharCounts['键'] = 20;
        app.CharCounts['鼠'] = 18;
        app.CharCounts['审'] = 16;
        app.CharCounts['数'] = 14;
        app.CharCounts['据'] = 12;
        app.CharCounts['的'] = 30;          // 虚字,不能作为单字兜底
        app.CharCounts['，'] = 12;          // 标点,不能作为单字兜底
        List<KeyValuePair<string, long>> words = app.FeatureWords(5);
        Check(words.Count == 5, "feature words fill the requested slots");
        Check(words[0].Key == "审计报告" && words[0].Value == 5, "the strongest phrase comes first");
        bool hasOverlap = false, hasFunction = false, hasPunct = false, hasSingleFallback = false;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].Key == "审计") hasOverlap = true;
            if (words[i].Key == "今天") hasFunction = true;
            if (words[i].Key == "的" || words[i].Key == "，") hasPunct = true;
            if (words[i].Key.Length == 1) hasSingleFallback = true;
        }
        Check(!hasOverlap, "overlapping n-grams are de-duplicated");
        Check(!hasFunction, "function phrases never become feature words");
        Check(!hasPunct, "function chars and punctuation never fall back into chips");
        Check(hasSingleFallback, "single content chars fill the remaining slots");
        List<KeyValuePair<string, long>> content = app.ContentChars(5);
        bool contentHasFunction = false;
        for (int i = 0; i < content.Count; i++) if (content[i].Key == "的") contentHasFunction = true;
        Check(!contentHasFunction && content.Count > 0, "single-char profiles exclude function chars");

        Console.WriteLine("Rime phrases: " + checks + " checks passed");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("RimePhraseTests", Run);
    }
}
