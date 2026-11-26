using System.Diagnostics;
using Kms.Core;
using Kms.Core.Time;

namespace Kms.Storage.Sqlite;

/// <summary>
/// <c>Stopwatch</c> 时刻 ↔ 墙钟毫秒 的换算锚点。
/// 锚点使用 <see cref="UnixTime"/>（v3 <c>EventClock.cs</c> 口径），禁止用 DateTime ticks 做时间基准。
/// </summary>
public sealed class EventClock
{
    private readonly long _anchorMs;
    private readonly long _anchorTicks;
    private readonly double _ticksPerMs;

    public EventClock()
        : this(SystemClock.Instance)
    {
    }

    public EventClock(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        _anchorTicks = Stopwatch.GetTimestamp();
        _anchorMs = UnixTime.NowMs(clock);
        _ticksPerMs = Stopwatch.Frequency / 1000.0;
    }

    /// <summary>
    /// 把 Stopwatch 时刻换成 Unix 毫秒。
    /// 四舍五入而不是截断：截断会让 ms↔ticks 往返产生 ±1 ms 的量化误差，
    /// 把 IKI 的 500/1000/1500 变成 501/1000/1499。
    /// </summary>
    public long ToMs(long ticks) =>
        _anchorMs + (long)Math.Round((ticks - _anchorTicks) / _ticksPerMs);

    /// <summary>把 Unix 毫秒换回 Stopwatch 时刻。测试用它构造可控时间线。</summary>
    public long TicksForMs(long epochMs) =>
        _anchorTicks + (long)Math.Round((epochMs - _anchorMs) * _ticksPerMs);
}

/// <summary><see cref="DbWriter"/> 的打开选项。</summary>
public sealed class DbWriterOptions
{
    /// <summary>打开时是否执行 <see cref="DbWriter.SelfHeal"/>。测试可关闭。</summary>
    public bool RunSelfHeal { get; init; } = true;

    /// <summary>墙钟来源；测试可注入假时钟。</summary>
    public IClock Clock { get; init; } = SystemClock.Instance;

    /// <summary>写入 <c>meta.app_version</c> 的版本号（已存在时不覆盖）。</summary>
    public string AppVersion { get; init; } = BuildInfo.Version;

    /// <summary>诊断输出；为空时写数据库同目录的 <c>db-writer.log</c>。</summary>
    public Action<string>? Diagnostic { get; init; }
}

/// <summary>前台窗口解析结果。对应 v3 <c>ForegroundApp.WindowInfo</c> 里写线程用到的三个字段。</summary>
public sealed class WindowTarget
{
    public string ProcessPath { get; init; } = "";

    public string ProcessName { get; init; } = "";

    public string WindowTitle { get; init; } = "";

    public static WindowTarget Empty { get; } = new();
}

/// <summary>数据库 <c>category_source</c> 的四个封闭取值，与 CHECK 约束逐字一致。</summary>
public static class CategorySources
{
    public const string None = "none";
    public const string DefaultRule = "default_rule";
    public const string UserOverride = "user_override";
    public const string AiCached = "ai_cached";

    /// <summary>非法值一律回落 <c>none</c>，保证不会触发 CHECK 约束。</summary>
    public static string Normalize(string? value) => value switch
    {
        DefaultRule => DefaultRule,
        UserOverride => UserOverride,
        AiCached => AiCached,
        _ => None
    };
}

/// <summary>一次窗口分类结果。v4 暂未移植 v3 的分类责任链，宿主可注入分类器。</summary>
public readonly record struct WindowCategory(
    int CategoryId,
    string Source = CategorySources.None,
    string Reason = "")
{
    /// <summary>v3 <c>CategoryService.UnclassifiedCategory</c>：0~5 里的第 4 号。</summary>
    public const int UnclassifiedCategory = 4;

    public static WindowCategory Unclassified { get; } =
        new(UnclassifiedCategory, CategorySources.None, "");
}

/// <summary>一段正在进行的前台窗口会话。</summary>
internal sealed class LiveSession
{
    public long Id;
    public long StartMs;
    public long EndMs;
    public string ProcessPath = "";
    public string ProcessName = "";
    public string WindowTitle = "";
    public long KeyCount;
    public long Left;
    public long Right;
    public long Middle;
    public long XClicks;
    public long Wheel;
    public double MovePx;

    /// <summary>连续活跃时长（相邻输入间隔 ≤ ActiveGapMs 才累加，挂机不计）。</summary>
    public long ActiveMs;

    public int CategoryId = WindowCategory.UnclassifiedCategory;
    public string CategorySource = CategorySources.None;
}

/// <summary>一段连续输入节律。</summary>
internal sealed class LiveBurst
{
    public long StartMs;
    public long EndMs;
    public long Count;
    public long Backspaces;
    public long Switches;
    public double Mean;
    public double M2;
    public bool HasPrevious;
    public long PreviousKeyMs;
    public string ProcessPath = "";
}

/// <summary>一个批次的聚合结果。字典全部复用，避免每批都 new。</summary>
internal sealed class WriteBatch
{
    public string Day = "";
    public long Keys;
    public long Left;
    public long Right;
    public long Middle;
    public long XClicks;
    public long Wheel;
    public double MovePx;
    public long FirstMs;
    public long LastMs;

    public readonly Dictionary<int, long> HourKeys = [];
    public readonly Dictionary<int, long> HourClicks = [];
    public readonly Dictionary<int, long> Vk = [];
    public readonly Dictionary<long, long> Cells = [];
    public readonly Dictionary<string, WindowAgg> Windows = new(StringComparer.Ordinal);
    public readonly List<LiveSession> ClosedSessions = [];
    public readonly List<LiveBurst> Bursts = [];

    public long Clicks => Left + Right + Middle + XClicks;

    public bool IsEmpty =>
        Keys == 0 && Left == 0 && Right == 0 && Middle == 0 && XClicks == 0
        && Wheel == 0 && MovePx <= 0 && ClosedSessions.Count == 0 && Bursts.Count == 0
        && HourKeys.Count == 0 && HourClicks.Count == 0 && Vk.Count == 0
        && Cells.Count == 0 && Windows.Count == 0;

    public void Reset(string day)
    {
        Day = day;
        Keys = 0;
        Left = 0;
        Right = 0;
        Middle = 0;
        XClicks = 0;
        Wheel = 0;
        MovePx = 0;
        FirstMs = 0;
        LastMs = 0;
        HourKeys.Clear();
        HourClicks.Clear();
        Vk.Clear();
        Cells.Clear();
        Windows.Clear();
        ClosedSessions.Clear();
        Bursts.Clear();
    }
}

/// <summary>按 (日, 进程, 窗口标题) 聚合的窗口行。</summary>
internal sealed class WindowAgg
{
    public string ProcessPath = "";
    public string ProcessName = "";
    public string Title = "";
    public string SemanticTag = "";
    public long Keys;
    public long Left;
    public long Right;
    public long Middle;
    public long XClicks;
    public long Wheel;
    public long FirstMs;
    public long LastMs;
    public int CategoryId = WindowCategory.UnclassifiedCategory;
    public string CategorySource = CategorySources.None;

    public long Clicks => Left + Right + Middle + XClicks;
}
