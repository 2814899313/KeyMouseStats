// ============================================================================
//  键鼠统计 v3.0  Milestone 1 / Step 5  CategoryService.cs
//  分类扩展链:把「这个窗口属于什么用途」从 AppActivity 里的硬编码判断，
//  提升为可插拔的责任链,为 3.2 桑基图与 4.1 BYOK 批量归类预留挂点。
//
//  责任链顺序(前者优先):
//    UserOverrideCategoryProvider  用户自定义规则(窗口/关键词/应用规则)
//    AiCacheCategoryProvider       v3.0 占位,恒返回 false;4.1 接 AI 缓存
//    DefaultRuleCategoryProvider   内置预设 + 站点推测(包装 AppActivity.Explain)
//
//  category_source 是**四值封闭枚举**,与数据库 CHECK 约束逐字对应:
//    'none' | 'default_rule' | 'user_override' | 'ai_cached'
//  未来接 AI 时只往 'ai_cached' 写,**表结构一字不改**。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;

namespace KeyMouseStats
{
    /// <summary>分类来源。数值与数据库 category_source 字符串一一对应。</summary>
    internal enum CategorySource
    {
        None = 0,
        DefaultRule = 1,
        UserOverride = 2,
        AiCached = 3
    }

    /// <summary>一次分类判定的结果。</summary>
    internal sealed class CategoryResult
    {
        internal int CategoryId = CategoryService.UnclassifiedCategory;
        internal CategorySource Source = CategorySource.None;
        internal string Reason = "";
        internal string ProviderId = "";
    }

    /// <summary>分类提供者。返回 false 表示「我不负责」,由链上的下一环接手。</summary>
    internal interface ICategoryProvider
    {
        string ProviderId { get; }
        bool TryClassify(AppUsage app, out CategoryResult result);
    }

    /// <summary>用户自定义规则(最高优先级)。直接复用 AppActivity.Explain 的规则分支。</summary>
    internal sealed class UserOverrideCategoryProvider : ICategoryProvider
    {
        public string ProviderId { get { return "user_override"; } }

        public bool TryClassify(AppUsage app, out CategoryResult result)
        {
            result = null;
            if (app == null) return false;

            AppActivity.Classification classification = AppActivity.Explain(app);
            if (classification == null) return false;
            if (!CategoryService.IsUserRuleSource(classification.Source)) return false;

            result = new CategoryResult();
            result.CategoryId = classification.Category;
            result.Source = CategorySource.UserOverride;
            result.Reason = classification.Reason;
            result.ProviderId = ProviderId;
            return true;
        }
    }

    /// <summary>
    /// AI 归类缓存(v3.0 占位)。4.1 接入 BYOK 后,这里改为读 ai 缓存表;
    /// 现在恒返回 false,责任链自然落到默认规则,行为与 v2.4.0 完全一致。
    /// </summary>
    internal sealed class AiCacheCategoryProvider : ICategoryProvider
    {
        public string ProviderId { get { return "ai_cached"; } }

        public bool TryClassify(AppUsage app, out CategoryResult result)
        {
            result = null;
            return false;
        }
    }

    /// <summary>内置预设 + 站点推测。责任链的最后一环,保证一定给出结论。</summary>
    internal sealed class DefaultRuleCategoryProvider : ICategoryProvider
    {
        public string ProviderId { get { return "default_rule"; } }

        public bool TryClassify(AppUsage app, out CategoryResult result)
        {
            result = null;
            if (app == null) return false;

            AppActivity.Classification classification = AppActivity.Explain(app);
            if (classification == null) return false;

            result = new CategoryResult();
            result.CategoryId = classification.Category;
            result.Source = CategoryService.MapExplainSource(classification.Source);
            result.Reason = classification.Reason;
            result.ProviderId = ProviderId;
            return true;
        }
    }

    /// <summary>分类服务的静态门面。产品与 DbWriter 只依赖它。</summary>
    internal static class CategoryService
    {
        /// <summary>未分类 = 0~5 里的第 4 号。冻结,不可改。</summary>
        internal const int UnclassifiedCategory = 4;

        private static readonly ICategoryProvider[] Providers = new ICategoryProvider[]
        {
            new UserOverrideCategoryProvider(),
            new AiCacheCategoryProvider(),
            new DefaultRuleCategoryProvider()
        };

        /// <summary>责任链(只读,顺序即优先级)。</summary>
        internal static ICategoryProvider[] Chain { get { return Providers; } }

        /// <summary>按责任链判定。永不返回 null。</summary>
        internal static CategoryResult Classify(AppUsage app)
        {
            CategoryResult result = null;
            for (int i = 0; i < Providers.Length; i++)
            {
                if (Providers[i].TryClassify(app, out result) && result != null) return result;
            }
            result = new CategoryResult();
            result.CategoryId = UnclassifiedCategory;
            result.Source = CategorySource.None;
            result.Reason = "责任链未给出结论";
            result.ProviderId = "none";
            return result;
        }

        /// <summary>
        /// AppActivity.Explain 的六种来源字符串 → 四值枚举的精确映射。
        /// 未列出的(例如 "待标注")一律落到 None。
        /// </summary>
        internal static CategorySource MapExplainSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return CategorySource.None;
            switch (source)
            {
                case "窗口规则":
                case "关键词规则":
                case "应用规则":
                    return CategorySource.UserOverride;
                case "站点推测":
                case "应用预设":
                    return CategorySource.DefaultRule;
                default:
                    return CategorySource.None;
            }
        }

        internal static bool IsUserRuleSource(string source)
        {
            return MapExplainSource(source) == CategorySource.UserOverride;
        }

        /// <summary>枚举 → 数据库 category_source 文本(与 CHECK 约束逐字一致)。</summary>
        internal static string SourceToString(CategorySource source)
        {
            switch (source)
            {
                case CategorySource.DefaultRule: return "default_rule";
                case CategorySource.UserOverride: return "user_override";
                case CategorySource.AiCached: return "ai_cached";
                default: return "none";
            }
        }

        /// <summary>数据库文本 → 枚举。非法值回落 None。</summary>
        internal static CategorySource ParseSource(string text)
        {
            if (string.IsNullOrEmpty(text)) return CategorySource.None;
            switch (text)
            {
                case "default_rule": return CategorySource.DefaultRule;
                case "user_override": return CategorySource.UserOverride;
                case "ai_cached": return CategorySource.AiCached;
                default: return CategorySource.None;
            }
        }
    }
}
