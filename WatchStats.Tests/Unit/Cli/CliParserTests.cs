using Microsoft.Extensions.Logging;
using WatchStats.Cli;

namespace WatchStats.Tests.Unit.Cli;

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
    public void Parse_DirectoryArg_Works()
    {
        var args = new[] { "--dir", _tmpDir };
        var result = CliParser.TryParse(args, out var cfg, out var err);
        if (!result)
        {
            throw new Exception($"Parse failed with error: {err}");
        }
        Assert.True(result);
        Assert.Null(err);
        Assert.NotNull(cfg);
        Assert.Equal(Path.GetFullPath(_tmpDir), cfg!.WatchPath);
        Assert.True(cfg.Workers >= 1);
        Assert.Equal(10000, cfg.BusCapacity);
        Assert.Equal(2000, cfg.IntervalMs);
        Assert.Equal(10, cfg.TopK);
        Assert.Equal(LogLevel.Information, cfg.LogLevel);
        Assert.False(cfg.JsonLogs);
        Assert.True(cfg.EnableMetricsLogs);
    }

    [Fact]
    public void Parse_AllOptions_Works()
    {
        var args = new[]
            { "--dir", _tmpDir, "--workers", "3", "--capacity=500", "--interval", "5000", "--topk", "7", "--logLevel", "Debug", "--json-logs", "--no-metrics-logs" };
        Assert.True(CliParser.TryParse(args, out var cfg, out var err));
        Assert.Null(err);
        Assert.NotNull(cfg);
        Assert.Equal(3, cfg!.Workers);
        Assert.Equal(1000, cfg.BusCapacity); // Clamped to minimum 1000
        Assert.Equal(5000, cfg.IntervalMs);
        Assert.Equal(7, cfg.TopK);
        Assert.Equal(LogLevel.Debug, cfg.LogLevel);
        Assert.True(cfg.JsonLogs);
        Assert.False(cfg.EnableMetricsLogs);
    }

    [Fact]
    public void Parse_MissingPath_Fails()
    {
        var args = Array.Empty<string>();
        Assert.False(CliParser.TryParse(args, out var cfg, out var err));
        Assert.NotNull(err);
        Assert.Contains("--dir", err);
    }

    [Fact]
    public void Parse_InvalidNumber_Fails()
    {
        var args = new[] { "--dir", _tmpDir, "--workers", "notanumber" };
        Assert.False(CliParser.TryParse(args, out var cfg, out var err));
        Assert.NotNull(err);
    }
    
    [Fact]
    public void Parse_Help_ReturnsHelpError()
    {
        var args = new[] { "--help" };
        Assert.False(CliParser.TryParse(args, out var cfg, out var err));
        Assert.Equal("help", err);
    }
}