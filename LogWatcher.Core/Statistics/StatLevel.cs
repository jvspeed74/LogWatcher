namespace LogWatcher.Core.Statistics;

/// <summary>
/// Statistics-internal log-level bucket. Integer values are pinned to match
/// <see cref="LogWatcher.Core.Processing.Parsing.LogLevel"/> so that
/// <see cref="WorkerStatsBuffer.LevelCounts"/> indices remain compatible.
/// </summary>
public enum StatLevel
{
    /// <summary>Informational messages.</summary>
    Info  = 0,
    /// <summary>Warning messages.</summary>
    Warn  = 1,
    /// <summary>Error messages.</summary>
    Error = 2,
    /// <summary>Debug-level messages.</summary>
    Debug = 3,
    /// <summary>Unrecognized or other levels.</summary>
    Other = 4
}
