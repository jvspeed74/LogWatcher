using System;
using System.Diagnostics;
using System.Threading;

namespace WatchStats.Core
{
    // Simple Reporter that follows the documented behavior: requests swap, waits for acks, merges into GlobalSnapshot, and prints a simple report.
    public sealed class Reporter
    {
        private readonly WorkerStats[] _workers;
        private readonly BoundedEventBus<FsEvent> _bus;
        private readonly int _topK;
        private readonly int _intervalSeconds;
        private Thread? _thread;
        private volatile bool _stopping;

        // snapshot reused across reports
        private readonly GlobalSnapshot _snapshot;

        public Reporter(WorkerStats[] workers, BoundedEventBus<FsEvent> bus, int topK = 10, int intervalSeconds = 2)
        {
            _workers = workers ?? throw new ArgumentNullException(nameof(workers));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _topK = Math.Max(1, topK);
            _intervalSeconds = Math.Max(1, intervalSeconds);
            _snapshot = new GlobalSnapshot(_topK);
        }

        public void Start()
        {
            _stopping = false;
            _thread = new Thread(ReporterLoop) { IsBackground = true, Name = "reporter" };
            _thread.Start();
        }

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
            var sw = Stopwatch.StartNew();
            long lastTicks = sw.ElapsedTicks;

            while (!_stopping)
            {
                Thread.Sleep(TimeSpan.FromSeconds(_intervalSeconds));

                var nowTicks = sw.ElapsedTicks;
                double elapsedSeconds = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
                lastTicks = nowTicks;

                // Swap phase
                foreach (var w in _workers) w.RequestSwap();
                // wait for acks with cancellation support
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _intervalSeconds)));
                try
                {
                    foreach (var w in _workers)
                    {
                        w.WaitForSwapAck(cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // timeout or cancelled; proceed with what we have
                    Console.Error.WriteLine("Reporter: swap wait timed out");
                }

                // Merge/Frame build
                var frame = BuildSnapshotAndFrame();

                // Print
                PrintReportFrame(frame, elapsedSeconds);
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

        // Extracted for unit testing: performs swap/merge and returns the populated GlobalSnapshot
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

        private void PrintReportFrame(GlobalSnapshot snapshot, double elapsedSeconds)
        {
            long allocatedNow = GC.GetTotalAllocatedBytes(false);
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            Console.WriteLine($"[REPORT] elapsed={elapsedSeconds:0.00}s lines={snapshot.LinesProcessed} malformed={snapshot.MalformedLines} fs-events={snapshot.FsCreated + snapshot.FsModified + snapshot.FsDeleted + snapshot.FsRenamed} busDropped={snapshot.BusDropped} busPublished={snapshot.BusPublished} busDepth={snapshot.BusDepth} allocated={allocatedNow} gen0={gen0} gen1={gen1} gen2={gen2}");
            if (snapshot.TopKMessages.Count > 0)
            {
                Console.WriteLine("TopK:");
                foreach (var kv in snapshot.TopKMessages)
                {
                    Console.WriteLine($"  {kv.Key}: {kv.Count}");
                }
            }
        }
    }
}
