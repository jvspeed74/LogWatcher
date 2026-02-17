namespace LogWatcher.Core.IO
{
    /// <summary>
    /// Provides functionality to read appended data from files.
    /// </summary>
    public interface IFileTailer
    {
        /// <summary>
        /// Reads newly appended data from the specified file starting at the given offset.
        /// </summary>
        /// <param name="path">Path to the file to read.</param>
        /// <param name="offset">Starting offset; updated with the new offset after reading.</param>
        /// <param name="onChunk">Callback invoked for each chunk of data read.</param>
        /// <param name="totalBytesRead">Total number of bytes read.</param>
        /// <param name="chunkSize">Size of chunks to read at a time.</param>
        /// <returns>Status indicating the result of the tail operation.</returns>
        TailReadStatus ReadAppended(string path, ref long offset, Action<ReadOnlySpan<byte>> onChunk, 
            out int totalBytesRead, int chunkSize = 64 * 1024);
    }
}
