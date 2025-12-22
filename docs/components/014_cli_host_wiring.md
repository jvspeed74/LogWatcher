## Component 14: CLI Host wiring (Program.cs) and lifecycle/shutdown

### Purpose

Assemble all components, start them in the correct order, and shut them down cleanly such that:

* FileSystemWatcher stops publishing before the bus/coordinator are torn down
* Worker threads exit promptly
* Reporter prints consistent R2 interval reports and a final summary
* Ctrl+C triggers cooperative shutdown

### Dependencies to wire

* `BoundedEventBus<FsEvent>` (capacity default 10,000)
* `FileStateRegistry`
* `FileTailer`
* `Utf8LineScanner`
* `LogParser`
* `FileProcessor`
* `WorkerStats[]` (N workers)
* `ProcessingCoordinator`
* `Reporter`
* `FilesystemWatcherAdapter`

### Step-by-step implementation

#### 1) Argument parsing (minimal, no external libs)

Support at least:

* positional: `<watchPath>`
* options:

    * `--workers N` (default: `Environment.ProcessorCount`)
    * `--queue-capacity N` (default: 10000)
    * `--report-interval-seconds N` (default: 2)
    * `--topk N` (default: 10)

Implementation guidance:

* Parse with a simple loop over `args[]`.
* Validate:

    * watchPath exists and is a directory
    * workers >= 1
    * queue capacity >= 1
    * report interval >= 1
    * topK >= 1

#### 2) Construct config object

Create `sealed class AppConfig` containing validated values. Pass config into constructors rather than relying on globals.

#### 3) Create and start components (startup order)

Recommended order:

1. Instantiate shared services:

    * `var bus = new BoundedEventBus<FsEvent>(config.QueueCapacity);`
    * `var registry = new FileStateRegistry();`
    * `var tailer = new FileTailer(chunkSize: 64 * 1024);` (chunk size may be a constant)
    * `var processor = new FileProcessor(tailer, /* scanner */, /* parser */);`
2. Create per-worker stats:

    * `var workersStats = new WorkerStats[config.Workers];`
    * initialize each with two `WorkerStatsBuffer`s
3. Instantiate coordinator:

    * `var coordinator = new ProcessingCoordinator(bus, registry, processor, workersStats, config);`
4. Instantiate reporter:

    * `var reporter = new Reporter(workersStats, bus, config);`
5. Instantiate filesystem watcher adapter:

    * `var watcher = new FilesystemWatcherAdapter(config.WatchPath, bus);`

Start in this order:

1. `coordinator.Start()` (consumers ready)
2. `reporter.Start()` (reporting ready)
3. `watcher.Start()` (producers start last)

This minimizes initial drops because consumers are already active.

#### 4) Ctrl+C shutdown handling (must be deterministic)

1. Register handler:

    * `Console.CancelKeyPress += (s,e) => { e.Cancel = true; TriggerShutdown(); };`
2. Implement `TriggerShutdown()` with an `Interlocked.Exchange` guard so it runs once.

Shutdown order (important):

1. `watcher.Stop()` (stop new publishes)
2. `bus.Stop()` (unblock worker dequeues)
3. `coordinator.Stop()` (join worker threads)
4. `reporter.Stop()` (join reporter thread; optionally final report)
5. Dispose:

    * `watcher.Dispose()`

Note:

* If reporter requires workers to ack swap, stop reporter after coordinator to avoid waiting on workers that are already gone. If you want a final report, have reporter do one last swap before coordinator stops, or accept best-effort final stats.

#### 5) “Running” behavior

After starting components:

* Print a single header line:

    * watched path, worker count, queue capacity, report interval, topK
* Keep main thread alive until shutdown:

    * simplest: `new ManualResetEventSlim(false).Wait()` and set it in TriggerShutdown.

#### 6) Exit codes

* Invalid args => return non-zero.
* Clean shutdown => return 0.

---
