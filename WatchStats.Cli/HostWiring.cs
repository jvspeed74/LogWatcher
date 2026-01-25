using WatchStats.Core;
using WatchStats.Core.Concurrency;
using WatchStats.Core.Events;
using WatchStats.Core.IO;
using WatchStats.Core.Metrics;

namespace WatchStats.Cli;

internal static class HostWiring
{
    private static int _shutdownRequested = 0;
    private static readonly ManualResetEventSlim _shutdownEvent = new(false);

    /// <summary>
    /// Requests a coordinated shutdown of the provided host components. Safe to call multiple times.
    /// Non-null components will be stopped/disposed where applicable; exceptions thrown by components are logged to <see cref="Console.Error"/>.
    /// </summary>
    /// <param name="bus">Optional event bus to stop.</param>
    /// <param name="watcher">Optional filesystem watcher adapter to stop and dispose.</param>
    /// <param name="coordinator">Optional processing coordinator to stop.</param>
    /// <param name="reporter">Optional reporter to stop.</param>
    public static void TriggerShutdown(BoundedEventBus<FsEvent>? bus, FilesystemWatcherAdapter? watcher,
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
    public static void WaitForShutdown() => _shutdownEvent.Wait();
}