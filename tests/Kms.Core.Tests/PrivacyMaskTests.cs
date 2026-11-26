using Kms.Core.Privacy;

namespace Kms.Core.Tests;

public sealed class PrivacyMaskTests
{
    [Theory]
    [InlineData(0, "编程 · 已隐藏")]
    [InlineData(8, "沟通 · 已隐藏")]
    [InlineData(9, "娱乐 · 已隐藏")]
    [InlineData(99, "其他 · 已隐藏")]
    [InlineData(-3, "其他 · 已隐藏")]
    public void MaskTitle_UsesCategoryName(int categoryId, string expected) =>
        Assert.Equal(expected, PrivacyMask.MaskTitle("任意标题", categoryId));

    [Fact]
    public void MaskTitle_NullStillMasked() =>
        Assert.Equal("编程 · 已隐藏", PrivacyMask.MaskTitle(null, 0));

    [Fact]
    public void MaskTitle_IsIdempotent()
    {
        string once = PrivacyMask.MaskTitle("x", 5);
        Assert.Equal(once, PrivacyMask.MaskTitle(once, 5));
    }

    [Fact]
    public void MaskText_KeepsLengthAndWhitespace()
    {
        const string source = "今天 写了 三千字";
        string masked = PrivacyMask.MaskText(source);

        Assert.Equal(source.Length, masked.Length);
        Assert.Contains(PrivacyMask.MaskChar, masked);
        Assert.Contains(' ', masked);
        Assert.DoesNotContain('今', masked);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MaskText_EmptyInputReturnsEmpty(string? input) =>
        Assert.Equal("", PrivacyMask.MaskText(input));

    [Fact]
    public void MaskText_IsIdempotent()
    {
        string masked = PrivacyMask.MaskText("hello world");
        Assert.Equal(masked, PrivacyMask.MaskText(masked));
    }

    [Theory]
    [InlineData(@"C:\Users\me\docs\plan.md", "plan.md")]
    [InlineData("/home/u/a/b/c.txt", "c.txt")]
    [InlineData("no-directory.txt", "no-directory.txt")]
    public void MaskPath_KeepsFileNameOnly(string input, string expected) =>
        Assert.Equal(expected, PrivacyMask.MaskPath(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MaskPath_EmptyInputReturnsEmpty(string? input) =>
        Assert.Equal("", PrivacyMask.MaskPath(input));

    [Fact]
    public void MaskPath_TrailingSeparatorFallsBackToHidden() =>
        Assert.Equal("已隐藏", PrivacyMask.MaskPath(@"C:\Users\me\"));

    [Fact]
    public void CategoryTable_MatchesV3()
    {
        string[] expected = ["编程", "游戏", "视频", "其他", "未分类", "办公", "科研", "写作", "沟通", "娱乐"];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], PrivacyMask.CategoryName(i));
        }
    }

    [Fact]
    public void MaskTitles_HandlesNullAndPreservesCount()
    {
        Assert.Empty(PrivacyMask.MaskTitles(null, 0));
        Assert.Equal(["视频 · 已隐藏", "视频 · 已隐藏"], PrivacyMask.MaskTitles(["a", "b"], 2));
    }
}
