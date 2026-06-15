using System.Text;

using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Processing;
using LogWatcher.Core.Processing.Parsing;
using LogWatcher.Core.Processing.Scanning;
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

    [Fact]
    public void ProcessOnce_WithLogger_DoesNotThrow()
    {
        var p = MakePath("log_with_logger.log");
        File.WriteAllText(p, "2023-01-02T03:04:05Z INFO request latency_ms=5\n");

        var fp = new FileProcessor(logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<FileProcessor>.Instance);
        var state = new FileState();

        lock (state.Gate)
        {
            var stats = new WorkerStatsBuffer();
            fp.ProcessOnce(p, state, stats);
            Assert.Equal(1, stats.LinesProcessed);
        }
    }

    [Theory]
    [InlineData("INFO", StatLevel.Info)]
    [InlineData("WARN", StatLevel.Warn)]
    [InlineData("ERROR", StatLevel.Error)]
    [InlineData("DEBUG", StatLevel.Debug)]
    [InlineData("OTHER", StatLevel.Other)]
    public void ProcessOnce_EachLogLevelToken_IncrementsCorrectStatLevelBucket(string token, StatLevel expected)
    {
        var p = MakePath($"level_{token}.log");
        File.WriteAllText(p, $"2023-01-02T03:04:05Z {token} key\n");

        var fp = new FileProcessor();
        var reg = new FileStateRegistry();
        var state = reg.GetOrCreate(p);
        lock (state.Gate)
        {
            var stats = new WorkerStatsBuffer();
            fp.ProcessOnce(p, state, stats);
            Assert.Equal(1, stats.LevelCounts[(int)expected]);
            // All other buckets must remain zero
            for (var i = 0; i < stats.LevelCounts.Length; i++)
                if (i != (int)expected)
                    Assert.Equal(0, stats.LevelCounts[i]);
        }
    }

    [Fact]
    [Invariant("PROC-008")]
    public void ScanAndParse_AllocatesNoHeapObjectsPerLine()
    {
        const string line = "2023-01-02T03:04:05Z INFO key latency_ms=123\n";
        var builder = new StringBuilder();
        for (var i = 0; i < 512; i++)
            builder.Append(line);

        var batch = Encoding.UTF8.GetBytes(builder.ToString());
        var carry = new PartialLineBuffer();
        Action<ReadOnlySpan<byte>> onLine = static lineSpan =>
        {
            if (!LogParser.TryParse(lineSpan, out _))
                throw new InvalidOperationException("Parse failed");
        };

        Utf8LineScanner.Scan(batch, ref carry, onLine);
        carry.Clear();

        long before = GC.GetAllocatedBytesForCurrentThread();
        Utf8LineScanner.Scan(batch, ref carry, onLine);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, after - before);
    }
}