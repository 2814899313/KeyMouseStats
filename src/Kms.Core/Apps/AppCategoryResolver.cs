namespace Kms.Core.Apps;

/// <summary>
/// 纯内存分类责任链。优先级：
/// 用户精确窗口规则 &gt; 用户标题关键词规则 &gt; 用户应用默认规则
/// &gt; 内置启发式 &gt; 未分类。
/// </summary>
public sealed class AppCategoryResolver
{
    private readonly Dictionary<string, AppCategoryDefinition> _byKey;
    private readonly Dictionary<int, AppCategoryDefinition> _byLegacy;
    private readonly Dictionary<string, AppCategoryRule> _rules;

    public AppCategoryResolver(
        IReadOnlyList<AppCategoryDefinition> categories,
        IReadOnlyList<AppCategoryRule> rules)
    {
        Categories = categories.Count == 0
            ? AppCategoryBuiltins.All
            : categories;
        _byKey = Categories.ToDictionary(static category => category.CategoryKey, StringComparer.Ordinal);
        _byLegacy = Categories.ToDictionary(static category => category.LegacyId);
        foreach ((int legacyId, string categoryKey) in AppCategoryBuiltins.LegacyAliases)
        {
            if (!_byLegacy.ContainsKey(legacyId)
                && _byKey.TryGetValue(categoryKey, out AppCategoryDefinition? alias))
            {
                _byLegacy[legacyId] = alias;
            }
        }

        _rules = rules.ToDictionary(static rule => rule.RuleKey, StringComparer.Ordinal);
        Rules = rules;
    }

    public IReadOnlyList<AppCategoryDefinition> Categories { get; }

    public IReadOnlyList<AppCategoryRule> Rules { get; }

    public AppCategoryMatch Resolve(string? processPath, string? title)
    {
        string key = AppIdentity.NormalizeKey(processPath);
        string window = (title ?? "").Trim();
        if (key.Length == 0)
        {
            return Match(AppCategoryBuiltins.UncategorizedCategory, "none", "未识别进程");
        }

        // 历史规则常按 exe 文件名而非完整路径保存；两种键都识别。
        string fileName = System.IO.Path.GetFileName(key);
        string appId = key + "\n" + window;
        AppCategoryRule? windowRule = null;
        if (!_rules.TryGetValue("window:" + appId, out windowRule)
            && fileName.Length > 0)
        {
            _rules.TryGetValue("window:" + fileName + "\n" + window, out windowRule);
        }

        if (windowRule is not null)
        {
            return Match(windowRule, "窗口规则");
        }

        List<string> prefixes = ["title:" + key + "\n"];
        if (fileName.Length > 0 && !string.Equals(fileName, key, StringComparison.Ordinal))
        {
            prefixes.Add("title:" + fileName + "\n");
        }

        AppCategoryRule? bestTitle = null;
        int bestKeywordLength = -1;
        foreach (AppCategoryRule rule in _rules.Values)
        {
            foreach (string prefix in prefixes)
            {
                if (!rule.RuleKey.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string keyword = rule.RuleKey[prefix.Length..];
                if (keyword.Length == 0
                    || keyword.Length <= bestKeywordLength
                    || window.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bestTitle = rule;
                bestKeywordLength = keyword.Length;
            }
        }

        if (bestTitle is not null)
        {
            return Match(bestTitle, "关键词规则");
        }

        AppCategoryRule? appRule = null;
        if (!_rules.TryGetValue("app:" + key, out appRule)
            && fileName.Length > 0)
        {
            _rules.TryGetValue("app:" + fileName, out appRule);
        }

        if (appRule is not null)
        {
            return Match(appRule, "应用规则");
        }

        AppCategoryDefinition heuristic = Heuristic(key);
        return Match(
            heuristic,
            heuristic.CategoryKey == AppCategoryBuiltins.Uncategorized
                ? "none"
                : "default_rule",
            heuristic.CategoryKey == AppCategoryBuiltins.Uncategorized
                ? "没有匹配规则"
                : "按应用名预设");
    }

    public AppCategoryDefinition ResolveApp(string? processPath) =>
        Resolve(processPath, "").Category;

    public bool IsWork(int legacyId) =>
        _byLegacy.TryGetValue(legacyId, out AppCategoryDefinition? category)
        && category.IsWork;

    public string NameFor(int legacyId) =>
        _byLegacy.TryGetValue(legacyId, out AppCategoryDefinition? category)
            ? category.Name
            : "未分类";

    public string KeyFor(int legacyId) =>
        _byLegacy.TryGetValue(legacyId, out AppCategoryDefinition? category)
            ? category.CategoryKey
            : AppCategoryBuiltins.Uncategorized;

    public IReadOnlySet<string> GetAppKeys(
        string categoryKey,
        IEnumerable<string> knownAppPaths)
    {
        HashSet<string> result = new(StringComparer.Ordinal);
        HashSet<string> coveredNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in knownAppPaths)
        {
            string key = AppIdentity.NormalizeKey(path);
            if (key.Length == 0)
            {
                continue;
            }

            string fileName = System.IO.Path.GetFileName(key);
            bool match = ResolveApp(key).CategoryKey == categoryKey;
            if (!match)
            {
                foreach (AppCategoryRule rule in _rules.Values)
                {
                    if (!string.Equals(CategoryKeyOf(rule), categoryKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string target = PathFromRule(rule.RuleKey) ?? "";
                    if (string.Equals(target, key, StringComparison.Ordinal)
                        || (fileName.Length > 0
                            && string.Equals(target, fileName, StringComparison.Ordinal)))
                    {
                        match = true;
                        break;
                    }
                }
            }

            if (match)
            {
                result.Add(key);
                coveredNames.Add(fileName);
            }
        }

        foreach (AppCategoryRule rule in _rules.Values)
        {
            if (!string.Equals(CategoryKeyOf(rule), categoryKey, StringComparison.Ordinal))
            {
                continue;
            }

            string? key = PathFromRule(rule.RuleKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // 已知完整路径已覆盖同名 exe 时，不再追加裸文件名，避免重复计数。
            if (!key.Contains('\\') && !key.Contains('/') && coveredNames.Contains(key))
            {
                continue;
            }

            result.Add(key);
        }

        return result;
    }

    private AppCategoryMatch Match(AppCategoryRule rule, string reason)
    {
        AppCategoryDefinition category =
            _byKey.TryGetValue(rule.CategoryKey, out AppCategoryDefinition? byKey)
                ? byKey
                : _byLegacy.GetValueOrDefault(rule.CategoryId, AppCategoryBuiltins.UncategorizedCategory);
        return new AppCategoryMatch(category, rule.Source, reason);
    }

    private static AppCategoryMatch Match(
        AppCategoryDefinition category,
        string source,
        string reason) => new(category, source, reason);

    private static string CategoryKeyOf(AppCategoryRule rule) =>
        string.IsNullOrWhiteSpace(rule.CategoryKey)
            ? AppCategoryBuiltins.KeyForLegacyId(rule.CategoryId)
            : rule.CategoryKey;

    private static string? PathFromRule(string ruleKey)
    {
        int separator;
        if (ruleKey.StartsWith("app:", StringComparison.Ordinal))
        {
            separator = 4;
        }
        else if (ruleKey.StartsWith("window:", StringComparison.Ordinal))
        {
            separator = 7;
        }
        else if (ruleKey.StartsWith("title:", StringComparison.Ordinal))
        {
            separator = 6;
        }
        else
        {
            return null;
        }

        string value = ruleKey[separator..];
        int newline = value.IndexOf('\n');
        return newline >= 0 ? value[..newline] : value;
    }

    private static AppCategoryDefinition Heuristic(string appKey)
    {
        string name = System.IO.Path.GetFileName(appKey).ToLowerInvariant();
        if (Contains(name, "code.exe|cursor.exe|devenv.exe|pycharm64.exe|idea64.exe|rider64.exe|webstorm64.exe|sublime_text.exe|notepad++.exe|windowsterminal.exe|powershell.exe|pwsh.exe|cmd.exe|codex.exe|antigravity.exe|trae.exe|windsurf.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Coding)!;
        }

        if (Contains(name, "chatgpt.exe|claude.exe|doubao.exe|yuanbao.exe|gemini.exe|perplexity.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Ai)!;
        }

        if (Contains(name, "winword.exe|excel.exe|powerpnt.exe|onenote.exe|outlook.exe|wps.exe|et.exe|wpp.exe|notion.exe|obsidian.exe|acrobat.exe|acrord32.exe|feishu.exe|dingtalk.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Office)!;
        }

        if (Contains(name, "matlab.exe|comsol.exe|spss.exe|stata.exe|stata-mp.exe|origin.exe|imagej.exe|chemdraw.exe|zotero.exe|endnote.exe|mendeley.exe|jabref.exe|rstudio.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Research)!;
        }

        if (Contains(name, "typora.exe|marktext.exe|scrivener.exe|ulysses.exe|zim.exe|writemonkey.exe|ghostwriter.exe|goink.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Writing)!;
        }

        if (Contains(name, "slack.exe|zoom.exe|teams.exe|msteams.exe|lark.exe|wxwork.exe|whatsapp.exe|line.exe|skype.exe|wechat.exe|weixin.exe|qq.exe|discord.exe|telegram.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Communication)!;
        }

        if (Contains(name, "vlc.exe|potplayer64.exe|potplayer.exe|mpv.exe|bilibili.exe|spotify.exe|cloudmusic.exe|neteasecloudmusic.exe|qqmusic.exe|kugou.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Media)!;
        }

        if (Contains(name, "steam.exe|epicgameslauncher.exe|battle.net.exe|cs2.exe|dota2.exe|league of legends.exe|valorant-win64-shipping.exe|genshinimpact.exe|starrail.exe"))
        {
            return AppCategoryBuiltins.ForKey(AppCategoryBuiltins.Game)!;
        }

        return AppCategoryBuiltins.UncategorizedCategory;
    }

    private static bool Contains(string name, string choices)
    {
        foreach (string choice in choices.Split('|'))
        {
            if (string.Equals(choice, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
