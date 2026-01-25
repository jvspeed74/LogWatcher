using Microsoft.Extensions.Logging;

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
    public int BusCapacity { get; }
    /// <summary>Report interval in milliseconds.</summary>
    public int IntervalMs { get; }
    /// <summary>Top-K value for reporting.</summary>
    public int TopK { get; }
    /// <summary>Minimum log level.</summary>
    public LogLevel LogLevel { get; }
    /// <summary>Enable JSON log output.</summary>
    public bool JsonLogs { get; }
    /// <summary>Enable periodic metrics logging.</summary>
    public bool EnableMetricsLogs { get; }

    /// <summary>
    /// Creates and validates an <see cref="CliConfig"/> instance. Throws <see cref="ArgumentException"/> or <see cref="ArgumentOutOfRangeException"/>
    /// for invalid inputs.
    /// </summary>
    /// <param name="watchPath">Directory to watch; must exist.</param>
    /// <param name="workers">Number of worker threads; clamped to [1, 64].</param>
    /// <param name="busCapacity">Event queue capacity; clamped to [1000, 1000000].</param>
    /// <param name="intervalMs">Reporting interval in milliseconds; clamped to [500, 60000].</param>
    /// <param name="topK">Top-K count for reporting; must be >= 1.</param>
    /// <param name="logLevel">Minimum log level.</param>
    /// <param name="jsonLogs">Enable JSON log output.</param>
    /// <param name="enableMetricsLogs">Enable periodic metrics logging.</param>
    public CliConfig(string watchPath, int workers, int busCapacity, int intervalMs, int topK,
        LogLevel logLevel, bool jsonLogs, bool enableMetricsLogs)
    {
        if (string.IsNullOrWhiteSpace(watchPath))
            throw new ArgumentException("watchPath is required", nameof(watchPath));
        if (!Directory.Exists(watchPath))
            throw new ArgumentException($"watchPath does not exist: {watchPath}", nameof(watchPath));
        
        // Clamp values to valid ranges
        workers = Math.Clamp(workers, 1, 64);
        busCapacity = Math.Clamp(busCapacity, 1000, 1_000_000);
        intervalMs = Math.Clamp(intervalMs, 500, 60_000);
        
        if (topK < 1) throw new ArgumentOutOfRangeException(nameof(topK), "topK must be >= 1");

        WatchPath = Path.GetFullPath(watchPath);
        Workers = workers;
        BusCapacity = busCapacity;
        IntervalMs = intervalMs;
        TopK = topK;
        LogLevel = logLevel;
        JsonLogs = jsonLogs;
        EnableMetricsLogs = enableMetricsLogs;
    }

    /// <summary>
    /// Returns a concise string representation of this configuration suitable for logging.
    /// </summary>
    public override string ToString()
    {
        return string.Format("WatchPath={0}; Workers={1}; BusCapacity={2}; IntervalMs={3}; TopK={4}; LogLevel={5}; JsonLogs={6}; EnableMetricsLogs={7}",
            WatchPath, Workers, BusCapacity, IntervalMs, TopK, LogLevel, JsonLogs, EnableMetricsLogs);
    }
}