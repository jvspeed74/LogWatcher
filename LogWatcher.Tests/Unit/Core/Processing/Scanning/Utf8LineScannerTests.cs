using System.Text;

using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Processing.Scanning;

namespace LogWatcher.Tests.Unit.Core.Processing.Scanning;

public class Utf8LineScannerTests
{
    private static string GetString(ReadOnlySpan<byte> span)
    {
        return Encoding.UTF8.GetString(span);
    }

    [Fact]
    [Invariant("SCAN-001")]
    [Invariant("SCAN-002")]
    public void Scan_WithLfTerminatedLines_EmitsLinesWithoutDelimiter()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();
        var bytes = Encoding.UTF8.GetBytes("line1\nline2\n");

        Utf8LineScanner.Scan(bytes, ref carry, s => emitted.Add(GetString(s)));

        Assert.Equal(new[] { "line1", "line2" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    [Invariant("SCAN-001")]
    [Invariant("SCAN-002")]
    [Invariant("SCAN-003")]
    public void Scan_WithCrlfTerminatedLines_EmitsLinesWithoutDelimiters()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();
        var bytes = Encoding.UTF8.GetBytes("a\r\nb\r\n");

        Utf8LineScanner.Scan(bytes, ref carry, s => emitted.Add(GetString(s)));

        Assert.Equal(new[] { "a", "b" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    [Invariant("SCAN-001")]
    [Invariant("SCAN-003")]
    [Invariant("SCAN-004")]
    public void Scan_WithCrlfSplitAcrossChunks_EmitsCompleteLines()
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
    [Invariant("SCAN-001")]
    [Invariant("SCAN-004")]
    public void Scan_WithLineSplitAcrossChunks_PreservesCarryAndEmitsWhenComplete()
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
    [Invariant("SCAN-001")]
    public void Scan_WithMultipleLinesInSingleChunk_EmitsAllLines()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("1\n2\n3\n"), ref carry, s => emitted.Add(GetString(s)));
        Assert.Equal(new[] { "1", "2", "3" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    [Invariant("SCAN-001")]
    [Invariant("SCAN-002")]
    public void Scan_WithConsecutiveNewlines_EmitsEmptyLines()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("\n\n"), ref carry, s => emitted.Add(GetString(s)));
        // expect two empty lines
        Assert.Equal(new[] { "", "" }, emitted);
        Assert.Equal(0, carry.Length);
    }

    [Fact]
    [Invariant("SCAN-001")]
    [Invariant("SCAN-004")]
    public void Scan_WithPartialLineNoNewline_PreservesInCarry()
    {
        var carry = default(PartialLineBuffer);
        var emitted = new List<string>();

        Utf8LineScanner.Scan(Encoding.UTF8.GetBytes("partial"), ref carry, s => emitted.Add(GetString(s)));
        Assert.Empty(emitted);
        Assert.Equal("partial", GetString(carry.AsSpan()));
    }

    [Fact]
    [Invariant("SCAN-005")]
    public void Scan_SpanPassedToOnLine_MustBeConsumedWithinCallback()
    {
        // The span passed to onLine is only valid for the duration of the callback.
        // Callers must convert or copy it immediately and must not retain it across calls.
        var carry = default(PartialLineBuffer);
        var linesCollected = new List<string>();
        var bytes = Encoding.UTF8.GetBytes("alpha\nbeta\n");

        Utf8LineScanner.Scan(bytes, ref carry, span =>
        {
            // Consume span immediately by converting to string within the callback
            linesCollected.Add(GetString(span));
        });

        Assert.Equal(2, linesCollected.Count);
        Assert.Equal("alpha", linesCollected[0]);
        Assert.Equal("beta", linesCollected[1]);
    }
}