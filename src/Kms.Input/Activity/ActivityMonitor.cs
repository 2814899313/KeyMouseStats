using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Kms.Core.Model;

namespace Kms.Input.Activity;

/// <summary>
/// 活动状态采样门面 + v3 纯函数。
///
/// 实例部分负责组合 <see cref="ActivityTracker"/>、系统空闲时间和锁屏 / 休眠 /
/// 会话断开通知；纯函数部分负责 v3 文本格式（活跃时段、小时桶、开始原因）的
/// 解析与编码，以及 GetLastInputInfo。Kms.Input 不引用 Kms.Storage，
/// 因此这些纯函数保留在采集层，供上层按需复用。
///
/// 完整接线方式与 v3 <c>ActivityMonitor.Tick</c> 相同：
/// <list type="number">
/// <item><see cref="ActivityAttribution.Prepare"/> 登记前台快照；</item>
/// <item><see cref="Tick"/> 采样；</item>
/// <item><see cref="ActivityAttribution.Finish"/> 推进快照并统计切换。</item>
/// </list>
/// </summary>
public sealed class ActivityMonitor
{
    public const int WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WM_POWERBROADCAST = 0x0218;

    public const int WTS_CONSOLE_CONNECT = 1;
    public const int WTS_CONSOLE_DISCONNECT = 2;
    public const int WTS_REMOTE_CONNECT = 3;
    public const int WTS_REMOTE_DISCONNECT = 4;
    public const int WTS_SESSION_LOGON = 5;
    public const int WTS_SESSION_LOGOFF = 6;
    public const int WTS_SESSION_LOCK = 7;
    public const int WTS_SESSION_UNLOCK = 8;

    public const int PBT_APMSUSPEND = 4;
    public const int PBT_APMRESUMEAUTOMATIC = 18;
    public const int PBT_APMRESUMESUSPEND = 7;

    public const int MinIdleThresholdSeconds = 10;
    public const int MaxIdleThresholdSeconds = 500;
    public const int DefaultIdleThresholdSeconds = 60;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly ActivityTracker _tracker;
    private readonly Func<uint> _idleMilliseconds;
    private readonly Func<DateTime> _wallClock;
    private readonly Func<long> _monotonicMilliseconds;

    private bool _locked;
    private bool _disconnected;
    private bool _suspended;

    /// <param name="tracker">区间积分器。</param>
    /// <param name="idleMilliseconds">系统空闲毫秒来源；默认调用 <see cref="SystemIdleMilliseconds"/>。</param>
    /// <param name="wallClock">本地壁钟；测试可注入。</param>
    /// <param name="monotonicMilliseconds">单调时钟毫秒；测试可注入。</param>
    public ActivityMonitor(
        ActivityTracker tracker,
        Func<uint>? idleMilliseconds = null,
        Func<DateTime>? wallClock = null,
        Func<long>? monotonicMilliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        _tracker = tracker;
        _idleMilliseconds = idleMilliseconds ?? SystemIdleMilliseconds;
        _wallClock = wallClock ?? (static () => DateTime.Now);
        _monotonicMilliseconds = monotonicMilliseconds ?? NowMonotonicMilliseconds;
    }

    public ActivityTracker Tracker => _tracker;

    /// <summary>
    /// 可选的应用归因器。设置后，锁屏 / 休眠 / 断开 / 输入不可读等通知会自动清空
    /// 前后快照，避免把恢复后的第一段错误归给中断前的窗口。
    /// </summary>
    public ActivityAttribution? Attribution { get; set; }

    /// <summary>最近一次 Tick / 通知后的状态文本（诊断用）。</summary>
    public string Status { get; private set; } = "等待采样";

    /// <summary>锁屏、会话断开或休眠期间为 true。</summary>
    public bool Busy => _locked || _disconnected || _suspended;

    /// <summary>系统通知层面不忙且积分器认为活跃。</summary>
    public bool IsActive => !Busy && _tracker.IsActive;

    /// <summary>
    /// 采样一次。忙时不做任何积分；读取空闲状态失败时复位积分器并返回 false。
    /// </summary>
    public bool Tick(int thresholdSeconds)
    {
        if (Busy)
        {
            Status = _locked || _disconnected ? "已锁屏 / 会话已断开" : "已暂停";
            return false;
        }

        uint idle = _idleMilliseconds();
        if (idle == uint.MaxValue)
        {
            _tracker.Reset(SessionStartReason.InputUnavailable);
            Attribution?.Reset();
            Status = "无法读取空闲状态";
            return false;
        }

        DateTime wall = _wallClock();
        long monotonic = _monotonicMilliseconds();
        bool accepted = _tracker.Sample(wall, monotonic, idle, thresholdSeconds);
        Status = IsActive ? "活跃" : "空闲";
        return accepted;
    }

    /// <summary>处理 WM_WTSSESSION_CHANGE / WM_POWERBROADCAST。未知消息与代码忽略。</summary>
    public void Message(int message, long code)
    {
        if (message is WM_WTSSESSION_CHANGE or WM_POWERBROADCAST)
        {
            Attribution?.Reset();
        }

        if (message == WM_WTSSESSION_CHANGE)
        {
            switch (code)
            {
                case WTS_SESSION_LOCK:
                    _locked = true;
                    _tracker.Break(SessionStartReason.Lock);
                    break;
                case WTS_SESSION_UNLOCK:
                    _locked = false;
                    _tracker.Break(SessionStartReason.Lock);
                    break;
                case WTS_CONSOLE_DISCONNECT:
                case WTS_REMOTE_DISCONNECT:
                case WTS_SESSION_LOGOFF:
                    _disconnected = true;
                    _tracker.Break(SessionStartReason.Disconnect);
                    break;
                case WTS_CONSOLE_CONNECT:
                case WTS_REMOTE_CONNECT:
                case WTS_SESSION_LOGON:
                    _disconnected = false;
                    _tracker.Break(SessionStartReason.Disconnect);
                    break;
            }
        }
        else if (message == WM_POWERBROADCAST)
        {
            switch (code)
            {
                case PBT_APMSUSPEND:
                    _suspended = true;
                    _tracker.Break(SessionStartReason.Suspend);
                    break;
                case PBT_APMRESUMEAUTOMATIC:
                case PBT_APMRESUMESUSPEND:
                    _suspended = false;
                    _tracker.Break(SessionStartReason.Suspend);
                    break;
            }
        }
    }

    /// <summary>结束当前观测并等待下一次全新采样；不改变锁屏 / 断开 / 休眠标记。</summary>
    public void Reset(SessionStartReason reason = SessionStartReason.Startup)
    {
        _tracker.Reset(reason);
        Attribution?.Reset();
        Status = "等待采样";
    }

    /// <summary>
    /// 系统层面的「距离上一次任何输入」毫秒数。读不到时返回 <see cref="uint.MaxValue"/>，
    /// 与 v3 及 <c>HookWatchdog</c> 的哨兵约定一致。
    /// </summary>
    public static uint SystemIdleMilliseconds()
    {
        LastInputInfo info = new() { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return uint.MaxValue;
        }

        return unchecked((uint)Environment.TickCount - info.Time);
    }

    /// <summary>TickCount 差值按 32 位无符号处理，跨越 49.7 天回绕仍为非负。</summary>
    public static uint IdleMilliseconds(uint currentTick, uint lastInputTick)
        => unchecked(currentTick - lastInputTick);

    public static string FormatDuration(double seconds)
    {
        long whole = Math.Max(0, (long)seconds);
        if (whole < 60)
        {
            return whole + " 秒";
        }

        if (whole < 3600)
        {
            return (whole / 60) + " 分 " + (whole % 60) + " 秒";
        }

        return (whole / 3600) + " 时 " + ((whole % 3600) / 60) + " 分";
    }

    /// <summary>固定 24 段 "R" 浮点，v3 <c>active_hours</c> 文本格式。</summary>
    public static string EncodeHours(double[] hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        string[] values = new string[24];
        for (int i = 0; i < 24; i++)
        {
            values[i] = hours[i].ToString("R", Invariant);
        }

        return string.Join(",", values);
    }

    public static void LoadHours(double[] hours, string? text)
    {
        ArgumentNullException.ThrowIfNull(hours);
        string[] values = (text ?? "").Split(',');
        for (int i = 0; i < Math.Min(24, values.Length); i++)
        {
            hours[i] = ParseSeconds(values[i]);
        }
    }

    /// <summary>解析失败、负数、NaN、Infinity 一律为 0（v3 <c>ActivityMonitor.ParseSeconds</c>）。</summary>
    public static double ParseSeconds(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, Invariant, out double seconds)
            && seconds >= 0
            && !double.IsInfinity(seconds)
            ? seconds
            : 0;
    }

    /// <summary>v3 文本活跃段：起点|终点|秒数[|峰值APM|峰值偏移|末段APM|1]。</summary>
    public static string EncodeSession(ActiveSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string value = session.Start.ToString("O", Invariant)
            + "|" + session.End.ToString("O", Invariant)
            + "|" + session.Seconds.ToString("R", Invariant);
        if (session.RateMeasured)
        {
            value += "|" + session.PeakApm.ToString(Invariant)
                + "|" + session.PeakOffsetSeconds.ToString("R", Invariant)
                + "|" + session.LastApm.ToString(Invariant)
                + "|1";
        }

        return value;
    }

    /// <summary>空闲段只有起点|终点|秒数三段。</summary>
    public static string EncodeIdlePeriod(ActiveSession period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return period.Start.ToString("O", Invariant)
            + "|" + period.End.ToString("O", Invariant)
            + "|" + period.Seconds.ToString("R", Invariant);
    }

    /// <summary>v3 <c>session_reason_v1</c>：起点 ticks|原因枚举值。</summary>
    public static string EncodeSessionReason(ActiveSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.Start.Ticks.ToString(Invariant) + "|" + ((int)session.StartReason).ToString(Invariant);
    }

    public static void LoadSession(DayRecord day, string? text) => LoadPeriod(day, text, false);

    public static void LoadIdlePeriod(DayRecord day, string? text) => LoadPeriod(day, text, true);

    /// <summary>
    /// 解析 v3 文本时段。拒绝跨日、反向、负数、超长和未知秒数，保持与 v3 相同的
    /// 「坏行忽略而不是修正」策略。
    /// </summary>
    public static void LoadPeriod(DayRecord day, string? text, bool idle)
    {
        ArgumentNullException.ThrowIfNull(day);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string[] fields = text.Split('|');
        if ((fields.Length != 3 && fields.Length != 7)
            || !DateTime.TryParseExact(
                fields[0],
                "O",
                Invariant,
                DateTimeStyles.RoundtripKind,
                out DateTime start)
            || !DateTime.TryParseExact(
                fields[1],
                "O",
                Invariant,
                DateTimeStyles.RoundtripKind,
                out DateTime end))
        {
            return;
        }

        double seconds = ParseSeconds(fields[2]);
        if (start.Date != day.Date
            || end < start
            || end > day.Date.AddDays(1)
            || seconds <= 0
            || seconds > (end - start).TotalSeconds + 0.001)
        {
            return;
        }

        ActiveSession session = new() { Start = start, End = end, Seconds = seconds };
        if (!idle
            && fields.Length == 7
            && fields[6] == "1"
            && long.TryParse(fields[3], NumberStyles.Integer, Invariant, out long peak)
            && long.TryParse(fields[5], NumberStyles.Integer, Invariant, out long last)
            && peak >= 0
            && last >= 0
            && last <= peak)
        {
            session.RateMeasured = true;
            session.PeakApm = peak;
            session.LastApm = last;
            session.PeakOffsetSeconds = Math.Min(seconds, ParseSeconds(fields[4]));
        }

        (idle ? day.IdlePeriods : day.Sessions).Add(session);
    }

    public static void LoadSessionReason(DayRecord day, string? text)
    {
        ArgumentNullException.ThrowIfNull(day);
        string[] fields = (text ?? "").Split('|');
        if (fields.Length != 2
            || !long.TryParse(fields[0], NumberStyles.Integer, Invariant, out long ticks)
            || !int.TryParse(fields[1], NumberStyles.Integer, Invariant, out int reason)
            || !Enum.IsDefined(typeof(SessionStartReason), reason))
        {
            return;
        }

        foreach (ActiveSession session in day.Sessions)
        {
            if (session.Start.Ticks == ticks)
            {
                session.StartReason = (SessionStartReason)reason;
                return;
            }
        }
    }

    /// <summary>会话开始原因的中文说明，与 v3 完全一致。</summary>
    public static string SessionReason(ActiveSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.StartReason switch
        {
            SessionStartReason.Startup => "程序启动 / 重新计时",
            SessionStartReason.Idle => "空闲后恢复",
            SessionStartReason.ObservationGap => "采样中断后恢复",
            SessionStartReason.ClockChange => "时钟异常后恢复",
            SessionStartReason.Lock => "解锁后恢复",
            SessionStartReason.Suspend => "休眠后恢复",
            SessionStartReason.Disconnect => "会话重连后恢复",
            SessionStartReason.Settings => "修改空闲阈值",
            SessionStartReason.InputUnavailable => "输入状态读取恢复",
            SessionStartReason.NewDay => "跨日分段",
            _ => "旧记录未标注"
        };
    }

    /// <summary>
    /// 修复旧版本已知的「空闲段末尾幽灵活跃段」：只删除 ≤0.5 ms、紧接空闲段终点、
    /// 且原区间本身不超过 5 ms 的记录；总量与真实短活跃段保留。
    /// </summary>
    public static int RemoveLegacyPhantomSessions(DayRecord day)
    {
        ArgumentNullException.ThrowIfNull(day);
        HashSet<DateTime> idleEnds = [];
        foreach (ActiveSession idle in day.IdlePeriods)
        {
            idleEnds.Add(idle.End);
        }

        return day.Sessions.RemoveAll(session =>
            session.Seconds > 0
            && session.Seconds <= 0.0005
            && session.End > session.Start
            && (session.End - session.Start).Ticks <= 5000
            && idleEnds.Contains(session.Start));
    }

    /// <summary>空闲阈值归一到 v3 的 [10, 500] 秒；过小回落默认 60。</summary>
    public static int NormalizeIdleThreshold(long seconds)
    {
        if (seconds < MinIdleThresholdSeconds)
        {
            return DefaultIdleThresholdSeconds;
        }

        return seconds > MaxIdleThresholdSeconds ? MaxIdleThresholdSeconds : (int)seconds;
    }

    private static long NowMonotonicMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

#pragma warning disable SYSLIB1054 // 保留 v3 的 DllImport；GetLastInputInfo 无复杂封送。
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
#pragma warning restore SYSLIB1054
}
