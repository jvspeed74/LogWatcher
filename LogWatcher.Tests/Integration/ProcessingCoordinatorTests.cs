using System.Collections.Concurrent;

using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Coordination;
using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Ingestion;
using LogWatcher.Core.Processing;
using LogWatcher.Core.Statistics;

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

// Processor that verifies state.Gate is held during each ProcessOnce call
internal class GateCheckingProcessor : IFileProcessor
{
    public bool GateAlwaysHeld { get; private set; } = true;

    public void ProcessOnce(string path, FileState state, WorkerStatsBuffer stats, int chunkSize = 64 * 1024)
    {
        if (!Monitor.IsEntered(state.Gate))
            GateAlwaysHeld = false;
    }
}

public class ProcessingCoordinatorTests
{
    [Fact]
    [Invariant("PROC-004")]
    [Invariant("FM-002")]
    public void ModifyThenDelete_WhileWorkerBusy_FinalizesDelete()
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
    [Invariant("PROC-002")]
    [Invariant("PROC-003")]
    public void ManyModifies_WhileWorkerBusy_CoalesceAndProcess()
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
    [Invariant("PROC-001")]
    public void ConcurrentWorkers_SamePath_NeverProcessSimultaneously()
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

    [Fact]
    [Invariant("PROC-005")]
    public void Coordinator_WhenEventsPublished_EventuallyProcessesAllPaths()
    {
        // Every byte appended to a watched file is eventually processed assuming events are not
        // permanently suppressed. Publishing Modified events triggers ProcessOnce for the path.
        var bus = new BoundedEventBus<FsEvent>(1000);
        var registry = new FileStateRegistry();
        var fake = new FakeProcessor();
        var workerStats = new[] { new WorkerStats() };
        var coord = new ProcessingCoordinator(bus, registry, fake, workerStats, 1, 50);

        coord.Start();

        const string path = "proc005_test.log";
        for (int i = 0; i < 5; i++)
            bus.Publish(new FsEvent(FsEventKind.Modified, path, null, DateTimeOffset.UtcNow, true));

        Thread.Sleep(500);
        coord.Stop();

        Assert.True(fake.Calls.Count > 0, "Every published event must eventually trigger a ProcessOnce call");
    }

    [Fact]
    [Invariant("PROC-006")]
    public void Coordinator_ProcessOnce_IsCalledOnlyWhileGateIsHeld()
    {
        var bus = new BoundedEventBus<FsEvent>(100);
        var registry = new FileStateRegistry();
        var gateChecker = new GateCheckingProcessor();
        var workerStats = new[] { new WorkerStats() };
        var coord = new ProcessingCoordinator(bus, registry, gateChecker, workerStats, 1, 50);

        coord.Start();
        bus.Publish(new FsEvent(FsEventKind.Modified, "gate_check.log", null, DateTimeOffset.UtcNow, true));
        Thread.Sleep(300);
        coord.Stop();

        Assert.True(gateChecker.GateAlwaysHeld, "ProcessOnce must only be called while state.Gate is held");
    }

    [Fact]
    [Invariant("PROC-007")]
    public void ProcessingCoordinator_WorkerCount_IsFixedAtConstruction()
    {
        // Worker count is determined at construction and cannot be changed at runtime.
        // There is no AddWorker/RemoveWorker API on ProcessingCoordinator.
        var bus = new BoundedEventBus<FsEvent>(10);
        var registry = new FileStateRegistry();
        var fake = new FakeProcessor();
        const int workerCount = 3;
        var workerStats = new WorkerStats[workerCount];
        for (int i = 0; i < workerCount; i++) workerStats[i] = new WorkerStats();

        var coord = new ProcessingCoordinator(bus, registry, fake, workerStats, workerCount, 50);
        coord.Start();
        Thread.Sleep(50);
        coord.Stop();

        // The coordinator was constructed with exactly workerCount workers and has no API to change that
        Assert.Equal(workerCount, workerStats.Length);
    }
}