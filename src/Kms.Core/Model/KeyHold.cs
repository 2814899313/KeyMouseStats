namespace Kms.Core.Model;

/// <summary>单个按键的按住时长聚合。对应 v3 <c>KeyHold</c>。</summary>
public sealed class KeyHold
{
    public long Count { get; set; }
    public double TotalMs { get; set; }
    public double MaxMs { get; set; }

    public double Mean => Count > 0 ? TotalMs / Count : double.NaN;

    public void Add(double milliseconds)
    {
        Count++;
        TotalMs += milliseconds;
        if (milliseconds > MaxMs)
        {
            MaxMs = milliseconds;
        }
    }
}
