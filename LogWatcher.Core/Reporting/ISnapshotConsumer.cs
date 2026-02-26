namespace LogWatcher.Core.Reporting;

/// <summary>
/// Receives finalized <see cref="GlobalSnapshot"/> instances from the <see cref="Reporter"/> after each reporting interval.
/// Implementations may write to console, push to a web UI, persist to storage, etc.
/// </summary>
public interface ISnapshotConsumer
{
    /// <summary>
    /// Called by the reporter after each interval's snapshot has been fully finalized.
    /// Implementations must not retain a reference to <paramref name="snapshot"/> beyond this call —
    /// the reporter reuses the same instance across intervals.
    /// </summary>
    /// <param name="snapshot">The finalized snapshot for this interval.</param>
    /// <param name="elapsed">Actual elapsed time for this interval. <see cref="TimeSpan.Zero"/> indicates a final/shutdown report.</param>
    void OnSnapshot(GlobalSnapshot snapshot, TimeSpan elapsed);
}