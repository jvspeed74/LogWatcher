using LogWatcher.Core.Reporting;
using LogWatcher.Core.Statistics;

namespace LogWatcher.Tests.Unit.Core.Reporting;

public class GlobalSnapshotTests
{
    [Fact]
    [Invariant("RPT-002")]
    public void MergeFrom_WithWorkerBuffer_SumsAllMetrics()
    {
        var snap = new GlobalSnapshot(3);
        var buf = new WorkerStatsBuffer();
        buf.FsCreated = 2;
        buf.LinesProcessed = 5;
        buf.MessageCounts["a"] = 2;
        buf.MessageCounts["b"] = 1;
        buf.Histogram.Add(10);
        buf.Histogram.Add(20);
        buf.Histogram.Add(20);

        snap.MergeFrom(buf);

        Assert.Equal(2, snap.FsCreated);
        Assert.Equal(5, snap.LinesProcessed);
        Assert.Equal(2, snap.MessageCounts["a"]);
        Assert.Equal(1, snap.MessageCounts["b"]);
        Assert.Equal(3, snap.Histogram.Count);
    }

    [Fact]
    [Invariant("STAT-003")]
    public void FinalizeSnapshot_WithMessagesAndLatencies_ComputesTopKAndPercentiles()
    {
        var snap = new GlobalSnapshot(2);
        snap.MessageCounts["x"] = 5;
        snap.MessageCounts["y"] = 3;
        snap.MessageCounts["z"] = 1;

        snap.Histogram.Add(10);
        snap.Histogram.Add(20);
        snap.Histogram.Add(30);
        snap.Histogram.Add(40);

        snap.FinalizeSnapshot(2);

        Assert.Equal(2, snap.TopKMessages.Count);
        Assert.Equal("x", snap.TopKMessages[0].Key);
        Assert.Equal("y", snap.TopKMessages[1].Key);

        Assert.Equal(20, snap.P50);
        Assert.Equal(40, snap.P95);
        Assert.Equal(40, snap.P99);
    }
}