# Component 1: UTF-8 Line Scanner (chunked, CRLF, carryover)

### Purpose (what this component must do)

Convert a stream of UTF-8 bytes read in arbitrary chunks into a sequence of complete “lines” suitable for parsing, while:

* handling both `\n` and `\r\n`
* supporting lines split across chunk boundaries
* avoiding per-line allocations (except when storing an incomplete trailing line)
* persisting an incomplete trailing line in a per-file carryover buffer

This component is a foundation: the file tailer will feed chunks into it, and the log parser will consume each emitted line.

### Public contract (C#-friendly)

Because `ReadOnlySpan<byte>` cannot be safely stored or yielded, implement a callback-based API.

1. Create a type `PartialLineBuffer` that holds the carryover bytes for a single file:

   * fields: `byte[] Buffer`, `int Length`
   * methods:

      * `ReadOnlySpan<byte> AsSpan()`
      * `void Clear()`
      * `void Append(ReadOnlySpan<byte> src)` (grows buffer as needed)
   * allocation policy:

      * first allocate small (e.g., 256 bytes)
      * grow by doubling
      * optional optimization later: use `ArrayPool<byte>`; implement plain arrays first for correctness

2. Create `Utf8LineScanner` as a static class with one method:

   * `void Scan(ReadOnlySpan<byte> chunk, ref PartialLineBuffer carry, Action<ReadOnlySpan<byte>> onLine)`

Behavior:

* `carry` contains bytes that are part of a line that did not end in a newline in the prior chunk.
* `Scan` processes `carry + chunk` as if concatenated, emitting each complete line via `onLine`.
* Any trailing incomplete bytes are stored back into `carry` and not emitted.

### Implementation steps

1. **Handle the “carry exists” path first**

   * If `carry.Length == 0`: skip to step 2.
   * Otherwise:

      * search `chunk` for the first `\n`
      * if none found:

         * append entire `chunk` to `carry`
         * return (still incomplete)
      * if found at index `i`:

         * the complete line is: `carry` + `chunk[..i]` (excluding `\n`)
         * if `chunk[i-1]` exists and is `\r` (or carry ends with `\r`), trim `\r` from the emitted line
         * build a contiguous buffer for emission:

            * easiest: append `chunk[..i]` into `carry`, emit `carry.AsSpan()` (minus trailing `\r`), then clear carry
            * this implies one copy only when carry existed, which is acceptable
         * then continue scanning the remaining bytes in `chunk[(i+1)..]` with “no carry” mode

2. **Scan the remaining chunk for newline delimiters**

   * Maintain `start = 0`
   * While you can find `\n`:

      * let newline index be `j`
      * line span is `chunk[start..j]`
      * if span ends with `\r`, trim it (`span[..^1]`)
      * invoke `onLine(span)`
      * set `start = j + 1`
   * After loop, if `start < chunk.Length`:

      * store trailing bytes `chunk[start..]` into `carry` (copy)

3. **Do not emit empty trailing line unless newline exists**

   * If chunk ends with newline, you should emit an empty line only if the actual line between delimiters is empty (e.g., `"\n\n"` yields an empty line in between). The algorithm above naturally does this.

4. **Edge cases to explicitly handle**

   * `\r\n` split across chunks:

      * carry ends with `\r`, next chunk starts with `\n`
      * your trimming logic must handle trimming the `\r` even if it’s in carry
   * Very long lines:

      * ensure carry can grow
      * ensure you don’t O(n^2) append; use doubling resize

### Unit tests (xUnit recommended)

Create `Utf8LineScannerTests` that validate:

1. `LF_only_single_chunk`
2. `CRLF_single_chunk`
3. `CRLF_split_across_chunks`
4. `Line_split_across_chunks_no_newline_until_later`
5. `Multiple_lines_in_one_chunk`
6. `Empty_lines_between_newlines` (e.g., `"\n\n"` emits an empty line once)
7. `Carryover_preserved_when_no_newline`

Test strategy:

* Use ASCII bytes to keep it readable; still UTF-8 valid.
* Collect emitted lines by copying spans to strings in tests only.

