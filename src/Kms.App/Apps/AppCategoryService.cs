using System.Globalization;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Storage.Apps;

namespace Kms.App.Apps;

public readonly record struct AppCategoryUsage(int AppCount, double ActiveSeconds);

/// <summary>
/// UI 与写线程共享的分类服务。分类定义/规则从 SQLite 小配置表加载，
/// 规则快照原子替换；DbWriter 只调用纯内存的 Resolve，不访问数据库。
/// </summary>
public sealed class AppCategoryService
{
    private readonly AppCategoryStore? _store;
    private readonly StatsData _data;
    private AppCategorySnapshot _snapshot;
    private AppCategoryResolver _resolver;

    public AppCategoryService(
        string? databasePath,
        StatsData data,
        bool loadFromDisk = true)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        if (loadFromDisk && !string.IsNullOrWhiteSpace(databasePath))
        {
            _store = new AppCategoryStore(databasePath);
            _snapshot = _store.Load();
        }
        else
        {
            // 未连接数据库（测试 / 纯文本模式）时，沿用数据内的历史 app_rule。
            _snapshot = new AppCategorySnapshot(
                AppCategoryBuiltins.All,
                _data.AppRules
                    .Select(static rule => new AppCategoryRule(
                        rule.RuleKey,
                        rule.CategoryKey,
                        rule.CategoryId,
                        "legacy_text",
                        0))
                    .ToArray());
        }

        _resolver = new AppCategoryResolver(_snapshot.Categories, _snapshot.Rules);
    }

    /// <summary>分类或规则快照变化后触发；可能在后台线程触发。</summary>
    public event Action? Changed;

    public IReadOnlyList<AppCategoryDefinition> Categories => _resolver.Categories;

    public IReadOnlyList<AppCategoryRule> Rules => _resolver.Rules;

    public AppCategoryMatch Resolve(string? processPath, string? title) =>
        _resolver.Resolve(processPath, title);

    public AppCategoryDefinition ResolveApp(string? processPath) =>
        _resolver.ResolveApp(processPath);

    public bool IsWork(int legacyId) => _resolver.IsWork(legacyId);

    public string NameFor(int legacyId) => _resolver.NameFor(legacyId);

    public string KeyFor(int legacyId) => _resolver.KeyFor(legacyId);

    public AppCategoryDefinition? Find(string categoryKey) =>
        Categories.FirstOrDefault(category =>
            string.Equals(category.CategoryKey, categoryKey, StringComparison.Ordinal));

    public IReadOnlySet<string> GetAppKeys(string categoryKey)
    {
        IEnumerable<string> paths = _data.History.Values
            .SelectMany(static day => day.Apps.Values)
            .Select(static app => app.ProcessPath)
            .Where(static path => !string.IsNullOrWhiteSpace(path));
        return _resolver.GetAppKeys(categoryKey, paths);
    }

    /// <summary>当前时间区间内某工作性质的实际覆盖应用数与时长的分类卡片投影。</summary>
    public AppCategoryUsage GetUsage(
        string categoryKey,
        DateTime start,
        DateTime end)
    {
        HashSet<string> apps = new(StringComparer.Ordinal);
        double seconds = 0;
        for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            string dayKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!_data.History.TryGetValue(dayKey, out DayRecord? day))
            {
                continue;
            }

            foreach (AppUsage app in day.Apps.Values)
            {
                if (!string.Equals(
                    Resolve(app.ProcessPath, app.Title).Category.CategoryKey,
                    categoryKey,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                string key = AppIdentity.NormalizeKey(app.ProcessPath);
                if (key.Length > 0)
                {
                    apps.Add(key);
                }

                seconds += app.ActiveSeconds;
            }
        }

        return new AppCategoryUsage(apps.Count, seconds);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            return;
        }

        AppCategorySnapshot snapshot = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        _snapshot = snapshot;
        Volatile.Write(ref _resolver, new AppCategoryResolver(snapshot.Categories, snapshot.Rules));
        Changed?.Invoke();
    }

    public async Task<AppCategoryDefinition> CreateCategoryAsync(
        string name,
        string colorHex,
        bool isWork,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            throw new InvalidOperationException("分类服务未连接数据库。");
        }

        AppCategoryDefinition category = await _store.CreateAsync(
            name,
            colorHex,
            isWork,
            sortOrder,
            cancellationToken).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        return category;
    }

    public async Task UpdateCategoryAsync(
        string categoryKey,
        string name,
        string colorHex,
        bool isWork,
        int sortOrder,
        CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            return;
        }

        await _store.UpdateAsync(
            categoryKey,
            name,
            colorHex,
            isWork,
            sortOrder,
            cancellationToken).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteCategoryAsync(
        string categoryKey,
        CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            return false;
        }

        AppCategoryDefinition? removed = Find(categoryKey);
        bool deleted = await _store.DeleteAsync(categoryKey, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            AppCategoryDefinition fallback = AppCategoryBuiltins.UncategorizedCategory;
            for (int i = 0; i < _data.AppRules.Count; i++)
            {
                AppRule rule = _data.AppRules[i];
                if (string.Equals(rule.CategoryKey, categoryKey, StringComparison.Ordinal)
                    || (string.IsNullOrEmpty(rule.CategoryKey)
                        && removed is not null
                        && rule.CategoryId == removed.LegacyId))
                {
                    _data.AppRules[i] = new AppRule(
                        rule.RuleKey,
                        fallback.LegacyId,
                        fallback.CategoryKey);
                }
            }

            await ReloadAsync(cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    public async Task SetAppCategoryAsync(
        string appKey,
        string categoryKey,
        CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            return;
        }

        await _store.UpsertRuleAsync(
            "app:" + AppIdentity.NormalizeKey(appKey),
            categoryKey,
            "user_override",
            cancellationToken).ConfigureAwait(false);
        await SyncRuleToTextAsync(
            "app:" + AppIdentity.NormalizeKey(appKey),
            categoryKey).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetTitleRuleAsync(
        string appKey,
        string keyword,
        string categoryKey,
        CancellationToken cancellationToken = default)
    {
        string cleaned = (keyword ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (_store is null || cleaned.Length == 0)
        {
            return;
        }

        string ruleKey = "title:" + AppIdentity.NormalizeKey(appKey) + "\n" + cleaned;
        await _store.UpsertRuleAsync(
            ruleKey,
            categoryKey,
            "user_override",
            cancellationToken).ConfigureAwait(false);
        await SyncRuleToTextAsync(ruleKey, categoryKey).ConfigureAwait(false);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRuleAsync(string ruleKey, CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            return;
        }

        await _store.DeleteRuleAsync(ruleKey, cancellationToken).ConfigureAwait(false);
        _data.AppRules.RemoveAll(rule => string.Equals(rule.RuleKey, ruleKey, StringComparison.Ordinal));
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task SyncRuleToTextAsync(string ruleKey, string categoryKey)
    {
        AppCategoryDefinition? category = Find(categoryKey);
        if (category is null)
        {
            return Task.CompletedTask;
        }

        _data.AppRules.RemoveAll(rule => string.Equals(rule.RuleKey, ruleKey, StringComparison.Ordinal));
        _data.AppRules.Add(new AppRule(ruleKey, category.LegacyId, category.CategoryKey));
        return Task.CompletedTask;
    }
}
