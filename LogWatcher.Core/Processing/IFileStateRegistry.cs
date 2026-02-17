namespace LogWatcher.Core.Processing
{
    /// <summary>
    /// Registry for managing per-file state objects.
    /// </summary>
    public interface IFileStateRegistry
    {
        /// <summary>
        /// Gets or creates a state object for the specified file path.
        /// </summary>
        /// <param name="path">File path to look up or create state for.</param>
        /// <returns>The file state object.</returns>
        FileState GetOrCreate(string path);

        /// <summary>
        /// Tries to get an existing state object for the specified path.
        /// </summary>
        /// <param name="path">File path to look up.</param>
        /// <param name="state">The found state object, or null if not found.</param>
        /// <returns>True if the state was found; otherwise false.</returns>
        bool TryGet(string path, out FileState? state);

        /// <summary>
        /// Finalizes deletion of the file state for the specified path.
        /// </summary>
        /// <param name="path">File path to remove state for.</param>
        void FinalizeDelete(string path);
    }
}
