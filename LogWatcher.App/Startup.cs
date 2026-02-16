namespace LogWatcher.App;

/// <summary>
/// Result of startup initialization indicating whether the application should exit and with what code.
/// </summary>
public sealed record StartupResult(bool ShouldExit, int ExitCode, string? Message, CliConfig? Config);

/// <summary>
/// Handles application startup logic including argument parsing and initial I/O.
/// </summary>
public static class Startup
{
    /// <summary>
    /// Initializes the application by parsing arguments and handling help/error cases.
    /// Returns a <see cref="StartupResult"/> indicating success or exit conditions.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A <see cref="StartupResult"/> with exit status and optional config.</returns>
    public static StartupResult Initialize(string[] args)
    {
        var parseResult = CliParser.Parse(args);

        if (parseResult.IsSuccess)
        {
            return new StartupResult(false, 0, null, parseResult.Config);
        }

        if (parseResult.IsHelp)
        {
            return new StartupResult(true, 0, CliParser.UsageText, null);
        }

        // Parse error
        var errorMessage = parseResult.Error?.Message ?? "unknown error";
        return new StartupResult(true, 2, $"Invalid arguments: {errorMessage}", null);
    }
}
