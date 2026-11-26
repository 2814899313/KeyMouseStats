using System.Globalization;
using System.Text;
using Avalonia.Media;
using Kms.Render.Text;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

public sealed class TextMeasureTests
{
    // ---------- 字号阶梯 ----------

    [Fact]
    public void Typography_Default_IsSixStepLadder()
    {
        Typography typography = Typography.Default;

        Assert.Equal(38, typography.Display);
        Assert.Equal(29, typography.H1);
        Assert.Equal(21, typography.H2);
        Assert.Equal(13, typography.Body);
        Assert.Equal(11, typography.Caption);
        Assert.Equal(11, typography.Mono);
        Assert.True(typography.Display > typography.H1);
        Assert.True(typography.H1 > typography.H2);
        Assert.True(typography.H2 > typography.Body);
        Assert.True(typography.Body > typography.Caption);
        Assert.Equal(typography.Caption, typography.Mono);
    }

    [Fact]
    public void Typography_AllThirteenThemesShareDefaultLadder()
    {
        foreach (ThemeTokens theme in ThemeCatalog.All)
        {
            Assert.Equal(Typography.Default, theme.Typography);
        }
    }

    // ---------- 字体回退链 ----------

    [Fact]
    public void MonoChain_IsCascadiaThenConsolasThenCourier()
    {
        Assert.Equal(
            new[] { "Cascadia Mono", "Consolas", "Courier New" },
            FontChains.Mono);
    }

    [Fact]
    public void UiChain_IsYaHeiThenSegoe()
    {
        Assert.Equal(
            new[] { "Microsoft YaHei UI", "Segoe UI" },
            FontChains.Ui);
    }

    [Fact]
    public void ResolveFamily_KeepsRequestedFamilyWhenInstalled()
    {
        string actual = FontChains.ResolveFamily(
            "Consolas",
            new[] { "Cascadia Mono", "Consolas", "Courier New" });

        Assert.Equal("Consolas", actual);
    }

    [Fact]
    public void ResolveFamily_FallsBackToConsolasWhenCascadiaMissing()
    {
        string actual = FontChains.ResolveFamily(
            "Cascadia Mono",
            new[] { "Consolas", "Courier New", "Segoe UI" });

        Assert.Equal("Consolas", actual);
    }

    [Fact]
    public void ResolveFamily_FallsBackToCourierWhenBothMonoFontsMissing()
    {
        string actual = FontChains.ResolveFamily(
            "Cascadia Mono",
            new[] { "Courier New", "Segoe UI" });

        Assert.Equal("Courier New", actual);
    }

    [Fact]
    public void ResolveFamily_IsCaseInsensitive()
    {
        string actual = FontChains.ResolveFamily(
            "cascadia mono",
            new[] { "CASCADIA MONO" });

        Assert.Equal("Cascadia Mono", actual);
    }

    [Fact]
    public void ResolveFamily_UiFallsBackToSegoe()
    {
        string actual = FontChains.ResolveFamily(
            "Microsoft YaHei UI",
            new[] { "Segoe UI", "Consolas" });

        Assert.Equal("Segoe UI", actual);
    }

    [Fact]
    public void ResolveFamily_KeepsUnknownFamilyWhenInstalled()
    {
        const string Requested = "Comic Sans MS";

        string actual = FontChains.ResolveFamily(Requested, new[] { Requested });

        Assert.Equal(Requested, actual);
    }

    [Fact]
    public void ResolveFamily_UnknownFamilyMissingUsesUiChain()
    {
        string actual = FontChains.ResolveFamily(
            "Not Installed Font",
            new[] { "Segoe UI" });

        Assert.Equal("Segoe UI", actual);
    }

    [Fact]
    public void ResolveFamily_EmptyRequestUsesUiChain()
    {
        string actual = FontChains.ResolveFamily(
            "  ",
            new[] { "Segoe UI" });

        Assert.Equal("Segoe UI", actual);
    }

    [Fact]
    public void ResolveFamily_NoFontsInstalledReturnsChainTail()
    {
        Assert.Equal("Courier New", FontChains.ResolveFamily("Cascadia Mono", Array.Empty<string>()));
        Assert.Equal("Segoe UI", FontChains.ResolveFamily(null, Array.Empty<string>()));
    }

    [Fact]
    public void ResolveFamily_ProbeOverloadIsUsed()
    {
        string actual = FontChains.ResolveFamily(
            "Cascadia Mono",
            name => string.Equals(name, "Consolas", StringComparison.Ordinal));

        Assert.Equal("Consolas", actual);
    }

    [Fact]
    public void ResolveFamily_WrapsToChainHeadIfLaterEntriesMissing()
    {
        string actual = FontChains.ResolveFamily(
            "Consolas",
            new[] { "Cascadia Mono" });

        Assert.Equal("Cascadia Mono", actual);
    }

    [Fact]
    public void ResolveFamily_NullAvailableThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => FontChains.ResolveFamily("Consolas", (IEnumerable<string>)null!));
    }

    // ---------- LRU 缓存 ----------

    [Fact]
    public void Cache_MissThenHitReturnsSameEntryAndMetrics()
    {
        int calls = 0;
        var cache = new TextMeasureCache(8, key =>
        {
            calls++;
            return new TextMetrics(key.Text.Length * 7, key.FontSize, key.FontSize * 0.8);
        });

        TextMeasureEntry first = cache.Get("abc", "Segoe UI", 13);
        TextMeasureEntry second = cache.Get("abc", "Segoe UI", 13);

        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.Equal(1, cache.Count);
        Assert.Equal(21, first.Width);
        Assert.Equal(13, first.Height);
        Assert.Equal(10.4, first.Baseline, 10);
    }

    [Fact]
    public void Cache_AllFiveKeyPartsSeparateEntries()
    {
        int calls = 0;
        var cache = new TextMeasureCache(8, key =>
        {
            calls++;
            return new TextMetrics(key.Text.Length, key.FontSize, 0);
        });

        TextMeasureKey baseline = Key("abc");
        TextMeasureKey otherText = Key("abd");
        TextMeasureKey otherFamily = Key("abc", family: "Consolas");
        TextMeasureKey otherSize = Key("abc", size: 14);
        TextMeasureKey otherWeight = Key("abc", weight: FontWeight.Bold);
        TextMeasureKey otherWidth = Key("abc", maxWidth: 100);

        TextMeasureEntry[] entries =
        [
            cache.Get(baseline),
            cache.Get(otherText),
            cache.Get(otherFamily),
            cache.Get(otherSize),
            cache.Get(otherWeight),
            cache.Get(otherWidth)
        ];

        Assert.Equal(6, cache.Count);
        Assert.Equal(6, calls);
        Assert.Equal(6, entries.Distinct().Count());
    }

    [Fact]
    public void Cache_EvictsLeastRecentlyUsed()
    {
        var cache = new TextMeasureCache(2, key => new TextMetrics(1, 1, 1));

        TextMeasureKey a = Key("a");
        TextMeasureKey b = Key("b");
        TextMeasureKey c = Key("c");

        cache.Get(a);
        cache.Get(b);
        cache.Get(c);

        Assert.False(cache.TryGet(a, out _));
        Assert.True(cache.TryGet(b, out _));
        Assert.True(cache.TryGet(c, out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Cache_HitRefreshesRecency()
    {
        var cache = new TextMeasureCache(2, key => new TextMetrics(1, 1, 1));

        TextMeasureKey a = Key("a");
        TextMeasureKey b = Key("b");
        TextMeasureKey c = Key("c");

        cache.Get(a);
        cache.Get(b);
        cache.Get(a); // A 变为最近使用
        cache.Get(c); // 应淘汰 B

        Assert.True(cache.TryGet(a, out _));
        Assert.False(cache.TryGet(b, out _));
        Assert.True(cache.TryGet(c, out _));
    }

    [Fact]
    public void Cache_ClearResetsCountAndForcesMiss()
    {
        int calls = 0;
        var cache = new TextMeasureCache(8, key =>
        {
            calls++;
            return new TextMetrics(1, 1, 1);
        });

        cache.Get("a", "Segoe UI", 13);
        Assert.Equal(1, cache.Count);

        cache.Clear();
        Assert.Equal(0, cache.Count);

        cache.Get("a", "Segoe UI", 13);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Cache_CapacityZeroDisablesStorage()
    {
        int calls = 0;
        var cache = new TextMeasureCache(0, key =>
        {
            calls++;
            return new TextMetrics(1, 1, 1);
        });

        cache.Get("a", "Segoe UI", 13);
        cache.Get("a", "Segoe UI", 13);

        Assert.Equal(2, calls);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_DefaultCapacityIs512()
    {
        var cache = new TextMeasureCache(key => new TextMetrics(1, 1, 1));

        Assert.Equal(512, cache.Capacity);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_NegativeCapacityThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextMeasureCache(-1));
    }

    [Fact]
    public void Cache_TryGetMissDoesNotCreate()
    {
        int calls = 0;
        var cache = new TextMeasureCache(8, key =>
        {
            calls++;
            return new TextMetrics(1, 1, 1);
        });

        bool found = cache.TryGet(Key("missing"), out TextMeasureEntry? entry);

        Assert.False(found);
        Assert.Null(entry);
        Assert.Equal(0, calls);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_NormalizesNegativeAndNaNMaxWidth()
    {
        int calls = 0;
        var cache = new TextMeasureCache(8, key =>
        {
            calls++;
            return new TextMetrics(1, 1, 1);
        });

        TextMeasureEntry negative = cache.Get("a", "Segoe UI", 13, FontWeight.Normal, -10);
        TextMeasureEntry zero = cache.Get("a", "Segoe UI", 13, FontWeight.Normal, 0);
        Assert.Same(negative, zero);

        TextMeasureEntry nan = cache.Get("a", "Segoe UI", 13, FontWeight.Normal, double.NaN);
        TextMeasureEntry infinity = cache.Get(
            "a",
            "Segoe UI",
            13,
            FontWeight.Normal,
            double.PositiveInfinity);
        Assert.Same(nan, infinity);

        Assert.Equal(2, calls);
    }

    [Fact]
    public void Cache_MeasureReturnsCachedMetrics()
    {
        var cache = new TextMeasureCache(4, key =>
            new TextMetrics(key.Text.Length * 10, key.FontSize, key.FontSize * 0.75));

        TextMetrics first = cache.Measure("abcd", "Segoe UI", 13);
        TextMetrics second = cache.Measure("abcd", "Segoe UI", 13);

        Assert.Equal(40, first.Width);
        Assert.Equal(13, first.Height);
        Assert.Equal(9.75, first.Baseline, 10);
        Assert.Equal(first, second);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Cache_InvalidFontSizeThrows()
    {
        var cache = new TextMeasureCache(4, key => new TextMetrics(1, 1, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get("a", "Segoe UI", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get("a", "Segoe UI", double.NaN));
    }

    [Fact]
    public void Cache_NullTextThrows()
    {
        var cache = new TextMeasureCache(4, key => new TextMetrics(1, 1, 1));

        Assert.Throws<ArgumentNullException>(() => cache.Get(null!, "Segoe UI", 13));
    }

    // ---------- 二分省略号 ----------

    public static TheoryData<string, double> EllipsisCases()
    {
        string[] texts =
        [
            "",
            "A",
            "AB",
            "Hello World",
            "键鼠统计工具",
            "Key键鼠Mouse统计",
            "A😀B",
            "😀😀😀",
            "e\u0301e\u0301",
            "👨‍👩‍👧",
            "🇨🇳",
            "中English混排123",
            new string('A', 64),
            "键A鼠B标C",
            "…"
        ];

        double[] widths =
        [
            -5,
            0,
            1,
            2,
            3,
            4,
            5,
            6,
            9,
            10,
            11,
            12,
            15,
            20,
            25,
            30,
            40,
            60,
            100,
            1000
        ];

        var data = new TheoryData<string, double>();
        foreach (string text in texts)
        {
            foreach (double width in widths)
            {
                data.Add(text, width);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EllipsisCases))]
    public void Ellipsis_MatchesBruteForceReference(string text, double maxWidth)
    {
        string expected = BruteForceEllipsis(text, maxWidth, MeasureByRune);
        string actual = TextFit.EllipsisByWidth(text, maxWidth, MeasureByRune);

        Assert.Equal(expected, actual);

        if (maxWidth >= 0)
        {
            Assert.True(
                MeasureByRune(actual) <= maxWidth,
                $"结果宽度 {MeasureByRune(actual)} 超过 {maxWidth}：{actual}");
        }
    }

    [Fact]
    public void Ellipsis_EmptyTextReturnsEmpty()
    {
        Assert.Equal("", TextFit.EllipsisByWidth("", 0, MeasureByRune));
        Assert.Equal("", TextFit.EllipsisByWidth("", -1, MeasureByRune));
        Assert.Equal("", TextFit.EllipsisByWidth("", double.PositiveInfinity, MeasureByRune));
    }

    [Fact]
    public void Ellipsis_ReturnsEmptyWhenEllipsisItselfDoesNotFit()
    {
        // 省略号宽 3；“>=” 语义与 v3 一致：刚好等于也返回空串。
        Assert.Equal("", TextFit.EllipsisByWidth("ABC", 3, MeasureByRune));
        Assert.Equal("", TextFit.EllipsisByWidth("ABC", 2, MeasureByRune));
    }

    [Fact]
    public void Ellipsis_ReturnsTailWhenNoPrefixFits()
    {
        // 全文 12 > 5，省略号 3 < 5，但剩余预算 2 放不下任何一个前缀。
        Assert.Equal("…", TextFit.EllipsisByWidth("AB", 5, MeasureByRune));
        Assert.Equal("…", TextFit.EllipsisByWidth("A", 5, MeasureByRune));
    }

    [Fact]
    public void Ellipsis_FitsExactlyReturnsOriginal()
    {
        Assert.Equal("AB", TextFit.EllipsisByWidth("AB", 12, MeasureByRune));
        Assert.Equal("AB", TextFit.EllipsisByWidth("AB", double.PositiveInfinity, MeasureByRune));
    }

    [Fact]
    public void Ellipsis_NeverSplitsSurrogatePair()
    {
        // 逐码元二分在这里会接受 “A + 半个 emoji”（其替换字形宽 6，合计 12），
        // 真正的文本元素算法只能退到 “A…”。
        string result = TextFit.EllipsisByWidth("A😀B", 15, MeasureByRune);

        Assert.Equal("A…", result);
        Assert.Equal(2, result.EnumerateRunes().Count());
        Assert.DoesNotContain('\uFFFD', result);
    }

    [Fact]
    public void Ellipsis_NeverSplitsCombiningCluster()
    {
        string result = TextFit.EllipsisByWidth("e\u0301e\u0301", 10, MeasureByRune);
        Assert.Equal("e\u0301…", result);

        // 组合符有正宽度时，逐码元会切出 "e…"；文本元素边界必须整体保留 e+combining。
        string withPositiveMark = TextFit.EllipsisByWidth("e\u0301X", 10, MeasureCombining);
        Assert.Equal("…", withPositiveMark);
    }

    [Fact]
    public void Ellipsis_NeverSplitsZwjCluster()
    {
        // 家庭 emoji 是单个文本元素；任何“半个家庭”都不允许出现。
        string result = TextFit.EllipsisByWidth("👨‍👩‍👧B", 15, MeasureByRune);

        Assert.Equal("…", result);
    }

    [Fact]
    public void Ellipsis_NaNTreatedAsUnconstrained()
    {
        Assert.Equal("AB", TextFit.EllipsisByWidth("AB", double.NaN, MeasureByRune));
        Assert.True(TextFit.Fits("AB", double.NaN, MeasureByRune));
        Assert.Equal(2, TextFit.VisibleCharsByWidth("AB", double.NaN, MeasureByRune));
    }

    [Fact]
    public void Ellipsis_UsesLogarithmicNumberOfMeasurements()
    {
        int calls = 0;
        string text = new('A', 4096);

        string result = TextFit.EllipsisByWidth(text, 30, value =>
        {
            calls++;
            return value.Length * 6.0;
        });

        Assert.Equal("AAAA…", result);
        Assert.InRange(calls, 2, 20);
    }

    [Fact]
    public void Ellipsis_NullTextThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => TextFit.EllipsisByWidth(null!, 10, MeasureByRune));
    }

    [Fact]
    public void Ellipsis_NullMeasureThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => TextFit.EllipsisByWidth("a", 10, null!));
    }

    // ---------- VisibleChars / Fits ----------

    [Theory]
    [InlineData("", 100, 0)]
    [InlineData("Hello World", 30, 5)]
    [InlineData("Hello World", 1000, 11)]
    [InlineData("键鼠统计", 25, 2)]
    [InlineData("A😀B", 20, 3)]
    [InlineData("A😀B", 10, 1)]
    [InlineData("A😀B", 0, 0)]
    public void VisibleCharsByWidth_ReturnsGraphemeAlignedLength(
        string text,
        double maxWidth,
        int expected)
    {
        Assert.Equal(expected, TextFit.VisibleCharsByWidth(text, maxWidth, MeasureByRune));
    }

    [Fact]
    public void Fits_EmptyTextIsAlwaysTrue()
    {
        Assert.True(TextFit.Fits("", -100, MeasureByRune));
        Assert.True(TextFit.Fits("", 0, MeasureByRune));
    }

    [Fact]
    public void Fits_UsesInjectedMeasure()
    {
        Assert.True(TextFit.Fits("AB", 12, MeasureByRune));
        Assert.False(TextFit.Fits("AB", 11, MeasureByRune));
    }

    [Fact]
    public void Fits_NullTextThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => TextFit.Fits(null!, 10, MeasureByRune));
    }

    [Fact]
    public void Fits_NullMeasureThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => TextFit.Fits("a", 10, null!));
    }

    // ---------- helpers ----------

    private static TextMeasureKey Key(
        string text,
        string family = "Segoe UI",
        double size = 13,
        FontWeight weight = FontWeight.Normal,
        double maxWidth = double.PositiveInfinity) =>
        new(text, family, size, weight, maxWidth);

    /// <summary>确定性的纯测量：ASCII 6、CJK 10、emoji 12、省略号 3、组合符 0。</summary>
    private static double MeasureByRune(string text)
    {
        double width = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            width += rune.Value switch
            {
                0x2026 => 3,
                0x1F600 => 12,
                0x200D => 0,
                >= 0x0300 and <= 0x036F => 0,
                >= 0x1F1E6 and <= 0x1F1FF => 6,
                >= 0x4E00 and <= 0x9FFF => 10,
                _ => 6
            };
        }

        return width;
    }

    private static double MeasureCombining(string text)
    {
        if (text == "…")
        {
            return 3;
        }

        double width = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            width += rune.Value == 0x0301 ? 2 : 6;
        }

        return width;
    }

    /// <summary>逐字参考实现：从后往前找最大可容纳的前缀（按 Unicode 文本元素切分）。</summary>
    private static string BruteForceEllipsis(
        string text,
        double maxWidth,
        Func<string, double> measure)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (measure(text) <= maxWidth)
        {
            return text;
        }

        const string Tail = "…";
        double tailWidth = measure(Tail);
        if (tailWidth >= maxWidth)
        {
            return string.Empty;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        for (int i = starts.Length - 1; i >= 1; i--)
        {
            string head = text[..starts[i]];
            if (measure(head) + tailWidth <= maxWidth)
            {
                return head + Tail;
            }
        }

        return Tail;
    }
}
