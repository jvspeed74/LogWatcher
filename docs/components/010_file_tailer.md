## Component 10: FileTailer (append-only reads, truncation handling, chunked processing)

### Purpose (what this component must do)

Read only newly appended bytes from a file since the last processed offset, in a way that:

* supports concurrent writers (log files being appended)
* handles delete/rename races without crashing
* detects truncation (file size < offset) and resets to 0
* processes data in **chunks** so you don’t allocate huge buffers
* updates the caller’s offset only after the caller successfully processes the bytes

### Recommended API (callback-based chunk processing)

Implement a method that reads and invokes a callback per chunk, so the pipeline can scan/parse without buffering the whole appended region.

Create:

```csharp
public enum TailReadStatus
{
    NoData,
    ReadSome,
    FileNotFound,
    AccessDenied,
    IoError,
    TruncatedReset
}

public sealed class FileTailer
{
    public TailReadStatus ReadAppended(
        string path,
        ref long offset,
        Action<ReadOnlySpan<byte>> onChunk,
        out int totalBytesRead);
}
```

Behavior:

* `offset` is the per-file tail offset from `FileState`. Do not modify it until after reading.
* If truncation is detected, set a local `effectiveOffset=0`, and return status `TruncatedReset` if any read happens; otherwise return `TruncatedReset` or `NoData` depending on whether you want truncation to be observable even with no new bytes.
* `onChunk` is called for each read chunk.
* The caller decides whether processing succeeded; however, for weekend scope, assume processing always succeeds. If you want strict correctness, have `onChunk` return `bool` and only advance offset when all returned true.

### Step-by-step implementation

1. **Open the file with correct sharing**

    * Use:

        * `FileMode.Open`
        * `FileAccess.Read`
        * `FileShare.ReadWrite | FileShare.Delete`
          This allows reading while writers append and even while the file is deleted/renamed.

2. **Determine file length and truncation**

    * Read `stream.Length` (may throw if file disappears).
    * If `length < offset`, treat as truncation:

        * `effectiveOffset = 0`
        * record that truncation occurred (return `TruncatedReset` if you read something or if you choose to surface it even without reads)

3. **Seek and read**

    * `stream.Seek(effectiveOffset, SeekOrigin.Begin)`
    * Read in fixed chunks (e.g., 64 KB):

        * rent from `ArrayPool<byte>.Shared.Rent(chunkSize)`
        * `bytesRead = stream.Read(buffer, 0, chunkSize)` in a loop until 0
        * call `onChunk(buffer.AsSpan(0, bytesRead))`
        * accumulate `totalBytesRead`

4. **Advance offset after reading**

    * If `totalBytesRead > 0`:

        * `offset = effectiveOffset + totalBytesRead`
        * return `ReadSome` (or `TruncatedReset` if truncation was detected and you want that status to win)
    * Else:

        * return `NoData` (or `TruncatedReset` if truncation detected and you want that visible)

5. **Exception handling**

    * `FileNotFoundException` / `DirectoryNotFoundException`:

        * `totalBytesRead = 0`
        * return `FileNotFound`
    * `UnauthorizedAccessException`:

        * return `AccessDenied`
    * `IOException`:

        * return `IoError`
          Always ensure pooled buffers are returned in a `finally`.

### Unit tests (xUnit)

Use a temp directory and real files.

1. `ReadAppended_ReadsOnlyNewBytes`

    * write initial bytes, offset=0, read => bytesRead = len
    * append more, read again => bytesRead = appended len only
2. `ReadAppended_TruncationResetsOffset`

    * write bytes, read to end (offset set)
    * truncate file (overwrite shorter)
    * read => offset becomes <= new length; and status indicates truncation
3. `ReadAppended_FileDeleted_ReturnsFileNotFoundOrNoData`

    * delete file then call; ensure benign status and no exception
