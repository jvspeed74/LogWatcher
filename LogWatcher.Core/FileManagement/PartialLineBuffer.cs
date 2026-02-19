namespace LogWatcher.Core.FileManagement;

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
            // TODO: Consider capping maximum buffer size to prevent unbounded growth from pathological input
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