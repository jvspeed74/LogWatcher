using System.Buffers;

namespace LogWatcher.Core.Processing.Tailing
{
    // TODO: FileTailer exposes only static members, making it impossible to substitute or mock in unit tests.
    // Callers (e.g. FileProcessor) are forced to use real file I/O even when testing higher-level logic.
    // Consider introducing an IFileTailer interface and an instance-based implementation so the I/O layer
    // can be replaced with a test double without hitting the filesystem.

    /// <summary>
    /// Utility to read bytes that have been appended to a file since a given offset.
    /// </summary>
    public sealed class FileTailer
    {
        // TODO: Consider making chunk size configurable per file type or based on available memory
        // FIXME: DefaultChunkSize is defined here as a private constant, but the same literal (64 * 1024) is
        // independently duplicated as the default argument in IFileProcessor.ProcessOnce and FileProcessor.ProcessOnce.
        // There is no single source of truth for this value; changing it in one place silently diverges from the others.
        private const int DefaultChunkSize = 64 * 1024;

        // TODO: The method signature uses both a `ref` parameter (offset) and an `out` parameter (totalBytesRead),
        // which produces a complex call site that is easy to misuse. Grouping these into a dedicated result record
        // or struct would make the contract clearer and harder to call incorrectly.
        //
        // TODO: There is no CancellationToken parameter. Long synchronous reads inside the while-loop below
        // cannot be interrupted by the caller. Consider adding a CancellationToken overload to support cooperative
        // cancellation in high-throughput or shutdown scenarios.
        //
        // TODO: The onChunk delegate creates an inverted (push-based) control flow. Callers cannot use natural
        // foreach-style iteration, compose with LINQ, or implement backpressure easily. A pull-based API (e.g.,
        // returning an IEnumerable<ReadOnlyMemory<byte>>) or an async streaming overload would be more composable.
        /// <summary>
        /// Reads bytes appended to <paramref name="path"/> since <paramref name="offset"/> and invokes <paramref name="onChunk"/> for each chunk read.
        /// The provided <see cref="ReadOnlySpan{Byte}"/> passed to <paramref name="onChunk"/> is only valid for the duration of the callback and must not be stored.
        /// On successful read the value referenced by <paramref name="offset"/> is advanced by the number of bytes read.
        /// I/O and permission errors are mapped to a <see cref="TailReadStatus"/> return value instead of being thrown.
        /// </summary>
        /// <param name="path">The filesystem path to the file to tail.</param>
        /// <param name="offset">On input the offset to start reading from; on successful read this value is advanced to the new offset.</param>
        /// <param name="onChunk">Callback invoked for each chunk read. The provided <see cref="ReadOnlySpan{Byte}"/> must not be retained beyond the callback.</param>
        /// <param name="totalBytesRead">Outputs the total number of bytes read during this call.</param>
        /// <param name="chunkSize">Maximum chunk size to use when reading; when &lt;= 0 the default of 64 KiB is used.</param>
        /// <returns>A <see cref="TailReadStatus"/> describing the outcome of the read operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> or <paramref name="onChunk"/> is <c>null</c>.</exception>
        public static TailReadStatus ReadAppended(
            string path,
            ref long offset,
            Action<ReadOnlySpan<byte>> onChunk,
            out int totalBytesRead,
            int chunkSize = DefaultChunkSize)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(onChunk);
            if (chunkSize <= 0) chunkSize = DefaultChunkSize;

            totalBytesRead = 0;
            bool truncated = false;

            byte[]? buffer = null;
            try
            {
                // Open with sharing to allow writers to append and deletions/renames
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                long length;
                try
                {
                    length = fs.Length;
                }
                catch (IOException)
                {
                    // Could not obtain length
                    return TailReadStatus.IoError;
                }

                long effectiveOffset = offset;
                if (length < offset)
                {
                    // truncation detected
                    effectiveOffset = 0;
                    truncated = true;
                }

                // TODO: The two guard conditions below (`effectiveOffset > length` and `effectiveOffset == length`)
                // could be unified into a single `effectiveOffset >= length` check. Their separate presence implies
                // distinct semantics, but both branches return the same status values, creating unnecessary duplication
                // and potential confusion for future maintainers.
                if (effectiveOffset > length)
                {
                    // no data
                    if (truncated) return TailReadStatus.TruncatedReset;
                    return TailReadStatus.NoData;
                }

                if (effectiveOffset == length)
                {
                    // no new data
                    if (truncated) return TailReadStatus.TruncatedReset;
                    return TailReadStatus.NoData;
                }

                // seek to effectiveOffset
                fs.Seek(effectiveOffset, SeekOrigin.Begin);

                // TODO: Consider using FileStream.ReadAsync for better async I/O performance in high-throughput scenarios
                // FIXME: The read loop has no cancellation path. If the file grows unboundedly between the `fs.Length`
                // check and this loop, the method will continue reading until it catches up with no way to stop early.
                buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

                int read;
                while ((read = fs.Read(buffer, 0, chunkSize)) > 0)
                {
                    totalBytesRead += read;
                    onChunk(new ReadOnlySpan<byte>(buffer, 0, read));
                }

                // advance offset only when we actually read bytes
                if (totalBytesRead > 0)
                {
                    offset = effectiveOffset + totalBytesRead;
                    return truncated ? TailReadStatus.TruncatedReset : TailReadStatus.ReadSome;
                }

                // FIXME: The comment below ("shouldn't reach here") is inaccurate. This path can be reached if
                // `fs.Read` returns 0 without EOF, for example during a concurrent truncation that occurs between
                // the `fs.Length` snapshot and the actual read. The comment gives a false guarantee and may cause
                // future maintainers to overlook a real edge case.
                // If we didn't read (shouldn't reach here because effectiveOffset==length handled), handle fallthrough
                if (truncated) return TailReadStatus.TruncatedReset;
                return TailReadStatus.NoData;
            }
            catch (FileNotFoundException)
            {
                // TODO: The `totalBytesRead = 0` reset in each catch block is redundant because totalBytesRead is
                // already initialized to 0 at the start of the method and is only incremented inside the try block,
                // which is not entered if an exception is thrown before that point. Consider removing the resets to
                // reduce noise, or encapsulate error-return logic in a shared helper to eliminate the repetition.
                totalBytesRead = 0;
                return TailReadStatus.FileNotFound;
            }
            catch (DirectoryNotFoundException)
            {
                totalBytesRead = 0;
                return TailReadStatus.FileNotFound;
            }
            catch (UnauthorizedAccessException)
            {
                totalBytesRead = 0;
                return TailReadStatus.AccessDenied;
            }
            catch (IOException)
            {
                totalBytesRead = 0;
                return TailReadStatus.IoError;
            }
            finally
            {
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
    }
}