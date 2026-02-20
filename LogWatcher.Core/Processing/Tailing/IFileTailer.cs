namespace LogWatcher.Core.Processing.Tailing
{
    /// <summary>
    /// Abstraction for reading bytes appended to a file since a given offset.
    /// Implement this interface to provide a test double that avoids real filesystem I/O.
    /// </summary>
    public interface IFileTailer
    {
        /// <summary>
        /// Reads bytes appended to <paramref name="path"/> since <paramref name="offset"/> and invokes
        /// <paramref name="onChunk"/> for each chunk read.
        /// </summary>
        /// <param name="path">The filesystem path to the file to tail.</param>
        /// <param name="offset">On input the offset to start reading from; on successful read advanced to new offset.</param>
        /// <param name="onChunk">Callback invoked for each chunk read. The span is only valid for the duration of the callback.</param>
        /// <param name="totalBytesRead">Outputs the total number of bytes read during this call.</param>
        /// <param name="chunkSize">Maximum chunk size; when explicitly passed as &lt;= 0 the implementation uses <see cref="FileTailer.DefaultChunkSize"/>.</param>
        /// <returns>A <see cref="TailReadStatus"/> describing the outcome.</returns>
        TailReadStatus ReadAppended(string path, ref long offset, Action<ReadOnlySpan<byte>> onChunk,
            out int totalBytesRead, int chunkSize = FileTailer.DefaultChunkSize);
    }
}