using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KeyMouseStats;

/// <summary>
/// 字符内容页(第 7 页)的数据层回归。
///
/// 这一层最容易被「看起来对」骗过去:CSV 里只要有一个字段带逗号,用 Split(',') 就会
/// 把整行数字错位;汉字个数那一列如果直接当字符数,遇到代理对(扩展区汉字、emoji)
/// 就会把 1 个字算成 2 个。所以下面几乎每条断言都在钉死「什么算一个字」。
/// </summary>
internal static class RimeLogTests
{
    private static int checks;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("RimeLogTests", Run);
    }

    private static void Run()
    {
        EncodingText();
        CsvSplitting();
        LineParsing();
        Aggregation();
        Slicing();
        DaySeries();
        ExportRoundTrip();
        MissingFile();
        TruncatedAndBadLines();
        RecordJoining();
        RecentWindow();
        CompositionAndRhythm();
        PunctuationExcludedAverage();
        RefreshSetting();
        Console.WriteLine("RimeLogTests: " + checks + " checks passed");
    }

    // ---------------------------------------------------------------- 最近上屏的滚动窗口

    private static void RecentWindow()
    {
        StringBuilder csv = new StringBuilder();
        DateTime start = new DateTime(2026, 10, 28, 8, 0, 0);
        for (int i = 0; i < 50; i++)
            csv.AppendLine(start.AddMinutes(i).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ",1,字" + i.ToString(CultureInfo.InvariantCulture));

        string path = WriteTemp("rime-tests-recent.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            RimeSlice slice = stats.Slice(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));
            Check(slice.Commits == 50, "all fifty commits are aggregated");
            // 回归:曾经保留的是「最旧的 40 条 + 最新 1 条」,界面看起来就是时间线错位。
            Check(slice.Recent.Count == 12, "the recent list is capped at twelve");
            Check(slice.Recent[0].Text == "字49", "the newest commit comes first");
            Check(slice.Recent[11].Text == "字38", "the twelfth newest closes the list");
            for (int i = 1; i < slice.Recent.Count; i++)
                Check(slice.Recent[i - 1].Time >= slice.Recent[i].Time, "the recent list is newest-first all the way down");
        }
        finally { TryDelete(path); }
    }

    // ---------------------------------------------------------------- 构成分类与输入节奏

    private static void CompositionAndRhythm()
    {
        // 分类:顺序是「先 CJK,再标点,最后字母数字」。
        Check(RimeText.Classify('你') == 0, "a Han character is classified as Han");
        Check(RimeText.Classify(0x20BB7) == 0, "an extension B Han character is still Han");
        Check(RimeText.Classify('，') == 1, "a full-width comma is punctuation");
        Check(RimeText.Classify('a') == 2, "a latin letter is letter/digit");
        Check(RimeText.Classify('7') == 2, "a digit is letter/digit");
        Check(RimeText.Classify(' ') == 3, "a space is whitespace");
        Check(RimeText.Classify(0x1F600) == 4, "an emoji falls into the catch-all bucket");
        Check(RimeText.IsCjk('，') == false, "CJK punctuation is not a Han character");

        StringBuilder csv = new StringBuilder();
        csv.AppendLine("2026-10-28 10:00:00,6,\"你好，abc\"");
        csv.AppendLine("2026-10-28 10:00:30,2,世界");
        csv.AppendLine("2026-10-28 10:05:00,2,再见");
        string path = WriteTemp("rime-tests-rhythm.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            RimeSlice slice = stats.Slice(new DateTime(2026, 10, 28), new DateTime(2026, 10, 28));

            Check(slice.Commits == 3, "three commits");
            Check(slice.Chars == 10, "ten declared characters");
            Check(slice.CompositionTotal == 10, "composition covers every code point");
            Check(slice.Composition[0] == 6, "six Han characters");
            Check(slice.Composition[1] == 1, "one punctuation mark");
            Check(slice.Composition[2] == 3, "three latin letters");
            Check(slice.Composition[3] == 0, "no whitespace");
            Check(Math.Abs(slice.CompositionShare(0) - 6.0 / 10.0) < 0.0001, "the Han share is computed from code points");
            // 你 好 ， a b c 世 界 再 见 —— 10 种不同字符,其中汉字 6 种
            Check(slice.DistinctChars == 10, "the distinct character count spans every commit");
            Check(slice.DistinctHan == 6, "punctuation and latin letters are not counted as Han");

            Check(slice.ActiveMinutes == 2, "two minutes have commits");
            Check(Math.Abs(slice.CharsPerActiveMinute - 5.0) < 0.0001, "average characters per active minute");
            Check(Math.Abs(slice.PeakPerMinute - 8) < 0.0001, "the busiest minute holds eight characters");
            // 10:00:00 与 10:00:30 相隔 30 秒算同一段;10:05 相隔 270 秒另起一段。
            Check(slice.LongestBurstMinutes == 1, "the longest burst is one minute long");
            Check(slice.LongestBurstChars == 8, "the longest burst holds eight characters");
        }
        finally { TryDelete(path); }
    }

    // ---------------------------------------------------------------- 平均每次字数:不含标点

    /// <summary>
    /// 「平均每次字数」只数非标点字符:标点不算字,只上屏标点的那一次也不算次数。
    /// 中文输入法里「，」「。」通常单独提交一次,把它们算进次数会把平均值系统性拉低。
    /// </summary>
    private static void PunctuationExcludedAverage()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("2026-10-30 09:00:00,3,你好，");
        csv.AppendLine("2026-10-30 09:00:05,1,，");
        csv.AppendLine("2026-10-30 09:00:10,2,世界");
        csv.AppendLine("2026-10-30 09:00:15,1,。");
        csv.AppendLine("2026-10-31 10:00:00,1,。");
        string path = WriteTemp("rime-tests-punctuation.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            RimeSlice slice = stats.Slice(new DateTime(2026, 10, 30), new DateTime(2026, 10, 30));
            // 原口径不变:纯标点仍然算上屏次数与字数,卡片 1/2 与图表继续用它。
            Check(slice.Commits == 4, "punctuation commits still count as commits");
            Check(slice.Chars == 7, "punctuation still counts toward the declared total");
            Check(Math.Abs(slice.AverageChars - 1.75) < 0.0001, "the old average still includes punctuation");
            // 新口径:4 个非标点字(你好世界)、2 次含文字的上屏,平均 2.00 而不是 1.75。
            Check(slice.TextChars == 4, "only non-punctuation characters reach the new total");
            Check(slice.TextCommits == 2, "a punctuation-only commit is not a text commit");
            Check(Math.Abs(slice.AverageTextChars - 2.0) < 0.0001, "the new average ignores punctuation");
            // 当天只有标点:次数仍记,但没有可平均的东西,返回 0 由调用方显示 "--"。
            RimeSlice onlyPunctuation = stats.Slice(new DateTime(2026, 10, 31), new DateTime(2026, 10, 31));
            Check(onlyPunctuation.Commits == 1 && onlyPunctuation.TextCommits == 0 && onlyPunctuation.TextChars == 0,
                "a punctuation-only day has no text characters");
            Check(onlyPunctuation.AverageTextChars == 0, "a punctuation-only day has no text average");
        }
        finally { TryDelete(path); }
    }

    // ---------------------------------------------------------------- 跨行记录与未加引号的逗号

    private static void RecordJoining()
    {
        Check(RimeTextStats.HasOpenQuote("\"abc"), "an unclosed quote is detected");
        Check(!RimeTextStats.HasOpenQuote("\"abc\""), "a closed quote is not flagged");
        Check(!RimeTextStats.HasOpenQuote("a\"\"b"), "an escaped pair is not an open quote");
        Check(!RimeTextStats.HasOpenQuote("普通一行"), "plain text has no quotes");

        // 明文里有换行时,Rime 会把它包在引号里,于是这条记录跨两行。
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("2026-10-28 10:00:00,3,\"a");
        csv.AppendLine("b\"");
        csv.AppendLine("2026-10-28 10:00:01,1,图");
        string path = WriteTemp("rime-tests-multiline.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            Check(stats.TotalCommits == 2, "a quoted newline keeps one record, not two bad lines");
            Check(stats.BadLines == 0, "the multi-line record is not reported as broken");
            Check(stats.Commits[0].Text == "a\nb", "the embedded newline is preserved");
            Check(stats.Commits[0].Chars == 3, "the declared count still applies");
        }
        finally { TryDelete(path); }

        // 写入方没加引号、明文里又带英文逗号:后面的字段都属于明文,不能丢。
        StringBuilder loose = new StringBuilder();
        loose.AppendLine("2026-10-28 11:00:00,5,a,b,c");
        string loosePath = WriteTemp("rime-tests-loosecomma.csv", loose.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(loosePath);
            Check(stats.TotalCommits == 1, "an unquoted comma still yields one record");
            Check(stats.Commits[0].Text == "a,b,c", "the extra fields are joined back into the text");
            Check(stats.Commits[0].Chars == 5, "the declared count is kept");
        }
        finally { TryDelete(loosePath); }
    }

    // ---------------------------------------------------------------- 刷新间隔设置

    private static void RefreshSetting()
    {
        Check(RimeTextStats.NormalizeRefresh(0) == 0, "zero means manual refresh");
        Check(RimeTextStats.NormalizeRefresh(-5) == 0, "a negative value also means manual");
        Check(RimeTextStats.NormalizeRefresh(15) == 15, "a sane interval is kept as is");
        Check(RimeTextStats.NormalizeRefresh(1) == 2, "an absurdly small interval is clamped up");
        Check(RimeTextStats.NormalizeRefresh(99999) == 3600, "an absurdly large interval is clamped down");
        Check(RimeTextStats.RefreshLabel(0) == "手动", "manual has a readable label");
        Check(RimeTextStats.RefreshLabel(30) == "30 秒", "seconds get a readable label");
        Check(RimeTextStats.RefreshOptions[0] > 0, "the first menu option is an automatic interval");
        Check(RimeTextStats.RefreshOptions[RimeTextStats.RefreshOptions.Length - 1] == 0, "manual is the last option");

        // 落盘往返:这一行是可选行,旧文件没有它时必须保持默认 15 秒。
        Store.History.Clear();
        Store.RollDay(DateTime.Today);
        Store.TextRefreshSeconds = 30;
        string payload = Store.ExportPayload();
        Check(payload != null, "the store can be serialized");
        Check(payload.IndexOf("text_refresh_v1=30", StringComparison.Ordinal) >= 0,
            "the refresh interval is written to the data file");
        Store.TextRefreshSeconds = 15;
    }

    // ---------------------------------------------------------------- 码点

    private static void EncodingText()
    {
        Check(RimeText.CountCodePoints("") == 0, "empty text has no characters");
        Check(RimeText.CountCodePoints(null) == 0, "null text has no characters");
        Check(RimeText.CountCodePoints("你好") == 2, "two BMP Han characters count as two");
        Check(RimeText.CountCodePoints("hello") == 5, "ascii counts per character");

        // 扩展区汉字「𠮷」是代理对:必须算 1 个字,显示时也要还原成一个字符。
        string rare = "\uD842\uDFB7";
        Check(rare.Length == 2, "the surrogate pair really is two UTF-16 units");
        Check(RimeText.CountCodePoints(rare) == 1, "a surrogate pair counts as one character");
        Check(RimeText.Display(0x20BB7) == rare, "code point 0x20BB7 renders back to the same pair");

        List<int> points = RimeText.CodePoints("a" + rare + "b");
        Check(points.Count == 3, "code point enumeration merges the pair");
        Check(points[0] == 'a' && points[1] == 0x20BB7 && points[2] == 'b', "code points keep their order and value");

        List<int> filtered = RimeText.CodePoints("a\r\nb");
        Check(filtered.Count == 2, "line breaks are not counted as typed characters");

        Check(RimeText.DisplayName(' ') == "空格", "a committed space gets a readable name");
        Check(RimeText.DisplayName('你') == "你", "a normal character displays itself");
        Check(RimeText.Display(-1) == "?", "an invalid code point degrades instead of throwing");
    }

    // ---------------------------------------------------------------- CSV

    private static void CsvSplitting()
    {
        List<string> plain = RimeTextStats.SplitCsv("2026-10-28 16:17:23,1,图");
        Check(plain.Count == 3, "three plain fields");
        Check(plain[2] == "图", "the text field survives");

        // 明文里出现英文逗号是这个功能最容易踩的坑。
        List<string> quoted = RimeTextStats.SplitCsv("2026-10-28 16:17:23,5,\"a,b,c\"");
        Check(quoted.Count == 3, "a quoted comma does not split the row");
        Check(quoted[2] == "a,b,c", "quoted commas are kept verbatim");

        List<string> escaped = RimeTextStats.SplitCsv("2026-10-28 16:17:23,3,\"say \"\"hi\"\"\"");
        Check(escaped.Count == 3, "escaped quotes do not split the row");
        Check(escaped[2] == "say \"hi\"", "double quotes unescape to one quote");

        List<string> missing = RimeTextStats.SplitCsv("2026-10-28 16:17:23,1");
        Check(missing.Count == 2, "a short row keeps only what it has");

        List<string> empty = RimeTextStats.SplitCsv("");
        Check(empty.Count == 1 && empty[0] == "", "an empty line is one empty field");
    }

    private static void LineParsing()
    {
        RimeCommit commit = RimeTextStats.ParseLine("2026-10-28 16:17:30,4,数据精度");
        Check(commit != null, "a well formed line parses");
        Check(commit.Chars == 4, "the declared count is trusted when valid");
        Check(commit.Text == "数据精度", "the text is preserved");
        Check(commit.Time == new DateTime(2026, 10, 28, 16, 17, 30), "the timestamp is parsed exactly");

        // 计数列错的时候按真实码点回算,而不是把 0 写进统计。
        RimeCommit fallback = RimeTextStats.ParseLine("2026-10-28 16:17:30,abc,数据精度");
        Check(fallback != null && fallback.Chars == 4, "a broken count falls back to the real length");

        RimeCommit zero = RimeTextStats.ParseLine("2026-10-28 16:17:30,0,你好");
        Check(zero != null && zero.Chars == 2, "a zero count is recomputed, not accepted");

        Check(RimeTextStats.ParseLine("时间点,汉字个数,汉字明文") == null, "the header line is not a commit");
        Check(RimeTextStats.ParseLine("这不是一行日志") == null, "garbage is rejected");
        Check(RimeTextStats.ParseLine("2026-10-28 16:17:30,1") == null, "a row without text is rejected");
        Check(RimeTextStats.ParseLine("") == null, "an empty line is rejected");
    }

    // ---------------------------------------------------------------- 聚合

    private static string WriteTemp(string name, string content)
    {
        string path = Path.Combine(Path.GetTempPath(), name);
        File.WriteAllText(path, content, new UTF8Encoding(true));
        return path;
    }

    private static void Aggregation()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("时间点,汉字个数,汉字明文");
        csv.AppendLine("2026-10-28 16:17:23,1,图");
        csv.AppendLine("2026-10-28 16:17:24,1,的");
        csv.AppendLine("2026-10-28 16:17:30,4,数据精度");
        csv.AppendLine("2026-10-28 16:17:32,3,是不是");
        csv.AppendLine("2026-10-28 18:05:00,4,数据精度");
        csv.AppendLine("2026-10-27 09:00:00,2,你好");

        string path = WriteTemp("rime-tests-aggregate.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            Check(stats.TotalCommits == 6, "six commits parsed, header skipped");
            Check(stats.TotalChars == 15, "character total is the sum of the declared counts");
            Check(stats.BadLines == 0, "no line is reported as bad");
            Check(stats.FirstDay == new DateTime(2026, 10, 27), "the first day is found");
            Check(stats.LastDay == new DateTime(2026, 10, 28), "the last day is found");
            Check(stats.RecordLines == 7, "the footer can report how many records were actually read");
            Check(stats.FileBytes > 0, "the file size is captured for change detection");

            RimeSlice slice = stats.Slice(new DateTime(2026, 10, 28), new DateTime(2026, 10, 28));
            Check(slice.Commits == 5, "today's slice has five commits");
            Check(slice.Chars == 13, "today's slice has thirteen characters");
            Check(slice.ActiveDays == 1, "one active day");
            Check(Math.Abs(slice.AverageChars - 2.6) < 0.0001, "average characters per commit");
            Check(slice.HourChars[16] == 9, "hour 16 holds nine characters");
            Check(slice.HourCommits[16] == 4, "hour 16 holds four commits");
            Check(slice.HourChars[18] == 4, "hour 18 holds four characters");

            // 词长:1 字的两次、3 字一次、4 字两次(「数据精度」重复上屏)
            Check(slice.WordLen[1] == 2, "two single character commits");
            Check(slice.WordLen[3] == 1, "one three character commit");
            Check(slice.WordLen[4] == 2, "the four character commit appears twice");
            Check(slice.WordLen[5] == 0, "no five character commit in this slice");
            Check(slice.WordLen[0] == 0, "bucket zero is never used");

            // 字符频次:「数」「据」各两次
            Check(slice.CharCounts[(int)'数'] == 2, "character frequency merges across commits");
            Check(slice.CharCounts[(int)'图'] == 1, "a character used once");

            // 不同字符:图 的 数 据 精 度 是 不 —— 去重后 8 种,且都是汉字
            Check(slice.DistinctChars == 8, "distinct characters are deduplicated across commits");
            Check(slice.DistinctHan == 8, "every distinct character here is Han");

            List<KeyValuePair<int, long>> top = slice.TopChars(3);
            Check(top.Count == 3, "top chars is limited");
            Check(top[0].Value >= top[1].Value && top[1].Value >= top[2].Value, "top chars is sorted descending");

            // 峰值:一分钟内最多上屏多少字(16:17 那一分钟是 1+1+4+3=9)
            Check(Math.Abs(slice.PeakPerMinute - 9) < 0.0001, "peak per minute is the busiest minute");

            // 时段分布:16 点 9 字 > 18 点 4 字
            Check(slice.HourChars[16] == 9 && slice.HourChars[18] == 4, "hour buckets hold the right totals");
            Check(slice.BusiestHour() == 16, "the busiest hour is the one with the most characters");
            Check(stats.LastCommit == new DateTime(2026, 10, 28, 18, 5, 0), "the newest commit time is recorded");

            // 最近上屏:新的在前
            Check(slice.Recent.Count == 5, "recent list covers the slice");
            Check(slice.Recent[0].Time >= slice.Recent[1].Time, "recent list is newest first");

            RimeSlice all = stats.Slice(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));
            Check(all.Commits == 6, "the full month slice has every commit");
            Check(all.ActiveDays == 2, "two active days in the month");
            Check(Math.Abs(all.AverageCharsPerActiveDay - 7.5) < 0.0001, "per active day average");
        }
        finally { TryDelete(path); }
    }

    private static void Slicing()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("2026-10-26 10:00:00,1,甲");
        csv.AppendLine("2026-10-27 10:00:00,1,乙");
        csv.AppendLine("2026-10-28 10:00:00,1,丙");
        string path = WriteTemp("rime-tests-slice.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            Check(stats.TotalCommits == 3, "no header line here, all three rows parse");

            RimeSlice middle = stats.Slice(new DateTime(2026, 10, 27), new DateTime(2026, 10, 27));
            Check(middle.Commits == 1, "a single day slice keeps one commit");
            Check(middle.CharCounts.ContainsKey((int)'乙'), "the right day is kept");

            RimeSlice empty = stats.Slice(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
            Check(empty.Commits == 0 && !empty.HasData, "an empty range has no data");
            Check(empty.AverageChars == 0, "an empty range does not divide by zero");
            Check(empty.TopChars(5).Count == 0, "an empty range has no top chars");
            Check(empty.Recent.Count == 0, "an empty range has no recent list");
            Check(empty.BusiestHour() == -1, "an empty range has no busiest hour");
        }
        finally { TryDelete(path); }
    }

    private static void DaySeries()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("2026-10-26 10:00:00,3,一二三");
        csv.AppendLine("2026-10-28 10:00:00,2,四五");
        string path = WriteTemp("rime-tests-series.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            List<RimeDay> series = stats.DaySeries(new DateTime(2026, 10, 26), new DateTime(2026, 10, 28));
            Check(series.Count == 3, "the series covers every day in the range");
            Check(series[0].Chars == 3, "the first day has its own total");
            Check(series[1].Chars == 0, "a missing day is filled with zero, not skipped");
            Check(series[2].Chars == 2, "the last day has its own total");
            Check(series[1].Day == new DateTime(2026, 10, 27), "filled days keep their date");

            List<RimeDay> reversed = stats.DaySeries(new DateTime(2026, 10, 28), new DateTime(2026, 10, 26));
            Check(reversed.Count == 0, "a reversed range yields nothing instead of looping");
        }
        finally { TryDelete(path); }
    }

    private static void ExportRoundTrip()
    {
        Check(RimeTextStats.QuoteCsv("普通") == "普通", "a plain cell is not quoted");
        Check(RimeTextStats.QuoteCsv("a,b") == "\"a,b\"", "a cell with a comma is quoted");
        Check(RimeTextStats.QuoteCsv("say \"hi\"") == "\"say \"\"hi\"\"\"", "inner quotes are doubled");
        Check(RimeTextStats.QuoteCsv(null) == "", "null exports as empty");

        StringBuilder csv = new StringBuilder();
        csv.AppendLine("2026-10-28 16:17:23,3,\"a,b\"");
        string path = WriteTemp("rime-tests-export.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            string exported = RimeTextStats.Export(stats, new DateTime(2026, 10, 28), new DateTime(2026, 10, 28));
            string[] lines = exported.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Check(lines.Length == 2, "export writes a header plus one row");
            Check(lines[0] == "时间点,字符数,明文", "export header matches the source format");

            // 导出再解析回去必须得到同一个词 —— 这是导出能被重新导入的前提。
            RimeCommit again = RimeTextStats.ParseLine(lines[1].TrimStart('\uFEFF'));
            Check(again != null && again.Text == "a,b", "exported row survives a round trip");
            Check(again.Chars == 3, "exported character count survives");
        }
        finally { TryDelete(path); }
    }

    private static void MissingFile()
    {
        RimeTextStats stats = RimeTextStats.Load(Path.Combine(Path.GetTempPath(), "rime-tests-does-not-exist.csv"));
        Check(stats.Missing, "a missing file is flagged, not thrown");
        Check(!stats.HasData, "a missing file has no data");
        Check(stats.Error.Length == 0, "a missing file is not an error");

        RimeTextStats blank = RimeTextStats.Load("");
        Check(blank.Missing, "an empty path is treated as missing");

        RimeSlice slice = stats.Slice(DateTime.Today, DateTime.Today);
        Check(slice.Commits == 0, "slicing an empty stats object is safe");
    }

    private static void TruncatedAndBadLines()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("时间点,汉字个数,汉字明文");
        csv.AppendLine("2026-10-28 16:17:23,1,图");
        csv.AppendLine("这不是日志");
        csv.AppendLine("2026-10-28 25:99:99,1,坏时间");
        csv.AppendLine("2026-10-28 16:17:24,1,的");

        string path = WriteTemp("rime-tests-bad.csv", csv.ToString());
        try
        {
            RimeTextStats stats = RimeTextStats.Load(path);
            Check(stats.TotalCommits == 2, "valid rows survive bad neighbours");
            Check(stats.BadLines == 2, "unparseable rows are counted, not silently dropped");
            Check(stats.HeaderLines == 1, "the header is recognised as a header");
            Check(stats.TotalChars == 2, "bad rows do not pollute the totals");
        }
        finally { TryDelete(path); }

        // 原始文件为空:不算错,只是没数据。
        string emptyPath = WriteTemp("rime-tests-empty.csv", "");
        try
        {
            RimeTextStats empty = RimeTextStats.Load(emptyPath);
            Check(!empty.Missing, "an existing empty file is not missing");
            Check(!empty.HasData, "an existing empty file has no data");
            Check(empty.BadLines == 0, "an empty file reports no bad lines");
        }
        finally { TryDelete(emptyPath); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
