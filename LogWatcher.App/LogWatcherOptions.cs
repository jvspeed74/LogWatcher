namespace LogWatcher.App;

/// <summary>
/// Validated configuration parameters for the LogWatcher application.
/// </summary>
public sealed record LogWatcherOptions(
    string WatchPath,
    int Workers,
    int QueueCapacity,
    int ReportIntervalSeconds,
    int TopK);