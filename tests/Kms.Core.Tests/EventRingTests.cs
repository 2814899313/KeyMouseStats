using Kms.Core.Input;

namespace Kms.Core.Tests;

public sealed class EventRingTests
{
    [Fact]
    public void Capacity_RoundsToPowerOfTwo()
    {
        Assert.Equal(2, new EventRing(1).Capacity);
        Assert.Equal(8, new EventRing(5).Capacity);
        Assert.Equal(32768, new EventRing().Capacity);
    }

    [Fact]
    public void TryEnqueue_Dequeue_PreservesOrder()
    {
        EventRing ring = new(8);
        for (int i = 1; i <= 3; i++)
        {
            Assert.True(ring.TryEnqueue(Event(i)));
        }

        Assert.Equal(3, ring.PendingCount);
        Assert.True(ring.TryDequeue(out RawInputEvent first));
        Assert.Equal(1, first.VkOrButton);
        Assert.True(ring.TryDequeue(out RawInputEvent second));
        Assert.Equal(2, second.VkOrButton);
    }

    [Fact]
    public void FullRing_DropsWithoutBlocking()
    {
        EventRing ring = new(2);
        Assert.True(ring.TryEnqueue(Event(1)));
        Assert.True(ring.TryEnqueue(Event(2)));
        Assert.False(ring.TryEnqueue(Event(3)));

        Assert.Equal(1, ring.DroppedCount);
        Assert.Equal(2, ring.EnqueuedCount);
    }

    [Fact]
    public void Drain_StopsAtAvailableAndMax()
    {
        EventRing ring = new(8);
        for (int i = 1; i <= 5; i++)
        {
            ring.TryEnqueue(Event(i));
        }

        RawInputEvent[] buffer = new RawInputEvent[4];
        Assert.Equal(3, ring.Drain(buffer, 3));
        Assert.Equal(1, buffer[0].VkOrButton);
        Assert.Equal(3, buffer[2].VkOrButton);
        Assert.Equal(2, ring.PendingCount);
    }

    private static RawInputEvent Event(int value) =>
        new(timestampTicks: value, hwnd: IntPtr.Zero, kind: InputEventKind.KeyDown, vkOrButton: value, x: 0, y: 0, delta: 0);
}
