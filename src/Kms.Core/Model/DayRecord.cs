namespace Kms.Core.Model;

/// <summary>单日完整记录。字段与 v3 <c>DayRecord</c> 一一对应，不增不减。</summary>
public sealed class DayRecord
{
    public DateTime Date { get; set; }
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

    public long[] HourKeys { get; } = new long[24];
    public long[] HourClicks { get; } = new long[24];

    /// <summary>屏幕点击热区采样格（key = ClickHeat.CellKey）与次数，只保留最近 30 天。</summary>
    public Dictionary<int, long> ClickCells { get; } = new();

    /// <summary>三通道热区：key = (channel &lt;&lt; 32) | cellKey。</summary>
    public Dictionary<long, long> ClickCellsV1 { get; } = new();

    public Dictionary<int, long> KeyCounts { get; } = new();
    public Dictionary<int, long> PhysicalKeys { get; } = new();
    public Dictionary<int, long> PhysicalVks { get; } = new();
    public Dictionary<string, long> ComboCounts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, AppUsage> Apps { get; } = new(StringComparer.Ordinal);

    public double ActiveSeconds { get; set; }
    public double IdleSeconds { get; set; }
    public double[] ActiveHours { get; } = new double[24];
    public List<ActiveSession> Sessions { get; } = new();
    public List<ActiveSession> IdlePeriods { get; } = new();

    public double AppObservedSeconds { get; set; }
    public long AppSwitches { get; set; }
    public long PeakApm { get; set; }

    public CrossDay Cross { get; } = new();

    public Dictionary<int, KeyHold> Holds { get; } = new();

    public long HoldCount { get; set; }
    public long HoldDiscarded { get; set; }
    public double HoldTotalMs { get; set; }
    public double HoldMaxMs { get; set; }
    public double HoldActiveSeconds { get; set; }

    /// <summary>按住时长 8 档直方图（最后一档为 ≥5 秒）。</summary>
    public long[] HoldBuckets { get; } = new long[8];

    public bool IsEmpty =>
        Keys == 0 &&
        Clicks == 0 &&
        Wheel == 0 &&
        HWheel == 0 &&
        MovePx < 0.5 &&
        MoveMeters == 0 &&
        ActiveSeconds == 0 &&
        IdleSeconds == 0 &&
        Cross.Packets == 0 &&
        Cross.Clicks == 0 &&
        HoldCount == 0 &&
        HoldActiveSeconds == 0;

    /// <summary>合并另一份同日记录（多机合并的「相加」策略）。</summary>
    public void AddFrom(DayRecord other)
    {
        Keys += other.Keys;
        Clicks += other.Clicks;
        Left += other.Left;
        Right += other.Right;
        Middle += other.Middle;
        XButtons += other.XButtons;
        Wheel += other.Wheel;
        HWheel += other.HWheel;
        MovePx += other.MovePx;
        MoveMeters += other.MoveMeters;
        ActiveSeconds += other.ActiveSeconds;
        IdleSeconds += other.IdleSeconds;
        AppObservedSeconds += other.AppObservedSeconds;
        AppSwitches += other.AppSwitches;
        if (other.PeakApm > PeakApm)
        {
            PeakApm = other.PeakApm;
        }

        for (int i = 0; i < 24; i++)
        {
            HourKeys[i] += other.HourKeys[i];
            HourClicks[i] += other.HourClicks[i];
            ActiveHours[i] += other.ActiveHours[i];
        }

        AddCounts(KeyCounts, other.KeyCounts);
        AddCounts(PhysicalKeys, other.PhysicalKeys);
        AddCounts(PhysicalVks, other.PhysicalVks);

        foreach ((int key, long value) in other.ClickCells)
        {
            ClickCells[key] = ClickCells.GetValueOrDefault(key) + value;
        }

        foreach ((long key, long value) in other.ClickCellsV1)
        {
            ClickCellsV1[key] = ClickCellsV1.GetValueOrDefault(key) + value;
        }

        foreach ((string key, long value) in other.ComboCounts)
        {
            ComboCounts[key] = ComboCounts.GetValueOrDefault(key) + value;
        }

        foreach ((string key, AppUsage app) in other.Apps)
        {
            if (!Apps.TryGetValue(key, out AppUsage? target))
            {
                target = new AppUsage { ProcessPath = app.ProcessPath, Title = app.Title };
                Apps[key] = target;
            }

            target.Keys += app.Keys;
            target.Clicks += app.Clicks;
            target.Wheel += app.Wheel;
            target.ActiveSeconds += app.ActiveSeconds;
            target.HasKeyGroups |= app.HasKeyGroups;
            for (int i = 0; i < 6; i++)
            {
                target.KeyGroups[i] += app.KeyGroups[i];
            }
        }

        foreach ((int key, KeyHold hold) in other.Holds)
        {
            if (!Holds.TryGetValue(key, out KeyHold? target))
            {
                target = new KeyHold();
                Holds[key] = target;
            }

            target.Count += hold.Count;
            target.TotalMs += hold.TotalMs;
            if (hold.MaxMs > target.MaxMs)
            {
                target.MaxMs = hold.MaxMs;
            }
        }

        Sessions.AddRange(other.Sessions);
        IdlePeriods.AddRange(other.IdlePeriods);
        Cross.AddFrom(other.Cross);
        HoldCount += other.HoldCount;
        HoldDiscarded += other.HoldDiscarded;
        HoldTotalMs += other.HoldTotalMs;
        if (other.HoldMaxMs > HoldMaxMs)
        {
            HoldMaxMs = other.HoldMaxMs;
        }

        HoldActiveSeconds += other.HoldActiveSeconds;
        for (int i = 0; i < HoldBuckets.Length; i++)
        {
            HoldBuckets[i] += other.HoldBuckets[i];
        }
    }

    /// <summary>
    /// 以日总数 keys/clicks 为准，按原分布等比缩放到小时桶与按键桶，再补齐舍入余数。
    /// 旧格式只写日总数、缺派生桶时不能凭空造数据；只有原桶合计 &gt; 0 且不一致才修。
    /// </summary>
    public bool RepairDerivedCounts(bool repairButtons)
    {
        bool changed = false;
        if (Keys > 0)
        {
            changed |= ScaleBuckets(HourKeys, Keys);
            changed |= ScaleCounts(KeyCounts, Keys);
        }

        if (Clicks > 0)
        {
            changed |= ScaleBuckets(HourClicks, Clicks);
            if (repairButtons)
            {
                long buttons = Left + Right + Middle + XButtons;
                if (buttons < Clicks)
                {
                    Left += Clicks - buttons;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool ScaleBuckets(long[] values, long total)
    {
        long current = 0;
        int largest = 0;
        for (int i = 0; i < values.Length; i++)
        {
            current += values[i];
            if (values[i] > values[largest])
            {
                largest = i;
            }
        }

        if (current <= 0 || current == total)
        {
            return false;
        }

        double factor = total / (double)current;
        long assigned = 0;
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Math.Max(0, (long)Math.Round(values[i] * factor));
            assigned += values[i];
        }

        values[largest] = Math.Max(0, values[largest] + total - assigned);
        return true;
    }

    private static bool ScaleCounts(Dictionary<int, long> values, long total)
    {
        if (values.Count == 0)
        {
            return false;
        }

        List<int> keys = [.. values.Keys];
        keys.Sort();
        long current = 0;
        int largest = keys[0];
        foreach (int key in keys)
        {
            current += values[key];
            if (values[key] > values[largest])
            {
                largest = key;
            }
        }

        if (current <= 0 || current == total)
        {
            return false;
        }

        double factor = total / (double)current;
        long assigned = 0;
        foreach (int key in keys)
        {
            values[key] = Math.Max(0, (long)Math.Round(values[key] * factor));
            assigned += values[key];
        }

        if (values.TryGetValue(largest, out long largestValue))
        {
            values[largest] = Math.Max(0, largestValue + total - assigned);
        }

        return true;
    }

    private static void AddCounts(Dictionary<int, long> target, Dictionary<int, long> source)
    {
        foreach ((int key, long value) in source)
        {
            target[key] = target.GetValueOrDefault(key) + value;
        }
    }
}
