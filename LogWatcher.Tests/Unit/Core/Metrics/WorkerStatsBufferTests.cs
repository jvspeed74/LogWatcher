using LogWatcher.Core.Metrics;

namespace LogWatcher.Tests.Unit.Core.Metrics;

public class WorkerStatsBufferTests
{
    [Fact]
    public void Reset_ClearsScalarsArraysAndCollections()
    {
        var b = new WorkerStatsBuffer();
        b.FsCreated = 1;
        b.LinesProcessed = 10;
        b.MalformedLines = 2;
        b.LevelCounts[0] = 5;
        b.MessageCounts["x"] = 3;
        b.Histogram.Add(100);

        b.Reset();

        Assert.Equal(0, b.FsCreated);
        Assert.Equal(0, b.LinesProcessed);
        Assert.Equal(0, b.MalformedLines);
        Assert.Equal(0, b.LevelCounts[0]);
        Assert.Empty(b.MessageCounts);
        Assert.Null(b.Histogram.Percentile(0.5));
    }

    [Fact]
    public void MessageCounts_AccumulatesCorrectly()
    {
        var b = new WorkerStatsBuffer();
        b.IncrementMessage("a");
        b.IncrementMessage("a");
        b.IncrementMessage("b");

        Assert.Equal(2, b.MessageCounts["a"]);
        Assert.Equal(1, b.MessageCounts["b"]);
    }

    [Fact]
    public void Histogram_AccumulatesAndResets()
    {
        var b = new WorkerStatsBuffer();
        b.RecordLatency(10);
        b.RecordLatency(20);
        b.RecordLatency(20);

        Assert.Equal(3, b.Histogram.Count);
        Assert.Equal(20, b.Histogram.Percentile(1.0));

        b.Reset();
        Assert.Equal(0, b.Histogram.Count);
        Assert.Null(b.Histogram.Percentile(0.5));
    }
}