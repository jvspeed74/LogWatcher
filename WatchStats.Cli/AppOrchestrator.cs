using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WatchStats.Core;
using WatchStats.Core.Concurrency;
using WatchStats.Core.Events;
using WatchStats.Core.IO;
using WatchStats.Core.Metrics;

namespace WatchStats.Cli;

/// <summary>
/// Orchestrates the lifecycle of WatchStats application components.
/// </summary>
public sealed class AppOrchestrator
{
    private readonly ILogger<AppOrchestrator> _logger;
    private readonly FilesystemWatcherAdapter _watcher;
    private readonly ProcessingCoordinator _coordinator;
    private readonly Reporter _reporter;
    private readonly BoundedEventBus<FsEvent> _bus;
    private readonly CliConfig _config;
    
    private readonly ManualResetEventSlim _shutdownEvent = new(false);
    private int _shutdownRequested = 0;

    public AppOrchestrator(
        FilesystemWatcherAdapter watcher,
        ProcessingCoordinator coordinator,
        Reporter reporter,
        BoundedEventBus<FsEvent> bus,
        CliConfig config,
        ILogger<AppOrchestrator> logger)
    {
        _watcher = watcher;
        _coordinator = coordinator;
        _reporter = reporter;
        _bus = bus;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Starts all application components in the correct order with lifecycle logging.
    /// </summary>
    public void Start()
    {
        _logger.LogInformation(
            eventId: new EventId(1, "DI_START_SEQUENCE"),
            "Starting application components");

        // Start coordinator (worker threads)
        StartComponent("WORKERS", () => _coordinator.Start(), 
            new { workerCount = _config.Workers });

        // Start reporter
        StartComponent("REPORTER", () => _reporter.Start(), 
            new { intervalMs = _config.IntervalMs });

        // Start watcher
        StartComponent("WATCHER", () => _watcher.Start(), 
            new { watchPath = _config.WatchPath });

        _logger.LogInformation(
            eventId: new EventId(5, "DI_START_COMPLETE"),
            "Application startup complete");
    }

    /// <summary>
    /// Stops all application components in the correct order with lifecycle logging.
    /// Safe to call multiple times.
    /// </summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1) return;

        _logger.LogInformation(
            eventId: new EventId(10, "DI_STOP_SEQUENCE_BEGIN"),
            "Initiating shutdown sequence");

        try
        {
            // Stop watcher
            StopComponent("WATCHER", () => _watcher.Stop());

            // Stop bus
            StopComponent("BUS", () => _bus.Stop());

            // Stop coordinator
            StopComponent("WORKERS", () => _coordinator.Stop());

            // Stop reporter
            StopComponent("REPORTER", () => _reporter.Stop());

            // Dispose watcher
            try
            {
                _watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing watcher");
            }

            _logger.LogInformation(
                eventId: new EventId(15, "DI_STOP_COMPLETE"),
                "Shutdown sequence complete");
        }
        finally
        {
            _shutdownEvent.Set();
        }
    }

    /// <summary>
    /// Blocks until shutdown has been requested and completed.
    /// </summary>
    public void WaitForShutdown() => _shutdownEvent.Wait();

    private void StartComponent(string componentName, Action startAction, object? metadata = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            startAction();
            sw.Stop();

            var eventId = componentName switch
            {
                "WATCHER" => new EventId(2, "WATCHER_STARTED"),
                "WORKERS" => new EventId(3, "WORKERS_STARTED"),
                "REPORTER" => new EventId(4, "REPORTER_STARTED"),
                _ => new EventId(0, $"{componentName}_STARTED")
            };

            if (metadata != null)
            {
                _logger.LogInformation(eventId, "{ComponentName} started. {@Metadata}", componentName, metadata);
            }
            else
            {
                _logger.LogInformation(eventId, "{ComponentName} started", componentName);
            }

            if (sw.ElapsedMilliseconds > 5000)
            {
                _logger.LogWarning(
                    eventId: new EventId(20, "START_TIMEOUT_WARNING"),
                    "Component start exceeded timeout. component={ComponentName}, elapsedMs={ElapsedMs}",
                    componentName,
                    sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting {ComponentName}", componentName);
            throw;
        }
    }

    private void StopComponent(string componentName, Action stopAction)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            stopAction();
            sw.Stop();

            var eventId = componentName switch
            {
                "WATCHER" => new EventId(11, "WATCHER_STOPPED"),
                "BUS" => new EventId(12, "BUS_STOPPED"),
                "WORKERS" => new EventId(13, "WORKERS_STOPPED"),
                "REPORTER" => new EventId(14, "REPORTER_STOPPED"),
                _ => new EventId(0, $"{componentName}_STOPPED")
            };

            _logger.LogInformation(eventId, "{ComponentName} stopped", componentName);

            if (sw.ElapsedMilliseconds > 5000)
            {
                _logger.LogWarning(
                    eventId: new EventId(21, "STOP_TIMEOUT_WARNING"),
                    "Component stop exceeded timeout. component={ComponentName}, elapsedMs={ElapsedMs}",
                    componentName,
                    sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping {ComponentName}", componentName);
        }
    }
}
