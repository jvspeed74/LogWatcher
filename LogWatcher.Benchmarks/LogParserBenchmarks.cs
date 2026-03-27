using BenchmarkDotNet.Attributes;

using LogWatcher.Core.Processing.Parsing;

namespace LogWatcher.Benchmarks;

[MemoryDiagnoser]
public class LogParserBenchmarks
{
    private static readonly byte[] ValidLineWithLatency =
        "2024-01-01T00:00:00.0000000Z INFO request-handled processing complete latency_ms=150\n"u8.ToArray();

    private static readonly byte[] ValidLineWithoutLatency =
        "2024-01-01T00:00:00.0000000Z INFO key message text here\n"u8.ToArray();

    private static readonly byte[] MalformedTimestampLine =
        "not-a-timestamp INFO key message\n"u8.ToArray();

    private byte[][] _lines1000 = [];

    [GlobalSetup]
    public void Setup()
    {
        _lines1000 = new byte[1000][];
        for (var i = 0; i < 1000; i++)
            _lines1000[i] = ValidLineWithLatency;
    }

    /// <summary>
    /// Per-parse allocation expected: one string for timestamp + one for message key.
    /// </summary>
    [Benchmark]
    public bool TryParse_ValidLine_WithLatency() =>
        LogParser.TryParse(ValidLineWithLatency, out _);

    [Benchmark]
    public bool TryParse_ValidLine_WithoutLatency() =>
        LogParser.TryParse(ValidLineWithoutLatency, out _);

    /// <summary>
    /// Early-exit path: timestamp parse fails immediately.
    /// Expected allocation: minimal (no message key string).
    /// </summary>
    [Benchmark]
    public bool TryParse_MalformedTimestamp() =>
        LogParser.TryParse(MalformedTimestampLine, out _);

    /// <summary>
    /// Amortised cost across 1 000 valid parses.
    /// </summary>
    [Benchmark]
    public void TryParse_1000ValidLines()
    {
        for (var i = 0; i < 1000; i++)
            LogParser.TryParse(_lines1000[i], out _);
    }
}