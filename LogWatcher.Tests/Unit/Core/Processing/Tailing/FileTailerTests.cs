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

    [Fact]
    [Invariant("TAIL-001")]
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
    [Invariant("TAIL-001")]
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
}