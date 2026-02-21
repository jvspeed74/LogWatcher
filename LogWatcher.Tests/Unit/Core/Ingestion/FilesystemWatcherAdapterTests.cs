using System.Diagnostics;

using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Ingestion;

namespace LogWatcher.Tests.Unit.Core.Ingestion;

public class FilesystemWatcherAdapterTests : IDisposable
{
    private readonly string _dir;

    public FilesystemWatcherAdapterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "watchstats_watcher_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    // TODO: map to invariant
    [Fact]
    public void Start_WhenLogFileCreated_PublishesEventToBus()
    {
        var bus = new BoundedEventBus<FsEvent>(1000);
        using var adapter = new FilesystemWatcherAdapter(_dir, bus);
        adapter.Start();

        var path = Path.Combine(_dir, "x.log");
        File.WriteAllText(path, "hello");

        // Wait up to 2s for event propagation
        var attempts = 0;
        while (bus.PublishedCount == 0 && attempts++ < 20) Thread.Sleep(100);

        adapter.Stop();

        Assert.True(bus.PublishedCount > 0);
    }

    [Fact]
    [Invariant("ING-003")]
    public void Start_WhenNonProcessableFileCreated_PublishesEventWithProcessableFalse()
    {
        var bus = new BoundedEventBus<FsEvent>(1000);
        using var adapter = new FilesystemWatcherAdapter(_dir, bus);
        adapter.Start();

        var path = Path.Combine(_dir, "x.dat");
        File.WriteAllText(path, "data");

        // Wait up to 2s for event propagation
        var attempts = 0;
        while (bus.PublishedCount == 0 && attempts++ < 20) Thread.Sleep(100);

        adapter.Stop();

        Assert.True(bus.PublishedCount > 0);

        // Events for non-.log/.txt extensions must have Processable=false
        var verified = 0;
        while (bus.TryDequeue(out var ev, 10))
        {
            Assert.False(ev.Processable);
            verified++;
        }

        Assert.True(verified > 0, "Expected at least one event to verify Processable=false");
    }

    [Fact]
    [Invariant("ING-001")]
    [Invariant("ING-002")]
    public void WatcherCallbacks_WhenBusIsFull_DropEventsSilentlyWithoutBlocking()
    {
        // ING-001: watcher callbacks must not perform IO, block, or do heavy computation.
        // The Publish call inside each callback is non-blocking — it returns immediately whether
        // the event is accepted or dropped.
        // ING-002: a publish failure due to a full bus is silent; the watcher never retries or blocks.
        var bus = new BoundedEventBus<FsEvent>(1);
        bus.Publish(new FsEvent(FsEventKind.Created, "prefill.log", null, DateTimeOffset.UtcNow, true));

        // Every subsequent publish will be dropped because the bus is at capacity
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            var accepted = bus.Publish(
                new FsEvent(FsEventKind.Modified, $"file{i}.log", null, DateTimeOffset.UtcNow, true));
            Assert.False(accepted, "Event must be silently dropped when bus is full");
        }

        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, "Publish must not block when bus is full");
        Assert.Equal(1, bus.PublishedCount);
        Assert.Equal(50, bus.DroppedCount);
    }
}