namespace LogWatcher.Core.Processing.Parsing;

/// <summary>
/// Represents the severity level of a parsed log line.
/// </summary>
public enum LogLevel
{
    /// <summary>Informational messages.</summary>
    Info,
    /// <summary>Warning messages.</summary>
    Warn,
    /// <summary>Error messages.</summary>
    Error,
    /// <summary>Debug-level messages.</summary>
    Debug,
    /// <summary>Unrecognized or other levels.</summary>
    Other
}