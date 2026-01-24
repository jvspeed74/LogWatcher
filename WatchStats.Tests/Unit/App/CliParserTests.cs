using WatchStats.App;

namespace WatchStats.Tests.Unit.App;

public class CliParserTests : IDisposable
{
    private readonly string _tmpDir;

    public CliParserTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "watchstats_test_" + Guid.NewGuid().ToString("N"));
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
    public void Parse_DefaultsAndPositional_Works()
    {
        var args = new[] { _tmpDir };
        Assert.True(CliParser.TryParse(args, out var cfg, out var err));
        Assert.Null(err);
        Assert.NotNull(cfg);
        Assert.Equal(Path.GetFullPath(_tmpDir), cfg!.WatchPath);
        Assert.True(cfg.Workers >= 1);
        Assert.Equal(10000, cfg.QueueCapacity);
        Assert.Equal(2, cfg.ReportIntervalSeconds);
        Assert.Equal(10, cfg.TopK);
    }

    [Fact]
    public void Parse_AllOptions_Works()
    {
        var args = new[]
            { _tmpDir, "--workers", "3", "--queue-capacity=500", "--report-interval-seconds", "5", "--topk", "7" };
        Assert.True(CliParser.TryParse(args, out var cfg, out var err));
        Assert.Null(err);
        Assert.NotNull(cfg);
        Assert.Equal(3, cfg!.Workers);
        Assert.Equal(500, cfg.QueueCapacity);
        Assert.Equal(5, cfg.ReportIntervalSeconds);
        Assert.Equal(7, cfg.TopK);
    }

    [Fact]
    public void Parse_MissingPath_Fails()
    {
        var args = Array.Empty<string>();
        Assert.False(CliParser.TryParse(args, out var cfg, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Parse_InvalidNumber_Fails()
    {
        var args = new[] { _tmpDir, "--workers", "notanumber" };
        Assert.False(CliParser.TryParse(args, out var cfg, out var err));
        Assert.NotNull(err);
    }
}