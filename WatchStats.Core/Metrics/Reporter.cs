using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WatchStats.Core.Concurrency;
using WatchStats.Core.Events;
using WatchStats.Core.Processing;

namespace WatchStats.Core.Metrics
{
    /// <summary>
    /// Periodically requests worker stats swaps, merges per-worker buffers into a <see cref="GlobalSnapshot"/>, and emits structured metrics logs.
    /// The reporter runs on a background thread when <see cref="Start"/> is called and stops after <see cref="Stop"/> is invoked.
    /// </summary>
    public sealed class Reporter
    {
        private readonly WorkerStats[] _workers;
        private readonly BoundedEventBus<FsEvent> _bus;
        private readonly int _topK;
        private readonly int _intervalMs;
        private readonly bool _enableMetricsLogs;
        private readonly ILogger<Reporter>? _logger;
        private readonly TimeSpan _swapTimeout = TimeSpan.FromSeconds(5);
        private Thread? _thread;
        private volatile bool _stopping;

        // snapshot reused across reports
        private readonly GlobalSnapshot _snapshot;

        // GC and bus drop baselines used to compute deltas between reports
        private long _lastAllocatedBytes;
        private int _lastGen0;
        private int _lastGen1;
        private int _lastGen2;
        private long _lastBusDropped;

        /// <summary>
        /// Creates a new <see cref="Reporter"/> instance.
        /// </summary>
        /// <param name="workers">Array of per-worker <see cref="WorkerStats"/> instances; used to request swaps and read inactive buffers.</param>
        /// <param name="bus">Event bus whose metrics (published/dropped/depth) are attached to the snapshot.</param>
        /// <param name="topK">Number of top messages to compute in each report; clamped to at least 1.</param>
        /// <param name="intervalMs">Report interval in milliseconds; clamped to at least 500.</param>
        /// <param name="enableMetricsLogs">Enable structured metrics logging for reporter_interval events.</param>
        /// <param name="logger">Optional logger for structured metrics output.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="workers"/> or <paramref name="bus"/> is null.</exception>
        public Reporter(WorkerStats[] workers, BoundedEventBus<FsEvent> bus, int topK, int intervalMs, bool enableMetricsLogs, ILogger<Reporter>? logger = null)
        {
            _workers = workers ?? throw new ArgumentNullException(nameof(workers));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _topK = Math.Max(1, topK);
            _intervalMs = Math.Max(500, intervalMs);
            _enableMetricsLogs = enableMetricsLogs;
            _logger = logger;
            _snapshot = new GlobalSnapshot(_topK);

            // initialize baselines to zero here; real baseline captured when Start() is called so tests can call BuildSnapshotAndFrame without timing side-effects
            _lastAllocatedBytes = 0;
            _lastGen0 = 0;
            _lastGen1 = 0;
            _lastGen2 = 0;
            _lastBusDropped = 0;
        }

        /// <summary>
        /// Starts the reporter's background thread which will periodically collect and emit metrics.
        /// Calling <see cref="Start"/> when already started will create a new background thread; callers should ensure it is not started multiple times unintentionally.
        /// </summary>
        public void Start()
        {
            // capture GC and bus baselines at start to compute deltas on first interval
            _lastAllocatedBytes = GC.GetTotalAllocatedBytes(false);
            _lastGen0 = GC.CollectionCount(0);
            _lastGen1 = GC.CollectionCount(1);
            _lastGen2 = GC.CollectionCount(2);
            _lastBusDropped = _bus.DroppedCount;

            _stopping = false;
            _thread = new Thread(ReporterLoop) { IsBackground = true, Name = "reporter" };
            _thread.Start();
        }

        /// <summary>
        /// Requests the reporter to stop and waits briefly for the background thread to exit.
        /// </summary>
        public void Stop()
        {
            _stopping = true;
            try
            {
                _thread?.Join(2000);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Reporter.Stop join error: {ex}");
            }
        }

        private void ReporterLoop()
        {
            while (!_stopping)
            {
                var sw = Stopwatch.StartNew();
                Thread.Sleep(TimeSpan.FromMilliseconds(_intervalMs));
                sw.Stop();
                var elapsedMs = (int)sw.ElapsedMilliseconds;

                // Swap phase with timeout tracking
                var swapSw = Stopwatch.StartNew();
                foreach (var w in _workers) w.RequestSwap();
                
                // wait for acks with swap timeout — run waits in parallel so one slow worker doesn't consume full timeout for all
                using var cts = new CancellationTokenSource(_swapTimeout);
                try
                {
                    var tasks = _workers.Select((w, idx) => Task.Run(() =>
                    {
                        try
                        {
                            w.WaitForSwapAck(cts.Token);
                            return idx; // acked index
                        }
                        catch (OperationCanceledException)
                        {
                            return -1; // not acked
                        }
                    })).ToArray();

                    // Wait for all tasks to complete within the swap timeout
                    Task.WaitAll(tasks, _swapTimeout);

                    // collect acknowledgements and warn on timeout
                    var ackedIndices = tasks.Where(t => t.IsCompleted && t.Result >= 0).Select(t => t.Result).ToArray();
                    int acked = ackedIndices.Length;
                    if (acked != _workers.Length)
                    {
                        var swapElapsed = swapSw.ElapsedMilliseconds;
                        if (swapElapsed >= 5000)
                        {
                            _logger?.LogWarning(
                                eventId: new EventId(11, "reporter_swap_timeout"),
                                "Worker buffer swap timeout. acked={Acked}, total={Total}, timeoutMs={TimeoutMs}",
                                acked, _workers.Length, swapElapsed);
                        }
                    }
                }
                catch (Exception ex) when (ex is AggregateException || ex is OperationCanceledException)
                {
                    // timeout or task exception; proceed with what we have
                    var swapElapsed = swapSw.ElapsedMilliseconds;
                    if (swapElapsed >= 5000)
                    {
                        _logger?.LogWarning(
                            eventId: new EventId(11, "reporter_swap_timeout"),
                            "Worker buffer swap timeout (exception). timeoutMs={TimeoutMs}, exception={Exception}",
                            swapElapsed, ex.Message);
                    }
                }

                // Merge/Frame build
                var frame = BuildSnapshotAndFrame();

                // Emit metrics
                EmitMetrics(frame, elapsedMs);
            }
        }

        /// <summary>
        /// Performs the merge of inactive worker buffers into the shared <see cref="GlobalSnapshot"/>, attaches bus metrics and finalizes derived outputs.
        /// This method is <c>internal</c> and extracted to allow unit testing of snapshot construction.
        /// </summary>
        /// <returns>The populated <see cref="GlobalSnapshot"/> instance (shared instance reused by the reporter).</returns>
        internal GlobalSnapshot BuildSnapshotAndFrame()
        {
            _snapshot.ResetForNextMerge(_topK);
            foreach (var w in _workers)
            {
                var buf = w.GetInactiveBufferForMerge();
                _snapshot.MergeFrom(buf);
            }

            // attach bus metrics
            _snapshot.BusPublished = _bus.PublishedCount;
            _snapshot.BusDropped = _bus.DroppedCount;
            _snapshot.BusDepth = _bus.Depth;

            _snapshot.FinalizeSnapshot(_topK);
            return _snapshot;
        }

        /// <summary>
        /// Emits structured metrics log for the interval if metrics logging is enabled.
        /// Computes deltas against the last baselines captured in Start/after the previous report.
        /// </summary>
        /// <param name="snapshot">Snapshot to emit metrics for.</param>
        /// <param name="intervalMs">Actual measured elapsed interval in milliseconds.</param>
        private void EmitMetrics(GlobalSnapshot snapshot, int intervalMs)
        {
            long allocatedNow = GC.GetTotalAllocatedBytes(false);
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            int gen0Delta = gen0 - _lastGen0;
            int gen1Delta = gen1 - _lastGen1;
            int gen2Delta = gen2 - _lastGen2;
            long busDropsDelta = snapshot.BusDropped - _lastBusDropped;

            // Compute per-second rates with zero-line handling
            double linesPerSec = intervalMs > 0 ? snapshot.LinesProcessed / (intervalMs / 1000.0) : 0.0;
            double malformedPerSec = intervalMs > 0 ? snapshot.MalformedLines / (intervalMs / 1000.0) : 0.0;

            // Extract level counts with helper to avoid repetitive bounds checking
            long levelInfo = GetLevelCount(snapshot, Processing.LogLevel.Info);
            long levelWarn = GetLevelCount(snapshot, Processing.LogLevel.Warn);
            long levelError = GetLevelCount(snapshot, Processing.LogLevel.Error);
            long levelOther = GetLevelCount(snapshot, Processing.LogLevel.Other);
            long levelDebug = GetLevelCount(snapshot, Processing.LogLevel.Debug);
            levelOther += levelDebug; // Combine Debug into Other for reporting

            // Emit structured metrics if enabled
            if (_enableMetricsLogs && _logger != null)
            {
                _logger.LogInformation(
                    eventId: new EventId(10, "reporter_interval"),
                    "Interval metrics. " +
                    "intervalMs={IntervalMs}, " +
                    "lines={Lines}, " +
                    "linesPerSec={LinesPerSec:F1}, " +
                    "malformed={Malformed}, " +
                    "malformedPerSec={MalformedPerSec:F1}, " +
                    "p50={P50}, " +
                    "p95={P95}, " +
                    "p99={P99}, " +
                    "drops={Drops}, " +
                    "truncations={Truncations}, " +
                    "overflows={Overflows}, " +
                    "gc0={Gc0}, " +
                    "gc1={Gc1}, " +
                    "gc2={Gc2}, " +
                    "levelInfo={LevelInfo}, " +
                    "levelWarn={LevelWarn}, " +
                    "levelError={LevelError}, " +
                    "levelOther={LevelOther}",
                    intervalMs,
                    snapshot.LinesProcessed,
                    linesPerSec,
                    snapshot.MalformedLines,
                    malformedPerSec,
                    snapshot.P50 ?? 0.0,
                    snapshot.P95 ?? 0.0,
                    snapshot.P99 ?? 0.0,
                    busDropsDelta,
                    snapshot.TruncationResetCount,
                    0, // overflows - TODO: implement overflow tracking in FilesystemWatcherAdapter
                    gen0Delta,
                    gen1Delta,
                    gen2Delta,
                    levelInfo,
                    levelWarn,
                    levelError,
                    levelOther);
            }

            // Update baselines for next interval
            _lastAllocatedBytes = allocatedNow;
            _lastGen0 = gen0;
            _lastGen1 = gen1;
            _lastGen2 = gen2;
            _lastBusDropped = snapshot.BusDropped;
        }

        /// <summary>
        /// Helper method to safely extract level count from snapshot with bounds checking.
        /// </summary>
        private static long GetLevelCount(GlobalSnapshot snapshot, Processing.LogLevel level)
        {
            int index = (int)level;
            return snapshot.LevelCounts.Length > index ? snapshot.LevelCounts[index] : 0;
        }
    }
}