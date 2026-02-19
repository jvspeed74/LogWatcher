using System.Text;

using LogWatcher.Core.Processing.Parsing;

namespace LogWatcher.Tests.Unit.Core;

public class LogParserTests
{
    private static ReadOnlySpan<byte> AsUtf8(string s)
    {
        return Encoding.UTF8.GetBytes(s).AsSpan();
    }

    [Fact]
    public void Parses_valid_line_with_latency()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z INFO request_started latency_ms=123");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal(new DateTimeOffset(2023, 1, 2, 3, 4, 5, TimeSpan.Zero), parsed.Timestamp);
        Assert.Equal(LogLevel.Info, parsed.Level);
        Assert.Equal("request_started", Encoding.UTF8.GetString(parsed.MessageKey));
        Assert.Equal(123, parsed.LatencyMs);
    }

    [Fact]
    public void Parses_valid_line_without_latency()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z WARN something_happened");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal(LogLevel.Warn, parsed.Level);
        Assert.Equal("something_happened", Encoding.UTF8.GetString(parsed.MessageKey));
        Assert.Null(parsed.LatencyMs);
    }

    [Fact]
    public void Malformed_timestamp_returns_false()
    {
        var line = AsUtf8("not-a-ts INFO hi latency_ms=10");
        Assert.False(LogParser.TryParse(line, out _));
    }

    [Fact]
    public void Unknown_level_maps_to_other()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z FOOBAR msg");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal(LogLevel.Other, parsed.Level);
    }

    [Fact]
    public void Message_key_is_first_token()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z INFO alpha beta gamma latency_ms=1");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Equal("alpha", Encoding.UTF8.GetString(parsed.MessageKey));
    }

    [Fact]
    public void Latency_malformed_is_null_but_parse_succeeds()
    {
        var line = AsUtf8("2023-01-02T03:04:05Z INFO something latency_ms=abc");
        Assert.True(LogParser.TryParse(line, out var parsed));
        Assert.Null(parsed.LatencyMs);
    }

    [Fact]
    public void Handles_offset_or_zulu_timestamps()
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