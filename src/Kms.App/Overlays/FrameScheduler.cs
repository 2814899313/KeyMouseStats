using Avalonia.Threading;
using Kms.Render.Motion;

namespace Kms.App.Overlays;

/// <summary>
/// 把 <see cref="FrameBudget"/> 接到 Avalonia 的 DispatcherTimer 上。
/// 只负责节拍与降级；具体动效由订阅者根据 delta 推进。
/// </summary>
internal sealed class FrameScheduler : IDisposable
{
    private readonly FrameBudget _budget = new();
    private readonly DispatcherTimer _timer;
    private DateTime _last;
    private bool _started;

    public FrameScheduler()
    {
        _timer = new DispatcherTimer { Interval = _budget.Interval };
        _timer.Tick += (_, _) => OnTick();
    }

    public event Action<double>? Tick;

    public int TargetFps => _budget.TargetFps;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _last = DateTime.UtcNow;
        _timer.Interval = _budget.Interval;
        _timer.Start();
    }

    public void Stop()
    {
        _started = false;
        _timer.Stop();
    }

    public void Dispose()
    {
        Stop();
        Tick = null;
    }

    private void OnTick()
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan delta = now - _last;
        _last = now;
        if (delta <= TimeSpan.Zero || delta.TotalMilliseconds > 500)
        {
            return;
        }

        if (_budget.Sample(delta))
        {
            _timer.Interval = _budget.Interval;
        }

        Tick?.Invoke(delta.TotalSeconds);
    }
}
