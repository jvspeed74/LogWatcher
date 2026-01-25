using Microsoft.Extensions.Logging;
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
        var cfg = new CliConfig(_tmpDir, 2, 1000, 3000, 5, LogLevel.Information, false, true);
        Assert.Equal(Path.GetFullPath(_tmpDir), cfg.WatchPath);
        Assert.Equal(2, cfg.Workers);
        Assert.Equal(1000, cfg.BusCapacity);
        Assert.Equal(3000, cfg.IntervalMs);
        Assert.Equal(5, cfg.TopK);
        Assert.Equal(LogLevel.Information, cfg.LogLevel);
        Assert.False(cfg.JsonLogs);
        Assert.True(cfg.EnableMetricsLogs);
    }

    [Fact]
    public void Constructor_InvalidPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CliConfig("doesnotexist", 1, 1000, 500, 1, LogLevel.Information, false, true));
    }

    [Fact]
    public void Constructor_ClampsWorkers()
    {
        var cfg = new CliConfig(_tmpDir, 0, 1000, 500, 1, LogLevel.Information, false, true);
        Assert.Equal(1, cfg.Workers); // Clamped to minimum 1
        
        var cfg2 = new CliConfig(_tmpDir, 100, 1000, 500, 1, LogLevel.Information, false, true);
        Assert.Equal(64, cfg2.Workers); // Clamped to maximum 64
    }
    
    [Fact]
    public void Constructor_ClampsBusCapacity()
    {
        var cfg = new CliConfig(_tmpDir, 1, 100, 500, 1, LogLevel.Information, false, true);
        Assert.Equal(1000, cfg.BusCapacity); // Clamped to minimum 1000
        
        var cfg2 = new CliConfig(_tmpDir, 1, 2_000_000, 500, 1, LogLevel.Information, false, true);
        Assert.Equal(1_000_000, cfg2.BusCapacity); // Clamped to maximum 1_000_000
    }
    
    [Fact]
    public void Constructor_ClampsIntervalMs()
    {
        var cfg = new CliConfig(_tmpDir, 1, 1000, 100, 1, LogLevel.Information, false, true);
        Assert.Equal(500, cfg.IntervalMs); // Clamped to minimum 500
        
        var cfg2 = new CliConfig(_tmpDir, 1, 1000, 100_000, 1, LogLevel.Information, false, true);
        Assert.Equal(60_000, cfg2.IntervalMs); // Clamped to maximum 60_000
    }

    [Fact]
    public void Constructor_InvalidTopK_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CliConfig(_tmpDir, 1, 1000, 500, 0, LogLevel.Information, false, true));
    }
}