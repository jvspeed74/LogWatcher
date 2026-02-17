namespace LogWatcher.Core.IO
{
    /// <summary>
    /// Adapter for watching filesystem changes and publishing events.
    /// </summary>
    public interface IFilesystemWatcherAdapter : IDisposable
    {
        /// <summary>
        /// Gets the number of watcher errors observed.
        /// </summary>
        long ErrorCount { get; }

        /// <summary>
        /// Starts watching for filesystem events.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops watching for filesystem events.
        /// </summary>
        void Stop();
    }
}
