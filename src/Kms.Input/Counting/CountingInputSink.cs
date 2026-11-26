using System.Globalization;
using Kms.Core.Input;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Input.Activity;

namespace Kms.Input.Counting;

/// <summary>
/// 把钩子事件并入当日统计并投递到写线程环形缓冲。
/// 只做统计计数；窗口标题/热区/磨损由上层或 Raw Input 路由负责。
///
/// 键盘路径与 v3 对齐：
/// 虚拟键先按扫描码/Extended 位归一化（左右 Ctrl/Alt/Shift），
/// 再由 <see cref="ShortcutTracker"/> 区分自动重复与丢失抬起；
/// 只有首次按下才计击键，同时记录物理键位、组合键与按住时长。
/// </summary>
public sealed class CountingInputSink : IInputSink
{
    /// <summary>同一物理键的重复按下在此窗口内视为自动重复。</summary>
    public const long AutoRepeatWindowMs = 1200;

    /// <summary>Move 入环前的合并窗口；文本路程仍在每个事件里精确累加。</summary>
    public const int MoveCoalesceMs = 10;

    private readonly StatsData _data;
    private readonly EventRing _ring;
    private readonly IClock _clock;
    private readonly Func<IntPtr> _foreground;
    private readonly ShortcutTracker _shortcuts = new();
    private readonly HoldTracker _holds;
    private readonly RateTracker _rate = new();
    private Func<WindowInfo> _appWindow = static () => WindowInfo.Empty;
    private int _lastX;
    private int _lastY;
    private bool _hasLastPosition;
    private bool _movePending;
    private double _moveDistance;
    private long _moveStart;
    private long _moveLast;
    private int _moveX;
    private int _moveY;

    public CountingInputSink(
        StatsData data,
        EventRing ring,
        IClock? clock = null,
        Func<IntPtr>? foreground = null,
        HoldTracker? holds = null)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _ring = ring ?? throw new ArgumentNullException(nameof(ring));
        _clock = clock ?? SystemClock.Instance;
        _foreground = foreground ?? (static () => IntPtr.Zero);
        _holds = holds ?? new HoldTracker(data.Totals);
    }

    public long Keys { get; private set; }
    public long Clicks { get; private set; }
    public long WheelEvents { get; private set; }
    public double MovePixels { get; private set; }

    public int PendingHolds => _holds.PendingCount;

    public long Apm => _rate.Apm;

    public long KeysPerMin => _rate.KeysPerMin;

    public long ClicksPerMin => _rate.ClicksPerMin;

    public long Input5m => _rate.Input5m;

    /// <summary>前台窗口快照来源（用于应用级按键/点击/滚轮归因）。</summary>
    public Func<WindowInfo> AppWindow
    {
        set => _appWindow = value ?? (static () => WindowInfo.Empty);
    }

    public void Key(int virtualKey, bool down, long ticks) => KeyEx(virtualKey, 0, 0, down, ticks);

    public void KeyEx(int virtualKey, int scanCode, uint flags, bool down, long ticks)
    {
        if (virtualKey <= 0)
        {
            return;
        }

        FlushMove();
        int vk = ShortcutTracker.Normalize(virtualKey, unchecked((uint)scanCode), flags);
        if (!down)
        {
            _shortcuts.Process(false, virtualKey, unchecked((uint)scanCode), flags, ticks, out _);
            _holds.Release(vk, ticks);
            Enqueue(InputEventKind.KeyUp, vk, 0, 0, 0, ticks);
            return;
        }

        string? combo = _shortcuts.Process(
            true,
            virtualKey,
            unchecked((uint)scanCode),
            flags,
            ticks,
            out bool firstPress);
        if (!firstPress)
        {
            return;
        }

        DayRecord day = Today();
        day.Keys++;
        day.HourKeys[_clock.UtcNow.ToLocalTime().Hour]++;
        day.KeyCounts[vk] = day.KeyCounts.GetValueOrDefault(vk) + 1;
        KeyboardHeat.Record(day, vk, unchecked((uint)scanCode), flags);
        _holds.Press(day, vk, ticks);
        ShortcutStats.Add(day, combo);
        RecordAppKey(day, vk);
        _rate.AddKey(DateTime.Now);
        UpdatePeak(day);
        _data.Totals.Keys++;
        Keys++;
        Enqueue(InputEventKind.KeyDown, vk, 0, 0, 0, ticks);
    }

    public void MouseButton(int button, bool down, int x, int y, long ticks)
    {
        if (!down || button is < 1 or > 5)
        {
            return;
        }

        FlushMove();
        DayRecord day = Today();
        day.Clicks++;
        day.HourClicks[_clock.UtcNow.ToLocalTime().Hour]++;
        _data.Totals.Clicks++;

        switch (button)
        {
            case 1:
                day.Left++;
                _data.Totals.Left++;
                break;
            case 2:
                day.Right++;
                _data.Totals.Right++;
                break;
            case 3:
                day.Middle++;
                _data.Totals.Middle++;
                break;
            default:
                day.XButtons++;
                _data.Totals.XButtons++;
                break;
        }

        Clicks++;
        ClickHeat.Record(day, x, y, button);
        RecordApp(day, click: true);
        _rate.AddClick(DateTime.Now);
        UpdatePeak(day);
        Enqueue(InputEventKind.MouseDown, button, x, y, 0, ticks);
    }

    public void Wheel(bool horizontal, int delta, long ticks)
    {
        FlushMove();
        DayRecord day = Today();
        day.Wheel++;
        _data.Totals.Wheel++;
        if (horizontal)
        {
            day.HWheel++;
            _data.Totals.HWheel++;
        }

        RecordApp(day, click: false);
        WheelEvents++;
        Enqueue(InputEventKind.Wheel, 0, 0, 0, delta, ticks);
    }

    public void Move(int x, int y, long ticks)
    {
        if (_hasLastPosition)
        {
            long dx = x - _lastX;
            long dy = y - _lastY;
            double distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (distance > 0)
            {
                Today().MovePx += distance;
                _data.Totals.MovePx += distance;
                MovePixels += distance;
                QueueMove(x, y, distance, ticks);
            }
        }

        _lastX = x;
        _lastY = y;
        _hasLastPosition = true;
    }

    /// <summary>锁屏/休眠/断开/退出时把未抬起的键丢弃，避免制造虚假长按。</summary>
    public int DiscardPendingHolds() => _holds.DiscardPending();

    /// <summary>每秒调用一次，让 APM 桶随时间衰减。</summary>
    public void TickRate() => _rate.Tick();

    /// <summary>每秒调用一次，把最近/峰值 APM 写回当前活跃会话。</summary>
    public void MeasureSession(ActiveSession? session, DateTime now) => _rate.Measure(session, now);

    private void RecordAppKey(DayRecord day, int virtualKey)
    {
        WindowInfo window = _appWindow();
        if (string.IsNullOrEmpty(window.ProcessPath))
        {
            return;
        }

        AppUsage app = ActivityAttribution.EnsureRecord(day, window);
        app.Keys++;
        app.HasKeyGroups = true;
        int group = KeySemantics.Group(virtualKey);
        if ((uint)group < (uint)app.KeyGroups.Length)
        {
            app.KeyGroups[group]++;
        }
    }

    private void RecordApp(DayRecord day, bool click)
    {
        WindowInfo window = _appWindow();
        if (string.IsNullOrEmpty(window.ProcessPath))
        {
            return;
        }

        AppUsage app = ActivityAttribution.EnsureRecord(day, window);
        if (click)
        {
            app.Clicks++;
        }
        else
        {
            app.Wheel++;
        }
    }

    private void UpdatePeak(DayRecord day)
    {
        long apm = _rate.Apm;
        if (apm > day.PeakApm)
        {
            day.PeakApm = apm;
        }

        long all = long.TryParse(
            _data.Settings.GetValueOrDefault("all_time_peak_apm"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long parsed)
            ? parsed
            : 0;

        if (apm > all)
        {
            _data.Settings["all_time_peak_apm"] = apm.ToString(CultureInfo.InvariantCulture);
        }
    }

    private DayRecord Today() => _data.GetOrCreate(_clock.UtcNow.ToLocalTime().Date);

    private void QueueMove(int x, int y, double distance, long ticks)
    {
        if (!_movePending)
        {
            _movePending = true;
            _moveDistance = 0;
            _moveStart = ticks;
        }

        _moveDistance += distance;
        _moveX = x;
        _moveY = y;
        _moveLast = ticks;

        long window = (long)(MoveCoalesceMs / 1000.0 * System.Diagnostics.Stopwatch.Frequency);
        if (ticks - _moveStart >= window)
        {
            FlushMove();
        }
    }

    /// <summary>把合并窗口内的移动写入环；不足 1px 的余数留到下一窗口。</summary>
    public void FlushMove()
    {
        if (!_movePending)
        {
            return;
        }

        int delta = (int)Math.Round(_moveDistance);
        if (delta > 0)
        {
            Enqueue(InputEventKind.Move, 0, _moveX, _moveY, delta, _moveLast, IntPtr.Zero);
            _moveDistance -= delta;
        }

        if (_moveDistance <= 0.0)
        {
            _movePending = false;
            _moveDistance = 0;
            _moveStart = 0;
            _moveLast = 0;
        }
        else
        {
            _moveStart = _moveLast;
        }
    }

    private void Enqueue(
        int kind,
        int vkOrButton,
        int x,
        int y,
        int delta,
        long ticks,
        IntPtr? hwnd = null)
    {
        RawInputEvent item = new(
            timestampTicks: ticks,
            hwnd: hwnd ?? _foreground(),
            kind: kind,
            vkOrButton: vkOrButton,
            x: x,
            y: y,
            delta: delta);
        _ring.TryEnqueue(item);
    }
}
