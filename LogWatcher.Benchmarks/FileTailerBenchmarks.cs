using BenchmarkDotNet.Attributes;

using LogWatcher.Core.Processing.Tailing;

namespace LogWatcher.Benchmarks;

[MemoryDiagnoser]
public class FileTailerBenchmarks
{
    private FileTailer _tailer = null!;
    private string _filePath = string.Empty;
    private long _offset1Mb;

    [GlobalSetup]
    public void Setup()
    {
        _tailer = new FileTailer();

        _filePath = Path.Combine(Path.GetTempPath(), $"logwatcher-bench-{Guid.NewGuid()}.log");
        var line = "2024-01-01T00:00:00.0000000Z INFO key message text here\n"u8.ToArray();

        // Pre-seed file with just over 1 MB of log data.
        using var fs = File.OpenWrite(_filePath);
        var targetBytes = 1024 * 1024;
        while (fs.Length < targetBytes)
            fs.Write(line);

        _offset1Mb = fs.Length; // used by NoNewData benchmark
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_filePath);

    /// <summary>
    /// Reads ~1 MB of new data from the file. Measures read throughput and verifies
    /// that only ArrayPool buffers are rented (no per-chunk heap allocation).
    /// </summary>
    [Benchmark]
    public void ReadAppended_1MB_NewData()
    {
        long offset = 0;
        _tailer.ReadAppended(_filePath, ref offset, static _ => { }, out _);
    }

    /// <summary>
    /// Offset is already at EOF — no data to read.
    /// Expected: 0 bytes allocated (no buffer is rented when there is no new data).
    /// </summary>
    [Benchmark]
    public TailReadStatus ReadAppended_NoNewData()
    {
        var offset = _offset1Mb;
        return _tailer.ReadAppended(_filePath, ref offset, static _ => { }, out _);
    }
}