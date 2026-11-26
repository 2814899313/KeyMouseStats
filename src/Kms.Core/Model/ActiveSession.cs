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
            CrossObservedSeconds = CrossObservedSeconds
        };
        foreach ((string key, double value) in AppSeconds)
        {
            copy.AppSeconds[key] = value;
        }

        return copy;
    }
}
