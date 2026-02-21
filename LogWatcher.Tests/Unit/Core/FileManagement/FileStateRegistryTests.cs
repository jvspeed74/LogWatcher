using LogWatcher.Core.FileManagement;

namespace LogWatcher.Tests.Unit.Core.FileManagement;

public class FileStateRegistryTests
{
    [Fact]
    [Invariant("FM-001")]
    public void GetOrCreate_AfterFinalizeDelete_ReturnsNewStateWithZeroOffset()
    {
        var reg = new FileStateRegistry();
        var path = "/tmp/test_fm001.log";

        var state1 = reg.GetOrCreate(path);
        lock (state1.Gate) { state1.Offset = 500; }

        reg.FinalizeDelete(path);

        // New state must not share or reuse the old offset
        var state2 = reg.GetOrCreate(path);
        Assert.NotSame(state1, state2);
        lock (state2.Gate)
        {
            Assert.Equal(0, state2.Offset);
        }
    }

    [Fact]
    [Invariant("FM-007")]
    public async Task FileState_OffsetMutatedUnderGate_ProducesConsistentResult()
    {
        var state = new FileState();
        const int threads = 8;
        const int iterations = 100;

        // Multiple threads incrementing offset while holding gate must produce a consistent total
        var tasks = Enumerable.Range(0, threads)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    lock (state.Gate)
                    {
                        state.Offset++;
                    }
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);
        Assert.Equal((long)(threads * iterations), state.Offset);
    }

    [Fact]
    [Invariant("FM-004")]
    [Invariant("FM-005")]
    [Invariant("FM-006")]
    [Invariant("FM-008")]
    public void FinalizeDelete_WithExistingState_RemovesStateAndIncrementsEpoch()
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
    [Invariant("FM-002")]
    [Invariant("FM-003")]
    public void MarkDeletePending_WhenDirtyIsSet_ClearsDirtyFlag()
    {
        var fs = new FileState();
        fs.MarkDirtyIfAllowed();
        Assert.True(fs.IsDirty);
        fs.MarkDeletePending();
        Assert.True(fs.IsDeletePending);
        Assert.False(fs.IsDirty);
    }

    [Fact]
    [Invariant("FM-003")]
    public void MarkDirtyIfAllowed_WhenDeletePending_DoesNotSetDirty()
    {
        var fs = new FileState();
        fs.MarkDeletePending();
        fs.MarkDirtyIfAllowed();
        Assert.True(fs.IsDeletePending);
        Assert.False(fs.IsDirty);
    }

    [Fact]
    [Invariant("FM-009")]
    public void GetOrCreate_ConcurrentCalls_ReturnsSameInstance()
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