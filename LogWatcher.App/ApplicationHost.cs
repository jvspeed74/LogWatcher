using LogWatcher.Core;
using LogWatcher.Core.Concurrency;
using LogWatcher.Core.Events;
using LogWatcher.Core.IO;
using LogWatcher.Core.Metrics;
using LogWatcher.Core.Processing;
using Microsoft.Extensions.DependencyInjection;

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
    /// Runs the application with the provided validated configuration parameters.
    /// </summary>
    /// <param name="watchPath">Absolute path to the directory to watch.</param>
    /// <param name="workers">Number of worker threads.</param>
    /// <param name="queueCapacity">Capacity of the filesystem event queue.</param>
    /// <param name="reportIntervalSeconds">Report interval in seconds.</param>
    /// <param name="topK">Top-K value for reporting.</param>
    /// <returns>Exit code: 0 for success, 1 for runtime error.</returns>
    public static int Run(string watchPath, int workers, int queueCapacity, int reportIntervalSeconds, int topK)
    {
        IServiceProvider? serviceProvider = null;
        IFilesystemWatcherAdapter? watcher = null;
        IProcessingCoordinator? coordinator = null;
        IReporter? reporter = null;
        BoundedEventBus<FsEvent>? bus = null;
        
        try
        {
            // Build DI container
            var services = new ServiceCollection();
            services.AddLogWatcherCore(workers, queueCapacity, reportIntervalSeconds, topK);
            services.AddFilesystemWatcher(watchPath);
            serviceProvider = services.BuildServiceProvider();
            
            // Resolve services
            bus = serviceProvider.GetRequiredService<BoundedEventBus<FsEvent>>();
            coordinator = serviceProvider.GetRequiredService<IProcessingCoordinator>();
            reporter = serviceProvider.GetRequiredService<IReporter>();
            watcher = serviceProvider.GetRequiredService<IFilesystemWatcherAdapter>();
            
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
            
            var configSummary = $"WatchPath={watchPath}; Workers={workers}; QueueCapacity={queueCapacity}; ReportIntervalSeconds={reportIntervalSeconds}; TopK={topK}";
            Console.WriteLine("Started: " + configSummary);
            
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
            
            // Dispose service provider
            if (serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
    
    /// <summary>
    /// Requests a coordinated shutdown of the provided host components. Safe to call multiple times.
    /// Non-null components will be stopped/disposed where applicable; exceptions thrown by components are logged to <see cref="Console.Error"/>.
    /// </summary>
    private static void TriggerShutdown(BoundedEventBus<FsEvent>? bus, IFilesystemWatcherAdapter? watcher,
        IProcessingCoordinator? coordinator, IReporter? reporter)
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
