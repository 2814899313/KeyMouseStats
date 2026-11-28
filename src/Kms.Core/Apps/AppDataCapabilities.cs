namespace Kms.Core.Apps;

/// <summary>按应用维度可精确提供的数据能力。位掩码便于一次表达多个维度。</summary>
[Flags]
public enum AppDataCapability
{
    None = 0,
    AppTotals = 1 << 0,
    Hourly = 1 << 1,
    KeyDetail = 1 << 2,
    Heatmap = 1 << 3,
    Focus = 1 << 4,
    Chars = 1 << 5,
    LiveRate = 1 << 6,
    All = AppTotals | Hourly | KeyDetail | Heatmap | Focus | Chars | LiveRate
}

/// <summary>
/// 能力描述：Exact 表示当前区间有真实按应用数据；Pending 表示该维度尚未采集到
/// 或仍只有全局数据。查询结果绝不把 Pending 维度伪造成 0。
/// </summary>
public sealed record AppDataCapabilities(
    AppDataCapability Exact,
    AppDataCapability Pending,
    string Note = "")
{
    public static AppDataCapabilities AllExact { get; } =
        new(AppDataCapability.All, AppDataCapability.None);

    public bool IsExact(AppDataCapability capability) =>
        (Exact & capability) == capability;

    public bool IsPending(AppDataCapability capability) =>
        (Pending & capability) == capability;

    public AppDataCapabilities WithPending(AppDataCapability capability, string note)
    {
        AppDataCapability exact = Exact & ~capability;
        AppDataCapability pending = Pending | capability;
        return new AppDataCapabilities(exact, pending, note);
    }

    public string Describe()
    {
        if (Pending == AppDataCapability.None)
        {
            return "全部维度可按应用精确查询";
        }

        return string.IsNullOrWhiteSpace(Note)
            ? "部分维度尚无按应用明细"
            : Note;
    }
}
