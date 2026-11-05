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

        Console.WriteLine("Rime phrases: " + checks + " checks passed");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("RimePhraseTests", Run);
    }
}
