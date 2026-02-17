using Microsoft.Extensions.DependencyInjection;
using LogWatcher.Core.Concurrency;
using LogWatcher.Core.Events;
using LogWatcher.Core.IO;
using LogWatcher.Core.Metrics;
using LogWatcher.Core.Processing;

namespace LogWatcher.Core;

/// <summary>
/// Extension methods for configuring LogWatcher Core services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LogWatcher Core services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="workers">Number of worker threads.</param>
    /// <param name="queueCapacity">Capacity of the filesystem event queue.</param>
    /// <param name="reportIntervalSeconds">Report interval in seconds.</param>
    /// <param name="topK">Top-K value for reporting.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLogWatcherCore(
        this IServiceCollection services,
        int workers,
        int queueCapacity,
        int reportIntervalSeconds,
        int topK)
    {
        // Register singletons for shared state
        services.AddSingleton<BoundedEventBus<FsEvent>>(sp => 
            new BoundedEventBus<FsEvent>(queueCapacity));
        
        services.AddSingleton<IFileStateRegistry, FileStateRegistry>();
        
        // Register worker stats array as singleton
        services.AddSingleton(sp => 
        {
            var workerStats = new WorkerStats[workers];
            for (int i = 0; i < workerStats.Length; i++)
            {
                workerStats[i] = new WorkerStats();
            }
            return workerStats;
        });
        
        // Register transient/scoped services
        services.AddTransient<IFileTailer, FileTailer>();
        services.AddTransient<IFileProcessor, FileProcessor>();
        
        // Register coordinator and reporter as singletons
        services.AddSingleton<IProcessingCoordinator>(sp =>
        {
            var bus = sp.GetRequiredService<BoundedEventBus<FsEvent>>();
            var registry = sp.GetRequiredService<IFileStateRegistry>();
            var processor = sp.GetRequiredService<IFileProcessor>();
            var workerStats = sp.GetRequiredService<WorkerStats[]>();
            return new ProcessingCoordinator(bus, registry, processor, workerStats, workers);
        });
        
        services.AddSingleton<IReporter>(sp =>
        {
            var workerStats = sp.GetRequiredService<WorkerStats[]>();
            var bus = sp.GetRequiredService<BoundedEventBus<FsEvent>>();
            return new Reporter(workerStats, bus, topK, reportIntervalSeconds);
        });
        
        return services;
    }
    
    /// <summary>
    /// Adds the filesystem watcher adapter to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="watchPath">The path to watch for file changes.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFilesystemWatcher(
        this IServiceCollection services,
        string watchPath)
    {
        services.AddSingleton<IFilesystemWatcherAdapter>(sp =>
        {
            var bus = sp.GetRequiredService<BoundedEventBus<FsEvent>>();
            return new FilesystemWatcherAdapter(watchPath, bus);
        });
        
        return services;
    }
}
