using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Ingestion;

namespace LogWatcher.Benchmarks;

// InvocationCount=1000: each BDN iteration runs exactly 1000 publish calls.
// For Publish_BusHasCapacity this is well below the 100_000-item capacity, so
// the bus never fills mid-iteration. For Publish_BusFull the bus stays full
// throughout all 1000 invocations (pre-filled by IterationSetup).
[MemoryDiagnoser]
[SimpleJob(invocationCount: 1000)]
public class BoundedEventBusBenchmarks
{
    // Large enough that the benchmark never fills it during a normal run.
    private BoundedEventBus<FsEvent> _bus = null!;

    // Small bus used to measure publish behavior when the bus is at capacity.
    private BoundedEventBus<FsEvent> _fullBus = null!;

    private FsEvent _event;

    [GlobalSetup]
    public void Setup()
    {
        _event = new FsEvent(
            FsEventKind.Modified,
            "/tmp/app.log",
            null,
            DateTimeOffset.UtcNow,
            true);

        // 100_000 capacity ensures the bus never fills during a single iteration
        // (InvocationCount=1000 on Publish_BusHasCapacity is well below this).
        _bus = new BoundedEventBus<FsEvent>(100_000);
        _fullBus = new BoundedEventBus<FsEvent>(1);
    }

    // Drain _bus fully before each Publish_BusHasCapacity iteration so accumulated
    // items from previous iterations don't push the bus toward capacity.
    [IterationSetup(Target = nameof(Publish_BusHasCapacity))]
    public void DrainBusForCapacity()
    {
        while (_bus.TryDequeue(out _, 0)) { }
    }

    // Drain then pre-fill _fullBus before each Publish_BusFull iteration so every
    // publish call during measurement hits a full bus and drops immediately.
    [IterationSetup(Target = nameof(Publish_BusFull))]
    public void PreFillBus()
    {
        while (_fullBus.TryDequeue(out _, 0)) { }
        _fullBus.Publish(_event);
    }

    /// <summary>
    /// Happy path: bus always has room. Measures the cost of a successful enqueue.
    /// InvocationCount=1000 (class-level SimpleJob) is well below the 100_000-item
    /// capacity, so the bus never fills during a single BDN iteration.
    /// </summary>
    [Benchmark]
    public bool Publish_BusHasCapacity() => _bus.Publish(_event);

    /// <summary>
    /// Drop-newest path: bus is at capacity. Publish must return immediately without blocking.
    /// Expected: near-zero latency (INV BP-006).
    /// </summary>
    [Benchmark]
    public bool Publish_BusFull() => _fullBus.Publish(_event);

    /// <summary>
    /// Dequeue from a bus that contains one item.
    /// </summary>
    [Benchmark]
    public bool TryDequeue_WithItem()
    {
        _bus.Publish(_event);
        return _bus.TryDequeue(out _, 0);
    }
}