using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Coordination;
using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Ingestion;
using LogWatcher.Core.Processing;
using LogWatcher.Core.Processing.Tailing;
using LogWatcher.Core.Reporting;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogWatcher.App;

/// <summary>
/// Hosted service that wires, starts, and shuts down all LogWatcher components.
/// </summary>
public sealed class LogWatcherService : BackgroundService
{
    private readonly LogWatcherOptions _options;
    private readonly IReadOnlyList<ISnapshotConsumer> _consumers;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LogWatcherService> _logger;

    public LogWatcherService(LogWatcherOptions options, IEnumerable<ISnapshotConsumer> consumers, ILoggerFactory loggerFactory, ILogger<LogWatcherService> logger)
    {
        _options = options;
        _consumers = consumers.ToList();
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BoundedEventBus<FsEvent>? bus = null;
        FileStateRegistry? registry = null;
        FileProcessor? processor = null;
        WorkerStats[]? workerStats = null;
        ProcessingCoordinator? coordinator = null;
        Reporter? reporter = null;
        FilesystemWatcherAdapter? watcher = null;

        try
        {
            // Construct components
            bus = new BoundedEventBus<FsEvent>(_options.QueueCapacity, _loggerFactory.CreateLogger<BoundedEventBus<FsEvent>>());
            registry = new FileStateRegistry(_loggerFactory.CreateLogger<FileStateRegistry>());
            var tailer = new FileTailer(_loggerFactory.CreateLogger<FileTailer>());
            processor = new FileProcessor(tailer, _loggerFactory.CreateLogger<FileProcessor>());
            workerStats = new WorkerStats[_options.Workers];
            for (int i = 0; i < workerStats.Length; i++)
                workerStats[i] = new WorkerStats();

            coordinator = new ProcessingCoordinator(bus, registry, processor, workerStats,
                workerCount: _options.Workers, logger: _loggerFactory.CreateLogger<ProcessingCoordinator>());
            reporter = new Reporter(workerStats, bus, _options.TopK,
                TimeSpan.FromSeconds(_options.ReportIntervalSeconds), logger: _loggerFactory.CreateLogger<Reporter>(), consumers: _consumers);
            watcher = new FilesystemWatcherAdapter(_options.WatchPath, bus, logger: _loggerFactory.CreateLogger<FilesystemWatcherAdapter>());

            // Start components in order
            coordinator.Start();
            reporter.Start();
            watcher.Start();

            _logger.LogInformation(
                "LogWatcher started. WatchPath={WatchPath} Workers={Workers} QueueCapacity={QueueCapacity} ReportIntervalSeconds={ReportIntervalSeconds} TopK={TopK}",
                _options.WatchPath, _options.Workers, _options.QueueCapacity,
                _options.ReportIntervalSeconds, _options.TopK);

            // Block until the host signals shutdown
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: stoppingToken cancelled on host shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in LogWatcherService");
            throw;
        }
        finally
        {
            _logger.LogInformation("LogWatcher shutting down...");

            StopComponent("watcher", () => watcher?.Stop());
            StopComponent("bus", () => bus?.Stop());
            StopComponent("coordinator", () => coordinator?.Stop());
            StopComponent("reporter", () => reporter?.Stop());
            StopComponent("watcher.Dispose", () => watcher?.Dispose());

            if (workerStats != null)
            {
                foreach (var ws in workerStats)
                    ws?.Dispose();
            }

            _logger.LogInformation("LogWatcher shutdown complete.");
        }
    }

    private void StopComponent(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping {Component}", name);
        }
    }
}