
# Component 2: Log Parser (ISO-8601 strict, M2, L1 latency)

### Purpose (what this component must do)

Parse each emitted line (as UTF-8 bytes) according to:

`<timestamp> <level> <message> latency_ms=<int>`

Rules:

* Timestamp must parse strictly as ISO-8601; if it fails, the line is malformed and contributes only to malformed count.
* Level maps to known values; unknown becomes `Other` (not malformed).
* Message key is the first token of `<message>` (M2).
* Latency behavior L1:

    * if `latency_ms=` is missing or malformed, still count the line; just omit latency.

### Public contract

Create a struct result that is cheap to use per line:

1. `readonly record struct ParsedLogLine(DateTimeOffset Timestamp, LogLevel Level, ReadOnlySpan<byte> MessageKey, int? LatencyMs);`

Then expose:

2. `bool TryParse(ReadOnlySpan<byte> line, out ParsedLogLine parsed);`

* Returns `false` only when the line is malformed due to timestamp parsing failure or missing required tokens (timestamp and level at minimum).
* Returns `true` if timestamp parses and there is at least a level token; message key may be empty if message missing (acceptable or treat as malformed—choose “acceptable” unless you prefer stricter).

### Implementation steps

1. **Fast tokenization (no allocations)**

    * Find first space index `s1`. If none: return false.
    * Find second space index `s2` after `s1+1`. If none: return false (no message token start; you can allow empty message, but you still need at least timestamp+level).
    * timestampBytes = `line[..s1]`
    * levelBytes = `line[(s1+1)..s2]`
    * messageStart = `s2 + 1`
2. **Strict ISO-8601 timestamp parse**

    * Convert `timestampBytes` to `string` via `Encoding.UTF8.GetString(timestampBytes)` (this allocates).
    * Parse with `DateTimeOffset.TryParseExact` using a small set of ISO-8601 formats you explicitly support.

        * Support at least:

            * `yyyy-MM-ddTHH:mm:ssK`
            * `yyyy-MM-ddTHH:mm:ss.fffK`
            * `yyyy-MM-ddTHH:mm:ss.fffffffK`
        * Use `CultureInfo.InvariantCulture` and `DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal` as appropriate.
    * If parse fails: return false.
3. **Parse level without allocating**

    * Compare `levelBytes` against UTF-8 literals (case-insensitive):

        * `INFO`, `WARN`, `ERROR`, `DEBUG`, etc.
    * Use `MemoryExtensions.Equals(levelBytes, "INFO"u8, StringComparison.OrdinalIgnoreCase)` style comparisons.
4. **Extract message key (M2)**

    * messageSpan = `line[messageStart..]`
    * Find first space in messageSpan; if none:

        * messageKey = messageSpan (but you may want to strip a trailing `latency_ms=` if the message is only that)
    * Otherwise:

        * messageKey = `messageSpan[..firstSpace]`
    * Note: you do not need to remove `latency_ms=` from the message key; since it appears later typically, and M2 is first token, it won’t interfere.
5. **Extract latency (L1)**

    * Search for `latency_ms=` in the full line:

        * Implement a small helper `IndexOfSubsequence(ReadOnlySpan<byte>, ReadOnlySpan<byte>)`
        * or use `line.IndexOf("latency_ms="u8)` if available with span overloads in your environment
    * If found:

        * valueSpan starts after the substring
        * parse consecutive digits using `Utf8Parser.TryParse(valueSpan, out int latency, out int bytesConsumed)`
        * accept only if bytesConsumed > 0 and the first consumed chars are digits
        * else latency = null
6. **Return parsed**

    * Populate `ParsedLogLine` with:

        * parsed timestamp
        * parsed level
        * messageKey span (points into `line`, valid only for the duration of the call chain)
        * latency nullable

### Unit tests

Create `LogParserTests`:

1. `Parses_valid_line_with_latency`
2. `Parses_valid_line_without_latency`
3. `Malformed_timestamp_returns_false`
4. `Unknown_level_maps_to_other`
5. `Message_key_is_first_token`
6. `Latency_malformed_is_null_but_parse_succeeds`
7. `Handles_offset_or_zulu_timestamps` (examples with `Z` and `-06:00`)

In tests, build lines as UTF-8 bytes and call `TryParse`.
