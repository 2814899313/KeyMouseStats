using Kms.Core.Apps;

namespace Kms.Core.Tests;

public sealed class AppFilterTests
{
    [Fact]
    public void AppFilterScope_UsesStableCacheKeysAndNormalizesUnicodePath()
    {
        AppFilterScope all = AppFilterScope.All;
        AppFilterScope app = AppFilterScope.ByApp(@"  C:\工具\Goink.EXE  ");
        AppFilterScope category = AppFilterScope.ByCategory(" 小说创作 ");

        Assert.Equal("all", all.CacheKey);
        Assert.Equal(@"app:c:\工具\goink.exe", app.CacheKey);
        Assert.Equal("cat:小说创作", category.CacheKey);
    }

    [Fact]
    public void AppScopeSet_ExpandsCategoryAndMatchesNormalizedKeys()
    {
        AppScopeSet scope = AppScopeSet.FromCategory(
            "编程",
            [@"C:\Apps\Goink.exe", @"C:\Apps\COMSOL.exe", @"C:\Apps\GOINK.EXE"]);

        Assert.False(scope.IsAll);
        Assert.Equal("cat:编程", scope.CacheKey);
        Assert.Equal(2, scope.Count);
        Assert.True(scope.Contains(@"c:\apps\goink.exe"));
        Assert.True(scope.Contains(@"C:\Apps\COMSOL.EXE"));
        Assert.False(scope.Contains(@"C:\Apps\Other.exe"));
        Assert.True(AppScopeSet.All.Contains("anything"));
    }

    [Fact]
    public void AppFilterScope_GroupedApps_ExpandsAllKeys()
    {
        AppFilterScope group = AppFilterScope.ByAppGroup(
            @"C:\New\ChatGPT.exe",
            [@"C:\Old\ChatGPT.exe", @"C:\New\ChatGPT.exe", @"c:\new\chatgpt.exe"]);
        AppScopeSet scope = group.ToScopeSet();

        Assert.Equal(2, scope.Count);
        Assert.True(scope.Contains(@"C:\Old\ChatGPT.exe"));
        Assert.True(scope.Contains(@"C:\New\ChatGPT.exe"));
        Assert.Equal(scope.CacheKey, group.CacheKey);
        Assert.Equal(2, group.AppKeys?.Count);
    }

    [Fact]
    public void AppDisplayName_CollapsesVersionSuffixesAndExtension()
    {
        Assert.Equal("chatgpt", AppDisplayName.GroupKey(@"C:\Apps\ChatGPT~3.exe"));
        Assert.Equal("keymousestats", AppDisplayName.GroupKey("keymousestats-2.2.0-r8.exe"));
        Assert.Equal("keymousestats", AppDisplayName.GroupKey("keymousestats-2.0.4-clean.exe"));
        Assert.Equal("keymousestats", AppDisplayName.GroupKey("keymousestats.clean.exe"));
        Assert.Equal("qq", AppDisplayName.GroupKey("qq~2.exe"));
        Assert.Equal("deepseek harness", AppDisplayName.GroupKey("deepseek harness.exe"));
    }

    [Fact]
    public void AppCategoryBuiltins_MergesLegacyEntertainmentIntoMedia()
    {
        Assert.DoesNotContain(
            AppCategoryBuiltins.All,
            category => category.CategoryKey == AppCategoryBuiltins.Entertainment);
        Assert.Equal(
            AppCategoryBuiltins.Media,
            AppCategoryBuiltins.ForKey("entertainment")?.CategoryKey);
        Assert.Equal(
            AppCategoryBuiltins.Media,
            AppCategoryBuiltins.KeyForLegacyId(9));
        Assert.Equal(
            AppCategoryBuiltins.Media,
            AppCategoryBuiltins.ForLegacyId(9)?.CategoryKey);
    }

    [Fact]
    public void AppDataCapabilities_ExactAndPendingAreDisjointAndExplainable()
    {
        AppDataCapabilities capabilities = AppDataCapabilities.AllExact
            .WithPending(AppDataCapability.Heatmap, "热区尚未采集");

        Assert.True(capabilities.IsExact(AppDataCapability.Hourly));
        Assert.True(capabilities.IsPending(AppDataCapability.Heatmap));
        Assert.False(capabilities.IsExact(AppDataCapability.Heatmap));
        Assert.Contains("热区", capabilities.Describe(), StringComparison.Ordinal);
    }
}
