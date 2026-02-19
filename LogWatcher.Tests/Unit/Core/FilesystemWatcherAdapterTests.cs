using LogWatcher.Core.Events;
using LogWatcher.Core.Ingestion;

namespace LogWatcher.Tests.Unit.Core;

public class FilesystemWatcherAdapterTests : IDisposable
{
    private readonly string _dir;

    public FilesystemWatcherAdapterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "watchstats_watcher_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void CreatedLogFile_PublishesEventToBus()
    {
        var bus = new BoundedEventBus<FsEvent>(1000);
        using var adapter = new FilesystemWatcherAdapter(_dir, bus);
        adapter.Start();

        var path = Path.Combine(_dir, "x.log");
        File.WriteAllText(path, "hello");

        // Wait up to 2s for event propagation
        var attempts = 0;
        while (bus.PublishedCount == 0 && attempts++ < 20) Thread.Sleep(100);

        adapter.Stop();

        Assert.True(bus.PublishedCount > 0);
    }
}