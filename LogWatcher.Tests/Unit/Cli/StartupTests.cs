using LogWatcher.App;

namespace LogWatcher.Tests.Unit.Cli;

/// <summary>
/// Tests for Startup.Initialize() method.
/// </summary>
public class StartupTests : IDisposable
{
    private readonly string _tmpDir;

    public StartupTests()
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
    public void Initialize_ValidArguments_ReturnsSuccess()
    {
        var args = new[] { _tmpDir };
        var result = Startup.Initialize(args);
        
        Assert.False(result.ShouldExit);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Message);
        Assert.NotNull(result.Config);
        Assert.Equal(Path.GetFullPath(_tmpDir), result.Config!.WatchPath);
    }

    [Fact]
    public void Initialize_HelpFlag_ReturnsExitWithUsageMessage()
    {
        var args = new[] { "--help" };
        var result = Startup.Initialize(args);
        
        Assert.True(result.ShouldExit);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Message);
        Assert.Contains("Usage:", result.Message);
        Assert.Null(result.Config);
    }

    [Fact]
    public void Initialize_ShortHelpFlag_ReturnsExitWithUsageMessage()
    {
        var args = new[] { "-h" };
        var result = Startup.Initialize(args);
        
        Assert.True(result.ShouldExit);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Message);
        Assert.Contains("Usage:", result.Message);
        Assert.Null(result.Config);
    }

    [Fact]
    public void Initialize_MissingPath_ReturnsExitWithError()
    {
        var args = Array.Empty<string>();
        var result = Startup.Initialize(args);
        
        Assert.True(result.ShouldExit);
        Assert.Equal(2, result.ExitCode);
        Assert.NotNull(result.Message);
        Assert.Contains("Invalid arguments", result.Message);
        Assert.Contains("missing watchPath", result.Message);
        Assert.Null(result.Config);
    }

    [Fact]
    public void Initialize_UnknownOption_ReturnsExitWithError()
    {
        var args = new[] { _tmpDir, "--invalid-option" };
        var result = Startup.Initialize(args);
        
        Assert.True(result.ShouldExit);
        Assert.Equal(2, result.ExitCode);
        Assert.NotNull(result.Message);
        Assert.Contains("Invalid arguments", result.Message);
        Assert.Contains("unknown option", result.Message);
        Assert.Null(result.Config);
    }

    [Fact]
    public void Initialize_InvalidNumericValue_ReturnsExitWithError()
    {
        var args = new[] { _tmpDir, "--workers", "notanumber" };
        var result = Startup.Initialize(args);
        
        Assert.True(result.ShouldExit);
        Assert.Equal(2, result.ExitCode);
        Assert.NotNull(result.Message);
        Assert.Contains("Invalid arguments", result.Message);
        Assert.Contains("invalid --workers value", result.Message);
        Assert.Null(result.Config);
    }

    [Fact]
    public void Initialize_NonExistentPath_ReturnsExitWithError()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N"));
        var args = new[] { nonExistentPath };
        var result = Startup.Initialize(args);
        
        Assert.True(result.ShouldExit);
        Assert.Equal(2, result.ExitCode);
        Assert.NotNull(result.Message);
        Assert.Contains("Invalid arguments", result.Message);
        Assert.Contains("does not exist", result.Message);
        Assert.Null(result.Config);
    }

    [Fact]
    public void Initialize_AllOptions_ReturnsSuccessWithConfig()
    {
        var args = new[] { _tmpDir, "--workers", "3", "--queue-capacity", "500", "--report-interval-seconds", "5", "--topk", "7" };
        var result = Startup.Initialize(args);
        
        Assert.False(result.ShouldExit);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Message);
        Assert.NotNull(result.Config);
        Assert.Equal(3, result.Config!.Workers);
        Assert.Equal(500, result.Config.QueueCapacity);
        Assert.Equal(5, result.Config.ReportIntervalSeconds);
        Assert.Equal(7, result.Config.TopK);
    }
}
