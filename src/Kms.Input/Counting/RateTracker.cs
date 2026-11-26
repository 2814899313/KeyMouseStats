using Kms.Core.Model;

namespace Kms.Input.Counting;

/// <summary>
/// 实时 APM / 最近 5 分钟输入量（v3 <c>LiveRate</c> + <c>RateMemory</c> 的实例化移植）。
///
/// 60 个 1 秒桶用于 APM，300 个 1 秒桶用于 5 分钟输入量；
/// 另保留 60 秒事件时刻，用于把当前会话的峰值/最近 APM 写回 <see cref="ActiveSession"/>。
/// </summary>
public sealed class RateTracker
{
    private readonly long[] _keySeconds = new long[60];
    private readonly long[] _clickSeconds = new long[60];
    private readonly long[] _keyFive = new long[300];
    private readonly long[] _clickFive = new long[300];
    private readonly Queue<DateTime> _events = new();

    private int _index;
    private int _indexFive;
    private long _rateStart = System.Diagnostics.Stopwatch.GetTimestamp();
    private DateTime _last = DateTime.MinValue;

    public long KeysPerMin
    {
        get
        {
            long sum = 0;
            for (int i = 0; i < 60; i++)
            {
                sum += _keySeconds[i];
            }

            return sum;
        }
    }

    public long ClicksPerMin
    {
        get
        {
            long sum = 0;
            for (int i = 0; i < 60; i++)
            {
                sum += _clickSeconds[i];
            }

            return sum;
        }
    }

    public long Apm => KeysPerMin + ClicksPerMin;

    public long Keys5m
    {
        get
        {
            long sum = 0;
            for (int i = 0; i < 300; i++)
            {
                sum += _keyFive[i];
            }

            return sum;
        }
    }

    public long Clicks5m
    {
        get
        {
            long sum = 0;
            for (int i = 0; i < 300; i++)
            {
                sum += _clickFive[i];
            }

            return sum;
        }
    }

    public long Input5m => Keys5m + Clicks5m;

    public void AddKey(DateTime now)
    {
        Tick();
        _keySeconds[_index]++;
        _keyFive[_indexFive]++;
        AddEvent(now);
    }

    public void AddClick(DateTime now)
    {
        Tick();
        _clickSeconds[_index]++;
        _clickFive[_indexFive]++;
        AddEvent(now);
    }

    public void Tick() => TickAt(System.Diagnostics.Stopwatch.GetTimestamp());

    private void TickAt(long now)
    {
        long frequency = System.Diagnostics.Stopwatch.Frequency;
        long steps = (now - _rateStart) / frequency;
        if (steps <= 0)
        {
            return;
        }

        if (steps >= 300)
        {
            Array.Clear(_keySeconds, 0, 60);
            Array.Clear(_clickSeconds, 0, 60);
            Array.Clear(_keyFive, 0, 300);
            Array.Clear(_clickFive, 0, 300);
            _index = 0;
            _indexFive = 0;
        }
        else
        {
            for (int i = 0; i < steps; i++)
            {
                _index = (_index + 1) % 60;
                _keySeconds[_index] = 0;
                _clickSeconds[_index] = 0;
                _indexFive = (_indexFive + 1) % 300;
                _keyFive[_indexFive] = 0;
                _clickFive[_indexFive] = 0;
            }
        }

        _rateStart += steps * frequency;
    }

    /// <summary>把当前会话的最近/峰值 APM 写回会话；每秒采样调用一次。</summary>
    public void Measure(ActiveSession? session, DateTime now)
    {
        if (session is null)
        {
            return;
        }

        Prune(now);
        long count = 0;
        foreach (DateTime time in _events)
        {
            if (time >= session.Start && time > now.AddSeconds(-60) && time <= now)
            {
                count++;
            }
        }

        session.RateMeasured = true;
        session.LastApm = count;
        if (count > session.PeakApm)
        {
            session.PeakApm = count;
            session.PeakOffsetSeconds = Math.Max(0, (now - session.Start).TotalSeconds);
        }
    }

    private void AddEvent(DateTime now)
    {
        Prune(now);
        _events.Enqueue(now);
    }

    private void Prune(DateTime now)
    {
        if (now < _last)
        {
            _events.Clear();
        }

        _last = now;
        while (_events.Count > 0 && _events.Peek() <= now.AddSeconds(-60))
        {
            _events.Dequeue();
        }
    }
}
