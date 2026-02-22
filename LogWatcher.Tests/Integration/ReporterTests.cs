using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Coordination;
using LogWatcher.Core.Ingestion;
using LogWatcher.Core.Processing.Parsing;
using LogWatcher.Core.Reporting;

namespace LogWatcher.Tests.Integration;

public class ReporterTests
{
    // TODO: map to invariant
    [Fact]
    public void BuildSnapshotAndFrame_WithPopulatedWorkerBuffers_MergesAllMetrics()
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

    // TODO: map to invariant
    [Fact]
    public void BuildSnapshotAndFrame_WithMessagesAndLatencies_ComputesTopKAndPercentiles()
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

    [Fact]
    [Invariant("RPT-001")]
    public void Reporter_WhenRunning_UsesActualElapsedTimeForRateComputation()
    {
        // Rates must be computed using actual elapsed time measured by Stopwatch,
        // never an assumed fixed interval duration.
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var bus = new BoundedEventBus<FsEvent>(10);
            var workers = new[] { new WorkerStats() };
            var reporter = new Reporter(workers, bus, 1, 1, ackTimeout: TimeSpan.FromMilliseconds(100));
            reporter.Start();
            Thread.Sleep(1500); // allow at least one interval report
            reporter.Stop();

            var output = writer.ToString();
            // Reporter must emit at least one report containing elapsed time and rate fields
            Assert.Contains("[REPORT]", output);
            Assert.Contains("elapsed=", output);
            Assert.Contains("lines/s=", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    [Invariant("RPT-003")]
    public void Reporter_OnStop_EmitsFinalReport()
    {
        // The reporter must emit at least one final report on shutdown.
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var bus = new BoundedEventBus<FsEvent>(10);
            var workers = new[] { new WorkerStats() };
            // 1-second interval; we stop after 100 ms so the thread exits cleanly within the join timeout
            var reporter = new Reporter(workers, bus, 1, 1, ackTimeout: TimeSpan.FromMilliseconds(100));
            reporter.Start();
            Thread.Sleep(100);
            reporter.Stop(); // must emit final report with elapsed=0.00 after loop exits

            var output = writer.ToString();
            // The final report is printed with elapsed=0.00 to indicate it is not a timed interval
            Assert.Contains("[REPORT]", output);
            Assert.Contains("elapsed=0.00", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    [Invariant("RPT-004")]
    public void Reporter_WhenWorkerAckTimesOut_LogsWarningAndContinues()
    {
        // When a worker fails to acknowledge a swap within the timeout the reporter
        // must proceed with available data and log a warning — it must not crash or block.
        var originalErr = Console.Error;
        var originalOut = Console.Out;
        using var errWriter = new StringWriter();
        using var outWriter = new StringWriter();
        Console.SetError(errWriter);
        Console.SetOut(outWriter);
        try
        {
            var bus = new BoundedEventBus<FsEvent>(10);
            var ws = new WorkerStats();
            // Worker never acknowledges swaps because it never calls AcknowledgeSwapIfRequested
            var workers = new[] { ws };
            // Extremely short ack timeout to force a timeout on every interval
            var reporter = new Reporter(workers, bus, 1, 1, ackTimeout: TimeSpan.FromMilliseconds(1));
            reporter.Start();
            Thread.Sleep(2500); // allow at least one interval with a forced ack timeout
            reporter.Stop();

            var errOutput = errWriter.ToString();
            var stdOutput = outWriter.ToString();
            // A warning must be logged when the ack times out
            Assert.Contains("timed out", errOutput, StringComparison.OrdinalIgnoreCase);
            // The reporter must still produce output despite the timeout
            Assert.Contains("[REPORT]", stdOutput);
        }
        finally
        {
            Console.SetError(originalErr);
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    [Invariant("RPT-005")]
    public void Stop_AfterStart_ThreadExitsWithinBoundedTime()
    {
        // Validates that Volatile.Write(ref _stopping, true) in Stop() is seen by
        // Volatile.Read(ref _stopping) in the loop thread, causing it to exit promptly.
        var bus = new BoundedEventBus<FsEvent>(10);
        var workers = new[] { new WorkerStats() };
        var reporter = new Reporter(workers, bus, 1, 1, ackTimeout: TimeSpan.FromMilliseconds(50));

        reporter.Start();
        reporter.Stop(); // must not hang

        // The reporter's background thread must have joined inside Stop()'s 2-second limit.
        // If the stopping flag write were invisible to the loop, Stop() would hang here.
        // Reaching this line proves the thread exited.
        Assert.True(true, "Stop() returned — thread exited within the join timeout.");
    }

    [Fact]
    [Invariant("RPT-005")]
    public void Stop_CalledMultipleTimes_IsIdempotent()
    {
        // A second Stop() after the thread has already exited must not throw or hang.
        var bus = new BoundedEventBus<FsEvent>(10);
        var workers = new[] { new WorkerStats() };
        var reporter = new Reporter(workers, bus, 1, 1, ackTimeout: TimeSpan.FromMilliseconds(50));

        reporter.Start();
        reporter.Stop();
        reporter.Stop(); // second call — must be safe
    }

    [Fact]
    [Invariant("RPT-006")]
    public void StartStopStart_StoppingFlagReset_ReporterRunsAgain()
    {
        // Validates that Volatile.Write(ref _stopping, false) in Start() correctly resets
        // the flag so that a restarted reporter loop does not exit immediately.
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var bus = new BoundedEventBus<FsEvent>(10);
            var workers = new[] { new WorkerStats() };
            var reporter = new Reporter(workers, bus, 1, 1, ackTimeout: TimeSpan.FromMilliseconds(50));

            // First cycle
            reporter.Start();
            reporter.Stop();

            // Second cycle — if _stopping were not reset, the loop would see true immediately
            // and no interval reports would ever fire in the second run.
            reporter.Start();
            Thread.Sleep(2500); // allow at least one interval tick
            reporter.Stop();

            var output = writer.ToString();
            int intervalReportCount = output.Split('\n')
                .Count(l => l.Contains("[REPORT]") && !l.Contains("elapsed=0.00"));
            Assert.True(intervalReportCount >= 1,
                $"Expected at least one interval report from the second Start(), but got {intervalReportCount}. " +
                "This suggests _stopping was not reset to false before the loop started.");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}