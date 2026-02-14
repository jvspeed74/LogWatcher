using System.Collections.Concurrent;
using LogWatcher.Core.Concurrency;
using LogWatcher.Core.Events;
using LogWatcher.Core.Metrics;
using LogWatcher.Core.Processing;

namespace LogWatcher.Tests.Stress;

public class SyntheticStressTests
{
    [Fact(Timeout = 20000)]
    public async Task Synthetic_Publisher_Stress_ShouldDropAndNoConcurrentPerPath()
    {
        var busCapacity = 20;
        var workers = 6;
        var publishers = 12;
        var paths = 8;
        var runMs = 2500;

        var bus = new BoundedEventBus<FsEvent>(busCapacity);
        var registry = new FileStateRegistry();

        // fake processor that ensures per-path single concurrency and increments counters
        var inFlight = new ConcurrentDictionary<string, int>();
        var processedCounts = new ConcurrentDictionary<string, int>();

        IFileProcessor fakeProcessor = new FakeProcessor(inFlight, processedCounts);

        var workerStats = new WorkerStats[workers];
        for (var i = 0; i < workers; i++) workerStats[i] = new WorkerStats();

        var coord = new ProcessingCoordinator(bus, registry, fakeProcessor, workerStats, workers,
            50);
        coord.Start();

        var cts = new CancellationTokenSource(runMs);

        var tasks = new Task[publishers];
        for (var p = 0; p < publishers; p++)
            tasks[p] = Task.Run(async () =>
            {
                var rnd = new Random(Guid.NewGuid().GetHashCode());
                while (!cts.IsCancellationRequested)
                {
                    var id = rnd.Next(paths);
                    var path = $"/tmp/fake/{id}.log";
                    var ev = new FsEvent(FsEventKind.Modified, path, null, DateTimeOffset.UtcNow, true);
                    bus.Publish(ev);
                    // yield minimally to allow high throughput but avoid tight busy spin
                    await Task.Yield();
                }
            });

        await Task.WhenAll(tasks);

        // stop publishers and then stop bus and coordinator
        bus.Stop();
        coord.Stop();

        // assertions
        Assert.True(bus.DroppedCount > 0, "Expected some events to be dropped under synthetic load");

        long totalProcessed = 0;
        foreach (var kv in processedCounts) totalProcessed += kv.Value;
        Assert.True(totalProcessed > 0, "Expected some processing to occur");
    }

    private sealed class FakeProcessor : IFileProcessor
    {
        private readonly ConcurrentDictionary<string, int> _counts;
        private readonly ConcurrentDictionary<string, int> _inFlight;

        public FakeProcessor(ConcurrentDictionary<string, int> inFlight, ConcurrentDictionary<string, int> counts)
        {
            _inFlight = inFlight;
            _counts = counts;
        }

        public void ProcessOnce(string path, FileState state, WorkerStatsBuffer stats, int chunkSize = 64 * 1024)
        {
            var curr = _inFlight.AddOrUpdate(path, 1, (_, v) => v + 1);
            try
            {
                if (curr > 1) throw new InvalidOperationException($"Concurrent processing detected for path {path}");

                // simulate tiny work
                Thread.Sleep(1);

                _counts.AddOrUpdate(path, 1, (_, v) => v + 1);

                // bump some counters on stats to mimic real processor
                stats.LinesProcessed += 1;
            }
            finally
            {
                _inFlight.AddOrUpdate(path, 0, (_, v) => Math.Max(0, v - 1));
            }
        }
    }
}