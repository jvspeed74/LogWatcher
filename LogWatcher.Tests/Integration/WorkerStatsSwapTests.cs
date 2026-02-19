using LogWatcher.Core.Coordination;

namespace LogWatcher.Tests.Integration;

public class WorkerStatsSwapTests
{
    [Fact]
    public void Swap_MovesWrittenDataToInactiveAndResetsActive()
    {
        var ws = new WorkerStats();

        // simulate worker writing
        ws.Active.LinesProcessed = 5;
        ws.Active.FsCreated = 2;
        ws.Active.MessageCounts["x"] = 3;
        ws.Active.Histogram.Add(100);

        // reporter requests swap
        ws.RequestSwap();

        // worker hits safe point and acknowledges
        ws.AcknowledgeSwapIfRequested();

        // reporter waits (should already be set)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        ws.WaitForSwapAck(cts.Token);

        // inactive should contain pre-swap values
        var inactive = ws.GetInactiveBufferForMerge();
        Assert.Equal(5, inactive.LinesProcessed);
        Assert.Equal(2, inactive.FsCreated);
        Assert.Equal(3, inactive.MessageCounts["x"]);
        Assert.Equal(1, inactive.Histogram.Count);

        // active should have been reset
        Assert.Equal(0, ws.Active.LinesProcessed);
        Assert.Empty(ws.Active.MessageCounts);
        Assert.Null(ws.Active.Histogram.Percentile(0.5));
    }

    [Fact]
    public void NoSwapRequest_DoesNothing()
    {
        var ws = new WorkerStats();
        ws.Active.LinesProcessed = 1;
        ws.AcknowledgeSwapIfRequested();
        Assert.Equal(1, ws.Active.LinesProcessed);
        Assert.Equal(ws.Active, ws.Active);
    }

    [Fact]
    public void RequestSwap_ThenAck_SetsAckEvent()
    {
        var ws = new WorkerStats();
        ws.RequestSwap();
        // ack
        ws.AcknowledgeSwapIfRequested();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        ws.WaitForSwapAck(cts.Token); // should not throw
    }

    [Fact]
    public void MultipleSequentialSwaps_WorkCorrectly()
    {
        var ws = new WorkerStats();

        // first interval
        ws.Active.LinesProcessed = 1;
        ws.RequestSwap();
        ws.AcknowledgeSwapIfRequested();
        ws.WaitForSwapAck(CancellationToken.None);
        var i1 = ws.GetInactiveBufferForMerge();
        Assert.Equal(1, i1.LinesProcessed);

        // second interval
        ws.Active.LinesProcessed = 2;
        ws.RequestSwap();
        ws.AcknowledgeSwapIfRequested();
        ws.WaitForSwapAck(CancellationToken.None);
        var i2 = ws.GetInactiveBufferForMerge();
        Assert.Equal(2, i2.LinesProcessed);
    }
}