using System.Collections.Concurrent;
using System.Diagnostics;

using LogWatcher.Core.Backpressure;

namespace LogWatcher.Tests.Unit.Core.Backpressure;

public class BoundedEventBusTests
{
    [Fact]
    [Invariant("BP-001")]
    [Invariant("BP-002")]
    [Invariant("BP-003")]
    [Invariant("BP-004")]
    public void Publish_WhenBusFull_DropsEvent()
    {
        var bus = new BoundedEventBus<int>(2);
        Assert.True(bus.Publish(1));
        Assert.True(bus.Publish(2));
        Assert.False(bus.Publish(3));

        Assert.Equal(2, bus.PublishedCount);
        Assert.Equal(1, bus.DroppedCount);
        Assert.Equal(2, bus.Depth);
    }

    // TODO: map to invariant
    [Fact]
    public void TryDequeue_WithMultiplePublishedItems_ReturnsInFifoOrder()
    {
        var bus = new BoundedEventBus<int>(10);
        bus.Publish(10);
        bus.Publish(20);

        Assert.True(bus.TryDequeue(out var a, 10));
        Assert.True(bus.TryDequeue(out var b, 10));
        Assert.Equal(10, a);
        Assert.Equal(20, b);
    }

    [Fact]
    [Invariant("BP-005")]
    public async Task Stop_WhenCalled_UnblocksConsumerAndReturnsFalse()
    {
        var bus = new BoundedEventBus<int>(2);

        // Start a consumer that waits
        var t = Task.Run(() => { Assert.False(bus.TryDequeue(out int _, 500)); });

        Thread.Sleep(50);
        bus.Stop();
        await t;
    }

    [Fact]
    [Invariant("BP-004")]
    public async Task MultipleProducers_ConcurrentPublish_AllItemsEnqueued()
    {
        var bus = new BoundedEventBus<int>(10000);
        var producers = 4;
        var perProducer = 1000;
        var tasks = new List<Task>();

        for (var p = 0; p < producers; p++)
        {
            var id = p;
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < perProducer; i++) bus.Publish(id * perProducer + i);
            }));
        }

        await Task.WhenAll(tasks);

        // Drain
        var count = 0;
        while (bus.TryDequeue(out int _, 10)) count++;

        Assert.Equal(producers * perProducer, count);
        Assert.Equal(producers * perProducer, bus.PublishedCount);
        Assert.Equal(0, bus.DroppedCount);
    }

    [Fact]
    [Invariant("BP-006")]
    public void Publish_WhenBusFull_ReturnsImmediatelyWithoutBlocking()
    {
        var bus = new BoundedEventBus<int>(1);
        bus.Publish(1); // fill bus to capacity

        // Publish when full must not block; it must drop and return immediately
        var sw = Stopwatch.StartNew();
        var result = bus.Publish(2);
        sw.Stop();

        Assert.False(result); // dropped
        Assert.True(sw.ElapsedMilliseconds < 200, "Publish must not block waiting for queue capacity");
    }

    [Fact]
    [Invariant("BP-004")]
    public async Task MultipleConsumers_ConcurrentDequeue_ConsumesAllItems()
    {
        var bus = new BoundedEventBus<int>(10000);
        var items = 10000;
        for (var i = 0; i < items; i++) bus.Publish(i);

        var consumers = 4;
        var collected = new ConcurrentBag<int>();
        var tasks = new List<Task>();
        for (var c = 0; c < consumers; c++)
            tasks.Add(Task.Run(() =>
            {
                while (bus.TryDequeue(out var v, 10)) collected.Add(v);
            }));

        await Task.WhenAll(tasks);

        Assert.Equal(items, collected.Count);
        Assert.Equal(items, bus.PublishedCount);
        Assert.Equal(0, bus.DroppedCount);
    }

    [Fact]
    [Invariant("BP-003")]
    [Invariant("BP-005")]
    public void Publish_AfterStop_ReturnsFalseAndDoesNotIncrementDropped()
    {
        // Post-Stop publishes must not be counted as capacity drops (BP-003):
        // a stopped bus is not a full bus.
        var bus = new BoundedEventBus<int>(10);
        bus.Publish(1);
        bus.Stop();

        var result = bus.Publish(2);

        Assert.False(result);
        Assert.Equal(0, bus.DroppedCount); // Stop is not a capacity event
        Assert.Equal(1, bus.PublishedCount);
    }

    [Fact]
    [Invariant("BP-005")]
    public void Stop_ItemsPublishedBeforeStop_CanStillBeDrained()
    {
        // BP-005: "may still drain remaining items already in the queue before returning false"
        var bus = new BoundedEventBus<int>(10);
        bus.Publish(1);
        bus.Publish(2);
        bus.Stop();

        Assert.True(bus.TryDequeue(out var a, 0));
        Assert.True(bus.TryDequeue(out var b, 0));
        Assert.Equal(1, a);
        Assert.Equal(2, b);
        // Queue now empty and stopped — next dequeue must return false
        Assert.False(bus.TryDequeue(out _, 0));
    }

    [Fact]
    [Invariant("BP-005")]
    public void Stop_CalledTwice_DoesNotThrow()
    {
        var bus = new BoundedEventBus<int>(1);
        bus.Stop();
        var ex = Record.Exception(() => bus.Stop());
        Assert.Null(ex);
    }

    [Fact]
    public void TryDequeue_EmptyBus_ReturnsFalseAfterTimeout()
    {
        var bus = new BoundedEventBus<int>(10);
        var sw = Stopwatch.StartNew();
        var result = bus.TryDequeue(out _, 50);
        sw.Stop();

        Assert.False(result);
        // Must have waited approximately the timeout (at least 40 ms) and not block indefinitely
        Assert.True(sw.ElapsedMilliseconds >= 40, "TryDequeue must wait for timeout before returning");
        Assert.True(sw.ElapsedMilliseconds < 5000, "TryDequeue must not block indefinitely");
    }

    [Fact]
    public void TryDequeue_ZeroTimeout_ReturnsImmediatelyWhenEmpty()
    {
        var bus = new BoundedEventBus<int>(10);
        var sw = Stopwatch.StartNew();
        var result = bus.TryDequeue(out _, 0);
        sw.Stop();

        Assert.False(result);
        Assert.True(sw.ElapsedMilliseconds < 50, "Zero-timeout TryDequeue must return immediately");
    }

    [Fact]
    public void Constructor_ZeroCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedEventBus<int>(0));
    }

    [Fact]
    public void Constructor_NegativeCapacity_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedEventBus<int>(-1));
    }
}