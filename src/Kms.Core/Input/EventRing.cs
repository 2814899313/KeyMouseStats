using System.Threading;

namespace Kms.Core.Input;

/// <summary>事件种类。与 <see cref="RawInputEvent.Kind"/> 对应。</summary>
public static class InputEventKind
{
    public const int KeyDown = 1;
    public const int KeyUp = 2;
    public const int MouseDown = 3;
    public const int Wheel = 4;
    public const int Move = 5;

    public static bool IsValid(int kind) => kind is >= KeyDown and <= Move;
}

/// <summary>
/// 一次原始输入事件。必须是值类型：入环时只做一次拷贝，不产生 GC 压力。
/// 移植自 v3 <c>EventRing.cs</c>，字段语义逐一对应。
/// </summary>
public readonly struct RawInputEvent
{
    public RawInputEvent(
        long timestampTicks,
        IntPtr hwnd,
        int kind,
        int vkOrButton,
        int x,
        int y,
        int delta)
    {
        TimestampTicks = timestampTicks;
        Hwnd = hwnd;
        Kind = kind;
        VkOrButton = vkOrButton;
        X = x;
        Y = y;
        Delta = delta;
    }

    /// <summary><c>Stopwatch.GetTimestamp()</c> 的时刻；写线程用锚点换算成墙钟毫秒。</summary>
    public long TimestampTicks { get; }

    /// <summary>事件发生瞬间的前台窗口句柄。</summary>
    public IntPtr Hwnd { get; }

    /// <summary><see cref="InputEventKind"/> 之一。</summary>
    public int Kind { get; }

    /// <summary>按键事件的虚拟键码，或鼠标事件的键位序号（1=左 2=右 3=中 4/5=侧）。</summary>
    public int VkOrButton { get; }

    /// <summary>屏幕坐标 X（鼠标事件）；按键事件为 0。</summary>
    public int X { get; }

    /// <summary>屏幕坐标 Y（鼠标事件）；按键事件为 0。</summary>
    public int Y { get; }

    /// <summary>滚轮增量（Move 事件为本次移动的像素增量，取整）。</summary>
    public int Delta { get; }
}

/// <summary>
/// 无锁 SPSC（单生产者-单消费者）环形缓冲区，移植自 v3 <c>EventRing.cs</c>。
///
/// 生产者：钩子 / Raw Input 分发所在线程；消费者：<c>DbWriter</c> 专用写线程。
/// 环满时立即丢弃并原子计数，绝不阻塞输入热路径。
/// </summary>
public sealed class EventRing
{
    /// <summary>默认容量。32768 槽覆盖 8 kHz 鼠标 1 秒以上的突发。</summary>
    public const int DefaultCapacity = 32768;

    private readonly RawInputEvent[] _slots;
    private readonly int _mask;
    private readonly int _capacity;

    /// <summary>生产者推进的写下标；只有生产者写，消费者读。</summary>
    private volatile int _write;

    /// <summary>消费者推进的读下标；只有消费者写，生产者读。</summary>
    private volatile int _read;

    private long _dropped;
    private long _enqueued;

    public EventRing()
        : this(DefaultCapacity)
    {
    }

    public EventRing(int capacity)
    {
        if (capacity < 2)
        {
            capacity = 2;
        }

        int rounded = 2;
        while (rounded < capacity)
        {
            rounded <<= 1;
        }

        _capacity = rounded;
        _mask = rounded - 1;
        _slots = new RawInputEvent[rounded];
    }

    public int Capacity => _capacity;

    /// <summary>当前积压数量（近似值：并发下只是快照）。</summary>
    public int PendingCount => unchecked(_write - _read);

    /// <summary>积压达到容量一半。写线程应在此时被提前唤醒。</summary>
    public bool IsBacklogged => PendingCount >= (_capacity >> 1);

    /// <summary>因环满被丢弃的事件总数（原子读）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>成功入队的事件总数。</summary>
    public long EnqueuedCount => Interlocked.Read(ref _enqueued);

    /// <summary>
    /// 生产者入队。绝不阻塞：环满时丢弃并计数，返回 <c>false</c>。
    /// </summary>
    public bool TryEnqueue(in RawInputEvent item)
    {
        int write = _write;
        int used = unchecked(write - _read);
        if (used >= _capacity)
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }

        _slots[write & _mask] = item;
        // volatile 写保证槽位内容先于写下标对外可见。
        _write = unchecked(write + 1);
        Interlocked.Increment(ref _enqueued);
        return true;
    }

    /// <summary>消费者出队。空返回 <c>false</c>。</summary>
    public bool TryDequeue(out RawInputEvent item)
    {
        int read = _read;
        if (read == _write)
        {
            item = default;
            return false;
        }

        item = _slots[read & _mask];
        _read = unchecked(read + 1);
        return true;
    }

    /// <summary>
    /// 批量出队到调用方预分配的数组。返回实际取出条数。
    /// 数组由调用方复用，本方法不分配。
    /// </summary>
    public int Drain(RawInputEvent[] buffer, int max)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (max <= 0)
        {
            return 0;
        }

        if (max > buffer.Length)
        {
            max = buffer.Length;
        }

        int read = _read;
        int available = unchecked(_write - read);
        if (available <= 0)
        {
            return 0;
        }

        if (max > available)
        {
            max = available;
        }

        for (int i = 0; i < max; i++)
        {
            buffer[i] = _slots[(read + i) & _mask];
        }

        _read = unchecked(read + max);
        return max;
    }

    /// <summary>测试用：清空并复位计数。运行期不要调用。</summary>
    public void Clear()
    {
        _write = 0;
        _read = 0;
        Interlocked.Exchange(ref _dropped, 0);
        Interlocked.Exchange(ref _enqueued, 0);
    }
}
