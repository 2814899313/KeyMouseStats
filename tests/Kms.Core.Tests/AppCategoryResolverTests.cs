using Kms.Core.Apps;

namespace Kms.Core.Tests;

/// <summary>
/// v4.2.0 工作性质分类责任链：窗口 &gt; 标题关键词 &gt; 应用 &gt; 启发式 &gt; 未分类，
/// 以及自定义分类的 is_work、应用展开与全局筛选。
/// </summary>
public sealed class AppCategoryResolverTests
{
    private const string Goink = @"C:\Apps\Goink.exe";
    private const string Code = @"C:\Apps\Code.exe";

    private static readonly AppCategoryDefinition Novel =
        new("novel", "小说创作", "#EC4899", true, 20, false, 12);

    private static readonly AppCategoryDefinition Sim =
        new("sim", "仿真实验", "#06B6D4", true, 21, false, 13);

    private static readonly AppCategoryDefinition Rest =
        new("rest", "休息", "#64748B", false, 22, false, 14);

    private static AppCategoryResolver Resolver(params AppCategoryRule[] rules) =>
        new([.. AppCategoryBuiltins.All, Novel, Sim, Rest], rules);

    private static AppCategoryRule Rule(string ruleKey, string categoryKey, int categoryId) =>
        new(ruleKey, categoryKey, categoryId, "user_override", 1);

    [Fact]
    public void Resolve_WindowRuleBeatsTitleAndApp()
    {
        AppCategoryResolver resolver = Resolver(
            Rule("window:" + Goink.ToLowerInvariant() + "\n第一章 初入江湖", Novel.CategoryKey, Novel.LegacyId),
            Rule("title:" + Goink.ToLowerInvariant() + "\n第一章", Sim.CategoryKey, Sim.LegacyId),
            Rule("app:" + Goink.ToLowerInvariant(), AppCategoryBuiltins.Office, 5));

        AppCategoryMatch match = resolver.Resolve(Goink, "第一章 初入江湖");
        Assert.Equal(Novel.CategoryKey, match.Category.CategoryKey);
        Assert.Equal("窗口规则", match.Reason);
    }

    [Fact]
    public void Resolve_TitleRuleBeatsApp_AndLongestKeywordWins()
    {
        AppCategoryResolver resolver = Resolver(
            Rule("title:" + Goink.ToLowerInvariant() + "\n第一章", Novel.CategoryKey, Novel.LegacyId),
            Rule("title:" + Goink.ToLowerInvariant() + "\n初入", Sim.CategoryKey, Sim.LegacyId),
            Rule("app:" + Goink.ToLowerInvariant(), AppCategoryBuiltins.Office, 5));

        AppCategoryMatch match = resolver.Resolve(Goink, "第一章 初入江湖");
        Assert.Equal(Novel.CategoryKey, match.Category.CategoryKey);
        Assert.Equal("关键词规则", match.Reason);
    }

    [Fact]
    public void Resolve_AppRuleBeatsHeuristic()
    {
        AppCategoryResolver resolver = Resolver(
            Rule("app:" + Code.ToLowerInvariant(), AppCategoryBuiltins.Media, 2));

        AppCategoryMatch match = resolver.Resolve(Code, "Program.cs");
        Assert.Equal(AppCategoryBuiltins.Media, match.Category.CategoryKey);
        Assert.Equal("应用规则", match.Reason);
    }

    [Fact]
    public void Resolve_HeuristicThenUncategorized()
    {
        AppCategoryResolver resolver = Resolver();

        AppCategoryMatch code = resolver.Resolve(Code, "Program.cs");
        Assert.Equal(AppCategoryBuiltins.Coding, code.Category.CategoryKey);
        Assert.Equal("按应用名预设", code.Reason);

        AppCategoryMatch unknown = resolver.Resolve(@"C:\Apps\mystery.exe", "Mystery");
        Assert.Equal(AppCategoryBuiltins.Uncategorized, unknown.Category.CategoryKey);
        Assert.Equal("none", unknown.Source);
    }

    [Fact]
    public void Resolve_LegacyFileNameRules_MatchFullProcessPath()
    {
        AppCategoryResolver resolver = Resolver(
            Rule("window:goink.exe\n初入", Sim.CategoryKey, Sim.LegacyId),
            Rule("title:goink.exe\n第一章", Novel.CategoryKey, Novel.LegacyId),
            Rule("app:goink.exe", AppCategoryBuiltins.Office, 5));

        Assert.Equal(Sim.CategoryKey, resolver.Resolve(Goink, "初入").Category.CategoryKey);
        Assert.Equal(Novel.CategoryKey, resolver.Resolve(Goink, "第一章 江湖").Category.CategoryKey);
        Assert.Equal(AppCategoryBuiltins.Office, resolver.Resolve(Goink, "其他标题").Category.CategoryKey);

        IReadOnlySet<string> apps = resolver.GetAppKeys(Novel.CategoryKey, [Goink, Code]);
        Assert.Contains(Goink.ToLowerInvariant(), apps);
    }

    [Fact]
    public void IsWork_UsesCategoryFlag_IncludingCustomDefinitions()
    {
        AppCategoryResolver resolver = Resolver();

        Assert.True(resolver.IsWork(Novel.LegacyId));
        Assert.True(resolver.IsWork(Sim.LegacyId));
        Assert.False(resolver.IsWork(Rest.LegacyId));
        Assert.True(resolver.IsWork(0));
        Assert.False(resolver.IsWork(4));
        Assert.Equal("小说创作", resolver.NameFor(Novel.LegacyId));
        Assert.Equal(Novel.CategoryKey, resolver.KeyFor(Novel.LegacyId));
    }

    [Fact]
    public void GetAppKeys_ExpandsKnownAppsAndRules()
    {
        AppCategoryResolver resolver = Resolver(
            Rule("app:" + Goink.ToLowerInvariant(), Novel.CategoryKey, Novel.LegacyId));

        IReadOnlySet<string> novel = resolver.GetAppKeys(
            Novel.CategoryKey,
            [Goink, Code, @"C:\Apps\mystery.exe"]);
        Assert.Contains(Goink.ToLowerInvariant(), novel);
        Assert.DoesNotContain(Code.ToLowerInvariant(), novel);

        IReadOnlySet<string> coding = resolver.GetAppKeys(
            AppCategoryBuiltins.Coding,
            [Goink, Code, @"C:\Apps\mystery.exe"]);
        Assert.Contains(Code.ToLowerInvariant(), coding);
        Assert.DoesNotContain(Goink.ToLowerInvariant(), coding);
    }

    [Fact]
    public void ByCategory_ScopeExpandsToDerivedAppSet()
    {
        AppCategoryResolver resolver = Resolver(
            Rule("app:" + Goink.ToLowerInvariant(), Novel.CategoryKey, Novel.LegacyId));
        AppFilterScope filter = AppFilterScope.ByCategory(Novel.CategoryKey);
        AppScopeSet scope = filter.ToScopeSet(
            resolver.GetAppKeys(Novel.CategoryKey, [Goink, Code]));

        Assert.False(scope.IsAll);
        Assert.Equal("cat:novel", scope.CacheKey);
        Assert.Equal(1, scope.Count);
        Assert.True(scope.Contains(Goink));
        Assert.False(scope.Contains(Code));
        Assert.Equal("cat:novel", filter.CacheKey);
    }

    [Fact]
    public void ByApp_ScopeStillMatchesSingleApp()
    {
        AppScopeSet scope = AppFilterScope.ByApp(Code).ToScopeSet();

        Assert.False(scope.IsAll);
        Assert.Equal("app:c:\\apps\\code.exe", scope.CacheKey);
        Assert.True(scope.Contains(Code));
        Assert.False(scope.Contains(Goink));
    }
}
