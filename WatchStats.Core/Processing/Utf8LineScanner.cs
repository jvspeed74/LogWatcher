namespace WatchStats.Core.Processing
{
    // PartialLineBuffer holds carryover bytes for a single file between chunk scans.
    // Fields are public to match the doc's simple data-shape.
    /// <summary>
    /// Holds carry-over bytes for a single file between chunk scans.
    /// The struct exposes a simple data shape (public fields) for performance and interoperability.
    /// </summary>
    public struct PartialLineBuffer
    {
        /// <summary>
        /// Underlying buffer that stores appended bytes. May be <c>null</c> when empty.
        /// </summary>
        public byte[]? Buffer;

        /// <summary>
        /// Number of valid bytes in <see cref="Buffer"/> (0..Buffer.Length).
        /// </summary>
        public int Length;

        private const int InitialSize = 256;

        /// <summary>
        /// Returns a readonly span of the valid bytes currently stored in the buffer.
        /// If the buffer is empty returns <see cref="ReadOnlySpan{Byte}.Empty"/>.
        /// </summary>
        /// <returns>A <see cref="ReadOnlySpan{Byte}"/> representing the valid bytes.</returns>
        public ReadOnlySpan<byte> AsSpan()
        {
            if (Buffer == null || Length == 0)
                return ReadOnlySpan<byte>.Empty;
            return new ReadOnlySpan<byte>(Buffer, 0, Length);
        }

        /// <summary>
        /// Clears the buffer by resetting <see cref="Length"/> to zero. The underlying array is not freed.
        /// </summary>
        public void Clear()
        {
            Length = 0;
        }

        /// <summary>
        /// Appends the provided source bytes into the buffer, growing the internal array by doubling when needed.
        /// </summary>
        /// <param name="src">The source bytes to append. If empty, the method returns immediately.</param>
        public void Append(ReadOnlySpan<byte> src)
        {
            if (src.Length == 0)
                return;

            if (Buffer == null)
            {
                Buffer = new byte[InitialSize];
            }

            int required = Length + src.Length;
            if (required > Buffer.Length)
            {
                int newSize = Buffer.Length;
                while (newSize < required)
                {
                    newSize = Math.Max(newSize * 2, newSize + 1);
                }

                var newBuf = new byte[newSize];
                if (Length > 0)
                    Array.Copy(Buffer, 0, newBuf, 0, Length);
                Buffer = newBuf;
            }

            // copy src into Buffer at offset Length
            src.CopyTo(new Span<byte>(Buffer, Length, src.Length));
            Length = required;
        }
    }

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
            if (onLine == null) throw new ArgumentNullException(nameof(onLine));

            // Handle the carry path first
            if (carry.Length > 0)
            {
                // find first '\n' in chunk
                int nlIndex = -1;
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (chunk[i] == (byte)'\n')
                    {
                        nlIndex = i;
                        break;
                    }
                }

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
                int j = -1;
                for (int i = start; i < chunk.Length; i++)
                {
                    if (chunk[i] == (byte)'\n')
                    {
                        j = i;
                        break;
                    }
                }

                if (j == -1)
                    break; // no more newlines

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