using System.Collections.Concurrent;

namespace Kms.Input.Counting;

/// <summary>
/// 钩子线程与统计线程之间的有界队列。
///
/// 低级钩子回调在专用消息线程（见 <see cref="Hooks.HookThreadHost"/>）；
/// 直接改 <c>StatsData</c> 会和 UI 枚举 <c>History</c> 撞车，所以回调只入队，
/// UI 线程定时 <see cref="Drain"/> 后再走 <see cref="CountingInputSink"/>。
///
/// 队列满时丢弃并计数：钩子回调绝不能阻塞系统输入链。
/// </summary>
public sealed class QueuedInputSink : IInputSink
{
    public const int DefaultCapacity = 32_768;

    private enum EventKind : byte
    {
        Key,
        MouseButton,
        Wheel,
        Move
    }

    private readonly record struct HookEvent(
        EventKind Kind,
        int A,
        int B,
        int Scan,
        uint Flags,
        int X,
        int Y,
        long Ticks);

    private readonly ConcurrentQueue<HookEvent> _queue = new();
    private readonly int _capacity;
    private int _pending;
    private long _received;
    private long _receivedKeys;
    private long _dropped;

    public QueuedInputSink(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(256, capacity);
    }

    /// <summary>入队的事件总数（含被丢弃的）。</summary>
    public long Received => Interlocked.Read(ref _received);

    /// <summary>入队的键盘事件数（按下 + 抬起）。</summary>
    public long ReceivedKeys => Interlocked.Read(ref _receivedKeys);

    /// <summary>因队列满被丢弃的事件数。</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    public int Pending => Volatile.Read(ref _pending);

    public void Key(int virtualKey, bool down, long ticks) =>
        KeyEx(virtualKey, 0, 0, down, ticks);

    public void KeyEx(int virtualKey, int scanCode, uint flags, bool down, long ticks)
    {
        Interlocked.Increment(ref _receivedKeys);
        Enqueue(new HookEvent(EventKind.Key, virtualKey, down ? 1 : 0, scanCode, flags, 0, 0, ticks));
    }

    public void MouseButton(int button, bool down, int x, int y, long ticks) =>
        Enqueue(new HookEvent(EventKind.MouseButton, button, down ? 1 : 0, 0, 0, x, y, ticks));

    public void Wheel(bool horizontal, int delta, long ticks) =>
        Enqueue(new HookEvent(EventKind.Wheel, horizontal ? 1 : 0, delta, 0, 0, 0, 0, ticks));

    public void Move(int x, int y, long ticks) =>
        Enqueue(new HookEvent(EventKind.Move, 0, 0, 0, 0, x, y, ticks));

    /// <summary>把队列交给 <paramref name="target"/>；返回本次处理条数。</summary>
    public int Drain(IInputSink target, int limit = 8_192)
    {
        ArgumentNullException.ThrowIfNull(target);

        int processed = 0;
        while (processed < limit && _queue.TryDequeue(out HookEvent item))
        {
            Interlocked.Decrement(ref _pending);
            switch (item.Kind)
            {
                case EventKind.Key:
                    target.KeyEx(item.A, item.Scan, item.Flags, item.B != 0, item.Ticks);
                    break;
                case EventKind.MouseButton:
                    target.MouseButton(item.A, item.B != 0, item.X, item.Y, item.Ticks);
                    break;
                case EventKind.Wheel:
                    target.Wheel(item.A != 0, item.B, item.Ticks);
                    break;
                case EventKind.Move:
                    target.Move(item.X, item.Y, item.Ticks);
                    break;
            }

            processed++;
        }

        return processed;
    }

    private void Enqueue(in HookEvent item)
    {
        Interlocked.Increment(ref _received);
        if (Volatile.Read(ref _pending) >= _capacity)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        _queue.Enqueue(item);
        Interlocked.Increment(ref _pending);
    }
}
