using System.Text;

using LogWatcher.Core.Processing.Parsing;

namespace LogWatcher.Tests.Unit.Core.Processing.Parsing;

public class LogParserTests
{
    private static ReadOnlySpan<byte> AsUtf8(string s)
    {
        return Encoding.UTF8.GetBytes(s).AsSpan();
    }

    [Fact]
    [Invariant("PRS-001")]
    [Invariant("PRS-002")]
    [Invariant("PRS-003")]
    public void TryParse_WithValidLineAndLatency_ParsesAllFields()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z INFO request_started latency_ms=123");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal(new DateTimeOffset(2023, 1, 2, 3, 4, 5, TimeSpan.Zero), parsed.Timestamp);
        Assert.Equal(LogLevel.Info, parsed.Level);
        Assert.Equal("request_started", Encoding.UTF8.GetString(parsed.MessageKey));
        Assert.Equal(123, parsed.LatencyMs);
    }

    [Fact]
    [Invariant("PRS-001")]
    public void TryParse_WithValidLineWithoutLatency_ParsesSuccessfully()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z WARN something_happened");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal(LogLevel.Warn, parsed.Level);
        Assert.Equal("something_happened", Encoding.UTF8.GetString(parsed.MessageKey));
        Assert.Null(parsed.LatencyMs);
    }

    [Fact]
    [Invariant("PRS-001")]
    [Invariant("PRS-003")]
    public void TryParse_WithMalformedTimestamp_ReturnsFalse()
    {
        var line = AsUtf8("not-a-ts INFO hi latency_ms=10");
        Assert.False(LogParser.TryParse(line, out _));
    }

    // TODO: map to invariant
    [Fact]
    public void TryParse_WithUnknownLevel_MapsToOther()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z FOOBAR msg");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal(LogLevel.Other, parsed.Level);
    }

    [Fact]
    [Invariant("PRS-002")]
    public void TryParse_WithMultipleTokens_UsesFirstTokenAsMessageKey()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z INFO alpha beta gamma latency_ms=1");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal("alpha", Encoding.UTF8.GetString(parsed.MessageKey));
    }

    [Fact]
    [Invariant("PRS-001")]
    public void TryParse_WithMalformedLatency_ParseSucceedsWithNullLatency()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z INFO something latency_ms=abc");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Null(parsed.LatencyMs);
    }

    [Fact]
    [Invariant("PRS-004")]
    public void MessageKey_SpanPointsIntoInputBytes_MustBeConsumedBeforeInputIsReleased()
    {
        // ParsedLogLine is a ref struct; MessageKey is a ReadOnlySpan<byte> pointing into the
        // original input bytes. Callers must consume it within the scope where the input is valid.
        var input = Encoding.UTF8.GetBytes("2023-01-02T03:04:05Z INFO request_started latency_ms=5");

        string keyConsumedInScope = string.Empty;
        if (LogParser.TryParse(input, out var parsed))
        {
            // Consume MessageKey immediately while input is still in scope
            keyConsumedInScope = Encoding.UTF8.GetString(parsed.MessageKey);
        }

        Assert.Equal("request_started", keyConsumedInScope);
    }

    [Fact]
    [Invariant("PRS-003")]
    public void TryParse_WithOffsetOrZuluTimestamp_ParsesBoth()
    {
        var z = AsUtf8("2023-01-02T03:04:05Z INFO zmsg");
        var off = AsUtf8("2023-01-02T03:04:05-06:00 INFO offmsg");

        Assert.True(LogParser.TryParse(z, out var pz));
        Assert.Equal(new DateTimeOffset(2023, 1, 2, 3, 4, 5, TimeSpan.Zero), pz.Timestamp);

        Assert.True(LogParser.TryParse(off, out var po));
        // adjusted to UTC
        Assert.Equal(new DateTimeOffset(2023, 1, 2, 9, 4, 5, TimeSpan.Zero), po.Timestamp);
    }
}