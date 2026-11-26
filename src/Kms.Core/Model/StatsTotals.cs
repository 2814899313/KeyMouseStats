namespace Kms.Core.Model;

/// <summary>全量累计（仅总数，不含细分）。对应 v3 <c>Counters</c>。</summary>
public sealed class StatsTotals
{
    public long Keys { get; set; }
    public long Clicks { get; set; }
    public long Left { get; set; }
    public long Right { get; set; }
    public long Middle { get; set; }
    public long XButtons { get; set; }
    public long Wheel { get; set; }

    /// <summary>Wheel 中属于横向滚动的子集。</summary>
    public long HWheel { get; set; }

    public double MovePx { get; set; }
    public double MoveMeters { get; set; }

    public long HoldCount { get; set; }
    public long HoldDiscarded { get; set; }
    public double HoldTotalMs { get; set; }
    public double HoldMaxMs { get; set; }
    public double HoldActiveSeconds { get; set; }
}
