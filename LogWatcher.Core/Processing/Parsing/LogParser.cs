using System.Globalization;
using System.Text;

namespace LogWatcher.Core.Processing.Parsing
{
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

        private static ReadOnlySpan<byte> LatencyPrefix => "latency_ms="u8;

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
            int s1 = line.IndexOf((byte)' ');
            if (s1 == -1) return false;
            int s2 = line.Slice(s1 + 1).IndexOf((byte)' ');
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
                int firstSpaceInMsg = messageSpan.IndexOf((byte)' ');
                if (firstSpaceInMsg == -1)
                    messageKey = messageSpan;
                else
                    messageKey = messageSpan.Slice(0, firstSpaceInMsg);
            }

            // 5. Extract latency
            int? latency = null;
            int idx = line.IndexOf(LatencyPrefix);
            if (idx >= 0)
            {
                int valueStart = idx + LatencyPrefix.Length;
                if (valueStart < line.Length)
                {
                    var valSpan = line.Slice(valueStart);
                    if (System.Buffers.Text.Utf8Parser.TryParse(valSpan, out int parsedLatency, out _))
                    {
                        latency = parsedLatency;
                    }
                }
            }

            parsed = new ParsedLogLine(dto, level, messageKey, latency);
            return true;
        }

        private static LogLevel ParseLevel(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return LogLevel.Other;

            // compare against known levels
            if (System.Text.Ascii.EqualsIgnoreCase(span, "INFO"u8)) return LogLevel.Info;
            if (System.Text.Ascii.EqualsIgnoreCase(span, "WARN"u8)) return LogLevel.Warn;
            if (System.Text.Ascii.EqualsIgnoreCase(span, "ERROR"u8)) return LogLevel.Error;
            if (System.Text.Ascii.EqualsIgnoreCase(span, "DEBUG"u8)) return LogLevel.Debug;

            return LogLevel.Other;
        }
    }
}