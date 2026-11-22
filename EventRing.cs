// ============================================================================
//  键鼠统计 v3.0  Milestone 1 / Step 3  EventRing.cs
//  无锁 SPSC(单生产者-单消费者)环形缓冲区 + 值类型事件。
//
//  为什么是 SPSC:
//   低级键盘钩子、鼠标钩子、Raw Input 分发、250ms 定时器全部在**同一个 UI 线程**
//    上回调(钩子装在装它的线程),所以生产者天然唯一;
//    消费者是专用后台写线程。SPSC 因此不需要任何锁。
//
//  硬要求:
//   · **零堆分配**: RawInputEvent 是 struct,槽位数组在构造时一次性分配;
//     入队只做一次结构体拷贝 + 两个 volatile 写。
//   · **环满不阻塞**: 满了立即丢弃并原子递增 _dropped。钩子回调耗时必须维持在
//     亚微秒级 —— 宁可丢样本,也不能让输入延迟、被 LowLevelHooksTimeout 摘钩。
//   · 计数用 unchecked(int 差): 差值恒在 [0, capacity] 内,自然跨越 int 溢出,
//     不需要每 2^31 次事件做一次昂贵的 64 位原子读。
//
//  C# 5: 不使用 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Threading;

namespace KeyMouseStats
{
    /// <summary>事件种类。与 RawInputEvent.Kind 对应。</summary>
    internal static class InputEventKind
    {
        internal const int KeyDown = 1;
        internal const int KeyUp = 2;
        internal const int MouseDown = 3;
        internal const int Wheel = 4;
        internal const int Move = 5;

        internal static bool IsValid(int kind)
        {
            return kind >= KeyDown && kind <= Move;
        }
    }

    /// <summary>
    /// 一次原始输入事件。必须是值类型:入环时只做一次拷贝,不产生 GC 压力。
    /// </summary>
    // 字段由生产者(钩子)赋值,消费者(写线程)读取。清爽版没有消费者,
    // 编译器会认为这些字段「从未被赋值」而报 CS0649 —— 属误报,这里精确抑制。
    // 注意:C# 5 不允许结构体字段初始化器,所以不能靠 = 0 消除。
#pragma warning disable 0649
    internal struct RawInputEvent
    {
        /// <summary>Stopwatch.GetTimestamp() 的时刻;写线程用锚点换算成墙钟毫秒。</summary>
        public long TimestampTicks;
        /// <summary>事件发生瞬间的前台窗口句柄(GetForegroundWindow 的廉价结果)。</summary>
        public IntPtr Hwnd;
        /// <summary>InputEventKind.*</summary>
        public int Kind;
        /// <summary>按键事件的虚拟键码,或鼠标事件的键位序号(1=左 2=右 3=中 4/5=侧)。</summary>
        public int VkOrButton;
        /// <summary>屏幕坐标 X(鼠标事件);按键事件为 0。</summary>
        public int X;
        /// <summary>屏幕坐标 Y(鼠标事件);按键事件为 0。</summary>
        public int Y;
        /// <summary>滚轮增量(Move 事件为本次移动的像素增量,取整)。</summary>
        public int Delta;
    }
#pragma warning restore 0649

    /// <summary>无锁 SPSC 环形缓冲区。</summary>
    internal sealed class EventRing
    {
        /// <summary>默认容量。32768 槽 × 约 40 字节 ≈ 1.25 MB,覆盖 8 kHz 鼠标 1 秒以上的突发。</summary>
        internal const int DefaultCapacity = 32768;

        private readonly RawInputEvent[] _slots;
        private readonly int _mask;
        private readonly int _capacity;

        /// <summary>生产者推进的写下标。只有生产者写,消费者读。</summary>
        private volatile int _write;
        /// <summary>消费者推进的读下标。只有消费者写,生产者读。</summary>
        private volatile int _read;

        private long _dropped;
        private long _enqueued;

        internal EventRing() : this(DefaultCapacity)
        {
        }

        internal EventRing(int capacity)
        {
            if (capacity < 2) capacity = 2;
            int rounded = 1;
            while (rounded < capacity) rounded <<= 1;
            _capacity = rounded;
            _mask = rounded - 1;
            _slots = new RawInputEvent[rounded];
        }

        internal int Capacity { get { return _capacity; } }

        /// <summary>当前积压数量(近似值:并发下只是快照)。</summary>
        internal int PendingCount
        {
            get { return unchecked(_write - _read); }
        }

        /// <summary>积压达到容量一半。写线程应在此时被提前唤醒。</summary>
        internal bool IsBacklogged { get { return PendingCount >= (_capacity >> 1); } }

        /// <summary>因环满被丢弃的事件总数(原子读)。</summary>
        internal long DroppedCount { get { return Interlocked.Read(ref _dropped); } }

        /// <summary>成功入队的事件总数。</summary>
        internal long EnqueuedCount { get { return Interlocked.Read(ref _enqueued); } }

        /// <summary>
        /// 生产者入队。**绝不阻塞**:环满时丢弃并计数,返回 false。
        /// </summary>
        internal bool TryEnqueue(ref RawInputEvent item)
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

        /// <summary>消费者出队。空返回 false。</summary>
        internal bool TryDequeue(out RawInputEvent item)
        {
            int read = _read;
            if (read == _write)
            {
                item = default(RawInputEvent);
                return false;
            }
            item = _slots[read & _mask];
            _read = unchecked(read + 1);
            return true;
        }

        /// <summary>
        /// 批量出队到调用方预分配的数组。返回实际取出条数。
        /// 数组由调用方复用,本方法不分配。
        /// </summary>
        internal int Drain(RawInputEvent[] buffer, int max)
        {
            if (buffer == null || max <= 0) return 0;
            if (max > buffer.Length) max = buffer.Length;
            int read = _read;
            int available = unchecked(_write - read);
            if (available <= 0) return 0;
            if (max > available) max = available;
            for (int i = 0; i < max; i++)
            {
                buffer[i] = _slots[(read + i) & _mask];
            }
            _read = unchecked(read + max);
            return max;
        }

        /// <summary>测试用:清空并复位计数。运行期不要调用。</summary>
        internal void Clear()
        {
            _write = 0;
            _read = 0;
            Interlocked.Exchange(ref _dropped, 0);
            Interlocked.Exchange(ref _enqueued, 0);
        }
    }
}
