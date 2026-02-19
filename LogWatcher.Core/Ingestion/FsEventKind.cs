namespace LogWatcher.Core.Ingestion;

/// <summary>
/// Filesystem event kinds used by the watcher and processing pipeline.
/// </summary>
public enum FsEventKind
{
    /// <summary>File created.</summary>
    Created,
    /// <summary>File modified.</summary>
    Modified,
    /// <summary>File deleted.</summary>
    Deleted,
    /// <summary>File renamed.</summary>
    Renamed
}