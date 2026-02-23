using System.Buffers;

using Microsoft.Extensions.Logging;

namespace LogWatcher.Core.Processing.Tailing
{
    /// <summary>
    /// Utility to read bytes that have been appended to a file since a given offset.
    /// </summary>
    public sealed partial class FileTailer : IFileTailer
    {
        public const int DefaultChunkSize = 64 * 1024;

        private readonly ILogger<FileTailer>? _logger;

        /// <param name="logger">Optional logger for truncation detection.</param>
        public FileTailer(ILogger<FileTailer>? logger = null)
        {
            _logger = logger;
        }
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
        public TailReadStatus ReadAppended(string path, ref long offset, Action<ReadOnlySpan<byte>> onChunk,
            out int totalBytesRead, int chunkSize = DefaultChunkSize)
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
                    if (_logger != null) LogTruncationDetected(_logger, path, offset);
                    effectiveOffset = 0;
                    truncated = true;
                }

                if (effectiveOffset >= length)
                {
                    // no new data (or file was truncated to exactly this offset)
                    if (truncated) return TailReadStatus.TruncatedReset;
                    return TailReadStatus.NoData;
                }
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

                // If fs.Read returned 0 without advancing (e.g. due to concurrent truncation between the
                // fs.Length snapshot and the actual read), fall through and report the appropriate status.
                if (truncated) return TailReadStatus.TruncatedReset;
                return TailReadStatus.NoData;
            }
            catch (FileNotFoundException)
            {
                return TailReadStatus.FileNotFound;
            }
            catch (DirectoryNotFoundException)
            {
                return TailReadStatus.FileNotFound;
            }
            catch (UnauthorizedAccessException)
            {
                return TailReadStatus.AccessDenied;
            }
            catch (IOException)
            {
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

        [LoggerMessage(Level = LogLevel.Debug, Message = "Truncation detected, offset reset path={Path} oldOffset={OldOffset}")]
        private static partial void LogTruncationDetected(ILogger logger, string path, long oldOffset);
    }
}