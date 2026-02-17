using System.Collections.Concurrent;

namespace LogWatcher.Core.Processing;

/// <summary>
/// Registry of <see cref="FileState"/> objects keyed by file path. Supports concurrent access.
/// </summary>
public sealed class FileStateRegistry : IFileStateRegistry
{
    // TODO: Consider adding a cleanup mechanism for orphaned FileState entries when files are no longer being watched
    private readonly ConcurrentDictionary<string, FileState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _epochs = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets an existing <see cref="FileState"/> for <paramref name="path"/> or creates a new one with a generation based on the current epoch.
    /// </summary>
    /// <param name="path">Filesystem path for which to get or create state.</param>
    /// <returns>The <see cref="FileState"/> instance associated with <paramref name="path"/>.</returns>
    public FileState GetOrCreate(string path)
    {
        return _states.GetOrAdd(path, p =>
        {
            _epochs.TryGetValue(p, out var epoch);
            var fs = new FileState
            {
                Offset = 0,
                Carry = new PartialLineBuffer(),
                Generation = epoch + 1
            };
            return fs;
        });
    }

    /// <summary>
    /// Attempts to look up the <see cref="FileState"/> for <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Filesystem path to lookup.</param>
    /// <param name="state">On success receives the associated <see cref="FileState"/>.</param>
    /// <returns><c>true</c> if a state for <paramref name="path"/> exists; otherwise <c>false</c>.</returns>
    public bool TryGet(string path, out FileState state)
    {
        return _states.TryGetValue(path, out state!);
    }

    /// <summary>
    /// Finalizes deletion of the state for <paramref name="path"/>, clearing its carry buffer for GC hygiene and bumping the epoch counter.
    /// </summary>
    /// <param name="path">Path whose state should be finalized and removed.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="path"/> is null.</exception>
    public void FinalizeDelete(string path)
    {
        if (_states.TryRemove(path, out var removed))
        {
            // clear its carry for GC
            try
            {
                removed.ClearCarry();
            }
            catch
            {
                // swallow any errors from clearing fields
                // TODO: Add structured logging for carry buffer cleanup failures (path, exception details)
            }
        }

        _epochs.AddOrUpdate(path, 1, (_, old) => old + 1);
    }

    /// <summary>
    /// Returns the current epoch (generation counter) for <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Path to query.</param>
    /// <returns>The current epoch (0 when unknown) for <paramref name="path"/>.</returns>
    public int GetCurrentEpoch(string path)
    {
        _epochs.TryGetValue(path, out var e);
        return e;
    }
}