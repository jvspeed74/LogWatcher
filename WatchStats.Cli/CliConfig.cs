namespace WatchStats.Cli;

/// <summary>
/// Validated application configuration populated from the CLI.
/// </summary>
public sealed class CliConfig
{
    /// <summary>Directory path to watch (absolute path returned from constructor).</summary>
    public string WatchPath { get; }
    /// <summary>Number of worker threads to use.</summary>
    public int Workers { get; }
    /// <summary>Capacity of the filesystem event queue.</summary>
    public int QueueCapacity { get; }
    /// <summary>Report interval in seconds.</summary>
    public int ReportIntervalSeconds { get; }
    /// <summary>Top-K value for reporting.</summary>
    public int TopK { get; }

    /// <summary>
    /// Creates and validates an <see cref="CliConfig"/> instance. Throws <see cref="ArgumentException"/> or <see cref="ArgumentOutOfRangeException"/>
    /// for invalid inputs.
    /// </summary>
    /// <param name="watchPath">Directory to watch; must exist.</param>
    /// <param name="workers">Number of worker threads; must be >= 1.</param>
    /// <param name="queueCapacity">Event queue capacity; must be >= 1.</param>
    /// <param name="reportIntervalSeconds">Reporting interval in seconds; must be >= 1.</param>
    /// <param name="topK">Top-K count for reporting; must be >= 1.</param>
    public CliConfig(string watchPath, int workers, int queueCapacity, int reportIntervalSeconds, int topK)
    {
        if (string.IsNullOrWhiteSpace(watchPath))
            throw new ArgumentException("watchPath is required", nameof(watchPath));
        if (!Directory.Exists(watchPath))
            throw new ArgumentException($"watchPath does not exist: {watchPath}", nameof(watchPath));
        if (workers < 1) throw new ArgumentOutOfRangeException(nameof(workers), "workers must be >= 1");
        if (queueCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), "queueCapacity must be >= 1");
        if (reportIntervalSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(reportIntervalSeconds),
                "reportIntervalSeconds must be >= 1");
        if (topK < 1) throw new ArgumentOutOfRangeException(nameof(topK), "topK must be >= 1");

        WatchPath = Path.GetFullPath(watchPath);
        Workers = workers;
        QueueCapacity = queueCapacity;
        ReportIntervalSeconds = reportIntervalSeconds;
        TopK = topK;
    }

    /// <summary>
    /// Returns a concise string representation of this configuration suitable for logging.
    /// </summary>
    public override string ToString()
    {
        return string.Format("WatchPath={0}; Workers={1}; QueueCapacity={2}; ReportIntervalSeconds={3}; TopK={4}",
            WatchPath, Workers, QueueCapacity, ReportIntervalSeconds, TopK);
    }
}