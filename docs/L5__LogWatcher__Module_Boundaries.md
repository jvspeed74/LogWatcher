# LogWatcher — Module Boundaries

> Framework rules: [`module_definition.md`](module_definition.md)

This document defines the 12 modules of LogWatcher, their boundaries, and responsibilities. Use this when deciding where
code belongs.

---

## **Quick Reference: 12 Modules → 7 Namespaces**

| #  | Module                  | Namespace                             | Responsibility                         |
|----|-------------------------|---------------------------------------|----------------------------------------|
| 1  | Ingestion               | `LogWatcher.Core.Ingestion`           | OS events → FsEvent                    |
| 2  | Event Distribution      | `LogWatcher.Core.Backpressure`        | Bounded async queue                    |
| 3  | File State Management   | `LogWatcher.Core.FileManagement`      | Per-file state, offset, lock           |
| 4  | File Tailing            | `LogWatcher.Core.Processing.Tailing`  | Incremental file reads                 |
| 5  | Line Scanning           | `LogWatcher.Core.Processing.Scanning` | Chunked bytes → lines                  |
| 6  | Log Parsing             | `LogWatcher.Core.Processing.Parsing`  | Bytes → structured records             |
| 7  | File Processing         | `LogWatcher.Core.Processing`          | Orchestrate tailer+scanner+parser      |
| 8  | Processing Coordination | `LogWatcher.Core.Processing`          | Route events, enforce serialization    |
| 9  | Statistics Collection   | `LogWatcher.Core.Statistics`          | Per-worker metrics accumulation        |
| 10 | Worker Coordination     | `LogWatcher.Core.Coordination`        | Double-buffer swap protocol            |
| 11 | Reporting               | `LogWatcher.Core.Reporting`           | Merge stats, print reports             |
| 12 | CLI & Host              | `LogWatcher.App`                      | Argument parsing, dependency injection |

---

## **Module Details**

### **Module 1: Ingestion**

**Namespace:** `LogWatcher.Core.Ingestion`

**Responsibility:** Adapt OS filesystem events into internal `FsEvent` objects.

**In Scope:**

- OS notification handling
- Event normalization
- Extension filtering
- Event contracts

**Out of Scope:**

- Queue distribution
- File content access
- State tracking

**Why:** Isolates OS-level concerns (FileSystemWatcher API, timing, paths) from business logic. This module owns the OS
boundary.

**Postulate Dimensions:**
FileSystemWatcher API changes, new file extensions to monitor, or different event source (e.g., polling instead of
notifications).

**Data Ownership:**
- `FsEvent` — the normalized internal representation of an OS filesystem event; its schema (path fields, processable flag, observed timestamp) is governed by the OS event model and extension filtering rules, which is this module's Postulate
- `FsEventKind` — the enum classifying the type of filesystem event; its variants change when new OS event types are added or the internal event classification changes, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: none
- Behavioral, outbound: `bus.Publish(FsEvent item) → bool` — publishes a normalized event to the bounded queue; never blocks regardless of return value (BP-006, ING-002)
- Data, outbound: `FsEvent` to Processing Coordination — the normalized event published to the queue and dequeued by worker threads
- Data, outbound: `FsEventKind` to Processing Coordination — the event-kind enum read when routing events and translating to `StatEventKind` before incrementing per-kind counters
- Data, inbound: `BoundedEventBus<FsEvent>` from Event Distribution — the queue into which events are published

**Dependency Direction:** depends on Event Distribution

---

### **Module 2: Event Distribution**

**Namespace:** `LogWatcher.Core.Backpressure`

**Responsibility:** Thread-safe bounded queue with producer/consumer coordination.

**In Scope:**

- FIFO queue mechanics
- Thread synchronization
- Backpressure (drop-newest)
- Shutdown signaling
- Metrics (published, dropped, depth)

**Out of Scope:**

- Event semantics
- Consumer threads
- Producer lifecycle

**Why:** Decouples event generation from event processing. Provides predictable backpressure behavior without requiring
callers to manage synchronization.

**Postulate Dimensions:**
Queue semantics change (drop-oldest vs. drop-newest), capacity strategy becomes adaptive, or synchronization primitive
changes (Monitor to lock-free channel).

**Data Ownership:**
- `BoundedEventBus<T>` — the bounded, thread-safe queue implementing backpressure; its schema (capacity policy, drop behavior, shutdown signaling, observable metrics) changes when queue semantics change, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `Publish(T item) → bool` — enqueues an item; drops and returns false when full, never blocks (BP-006)
- Behavioral, inbound: `TryDequeue(out T item, int timeoutMs) → bool` — dequeues with a timeout; returns false on timeout or after Stop()
- Behavioral, inbound: `Stop() → void` — signals shutdown; unblocks waiting consumers; no further items are enqueued after this call (BP-005)
- Behavioral, outbound: none
- Data, outbound: `BoundedEventBus<FsEvent>` to Ingestion, Processing Coordination, Reporting, CLI & Host — the shared queue instance referenced by producers, consumers, and the reporter reading bus metrics
- Data, inbound: none

**Dependency Direction:** none

---

### **Module 3: File State Management**

**Namespace:** `LogWatcher.Core.FileManagement`

**Responsibility:** Per-file mutable state tracking (offset, carry buffer, flags, lock).

**In Scope:**

- Offset tracking
- Carry buffer for incomplete lines
- Per-file gate lock
- Dirty flag for coalescing
- Delete-pending flag
- Tombstone epoch

**Out of Scope:**

- File IO
- Line parsing
- Statistics
- Worker coordination

**Why:** Centralizes all per-file state so workers can safely access and update file tracking without races or lost
updates.

**Postulate Dimensions:**
New per-file metadata needed (e.g., line count, last modified time), tombstone strategy changes, or carry buffer
strategy changes (fixed size vs. exponential growth).

**Data Ownership:**
- `FileState` — per-file mutable state (offset, carry buffer, dirty flag, delete-pending flag, gate lock, generation); its schema changes when new per-file metadata is required, which is this module's Postulate
- `PartialLineBuffer` — carry buffer for incomplete lines at chunk boundaries; its schema (growth strategy, length tracking) changes with carry buffer strategy, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `GetOrCreate(string path) → FileState` — returns or creates per-file state; always returns a new state with offset zero and empty carry after finalization (FM-005)
- Behavioral, inbound: `TryGet(string path, out FileState state) → bool` — returns existing state if present, false otherwise
- Behavioral, inbound: `FinalizeDelete(string path) → void` — releases the carry buffer and removes state (FM-004)
- Behavioral, outbound: `PartialLineBuffer.AsSpan() → ReadOnlySpan<byte>` — returns buffered carry data; the span is only valid until the next mutating call on the same buffer (FM-PLB-005)
- Data, outbound: `FileState` to Processing Coordination, File Processing — per-file state consumed by the coordinator for gate acquisition and by the processor for offset update and carry access; callers must hold `state.Gate` before reading or mutating `Offset` or `Carry` (FM-007)
- Data, outbound: `PartialLineBuffer` to File Processing, Line Scanning — carry buffer threaded through successive chunks; the span returned by `AsSpan()` is only valid until the next mutating call (FM-PLB-005)
- Data, inbound: none

**Dependency Direction:** none

---

### **Module 4: File Tailing**

**Namespace:** `LogWatcher.Core.Processing.Tailing`

**Responsibility:** Incremental file reads with truncation detection.

**In Scope:**

- File opening with sharing flags
- Chunked reading
- Truncation detection
- Status reporting

**Out of Scope:**

- State tracking
- Line splitting
- Parsing
- File state mutations

**Why:** Isolates IO concerns from the processing pipeline. Callback-driven design lets consumers process data as it's
read, without buffering.

**Postulate Dimensions:**
Chunk size needs tuning, file sharing flags change (OS-specific behavior), or retry logic added for transient failures.

**Data Ownership:**
- `TailReadStatus` — enum of possible read outcomes (NoData, ReadSome, FileNotFound, AccessDenied, IoError, TruncatedReset); its variants change when new tailing failure modes are recognized, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `ReadAppended(string path, ref long offset, Action<ReadOnlySpan<byte>> onChunk, out int totalBytesRead, int chunkSize) → TailReadStatus` — reads bytes appended since the given offset; advances the caller's offset by exactly the number of bytes delivered on success (TAIL-006); the span passed to `onChunk` is only valid for the duration of that callback (TAIL-005); IO errors are mapped to status codes and never thrown (TAIL-004)
- Behavioral, outbound: `onChunk(ReadOnlySpan<byte> chunk)` callback — delivers a raw byte chunk to the caller; the span is only valid for the duration of the callback and must not be retained (TAIL-005)
- Data, outbound: `TailReadStatus` to File Processing — read outcome signal used to update error counters and decide next action
- Data, inbound: none

**Dependency Direction:** none

---

### **Module 5: Line Scanning**

**Namespace:** `LogWatcher.Core.Processing.Scanning`

**Responsibility:** CRLF/LF-delimited scanning with carryover support.

**In Scope:**

- Line delimiter detection
- Incomplete line carryover
- Span-based emission (zero-copy)

**Out of Scope:**

- File state
- Line content validation
- Parsing

**Why:** Separates the mechanical task of splitting bytes into lines from the semantic task of understanding log format.

**Postulate Dimensions:**
Support different line delimiters (e.g., NUL-terminated), change carryover strategy (buffer management), or add line
length validation.

**Data Ownership:** none

**Contracts:**
- Behavioral, inbound: `Scan(ReadOnlySpan<byte> chunk, ref PartialLineBuffer carry, Action<ReadOnlySpan<byte>> onLine) → void` — splits a byte chunk into complete lines using carry for partial data; every byte is either emitted as part of a complete line or stored in carry (SCAN-001); the span passed to `onLine` is only valid for the duration of that callback (SCAN-005)
- Behavioral, outbound: `onLine(ReadOnlySpan<byte> line)` callback — delivers a complete, delimiter-stripped UTF-8 line; the span is only valid for the duration of the callback and must not be retained (SCAN-005)
- Data, outbound: none
- Data, inbound: `PartialLineBuffer` from File State Management — carry buffer threaded through successive chunks; the span from `AsSpan()` is only valid until the next mutating call on the same buffer (FM-PLB-005)

**Dependency Direction:** depends on File State Management

---

### **Module 6: Log Parsing**

**Namespace:** `LogWatcher.Core.Processing.Parsing`

**Responsibility:** Parse UTF-8 log lines into structured records.

**In Scope:**

- ISO-8601 timestamp parsing
- Level mapping
- Message key extraction
- Latency extraction
- Malformed detection

**Out of Scope:**

- Line splitting
- Statistics accumulation
- Message key storage

**Why:** Encodes log format knowledge in one place. Changes to log format require changes here and nowhere else.

**Postulate Dimensions:**
Log format changes (timestamp format, level values, field names), new fields to extract, or parsing strictness changes.

**Data Ownership:**
- `ParsedLogLine` — structured log record (timestamp, level, message key span, optional latency); its schema changes when the log format changes, which is this module's Postulate
- `LogLevel` — enum mapping log level strings to internal values; its variants change when the set of recognized log levels changes, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `TryParse(ReadOnlySpan<byte> line, out ParsedLogLine parsed) → bool` — parses a UTF-8 line into a structured record; returns false only when timestamp parsing fails or required tokens are absent (PRS-001); the `MessageKey` span inside the returned `ParsedLogLine` is only valid for the duration of the enclosing `onLine` callback (PRS-004)
- Behavioral, outbound: none
- Data, outbound: `ParsedLogLine` to File Processing — structured record consumed inline during the `onLine` callback; `MessageKey` span is only valid for the enclosing callback duration (PRS-004)
- Data, outbound: `LogLevel` to File Processing — log-level enum read from `ParsedLogLine.Level` and translated to `StatLevel` before crossing the Statistics Collection boundary via `IncrementLevel`
- Data, inbound: none

**Dependency Direction:** none

---

### **Module 7: File Processing**

**Namespace:** `LogWatcher.Core.Processing`

**Responsibility:** Orchestrate tailer + scanner + parser for one file.

**In Scope:**

- Orchestration order
- Delegating to substages
- Passing stats through pipeline
- Updating file offset

**Out of Scope:**

- IO logic
- Scanning logic
- Parsing logic
- State transitions
- Worker coordination

**Why:** Provides a single orchestration point that ties together the IO, scanning, and parsing substages without
duplicating logic.

**Postulate Dimensions:**
Processing pipeline order changes, pre/post-processing hooks needed, or error handling strategy changes.

**Data Ownership:** none

**Contracts:**
- Behavioral, inbound: `ProcessOnce(string path, FileState state, WorkerStatsBuffer stats, int chunkSize) → void` — orchestrates one read-scan-parse cycle for the given file; must only be called while the caller holds `state.Gate` (PROC-006)
- Behavioral, outbound: calls `IFileTailer.ReadAppended(string path, ref long offset, Action<ReadOnlySpan<byte>> onChunk, out int totalBytesRead, int chunkSize) → TailReadStatus` — delegates IO to File Tailing; `onChunk` span valid only for callback duration (TAIL-005)
- Behavioral, outbound: calls `Utf8LineScanner.Scan(ReadOnlySpan<byte> chunk, ref PartialLineBuffer carry, Action<ReadOnlySpan<byte>> onLine) → void` — delegates line splitting to Line Scanning; `onLine` span valid only for callback duration (SCAN-005)
- Behavioral, outbound: calls `LogParser.TryParse(ReadOnlySpan<byte> line, out ParsedLogLine parsed) → bool` — delegates parsing to Log Parsing; `ParsedLogLine.MessageKey` span valid only for the enclosing `onLine` callback (PRS-004)
- Behavioral, outbound: calls `WorkerStatsBuffer.IncrementLevel(StatLevel level) → void` — increments the per-level counter after translating `ParsedLogLine.Level` from `LogLevel` to `StatLevel`
- Behavioral, outbound: calls `WorkerStatsBuffer.IncrementMessage(string key) → void` — increments the per-message-key frequency counter using the `MessageKey` span during the enclosing `onLine` callback
- Behavioral, outbound: calls `WorkerStatsBuffer.RecordLatency(int latencyMs) → void` — records a latency sample when the parsed record contains a latency value (STAT-005)
- Data, outbound: none
- Data, inbound: `FileState` from File State Management — per-file state providing offset (read/written) and carry buffer; `state.Gate` must be held by the caller before `ProcessOnce` is called (FM-007, PROC-006)
- Data, inbound: `PartialLineBuffer` from File State Management — carry buffer accessed via `FileState.Carry` and passed by `ref` to the scanner; span from `AsSpan()` valid only until next mutating call (FM-PLB-005)
- Data, inbound: `WorkerStatsBuffer` from Statistics Collection — per-worker accumulator written with counters and latency samples during processing
- Data, inbound: `TailReadStatus` from File Tailing — read outcome used to branch on errors and increment error counters
- Data, inbound: `ParsedLogLine` from Log Parsing — structured record consumed inline; `MessageKey` span valid only for enclosing callback duration (PRS-004)
- Data, inbound: `LogLevel` from Log Parsing — log-level value read from `ParsedLogLine.Level` and translated to `StatLevel` before crossing the Statistics Collection boundary
- Data, inbound: `StatLevel` from Statistics Collection — target type for the `LogLevel → StatLevel` translation performed before calling `IncrementLevel`

**Dependency Direction:** depends on File Tailing, Line Scanning, Log Parsing, File State Management, Statistics Collection

---

### **Module 8: Processing Coordination**

**Namespace:** `LogWatcher.Core.Processing`

**Responsibility:** Route events to files, enforce per-file serialization, coordinate stats swaps.

**In Scope:**

- Worker thread lifecycle
- Event routing (created/modified/deleted/renamed)
- Per-file gate acquisition
- Dirty flag + catch-up loop
- Delete-pending handling
- S2a swap acknowledgement point

**Out of Scope:**

- File IO
- State transitions
- Statistics

**Why:** Coordinates the entire processing pipeline: dequeuing events, routing to files, managing per-file locks, and
coalescing redundant work.

**Postulate Dimensions:**
Worker count policy changes, event routing rules change, dirty loop strategy changes (catch-up vs. drop), or swap ack
timing changes.

**Data Ownership:** none

**Contracts:**
- Behavioral, inbound: `Start() → void` — launches worker threads; worker count is fixed at construction time and never changes during the lifetime of the coordinator (PROC-007)
- Behavioral, inbound: `Stop() → void` — signals workers to drain remaining events and exit
- Behavioral, outbound: calls `bus.TryDequeue(out FsEvent item, int timeoutMs) → bool` — dequeues events from Event Distribution
- Behavioral, outbound: calls `registry.GetOrCreate(string path) → FileState` — gets or creates per-file state from File State Management
- Behavioral, outbound: calls `registry.TryGet(string path, out FileState state) → bool` — looks up file state without creating
- Behavioral, outbound: calls `registry.FinalizeDelete(string path) → void` — finalizes and removes state for a deleted file (FM-004)
- Behavioral, outbound: calls `processor.ProcessOnce(string path, FileState state, WorkerStatsBuffer stats, int chunkSize) → void` — delegates one processing cycle to File Processing; caller must hold `state.Gate` before this call (PROC-006)
- Behavioral, outbound: calls `workerStats.AcknowledgeSwapIfRequested() → void` — acknowledges a pending buffer swap to Worker Coordination at a safe point after fully handling one dequeued event (CD-004)
- Behavioral, outbound: calls `WorkerStatsBuffer.IncrementFsEvent(StatEventKind kind) → void` — increments the per-kind event counter after translating `FsEvent.Kind` from `FsEventKind` to `StatEventKind`
- Data, outbound: none
- Data, inbound: `FsEvent` from Ingestion — dequeued event used to determine routing action (created/modified/deleted/renamed)
- Data, inbound: `FsEventKind` from Ingestion — event-kind field read from `FsEvent` during routing and translated to `StatEventKind` before calling `IncrementFsEvent`
- Data, inbound: `BoundedEventBus<FsEvent>` from Event Distribution — the queue dequeued by worker threads
- Data, inbound: `FileState` from File State Management — per-file state used for gate acquisition, dirty-flag coalescing, and delete-pending detection; `state.Gate` must be held before reading or mutating `Offset` or `Carry` (FM-007)
- Data, inbound: `WorkerStats` from Worker Coordination — per-worker double-buffer coordination object; `AcknowledgeSwapIfRequested` is called on it at safe points
- Data, inbound: `WorkerStatsBuffer` from Statistics Collection — active buffer accessed via `WorkerStats.Active` and passed to `ProcessOnce`
- Data, inbound: `StatEventKind` from Statistics Collection — target type for the `FsEventKind → StatEventKind` translation performed before calling `IncrementFsEvent`

**Dependency Direction:** depends on Ingestion, Event Distribution, File State Management, File Processing, Worker Coordination, Statistics Collection

---

### **Module 9: Statistics Collection**

**Namespace:** `LogWatcher.Core.Statistics`

**Responsibility:** Per-worker per-interval metrics accumulation.

**In Scope:**

- Scalar counters
- Per-level counts
- Per-message frequency
- Latency distribution
- Top-K extraction
- Reset semantics

**Out of Scope:**

- Worker coordination
- Reporting
- File state

**Why:** Provides a single container for all metrics that workers accumulate during an interval, making them easy to
swap and merge.

**Postulate Dimensions:**
New counter needed (e.g., BytesProcessed), histogram bounds change, top-K algorithm changes, or reset semantics change.

**Data Ownership:**
- `WorkerStatsBuffer` — per-worker per-interval metrics accumulator (scalar counters, per-level counts, per-message frequency map, latency histogram); its schema changes when new metrics are needed or reset semantics change, which is this module's Postulate
- `LatencyHistogram` — latency distribution tracker (bins, overflow bucket, total count); its schema (bin bounds, overflow semantics) changes when histogram bounds change, which is this module's Postulate
- `TopK` — top-K message frequency computation utility; its algorithm and return contract change when top-K accumulation rules change, which is this module's Postulate
- `StatLevel` — statistics-internal enum of log-level buckets; its variants change when the set of distinct level categories tracked by the metrics system changes, which is this module's Postulate
- `StatEventKind` — statistics-internal enum of filesystem-event buckets; its variants change when the set of distinct event categories tracked by the metrics system changes, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `WorkerStatsBuffer.IncrementFsEvent(StatEventKind kind) → void` — increments the per-kind event counter; unrecognized kind is silently ignored (STAT-001)
- Behavioral, inbound: `WorkerStatsBuffer.IncrementLevel(StatLevel level) → void` — increments the per-level counter; unrecognized index is silently ignored (STAT-001)
- Behavioral, inbound: `WorkerStatsBuffer.IncrementMessage(string key) → void` — increments the per-message-key frequency counter
- Behavioral, inbound: `WorkerStatsBuffer.RecordLatency(int latencyMs) → void` — adds a latency sample to the histogram; values outside the supported range go to the overflow bucket, never discarded or thrown (STAT-005)
- Behavioral, inbound: `WorkerStatsBuffer.Reset() → void` — resets the buffer to observable zero state; callers must not assume anything about internal capacity or allocation state after reset (STAT-004)
- Behavioral, inbound: `LatencyHistogram.Percentile(double p) → int?` — computes a percentile; returns `null` when the histogram contains no data (STAT-006)
- Behavioral, outbound: none
- Data, outbound: `WorkerStatsBuffer` to File Processing, Worker Coordination, Reporting — per-worker accumulator written by processors, managed by Worker Coordination via swap, and read by Reporting after swap acknowledgement
- Data, outbound: `LatencyHistogram` to Reporting — latency distribution read during snapshot merge; `Percentile()` returns `null` when empty (STAT-006)
- Data, outbound: `TopK` to Reporting — top-K computation utility called during snapshot finalization
- Data, outbound: `StatLevel` to File Processing — target enum type for the `LogLevel → StatLevel` translation performed in `ProcessOnce` before calling `IncrementLevel`
- Data, outbound: `StatEventKind` to Processing Coordination — target enum type for the `FsEventKind → StatEventKind` translation performed in the worker routing loop before calling `IncrementFsEvent`
- Data, inbound: none

**Dependency Direction:** none

---

### **Module 10: Worker Coordination**

**Namespace:** `LogWatcher.Core.Coordination`

**Responsibility:** Double-buffer swap protocol with ack synchronization.

**In Scope:**

- Active/inactive buffer references
- Swap request/ack protocol
- Swap timing
- Ack signaling

**Out of Scope:**

- Buffer internals
- Merge logic
- Reporting

**Why:** Separates the synchronization protocol from the metrics definitions, allowing both to evolve independently.

**Postulate Dimensions:**
Swap protocol changes (double-buffer to triple-buffer), ack mechanism changes (ManualResetEventSlim to different
primitive), or swap timing changes (per-event to periodic).

**Data Ownership:**
- `WorkerStats` — double-buffer coordination object encapsulating the active/inactive buffer references and the swap-request/ack protocol; its schema changes when the swap protocol or ack mechanism changes, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `WorkerStats.RequestSwap() → void` — reporter signals that a buffer swap is requested; called by Reporting at the start of each report cycle
- Behavioral, inbound: `WorkerStats.WaitForSwapAck(CancellationToken ct) → void` — reporter blocks until the worker acknowledges the swap or the token is cancelled; the inactive buffer is only safe to read after this returns (CD-005)
- Behavioral, inbound: `WorkerStats.GetInactiveBufferForMerge() → WorkerStatsBuffer` — returns the inactive buffer for reading; only valid to call after `WaitForSwapAck` has returned (CD-005)
- Behavioral, inbound: `WorkerStats.AcknowledgeSwapIfRequested() → void` — worker acknowledges a pending swap after completing both the buffer swap and the reset of the new active buffer (CD-003, CD-004); called by Processing Coordination
- Behavioral, outbound: none
- Data, outbound: `WorkerStats` to Processing Coordination, Reporting, CLI & Host — the double-buffer coordination object whose active side workers write to and whose inactive side Reporting reads after swap acknowledgement
- Data, inbound: `WorkerStatsBuffer` from Statistics Collection — the buffer type wrapped in active/inactive pairs by `WorkerStats`; its schema is governed by Statistics Collection

**Dependency Direction:** depends on Statistics Collection

---

### **Module 11: Reporting**

**Namespace:** `LogWatcher.Core.Reporting`

**Responsibility:** Merge worker buffers into aggregated snapshot, compute derived metrics, print reports.

**In Scope:**

- Reporting interval loop
- Worker swap coordination
- Merge logic
- GC metrics
- Rate computation
- Top-K and percentile finalization
- Report formatting

**Out of Scope:**

- Worker threads
- Statistics definitions
- File processing

**Why:** Centralizes output logic so changing report format, interval, or metrics only affects this module.

**Postulate Dimensions:**
Report interval changes, output format changes (console to file to JSON), new GC metrics added, or rate computation
changes.

**Data Ownership:**
- `GlobalSnapshot` — aggregated cross-worker snapshot including merged counters, GC metrics, derived rates, top-K results, and computed percentiles; its schema changes when the report format or aggregated metrics change, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `Start() → void` — starts the reporting interval loop thread; the stopping flag is reset before launching so a stopped reporter can be restarted (RPT-006)
- Behavioral, inbound: `Stop() → void` — signals the loop to exit; guarantees at least one final report on shutdown (RPT-003); the stopping flag is always visible to the loop thread (RPT-005)
- Behavioral, outbound: `ISnapshotConsumer.OnSnapshot(GlobalSnapshot snapshot, TimeSpan elapsed) → void` callback — invoked on each report interval with the finalized snapshot; elapsed is the actual measured interval duration (RPT-001)
- Behavioral, outbound: calls `WorkerStats.RequestSwap() → void` — initiates a buffer swap for each worker at the start of each report cycle
- Behavioral, outbound: calls `WorkerStats.WaitForSwapAck(CancellationToken ct) → void` — waits for each worker's swap acknowledgement before reading the inactive buffer (CD-005); proceeds with available data and logs a warning if the ack times out (RPT-004)
- Behavioral, outbound: calls `WorkerStats.GetInactiveBufferForMerge() → WorkerStatsBuffer` — reads the inactive buffer only after swap acknowledgement has been received (CD-005); a worker whose ack was not received is excluded from the current snapshot (RPT-007)
- Behavioral, outbound: calls `LatencyHistogram.Percentile(double p) → int?` — computes P50, P95, and P99; `null` means no measurements were recorded (STAT-006)
- Data, outbound: `GlobalSnapshot` to CLI & Host — finalized snapshot delivered to `ISnapshotConsumer` implementations on each interval
- Data, inbound: `WorkerStats` from Worker Coordination — double-buffer coordination object; swap is requested and acknowledged before reading the inactive buffer
- Data, inbound: `WorkerStatsBuffer` from Statistics Collection — inactive buffer read after swap acknowledgement and merged into `GlobalSnapshot`; snapshot is reset before each merge so stale data from a prior interval is never included (RPT-002)
- Data, inbound: `LatencyHistogram` from Statistics Collection — latency distribution merged from each inactive buffer; `Percentile()` returns `null` when the histogram contains no data (STAT-006)
- Data, inbound: `BoundedEventBus<FsEvent>` from Event Distribution — `PublishedCount`, `DroppedCount`, and `Depth` are read each interval for bus metrics in the snapshot
- Data, inbound: `TopK` from Statistics Collection — called during snapshot finalization to compute top-K message frequencies from the merged message count map

**Dependency Direction:** depends on Worker Coordination, Statistics Collection, Event Distribution

---

### **Module 12: CLI & Host**

**Namespace:** `LogWatcher.App`

**Responsibility:** Argument parsing, dependency injection, component lifecycle.

**In Scope:**

- Argument parsing
- Configuration validation
- Dependency construction
- Startup/shutdown ordering
- Exit codes

**Out of Scope:**

- Business logic
- Component internals

**Why:** Keeps bootstrap logic separate from modules so the core system is testable independently of how it's wired up.

**Postulate Dimensions:**
Argument names or validation rules change, new configuration options added, component assembly order changes, or
shutdown sequence changes.

**Data Ownership:**
- `LogWatcherOptions` — parsed and validated CLI configuration record (watch path, worker count, queue capacity, report interval, top-K count); its schema changes when new CLI arguments are added or validation rules change, which is this module's Postulate

**Contracts:**
- Behavioral, inbound: `ISnapshotConsumer.OnSnapshot(GlobalSnapshot snapshot, TimeSpan elapsed) → void` — implemented here (`ConsoleSnapshotConsumer`); receives the finalized snapshot from Reporting on each interval for console output
- Behavioral, outbound: starts components in order: `ProcessingCoordinator.Start()`, then `Reporter.Start()`, then `FilesystemWatcherAdapter.Start()` — consumers are always ready before producers (HOST-003)
- Behavioral, outbound: stops components in order: watcher stop → bus stop → coordinator stop → reporter stop (HOST-001)
- Data, outbound: none (`LogWatcherOptions` is consumed internally and its values are unpacked into primitive constructor arguments; it does not cross into other modules as a shared type)
- Data, inbound: `GlobalSnapshot` from Reporting — received via `ISnapshotConsumer.OnSnapshot` for console formatting
- Data, inbound: `BoundedEventBus<FsEvent>` from Event Distribution — constructed here and passed to Ingestion, Processing Coordination, and Reporting
- Data, inbound: `WorkerStats` from Worker Coordination — constructed here as an array and passed to Processing Coordination and Reporting

**Dependency Direction:** depends on Ingestion, Event Distribution, File State Management, File Tailing, File Processing, Processing Coordination, Worker Coordination, Reporting

---

## **Dependency Graph (Allowed Flows)**

Data flows in one direction:

```
Ingestion → Events → Processing → Statistics → Coordination → Reporting
```

**Critical Rule:** No backward dependencies. Statistics never calls Processing. Reporting never calls Coordination.
