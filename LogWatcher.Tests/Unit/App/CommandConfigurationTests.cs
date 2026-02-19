using LogWatcher.App;

namespace LogWatcher.Tests.Unit.App;

public class CommandConfigurationTests : IDisposable
{
    private readonly string _tmpDir;

    public CommandConfigurationTests()
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
        var command = CommandConfiguration.CreateRootCommand();
        
        var parseResult = command.Parse(args);
        
        Assert.Empty(parseResult.Errors);
    }

    [Fact]
    public void Parse_AllOptions_Works()
    {
        var args = new[] { _tmpDir, "--workers", "3", "-q", "500", "-r", "5", "-k", "7" };
        var command = CommandConfiguration.CreateRootCommand();
        
        var parseResult = command.Parse(args);
        
        Assert.Empty(parseResult.Errors);
    }

    [Fact]
    public void Parse_MissingPath_Fails()
    {
        var args = Array.Empty<string>();
        var command = CommandConfiguration.CreateRootCommand();
        
        var parseResult = command.Parse(args);
        
        Assert.NotEmpty(parseResult.Errors);
    }

    [Fact]
    public void Parse_InvalidNumber_Fails()
    {
        var args = new[] { _tmpDir, "--workers", "notanumber" };
        var command = CommandConfiguration.CreateRootCommand();
        
        var parseResult = command.Parse(args);
        
        Assert.NotEmpty(parseResult.Errors);
    }

    [Fact]
    public void Parse_HelpRequested_ShowsHelp()
    {
        var args = new[] { "--help" };
        var command = CommandConfiguration.CreateRootCommand();
        
        var parseResult = command.Parse(args);
        
        // Help was requested, should have no errors
        Assert.Empty(parseResult.Errors);
    }
}