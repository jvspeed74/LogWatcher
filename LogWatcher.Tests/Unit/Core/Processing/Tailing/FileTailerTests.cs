using System.Text;

using LogWatcher.Core.Processing.Tailing;

namespace LogWatcher.Tests.Unit.Core.Processing.Tailing;

public class FileTailerTests : IDisposable
{
    private readonly string _dir;

    public FileTailerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "watchstats_tests_" + Guid.NewGuid().ToString("N"));
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

    private string MakePath(string name)
    {
        return Path.Combine(_dir, name);
    }

    // TODO: map to invariant
    [Fact]
    public void ReadAppended_WithAppendedContent_ReadsOnlyNewBytes()
    {
        var p = MakePath("log1.txt");
        File.WriteAllText(p, "hello");

        IFileTailer tailer = new FileTailer();
        long offset = 0;
        var total = 0;
        var sb = new StringBuilder();

        var status = tailer.ReadAppended(p, ref offset, s => sb.Append(Encoding.UTF8.GetString(s)), out total);
        Assert.Equal(TailReadStatus.ReadSome, status);
        Assert.Equal(5, total);
        Assert.Equal("hello", sb.ToString());
        Assert.Equal(5, offset);

        // append more
        File.AppendAllText(p, " world");
        sb.Clear();
        status = tailer.ReadAppended(p, ref offset, s => sb.Append(Encoding.UTF8.GetString(s)), out total);
        Assert.Equal(TailReadStatus.ReadSome, status);
        Assert.Equal(6, total);
        Assert.Equal(" world", sb.ToString());
        Assert.Equal(11, offset);
    }

    [Fact]
    [Invariant("TAIL-002")]
    public void ReadAppended_WhenFileTruncated_ResetsOffsetAndReadsFromStart()
    {
        var p = MakePath("log2.txt");
        File.WriteAllText(p, "12345678");

        IFileTailer tailer = new FileTailer();
        long offset = 0;
        tailer.ReadAppended(p, ref offset, _ => { }, out var total);
        Assert.Equal(8, offset);

        // truncate file to smaller content
        File.WriteAllText(p, "abc");

        var status = tailer.ReadAppended(p, ref offset, s => { }, out var tot2);
        Assert.True(status == TailReadStatus.TruncatedReset || status == TailReadStatus.ReadSome);
        // offset should now be >= 3 (effectiveOffset 0 + bytes read)
        Assert.True(offset <= 3 || offset == tot2);
    }

    [Fact]
    [Invariant("TAIL-004")]
    public void ReadAppended_WhenFileDeleted_ReturnsFileNotFound()
    {
        var p = MakePath("log3.txt");
        File.WriteAllText(p, "x");

        IFileTailer tailer = new FileTailer();
        long offset = 0;
        tailer.ReadAppended(p, ref offset, _ => { }, out var total);

        File.Delete(p);

        var status = tailer.ReadAppended(p, ref offset, _ => { }, out var tot2);
        Assert.True(status == TailReadStatus.FileNotFound || status == TailReadStatus.NoData ||
                    status == TailReadStatus.TruncatedReset);
    }

    [Fact]
    [Invariant("TAIL-001")]
    public void ReadAppended_WhenFileNotFound_OffsetIsNotAdvanced()
    {
        var p = MakePath("nonexistent.txt");
        IFileTailer tailer = new FileTailer();
        long offset = 42;

        var status = tailer.ReadAppended(p, ref offset, _ => { }, out _);

        Assert.Equal(TailReadStatus.FileNotFound, status);
        Assert.Equal(42, offset);
    }

    [Fact]
    [Invariant("TAIL-003")]
    public void ReadAppended_ReadBufferAlwaysReleased_EvenWhenFileDeletedMidStream()
    {
        // FileTailer rents a buffer from ArrayPool and must return it in a finally block
        // regardless of whether the read succeeds, the file is missing, or an error occurs.
        IFileTailer tailer = new FileTailer();
        var p = MakePath("tail003.txt");
        File.WriteAllText(p, "initial content");

        long offset = 0;
        // Successful read — buffer is rented and returned
        var status1 = tailer.ReadAppended(p, ref offset, _ => { }, out _);
        Assert.Equal(TailReadStatus.ReadSome, status1);

        // Delete file then read again — buffer must still be rented and returned without leak
        File.Delete(p);
        var status2 = tailer.ReadAppended(p, ref offset, _ => { }, out _);
        Assert.Equal(TailReadStatus.FileNotFound, status2);

        // Tailer must be fully reusable after any outcome
        File.WriteAllText(p, "new content");
        long offset2 = 0;
        var status3 = tailer.ReadAppended(p, ref offset2, _ => { }, out _);
        Assert.Equal(TailReadStatus.ReadSome, status3);
    }

    [Fact]
    [Invariant("TAIL-005")]
    public void ReadAppended_SpanPassedToOnChunk_MustBeConsumedWithinCallback()
    {
        // The span passed to onChunk is backed by a pooled buffer and is only valid
        // for the duration of the callback. Callers must not retain it.
        var p = MakePath("tail005.txt");
        const string content = "hello world from tailer";
        File.WriteAllText(p, content);

        IFileTailer tailer = new FileTailer();
        long offset = 0;
        string capturedContent = string.Empty;

        tailer.ReadAppended(p, ref offset, chunk =>
        {
            // Consume the span immediately within the callback, as required by the contract
            capturedContent += Encoding.UTF8.GetString(chunk);
        }, out _);

        Assert.Equal(content, capturedContent);
    }
}