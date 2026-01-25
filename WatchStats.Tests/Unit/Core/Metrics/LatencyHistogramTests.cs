using WatchStats.Core.Metrics;

namespace WatchStats.Tests.Unit.Core.Metrics;

public class LatencyHistogramTests
{
    [Fact]
    public void EmptyHistogram_PercentilesAreNull()
    {
        var h = new LatencyHistogram();
        Assert.Null(h.Percentile(0.5));
        Assert.Null(h.Percentile(0.95));
        Assert.Null(h.Percentile(0.99));
    }

    [Fact]
    public void SingleValue_AllPercentilesSame()
    {
        var h = new LatencyHistogram();
        h.Add(123);
        Assert.Equal(123, h.Percentile(0.5));
        Assert.Equal(123, h.Percentile(0.95));
        Assert.Equal(123, h.Percentile(1.0));
        Assert.Equal(1, h.Count);
    }

    [Fact]
    public void LinearDistribution_PercentilesMatchExpectedBin()
    {
        var h = new LatencyHistogram();
        // add values 1,2,3,4
        h.Add(1);
        h.Add(2);
        h.Add(3);
        h.Add(4);
        // count=4: p50 -> target=ceil(0.5*4)=2 => 2
        Assert.Equal(2, h.Percentile(0.5));
        // p95 -> target=ceil(0.95*4)=4 => 4
        Assert.Equal(4, h.Percentile(0.95));
    }

    [Fact]
    public void OverflowValues_GoToOverflowBin()
    {
        var h = new LatencyHistogram();
        h.Add(10001);
        h.Add(20000);
        Assert.Equal(10001, h.Percentile(1.0));
    }

    [Fact]
    public void Merge_SumsCountsCorrectly()
    {
        var a = new LatencyHistogram();
        a.Add(1);
        a.Add(2);

        var b = new LatencyHistogram();
        b.Add(3);

        a.MergeFrom(b);
        Assert.Equal(3, a.Count);
        Assert.Equal(1, a.Bins[1]);
        Assert.Equal(1, a.Bins[2]);
        Assert.Equal(1, a.Bins[3]);
    }
}