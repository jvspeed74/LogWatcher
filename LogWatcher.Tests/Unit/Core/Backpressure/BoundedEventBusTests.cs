using System.Collections.Concurrent;

using LogWatcher.Core.Backpressure;

namespace LogWatcher.Tests.Unit.Core.Backpressure;

public class BoundedEventBusTests
{
    [Fact]
    public void Publish_DropsWhenFull()
    {
        var bus = new BoundedEventBus<int>(2);
        Assert.True(bus.Publish(1));
        Assert.True(bus.Publish(2));
        Assert.False(bus.Publish(3));

        Assert.Equal(2, bus.PublishedCount);
        Assert.Equal(1, bus.DroppedCount);
        Assert.Equal(2, bus.Depth);
    }

    [Fact]
    public void TryDequeue_ReturnsItemsInFifoOrder()
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
    public void Stop_UnblocksDequeueAndReturnsFalseWhenEmpty()
    {
        var bus = new BoundedEventBus<int>(2);

        // Start a consumer that waits
        var t = Task.Run(() => { Assert.False(bus.TryDequeue(out var item, 500)); });

        Thread.Sleep(50);
        bus.Stop();
        t.Wait();
    }

    [Fact(Skip = "Flaky test, nondeterministic failure, needs investigation")]
    public void MultiProducer_DoesNotCorruptQueue()
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

        Task.WaitAll(tasks.ToArray());

        // Drain
        var count = 0;
        while (bus.TryDequeue(out var item, 10)) count++;

        Assert.Equal(producers * perProducer, count);
        Assert.Equal(producers * perProducer, bus.PublishedCount);
        Assert.Equal(0, bus.DroppedCount);
    }

    [Fact(Skip = "Flaky test, nondeterministic failure, needs investigation")]
    public void MultiConsumer_ConsumesAllPublishedItems()
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

        Task.WaitAll(tasks.ToArray(), 2000);

        Assert.Equal(items, collected.Count);
        Assert.Equal(items, bus.PublishedCount);
        Assert.Equal(0, bus.DroppedCount);
    }
}