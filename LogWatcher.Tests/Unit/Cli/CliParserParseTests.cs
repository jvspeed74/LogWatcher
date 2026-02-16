using LogWatcher.App;

namespace LogWatcher.Tests.Unit.Cli;

/// <summary>
/// Tests for the new structured CliParser.Parse() method.
/// </summary>
public class CliParserParseTests : IDisposable
{
    private readonly string _tmpDir;

    public CliParserParseTests()
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
    public void Parse_ValidArguments_ReturnsSuccess()
    {
        var args = new[] { _tmpDir };
        var result = CliParser.Parse(args);
        
        Assert.True(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.NotNull(result.Config);
        Assert.Null(result.Error);
        Assert.Equal(Path.GetFullPath(_tmpDir), result.Config!.WatchPath);
    }

    [Fact]
    public void Parse_HelpFlag_ReturnsHelp()
    {
        var args = new[] { "--help" };
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.True(result.IsHelp);
        Assert.Null(result.Config);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Parse_ShortHelpFlag_ReturnsHelp()
    {
        var args = new[] { "-h" };
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.True(result.IsHelp);
        Assert.Null(result.Config);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Parse_MissingPath_ReturnsError()
    {
        var args = Array.Empty<string>();
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
        Assert.Equal(ParseErrorKind.MissingPath, result.Error!.Kind);
        Assert.Equal("missing watchPath", result.Error.Message);
    }

    [Fact]
    public void Parse_UnknownOption_ReturnsError()
    {
        var args = new[] { _tmpDir, "--invalid-option" };
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
        Assert.Equal(ParseErrorKind.UnknownOption, result.Error!.Kind);
        Assert.Contains("unknown option", result.Error.Message);
    }

    [Fact]
    public void Parse_MissingOptionValue_ReturnsError()
    {
        var args = new[] { _tmpDir, "--workers" };
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
        Assert.Equal(ParseErrorKind.MissingValue, result.Error!.Kind);
        Assert.Contains("--workers requires a value", result.Error.Message);
    }

    [Fact]
    public void Parse_InvalidNumericValue_ReturnsError()
    {
        var args = new[] { _tmpDir, "--workers", "notanumber" };
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
        Assert.Equal(ParseErrorKind.InvalidValue, result.Error!.Kind);
        Assert.Contains("invalid --workers value", result.Error.Message);
    }

    [Fact]
    public void Parse_MultiplePositionalArguments_ReturnsError()
    {
        var args = new[] { _tmpDir, "extra-arg" };
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
        Assert.Equal(ParseErrorKind.UnexpectedArgument, result.Error!.Kind);
        Assert.Equal("unexpected positional argument", result.Error.Message);
    }

    [Fact]
    public void Parse_NonExistentPath_ReturnsValidationError()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N"));
        var args = new[] { nonExistentPath };
        var result = CliParser.Parse(args);
        
        Assert.False(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.Null(result.Config);
        Assert.NotNull(result.Error);
        Assert.Equal(ParseErrorKind.ValidationError, result.Error!.Kind);
        Assert.Contains("does not exist", result.Error.Message);
    }

    [Fact]
    public void Parse_AllOptionsWithEquals_ReturnsSuccess()
    {
        var args = new[] { _tmpDir, "--workers=3", "--queue-capacity=500", "--report-interval-seconds=5", "--topk=7" };
        var result = CliParser.Parse(args);
        
        Assert.True(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.NotNull(result.Config);
        Assert.Null(result.Error);
        Assert.Equal(3, result.Config!.Workers);
        Assert.Equal(500, result.Config.QueueCapacity);
        Assert.Equal(5, result.Config.ReportIntervalSeconds);
        Assert.Equal(7, result.Config.TopK);
    }

    [Fact]
    public void Parse_AllOptionsWithSpace_ReturnsSuccess()
    {
        var args = new[] { _tmpDir, "--workers", "3", "--queue-capacity", "500", "--report-interval-seconds", "5", "--topk", "7" };
        var result = CliParser.Parse(args);
        
        Assert.True(result.IsSuccess);
        Assert.False(result.IsHelp);
        Assert.NotNull(result.Config);
        Assert.Null(result.Error);
        Assert.Equal(3, result.Config!.Workers);
        Assert.Equal(500, result.Config.QueueCapacity);
        Assert.Equal(5, result.Config.ReportIntervalSeconds);
        Assert.Equal(7, result.Config.TopK);
    }
}
