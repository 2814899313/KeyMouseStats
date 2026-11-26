using Kms.Core.Model;

namespace Kms.Input.Counting;

/// <summary>
/// 按住时长采集（v3 <c>HoldTracker</c> 的实例化移植）。
/// 所有调用都发生在统计线程（本项目的 UI 线程排空队列时），无需加锁。
/// </summary>
public sealed class HoldTracker
{
    public const int BucketCount = 8;

    private static readonly double[] Edges = [50, 100, 200, 500, 1000, 2000, 5000];

    /// <summary>超过此时长视为挂机或丢失 UP，丢弃并计入丢弃数。</summary>
    public const double MaxHoldMs = 60_000;

    /// <summary>单段「有键按下」的墙钟上限；超过它只能是休眠或丢事件。</summary>
    public const double MaxActiveSpanMs = 600_000;

    private readonly StatsTotals? _totals;

    private readonly Dictionary<int, Pending> _down = new();
    private DayRecord? _activeDay;
    private long _activeStart;

    public HoldTracker(StatsTotals? totals = null)
    {
        _totals = totals;
    }

    private struct Pending
    {
        public DayRecord? Day;
        public long Ticks;
    }

    public int PendingCount => _down.Count;

    /// <summary>首次按下；自动重复不会重置起点。丢失抬起时旧样本按丢弃处理。</summary>
    public void Press(DayRecord? day, int virtualKey, long ticks)
    {
        if (day is null || virtualKey <= 0)
        {
            return;
        }

        if (_down.TryGetValue(virtualKey, out Pending stale))
        {
            _down.Remove(virtualKey);
            Discard(stale);
        }

        _down[virtualKey] = new Pending { Day = day, Ticks = ticks };
        RefreshActive(day, ticks);
    }

    /// <summary>抬起时结算；返回是否计入有效样本。</summary>
    public bool Release(int virtualKey, long ticks)
    {
        if (!_down.TryGetValue(virtualKey, out Pending pending))
        {
            return false;
        }

        _down.Remove(virtualKey);
        bool counted = Settle(pending, virtualKey, ticks);
        RefreshActive(pending.Day, ticks);
        return counted;
    }

    /// <summary>把还按着的键全部丢弃（锁屏、休眠、断开、退出前调用）。</summary>
    public int DiscardPending() => DiscardPending(System.Diagnostics.Stopwatch.GetTimestamp());

    public int DiscardPending(long ticks)
    {
        int count = 0;
        foreach (Pending pending in _down.Values)
        {
            Discard(pending);
            count++;
        }

        _down.Clear();
        RefreshActive(null, ticks);
        return count;
    }

    public static int BucketIndex(double milliseconds)
    {
        for (int i = 0; i < Edges.Length; i++)
        {
            if (milliseconds < Edges[i])
            {
                return i;
            }
        }

        return BucketCount - 1;
    }

    private bool Settle(Pending pending, int virtualKey, long ticks)
    {
        DayRecord? day = pending.Day;
        if (day is null)
        {
            return false;
        }

        double milliseconds = (ticks - pending.Ticks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (milliseconds <= 0 || milliseconds > MaxHoldMs)
        {
            Discard(pending);
            return false;
        }

        day.HoldCount++;
        day.HoldTotalMs += milliseconds;
        if (milliseconds > day.HoldMaxMs)
        {
            day.HoldMaxMs = milliseconds;
        }

        day.HoldBuckets[BucketIndex(milliseconds)]++;
        if (!day.Holds.TryGetValue(virtualKey, out KeyHold? hold))
        {
            hold = new KeyHold();
            day.Holds[virtualKey] = hold;
        }

        hold.Add(milliseconds);

        if (_totals is not null)
        {
            _totals.HoldCount++;
            _totals.HoldTotalMs += milliseconds;
            if (milliseconds > _totals.HoldMaxMs)
            {
                _totals.HoldMaxMs = milliseconds;
            }
        }

        return true;
    }

    private void Discard(Pending pending)
    {
        if (pending.Day is not null)
        {
            pending.Day.HoldDiscarded++;
        }

        if (_totals is not null)
        {
            _totals.HoldDiscarded++;
        }
    }

    private void RefreshActive(DayRecord? day, long ticks)
    {
        if (_down.Count > 0)
        {
            if (_activeDay is null)
            {
                _activeDay = day;
                _activeStart = ticks;
            }

            return;
        }

        if (_activeDay is null)
        {
            return;
        }

        double seconds = (ticks - _activeStart) * 1.0 / System.Diagnostics.Stopwatch.Frequency;
        DayRecord owner = _activeDay;
        _activeDay = null;
        _activeStart = 0;

        if (seconds <= 0 || seconds * 1000.0 > MaxActiveSpanMs)
        {
            return;
        }

        owner.HoldActiveSeconds += seconds;
        if (_totals is not null)
        {
            _totals.HoldActiveSeconds += seconds;
        }
    }
}
