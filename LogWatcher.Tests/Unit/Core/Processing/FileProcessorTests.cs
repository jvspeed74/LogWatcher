using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Processing;
using LogWatcher.Core.Statistics;

namespace LogWatcher.Tests.Unit.Core.Processing;

public class FileProcessorTests : IDisposable
{
    private readonly string _dir;

    public FileProcessorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "watchstats_fp_" + Guid.NewGuid().ToString("N"));
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
    [Invariant("PRS-001")]
    [Invariant("PRS-002")]
    public void ProcessOnce_WithValidLines_UpdatesLineAndLevelCounts()
    {
        var p = MakePath("p1.log");
        File.WriteAllText(p, "2023-01-02T03:04:05Z INFO key1 latency_ms=10\n2023-01-02T03:04:06Z WARN key2\n");

        var fp = new FileProcessor();
        var reg = new FileStateRegistry();
        var state = reg.GetOrCreate(p);
        lock (state.Gate)
        {
            var stats = new WorkerStatsBuffer();
            fp.ProcessOnce(p, state, stats);
            Assert.Equal(2, stats.LinesProcessed);
            Assert.Equal(0, stats.MalformedLines);
            Assert.Equal(1, stats.MessageCounts["key1"]);
            Assert.Equal(1, stats.MessageCounts["key2"]);
            Assert.Equal(1, stats.Histogram.Count);
        }
    }

    [Fact]
    [Invariant("TAIL-001")]
    public void ProcessOnce_WithAppendedContent_ReadsOnlyNewBytes()
    {
        var p = MakePath("p2.log");
        File.WriteAllText(p, "2023-01-02T03:04:05Z INFO a\n");

        var fp = new FileProcessor();
        var reg = new FileStateRegistry();
        var state = reg.GetOrCreate(p);
        lock (state.Gate)
        {
            var stats = new WorkerStatsBuffer();
            fp.ProcessOnce(p, state, stats);
            Assert.Equal(1, stats.LinesProcessed);
        }

        File.AppendAllText(p, "2023-01-02T03:04:06Z INFO b\n");
        lock (state.Gate)
        {
            var stats2 = new WorkerStatsBuffer();
            fp.ProcessOnce(p, state, stats2);
            Assert.Equal(1, stats2.LinesProcessed);
            Assert.Equal(1, stats2.MessageCounts["b"]);
        }
    }

    [Fact]
    [Invariant("PRS-001")]
    [Invariant("PRS-003")]
    public void ProcessOnce_WithMalformedTimestamp_IncrementsMalformedCount()
    {
        var p = MakePath("p3.log");
        File.WriteAllText(p, "not-a-ts INFO a\n2023-01-02T03:04:05Z INFO b\n");

        var fp = new FileProcessor();
        var reg = new FileStateRegistry();
        var state = reg.GetOrCreate(p);
        lock (state.Gate)
        {
            var stats = new WorkerStatsBuffer();
            fp.ProcessOnce(p, state, stats);
            Assert.Equal(2, stats.LinesProcessed);
            Assert.Equal(1, stats.MalformedLines);
            Assert.Equal(1, stats.MessageCounts["b"]);
        }
    }

    [Fact]
    [Invariant("PRS-001")]
    public void ProcessOnce_WithMissingLatency_DoesNotRecordHistogramEntry()
    {
        var p = MakePath("p4.log");
        File.WriteAllText(p, "2023-01-02T03:04:05Z INFO no_latency\n");

        var fp = new FileProcessor();
        var reg = new FileStateRegistry();
        var state = reg.GetOrCreate(p);
        lock (state.Gate)
        {
            var stats = new WorkerStatsBuffer();
            fp.ProcessOnce(p, state, stats);
            Assert.Equal(1, stats.LinesProcessed);
            Assert.Equal(0, stats.Histogram.Count);
        }
    }

    [Fact]
    [Invariant("SCAN-001")]
    [Invariant("SCAN-004")]
    public void ProcessOnce_WithLineSplitAcrossChunks_EmitsLineCorrectly()
    {
        var p = MakePath("p5.log");
        // Make a long line > small chunk size (use 32 bytes chunk)
        var longLine = "2023-01-02T03:04:05Z INFO " + new string('x', 200) + "\n";
        File.WriteAllText(p, longLine);

        var fp = new FileProcessor();
        var reg = new FileStateRegistry();
        var state = reg.GetOrCreate(p);
        lock (state.Gate)
        {
            var stats = new WorkerStatsBuffer();
            // force small chunk size to cause carryover
            fp.ProcessOnce(p, state, stats, 32);
            Assert.Equal(1, stats.LinesProcessed);
            Assert.Single(stats.MessageCounts);
        }
    }
}