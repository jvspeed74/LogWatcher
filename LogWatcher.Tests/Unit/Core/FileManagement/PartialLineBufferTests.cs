using LogWatcher.Core.FileManagement;

namespace LogWatcher.Tests.Unit.Core.FileManagement;

public class PartialLineBufferTests
{
    [Fact]
    [Invariant("FM-PLB-001")]
    public void Length_AfterAppend_ReflectsValidBytesNotAllocatedCapacity()
    {
        var buf = default(PartialLineBuffer);
        // Append fewer bytes than the initial internal allocation (256)
        buf.Append(new byte[10]);

        Assert.Equal(10, buf.Length);
        // Internal buffer is larger than 10 bytes; Length must not equal capacity
        Assert.NotNull(buf.Buffer);
        Assert.True(buf.Buffer!.Length > buf.Length,
            "Length must reflect valid bytes only, not the allocated capacity of the underlying storage.");
    }

    [Fact]
    [Invariant("FM-PLB-002")]
    public void Append_BeyondInitialCapacity_PreservesExistingBytes()
    {
        var buf = default(PartialLineBuffer);
        var initial = Enumerable.Range(0, 100).Select(i => (byte)(i % 256)).ToArray();
        buf.Append(initial);

        // Trigger a growth reallocation by appending more than the remaining initial capacity
        var extra = new byte[300];
        buf.Append(extra);

        // Bytes written before the growth event must be readable and correct after it
        var span = buf.AsSpan();
        for (int i = 0; i < initial.Length; i++)
            Assert.Equal(initial[i], span[i]);
    }

    [Fact]
    [Invariant("FM-PLB-003")]
    public void Append_WithEmptyInput_IsNoOp()
    {
        var buf = default(PartialLineBuffer);
        buf.Append(new byte[] { 1, 2, 3 });
        var storageBeforeCall = buf.Buffer;
        var lengthBeforeCall = buf.Length;

        // Empty append must leave Length and the underlying storage unchanged
        buf.Append(ReadOnlySpan<byte>.Empty);

        Assert.Equal(lengthBeforeCall, buf.Length);
        Assert.Same(storageBeforeCall, buf.Buffer);
    }

    [Fact]
    [Invariant("FM-PLB-004")]
    public void Clear_MakesBufferEmptyWithoutReleasingStorage()
    {
        var buf = default(PartialLineBuffer);
        buf.Append(new byte[] { 1, 2, 3 });
        var storageBefore = buf.Buffer;

        buf.Clear();

        Assert.Equal(0, buf.Length);
        Assert.Same(storageBefore, buf.Buffer); // underlying array is retained
    }

    [Fact]
    [Invariant("FM-PLB-004")]
    public void Release_MakesBufferEmptyAndReleasesStorage()
    {
        var buf = default(PartialLineBuffer);
        buf.Append(new byte[] { 1, 2, 3 });

        buf.Release();

        Assert.Equal(0, buf.Length);
        Assert.Null(buf.Buffer); // underlying array reference is released
    }

    [Fact]
    [Invariant("FM-PLB-005")]
    public void AsSpan_AfterMutatingCall_ReflectsUpdatedState()
    {
        var buf = default(PartialLineBuffer);
        buf.Append(new byte[] { 10, 20, 30 });

        // The span returned by AsSpan() is only valid until the next mutating call.
        // Callers must not retain the span across mutations.
        _ = buf.AsSpan(); // consume the span before mutating

        buf.Clear(); // mutating call — previous span is now invalid

        // A new call to AsSpan() after mutation must reflect the updated state
        var spanAfterClear = buf.AsSpan();
        Assert.True(spanAfterClear.IsEmpty, "AsSpan must reflect current state after a mutating call.");
    }
}
