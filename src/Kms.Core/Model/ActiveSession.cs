namespace Kms.Core.Model;

public enum SessionStartReason
{
    Legacy,
    Startup,
    Idle,
    ObservationGap,
    ClockChange,
    Lock,
    Suspend,
    Disconnect,
    Settings,
    InputUnavailable,
    NewDay
}

/// <summary>一次活跃段或空闲段。对应 v3 <c>ActiveSession</c>。</summary>
public sealed class ActiveSession
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public SessionStartReason StartReason { get; set; }
    public double Seconds { get; set; }
    public bool RateMeasured { get; set; }
    public long PeakApm { get; set; }
    public long LastApm { get; set; }
    public double PeakOffsetSeconds { get; set; }
    public double CrossObservedSeconds { get; set; }

    /// <summary>SQLite v2 会话补全的归属信息；v13 文本会话没有这些列时为空。</summary>
    public string ProcessPath { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public int CategoryId { get; set; } = -1;
    public double MovePx { get; set; }

    public Dictionary<string, double> AppSeconds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ActiveSession Copy()
    {
        ActiveSession copy = new()
        {
            Start = Start,
            End = End,
            StartReason = StartReason,
            Seconds = Seconds,
            RateMeasured = RateMeasured,
            PeakApm = PeakApm,
            LastApm = LastApm,
            PeakOffsetSeconds = PeakOffsetSeconds,
            CrossObservedSeconds = CrossObservedSeconds,
            ProcessPath = ProcessPath,
            ProcessName = ProcessName,
            WindowTitle = WindowTitle,
            CategoryId = CategoryId,
            MovePx = MovePx
        };
        foreach ((string key, double value) in AppSeconds)
        {
            copy.AppSeconds[key] = value;
        }

        return copy;
    }
}
