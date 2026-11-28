namespace Kms.Core.Model;

public sealed record AppRule(string RuleKey, int CategoryId, string CategoryKey = "");

/// <summary>
/// 数据上下文接缝。v3 的静态 <c>Store</c> 拆成实例后，测试可以各建一份，互不影响。
/// </summary>
public interface IDataContext
{
    IDictionary<string, DayRecord> History { get; }

    StatsTotals Totals { get; }
}

/// <summary>一次解析得到的完整 v13 数据；文本后端与 SQLite 后端都填这个结构。</summary>
public sealed class StatsData : IDataContext
{
    public int FormatVersion { get; set; } = 10;

    public Dictionary<string, DayRecord> History { get; } = new(StringComparer.Ordinal);

    IDictionary<string, DayRecord> IDataContext.History => History;

    public StatsTotals Totals { get; } = new();

    public Dictionary<string, MonthArchive> Archives { get; } = new(StringComparer.Ordinal);

    public List<AppRule> AppRules { get; } = new();

    public List<KeyboardProfile> Keyboards { get; } = new();

    public List<MouseProfile> Mice { get; } = new();

    /// <summary>全局简单设置的最后一次取值（idle_threshold、art_theme 等）。</summary>
    public Dictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>全局键值行，保留出现顺序；用于 v13 往返写入。</summary>
    public List<KeyValuePair<string, string>> GlobalLines { get; } = new();

    /// <summary>解析器不认识的全局行，原样保留。</summary>
    public List<string> UnknownGlobalLines { get; } = new();

    public DayRecord GetOrCreate(DateTime date)
    {
        string key = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (!History.TryGetValue(key, out DayRecord? day))
        {
            day = new DayRecord { Date = date.Date };
            History[key] = day;
        }

        return day;
    }
}
