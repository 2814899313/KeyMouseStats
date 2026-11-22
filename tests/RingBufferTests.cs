// ============================================================================
//  tests/RingBufferTests.cs  ·  v3.0 Milestone 1 / Step 3
//
//  验证无锁 SPSC 环形缓冲区:
//   1. 顺序与载荷在单线程下完全正确;
//   2. 环绕边界(容量 4)反复写满再读空不出错;
//   3. 环满时立即丢弃并原子计数,**绝不阻塞**;
//   4. 真实两线程并发下 SPSC 顺序正确、无丢失(生产者让路式重试);
//   5. 入队/出队不产生堆分配(零 GC 压力)。
//
//  纯托管用例,不需要内嵌原生资源,也不触碰任何用户数据目录。
// ============================================================================

using System;
using System.Threading;
using KeyMouseStats;

internal static class RingBufferTests
{
    private static int _checks;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }

    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + "(期望 " + expected + ",实际 " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("RingBufferTests", Run);
    }

    private static RawInputEvent Make(long ticks, int kind, int vk, int x, int y, int delta)
    {
        RawInputEvent item = new RawInputEvent();
        item.TimestampTicks = ticks;
        item.Hwnd = new IntPtr(vk + 1);
        item.Kind = kind;
        item.VkOrButton = vk;
        item.X = x;
        item.Y = y;
        item.Delta = delta;
        return item;
    }

    private static void Run()
    {
        Sequential();
        WrapAround();
        Overflow();
        BackloggedThreshold();
        ConcurrentSpSc();
        HighPollingSustained();
        AllocationSmoke();
        Console.WriteLine("RingBufferTests: " + _checks + " 项断言通过");
    }

    /// <summary>1. 单线程顺序与载荷。</summary>
    private static void Sequential()
    {
        EventRing ring = new EventRing(8192);
        Equal(ring.PendingCount, 0, "初始应为空");

        for (int i = 0; i < 1000; i++)
        {
            RawInputEvent item = Make(i, InputEventKind.KeyDown, i, i * 2, i * 3, i);
            Check(ring.TryEnqueue(ref item), "第 " + i + " 条应入队成功");
        }
        Equal(ring.PendingCount, 1000, "积压数量");
        Equal(ring.EnqueuedCount, 1000, "入队计数");
        Equal(ring.DroppedCount, 0, "不应有丢弃");

        for (int i = 0; i < 1000; i++)
        {
            RawInputEvent item;
            Check(ring.TryDequeue(out item), "第 " + i + " 条应出队成功");
            Equal(item.VkOrButton, i, "出队顺序必须与入队一致");
            Equal(item.TimestampTicks, i, "时间戳应保真");
            Equal(item.X, i * 2, "X 应保真");
            Equal(item.Y, i * 3, "Y 应保真");
            Equal(item.Delta, i, "Delta 应保真");
            Check(item.Hwnd == new IntPtr(i + 1), "HWND 应保真");
        }

        RawInputEvent empty;
        Check(!ring.TryDequeue(out empty), "读空后应返回 false");
        Equal(ring.PendingCount, 0, "读空后积压为 0");
    }

    /// <summary>2. 环绕边界:容量被向上取整到 4,反复写满/读空。</summary>
    private static void WrapAround()
    {
        EventRing ring = new EventRing(4);
        Equal(ring.Capacity, 4, "容量应取整为 4");

        for (int round = 0; round < 50; round++)
        {
            for (int i = 0; i < 4; i++)
            {
                RawInputEvent item = Make(round * 10 + i, InputEventKind.Move, round * 10 + i, 0, 0, 0);
                Check(ring.TryEnqueue(ref item), "环绕第 " + round + " 轮第 " + i + " 条入队");
            }
            Equal(ring.PendingCount, 4, "写满后积压应为容量");

            for (int i = 0; i < 4; i++)
            {
                RawInputEvent item;
                Check(ring.TryDequeue(out item), "环绕出队");
                Equal(item.VkOrButton, round * 10 + i, "环绕后顺序仍正确");
            }
            Equal(ring.PendingCount, 0, "读空后积压为 0");
        }
    }

    /// <summary>3. 环满:立即丢弃 + 计数,不阻塞。</summary>
    private static void Overflow()
    {
        EventRing ring = new EventRing(8);
        Equal(ring.Capacity, 8, "容量 8");

        int accepted = 0;
        int rejected = 0;
        for (int i = 0; i < 20; i++)
        {
            RawInputEvent item = Make(i, InputEventKind.KeyDown, i, 0, 0, 0);
            if (ring.TryEnqueue(ref item)) accepted++;
            else rejected++;
        }

        Equal(accepted, 8, "只应接受容量条");
        Equal(rejected, 12, "其余应立即被拒绝");
        Equal(ring.DroppedCount, 12, "丢弃计数必须原子累加");
        Equal(ring.PendingCount, 8, "满仓时积压等于容量");

        // 环满后入队必须立即返回 false(不阻塞),并且仍然计入丢弃。
        RawInputEvent extra = Make(99, InputEventKind.KeyDown, 99, 0, 0, 0);
        Check(!ring.TryEnqueue(ref extra), "环满时入队必须立即返回 false");
        Equal(ring.DroppedCount, 13, "失败的入队也要计入丢弃");
    }

    /// <summary>3b. 默认容量与背压阈值:8 kHz 鼠标需要更大的默认环。</summary>
    private static void BackloggedThreshold()
    {
        EventRing ring = new EventRing();
        Equal(ring.Capacity, EventRing.DefaultCapacity, "默认容量应取 DefaultCapacity");
        Equal(EventRing.DefaultCapacity, 32768, "默认容量必须为 32768");
        for (int i = 0; i < 16384; i++)
        {
            RawInputEvent item = Make(i, InputEventKind.Move, 0, 0, 0, 1);
            Check(ring.TryEnqueue(ref item), "背压阈值填充 " + i);
        }
        Check(ring.IsBacklogged, "积压达到容量一半时应请求 Wake");
        Equal(ring.DroppedCount, 0, "阈值填充不应丢弃");
    }

    /// <summary>4. 真实两线程并发:生产者让路式重试保证零丢失,消费者校验顺序。</summary>
    private static void ConcurrentSpSc()
    {
        const int total = 200000;
        EventRing ring = new EventRing(65536);
        int produced = 0;

        Thread producer = new Thread(delegate()
        {
            for (int i = 0; i < total; i++)
            {
                RawInputEvent item = Make(i, InputEventKind.KeyDown, i, 0, 0, 0);
                int guard = 0;
                while (!ring.TryEnqueue(ref item))
                {
                    Thread.SpinWait(20);
                    guard++;
                    if (guard > 1000000) return;
                }
                Interlocked.Increment(ref produced);
            }
        });
        producer.IsBackground = true;
        producer.Start();

        int received = 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (received < total)
        {
            RawInputEvent item;
            if (ring.TryDequeue(out item))
            {
                if (item.VkOrButton != received)
                    throw new Exception("并发乱序:期望序号 " + received + ",实际 " + item.VkOrButton);
                received++;
            }
            else
            {
                Thread.SpinWait(20);
                if (DateTime.UtcNow > deadline)
                    throw new Exception("并发消费超时,已收到 " + received + "/" + total);
            }
        }

        producer.Join(5000);
        Equal(received, total, "并发下必须完整收到");
        Equal(produced, total, "生产者必须全部入队");
        Equal(ring.DroppedCount, 0, "让路式重试下不应有丢弃");
        _checks++;
    }

    /// <summary>
    /// 4b. 8 kHz × 10 秒持续压力:生产者每 200 ms 突发 1600 条,消费者每 200 ms 排空。
    /// 这是 DbWriter.Wake/WaitOne(200) 之后的真实节拍;默认 32768 环必须零丢弃。
    /// </summary>
    private static void HighPollingSustained()
    {
        const int bursts = 50;
        const int burstSize = 1600;
        const int total = bursts * burstSize;   // 80,000 = 8 kHz × 10 s
        EventRing ring = new EventRing();
        int produced = 0;
        int consumed = 0;
        Exception failure = null;
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);

        Thread producer = new Thread(delegate()
        {
            try
            {
                for (int b = 0; b < bursts; b++)
                {
                    for (int i = 0; i < burstSize; i++)
                    {
                        RawInputEvent item = Make(b * burstSize + i, InputEventKind.Move, 0, i, b, 1);
                        if (!ring.TryEnqueue(ref item))
                        {
                            failure = new Exception("8 kHz 压力下发生丢弃");
                            return;
                        }
                        produced++;
                    }
                    Thread.Sleep(200);
                }
            }
            catch (Exception error) { failure = error; }
        });

        Thread consumer = new Thread(delegate()
        {
            RawInputEvent[] buffer = new RawInputEvent[4096];
            while (consumed < total && DateTime.UtcNow <= deadline)
            {
                Thread.Sleep(200);
                int n;
                while ((n = ring.Drain(buffer, buffer.Length)) > 0) consumed += n;
            }
        });

        producer.IsBackground = true;
        consumer.IsBackground = true;
        producer.Start();
        consumer.Start();
        producer.Join(20000);
        consumer.Join(20000);

        Check(failure == null, "8 kHz 压力生产不得抛出异常");
        Equal(produced, total, "8 kHz × 10 秒应生产 80,000 条");
        Equal(consumed, total, "消费者应完整排空 80,000 条");
        Equal(ring.DroppedCount, 0, "8 kHz 持续移动下 DroppedCount 必须为 0");
        Equal(ring.PendingCount, 0, "消费结束后环应为空");
    }

    /// <summary>5. 零堆分配冒烟:二十万次入队/出队不应带来堆增长。</summary>
    private static void AllocationSmoke()
    {
        EventRing ring = new EventRing(1024);
        RawInputEvent item = Make(0, InputEventKind.KeyDown, 65, 1, 2, 3);

        // 预热,触发 JIT 与首次分配。
        for (int i = 0; i < 2000; i++)
        {
            ring.TryEnqueue(ref item);
            RawInputEvent out1;
            ring.TryDequeue(out out1);
        }

        long before = GC.GetTotalMemory(true);
        for (int i = 0; i < 200000; i++)
        {
            item.TimestampTicks = i;
            ring.TryEnqueue(ref item);
            RawInputEvent out2;
            if (!ring.TryDequeue(out out2)) break;
        }
        long after = GC.GetTotalMemory(true);
        long growth = after - before;

        Check(growth < 262144, "入队/出队不应产生堆增长,实际增长 " + growth + " B");
    }
}
