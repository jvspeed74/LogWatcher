namespace LogWatcher.Core.Metrics
{
    /// <summary>
    /// Periodically collects and reports statistics from worker threads.
    /// </summary>
    public interface IReporter
    {
        /// <summary>
        /// Starts the reporter's background thread.
        /// </summary>
        void Start();

        /// <summary>
        /// Requests the reporter to stop.
        /// </summary>
        void Stop();
    }
}
