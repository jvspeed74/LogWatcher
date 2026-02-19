namespace LogWatcher.Core.Ingestion
{
    /// <summary>
    /// Represents a filesystem event observed by the watcher.
    /// </summary>
    /// <param name="Kind">The kind of filesystem event.</param>
    /// <param name="Path">The affected path.</param>
    /// <param name="OldPath">For rename events the original path; otherwise <c>null</c>.</param>
    /// <param name="ObservedAt">Timestamp when the event was observed (UTC).</param>
    /// <param name="Processable">Whether this path should be processed by the tailing pipeline.</param>
    public readonly record struct FsEvent(
        FsEventKind Kind,
        string Path,
        string? OldPath,
        DateTimeOffset ObservedAt,
        bool Processable);
}