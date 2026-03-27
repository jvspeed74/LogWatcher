using BenchmarkDotNet.Attributes;

using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Processing.Scanning;

namespace LogWatcher.Benchmarks;

[MemoryDiagnoser]
public class Utf8LineScannerBenchmarks
{
    // 1 000 complete newline-terminated log lines in one contiguous buffer.
    private byte[] _chunk1000Lines = [];

    // Same data split so the last line crosses the chunk boundary.
    private byte[] _splitChunkA = [];
    private byte[] _splitChunkB = [];

    [GlobalSetup]
    public void Setup()
    {
        var line = "2024-01-01T00:00:00.0000000Z INFO key message text here\n"u8.ToArray();
        _chunk1000Lines = new byte[line.Length * 1000];
        for (var i = 0; i < 1000; i++)
            line.CopyTo(_chunk1000Lines, i * line.Length);

        // Split: everything except the last half-line, then the last half-line alone.
        var splitPoint = _chunk1000Lines.Length - line.Length / 2;
        _splitChunkA = _chunk1000Lines[..splitPoint];
        _splitChunkB = _chunk1000Lines[splitPoint..];
    }

    /// <summary>
    /// Measures throughput when all 1 000 lines are complete within a single chunk.
    /// Expected allocation: 0 B (no carryover buffer needed).
    /// </summary>
    [Benchmark]
    public void Scan_1000CompleteLinesNoCarry()
    {
        var carry = new PartialLineBuffer();
        Utf8LineScanner.Scan(_chunk1000Lines, ref carry, static _ => { });
    }

    /// <summary>
    /// Measures throughput when the last line is split across two chunks.
    /// The carryover buffer is allocated lazily on the first scan; subsequent scans
    /// of the same-length line reuse it.
    /// </summary>
    [Benchmark]
    public void Scan_1000LinesWithSplitCarry()
    {
        var carry = new PartialLineBuffer();
        Utf8LineScanner.Scan(_splitChunkA, ref carry, static _ => { });
        Utf8LineScanner.Scan(_splitChunkB, ref carry, static _ => { });
    }
}