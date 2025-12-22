## Component 11: FileProcessor (tail → scan → parse → update WorkerStatsBuffer)

### Purpose (what this component must do)

Given a path and its `FileState` (under gate), process newly appended bytes by:

* reading chunks via `FileTailer`
* scanning UTF-8 lines using `Utf8LineScanner` and the file’s carryover buffer
* parsing each complete line via `LogParser`
* updating the worker’s **active** `WorkerStatsBuffer`
* respecting L1 (missing latency does not make line malformed)
* leaving `FileState.Offset` and carryover consistent

### Dependencies (inject into FileProcessor)

* `FileTailer` (IO)
* `Utf8LineScanner` (line splitting)
* `LogParser` (parsing)
* `Encoding UTF8` for message key conversion, if needed

### Public contract

Create:

```csharp
public sealed class FileProcessor
{
    public void ProcessOnce(
        string path,
        FileState state,
        WorkerStatsBuffer stats);
}
```

“ProcessOnce” means: tail-read whatever is appended *right now* and process it. Catch-up looping (dirty flag) is handled by the coordinator, not here.

### Step-by-step implementation

1. **Precondition**

    * Caller must hold `state.Gate`. Document this.
    * If `state.IsDeletePending` is true, the coordinator should finalize delete; FileProcessor can be no-op.

2. **Local variables**

    * `int bytesReadTotal`
    * Call tailer with `ref state.Offset` or with local offset:

        * For conservative correctness, use a local copy:

            * `long localOffset = state.Offset`
            * tailer reads and updates `localOffset`
            * Only assign `state.Offset = localOffset` after processing completes
        * This prevents advancing offset if parsing throws (shouldn’t happen, but safer).

3. **ReadAppended callback**

    * For each chunk span:

        * call `Utf8LineScanner.Scan(chunk, ref state.Carry, onLine)`
    * `onLine` updates stats (see next step).

4. **Line processing**
   For each line `ReadOnlySpan<byte> line`:

    * increment `stats.LinesProcessed`
    * if `!parser.TryParse(line, out parsed)`:

        * increment `stats.MalformedLines`
        * return (continue)
    * else:

        * increment `stats.LevelCounts[(int)parsed.Level]`
        * message key (M2):

            * Convert `parsed.MessageKey` to string for dictionary key.
            * For fewer allocations, implement a small interning cache later; for now:

                * `string key = Encoding.UTF8.GetString(parsed.MessageKey)`
            * update message counts:

                * `stats.MessageCounts[key] = stats.MessageCounts.GetValueOrDefault(key) + 1`
        * latency:

            * if `parsed.LatencyMs is int v`: `stats.Histogram.Add(v)`

5. **Handle tailer status counters**
   After tailer returns status:

    * if `FileNotFound`: `stats.FileNotFoundCount++`
    * if `AccessDenied`: `stats.AccessDeniedCount++`
    * if `IoError`: `stats.IoExceptionCount++`
    * if truncation occurred: `stats.TruncationResetCount++`
    * if `NoData`: nothing to do

6. **Advance state.Offset**

    * If you used `localOffset`, assign it back only after chunk processing completes without exception.
    * Keep carryover buffer as updated by line scanner.

### Unit tests (xUnit)

Use a temp file and real writing to exercise the full pipeline.

1. `ProcessOnce_UpdatesLineAndLevelCounts`

    * write a few valid lines, call ProcessOnce, assert counts
2. `ProcessOnce_TailsOnlyNewBytes`

    * write initial lines, process, append more, process again, assert totals increment only for new lines
3. `ProcessOnce_HandlesMalformedTimestamp`

    * include a bad timestamp line; assert malformed count increments but processing continues
4. `ProcessOnce_HandlesMissingLatency`

    * line without latency_ms; assert line counted, histogram unchanged
5. `ProcessOnce_CarryoverAcrossChunks`

    * force chunk boundary mid-line:

        * easiest: write a long line > chunk size or reduce tailer chunk size in test
    * ensure line is counted once, not split

### Notes on spec alignment

* This component is where span-based parsing is actually used.
* The main remaining “allocation hot spot” is timestamp parsing and message key string conversion. That is acceptable for correctness-first; optimize later if needed.

---

Next response should cover:

* ProcessingCoordinator (workers, routing, per-file gate, dirty/delete-pending/tombstones, swap ack point)
* FilesystemWatcherAdapter (FileSystemWatcher integration and publish discipline)

These complete the runtime pipeline before CLI wiring.
