using LogWatcher.Core.Concurrency;
using LogWatcher.Core.Events;
using LogWatcher.Core.Metrics;
using LogWatcher.Core.Processing;

namespace LogWatcher.Tests.Integration;

public class ReporterTests
{
    [Fact]
    public void BuildSnapshotAndFrame_MergesWorkerBuffersAndAttachesBusMetrics()
    {
        // Arrange
        var bus = new BoundedEventBus<FsEvent>(10);
        // publish some events to bump published counter
        for (var i = 0; i < 5; i++)
            bus.Publish(new FsEvent(FsEventKind.Created, $"/tmp/{i}.log", null, DateTimeOffset.UtcNow, true));

        var workers = new WorkerStats[2];
        for (var i = 0; i < workers.Length; i++) workers[i] = new WorkerStats();

        // populate Active buffers
        workers[0].Active.IncrementFsEvent(FsEventKind.Created);
        workers[0].Active.IncrementLevel(LogLevel.Info);
        workers[0].Active.IncrementMessage("k1");
        workers[0].Active.RecordLatency(10);

        workers[1].Active.IncrementFsEvent(FsEventKind.Modified);
        workers[1].Active.IncrementLevel(LogLevel.Warn);
        workers[1].Active.IncrementMessage("k2");
        workers[1].Active.RecordLatency(100);

        // cause swap so Inactive buffers contain our data
        foreach (var w in workers)
        {
            w.RequestSwap();
            w.AcknowledgeSwapIfRequested();
        }

        var reporter = new Reporter(workers, bus, 2, 1);

        // Act
        var snap = reporter.BuildSnapshotAndFrame();

        // Assert merged scalars
        Assert.Equal(1, snap.FsCreated);
        Assert.Equal(1, snap.FsModified);
        // Level counts merged (ensure non-zero)
        Assert.True(snap.LevelCounts[(int)LogLevel.Info] >= 1);
        Assert.True(snap.LevelCounts[(int)LogLevel.Warn] >= 1);
        // Assert message counts merged
        Assert.True(snap.MessageCounts.ContainsKey("k1"));
        Assert.True(snap.MessageCounts.ContainsKey("k2"));
        // Bus metrics
        Assert.Equal(5, snap.BusPublished);
        Assert.Equal(0, snap.BusDropped);
    }

    [Fact]
    public void BuildSnapshotAndFrame_ComputesTopKAndPercentiles()
    {
        var bus = new BoundedEventBus<FsEvent>(10);
        var workers = new WorkerStats[1];
        workers[0] = new WorkerStats();

        // populate with multiple messages
        var active = workers[0].Active;
        active.IncrementMessage("a");
        active.IncrementMessage("a");
        active.IncrementMessage("b");
        active.RecordLatency(10);
        active.RecordLatency(50);
        active.RecordLatency(90);

        workers[0].RequestSwap();
        workers[0].AcknowledgeSwapIfRequested();

        var reporter = new Reporter(workers, bus, 2, 1);
        var snap = reporter.BuildSnapshotAndFrame();

        Assert.Equal(2, snap.TopKMessages.Count);
        var topKeys = snap.TopKMessages.Select(t => t.Key).ToArray();
        Assert.Equal("a", topKeys[0]);

        Assert.NotNull(snap.P50);
        Assert.NotNull(snap.P95);
        Assert.NotNull(snap.P99);
    }
}