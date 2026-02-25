using System.Diagnostics;

using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Coordination;
using LogWatcher.Core.Ingestion;

using Microsoft.Extensions.Logging;

namespace LogWatcher.Core.Reporting
{
    /// <summary>
    /// Periodically requests worker stats swaps, merges per-worker buffers into a <see cref="GlobalSnapshot"/>,
    /// and notifies all registered <see cref="ISnapshotConsumer"/> implementations.
    /// The reporter runs on a background thread when <see cref="Start"/> is called and stops after <see cref="Stop"/> is invoked.
    /// </summary>
    public sealed partial class Reporter : IDisposable
    {
        private readonly WorkerStats[] _workers;
        private readonly BoundedEventBus<FsEvent> _bus;
        private readonly int _topK;
        private readonly TimeSpan _interval;
        private readonly TimeSpan _ackTimeout;
        private readonly ILogger<Reporter>? _logger;
        private readonly IReadOnlyList<ISnapshotConsumer> _consumers;
        private Thread? _thread;
        private bool _stopping;
        private PeriodicTimer? _timer;

        // snapshot reused across reports
        private readonly GlobalSnapshot _snapshot;

        // GC baselines used to compute deltas between reports
        private long _lastAllocatedBytes;
        private int _lastGen0;
        private int _lastGen1;
        private int _lastGen2;

        /// <summary>
        /// Creates a new <see cref="Reporter"/> instance.
        /// </summary>
        /// <param name="workers">Array of per-worker <see cref="WorkerStats"/> instances; used to request swaps and read inactive buffers.</param>
        /// <param name="bus">Event bus whose metrics (published/dropped/depth) are attached to the snapshot.</param>
        /// <param name="topK">Number of top messages to compute in each report; clamped to at least 1.</param>
        /// <param name="interval">Report interval; clamped to at least 1 second.</param>
        /// <param name="ackTimeout">Timeout to wait for worker swap acknowledgements. If null, defaults to max(1s, interval * 1.5).</param>
        /// <param name="logger">Logger for operational warnings. If null, warnings are suppressed.</param>
        /// <param name="consumers">Snapshot consumers invoked after each interval. If null or empty, no consumers are notified.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="workers"/> or <paramref name="bus"/> is null.</exception>
        public Reporter(WorkerStats[] workers, BoundedEventBus<FsEvent> bus, int topK = 10, TimeSpan interval = default,
            TimeSpan? ackTimeout = null, ILogger<Reporter>? logger = null, IReadOnlyList<ISnapshotConsumer>? consumers = null)
        {
            _workers = workers ?? throw new ArgumentNullException(nameof(workers));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _topK = Math.Max(1, topK);
            _interval = interval == default ? TimeSpan.FromSeconds(2) : interval;
            // default ack timeout is 1.5x the reporting interval to tolerate busy workers
            _ackTimeout = ackTimeout ?? TimeSpan.FromSeconds(Math.Max(1, _interval.TotalSeconds) * 1.5);
            _logger = logger;
            _consumers = consumers ?? [];
            _snapshot = new GlobalSnapshot(_topK);

            // initialize baselines to zero here; real baseline captured when Start() is called so tests can call BuildSnapshotAndFrame without timing side-effects
            _lastAllocatedBytes = 0;
            _lastGen0 = 0;
            _lastGen1 = 0;
            _lastGen2 = 0;
        }

        /// <summary>
        /// Starts the reporter's background thread which will periodically collect and notify consumers.
        /// Calling <see cref="Start"/> when already started will create a new background thread; callers should ensure it is not started multiple times unintentionally.
        /// </summary>
        public void Start()
        {
            // capture GC baselines at start to compute deltas on first interval
            _lastAllocatedBytes = GC.GetTotalAllocatedBytes(false);
            _lastGen0 = GC.CollectionCount(0);
            _lastGen1 = GC.CollectionCount(1);
            _lastGen2 = GC.CollectionCount(2);

            Volatile.Write(ref _stopping, false);
            _timer = new PeriodicTimer(_interval);
            _thread = new Thread(ReporterLoop) { IsBackground = true, Name = "reporter" };
            _thread.Start();
        }

        /// <summary>
        /// Requests the reporter to stop and waits briefly for the background thread to exit.
        /// </summary>
        public void Stop()
        {
            _timer?.Dispose();
            Volatile.Write(ref _stopping, true);
            try
            {
                _thread?.Join(2000);
            }
            catch (Exception ex)
            {
                if (_logger != null) LogStopJoinError(_logger, ex);
            }
        }

        /// <inheritdoc/>
        public void Dispose() => Stop();

        private void ReporterLoop()
        {
            var sw = Stopwatch.StartNew();
            long lastTicks = sw.ElapsedTicks;

            while (!Volatile.Read(ref _stopping))
            {
                if (!_timer!.WaitForNextTickAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult())
                    break;

                var nowTicks = sw.ElapsedTicks;
                var elapsed = TimeSpan.FromSeconds((nowTicks - lastTicks) / (double)Stopwatch.Frequency);
                lastTicks = nowTicks;

                // Swap phase
                foreach (var w in _workers) w.RequestSwap();
                if (_logger != null) LogReportCycle(_logger, _workers.Length);
                // Wait for acks in parallel so one slow worker doesn't consume the full timeout for all.
                // Parallel.ForEach is justified here: workers are independent and sequential waits would
                // accumulate per-worker timeouts, causing unbounded delay under a slow/stuck worker.
                using var cts = new CancellationTokenSource(_ackTimeout);
                int acked = 0;
                Parallel.ForEach(_workers, w =>
                {
                    try
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        w.WaitForSwapAck(cts.Token);
                        Interlocked.Increment(ref acked);
                    }
                    catch (OperationCanceledException) { }
                });
                if (acked != _workers.Length)
                    if (_logger != null) LogSwapTimeout(_logger, acked, _workers.Length);

                // Merge/Frame build + notify consumers
                var frame = BuildSnapshotAndFrame();
                foreach (var c in _consumers)
                    c.OnSnapshot(frame, elapsed);
            }

            // optional final report on stop
            try
            {
                var final = BuildSnapshotAndFrame(updateBaselines: false);
                foreach (var c in _consumers)
                    c.OnSnapshot(final, TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                if (_logger != null) LogFinalReportError(_logger, ex);
            }
        }

        /// <summary>
        /// Performs the merge of inactive worker buffers into the shared <see cref="GlobalSnapshot"/>,
        /// attaches bus and GC metrics, and finalizes derived outputs.
        /// This method is <c>internal</c> and extracted to allow unit testing of snapshot construction.
        /// </summary>
        /// <returns>The populated <see cref="GlobalSnapshot"/> instance (shared instance reused by the reporter).</returns>
        internal GlobalSnapshot BuildSnapshotAndFrame(bool updateBaselines = true)
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

            // attach GC metrics (deltas computed against baselines from Start() or last interval)
            long allocatedNow = GC.GetTotalAllocatedBytes(false);
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            _snapshot.AllocatedBytesDelta = allocatedNow - _lastAllocatedBytes;
            _snapshot.AllocatedBytesTotal = allocatedNow;
            _snapshot.Gen0Delta = gen0 - _lastGen0;
            _snapshot.Gen1Delta = gen1 - _lastGen1;
            _snapshot.Gen2Delta = gen2 - _lastGen2;
            if (updateBaselines)
            {
                _lastAllocatedBytes = allocatedNow;
                _lastGen0 = gen0;
                _lastGen1 = gen1;
                _lastGen2 = gen2;
            }

            _snapshot.FinalizeSnapshot(_topK);
            return _snapshot;
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Reporter.Stop join error")]
        private static partial void LogStopJoinError(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Reporter: swap wait timed out (acked={Acked} of {Total})")]
        private static partial void LogSwapTimeout(ILogger logger, int acked, int total);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Reporter final report error")]
        private static partial void LogFinalReportError(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Report cycle starting workers={Workers}")]
        private static partial void LogReportCycle(ILogger logger, int workers);
    }
}