using LogWatcher.Core.Statistics;

namespace LogWatcher.Tests.Unit.Core;

public class TopKTests
{
    [Fact]
    public void Empty_ReturnsEmpty()
    {
        var res = TopK.ComputeTopK(new Dictionary<string, int>(), 10);
        Assert.Empty(res);
    }

    [Fact]
    public void KGreaterThanCount_ReturnsAllSorted()
    {
        var d = new Dictionary<string, int>
        {
            ["b"] = 2,
            ["a"] = 3,
            ["c"] = 1
        };

        var res = TopK.ComputeTopK(d, 10);
        Assert.Equal(3, res.Count);
        Assert.Equal(("a", 3), res[0]);
        Assert.Equal(("b", 2), res[1]);
        Assert.Equal(("c", 1), res[2]);
    }

    [Fact]
    public void TieBreak_IsOrdinalAscending()
    {
        var d = new Dictionary<string, int>
        {
            ["b"] = 5,
            ["A"] = 5,
            ["a"] = 5
        };

        var res = TopK.ComputeTopK(d, 10);
        // Ordinal ascending: "A" (0x41) < "a" (0x61) < "b"
        Assert.Equal("A", res[0].Key);
        Assert.Equal("a", res[1].Key);
        Assert.Equal("b", res[2].Key);
    }

    [Fact]
    public void OrdersByCountDescending()
    {
        var d = new Dictionary<string, int>
        {
            ["k1"] = 1,
            ["k2"] = 10,
            ["k3"] = 5
        };

        var res = TopK.ComputeTopK(d, 2);
        Assert.Equal(2, res.Count);
        Assert.Equal(("k2", 10), res[0]);
        Assert.Equal(("k3", 5), res[1]);
    }
}