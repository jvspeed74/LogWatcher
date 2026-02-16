using LogWatcher.Core.Concurrency;
using LogWatcher.Core.Events;
using LogWatcher.Core.IO;
using LogWatcher.Core.Metrics;
using LogWatcher.Core.Processing;

namespace LogWatcher.App;

/// <summary>
/// Hosts and executes the LogWatcher application with validated configuration.
/// Responsible for component wiring, startup, and shutdown coordination.
/// </summary>
public static class ApplicationHost
{
    private static int _shutdownRequested = 0;
    private static readonly ManualResetEventSlim _shutdownEvent = new(false);

    /// <summary>
    /// Runs the application with the provided configuration.
    /// </summary>
    /// <param name="config">Validated CLI configuration.</param>
    /// <returns>Exit code: 0 for success, 1 for runtime error.</returns>
    public static int Run(CliConfig config)
    {
        BoundedEventBus<FsEvent>? bus = null;
        FileStateRegistry? registry = null;
        FileTailer? tailer = null;
        FileProcessor? processor = null;
        WorkerStats[]? workerStats = null;
        ProcessingCoordinator? coordinator = null;
        Reporter? reporter = null;
        FilesystemWatcherAdapter? watcher = null;

        try
        {
            // Construct components
            bus = new BoundedEventBus<FsEvent>(config.QueueCapacity);
            registry = new FileStateRegistry();
            tailer = new FileTailer();
            processor = new FileProcessor(tailer);

            workerStats = new WorkerStats[config.Workers];
            for (int i = 0; i < workerStats.Length; i++)
            {
                workerStats[i] = new WorkerStats();
            }

            coordinator = new ProcessingCoordinator(bus, registry, processor, workerStats, 
                workerCount: config.Workers);
            reporter = new Reporter(workerStats, bus, config.TopK, config.ReportIntervalSeconds);
            watcher = new FilesystemWatcherAdapter(config.WatchPath, bus);

            // Register shutdown handlers
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                TriggerShutdown(bus, watcher, coordinator, reporter);
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => 
                TriggerShutdown(bus, watcher, coordinator, reporter);

            // Start components in order
            coordinator.Start();
            reporter.Start();
            watcher.Start();

            Console.WriteLine("Started: " + config);

            // Wait until shutdown is requested
            WaitForShutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex}");
            return 1;
        }
        finally
        {
            // Ensure final cleanup
            TriggerShutdown(bus, watcher, coordinator, reporter);
        }
    }

    /// <summary>
    /// Requests a coordinated shutdown of the provided host components. Safe to call multiple times.
    /// Non-null components will be stopped/disposed where applicable; exceptions thrown by components are logged to <see cref="Console.Error"/>.
    /// </summary>
    private static void TriggerShutdown(BoundedEventBus<FsEvent>? bus, FilesystemWatcherAdapter? watcher,
        ProcessingCoordinator? coordinator, Reporter? reporter)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1) return;
        Console.WriteLine("Shutdown requested...");

        try
        {
            if (watcher != null)
            {
                try
                {
                    watcher.Stop();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"watcher.Stop error: {ex}");
                }
            }

            if (bus != null)
            {
                try
                {
                    bus.Stop();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"bus.Stop error: {ex}");
                }
            }

            if (coordinator != null)
            {
                try
                {
                    coordinator.Stop();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"coordinator.Stop error: {ex}");
                }
            }

            if (reporter != null)
            {
                try
                {
                    reporter.Stop();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"reporter.Stop error: {ex}");
                }
            }

            if (watcher != null)
            {
                try
                {
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"watcher.Dispose error: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during shutdown: {ex}");
        }
        finally
        {
            _shutdownEvent.Set();
        }
    }

    /// <summary>
    /// Blocks until the host shutdown has been requested and the shutdown event has been signaled.
    /// </summary>
    private static void WaitForShutdown() => _shutdownEvent.Wait();
}
