using LogWatcher.Core.FileManagement;

namespace LogWatcher.Tests.Unit.Core;

public class FileStateRegistryTests
{
    [Fact]
    public void FinalizeDelete_RemovesStateAndIncrementsEpoch()
    {
        var reg = new FileStateRegistry();
        var fs = reg.GetOrCreate("/tmp/x.log");
        lock (fs.Gate)
        {
            fs.Offset = 123;
            fs.Carry.Append(new ReadOnlySpan<byte>(new byte[] { 1, 2, 3 }));
        }

        reg.FinalizeDelete("/tmp/x.log");

        // state should be removed
        Assert.False(reg.TryGet("/tmp/x.log", out _));
        // epoch should be incremented
        Assert.Equal(1, reg.GetCurrentEpoch("/tmp/x.log"));

        // create again -> new generation should be epoch + 1
        var fs2 = reg.GetOrCreate("/tmp/x.log");
        Assert.Equal(2, fs2.Generation);
        lock (fs2.Gate)
        {
            Assert.Equal(0, fs2.Offset);
            Assert.Equal(0, fs2.Carry.Length);
        }
    }

    [Fact]
    public void MarkDeletePending_ClearsDirty()
    {
        var fs = new FileState();
        fs.MarkDirtyIfAllowed();
        Assert.True(fs.IsDirty);
        fs.MarkDeletePending();
        Assert.True(fs.IsDeletePending);
        Assert.False(fs.IsDirty);
    }

    [Fact]
    public void MarkDirty_DoesNotSetWhenDeletePending()
    {
        var fs = new FileState();
        fs.MarkDeletePending();
        fs.MarkDirtyIfAllowed();
        Assert.True(fs.IsDeletePending);
        Assert.False(fs.IsDirty);
    }

    [Fact]
    public void Concurrent_GetOrCreate_ReturnsSameInstanceUntilDeleted()
    {
        var reg = new FileStateRegistry();
        var path = "/tmp/concurrent.log";

        var results = new FileState[16];
        Parallel.For(0, 16, i => { results[i] = reg.GetOrCreate(path); });

        // all should point to the same instance
        var first = results[0];
        foreach (var r in results) Assert.Same(first, r);

        // finalize delete
        reg.FinalizeDelete(path);

        // new creation returns a different instance
        var n = reg.GetOrCreate(path);
        Assert.NotSame(first, n);
    }
}