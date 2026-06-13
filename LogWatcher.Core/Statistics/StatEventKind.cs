namespace LogWatcher.Core.Statistics;

/// <summary>
/// Statistics-internal filesystem-event bucket. Integer values are pinned to match
/// <see cref="LogWatcher.Core.Ingestion.FsEventKind"/> so that per-kind counters
/// in <see cref="WorkerStatsBuffer"/> remain index-compatible.
/// </summary>
public enum StatEventKind
{
    /// <summary>File created.</summary>
    Created  = 0,
    /// <summary>File modified.</summary>
    Modified = 1,
    /// <summary>File deleted.</summary>
    Deleted  = 2,
    /// <summary>File renamed.</summary>
    Renamed  = 3
}
