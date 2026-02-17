namespace LogWatcher.Core.Concurrency
{
    /// <summary>
    /// Coordinates worker threads that process filesystem events.
    /// </summary>
    public interface IProcessingCoordinator
    {
        /// <summary>
        /// Starts the worker threads.
        /// </summary>
        void Start();

        /// <summary>
        /// Requests an orderly shutdown of the worker threads.
        /// </summary>
        void Stop();
    }
}
