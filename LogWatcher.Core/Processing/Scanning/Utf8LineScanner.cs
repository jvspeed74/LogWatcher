using LogWatcher.Core.FileManagement;

namespace LogWatcher.Core.Processing.Scanning
{
    // PartialLineBuffer holds carryover bytes for a single file between chunk scans.
    // Fields are public to match the doc's simple data-shape.

    /// <summary>
    /// Utilities for scanning UTF-8 byte chunks and extracting newline-delimited lines.
    /// </summary>
    public static class Utf8LineScanner
    {
        /// <summary>
        /// Scans the concatenation of <paramref name="carry"/> and <paramref name="chunk"/>, invokes <paramref name="onLine"/> for each complete
        /// line found (the line passed to the callback does not include the newline character and any trailing CR is trimmed),
        /// and stores any trailing incomplete bytes back into <paramref name="carry"/>.
        /// </summary>
        /// <param name="chunk">The incoming byte chunk to scan.</param>
        /// <param name="carry">A per-file carry buffer that contains previously seen but incomplete line bytes; updated with any remaining trailing partial bytes.</param>
        /// <param name="onLine">Callback invoked for each complete line. The provided <see cref="ReadOnlySpan{Byte}"/> contains the raw bytes of the line (no CR/LF).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="onLine"/> is <c>null</c>.</exception>
        public static void Scan(ReadOnlySpan<byte> chunk, ref PartialLineBuffer carry,
            Action<ReadOnlySpan<byte>> onLine)
        {
            ArgumentNullException.ThrowIfNull(onLine);

            // Handle the carry path first
            if (carry.Length > 0)
            {
                // find first '\n' in chunk
                int nlIndex = chunk.IndexOf((byte)'\n');

                if (nlIndex == -1)
                {
                    // no newline in this chunk -> append whole chunk to carry and return
                    carry.Append(chunk);
                    return;
                }

                // newline found at nlIndex -> append chunk[..nlIndex] to carry, emit carry as a single line
                if (nlIndex > 0)
                {
                    carry.Append(chunk.Slice(0, nlIndex));
                }
                // else nlIndex == 0 -> nothing to append, carry already contains prior bytes

                var emitSpan = carry.AsSpan();
                if (emitSpan.Length > 0 && emitSpan[emitSpan.Length - 1] == (byte)'\r')
                {
                    emitSpan = emitSpan.Slice(0, emitSpan.Length - 1);
                }

                onLine(emitSpan);
                carry.Clear();

                // continue scanning with the remainder of the chunk after the newline
                chunk = chunk.Slice(nlIndex + 1);
            }

            // Scan remaining chunk for newline delimiters
            int start = 0;
            while (start < chunk.Length)
            {
                int j = chunk.Slice(start).IndexOf((byte)'\n');
                if (j == -1)
                    break; // no more newlines
                j += start; // adjust to full chunk index

                var lineSpan = chunk.Slice(start, j - start);
                if (lineSpan.Length > 0 && lineSpan[lineSpan.Length - 1] == (byte)'\r')
                {
                    lineSpan = lineSpan.Slice(0, lineSpan.Length - 1);
                }

                onLine(lineSpan);
                start = j + 1;
            }

            // store trailing bytes (if any) into carry
            if (start < chunk.Length)
            {
                carry.Append(chunk.Slice(start));
            }
        }
    }
}