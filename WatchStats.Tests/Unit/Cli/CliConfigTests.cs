using WatchStats.Cli;

namespace WatchStats.Tests.Unit.Cli;

public class CliConfigTests : IDisposable
{
    private readonly string _tmpDir;

    public CliConfigTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "watchstats_config_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Constructor_Valid_SetsValues()
    {
        var cfg = new CliConfig(_tmpDir, 2, 1000, 3, 5);
        Assert.Equal(Path.GetFullPath(_tmpDir), cfg.WatchPath);
        Assert.Equal(2, cfg.Workers);
        Assert.Equal(1000, cfg.QueueCapacity);
        Assert.Equal(3, cfg.ReportIntervalSeconds);
        Assert.Equal(5, cfg.TopK);
    }

    [Fact]
    public void Constructor_InvalidPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CliConfig("doesnotexist", 1, 1, 1, 1));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    public void Constructor_InvalidNumbers_Throws(int workers, int queueCap, int reportSecs, int topk)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CliConfig(_tmpDir, workers, queueCap, reportSecs, topk));
    }
}