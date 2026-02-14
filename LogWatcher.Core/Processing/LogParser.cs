using System.Globalization;
using System.Text;

namespace LogWatcher.Core.Processing
{
    /// <summary>
    /// Represents the severity level of a parsed log line.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Informational messages.</summary>
        Info,
        /// <summary>Warning messages.</summary>
        Warn,
        /// <summary>Error messages.</summary>
        Error,
        /// <summary>Debug-level messages.</summary>
        Debug,
        /// <summary>Unrecognized or other levels.</summary>
        Other
    }

    /// <summary>
    /// A stack-only view containing fields parsed from a single log line.
    /// This is a <c>readonly ref struct</c> and therefore must not be boxed or stored across async/heap boundaries.
    /// </summary>
    public readonly ref struct ParsedLogLine
    {
        /// <summary>The event timestamp parsed from the log line.</summary>
        public DateTimeOffset Timestamp { get; }
        /// <summary>The parsed log level.</summary>
        public LogLevel Level { get; }
        /// <summary>
        /// The message key bytes as a <see cref="ReadOnlySpan{Byte}"/> that points into the original input.
        /// The span is only valid for the lifetime of the input passed to <see cref="LogParser.TryParse"/> and must not be retained.
        /// </summary>
        public ReadOnlySpan<byte> MessageKey { get; }
        /// <summary>Optional latency value (milliseconds) extracted from the line, or <c>null</c> if not present.</summary>
        public int? LatencyMs { get; }

        public ParsedLogLine(DateTimeOffset timestamp, LogLevel level, ReadOnlySpan<byte> messageKey, int? latencyMs)
        {
            Timestamp = timestamp;
            Level = level;
            MessageKey = messageKey;
            LatencyMs = latencyMs;
        }
    }

    /// <summary>
    /// Lightweight parser for newline-delimited UTF-8 log lines that extracts timestamp, level, message key and optional latency.
    /// Parsing works on <see cref="ReadOnlySpan{Byte}"/> to avoid allocations; callers must not retain spans returned inside <see cref="ParsedLogLine"/>.
    /// </summary>
    public static class LogParser
    {
        private static readonly string[] IsoFormats = new[]
        {
            "yyyy-MM-ddTHH:mm:ssK",
            "yyyy-MM-ddTHH:mm:ss.fffK",
            "yyyy-MM-ddTHH:mm:ss.fffffffK"
        };

        private static ReadOnlySpan<byte> LatencyPrefix => new byte[]
        {
            (byte)'l', (byte)'a', (byte)'t', (byte)'e', (byte)'n', (byte)'c', (byte)'y', (byte)'_', (byte)'m',
            (byte)'s', (byte)'='
        };

        /// <summary>
        /// Attempts to parse a single UTF‑8 encoded log line into a <see cref="ParsedLogLine"/> view.
        /// </summary>
        /// <param name="line">The UTF‑8 encoded bytes of a single log line. The parser does not retain the span.</param>
        /// <param name="parsed">On success contains the parsed fields; on failure the out value is unspecified.</param>
        /// <returns><c>true</c> when parsing succeeded and <paramref name="parsed"/> contains valid fields; otherwise <c>false</c>.</returns>
        public static bool TryParse(ReadOnlySpan<byte> line, out ParsedLogLine parsed)
        {
            parsed = default;

            // 1. Tokenization: find first and second spaces
            int s1 = IndexOfByte(line, (byte)' ');
            if (s1 == -1) return false;
            int s2 = IndexOfByte(line.Slice(s1 + 1), (byte)' ');
            if (s2 == -1) return false;
            s2 = s2 + s1 + 1; // adjust to original span

            var timestampBytes = line.Slice(0, s1);
            var levelBytes = line.Slice(s1 + 1, s2 - (s1 + 1));
            int messageStart = s2 + 1;
            ReadOnlySpan<byte> messageSpan =
                messageStart < line.Length ? line.Slice(messageStart) : ReadOnlySpan<byte>.Empty;

            // 2. Parse timestamp (strict ISO-8601)
            string tsString = Encoding.UTF8.GetString(timestampBytes);
            // TODO: Consider caching UTF8 decoder or using Utf8Parser to avoid allocations
            if (!DateTimeOffset.TryParseExact(tsString, IsoFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            {
                parsed = default;
                return false;
            }

            // 3. Parse level without allocation (case-insensitive ASCII)
            var level = ParseLevel(levelBytes);

            // 4. Extract message key (first token of messageSpan)
            ReadOnlySpan<byte> messageKey;
            if (messageSpan.IsEmpty)
            {
                messageKey = ReadOnlySpan<byte>.Empty;
            }
            else
            {
                int firstSpaceInMsg = IndexOfByte(messageSpan, (byte)' ');
                if (firstSpaceInMsg == -1)
                    messageKey = messageSpan;
                else
                    messageKey = messageSpan.Slice(0, firstSpaceInMsg);
            }

            // 5. Extract latency
            int? latency = null;
            int idx = IndexOfSubsequence(line, LatencyPrefix);
            if (idx >= 0)
            {
                int valueStart = idx + LatencyPrefix.Length;
                if (valueStart < line.Length)
                {
                    var valSpan = line.Slice(valueStart);
                    // parse consecutive digits
                    int i = 0;
                    long acc = 0;
                    bool any = false;
                    while (i < valSpan.Length)
                    {
                        byte b = valSpan[i];
                        if (b < (byte)'0' || b > (byte)'9') break;
                        any = true;
                        acc = acc * 10 + (b - (byte)'0');
                        if (acc > int.MaxValue)
                        {
                            any = false;
                            break;
                        }

                        i++;
                    }

                    if (any && i > 0)
                    {
                        latency = (int)acc;
                    }
                }
            }

            parsed = new ParsedLogLine(dto, level, messageKey, latency);
            return true;
        }

        private static int IndexOfByte(ReadOnlySpan<byte> span, byte value)
        {
            // FIXME: Replace with ReadOnlySpan<byte>.IndexOf() for better performance
            // The built-in IndexOf uses vectorized operations on supported platforms
            for (int i = 0; i < span.Length; i++)
                if (span[i] == value)
                    return i;
            return -1;
        }

        private static LogLevel ParseLevel(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return LogLevel.Other;

            // compare against known levels
            if (EqualsIgnoreCaseAscii(span, "INFO")) return LogLevel.Info;
            if (EqualsIgnoreCaseAscii(span, "WARN")) return LogLevel.Warn;
            if (EqualsIgnoreCaseAscii(span, "ERROR")) return LogLevel.Error;
            if (EqualsIgnoreCaseAscii(span, "DEBUG")) return LogLevel.Debug;

            return LogLevel.Other;
        }

        private static bool EqualsIgnoreCaseAscii(ReadOnlySpan<byte> left, string right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
            {
                byte lb = left[i];
                char rc = right[i];
                // normalize lb to upper-case ASCII
                if (lb >= (byte)'a' && lb <= (byte)'z') lb = (byte)(lb - 32);
                if (lb != (byte)rc) return false;
            }

            return true;
        }

        private static int IndexOfSubsequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
        {
            // TODO: Consider using Boyer-Moore or other efficient substring search algorithm for better performance
            // Current naive implementation has O(n*m) complexity
            if (needle.Length == 0) return 0;
            if (needle.Length > haystack.Length) return -1;

            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    byte a = haystack[i + j];
                    byte b = needle[j];
                    // case-insensitive match for latency prefix (we stored lower-case)
                    if (a >= (byte)'A' && a <= (byte)'Z') a = (byte)(a + 32);
                    if (a != b)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok) return i;
            }

            return -1;
        }
    }
}