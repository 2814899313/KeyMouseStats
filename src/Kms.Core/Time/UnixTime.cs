namespace Kms.Core.Time;

/// <summary>Unix 毫秒基准（1970-01-01T00:00:00Z）。</summary>
public static class UnixTime
{
    /// <summary>0001-01-01 到 1970-01-01 的毫秒数；迁移与历史修复必须使用它。</summary>
    public const long YearOneToUnixMs = 62_135_596_800_000L;

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>把 DateTime 换成 Unix 毫秒；Local 先转 UTC，Unspecified 按 UTC 解释。</summary>
    public static long ToUnixMs(DateTime value)
    {
        if (value.Kind == DateTimeKind.Local)
        {
            value = value.ToUniversalTime();
        }
        else if (value.Kind == DateTimeKind.Unspecified)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return (long)(value - Epoch).TotalMilliseconds;
    }

    public static long NowMs(IClock clock) => ToUnixMs(clock.UtcNow);

    /// <summary>Unix 毫秒 → UTC。</summary>
    public static DateTime FromUnixMs(long milliseconds) =>
        Epoch.AddMilliseconds(milliseconds);

    /// <summary>Unix 毫秒 → 本地时间。仅供诊断/迁移测试使用。</summary>
    public static DateTime ToLocal(long milliseconds) =>
        Epoch.AddMilliseconds(milliseconds).ToLocalTime();
}

/// <summary>可注入时钟；测试用假时钟，生产用系统时钟。</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>单调时钟（Stopwatch tick），用于按住时长与组合键。</summary>
    long MonotonicTicks { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTime UtcNow => DateTime.UtcNow;

    public long MonotonicTicks => System.Diagnostics.Stopwatch.GetTimestamp();
}
