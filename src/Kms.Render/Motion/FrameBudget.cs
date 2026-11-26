namespace Kms.Render.Motion;

/// <summary>
/// 帧预算的自适应降级器。只做纯计算，方便在无 UI 的测试里验证。
/// 目标帧率从 120 开始；连续慢帧会降到 60/30，弱机不再硬追 120。
/// </summary>
public sealed class FrameBudget
{
    public const int HighFps = 120;
    public const int MidFps = 60;
    public const int LowFps = 30;

    private const double HighFrameMs = 1000.0 / HighFps + 0.5;
    private const double MidFrameMs = 1000.0 / MidFps + 0.5;
    private int _slowHigh;
    private int _slowMid;
    private int _frames;

    public int TargetFps { get; private set; } = HighFps;

    public TimeSpan Interval => TimeSpan.FromMilliseconds(1000.0 / TargetFps);

    /// <summary>采样一帧；返回 true 表示目标帧率发生变化。</summary>
    public bool Sample(TimeSpan frame)
    {
        double ms = frame.TotalMilliseconds;
        if (ms <= 0 || ms > 500)
        {
            return false;
        }

        _frames++;
        if (ms > MidFrameMs)
        {
            _slowMid++;
            _slowHigh = 0;
        }
        else if (ms > HighFrameMs)
        {
            _slowHigh++;
            _slowMid = 0;
        }
        else
        {
            _slowMid = 0;
            _slowHigh = 0;
        }

        if (_slowMid >= 20 && TargetFps > LowFps)
        {
            TargetFps = LowFps;
            Reset();
            return true;
        }

        if (_slowHigh >= 45 && TargetFps > MidFps)
        {
            TargetFps = MidFps;
            Reset();
            return true;
        }

        // 每 10 秒探测一次上一档；如果探测后仍然慢，会按上面的阈值再次降级。
        if (_frames >= TargetFps * 10 && TargetFps < HighFps)
        {
            TargetFps = TargetFps == LowFps ? MidFps : HighFps;
            Reset();
            return true;
        }

        return false;
    }

    private void Reset()
    {
        _slowHigh = 0;
        _slowMid = 0;
        _frames = 0;
    }
}
