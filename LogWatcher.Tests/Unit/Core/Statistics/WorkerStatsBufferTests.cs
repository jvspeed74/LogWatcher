using LogWatcher.Core.Statistics;

namespace LogWatcher.Tests.Unit.Core.Statistics;

public class WorkerStatsBufferTests
{
    [Fact]
    [Invariant("STAT-004")]
    public void Reset_WhenCalled_ClearsAllFieldsToZero()
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

    // TODO: map to invariant
    [Fact]
    public void IncrementMessage_CalledMultipleTimes_AccumulatesCountsCorrectly()
    {
        var b = new WorkerStatsBuffer();
        b.IncrementMessage("a");
        b.IncrementMessage("a");
        b.IncrementMessage("b");

        Assert.Equal(2, b.MessageCounts["a"]);
        Assert.Equal(1, b.MessageCounts["b"]);
    }

    [Fact]
    [Invariant("STAT-002")]
    [Invariant("STAT-004")]
    public void Histogram_RecordThenReset_AccumulatesAndClears()
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