using System.Globalization;
using System.Text;
using Kms.Core.Text;

namespace Kms.Core.Tests;

/// <summary>
/// Rime 上屏日志（commit_log.csv）数据层回归。
///
/// 这一层最容易被「看起来对」骗过去：CSV 里只要有一个字段带逗号，用 Split(',')
/// 就会把整行数字错位；汉字个数那一列如果直接当字符数，遇到代理对（扩展区汉字、
/// emoji）就会把 1 个字算成 2 个。下面几乎每条断言都在钉死「什么算一个字」。
/// </summary>
public sealed class RimeLogTests
{
    // ---------------------------------------------------------------- 码点与分类

    [Fact]
    public void CodePoints_CountSurrogatePairsAndSkipLineBreaks()
    {
        Assert.Equal(0, RimeText.CountCodePoints(""));
        Assert.Equal(0, RimeText.CountCodePoints(null));
        Assert.Equal(2, RimeText.CountCodePoints("你好"));
        Assert.Equal(5, RimeText.CountCodePoints("hello"));

        const string rare = "\uD842\uDFB7"; // 𠮷：扩展 B 区汉字，UTF-16 代理对
        Assert.Equal(2, rare.Length);
        Assert.Equal(1, RimeText.CountCodePoints(rare));
        Assert.Equal(rare, RimeText.Display(0x20BB7));

        List<int> points = RimeText.CodePoints("a" + rare + "b");
        Assert.Equal(3, points.Count);
        Assert.Equal((int)'a', points[0]);
        Assert.Equal(0x20BB7, points[1]);
        Assert.Equal((int)'b', points[2]);

        Assert.Equal(2, RimeText.CodePoints("a\r\nb").Count);
        Assert.Equal(2, RimeText.CodePoints("a\rb").Count);
        Assert.Equal(2, RimeText.CodePoints("a\nb").Count);
        Assert.Equal(2, RimeText.CountCodePoints("a\r\nb"));
    }

    [Fact]
    public void TextHelpers_ClassifyAndDisplayNames()
    {
        Assert.Equal("空格", RimeText.DisplayName(' '));
        Assert.Equal("Tab", RimeText.DisplayName('\t'));
        Assert.Equal("控制符", RimeText.DisplayName(0x01));
        Assert.Equal("你", RimeText.DisplayName('你'));
        Assert.Equal("?", RimeText.Display(-1));
        Assert.Equal("?", RimeText.Display(0x110000));

        Assert.Equal(0, RimeText.Classify('你'));
        Assert.Equal(0, RimeText.Classify(0x20BB7));
        Assert.Equal(1, RimeText.Classify('，'));
        Assert.Equal(2, RimeText.Classify('a'));
        Assert.Equal(2, RimeText.Classify('7'));
        Assert.Equal(3, RimeText.Classify(' '));
        Assert.Equal(3, RimeText.Classify('\t'));
        Assert.Equal(3, RimeText.Classify(0x3000));
        Assert.Equal(4, RimeText.Classify(0x1F600));

        Assert.False(RimeText.IsCjk('，'));
        Assert.True(RimeText.IsCjk('你'));
        Assert.True(RimeText.IsCjk(0x20BB7));
        Assert.True(RimeText.IsCjk(0x3400));
        Assert.True(RimeText.IsCjk(0xF900));
        Assert.True(RimeText.IsCjk(0x2FA1F));
    }

    // ---------------------------------------------------------------- CSV 拆分

    [Fact]
    public void SplitCsv_HandlesQuotesCommasEscapesAndShortRows()
    {
        List<string> plain = RimeLog.SplitCsv("2026-10-28 16:17:23,1,图");
        Assert.Equal(3, plain.Count);
        Assert.Equal("图", plain[2]);

        // 明文里出现英文逗号是这个功能最容易踩的坑。
        List<string> quoted = RimeLog.SplitCsv("2026-10-28 16:17:23,5,\"a,b,c\"");
        Assert.Equal(3, quoted.Count);
        Assert.Equal("a,b,c", quoted[2]);

        List<string> escaped = RimeLog.SplitCsv("2026-10-28 16:17:23,3,\"say \"\"hi\"\"\"");
        Assert.Equal(3, escaped.Count);
        Assert.Equal("say \"hi\"", escaped[2]);

        List<string> shortRow = RimeLog.SplitCsv("2026-10-28 16:17:23,1");
        Assert.Equal(2, shortRow.Count);
        Assert.Equal("1", shortRow[1]);

        List<string> empty = RimeLog.SplitCsv("");
        Assert.Single(empty);
        Assert.Equal("", empty[0]);

        Assert.Empty(RimeLog.SplitCsv(null));

        List<string> quotedNewline = RimeLog.SplitCsv("a,\"b\nc\"");
        Assert.Equal(2, quotedNewline.Count);
        Assert.Equal("b\nc", quotedNewline[1]);
    }

    // ---------------------------------------------------------------- 单行解析

    [Fact]
    public void ParseLine_ParsesLegacyThreeColumnRows()
    {
        RimeCommit? commit = RimeLog.ParseLine("2026-10-28 16:17:30,4,数据精度");
        Assert.NotNull(commit);
        Assert.Equal(4, commit!.Chars);
        Assert.Equal("数据精度", commit.Text);
        Assert.Equal(new DateTime(2026, 10, 28, 16, 17, 30), commit.Time);
        Assert.Equal(4, commit.TotalLength);
        Assert.Equal(4, commit.HanziCount);
        Assert.Equal("normal", commit.CommitType);
        Assert.False(commit.FormatComplete);
        Assert.False(commit.CandidateKnown);
        Assert.Equal(-1, commit.CandidateIndex);
        Assert.Equal(-1, commit.CodeLength);

        RimeCommit? punctuation = RimeLog.ParseLine("2026-11-13 00:14:16,1,，");
        Assert.NotNull(punctuation);
        Assert.Equal("punct", punctuation!.CommitType);
        Assert.Equal(0, punctuation.HanziCount);

        RimeCommit? raw = RimeLog.ParseLine("2026-11-13 00:20:00,11,hello world");
        Assert.NotNull(raw);
        Assert.Equal("raw_ascii", raw!.CommitType);
        Assert.Equal(11, raw.TotalLength);

        RimeCommit? mixed = RimeLog.ParseLine("2026-10-28 10:00:00,6,\"你好，abc\"");
        Assert.NotNull(mixed);
        Assert.Equal("mixed", mixed!.CommitType);
        Assert.Equal(6, mixed.TotalLength);
        Assert.Equal(2, mixed.HanziCount);
    }

    [Fact]
    public void ParseLine_ReadsStrictSevenColumnMetadata()
    {
        RimeCommit? rich = RimeLog.ParseLine(
            "2026-11-13 00:10:45,5,你现在看看,msedge.exe,rime_ice,15,");
        Assert.NotNull(rich);
        Assert.Equal("你现在看看", rich!.Text);
        Assert.Equal("msedge.exe", rich.Application);
        Assert.Equal("rime_ice", rich.Scheme);
        Assert.Equal(15, rich.CodeLength);
        Assert.False(rich.CandidateKnown);
        Assert.Equal(-1, rich.CandidateIndex);
        Assert.False(rich.FormatComplete);

        RimeCommit? complete = RimeLog.ParseLine(
            "2026-11-13 00:10:46,3,新记录,code.exe,rime_ice,8,0");
        Assert.NotNull(complete);
        Assert.Equal("新记录", complete!.Text);
        Assert.True(complete.FormatComplete);
        Assert.True(complete.CandidateKnown);
        Assert.Equal(0, complete.CandidateIndex);
        Assert.Equal("code.exe", complete.Application);
        Assert.Equal("rime_ice", complete.Scheme);
        Assert.Equal(8, complete.CodeLength);

        RimeCommit? paged = RimeLog.ParseLine(
            "2026-11-13 00:10:47,3,翻页,code.exe,rime_ice,8,2");
        Assert.NotNull(paged);
        Assert.Equal(2, paged!.CandidateIndex);
        Assert.True(paged.FormatComplete);

        // 明文没加引号、自己带英文逗号时，后面的字段仍属于明文。
        RimeCommit? unquotedComma = RimeLog.ParseLine(
            "2026-11-13 00:10:48,5,a,b,c,code.exe,rime_ice,8,0");
        Assert.NotNull(unquotedComma);
        Assert.Equal("a,b,c", unquotedComma!.Text);
        Assert.Equal("code.exe", unquotedComma.Application);
        Assert.True(unquotedComma.FormatComplete);

        // 三列明文后面跟的空元数据列不能拼成「我想的,,,」。
        RimeCommit? emptyMeta = RimeLog.ParseLine("2026-11-12 23:58:35,3,我想的,,,,");
        Assert.NotNull(emptyMeta);
        Assert.Equal("我想的", emptyMeta!.Text);
        Assert.Equal(3, emptyMeta.TotalLength);
        Assert.False(emptyMeta.FormatComplete);
    }

    [Fact]
    public void ParseLine_ReadsSixAndTenColumnFormats()
    {
        // 六列：没有候选列，能被解析但不算完整七列。
        RimeCommit? six = RimeLog.ParseLine(
            "2026-11-13 00:10:45,5,你现在看看,msedge.exe,rime_ice,15");
        Assert.NotNull(six);
        Assert.Equal("你现在看看", six!.Text);
        Assert.Equal("msedge.exe", six.Application);
        Assert.Equal("rime_ice", six.Scheme);
        Assert.Equal(15, six.CodeLength);
        Assert.False(six.CandidateKnown);
        Assert.False(six.FormatComplete);

        // 十列导出格式：额外的总字符数/汉字数/输入类型列不进入明文。
        RimeCommit? ten = RimeLog.ParseLine(
            "2026-11-13 00:10:45,5,\"你,好\",msedge.exe,rime_ice,15,0,3,2,normal");
        Assert.NotNull(ten);
        Assert.Equal("你,好", ten!.Text);
        Assert.Equal("msedge.exe", ten.Application);
        Assert.Equal("rime_ice", ten.Scheme);
        Assert.Equal(15, ten.CodeLength);
        Assert.True(ten.CandidateKnown);
        Assert.Equal(0, ten.CandidateIndex);
        Assert.True(ten.FormatComplete);
        // 派生列只是写入方的快照，读取时仍按真实码点重算（你、逗号、好 = 3 个码点）。
        Assert.Equal(3, ten.TotalLength);
        Assert.Equal(2, ten.HanziCount);
        Assert.Equal("normal", ten.CommitType);

        // 导出格式同样允许未加引号的逗号。
        RimeCommit? tenLoose = RimeLog.ParseLine(
            "2026-11-13 00:10:45,5,你,好abc,msedge.exe,rime_ice,15,1,5,4,mixed");
        Assert.NotNull(tenLoose);
        Assert.Equal("你,好abc", tenLoose!.Text);
        Assert.Equal(1, tenLoose.CandidateIndex);
        Assert.Equal("mixed", tenLoose.CommitType);
        Assert.True(tenLoose.FormatComplete);
    }

    [Fact]
    public void ParseLine_RejectsShortHeaderGarbageAndBadTimeRows()
    {
        Assert.Null(RimeLog.ParseLine("时间点,汉字个数,汉字明文"));
        Assert.Null(RimeLog.ParseLine("这不是一行日志"));
        Assert.Null(RimeLog.ParseLine("2026-10-28 16:17:30,1"));
        Assert.Null(RimeLog.ParseLine(""));
        Assert.Null(RimeLog.ParseLine(null));
        Assert.Null(RimeLog.ParseLine("2026-10-28 25:99:99,1,坏时间"));
    }

    [Fact]
    public void ParseLine_RecalculatesInvalidCharacterCounts()
    {
        RimeCommit? broken = RimeLog.ParseLine("2026-10-28 16:17:30,abc,数据精度");
        Assert.NotNull(broken);
        Assert.Equal(4, broken!.Chars);
        Assert.Equal(4, broken.TotalLength);

        RimeCommit? zero = RimeLog.ParseLine("2026-10-28 16:17:30,0,你好");
        Assert.NotNull(zero);
        Assert.Equal(2, zero!.Chars);

        RimeCommit? negative = RimeLog.ParseLine("2026-10-28 16:17:30,-3,你好");
        Assert.NotNull(negative);
        Assert.Equal(2, negative!.Chars);

        RimeCommit? huge = RimeLog.ParseLine("2026-10-28 16:17:30,100001,你好");
        Assert.NotNull(huge);
        Assert.Equal(2, huge!.Chars);

        // 合法的写入方声明仍然优先：声明 1 就按 1 算，不拿真实长度覆盖。
        RimeCommit? trusted = RimeLog.ParseLine("2026-10-28 16:17:30,1,数据精度");
        Assert.NotNull(trusted);
        Assert.Equal(1, trusted!.Chars);
        Assert.Equal(4, trusted.TotalLength);
    }

    // ---------------------------------------------------------------- 文件解析与聚合

    [Fact]
    public void Load_AggregatesDeclaredCountsHoursAndRecentWindow()
    {
        string csv = string.Join("\n", new[]
        {
            "时间点,汉字个数,汉字明文",
            "2026-10-28 16:17:23,1,图",
            "2026-10-28 16:17:24,1,的",
            "2026-10-28 16:17:30,4,数据精度",
            "2026-10-28 16:17:32,3,是不是",
            "2026-10-28 18:05:00,4,数据精度",
            "2026-10-27 09:00:00,2,你好"
        }) + "\n";

        string folder = NewTempFolder();
        try
        {
            string path = WriteFile(folder, "aggregate.csv", csv);
            RimeLogStats stats = RimeLog.Load(path);

            Assert.False(stats.Missing);
            Assert.Equal(path, stats.Source);
            Assert.Equal("", stats.Error);
            Assert.False(stats.Truncated);
            Assert.Equal(6L, stats.TotalCommits);
            Assert.Equal(15L, stats.TotalChars);
            Assert.Equal(0L, stats.BadLines);
            Assert.Equal(1L, stats.HeaderLines);
            Assert.Equal(7L, stats.RecordLines);
            Assert.Equal(new DateTime(2026, 10, 27), stats.FirstDay);
            Assert.Equal(new DateTime(2026, 10, 28), stats.LastDay);
            Assert.Equal(new DateTime(2026, 10, 28, 18, 5, 0), stats.LastCommit);
            Assert.True(stats.FileBytes > 0);
            Assert.Equal(13L, stats.DayChars[new DateTime(2026, 10, 28)]);
            Assert.Equal(5L, stats.DayCommits[new DateTime(2026, 10, 28)]);

            RimeSlice slice = stats.Slice(new DateTime(2026, 10, 28), new DateTime(2026, 10, 28));
            Assert.Equal(5L, slice.Commits);
            Assert.Equal(13L, slice.Chars);
            Assert.Equal(1, slice.ActiveDays);
            Assert.Equal(2, slice.ActiveMinutes);
            Assert.Equal(2.6, slice.AverageChars, 9);
            Assert.Equal(6.5, slice.CharsPerActiveMinute, 9);
            Assert.Equal(9L, slice.HourChars[16]);
            Assert.Equal(4L, slice.HourCommits[16]);
            Assert.Equal(4L, slice.HourChars[18]);
            Assert.Equal(1L, slice.HourCommits[18]);
            Assert.Equal(16, slice.BusiestHour());

            // 词长：1 字两次、3 字一次、4 字两次（「数据精度」重复上屏）。
            Assert.Equal(2L, slice.WordLen[1]);
            Assert.Equal(1L, slice.WordLen[3]);
            Assert.Equal(2L, slice.WordLen[4]);
            Assert.Equal(0L, slice.WordLen[5]);
            Assert.Equal(0L, slice.WordLen[0]);

            Assert.Equal(2L, slice.CharCounts[(int)'数']);
            Assert.Equal(1L, slice.CharCounts[(int)'图']);
            Assert.Equal(8, slice.DistinctChars);
            Assert.Equal(8, slice.DistinctHan);

            List<KeyValuePair<int, long>> top = slice.TopChars(3);
            Assert.Equal(3, top.Count);
            Assert.True(top[0].Value >= top[1].Value);
            Assert.True(top[1].Value >= top[2].Value);

            // 16:17 这一分钟是 1+1+4+3=9 字。
            Assert.Equal(9.0, slice.PeakPerMinute, 9);
            Assert.Equal(5, slice.Recent.Count);
            Assert.True(slice.Recent[0].Time >= slice.Recent[1].Time);

            RimeSlice all = stats.Slice(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));
            Assert.Equal(6L, all.Commits);
            Assert.Equal(2, all.ActiveDays);
            Assert.Equal(7.5, all.AverageCharsPerActiveDay, 9);
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    [Fact]
    public void Slice_CompositionAndPunctuationExcludedAverageMatchV3()
    {
        string csv = string.Join("\n", new[]
        {
            "2026-10-28 10:00:00,6,\"你好，abc\"",
            "2026-10-28 10:00:30,2,世界",
            "2026-10-28 10:05:00,2,再见"
        }) + "\n";

        RimeLogStats stats = RimeLog.ParseCsv(csv);
        RimeSlice slice = stats.Slice(new DateTime(2026, 10, 28), new DateTime(2026, 10, 28));

        Assert.Equal(3L, slice.Commits);
        Assert.Equal(10L, slice.Chars);
        Assert.Equal(10L, slice.CompositionTotal);
        Assert.Equal(6L, slice.Composition[0]);
        Assert.Equal(1L, slice.Composition[1]);
        Assert.Equal(3L, slice.Composition[2]);
        Assert.Equal(0L, slice.Composition[3]);
        Assert.Equal(0.6, slice.CompositionShare(0), 9);

        // 你 好 ， a b c 世 界 再 见 —— 10 种不同字符，其中汉字 6 种。
        Assert.Equal(10, slice.DistinctChars);
        Assert.Equal(6, slice.DistinctHan);

        Assert.Equal(2, slice.ActiveMinutes);
        Assert.Equal(5.0, slice.CharsPerActiveMinute, 9);
        Assert.Equal(8.0, slice.PeakPerMinute, 9);
        // 10:00:00 与 10:00:30 相隔 30 秒算同一段；10:05 相隔 270 秒另起一段。
        Assert.Equal(1, slice.LongestBurstMinutes);
        Assert.Equal(8L, slice.LongestBurstChars);

        // 原口径：纯标点仍算上屏次数与字数。
        string punctuationCsv = string.Join("\n", new[]
        {
            "2026-10-30 09:00:00,3,你好，",
            "2026-10-30 09:00:05,1,，",
            "2026-10-30 09:00:10,2,世界",
            "2026-10-30 09:00:15,1,。",
            "2026-10-31 10:00:00,1,。"
        }) + "\n";
        RimeLogStats punctuationStats = RimeLog.ParseCsv(punctuationCsv);
        RimeSlice day = punctuationStats.Slice(new DateTime(2026, 10, 30), new DateTime(2026, 10, 30));
        Assert.Equal(4L, day.Commits);
        Assert.Equal(7L, day.Chars);
        Assert.Equal(1.75, day.AverageChars, 9);

        // 新口径：4 个非标点字（你好世界）、2 次含文字的上屏，平均 2.00。
        Assert.Equal(4L, day.TextChars);
        Assert.Equal(2L, day.TextCommits);
        Assert.Equal(2.0, day.AverageTextChars, 9);

        RimeSlice onlyPunctuation = punctuationStats.Slice(
            new DateTime(2026, 10, 31), new DateTime(2026, 10, 31));
        Assert.Equal(1L, onlyPunctuation.Commits);
        Assert.Equal(0L, onlyPunctuation.TextChars);
        Assert.Equal(0L, onlyPunctuation.TextCommits);
        Assert.Equal(0.0, onlyPunctuation.AverageTextChars, 9);
    }

    [Fact]
    public void Slice_CandidateAndCodeEfficiencyUseOnlyCompleteSamples()
    {
        string csv = string.Join("\n", new[]
        {
            "2026-11-13 10:00:00,5,你好,code.exe,rime_ice,10,0",
            "2026-11-13 10:00:05,5,世界,code.exe,rime_ice,8,1",
            "2026-11-13 10:00:10,5,再见,code.exe,rime_ice,12,"
        }) + "\n";

        RimeSlice slice = RimeLog.ParseCsv(csv).Slice(
            new DateTime(2026, 11, 13), new DateTime(2026, 11, 13));

        Assert.Equal(3L, slice.Commits);
        Assert.Equal(2L, slice.CandidateKnown);
        Assert.Equal(1L, slice.CandidateFirst);
        Assert.Equal(1L, slice.CandidatePaged);
        Assert.Equal(2.0 / 3.0, slice.CandidateCoverage, 9);
        Assert.Equal(0.5, slice.FirstChoiceRate, 9);
        Assert.Equal(0.5, slice.PagedCandidateRate, 9);

        // 三条都是拼音上屏：码长 10+8+12=30，汉字 2×3=6，平均 5.0 键/字。
        Assert.Equal(3L, slice.CodeSamples);
        Assert.Equal(30L, slice.CodeLengthTotal);
        Assert.Equal(6L, slice.CodeHanziTotal);
        Assert.Equal(5.0, slice.AverageCodeLength, 9);

        RimeSlice empty = new RimeSlice();
        Assert.True(double.IsNaN(empty.FirstChoiceRate));
        Assert.True(double.IsNaN(empty.PagedCandidateRate));
        Assert.True(double.IsNaN(empty.AverageCodeLength));
        Assert.Equal(0.0, empty.CandidateCoverage, 9);
    }

    [Fact]
    public void Slice_RecentWindowKeepsTwelveNewestInNewestFirstOrder()
    {
        StringBuilder csv = new();
        DateTime start = new(2026, 10, 28, 8, 0, 0);
        for (int i = 0; i < 50; i++)
        {
            csv.Append(Row(start.AddMinutes(i), 1, "字" + i.ToString(CultureInfo.InvariantCulture)))
                .Append('\n');
        }

        RimeLogStats stats = RimeLog.ParseCsv(csv.ToString());
        RimeSlice slice = stats.Slice(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));

        Assert.Equal(50L, slice.Commits);
        Assert.Equal(12, slice.Recent.Count);
        Assert.Equal("字49", slice.Recent[0].Text);
        Assert.Equal("字38", slice.Recent[11].Text);
        for (int i = 1; i < slice.Recent.Count; i++)
        {
            Assert.True(slice.Recent[i - 1].Time >= slice.Recent[i].Time);
        }
    }

    [Fact]
    public void Slice_EmptyAndReversedRangesAreSafe()
    {
        string csv = string.Join("\n", new[]
        {
            "2026-10-26 10:00:00,1,甲",
            "2026-10-27 10:00:00,1,乙",
            "2026-10-28 10:00:00,1,丙"
        }) + "\n";
        RimeLogStats stats = RimeLog.ParseCsv(csv);

        Assert.Equal(3L, stats.TotalCommits);
        RimeSlice middle = stats.Slice(new DateTime(2026, 10, 27), new DateTime(2026, 10, 27));
        Assert.Equal(1L, middle.Commits);
        Assert.True(middle.CharCounts.ContainsKey((int)'乙'));

        RimeSlice empty = stats.Slice(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        Assert.False(empty.HasData);
        Assert.Equal(0.0, empty.AverageChars, 9);
        Assert.Equal(0.0, empty.AverageTextChars, 9);
        Assert.Empty(empty.TopChars(5));
        Assert.Empty(empty.TopPhrases(5));
        Assert.Empty(empty.TopApps(5));
        Assert.Empty(empty.Recent);
        Assert.Equal(-1, empty.BusiestHour());
        Assert.Equal(0L, empty.CompositionTotal);
        Assert.Equal(0, empty.ActiveDays);
        Assert.Equal(0, empty.ActiveMinutes);

        RimeSlice reversed = stats.Slice(new DateTime(2026, 10, 28), new DateTime(2026, 10, 26));
        Assert.False(reversed.HasData);
    }

    [Fact]
    public void DaySeries_FillsMissingDaysAndRejectsReversedRanges()
    {
        string csv = string.Join("\n", new[]
        {
            "2026-10-26 10:00:00,3,一二三",
            "2026-10-28 10:00:00,2,四五"
        }) + "\n";
        RimeLogStats stats = RimeLog.ParseCsv(csv);

        List<RimeDay> series = stats.DaySeries(
            new DateTime(2026, 10, 26), new DateTime(2026, 10, 28));
        Assert.Equal(3, series.Count);
        Assert.Equal(3L, series[0].Chars);
        Assert.Equal(0L, series[1].Chars);
        Assert.Equal(2L, series[2].Chars);
        Assert.Equal(new DateTime(2026, 10, 27), series[1].Day);
        Assert.Equal(1L, series[0].Commits);

        Assert.Empty(stats.DaySeries(new DateTime(2026, 10, 28), new DateTime(2026, 10, 26)));
    }

    // ---------------------------------------------------------------- 导出

    [Fact]
    public void Export_WritesExactHeaderAndRoundTripsQuotedText()
    {
        Assert.Equal("普通", RimeLog.QuoteCsv("普通"));
        Assert.Equal("\"a,b\"", RimeLog.QuoteCsv("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", RimeLog.QuoteCsv("say \"hi\""));
        Assert.Equal("", RimeLog.QuoteCsv(null));
        Assert.Equal("\"a\nb\"", RimeLog.QuoteCsv("a\nb"));
        Assert.Equal("\"a\rb\"", RimeLog.QuoteCsv("a\rb"));

        string csv = "2026-10-28 16:17:23,3,\"a,b\"\n";
        RimeLogStats stats = RimeLog.ParseCsv(csv);
        string exported = RimeLog.Export(
            stats, new DateTime(2026, 10, 28), new DateTime(2026, 10, 28));
        string[] lines = exported.Split(
            new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(
            "时间点,字符数,明文,程序,方案,码长,候选序,总字符数,汉字数,输入类型",
            lines[0]);
        Assert.Equal(10, RimeLog.SplitCsv(lines[1]).Count);

        RimeCommit? again = RimeLog.ParseLine(lines[1].TrimStart('\uFEFF'));
        Assert.NotNull(again);
        Assert.Equal("a,b", again!.Text);
        Assert.Equal(3, again.Chars);
        Assert.Equal(3, again.TotalLength);
        Assert.Equal(0, again.HanziCount);
        Assert.Equal("raw_ascii", again.CommitType);

        // 引号文本导出后也能原样读回。
        string quoteCsv = "2026-10-28 16:18:00,8,\"say \"\"hi\"\"\"\n";
        string quoteExport = RimeLog.Export(
            RimeLog.ParseCsv(quoteCsv),
            new DateTime(2026, 10, 28),
            new DateTime(2026, 10, 28));
        string[] quoteLines = quoteExport.Split(
            new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        RimeCommit? quoteAgain = RimeLog.ParseLine(quoteLines[1]);
        Assert.NotNull(quoteAgain);
        Assert.Equal("say \"hi\"", quoteAgain!.Text);

        string outOfRange = RimeLog.Export(
            stats, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));
        string[] outLines = outOfRange.Split(
            new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(outLines);
        Assert.Equal(lines[0], outLines[0]);

        string headerOnly = RimeLog.Export(null, DateTime.Today, DateTime.Today);
        Assert.Equal(lines[0] + Environment.NewLine, headerOnly);
    }

    // ---------------------------------------------------------------- 缺失 / 坏行 / 空文件

    [Fact]
    public void Load_MissingPathReturnsEmptyStatsWithoutThrowing()
    {
        string folder = NewTempFolder();
        try
        {
            string missing = Path.Combine(folder, "does-not-exist.csv");
            RimeLogStats stats = RimeLog.Load(missing);
            Assert.True(stats.Missing);
            Assert.False(stats.HasData);
            Assert.Empty(stats.Commits);
            Assert.Equal("", stats.Error);
            Assert.Equal(0L, stats.TotalCommits);
            Assert.Equal(0L, stats.RecordLines);
            Assert.Equal(missing, stats.Source);

            Assert.True(RimeLog.Load("").Missing);
            Assert.True(RimeLog.Load(null).Missing);

            RimeSlice slice = stats.Slice(DateTime.Today, DateTime.Today);
            Assert.False(slice.HasData);

            RimeLogStats parsedNull = RimeLog.ParseCsv(null);
            Assert.False(parsedNull.Missing);
            Assert.False(parsedNull.HasData);
            Assert.Equal(0L, parsedNull.BadLines);
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    [Fact]
    public void Load_CountsHeaderBadLinesAndEmptyFiles()
    {
        string csv = string.Join("\n", new[]
        {
            "时间点,汉字个数,汉字明文",
            "2026-10-28 16:17:23,1,图",
            "这不是日志",
            "2026-10-28 25:99:99,1,坏时间",
            "2026-10-28 16:17:24,1,的"
        }) + "\n";

        string folder = NewTempFolder();
        try
        {
            RimeLogStats stats = RimeLog.Load(WriteFile(folder, "bad.csv", csv));
            Assert.Equal(2L, stats.TotalCommits);
            Assert.Equal(2L, stats.BadLines);
            Assert.Equal(1L, stats.HeaderLines);
            Assert.Equal(5L, stats.RecordLines);
            Assert.Equal(2L, stats.TotalChars);
            Assert.Equal("图", stats.Commits[0].Text);
            Assert.Equal("的", stats.Commits[1].Text);

            RimeLogStats empty = RimeLog.Load(WriteFile(folder, "empty.csv", "", bom: false));
            Assert.False(empty.Missing);
            Assert.False(empty.HasData);
            Assert.Equal(0L, empty.BadLines);
            Assert.Equal(0L, empty.HeaderLines);
            Assert.Equal(0L, empty.RecordLines);
            Assert.Equal(0L, empty.FileBytes);

            RimeLogStats headerOnly = RimeLog.Load(
                WriteFile(folder, "header-only.csv", "时间点,汉字个数,汉字明文\n"));
            Assert.False(headerOnly.HasData);
            Assert.Equal(1L, headerOnly.HeaderLines);
            Assert.Equal(0L, headerOnly.BadLines);
            Assert.Equal(1L, headerOnly.RecordLines);
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    [Fact]
    public void Load_JoinsQuotedMultilineRecordsAndUnquotedCommas()
    {
        Assert.True(RimeLog.HasOpenQuote("\"abc"));
        Assert.False(RimeLog.HasOpenQuote("\"abc\""));
        Assert.False(RimeLog.HasOpenQuote("a\"\"b"));
        Assert.False(RimeLog.HasOpenQuote("普通一行"));

        string multiline = "2026-10-28 10:00:00,3,\"a\nb\"\n"
            + "2026-10-28 10:00:01,1,图\n";
        string folder = NewTempFolder();
        try
        {
            RimeLogStats joined = RimeLog.Load(WriteFile(folder, "multiline.csv", multiline));
            Assert.Equal(2L, joined.TotalCommits);
            Assert.Equal(0L, joined.BadLines);
            Assert.Equal(0L, joined.HeaderLines);
            Assert.Equal(2L, joined.RecordLines);
            Assert.Equal("a\nb", joined.Commits[0].Text);
            Assert.Equal(3, joined.Commits[0].Chars);
            // 明文里的换行不属于码点统计。
            Assert.Equal(2, joined.Commits[0].TotalLength);

            RimeLogStats loose = RimeLog.Load(
                WriteFile(folder, "loose.csv", "2026-10-28 11:00:00,5,a,b,c\n"));
            Assert.Equal(1L, loose.TotalCommits);
            Assert.Equal("a,b,c", loose.Commits[0].Text);
            Assert.Equal(5, loose.Commits[0].Chars);

            RimeLogStats looseMetadata = RimeLog.Load(WriteFile(
                folder,
                "loose-metadata.csv",
                "2026-10-28 11:01:00,5,a,b,c,code.exe,rime_ice,8,0\n"));
            Assert.Equal(1L, looseMetadata.TotalCommits);
            Assert.Equal("a,b,c", looseMetadata.Commits[0].Text);
            Assert.Equal("code.exe", looseMetadata.Commits[0].Application);
            Assert.True(looseMetadata.Commits[0].FormatComplete);
        }
        finally
        {
            TryDeleteFolder(folder);
        }
    }

    [Fact]
    public void Load_CompleteOnlySkipsLegacyAndIncompleteRows()
    {
        string csv = string.Join("\n", new[]
        {
            "时间点,字符数,明文,程序,方案,码长,候选序",
            "2026-11-13 10:00:00,2,旧记录",
            "2026-11-13 10:00:01,3,六列,code.exe,rime_ice,8",
            "2026-11-13 10:00:02,3,空候选,code.exe,rime_ice,8,",
            "2026-11-13 10:00:03,3,完整,code.exe,rime_ice,8,0"
        }) + "\n";

        RimeLogStats strict = RimeLog.ParseCsv(csv, completeOnly: true);
        Assert.Equal(1L, strict.TotalCommits);
        Assert.Equal(1L, strict.HeaderLines);
        Assert.Equal(0L, strict.BadLines);
        Assert.Equal(3L, strict.IncompleteRows);
        Assert.Equal(5L, strict.RecordLines);
        Assert.Equal("完整", strict.Commits[0].Text);
        Assert.True(strict.Commits[0].FormatComplete);

        RimeLogStats all = RimeLog.ParseCsv(csv);
        Assert.Equal(4L, all.TotalCommits);
        Assert.Equal(0L, all.IncompleteRows);
    }

    [Fact]
    public void Load_DefaultPathOverrideUsesStrictProductMode()
    {
        string folder = NewTempFolder();
        string? previous = RimeLog.PathOverride;
        try
        {
            string path = WriteFile(
                folder,
                "default.csv",
                "2026-11-13 10:00:00,2,旧记录\n"
                + "2026-11-13 10:00:01,3,新记录,code.exe,rime_ice,8,0\n");

            RimeLog.PathOverride = path;
            Assert.Equal(path, RimeLog.DefaultPath);

            RimeLogStats strict = RimeLog.Load();
            Assert.Equal(1L, strict.TotalCommits);
            Assert.Equal(1L, strict.IncompleteRows);
            Assert.Equal("新记录", strict.Commits[0].Text);

            RimeLogStats all = RimeLog.Load(path);
            Assert.Equal(2L, all.TotalCommits);
            Assert.Equal(0L, all.IncompleteRows);
        }
        finally
        {
            RimeLog.PathOverride = previous;
            TryDeleteFolder(folder);
        }
    }

    [Fact]
    public void ParseCsv_HandlesBomAndCrLf()
    {
        string csv = "\uFEFF时间点,汉字个数,汉字明文\r\n"
            + "2026-10-28 16:17:23,1,图\r\n";

        RimeLogStats stats = RimeLog.ParseCsv(csv);
        Assert.Equal(1L, stats.TotalCommits);
        Assert.Equal(1L, stats.HeaderLines);
        Assert.Equal(0L, stats.BadLines);
        Assert.Equal(2L, stats.RecordLines);
        Assert.Equal("图", stats.Commits[0].Text);

        RimeCommit? row = RimeLog.ParseLine("\uFEFF2026-10-28 16:17:23,1,图");
        Assert.NotNull(row);
        Assert.Equal("图", row!.Text);
    }

    // ---------------------------------------------------------------- 隐私

    [Fact]
    public void Privacy_RedactKeepsShapeButNeverLeaksPlainText()
    {
        RimeCommit? commit = RimeLog.ParseLine(
            "2026-11-13 10:00:00,5,秘密abc,code.exe,rime_ice,8,0");
        Assert.NotNull(commit);

        string redacted = commit!.RedactedText;
        Assert.Equal(5, redacted.Length);
        Assert.Equal(redacted, commit.Redact());
        Assert.Equal(redacted, commit.DisplayText(false));
        Assert.Equal("秘密abc", commit.DisplayText(true));
        Assert.Contains('\uFF0A', redacted);
        Assert.DoesNotContain("秘密", redacted);
        Assert.DoesNotContain("秘密", commit.ToString());
        Assert.Contains("5 字", commit.ToString());
        Assert.Contains("mixed", commit.ToString());

        Assert.Equal("", RimeLog.Redact(null));
        Assert.Equal("\uFF0A \uFF0A", RimeLog.Redact("a b"));
        Assert.Equal("\uFF0A", RimeText.Redact("a"));
    }

    // ---------------------------------------------------------------- 词组 / 应用

    [Fact]
    public void PhraseFilterAndAppSummaryProduceFeatureWords()
    {
        string csv = string.Join("\n", new[]
        {
            "2026-10-28 10:00:00,4,数据精度,code.exe,rime_ice,8,0",
            "2026-10-28 10:00:05,4,数据精度,code.exe,rime_ice,8,1",
            "2026-10-28 10:00:10,2,你好,msedge.exe,rime_ice,4,0"
        }) + "\n";

        RimeSlice slice = RimeLog.ParseCsv(csv).Slice(
            new DateTime(2026, 10, 28), new DateTime(2026, 10, 28));

        Assert.Equal(2L, slice.PhraseCounts["数据"]);
        Assert.Equal(2L, slice.PhraseCounts["数据精度"]);
        Assert.Equal(1L, slice.PhraseCounts["你好"]);

        List<RimeAppSummary> apps = slice.TopApps(2);
        Assert.Equal(2, apps.Count);
        RimeAppSummary first = apps[0];
        Assert.Equal("code.exe", first.Path);
        Assert.Equal(8L, first.Chars);
        Assert.Equal(2L, first.Commits);
        Assert.Equal(8L, first.Hanzi);
        Assert.Equal(2L, first.PhraseCounts["数据"]);
        Assert.Equal(2L, first.PhraseCounts["数据精度"]);
        Assert.NotEmpty(first.TopChars(2));
        Assert.NotEmpty(first.FeatureWords(3));
        foreach (KeyValuePair<string, long> pair in first.ContentChars(8))
        {
            Assert.False(RimePhraseFilter.IsFunctionChar(pair.Key[0]));
        }

        Assert.True(RimePhraseFilter.IsFunctionChar('的'));
        Assert.False(RimePhraseFilter.IsFunctionChar('数'));
        Assert.True(RimePhraseFilter.IsFunctionPhrase("我们"));
        Assert.True(RimePhraseFilter.IsFunctionPhrase("的的"));
        Assert.False(RimePhraseFilter.IsFunctionPhrase("数据"));

        Dictionary<string, long> counts = new(StringComparer.Ordinal)
        {
            ["我们"] = 9,
            ["数据"] = 3,
            ["数据精度"] = 2,
            ["精度"] = 2
        };
        List<KeyValuePair<string, long>> picked = RimePhraseFilter.PickPhrases(counts, 5);
        Assert.Contains(picked, pair => pair.Key == "数据");
        Assert.DoesNotContain(picked, pair => pair.Key == "我们");
        Assert.DoesNotContain(picked, pair => pair.Key == "数据精度");
        Assert.Empty(RimePhraseFilter.PickPhrases(null, 5));
        Assert.Empty(RimePhraseFilter.PickPhrases(counts, 0));

        Assert.True(RimePhraseFilter.Overlaps(
            new List<string> { "数据" }, "数据精度"));
        Assert.False(RimePhraseFilter.Overlaps(
            new List<string> { "数据" }, "精度"));
    }

    // ---------------------------------------------------------------- 设置常量

    [Fact]
    public void RefreshSettingAndLimitsMatchV3()
    {
        Assert.Equal(0, RimeLog.NormalizeRefresh(0));
        Assert.Equal(0, RimeLog.NormalizeRefresh(-5));
        Assert.Equal(15, RimeLog.NormalizeRefresh(15));
        Assert.Equal(2, RimeLog.NormalizeRefresh(1));
        Assert.Equal(3600, RimeLog.NormalizeRefresh(99999));
        Assert.Equal("手动", RimeLog.RefreshLabel(0));
        Assert.Equal("30 秒", RimeLog.RefreshLabel(30));
        Assert.True(RimeLog.RefreshOptions[0] > 0);
        Assert.Equal(0, RimeLog.RefreshOptions[^1]);

        Assert.Equal(8L * 1024 * 1024, RimeLog.TailBytes);
        Assert.Equal(200_000, RimeLog.MaxCommits);
        Assert.Equal(40, RimeLog.RecentKeep);
        Assert.Equal(12, RimeLog.RecentDisplay);
    }

    // ---------------------------------------------------------------- 测试工具

    private static string Stamp(DateTime time) =>
        time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Row(DateTime time, int chars, string text) =>
        Stamp(time) + "," + chars.ToString(CultureInfo.InvariantCulture) + "," + text;

    private static string NewTempFolder()
    {
        string folder = Path.Combine(
            Path.GetTempPath(), "kms-rime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string WriteFile(
        string folder,
        string name,
        string content,
        bool bom = true)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content, new UTF8Encoding(bom));
        return path;
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
