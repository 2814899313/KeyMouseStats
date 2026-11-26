using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Kms.Core.Input;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Input;
using Kms.Input.Activity;
using Kms.Input.Counting;
using Kms.Input.Devices;
using Kms.Input.Hooks;
using Kms.Input.RawInput;
using Kms.Storage.Sqlite;

namespace Kms.App;

/// <summary>
/// v4 采集宿主：钩子线程 + Raw Input + 活跃积分 + DbWriter。
///
/// 线程模型：
/// <list type="bullet">
/// <item>钩子装在 <see cref="HookThreadHost"/> 的专用消息线程，回调只写入
///       <see cref="QueuedInputSink"/>，不碰数据模型。</item>
/// <item>UI 线程每 30ms 排空队列，调用 <see cref="CountingInputSink"/> 更新
///       <c>StatsData</c>，因而所有 UI 读取与写入都在同一线程。</item>
/// <item>活跃采样与钩子看门狗走 <see cref="DispatcherTimer"/>，同样在 UI 线程。</item>
/// </list>
/// </summary>
public sealed class InputService : IDisposable
{
    /// <summary>队列排空间隔；30ms 对显示与 APM 足够，同时避免每事件一次调度。</summary>
    public const int DrainIntervalMs = 30;

    private readonly LoadedStats _loaded;
    private readonly EventRing _ring;
    private readonly CountingInputSink _sink;
    private readonly QueuedInputSink _queue;
    private readonly HookThreadHost _hooks;
    private readonly RawInputHost _rawHost;
    private readonly RawDeviceSink _rawSink;
    private readonly PeripheralWear _keyboardWear;
    private readonly PeripheralMouseWear _mouseWear;
    private readonly DbWriter _writer;
    private readonly ActivityTracker _tracker;
    private readonly ActivityMonitor _monitor;
    private readonly ActivityAttribution _attribution;
    private readonly ForegroundApp _foreground;
    private readonly DispatcherTimer _drainTimer;
    private readonly DispatcherTimer _activityTimer;
    private readonly DispatcherTimer _healthTimer;
    private readonly DispatcherTimer _saveTimer;
    private bool _started;
    private bool _disposed;
    private bool _rawStarted;

    private bool _wasBusy;

    public InputService(LoadedStats loaded)
    {
        _loaded = loaded;
        _ring = new EventRing();
        _sink = new CountingInputSink(loaded.Data, _ring, SystemClock.Instance, NativeForeground);
        _queue = new QueuedInputSink();
        _foreground = new ForegroundApp();
        _writer = DbWriter.Open(DataPaths.V4Database, _ring);
        _writer.ResolveWindow = hwnd =>
        {
            WindowInfo info = _foreground.Resolve(hwnd);
            return new WindowTarget
            {
                ProcessPath = info.ProcessPath,
                ProcessName = info.ProcessName,
                WindowTitle = info.WindowTitle
            };
        };

        _tracker = new ActivityTracker(date => loaded.Data.GetOrCreate(date));
        _attribution = new ActivityAttribution(date => loaded.Data.GetOrCreate(date));
        _monitor = new ActivityMonitor(_tracker) { Attribution = _attribution };
        _sink.AppWindow = () => _attribution.Current;

        _keyboardWear = new PeripheralWear();
        _mouseWear = new PeripheralMouseWear();
        _rawSink = new RawDeviceSink(_keyboardWear, _mouseWear);
        _keyboardWear.BeginLoad();
        _keyboardWear.LoadProfiles(loaded.Data.Keyboards);
        loaded.Data.Keyboards.Clear();
        loaded.Data.Keyboards.AddRange(_keyboardWear.SnapshotKeyboards(includePending: true));

        _mouseWear.BeginLoad();
        _mouseWear.LoadProfiles(loaded.Data.Mice);
        loaded.Data.Mice.Clear();
        loaded.Data.Mice.AddRange(_mouseWear.SnapshotMice(includePending: true));

        _rawHost = new RawInputHost(_rawSink);
        _hooks = new HookThreadHost(_queue, busy: () => _monitor.Busy);

        _drainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DrainIntervalMs) };
        _drainTimer.Tick += (_, _) => Drain();
        _activityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _activityTimer.Tick += (_, _) => SampleActivity();
        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HookWatchdog.CheckIntervalMs) };
        _healthTimer.Tick += (_, _) => _hooks.RequestCheck();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _saveTimer.Tick += (_, _) => Save();
    }

    public StatsData Data => _loaded.Data;

    public LoadedStats Loaded => _loaded;

    public string Status { get; private set; } = "未启动";

    public long CountedKeys => _sink.Keys;

    public long Apm => _sink.Apm;

    public long KeysPerMin => _sink.KeysPerMin;

    public long ClicksPerMin => _sink.ClicksPerMin;

    public long CountedClicks => _sink.Clicks;

    public double CountedMovePixels => _sink.MovePixels;

    /// <summary>钩子线程收到的事件总数（含已排空的）。</summary>
    public long HookEvents => _queue.Received;

    public long RawMousePackets => _rawSink.RawPackets;

    /// <summary>钩子线程收到的键盘事件数（按下 + 抬起）。</summary>
    public long HookKeyEvents => _queue.ReceivedKeys;

    /// <summary>队列满丢弃的事件数；正常应为 0。</summary>
    public long DroppedEvents => _queue.Dropped;

    /// <summary>固件数据只用于开发预览，不能写回用户目录；读取失败时也不能覆盖原档。</summary>
    private bool CanPersist =>
        _loaded.Source != "开发固件"
        && !_loaded.Status.StartsWith("数据读取失败", StringComparison.Ordinal);

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _writer.Start();
        bool raw = _rawHost.Start();
        _rawStarted = raw;
        bool hooks = _hooks.Start();

        _drainTimer.Start();
        _healthTimer.Start();
        _activityTimer.Start();
        _saveTimer.Start();

        // 首次以 v3 数据启动时立刻落一份 v4 文本，之后它才是主存档。
        if (CanPersist && !File.Exists(DataPaths.V4Text))
        {
            Save();
        }

        Status = hooks
            ? "实时采集中"
            : "钩子安装失败，采集未启动";
        RefreshStatus();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _drainTimer.Stop();
        _activityTimer.Stop();
        _healthTimer.Stop();
        _saveTimer.Stop();

        _hooks.Dispose();
        Drain(int.MaxValue);
        _sink.DiscardPendingHolds();
        _sink.FlushMove();
        Save();
        _rawHost.Stop();
        _writer.Flush(1_000);
        _writer.Stop();
        _writer.Dispose();
        _rawHost.Dispose();
    }

    private void Drain(int limit = 8_192)
    {
        try
        {
            _queue.Drain(_sink, limit);
        }
        catch
        {
            // 单次排空失败不能把采集宿主带崩；下个 tick 继续。
        }
    }

    private void Save()
    {
        if (!CanPersist)
        {
            return;
        }

        if (!StatsSaver.Save(_loaded.Data) && StatsSaver.LastError is { } error)
        {
            Status = "保存失败：" + error;
        }
    }

    private void SampleActivity()
    {
        try
        {
            DateTime now = DateTime.Now;
            bool busy = _monitor.Busy;
            if (busy && !_wasBusy)
            {
                _sink.DiscardPendingHolds();
            }

            _wasBusy = busy;

            WindowInfo window = _foreground.Capture();
            _attribution.Prepare(now, window);
            bool accepted = _monitor.Tick(60);
            _attribution.Finish(now, accepted, _monitor.IsActive);
            _sink.TickRate();
            _sink.MeasureSession(_tracker.CurrentSession, now);
            ApplyRawDistance();
            RefreshStatus();
        }
        catch
        {
            // 采集宿主不能因单次采样失败退出。
        }
    }

    /// <summary>把 Raw Input 的传感器计数按 DPI 换算成米；DPI 未设置时不累计。</summary>
    private void ApplyRawDistance()
    {
        double counts = _rawSink.TakeRawCounts();
        if (counts <= 0)
        {
            return;
        }

        double dpi = 0;
        if (_loaded.Data.Settings.TryGetValue("mouse_dpi", out string? raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            dpi = parsed;
        }

        double meters = MouseDistance.ToMeters(counts, dpi);
        if (meters <= 0)
        {
            return;
        }

        DayRecord day = _loaded.Data.GetOrCreate(DateTime.Now.Date);
        day.MoveMeters += meters;
        _loaded.Data.Totals.MoveMeters += meters;
    }

    private void RefreshStatus()
    {
        if (!_started)
        {
            return;
        }

        bool keyboard = _hooks.KeyboardAlive;
        bool mouse = _hooks.MouseAlive;
        Status = keyboard || mouse
            ? $"实时采集中 · 键盘={(keyboard ? "on" : "off")} 鼠标={(mouse ? "on" : "off")} " +
              $"raw={(_rawStarted ? "on" : "off")}"
            : "钩子已断开，等待自愈";
    }

    private static IntPtr NativeForeground()
    {
        try
        {
            return GetForegroundWindow();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private sealed class RawDeviceSink : IRawInputSink
    {
        private long _rawCountsMilli;
        private long _rawPackets;

        public RawDeviceSink(PeripheralWear keyboardWear, PeripheralMouseWear mouseWear)
        {
            Keyboards = new KeyboardDeviceRegistry(keyboardWear) { ResolveInline = true };
            Mice = new MouseDeviceRegistry(mouseWear) { ResolveInline = true };
        }

        public KeyboardDeviceRegistry Keyboards { get; }

        public MouseDeviceRegistry Mice { get; }

        public long RawPackets => Interlocked.Read(ref _rawPackets);

        /// <summary>取走累计的 Raw Input 传感器计数（0.001 精度）。</summary>
        public double TakeRawCounts()
        {
            long value = Interlocked.Exchange(ref _rawCountsMilli, 0);
            return value / 1000.0;
        }

        public void RawMouse(IntPtr device, ushort buttonFlags, short wheelDelta, int dx, int dy)
        {
            Interlocked.Increment(ref _rawPackets);
            Mice.HandleButtons(device, buttonFlags, DateTime.Now);
            if (dx != 0 || dy != 0)
            {
                double distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
                Interlocked.Add(ref _rawCountsMilli, (long)Math.Round(distance * 1000.0));
            }
        }

        public void RawKey(IntPtr device, int virtualKey, int makeCode, uint flags, bool down)
        {
            Keyboards.HandleRawKey(device, virtualKey, makeCode, flags, down);
        }

        public void DeviceChanged(IntPtr device, bool removed)
        {
            if (removed)
            {
                Keyboards.OnDeviceRemoved(device);
                Mice.OnDeviceRemoved(device);
            }
        }
    }
}
