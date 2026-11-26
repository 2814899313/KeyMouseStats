using System.Runtime.InteropServices;

namespace Kms.Input.RawInput;

/// <summary>
/// Raw Input 消息宿主：在专属后台线程上创建 message-only 窗口，
/// 以 RIDEV_INPUTSINK + RIDEV_DEVNOTIFY 注册鼠标/键盘探针，
/// 把 WM_INPUT 与 WM_INPUT_DEVICE_CHANGE 解码后路由给 <see cref="IRawInputSink"/>。
///
/// 生命周期：<see cref="Start"/> 返回时窗口与探针均已就绪；消息循环跑在宿主线程上，
/// <see cref="Stop"/> 投递 WM_CLOSE 并等待线程退出。窗口过程从不向 user32 抛异常。
/// 接缝回调在宿主线程上同步执行，必须快速返回，不能阻塞。
/// </summary>
public sealed class RawInputHost : IDisposable
{
    /// <summary>等待窗口/探针就绪的上限。</summary>
    public const int StartupTimeoutMs = 5_000;

    /// <summary>等待消息线程退出的上限。</summary>
    public const int StopTimeoutMs = 5_000;

    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const int ErrorTimeout = 1460; // ERROR_TIMEOUT

    private static int _classSequence;

    private readonly IRawInputSink _sink;
    private readonly object _gate = new();
    private readonly RawMouseInput _mouse = new();
    private readonly RawKeyboardInput _keyboard = new();

    private Thread? _thread;
    private Session? _session;
    private IntPtr _hwnd;
    private int _lastError;
    private bool _disposed;

    private long _mousePackets;
    private long _keyboardPackets;
    private long _deviceChanges;

    public RawInputHost(IRawInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>窗口与探针都已就绪、消息循环仍在运行。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _thread is { IsAlive: true } && _hwnd != IntPtr.Zero;
            }
        }
    }

    /// <summary>message-only 窗口句柄；未运行时为 <see cref="IntPtr.Zero"/>。</summary>
    public IntPtr WindowHandle
    {
        get
        {
            lock (_gate)
            {
                return _hwnd;
            }
        }
    }

    /// <summary>最近一次启动/消息循环失败的 Win32 错误码；成功启动后为 0。</summary>
    public int LastError => Volatile.Read(ref _lastError);

    /// <summary>自创建以来成功路由的鼠标包数。</summary>
    public long MousePacketCount => Interlocked.Read(ref _mousePackets);

    /// <summary>自创建以来成功路由的键盘包数。</summary>
    public long KeyboardPacketCount => Interlocked.Read(ref _keyboardPackets);

    /// <summary>自创建以来路由的设备到达/移除通知数。</summary>
    public long DeviceChangeCount => Interlocked.Read(ref _deviceChanges);

    /// <summary>
    /// 启动宿主线程并注册探针。已运行时直接返回 <c>true</c>；
    /// 上一次启动尚未结束或失败时返回 <c>false</c>，错误码见 <see cref="LastError"/>。
    /// </summary>
    public bool Start()
    {
        Thread thread;
        Session session;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_thread is { IsAlive: true })
            {
                return _hwnd != IntPtr.Zero;
            }

            Volatile.Write(ref _lastError, 0);
            session = new Session();
            _session = session;
            thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "Kms.RawInputHost",
            };
            _thread = thread;
            thread.Start(session);
        }

        if (!session.Startup.Task.Wait(StartupTimeoutMs))
        {
            Volatile.Write(ref _lastError, ErrorTimeout);
            Stop();
            return false;
        }

        bool ok = session.Startup.Task.Result;
        if (!ok)
        {
            RetireFailedThread(thread);
            return false;
        }

        if (!IsRunning)
        {
            RetireFailedThread(thread);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 停止消息循环并注销探针。幂等；可从接缝回调内调用（此时不会等待自己）。
    /// </summary>
    public void Stop()
    {
        Thread? thread;
        Session? session;
        IntPtr hwnd;

        lock (_gate)
        {
            thread = _thread;
            session = _session;
            hwnd = _hwnd;
            // 先摘掉消息目标，但保留 _thread/_session 直到线程真正退出，
            // 防止新的 Start 与旧线程的 RIDEV_REMOVE 清理交叠。
            _hwnd = IntPtr.Zero;
        }

        if (session is not null)
        {
            session.StopRequested = true;
            // 若 Start 正在等待，立刻放行；线程自己会走清理路径。
            session.Startup.TrySetResult(false);
        }

        if (thread is null)
        {
            return;
        }

        if (hwnd != IntPtr.Zero)
        {
            _ = RawInputNative.PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        if (thread.ManagedThreadId != Environment.CurrentManagedThreadId)
        {
            thread.Join(StopTimeoutMs);

            lock (_gate)
            {
                if (ReferenceEquals(_thread, thread) && !thread.IsAlive)
                {
                    _thread = null;
                    _session = null;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
        GC.SuppressFinalize(this);
    }

    private void ThreadMain(object? state)
    {
        var session = (Session)state!;
        IntPtr instance = IntPtr.Zero;
        IntPtr hwnd = IntPtr.Zero;
        ushort atom = 0;
        bool devicesRegistered = false;
        string? className = null;

        try
        {
            instance = RawInputNative.GetModuleHandle(null);
            if (instance == IntPtr.Zero)
            {
                Fail(session, Marshal.GetLastWin32Error());
                return;
            }

            className = FormattableString.Invariant(
                $"KmsRawInputHost.{Environment.ProcessId}.{Interlocked.Increment(ref _classSequence)}");

            RawInputNative.WndProc windowProc = WindowProc;
            session.WindowProc = windowProc;
            var windowClass = new RawInputNative.WndClassEx
            {
                Size = (uint)Marshal.SizeOf<RawInputNative.WndClassEx>(),
                Style = 0,
                WindowProc = Marshal.GetFunctionPointerForDelegate(windowProc),
                ClassExtra = 0,
                WindowExtra = 0,
                Instance = instance,
                Icon = IntPtr.Zero,
                Cursor = IntPtr.Zero,
                Background = IntPtr.Zero,
                MenuName = null,
                ClassName = className,
                SmallIcon = IntPtr.Zero,
            };

            atom = RawInputNative.RegisterClassEx(ref windowClass);
            if (atom == 0)
            {
                Fail(session, Marshal.GetLastWin32Error());
                return;
            }

            hwnd = RawInputNative.CreateWindowEx(
                0,
                className,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                RawInputNative.HwndMessage,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                Fail(session, Marshal.GetLastWin32Error());
                return;
            }

            lock (_gate)
            {
                if (ReferenceEquals(_session, session))
                {
                    _hwnd = hwnd;
                }
            }

            if (session.StopRequested)
            {
                Fail(session, 0);
                return;
            }

            var devices = new RawInputNative.RawInputDevice[2];
            devices[0] = new RawInputNative.RawInputDevice
            {
                UsagePage = RawInputNative.HidUsagePageGeneric,
                Usage = RawInputNative.HidUsageMouse,
                Flags = RawInputNative.RidevInputSink | RawInputNative.RidevDevNotify,
                Target = hwnd,
            };
            devices[1] = new RawInputNative.RawInputDevice
            {
                UsagePage = RawInputNative.HidUsagePageGeneric,
                Usage = RawInputNative.HidUsageKeyboard,
                Flags = RawInputNative.RidevInputSink | RawInputNative.RidevDevNotify,
                Target = hwnd,
            };

            // 注册失败也可能是部分成功（例如只有键盘被接受），
            // 因此先把清理标记置位，退出时统一走 RIDEV_REMOVE。
            devicesRegistered = true;
            if (!RawInputNative.RegisterRawInputDevices(
                    devices,
                    (uint)devices.Length,
                    (uint)Marshal.SizeOf<RawInputNative.RawInputDevice>()))
            {
                Fail(session, Marshal.GetLastWin32Error());
                return;
            }

            session.Startup.TrySetResult(true);
            int result;
            while ((result = RawInputNative.GetMessage(out RawInputNative.Msg message, IntPtr.Zero, 0, 0)) > 0)
            {
                _ = RawInputNative.TranslateMessage(ref message);
                _ = RawInputNative.DispatchMessage(ref message);
            }

            if (result < 0)
            {
                Volatile.Write(ref _lastError, Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            Fail(session, ex.HResult);
        }
        finally
        {
            // 消息循环已结束：先把窗口句柄摘掉，避免并发的 Start 误判为仍在运行。
            lock (_gate)
            {
                if (ReferenceEquals(_session, session) && _hwnd == hwnd)
                {
                    _hwnd = IntPtr.Zero;
                }
            }

            if (devicesRegistered)
            {
                UnregisterRawInputDevices();
            }

            if (hwnd != IntPtr.Zero)
            {
                _ = RawInputNative.DestroyWindow(hwnd);
            }

            if (atom != 0 && className is not null && instance != IntPtr.Zero)
            {
                _ = RawInputNative.UnregisterClass(className, instance);
            }

            lock (_gate)
            {
                if (ReferenceEquals(_session, session))
                {
                    _hwnd = IntPtr.Zero;
                    _session = null;
                    if (ReferenceEquals(_thread, Thread.CurrentThread))
                    {
                        _thread = null;
                    }
                }
            }

            session.Startup.TrySetResult(false);
        }
    }

    /// <summary>窗口过程：任何异常都在这里终结，绝不抛回 user32。</summary>
    private IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case (uint)InputMessages.WM_INPUT:
                    HandleInput(lParam);
                    break;

                case (uint)InputMessages.WM_INPUT_DEVICE_CHANGE:
                    HandleDeviceChange(wParam, lParam);
                    break;

                case WmClose:
                    if (!RawInputNative.DestroyWindow(hWnd))
                    {
                        RawInputNative.PostQuitMessage(0);
                    }

                    return IntPtr.Zero;

                case WmDestroy:
                    RawInputNative.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch
        {
            // 解码或接缝回调失败不能让窗口过程崩溃；兜底交给默认处理。
        }

        // WM_INPUT 必须交给 DefWindowProc 完成系统侧清理。
        return RawInputNative.DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void HandleInput(IntPtr lParam)
    {
        if (_mouse.Read(lParam, out RawMouseData mouse))
        {
            Interlocked.Increment(ref _mousePackets);
            try
            {
                _sink.RawMouse(mouse.Device, mouse.ButtonFlags, mouse.WheelDelta, mouse.DeltaX, mouse.DeltaY);
            }
            catch
            {
                // 接缝实现自己的异常不改变输入分发的生命周期。
            }

            return;
        }

        if (_keyboard.Read(lParam, out RawKeyboardData key))
        {
            Interlocked.Increment(ref _keyboardPackets);
            try
            {
                _sink.RawKey(key.Device, key.VirtualKey, key.MakeCode, key.Flags, key.Down);
            }
            catch
            {
                // 同上。
            }
        }
    }

    private void HandleDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        int action = wParam.ToInt32();
        if (action is not (RawInputNative.GidcArrival or RawInputNative.GidcRemoval) ||
            lParam == IntPtr.Zero)
        {
            return;
        }

        Interlocked.Increment(ref _deviceChanges);
        try
        {
            _sink.DeviceChanged(lParam, action == RawInputNative.GidcRemoval);
        }
        catch
        {
            // 同上。
        }
    }

    private static void UnregisterRawInputDevices()
    {
        var devices = new RawInputNative.RawInputDevice[2];
        devices[0] = new RawInputNative.RawInputDevice
        {
            UsagePage = RawInputNative.HidUsagePageGeneric,
            Usage = RawInputNative.HidUsageMouse,
            Flags = RawInputNative.RidevRemove,
            Target = IntPtr.Zero,
        };
        devices[1] = new RawInputNative.RawInputDevice
        {
            UsagePage = RawInputNative.HidUsagePageGeneric,
            Usage = RawInputNative.HidUsageKeyboard,
            Flags = RawInputNative.RidevRemove,
            Target = IntPtr.Zero,
        };

        _ = RawInputNative.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputNative.RawInputDevice>());
    }

    private void Fail(Session session, int error)
    {
        Volatile.Write(ref _lastError, error);
        session.Startup.TrySetResult(false);
    }

    private void RetireFailedThread(Thread thread)
    {
        if (thread.IsAlive)
        {
            thread.Join(1_000);
        }

        lock (_gate)
        {
            if (!thread.IsAlive)
            {
                _thread = null;
            }
        }
    }

    private sealed class Session
    {
        public TaskCompletionSource<bool> Startup { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool StopRequested;

        public RawInputNative.WndProc? WindowProc;
    }
}
