## Implementation plan for C# / .NET 8+ (standard library only), component by component

Assumptions:

* Target framework: `net8.0`
* OS: Windows
* Concurrency: threads + locks (`Thread`, `Monitor`, `ManualResetEventSlim`, `Interlocked`, `Volatile`)
* Parsing: `ReadOnlySpan<byte>`, `Span<byte>`, `Utf8Parser`, `DateTimeOffset.TryParseExact`
* Tests: xUnit or MSTest (either is fine; instructions below assume xUnit, but keep usage minimal)

---

# 0) Solution layout and build commands

1. Create solution:

    * `WatchStats` (console app)
    * `WatchStats.Tests` (test project)
2. Enable nullable and analyzers:

    * `<Nullable>enable</Nullable>`
    * `<ImplicitUsings>enable</ImplicitUsings>`
    * Treat warnings as errors if you want discipline.
3. Add shared project settings for Release profiling (optional):

    * `dotnet run -c Release -- <args>`
4. Add a single `WatchStats.Core` folder/namespace (even if same project) to keep components separated:

    * `Core/Events`
    * `Core/Bus`
    * `Core/Registry`
    * `Core/Pipeline`
    * `Core/Stats`
    * `Core/Reporting`
    * `Core/Watcher`

---

# 1) Domain types (Events, Records, Counters)

## Filesystem event model

1. Create `enum FsEventKind { Created, Modified, Deleted, Renamed }`.
2. Create `readonly record struct FsEvent(...)` containing:

    * `FsEventKind Kind`
    * `string Path`
    * `string? OldPath` (for rename)
    * `DateTimeOffset ObservedAt`
    * `bool Processable` (extension `.log` / `.txt`)
3. Create helper:

    * `static bool IsProcessablePath(string path)` using `Path.GetExtension` + case-insensitive compare.

## Parsed record model

1. Create `enum LogLevel { Trace, Debug, Info, Warn, Error, Fatal, Other }` (or your set).
2. Create `readonly record struct LogRecord(...)`:

    * `DateTimeOffset Timestamp`
    * `LogLevel Level`
    * `ReadOnlyMemory<byte> MessageKey` OR a custom key type (see below)
    * `int? LatencyMs`

### Message key representation (M2)

Because you want “zero-ish allocations”, avoid creating strings per line.
Recommended key approach for .NET 8:

* Use a pooled/owned byte slice for keys, but that’s complex.
* Weekend-feasible approach that is still efficient:

    * Convert key to a `string` **only when first seen**, and reuse it as dictionary key thereafter.
    * Keep hot-path span scanning; only allocate on new keys.

Implementation contract:

* Parser returns `ReadOnlySpan<byte>` for message key.
* Aggregator converts to string via `Encoding.UTF8.GetString(span)` only when needed.

---

# 2) UTF-8 line scanner (Span-based) + tests

## Component: `Utf8LineScanner`

1. Create a method:

    * `IEnumerable<ReadOnlySpan<byte>> ScanLines(ReadOnlySpan<byte> bytes, ref PartialLineBuffer carry)`
    * In C#, you cannot yield spans directly. Instead implement a callback form:

        * `void ScanLines(ReadOnlySpan<byte> bytes, ref PartialLineBuffer carry, Action<ReadOnlySpan<byte>> onLine)`
2. Implement `PartialLineBuffer`:

    * Holds a `byte[]` and `int Length`
    * Supports:

        * `Append(ReadOnlySpan<byte>)` (grow buffer using `ArrayPool<byte>` or plain `new` initially)
        * `Clear()`
        * `AsSpan()`
3. Scanner algorithm:

    * If `carry.Length > 0`, process combined data:

        * Find newline in incoming bytes
        * If a newline completes the carry line: create a temporary combined buffer (carry + portion up to newline) and
          call `onLine`

            * For “zero-ish”, avoid combining unless necessary; but carry implies a copy anyway.
    * For remaining bytes, scan for `\n` and emit spans referencing the original buffer.
    * Handle `\r\n` by trimming trailing `\r`.
    * Store trailing incomplete bytes into carry (copy).
4. Tests (xUnit):

    * lines split across chunks
    * `\r\n` across boundary
    * carryover persists correctly

---

# 3) Log parser (ISO-8601 strict, L1 latency, M2 message key) + tests

## Component: `LogParser`

Expose:

* `bool TryParse(ReadOnlySpan<byte> line, out ParsedLine parsed)`
  Where `ParsedLine` contains:
* `DateTimeOffset Timestamp`
* `LogLevel Level`
* `ReadOnlySpan<byte> MessageKey` (returned as slice of the line span; valid only within call scope)
* `int? LatencyMs`
* `bool Malformed`

Because message key span cannot outlive the line buffer, update stats immediately in the pipeline rather than storing
spans.

### Steps

1. Tokenize first two space-delimited tokens:

    * timestamp token (bytes up to first space)
    * level token (bytes from first space+1 to second space)
2. Strict ISO-8601 parse:

    * Convert timestamp token to `string` for parsing (this is per-line allocation).
    * If you want to avoid it, use `Utf8Parser` for a constrained format, but ISO-8601 is complex.
    * Weekend-friendly: accept that timestamp parse allocates; it’s still “mostly zero-ish” elsewhere.
    * Use `DateTimeOffset.TryParseExact` with a small set of ISO formats (document them).
3. Parse level:

    * Compare token bytes case-insensitively without allocating:

        * Use `MemoryExtensions.Equals(span, "INFO"u8, StringComparison.OrdinalIgnoreCase)` style (
          `ReadOnlySpan<byte>` + `u8` literals).
4. Message key (M2):

    * Message begins after second space.
    * Message key ends at next space or end-of-line.
    * If message begins with `latency_ms=`, key is `latency_ms=...` (rare) but accept.
5. Latency parse (L1):

    * Search for `latency_ms=` in the whole line (bytes):

        * Use `line.IndexOf("latency_ms="u8)` (implement helper if needed)
    * Parse integer digits following it using `Utf8Parser.TryParse`.
    * If parse fails, set latency null, do not mark malformed.
6. Tests:

    * valid
    * bad timestamp => false/malformed
    * latency missing => parsed with null latency
    * message key extraction
    * unknown level => Other

---

# 4) Latency histogram (0–10,000 + overflow) + percentile + tests

## Component: `LatencyHistogram`

1. Define bins:

    * simplest: 0..10,000 inclusive per 1ms => 10,001 bins + overflow (10,002 ints)
    * Memory: 10,002 * 4 bytes ≈ 40 KB per worker; acceptable.
2. Store counts in `int[] bins`.
3. Add:

    * `void Add(int latencyMs)`

        * clamp <0 to 0, >10000 to overflow bin
4. Merge:

    * `void MergeFrom(LatencyHistogram other)` (sum arrays)
5. Percentile:

    * `int? Percentile(double p)`

        * compute total count
        * find rank
        * cumulative scan
6. Tests:

    * empty => null percentiles
    * known values => percentiles correct
    * overflow

---

# 5) Top-K computation (report-time) + tests

## Component: `TopK`

1. Merge dictionaries into a global `Dictionary<string,int>`.
2. Compute top-K:

    * Iterate global dictionary into a list of entries
    * Use a partial selection approach:

        * simplest: sort by count desc then key asc; take K
    * Given report interval 2s and typical sizes, full sort is fine.
3. Tests for tie-break.

---

# 6) WorkerStats + double-buffer swap protocol (S2a) + tests

## Component: `WorkerStatsBuffer`

Fields:

* scalar counters (`long` for totals)
* `long[] LevelCounts` sized to enum
* `Dictionary<string,int> MessageCounts`
* `LatencyHistogram Histogram`

Methods:

* `Reset()` clears counters, clears dictionary, resets histogram bins (Array.Clear)
* `MergeInto(GlobalStatsSnapshot target)` (or merge externally)

## Component: `WorkerStats` (double buffer)

Fields:

* `WorkerStatsBuffer _a, _b;`
* `WorkerStatsBuffer _active; WorkerStatsBuffer _inactive;`
* `int _swapRequested` (0/1)
* `ManualResetEventSlim _swapAck` (or `SemaphoreSlim` if you want, but standard)
  Methods:
* `RequestSwap()` sets `_swapRequested=1` and resets ack
* `TryAcknowledgeSwap()` called by worker at safe point:

    * if swapRequested==1:

        * swap active/inactive references
        * reset new active buffer
        * set swapRequested=0
        * set ack event
* `WaitForAck()` used by reporter

Tests:

* updates go to active only
* after swap, inactive contains interval stats and active reset
* multi-thread swap request/ack correctness

---

# 7) EventBus (bounded, drop newest) + tests

## Component: `BoundedEventBus<T>`

Implementation using `Queue<T>` + `Monitor`:
Fields:

* `Queue<T> _q`
* `int _capacity`
* `object _lock`
* `bool _stopped`
  Counters:
* `long Published`, `long Dropped`
  Methods:
* `bool Publish(T item)`

    * lock
    * if stopped => false
    * if q.Count == capacity => Dropped++, return false
    * enqueue, Published++, `Monitor.Pulse`, return true
* `bool TryDequeue(out T item, int timeoutMs)`

    * lock
    * while q empty and not stopped:

        * `Monitor.Wait(lock, timeoutMs)`; handle timeouts
    * if q empty => false
    * dequeue => true
* `void Stop()`

    * lock set stopped, pulse all

Tests:

* at capacity drop newest
* multi-producer publish
* multi-consumer dequeue
* stop unblocks waiters

---

# 8) FileStateRegistry (gate + flags + tombstone epoch) + tests

## Component: `FileState`

Fields:

* `object Gate = new object()`
* `long Offset`
* `PartialLineBuffer Carry`
* `int Dirty` (0/1, use `Volatile.Read/Write`)
* `int DeletePending` (0/1)
* `int Generation` (optional)
  Helper methods:
* `MarkDirty()` (only if not deletePending)
* `MarkDeletePending()` sets flag, clears dirty

## Component: `FileStateRegistry`

Fields:

* `ConcurrentDictionary<string, FileState> _states`
* `ConcurrentDictionary<string, int> _epochs` (tombstones)
  Methods:
* `FileState GetOrCreate(string path)`

    * return existing or create new with epoch+1, offset 0
* `bool TryGet(string path, out FileState state)`
* `void FinalizeDelete(string path)`

    * `_states.TryRemove`
    * `_epochs.AddOrUpdate(path, 1, (_, e) => e+1)`
* `void MarkDeletePending(string path)` if exists
* Optional eviction: timer-based cleanup of `_epochs` by tracking last updated time (skip for weekend if not needed)

Tests:

* delete removes state
* recreate creates fresh state offset 0
* deletePending behavior under concurrent access

---

# 9) FileTailer (tailing read) + tests

## Component: `FileTailer`

Method:

*
`bool TryReadAppended(string path, ref long offset, out ReadOnlyMemory<byte> data, out int bytesRead, out TailReadResult result)`
Where `TailReadResult` indicates:
* Success with data
* NoData
* FileNotFound
* AccessDenied
* TruncatedReset (offset reset happened)

Implementation:

1. Open file stream:

    * `FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)`
2. Get length:

    * if length < offset => offset=0
3. Seek offset.
4. Read to EOF into a pooled buffer:

    * Use `ArrayPool<byte>.Shared.Rent(size)` (cap chunk sizes e.g. 64KB)
    * If data could exceed chunk, either:

        * accumulate into a single grown array (costly), or
        * return data per chunk and let pipeline scan per chunk
    * Recommended: process chunk-by-chunk to avoid huge allocations:

        * expose `ReadLoop` callback: `ProcessChunk(ReadOnlySpan<byte>)`
5. Handle exceptions:

    * FileNotFound, DirectoryNotFound => benign
    * UnauthorizedAccessException, IOException => benign with counter

Tests using temp files:

* append read
* truncation resets
* delete mid-read handled

---

# 10) Processing pipeline (tail → scan → parse → update stats)

## Component: `FileProcessor`

Method:

* `ProcessPath(string path, FileState state, WorkerStatsBuffer activeStats)`
  Steps:

1. If state.DeletePending set: return “needs finalize”.
2. Use tailer to read appended bytes from state.Offset.
3. For each chunk:

    * scanner.ScanLines(chunk, ref state.Carry, onLine)
    * onLine:

        * activeStats.Lines++
        * if parser.TryParse false => activeStats.Malformed++
        * else:

            * activeStats.LevelCounts[level]++
            * messageKeySpan -> convert to string only when required:

                * attempt dictionary lookup without allocating? (not possible with span key)
                * simplest: allocate string per line (bad)
                * better: maintain a small per-worker cache mapping from a hashed span to string (complex)
            * weekend-feasible compromise:

                * allocate string for message key per line, but reduce by interning via dictionary:

                    * create string
                    * `CollectionsMarshal.GetValueRefOrAddDefault` to update count efficiently
                * If allocations are too high, revisit later.
            * if latency present: histogram.Add(latency)
4. Update state.Offset only after successfully processing bytes read.

Note: if you want to preserve “zero-ish” more strictly, the next optimization is a custom message-key interner (byte[]
copy only on first sight). Implement after correctness.

---

# 11) ProcessingCoordinator (workers, routing, gate/dirty/delete-pending) + tests

## Component: `ProcessingCoordinator`

Fields:

* `BoundedEventBus<FsEvent> _bus`
* `FileStateRegistry _registry`
* `FileProcessor _processor`
* `WorkerStats[] _workerStats`
* `Thread[] _threads`
* `volatile bool _stopping`

Worker thread procedure (per worker):

1. while not stopping:

    * dequeue event (timeout wait)
    * route based on kind:

        * Deleted => HandleDelete(path)
        * Renamed => HandleDelete(oldPath) then HandleCreateOrModify(newPath)
        * Created/Modified => if Processable then HandleCreateOrModify(path) else only count event
    * after handling one event fully, call `workerStats.TryAcknowledgeSwap()` (S2a)

### HandleCreateOrModify(path)

1. var state = registry.GetOrCreate(path)
2. if !Monitor.TryEnter(state.Gate):

    * if DeletePending not set: set Dirty=1, increment coalesce counter
    * return
3. try:

    * if DeletePending: registry.FinalizeDelete(path); increment; return
    * loop:

        * processor.ProcessPath(...)
        * if DeletePending: registry.FinalizeDelete(path); return
        * if Dirty==1: Dirty=0; continue
        * break
4. finally Monitor.Exit

### HandleDelete(path)

1. if !registry.TryGet(path, out state) => return
2. if !Monitor.TryEnter(state.Gate):

    * state.DeletePending=1; state.Dirty=0; increment deletePendingSet
    * return
3. try:

    * state.DeletePending=1; state.Dirty=0
    * registry.FinalizeDelete(path)
4. finally Exit gate

Tests:

* simulated events with mock tailer/processor verify state transitions
* concurrency tests for delete vs in-flight

---

# 12) Reporter/Merger (2s, GC metrics) + tests

## Component: `Reporter`

Fields:

* `WorkerStats[]`
* `Stopwatch`
* GC baselines:

    * `long lastAllocated = GC.GetTotalAllocatedBytes(precise: false)` (or per-thread allocated if you prefer)
    * `int gen0 = GC.CollectionCount(0)` etc.
      Loop:

1. sleep/wait 2s (or use `PeriodicTimer`)
2. record elapsed since last report using Stopwatch
3. for each worker: `RequestSwap()`
4. wait all acks
5. merge inactive buffers:

    * sum scalars
    * merge dictionaries
    * merge histograms
6. compute top-K, percentiles
7. compute GC deltas:

    * allocatedDelta = current - lastAllocated
    * collection deltas
8. print report with elapsedSeconds
9. update baselines

Tests:

* merge correctness with synthetic buffers
* elapsedSeconds used in rates

---

# 13) FilesystemWatcherAdapter (System.IO.FileSystemWatcher) integration

## Component: `FilesystemWatcherAdapter`

1. Create `FileSystemWatcher` with:

    * `Path = watchDir`
    * `IncludeSubdirectories = false`
    * `NotifyFilter = FileName | LastWrite | Size` (tune)
    * `Filter = "*.*"` (you filter extensions yourself)
2. Wire events:

    * Created, Changed, Deleted, Renamed
3. In each handler:

    * build FsEvent with ObservedAt = DateTimeOffset.UtcNow
    * determine processable by extension
    * publish to bus
4. Start:

    * `EnableRaisingEvents = true`
5. Shutdown:

    * disable and dispose watcher

Notes:

* FileSystemWatcher often emits multiple Changed events; expected.
* Consider setting `InternalBufferSize` higher to reduce overflow risk; still standard library.

---

# 14) CLI host (Program.cs) wiring

1. Parse args (use simple manual parsing; no external libs).
2. Build config.
3. Instantiate:

    * registry
    * bus (capacity=10,000)
    * processor (tailer + scanner + parser)
    * coordinator (start N workers)
    * watcher adapter (start)
    * reporter (start)
4. Hook Ctrl+C:

    * stop watcher
    * bus.Stop()
    * coordinator.Stop() (set flag, join threads)
    * reporter.Stop()
5. Print final summary.

---

# 15) Stress tests in .NET

## Synthetic bus stress

* Spawn M publisher threads that call Publish in a tight loop.
* Run for e.g. 5 seconds.
* Assert:

    * no deadlock
    * Dropped increases when overloaded
    * Queue depth bounded

## IO stress

* In temp dir, spawn writer tasks/threads appending to multiple `.log` files.
* Start real watcher/coordinator.
* Randomly rename/delete some files.
* Run for 10–30 seconds.
* Assert:

    * process doesn’t crash
    * lines processed > 0
    * state registry size doesn’t explode

---

# Final note on allocation goals (practical for .NET 8)

Strictly “no per-line allocations” conflicts with “strict ISO-8601 parsing” if implemented via `TryParseExact` on a
string. If allocation pressure is too high:

1. Relax timestamp parsing to `DateTimeOffset.TryParse` on UTF-16 string (still allocs).
2. Or implement a restricted ISO-8601 parser over `ReadOnlySpan<byte>` (no allocation), focusing on the exact timestamp
   format you’ll generate in tests.

Implement correctness first; then optimize hot-path allocations (message key interner + timestamp parser) if needed.

---
