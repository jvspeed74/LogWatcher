using System.Buffers.Text;
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

            // 1. Tokenization: find first and second spaces to isolate the three fixed fields.
            //    Line format: "<timestamp> <level> <message...>"
            int s1 = line.IndexOf((byte)' ');
            if (s1 == -1) return false;
            int s2 = line.Slice(s1 + 1).IndexOf((byte)' ');
            if (s2 == -1) return false;
            // s2 was found relative to the slice starting at s1+1; rebase it to the original span.
            s2 = s2 + s1 + 1;

            var timestampBytes = line.Slice(0, s1);
            var levelBytes = line.Slice(s1 + 1, s2 - (s1 + 1));
            int messageStart = s2 + 1;
            ReadOnlySpan<byte> messageSpan =
                messageStart < line.Length ? line.Slice(messageStart) : ReadOnlySpan<byte>.Empty;

            // 2. Parse timestamp (strict ISO-8601, zero-allocation span-based parser)
            if (!TryParseTimestamp(timestampBytes, out DateTimeOffset dto))
                return false;

            // 3. Parse level (case-insensitive, zero-allocation)
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
                messageKey = firstSpaceInMsg == -1 ? messageSpan : messageSpan.Slice(0, firstSpaceInMsg);
            }

            // 5. Extract latency — search the entire line (not just the message field) because
            //    latency_ms= may appear anywhere after the level token. Missing or unparseable
            //    values are silently ignored; they never mark the line malformed (PRS-001).
            int? latency = null;
            int idx = line.IndexOf(LatencyPrefix);
            if (idx >= 0)
            {
                int valueStart = idx + LatencyPrefix.Length;
                if (valueStart < line.Length &&
                    Utf8Parser.TryParse(line.Slice(valueStart), out int latencyValue, out _))
                {
                    latency = latencyValue;
                }
            }

            parsed = new ParsedLogLine(dto, level, messageKey, latency);
            return true;
        }

        /// <summary>
        /// Parses a strict ISO-8601 timestamp from UTF-8 bytes without any heap allocation.
        /// Accepted formats: <c>yyyy-MM-ddTHH:mm:ssZ</c>, <c>yyyy-MM-ddTHH:mm:ss.f+Z</c>,
        /// <c>yyyy-MM-ddTHH:mm:ss±HH:MM</c>, <c>yyyy-MM-ddTHH:mm:ss.f+±HH:MM</c>.
        /// The result is always adjusted to UTC (PRS-003).
        /// </summary>
        private static bool TryParseTimestamp(ReadOnlySpan<byte> span, out DateTimeOffset result)
        {
            result = default;

            // Minimum length: yyyy-MM-ddTHH:mm:ssZ = 20 chars
            if (span.Length < 20) return false;

            if (!TryParseDigits4(span, 0, out int year)) return false;
            if (span[4] != (byte)'-') return false;
            if (!TryParseDigits2(span, 5, out int month)) return false;
            if (span[7] != (byte)'-') return false;
            if (!TryParseDigits2(span, 8, out int day)) return false;
            if (span[10] != (byte)'T') return false;
            if (!TryParseDigits2(span, 11, out int hour)) return false;
            if (span[13] != (byte)':') return false;
            if (!TryParseDigits2(span, 14, out int minute)) return false;
            if (span[16] != (byte)':') return false;
            if (!TryParseDigits2(span, 17, out int second)) return false;

            int pos = 19;
            int millisecond = 0;

            // Optional fractional seconds
            if (pos < span.Length && span[pos] == (byte)'.')
            {
                pos++;
                int fracStart = pos;
                while (pos < span.Length && span[pos] >= (byte)'0' && span[pos] <= (byte)'9')
                    pos++;
                int fracLen = pos - fracStart;
                if (fracLen == 0) return false; // '.' must be followed by at least one digit

                // Truncate to three significant digits (millisecond precision), then left-pad
                // shorter fractions with trailing zeros so that ".1" → 100 ms, ".12" → 120 ms,
                // ".123" → 123 ms, ".1234" → 123 ms (sub-millisecond precision is discarded).
                int ms = 0;
                int take = fracLen < 3 ? fracLen : 3;
                for (int i = 0; i < take; i++)
                    ms = ms * 10 + (span[fracStart + i] - (byte)'0');
                // Pad to millisecond precision when fewer than three digits were present
                for (int i = fracLen; i < 3; i++)
                    ms *= 10;
                millisecond = ms;
            }

            if (pos >= span.Length) return false;

            TimeSpan offset;
            byte tz = span[pos];
            if (tz == (byte)'Z')
            {
                pos++;
                offset = TimeSpan.Zero;
            }
            else if (tz == (byte)'+' || tz == (byte)'-')
            {
                // ±HH:MM — needs 6 bytes: sign + 2 digits + ':' + 2 digits
                if (pos + 6 > span.Length) return false;
                if (!TryParseDigits2(span, pos + 1, out int offHour)) return false;
                if (span[pos + 3] != (byte)':') return false;
                if (!TryParseDigits2(span, pos + 4, out int offMin)) return false;
                offset = new TimeSpan(offHour, offMin, 0);
                if (tz == (byte)'-') offset = -offset;
                pos += 6;
            }
            else
            {
                return false;
            }

            // Strict ISO-8601: no trailing characters allowed
            if (pos != span.Length) return false;

            // Range-check fields that the DateTimeOffset constructor does not guard cheaply.
            // These catches the most common invalid values without paying exception overhead;
            // the try/catch below handles the residual calendar-specific invalids (e.g. Feb 30).
            if (month < 1 || month > 12) return false;
            if (day < 1 || day > 31) return false;
            if (hour > 23 || minute > 59 || second > 59) return false;

            try
            {
                result = new DateTimeOffset(year, month, day, hour, minute, second, millisecond, offset)
                    .ToUniversalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Catches invalid calendar combinations (e.g. Feb 30) and out-of-range offsets
                return false;
            }

            return true;
        }

        private static bool TryParseDigits2(ReadOnlySpan<byte> span, int pos, out int value)
        {
            value = 0;
            // HACK: Slicing to exactly 2 bytes before calling Utf8Parser is intentional.
            // Utf8Parser stops at the first non-digit and returns the count of bytes consumed,
            // so passing an unbounded slice would silently accept "1:" as the value 1 (consumed=1).
            // Capping the slice to 2 and asserting consumed==2 enforces exact-width parsing
            // with no per-byte branching, keeping the hot timestamp path branchless and allocation-free.
            return pos + 2 <= span.Length
                && Utf8Parser.TryParse(span.Slice(pos, 2), out value, out int consumed)
                && consumed == 2;
        }

        private static bool TryParseDigits4(ReadOnlySpan<byte> span, int pos, out int value)
        {
            value = 0;
            // HACK: Same fixed-slice technique as TryParseDigits2 — slice to exactly 4 bytes so
            // Utf8Parser cannot over-consume and the consumed==4 assertion enforces exact width.
            return pos + 4 <= span.Length
                && Utf8Parser.TryParse(span.Slice(pos, 4), out value, out int consumed)
                && consumed == 4;
        }

        private static LogLevel ParseLevel(ReadOnlySpan<byte> span)
        {
            if (Ascii.EqualsIgnoreCase(span, "INFO"u8)) return LogLevel.Info;
            if (Ascii.EqualsIgnoreCase(span, "WARN"u8)) return LogLevel.Warn;
            if (Ascii.EqualsIgnoreCase(span, "ERROR"u8)) return LogLevel.Error;
            if (Ascii.EqualsIgnoreCase(span, "DEBUG"u8)) return LogLevel.Debug;
            return LogLevel.Other;
        }
    }
}