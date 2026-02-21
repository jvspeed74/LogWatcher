using System.Diagnostics;

using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Coordination;
using LogWatcher.Core.Ingestion;

namespace LogWatcher.Core.Reporting
{
    /// <summary>
    /// Periodically requests worker stats swaps, merges per-worker buffers into a <see cref="GlobalSnapshot"/>, and prints a report.
    /// The reporter runs on a background thread when <see cref="Start"/> is called and stops after <see cref="Stop"/> is invoked.
    /// </summary>
    public sealed class Reporter : IDisposable
    {
        private readonly WorkerStats[] _workers;
        private readonly BoundedEventBus<FsEvent> _bus;
        private readonly int _topK;
        private readonly int _intervalSeconds;
        private readonly TimeSpan _ackTimeout;
        private Task? _loopTask;
        private CancellationTokenSource? _cts;

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
        /// <param name="intervalSeconds">Report interval in seconds; clamped to at least 1.</param>
        /// <param name="ackTimeout">Timeout to wait for worker swap acknowledgements. If null, defaults to max(1s, intervalSeconds).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="workers"/> or <paramref name="bus"/> is null.</exception>
        public Reporter(WorkerStats[] workers, BoundedEventBus<FsEvent> bus, int topK = 10, int intervalSeconds = 2, TimeSpan? ackTimeout = null)
        {
            _workers = workers ?? throw new ArgumentNullException(nameof(workers));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _topK = Math.Max(1, topK);
            _intervalSeconds = Math.Max(1, intervalSeconds);
            // default ack timeout is 1.5x the reporting interval to tolerate busy workers
            _ackTimeout = ackTimeout ?? TimeSpan.FromSeconds(Math.Max(1, _intervalSeconds) * 1.5);
            _snapshot = new GlobalSnapshot(_topK);

            // initialize baselines to zero here; real baseline captured when Start() is called so tests can call BuildSnapshotAndFrame without timing side-effects
            _lastAllocatedBytes = 0;
            _lastGen0 = 0;
            _lastGen1 = 0;
            _lastGen2 = 0;
        }

        /// <summary>
        /// Starts the reporter's background loop which will periodically collect and print reports.
        /// Calling <see cref="Start"/> when already started will create a new background task; callers should ensure it is not started multiple times unintentionally.
        /// </summary>
        public void Start()
        {
            // capture GC baselines at start to compute deltas on first interval
            _lastAllocatedBytes = GC.GetTotalAllocatedBytes(false);
            _lastGen0 = GC.CollectionCount(0);
            _lastGen1 = GC.CollectionCount(1);
            _lastGen2 = GC.CollectionCount(2);

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => ReporterLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Requests the reporter to stop and waits briefly for the background loop to exit.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _loopTask?.Wait(2000);
            }
            catch (AggregateException aex) when (aex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // expected: loop cancelled normally
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Reporter.Stop wait error: {ex}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Stops the reporter and disposes the internal cancellation token source.
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        private async Task ReporterLoopAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            long lastTicks = sw.ElapsedTicks;

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    var nowTicks = sw.ElapsedTicks;
                    double elapsedSeconds = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
                    lastTicks = nowTicks;

                    // Swap phase
                    foreach (var w in _workers) w.RequestSwap();
                    // wait for acks sequentially within a shared deadline
                    using var swapCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    swapCts.CancelAfter(_ackTimeout);
                    int acked = 0;
                    foreach (var w in _workers)
                    {
                        try
                        {
                            w.WaitForSwapAck(swapCts.Token);
                            acked++;
                        }
                        catch (OperationCanceledException)
                        {
                            if (ct.IsCancellationRequested) break;
                        }
                    }

                    if (acked != _workers.Length)
                    {
                        Console.Error.WriteLine($"Reporter: swap wait timed out (acked={acked} of {_workers.Length})");
                    }

                    // Merge/Frame build
                    var frame = BuildSnapshotAndFrame();

                    // Print
                    PrintReportFrame(frame, elapsedSeconds);
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }

            // optional final report on stop
            try
            {
                var final = BuildSnapshotAndFrame();
                PrintReportFrame(final, 0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Reporter final report error: {ex}");
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
        /// Formats and writes the provided snapshot to <see cref="Console"/> including allocation and GC stats.
        /// Computes deltas against the last baselines captured in Start/after the previous non-final report.
        /// </summary>
        /// <param name="snapshot">Snapshot to print.</param>
        /// <param name="elapsedSeconds">Elapsed interval in seconds used for the printed report line. Zero indicates a final/no-interval report.</param>
        private void PrintReportFrame(GlobalSnapshot snapshot, double elapsedSeconds)
        {
            long allocatedNow = GC.GetTotalAllocatedBytes(false);
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            long allocatedDelta = allocatedNow - _lastAllocatedBytes;
            int gen0Delta = gen0 - _lastGen0;
            int gen1Delta = gen1 - _lastGen1;
            int gen2Delta = gen2 - _lastGen2;

            // compute simple per-second rates when we have a positive elapsedSeconds
            double fsEventsTotal = snapshot.FsCreated + snapshot.FsModified + snapshot.FsDeleted + snapshot.FsRenamed;
            double lines = snapshot.LinesProcessed;
            double fsRate = elapsedSeconds > 0 ? fsEventsTotal / elapsedSeconds : 0.0;
            double linesRate = elapsedSeconds > 0 ? lines / elapsedSeconds : 0.0;

            // TODO: format long message into several short ones
            // TODO: Add histogram percentiles (P50, P95, P99) to the report output
            Console.WriteLine(
                $"[REPORT] elapsed={elapsedSeconds:0.00}s lines={snapshot.LinesProcessed} lines/s={linesRate:0.00} malformed={snapshot.MalformedLines} fs-events={fsEventsTotal} fs/s={fsRate:0.00} busDropped={snapshot.BusDropped} busPublished={snapshot.BusPublished} busDepth={snapshot.BusDepth} allocatedDelta={allocatedDelta} allocated={allocatedNow} gen0Delta={gen0Delta} gen1Delta={gen1Delta} gen2Delta={gen2Delta}");

            if (snapshot.TopKMessages.Count > 0)
            {
                // TODO: Verify the message counts are being aggregated correctly across workers
                Console.WriteLine("TopK:");
                foreach (var kv in snapshot.TopKMessages)
                {
                    Console.WriteLine($"  {kv.Key}: {kv.Count}");
                }
            }

            // TODO: Consider whether baseline updates are necessary for final report (elapsedSeconds==0)
            // update baselines only when this was a regular interval (not the final forced report with elapsedSeconds==0)
            if (elapsedSeconds > 0)
            {
                _lastAllocatedBytes = allocatedNow;
                _lastGen0 = gen0;
                _lastGen1 = gen1;
                _lastGen2 = gen2;
            }
        }
    }
}