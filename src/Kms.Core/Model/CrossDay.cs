namespace Kms.Core.Model;

/// <summary>交叉遥测（Raw Input 独立于钩子的观测）。对应 v3 <c>CrossDay</c>。</summary>
public sealed class CrossDay
{
    public double[] ObservedHours { get; } = new double[24];
    public Dictionary<string, double[]> AppHours { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>left / right / up / down 的绝对轴计数。</summary>
    public double[] Direction { get; } = new double[4];

    public double PathCounts { get; set; }
    public double DragCounts { get; set; }
    public long Packets { get; set; }
    public long CalibratedPackets { get; set; }
    public long Flicks { get; set; }
    public long Reversals { get; set; }

    /// <summary>Raw Input 独立累加的点击；与 <see cref="DayRecord.Clicks"/> 不同源。</summary>
    public long Clicks { get; set; }

    public long MoveClicks { get; set; }

    public void AddFrom(CrossDay other)
    {
        for (int i = 0; i < 24; i++)
        {
            ObservedHours[i] += other.ObservedHours[i];
        }

        foreach ((string path, double[] hours) in other.AppHours)
        {
            if (!AppHours.TryGetValue(path, out double[]? target))
            {
                target = new double[24];
                AppHours[path] = target;
            }

            for (int i = 0; i < 24; i++)
            {
                target[i] += hours[i];
            }
        }

        for (int i = 0; i < 4; i++)
        {
            Direction[i] += other.Direction[i];
        }

        PathCounts += other.PathCounts;
        DragCounts += other.DragCounts;
        Packets += other.Packets;
        CalibratedPackets += other.CalibratedPackets;
        Flicks += other.Flicks;
        Reversals += other.Reversals;
        Clicks += other.Clicks;
        MoveClicks += other.MoveClicks;
    }
}
