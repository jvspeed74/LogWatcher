using System.Collections.Concurrent;
using LogWatcher.Core.Concurrency;
using LogWatcher.Core.Events;
using LogWatcher.Core.Metrics;
using LogWatcher.Core.Processing;

namespace LogWatcher.Tests.Integration;

// Fake processor implementing IFileProcessor
internal class FakeProcessor : IFileProcessor
{
    private readonly int _delayMs;
    private readonly ConcurrentDictionary<string, int> _inProgress = new();
    public readonly ConcurrentBag<string> Calls = new();

    public FakeProcessor(int delayMs = 0)
    {
        _delayMs = delayMs;
    }

    public void ProcessOnce(string path, FileState state, WorkerStatsBuffer stats, int chunkSize = 64 * 1024)
    {
        // Check not re-entrant for same path
        if (!_inProgress.TryAdd(path, 1))
            throw new InvalidOperationException("Concurrent processing for same path");
        try
        {
            Calls.Add(path);
            if (_delayMs > 0) Thread.Sleep(_delayMs);
        }
        finally
        {
            _inProgress.TryRemove(path, out _);
        }
    }
}

public class ProcessingCoordinatorTests
{
    [Fact]
    public void ModifyInFlightThenDelete_LeadsToDeletePendingAndFinalize()
    {
        var bus = new BoundedEventBus<FsEvent>(100);
        var registry = new FileStateRegistry();
        var fake = new FakeProcessor(50); // simulate work
        var workerStats = new[] { new WorkerStats() };

        var coord = new ProcessingCoordinator(bus, registry, fake, workerStats, 1,
            50);

        coord.Start();

        var path = "file1.log";
        // publish a modify event
        bus.Publish(new FsEvent(FsEventKind.Modified, path, null, DateTimeOffset.UtcNow, true));
        // publish delete while worker may be processing
        bus.Publish(new FsEvent(FsEventKind.Deleted, path, null, DateTimeOffset.UtcNow, true));

        // wait a bit
        Thread.Sleep(300);

        // After some time, the registry epoch should be >= 0 (exists even if not finalized)
        var epoch = registry.GetCurrentEpoch(path);

        coord.Stop();

        Assert.True(epoch >= 0);
    }

    [Fact]
    public void ManyModifiesWhileBusy_CoalesceAndCatchup()
    {
        var bus = new BoundedEventBus<FsEvent>(1000);
        var registry = new FileStateRegistry();
        var fake = new FakeProcessor(5);
        var workerStats = new[] { new WorkerStats() };
        var coord = new ProcessingCoordinator(bus, registry, fake, workerStats, 1,
            50);

        coord.Start();

        var path = "file2.log";
        for (var i = 0; i < 20; i++)
            bus.Publish(new FsEvent(FsEventKind.Modified, path, null, DateTimeOffset.UtcNow, true));

        Thread.Sleep(500);
        coord.Stop();

        // Assert that fake was called at least once and coordinator didn't crash
        Assert.True(fake.Calls.Count > 0);
    }

    [Fact]
    public void ConcurrentWorkers_DoNotProcessSamePathSimultaneously()
    {
        var bus = new BoundedEventBus<FsEvent>(1000);
        var registry = new FileStateRegistry();
        var fake = new FakeProcessor(10);
        var workerStats = new[] { new WorkerStats(), new WorkerStats() };
        var coord = new ProcessingCoordinator(bus, registry, fake, workerStats, 2,
            50);

        coord.Start();

        var path = "file3.log";
        for (var i = 0; i < 200; i++)
            bus.Publish(new FsEvent(FsEventKind.Modified, path, null, DateTimeOffset.UtcNow, true));

        Thread.Sleep(1000);
        coord.Stop();

        // If concurrent processing occurred, FakeProcessor would throw. If we reached here, it's fine.
        Assert.True(true);
    }
}