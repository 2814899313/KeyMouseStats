using System.Runtime.InteropServices;

namespace Kms.Input.Hooks;

/// <summary>
/// 把低级钩子装到**专用消息线程**上。
///
/// 为什么不能装在 Avalonia UI 线程：低级钩子的回调由系统在安装线程「取消息」时派发。
/// Avalonia 的消息泵会按窗口/消息类型过滤（渲染期间尤其明显），实测会丢掉大多数键鼠事件。
/// 专用线程用未过滤的 <c>GetMessage</c> 泵，回调稳定，且不会阻塞 UI。
///
/// 线程归属硬约束：
/// <list type="bullet">
/// <item><see cref="LowLevelHooks.Install"/> / <see cref="LowLevelHooks.Uninstall"/> 只在本线程调用。</item>
/// <item>看门狗的体检通过 <see cref="RequestCheck"/> 投递 <c>WM_APP</c>，同样在本线程执行。</item>
/// <item>回调里只允许入队（见 <c>QueuedInputSink</c>），统计与数据模型仍归 UI 线程。</item>
/// </list>
/// </summary>
public sealed class HookThreadHost : IDisposable
{
    private const int WM_QUIT = 0x0012;
    private const int WM_APP_CHECK = 0x8000 + 0x4B4D;

    private readonly IInputSink _sink;
    private readonly Func<bool>? _busy;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();

    private LowLevelHooks? _hooks;
    private int _threadId;
    private int _started;
    private volatile bool _stop;
    private bool _disposed;

    public HookThreadHost(IInputSink sink, Func<bool>? busy = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _busy = busy;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Kms.HookThread"
        };
    }

    public bool KeyboardAlive => _hooks?.IsKeyboardAlive == true;

    public bool MouseAlive => _hooks?.IsMouseAlive == true;

    /// <summary>至少一条钩子安装成功。</summary>
    public bool Installed => Volatile.Read(ref _started) != 0;

    public long LastKeyboardTickMs => _hooks?.LastKeyboardTickMs ?? 0;

    public long LastMouseTickMs => _hooks?.LastMouseTickMs ?? 0;

    /// <summary>启动线程并等待安装结果；最多等 5 秒。</summary>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_thread.IsAlive)
        {
            return Installed;
        }

        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            return false;
        }

        return Installed;
    }

    /// <summary>请求本线程做一次体检并按需重装；线程没起来时无操作。</summary>
    public void RequestCheck()
    {
        int id = Volatile.Read(ref _threadId);
        if (id != 0)
        {
            PostThreadMessage((uint)id, WM_APP_CHECK, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop = true;

        int id = Volatile.Read(ref _threadId);
        if (id != 0)
        {
            PostThreadMessage((uint)id, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(3));
        }

        lock (_gate)
        {
            _hooks?.Dispose();
            _hooks = null;
        }

        _ready.Dispose();
    }

    private void Run()
    {
        int threadId = unchecked((int)GetCurrentThreadId());
        Volatile.Write(ref _threadId, threadId);

        LowLevelHooks hooks = new(_sink, busy: _busy);
        lock (_gate)
        {
            _hooks = hooks;
        }

        try
        {
            hooks.Install();
            bool any = hooks.IsKeyboardAlive || hooks.IsMouseAlive;
            Volatile.Write(ref _started, any ? 1 : 0);
        }
        catch
        {
            Volatile.Write(ref _started, 0);
        }
        finally
        {
            _ready.Set();
        }

        while (!_stop && GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Id == WM_APP_CHECK)
            {
                Repair(hooks);
                continue;
            }

            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        hooks.Uninstall();
        hooks.Dispose();
    }

    private void Repair(LowLevelHooks hooks)
    {
        try
        {
            if (hooks.CheckHealth() != HookRepair.None)
            {
                hooks.Install();
                Volatile.Write(ref _started, (hooks.IsKeyboardAlive || hooks.IsMouseAlive) ? 1 : 0);
            }
        }
        catch
        {
            // 看门狗下一轮还会来；这里绝不能让线程退出。
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Hwnd;
        public uint Id;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
