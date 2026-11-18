// ============================================================================
//  tests/CategoryProviderTests.cs  ·  v3.0 Milestone 1 / Step 5
//
//  验证分类扩展链:
//   1. 0~9 号分类 ID 完整且 0~5 的语义/数值被冻结;
//   2. 责任链优先级:UserOverride > AiCache > DefaultRule;
//   3. AiCache 在 v3.0 恒为 false(占位),不影响既有判定;
//   4. AppActivity.Explain 的六种来源字符串 → category_source 四值枚举的精确映射;
//   5. 6~9 号新类别的默认规则生效,且没有把既有应用改判(科研/办公边界)。
//
//  纯托管用例,不写文件,不触碰用户目录。
// ============================================================================

using System;
using KeyMouseStats;

internal static class CategoryProviderTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + "(期望 " + expected + ",实际 " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("CategoryProviderTests", Run);
    }

    private static AppUsage App(string path)
    {
        AppUsage app = new AppUsage();
        app.ProcessPath = path;
        return app;
    }

    private static AppUsage App(string path, string title)
    {
        AppUsage app = new AppUsage();
        app.ProcessPath = path;
        app.Title = title;
        return app;
    }

    private static void Run()
    {
        AppActivity.Rules.Clear();
        try
        {
            CategoryIdsFrozen();
            ChainOrderAndAiPlaceholder();
            ChainPriority();
            SourceStringMapping();
            NewCategoryDefaults();
            LegacyDefaultsUnchanged();
            Console.WriteLine("CategoryProviderTests: " + _checks + " 项断言通过");
        }
        finally
        {
            AppActivity.Rules.Clear();
        }
    }

    /// <summary>1. 0~9 号分类完整,0~5 语义冻结。</summary>
    private static void CategoryIdsFrozen()
    {
        string[] expected = new string[]
        {
            "编程", "游戏", "视频", "其他", "未分类", "办公", "科研", "写作", "沟通", "娱乐"
        };
        Equal(AppActivity.Categories.Length, expected.Length, "分类数量应为 10");
        for (int i = 0; i < expected.Length; i++)
        {
            Check(string.Equals(AppActivity.Categories[i], expected[i], StringComparison.Ordinal),
                "第 " + i + " 号分类应为“" + expected[i] + "”,实际=" + AppActivity.Categories[i]);
        }
        Equal(CategoryService.UnclassifiedCategory, 4, "未分类必须固定为 4 号");
    }

    /// <summary>2 + 3. 责任链顺序,以及 AiCache 占位恒 false。</summary>
    private static void ChainOrderAndAiPlaceholder()
    {
        ICategoryProvider[] chain = CategoryService.Chain;
        Equal(chain.Length, 3, "责任链应有三环");
        Check(string.Equals(chain[0].ProviderId, "user_override", StringComparison.Ordinal), "第一环应是用户规则");
        Check(string.Equals(chain[1].ProviderId, "ai_cached", StringComparison.Ordinal), "第二环应是 AI 缓存");
        Check(string.Equals(chain[2].ProviderId, "default_rule", StringComparison.Ordinal), "第三环应是默认规则");

        AiCacheCategoryProvider ai = new AiCacheCategoryProvider();
        CategoryResult result;
        Check(!ai.TryClassify(App(@"C:\tools\code.exe"), out result), "AiCache 在 v3.0 必须恒返回 false");
        Check(result == null, "AiCache 不负责时不应产出结果");
    }

    /// <summary>4. 优先级:用户规则压过默认规则。</summary>
    private static void ChainPriority()
    {
        AppUsage app = App(@"C:\tools\typora.exe", "草稿.md");

        // 无规则:默认规则命中「写作」。
        CategoryResult preset = CategoryService.Classify(app);
        Equal(preset.CategoryId, 7, "无规则时应落到默认的写作(7)");
        Check(preset.Source == CategorySource.DefaultRule, "无规则时来源应为 default_rule");
        Check(string.Equals(preset.ProviderId, "default_rule", StringComparison.Ordinal), "应由默认规则环给出结论");

        // 加一条窗口规则 → 用户规则优先。
        AppActivity.Rules[AppActivity.WindowRuleKey(app)] = 6;
        CategoryResult overridden = CategoryService.Classify(app);
        Equal(overridden.CategoryId, 6, "用户规则应把该窗口改判为科研(6)");
        Check(overridden.Source == CategorySource.UserOverride, "有用户规则时来源应为 user_override");
        Check(string.Equals(overridden.ProviderId, "user_override", StringComparison.Ordinal), "应由用户规则环给出结论");

        // AppActivity.Category 走的是同一条判定链。
        Equal(AppActivity.Category(app), 6, "AppActivity.Category 应与责任链一致");

        AppActivity.Rules.Clear();
        Equal(AppActivity.Category(app), 7, "清掉规则后应回到默认写作");
    }

    /// <summary>4b. 六种既有来源字符串的精确映射。</summary>
    private static void SourceStringMapping()
    {
        Check(CategoryService.MapExplainSource("窗口规则") == CategorySource.UserOverride, "窗口规则 → user_override");
        Check(CategoryService.MapExplainSource("关键词规则") == CategorySource.UserOverride, "关键词规则 → user_override");
        Check(CategoryService.MapExplainSource("应用规则") == CategorySource.UserOverride, "应用规则 → user_override");
        Check(CategoryService.MapExplainSource("站点推测") == CategorySource.DefaultRule, "站点推测 → default_rule");
        Check(CategoryService.MapExplainSource("应用预设") == CategorySource.DefaultRule, "应用预设 → default_rule");
        Check(CategoryService.MapExplainSource("待标注") == CategorySource.None, "待标注 → none");
        Check(CategoryService.MapExplainSource("") == CategorySource.None, "空串 → none");
        Check(CategoryService.MapExplainSource(null) == CategorySource.None, "null → none");

        // 枚举 ↔ 数据库文本 的往返必须与 CHECK 约束逐字一致。
        CategorySource[] all = new CategorySource[]
        {
            CategorySource.None, CategorySource.DefaultRule,
            CategorySource.UserOverride, CategorySource.AiCached
        };
        for (int i = 0; i < all.Length; i++)
        {
            string text = CategoryService.SourceToString(all[i]);
            Check(CategoryService.ParseSource(text) == all[i], "文本往返应一致:" + text);
        }
        Check(string.Equals(CategoryService.SourceToString(CategorySource.None), "none", StringComparison.Ordinal), "none");
        Check(string.Equals(CategoryService.SourceToString(CategorySource.DefaultRule), "default_rule", StringComparison.Ordinal), "default_rule");
        Check(string.Equals(CategoryService.SourceToString(CategorySource.UserOverride), "user_override", StringComparison.Ordinal), "user_override");
        Check(string.Equals(CategoryService.SourceToString(CategorySource.AiCached), "ai_cached", StringComparison.Ordinal), "ai_cached");
        Check(CategoryService.ParseSource("bogus") == CategorySource.None, "非法文本应回落 none");

        // 真实的 Explain 来源字符串必须能被四个值覆盖。
        CategoryResult result = CategoryService.Classify(App(@"C:\tools\typora.exe"));
        Check(result.Source == CategorySource.DefaultRule, "默认规则的来源枚举应正确");
        Check(result.Reason.Length > 0, "应带可读的判定理由");
    }

    /// <summary>5. 6~9 号新类别的默认规则。</summary>
    private static void NewCategoryDefaults()
    {
        Equal(AppActivity.Category(App(@"C:\Apps\matlab.exe")), 6, "matlab → 科研(6)");
        Equal(AppActivity.Category(App(@"C:\Apps\zotero.exe")), 5, "zotero 仍为办公(不得改判)");
        Equal(AppActivity.Category(App(@"C:\Apps\typora.exe")), 7, "typora → 写作(7)");
        Equal(AppActivity.Category(App(@"C:\Apps\slack.exe")), 8, "slack → 沟通(8)");
        Equal(AppActivity.Category(App(@"C:\Apps\steam.exe")), 9, "steam → 娱乐(9)");

        AppUsage paper = App(@"C:\browser\chrome.exe", "论文草稿 - arXiv");
        Equal(AppActivity.Category(paper), 6, "学术站点应判为科研(6)");
    }

    /// <summary>5b. 既有应用的判定一个都不许变。</summary>
    private static void LegacyDefaultsUnchanged()
    {
        Equal(AppActivity.Category(App(@"C:\tools\Code.exe")), 0, "Code.exe 仍为编程");
        Equal(AppActivity.Category(App(@"C:\Games\cs2.exe")), 1, "cs2.exe 仍为游戏");
        Equal(AppActivity.Category(App(@"C:\Apps\vlc.exe")), 2, "vlc.exe 仍为视频");
        Equal(AppActivity.Category(App(@"C:\Windows\explorer.exe")), 3, "explorer.exe 仍为其他");
        Equal(AppActivity.Category(App(@"C:\Apps\wechat.exe")), 5, "wechat.exe 仍为办公(不得改判为沟通)");
        Equal(AppActivity.Category(App(@"C:\Apps\notion.exe")), 5, "notion.exe 仍为办公(不得改判为写作)");
        Equal(AppActivity.Category(App(@"C:\Apps\chatgpt.exe")), 5, "chatgpt.exe 仍为办公");
        Equal(AppActivity.Category(App(@"C:\unknown\never-seen.exe")), 4, "未知应用仍为未分类");
    }
}
