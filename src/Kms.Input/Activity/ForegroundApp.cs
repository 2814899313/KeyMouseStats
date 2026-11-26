using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Kms.Core.IO;

namespace Kms.Input.Activity;

/// <summary>
/// 可替换的窗口解析委托。默认实现是 Win32（含 UWP 宿主窗口）解析；
/// 测试或宿主可以换成带额外规则 / 更严格缓存的上层实现。
/// </summary>
public delegate WindowInfo WindowResolver(IntPtr hwnd);

/// <summary>
/// 前台窗口的纯解析结果。只描述窗口，不携带分类、规则或聚合数据。
/// 值类型便于缓存与跨线程传递；<c>default</c> 与 null 输入都按空串处理。
/// </summary>
public readonly struct WindowInfo : IEquatable<WindowInfo>
{
    private readonly string? _processPath;
    private readonly string? _processName;
    private readonly string? _windowTitle;

    public WindowInfo(string? processPath, string? processName, string? windowTitle)
    {
        _processPath = processPath;
        _processName = processName;
        _windowTitle = windowTitle;
    }

    /// <summary>进程完整路径；无法读取时为空串。</summary>
    public string ProcessPath => _processPath ?? "";

    /// <summary>进程文件名；未显式给出时由路径推导。<c>default</c> 也安全。</summary>
    public string ProcessName =>
        string.IsNullOrEmpty(_processName) ? PathNames.FileName(ProcessPath) : _processName;

    /// <summary>完整窗口标题；不可用时是明确的回退文案。</summary>
    public string WindowTitle => _windowTitle ?? "";

    public bool IsEmpty =>
        ProcessPath.Length == 0
        && ProcessName.Length == 0
        && WindowTitle.Length == 0;

    public static WindowInfo Empty => new("", "", "");

    public bool Equals(WindowInfo other) =>
        string.Equals(ProcessPath, other.ProcessPath, StringComparison.Ordinal)
        && string.Equals(ProcessName, other.ProcessName, StringComparison.Ordinal)
        && string.Equals(WindowTitle, other.WindowTitle, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is WindowInfo other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ProcessPath, ProcessName, WindowTitle);

    public static bool operator ==(WindowInfo left, WindowInfo right) => left.Equals(right);

    public static bool operator !=(WindowInfo left, WindowInfo right) => !left.Equals(right);
}

/// <summary>
/// 前台窗口解析 + 短期缓存。移植 v3 <c>ForegroundApp.ResolveWindowByHwnd</c>：
/// 按 HWND 查询进程路径、进程名和窗口标题，带 256 条 / 1500 ms 的 LRU 缓存。
///
/// 本类型只做 Win32 解析；分类、规则、应用聚合必须留在上层。
/// 所有跨进程调用（OpenProcess / GetWindowText）都应发生在后台写线程，
/// 绝不能放进低级钩子回调。
/// </summary>
public sealed class ForegroundApp
{
    /// <summary>缓存条数上限。</summary>
    public const int CacheCapacity = 256;

    /// <summary>缓存有效期（毫秒）。</summary>
    public const int CacheTtlMilliseconds = 1_500;

    public const string NoForegroundTitle = "无前台窗口";
    public const string UnknownWindowTitle = "无法识别的前台窗口";
    public const string UntitledWindowTitle = "无标题窗口";
    public const string SwitchingWindowTitle = "窗口切换中 / 无法归因";

    private sealed class CacheEntry
    {
        public WindowInfo Info;
        public long StampMs;
        public long LastUsed;
    }

    private readonly object _gate = new();
    private readonly Dictionary<IntPtr, CacheEntry> _cache = new();
    private readonly int _cacheCapacity;
    private readonly int _cacheTtlMilliseconds;
    private readonly Func<long> _timestampMilliseconds;
    private readonly Func<IntPtr> _foregroundWindow;
    private WindowResolver _resolveWindow;
    private long _useCounter;

    /// <param name="resolveWindow">原始解析器；为空时使用 Win32 实现。</param>
    /// <param name="cacheCapacity">缓存条数上限，必须为正数。</param>
    /// <param name="cacheTtlMilliseconds">缓存有效期，必须非负。</param>
    /// <param name="timestampMilliseconds">缓存时钟；测试可注入。默认使用 Stopwatch 毫秒。</param>
    /// <param name="foregroundWindow">当前前台窗口来源；测试可注入。默认调用 GetForegroundWindow。</param>
    public ForegroundApp(
        WindowResolver? resolveWindow = null,
        int cacheCapacity = CacheCapacity,
        int cacheTtlMilliseconds = CacheTtlMilliseconds,
        Func<long>? timestampMilliseconds = null,
        Func<IntPtr>? foregroundWindow = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheTtlMilliseconds);

        _resolveWindow = resolveWindow ?? ResolveWindowCore;
        _cacheCapacity = cacheCapacity;
        _cacheTtlMilliseconds = cacheTtlMilliseconds;
        _timestampMilliseconds = timestampMilliseconds ?? NowMilliseconds;
        _foregroundWindow = foregroundWindow ?? GetForegroundWindow;
    }

    /// <summary>
    /// 可替换的原始窗口解析委托。替换后会立即清空缓存，避免旧解析结果继续生效。
    /// </summary>
    public WindowResolver ResolveWindowByHwnd
    {
        get => _resolveWindow;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                _resolveWindow = value;
                _cache.Clear();
            }
        }
    }

    /// <summary>当前缓存条数（诊断 / 测试用）。</summary>
    public int CachedWindowCount
    {
        get
        {
            lock (_gate)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>带短期 LRU 缓存的解析入口。HWND 为零时直接返回「无前台窗口」。</summary>
    public WindowInfo Resolve(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return new WindowInfo("", "", NoForegroundTitle);
        }

        long now = _timestampMilliseconds();
        lock (_gate)
        {
            if (_cache.TryGetValue(hwnd, out CacheEntry? hit)
                && now >= hit.StampMs
                && now - hit.StampMs <= _cacheTtlMilliseconds)
            {
                hit.LastUsed = ++_useCounter;
                return hit.Info;
            }
        }

        WindowResolver resolver;
        lock (_gate)
        {
            resolver = _resolveWindow;
        }

        // 跨进程解析不持锁：目标进程卡住时不能阻塞其他窗口的解析。
        WindowInfo info = resolver(hwnd);

        lock (_gate)
        {
            _cache[hwnd] = new CacheEntry
            {
                Info = info,
                StampMs = now,
                LastUsed = ++_useCounter
            };
            if (_cache.Count > _cacheCapacity)
            {
                EvictOldest();
            }
        }

        return info;
    }

    /// <summary>
    /// 解析当前前台窗口。解析前后各取一次 HWND；若期间窗口已切换，返回明确的
    /// 「窗口切换中」结果，不把 A 的标题误归给 B。
    /// </summary>
    public WindowInfo Capture()
    {
        IntPtr hwnd = _foregroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return new WindowInfo("", "", NoForegroundTitle);
        }

        WindowInfo info = Resolve(hwnd);
        if (_foregroundWindow() != hwnd)
        {
            return new WindowInfo("", "", SwitchingWindowTitle);
        }

        return info;
    }

    /// <summary>清空窗口缓存。锁屏 / 会话切换后可主动调用。</summary>
    public void ClearWindowCache()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    /// <summary>移除单个窗口的缓存。</summary>
    public bool Invalidate(IntPtr hwnd)
    {
        lock (_gate)
        {
            return _cache.Remove(hwnd);
        }
    }

    private void EvictOldest()
    {
        // 256 条规模，线性寻找最久未用项即可；只在缓存满且新增时发生。
        IntPtr oldestKey = IntPtr.Zero;
        long oldestUse = long.MaxValue;
        bool found = false;
        foreach ((IntPtr key, CacheEntry entry) in _cache)
        {
            if (entry.LastUsed < oldestUse)
            {
                oldestUse = entry.LastUsed;
                oldestKey = key;
                found = true;
            }
        }

        if (found)
        {
            _cache.Remove(oldestKey);
        }
    }

    private static long NowMilliseconds() =>
        (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

    private static WindowInfo ResolveWindowCore(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return new WindowInfo("", "", NoForegroundTitle);
        }

        try
        {
            if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0)
            {
                return new WindowInfo("", "", UnknownWindowTitle);
            }

            string path = QueryProcessPath(pid);

            // UWP：ApplicationFrameHost.exe 只是宿主框，真正的窗口属于
            // Windows.UI.Core.CoreWindow 子窗口所属的进程。
            if (IsFrameHost(path))
            {
                IntPtr core = FindUwpCoreWindow(hwnd);
                if (core != IntPtr.Zero
                    && GetWindowThreadProcessId(core, out uint childPid) != 0
                    && childPid != 0)
                {
                    string real = QueryProcessPath(childPid);
                    if (!string.IsNullOrEmpty(real))
                    {
                        path = real;
                    }
                }
            }

            return new WindowInfo(path, PathNames.FileName(path), QueryWindowTitle(hwnd));
        }
        catch
        {
            return new WindowInfo("", "", UnknownWindowTitle);
        }
    }

    private static string QueryProcessPath(uint pid)
    {
        IntPtr process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            StringBuilder path = new(32768);
            uint size = (uint)path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string QueryWindowTitle(IntPtr hwnd)
    {
        StringBuilder buffer = new(1024);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.Length == 0 ? UntitledWindowTitle : buffer.ToString();
    }

    private static bool IsFrameHost(string path)
        => string.Equals(
            PathNames.FileName(path),
            "ApplicationFrameHost.exe",
            StringComparison.OrdinalIgnoreCase);

    private static IntPtr FindUwpCoreWindow(IntPtr frame)
    {
        lock (UwpGate)
        {
            _uwpChildFound = IntPtr.Zero;
            try
            {
                EnumChildWindows(frame, ChildProbe, IntPtr.Zero);
            }
            catch
            {
                // 枚举失败时按普通窗口处理，保留宿主进程路径。
            }

            return _uwpChildFound;
        }
    }

    private static bool ProbeUwpChild(IntPtr child, IntPtr lParam)
    {
        _ = lParam;
        try
        {
            StringBuilder name = new(256);
            if (GetClassName(child, name, name.Capacity) > 0
                && string.Equals(name.ToString(), "Windows.UI.Core.CoreWindow", StringComparison.Ordinal))
            {
                _uwpChildFound = child;
                return false; // 找到即停止枚举
            }
        }
        catch
        {
            // 单个子窗口失败继续枚举其余项。
        }

        return true;
    }

    private static readonly object UwpGate = new();
    private static readonly NativeEnumChildProc ChildProbe = ProbeUwpChild;
    private static IntPtr _uwpChildFound;

    private delegate bool NativeEnumChildProc(IntPtr hwnd, IntPtr lParam);

#pragma warning disable SYSLIB1054 // StringBuilder 取标题 / 路径无法直接用源生成器替代，保留 v3 的 DllImport。
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, NativeEnumChildProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenProcess(
        uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        uint flags,
        StringBuilder path,
        ref uint size);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
#pragma warning restore SYSLIB1054
}
