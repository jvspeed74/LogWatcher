using System.Text;
using WatchStats.Core.Processing;

namespace WatchStats.Tests.Unit.Core.Processing;

public class Utf8LineScannerTests
{
    private static string GetString(ReadOnlySpan<byte> span)
    {
        return Encoding.UTF8.GetString(span);
    }

    [Fact]
    public void LF_only_single_chunk()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();
        var bytes = Encoding.UTF8.GetBytes("line1\nline2\n");

        Utf8LineScanner.Scan(bytes, ref carry, s => emitted.Add(GetString(s)));

        Assert.Equal(new[] { "line1", "line2" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    public void CRLF_single_chunk()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();
        var bytes = Encoding.UTF8.GetBytes("a\r\nb\r\n");

        Utf8LineScanner.Scan(bytes, ref carry, s => emitted.Add(GetString(s)));

        Assert.Equal(new[] { "a", "b" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    public void CRLF_split_across_chunks()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("first line\r"), ref carry, s => emitted.Add(GetString(s)));
        // nothing emitted yet
        Assert.Empty(emitted);
        Assert.Equal("first line\r", GetString(carry.AsSpan()));

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("\nsecond\r\n"), ref carry, s => emitted.Add(GetString(s)));
        Assert.Equal(new[] { "first line", "second" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    public void Line_split_across_chunks_no_newline_until_later()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("hello "), ref carry, s => emitted.Add(GetString(s)));
        Assert.Empty(emitted);

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("world\n"), ref carry, s => emitted.Add(GetString(s)));
        Assert.Single(emitted);
        Assert.Equal("hello world", emitted[0]);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    public void Multiple_lines_in_one_chunk()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("1\n2\n3\n"), ref carry, s => emitted.Add(GetString(s)));
        Assert.Equal(new[] { "1", "2", "3" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    public void Empty_lines_between_newlines()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("\n\n"), ref carry, s => emitted.Add(GetString(s)));
        // expect two empty lines
        Assert.Equal(new[] { "", "" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    public void Carryover_preserved_when_no_newline()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("partial"), ref carry, s => emitted.Add(GetString(s)));
        Assert.Empty(emitted);
        Assert.Equal("partial", GetString(carry.AsSpan()));
    }
}