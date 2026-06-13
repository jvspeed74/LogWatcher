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

    [Fact]
    [Invariant("STAT-001")]
    public void LevelCounts_IndexedByStatLevelEnumValue_OutOfRangeIndexIsIgnored()
    {
        var buf = new WorkerStatsBuffer();

        buf.IncrementLevel(StatLevel.Info);
        buf.IncrementLevel(StatLevel.Warn);
        buf.IncrementLevel(StatLevel.Error);
        buf.IncrementLevel(StatLevel.Debug);

        // Verify counts are indexed by the integer value of each StatLevel
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Info]);
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Warn]);
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Error]);
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Debug]);

        // An unrecognized (out-of-range) index must be silently ignored — no exception
        var ex = Record.Exception(() =>
        {
            buf.IncrementLevel((StatLevel)9999);
            buf.IncrementLevel((StatLevel)(-1));
        });
        Assert.Null(ex);
    }

    [Fact]
    [Invariant("STAT-001")]
    public void IncrementFsEvent_WithOutOfRangeKind_DoesNotThrow()
    {
        var buf = new WorkerStatsBuffer();
        var ex = Record.Exception(() =>
        {
            buf.IncrementFsEvent((StatEventKind)9999);
            buf.IncrementFsEvent((StatEventKind)(-1));
        });
        Assert.Null(ex);
    }

    [Fact]
    public void IncrementLevel_EachStatLevelVariant_IncrementsCorrectBucket()
    {
        var buf = new WorkerStatsBuffer();
        buf.IncrementLevel(StatLevel.Info);
        buf.IncrementLevel(StatLevel.Warn);
        buf.IncrementLevel(StatLevel.Error);
        buf.IncrementLevel(StatLevel.Debug);
        buf.IncrementLevel(StatLevel.Other);

        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Info]);
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Warn]);
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Error]);
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Debug]);
        Assert.Equal(1, buf.LevelCounts[(int)StatLevel.Other]);
    }

    [Fact]
    public void IncrementFsEvent_EachStatEventKindVariant_IncrementsCorrectCounter()
    {
        var buf = new WorkerStatsBuffer();
        buf.IncrementFsEvent(StatEventKind.Created);
        buf.IncrementFsEvent(StatEventKind.Modified);
        buf.IncrementFsEvent(StatEventKind.Deleted);
        buf.IncrementFsEvent(StatEventKind.Renamed);

        Assert.Equal(1, buf.FsCreated);
        Assert.Equal(1, buf.FsModified);
        Assert.Equal(1, buf.FsDeleted);
        Assert.Equal(1, buf.FsRenamed);
    }
}