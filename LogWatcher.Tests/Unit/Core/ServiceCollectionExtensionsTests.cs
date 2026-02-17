using LogWatcher.Core;
using LogWatcher.Core.Concurrency;
using LogWatcher.Core.Events;
using LogWatcher.Core.IO;
using LogWatcher.Core.Metrics;
using LogWatcher.Core.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace LogWatcher.Tests.Unit.Core;

/// <summary>
/// Unit tests for dependency injection configuration via ServiceCollectionExtensions.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLogWatcherCore_RegistersAllRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        const int workers = 4;
        const int queueCapacity = 1000;
        const int reportIntervalSeconds = 2;
        const int topK = 10;

        // Act
        services.AddLogWatcherCore(workers, queueCapacity, reportIntervalSeconds, topK);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - all services can be resolved
        Assert.NotNull(serviceProvider.GetRequiredService<BoundedEventBus<FsEvent>>());
        Assert.NotNull(serviceProvider.GetRequiredService<IFileStateRegistry>());
        Assert.NotNull(serviceProvider.GetRequiredService<WorkerStats[]>());
        Assert.NotNull(serviceProvider.GetRequiredService<IProcessingCoordinator>());
        Assert.NotNull(serviceProvider.GetRequiredService<IReporter>());
    }

    [Fact]
    public void AddLogWatcherCore_BoundedEventBus_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<BoundedEventBus<FsEvent>>();
        var instance2 = serviceProvider.GetRequiredService<BoundedEventBus<FsEvent>>();

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddLogWatcherCore_FileStateRegistry_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<IFileStateRegistry>();
        var instance2 = serviceProvider.GetRequiredService<IFileStateRegistry>();

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddLogWatcherCore_WorkerStatsArray_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        const int workers = 4;
        services.AddLogWatcherCore(workers, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<WorkerStats[]>();
        var instance2 = serviceProvider.GetRequiredService<WorkerStats[]>();

        // Assert
        Assert.Same(instance1, instance2);
        Assert.Equal(workers, instance1.Length);
    }

    [Fact]
    public void AddLogWatcherCore_ProcessingCoordinator_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<IProcessingCoordinator>();
        var instance2 = serviceProvider.GetRequiredService<IProcessingCoordinator>();

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddLogWatcherCore_Reporter_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<IReporter>();
        var instance2 = serviceProvider.GetRequiredService<IReporter>();

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddLogWatcherCore_FileTailer_IsTransient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<IFileTailer>();
        var instance2 = serviceProvider.GetRequiredService<IFileTailer>();

        // Assert
        Assert.NotSame(instance1, instance2);
    }

    [Fact]
    public void AddLogWatcherCore_FileProcessor_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<IFileProcessor>();
        var instance2 = serviceProvider.GetRequiredService<IFileProcessor>();

        // Assert - FileProcessor is singleton since it's shared across all workers
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void AddFilesystemWatcher_RegistersWatcherService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var watchPath = Path.GetTempPath();

        // Act
        services.AddFilesystemWatcher(watchPath);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var watcher = serviceProvider.GetRequiredService<IFilesystemWatcherAdapter>();
        Assert.NotNull(watcher);
    }

    [Fact]
    public void AddFilesystemWatcher_Watcher_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var watchPath = Path.GetTempPath();
        services.AddFilesystemWatcher(watchPath);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var instance1 = serviceProvider.GetRequiredService<IFilesystemWatcherAdapter>();
        var instance2 = serviceProvider.GetRequiredService<IFilesystemWatcherAdapter>();

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void ServiceProvider_CanResolveConcreteImplementations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogWatcherCore(workers: 4, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - verify concrete types are correct
        Assert.IsType<FileStateRegistry>(serviceProvider.GetRequiredService<IFileStateRegistry>());
        Assert.IsType<FileTailer>(serviceProvider.GetRequiredService<IFileTailer>());
        Assert.IsType<FileProcessor>(serviceProvider.GetRequiredService<IFileProcessor>());
        Assert.IsType<ProcessingCoordinator>(serviceProvider.GetRequiredService<IProcessingCoordinator>());
        Assert.IsType<Reporter>(serviceProvider.GetRequiredService<IReporter>());
    }

    [Fact]
    public void AddLogWatcherCore_WorkerStatsArray_InitializesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        const int workers = 8;
        services.AddLogWatcherCore(workers, queueCapacity: 1000, reportIntervalSeconds: 2, topK: 10);
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var workerStats = serviceProvider.GetRequiredService<WorkerStats[]>();

        // Assert
        Assert.Equal(workers, workerStats.Length);
        foreach (var stats in workerStats)
        {
            Assert.NotNull(stats);
        }
    }
}
