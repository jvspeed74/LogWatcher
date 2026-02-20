# Domain Boundaries Reference

This document defines the 12 domains of LogWatcher, their boundaries, and responsibilities. Use this when deciding where
code belongs.

---

## **Quick Reference: 12 Domains → 7 Namespaces**

| #  | Domain                  | Namespace                             | Responsibility                         |
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

## **Domain Details**

### **Domain 1: Ingestion**

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

**Why:** Isolates OS-level concerns (FileSystemWatcher API, timing, paths) from business logic. This domain owns the OS
boundary.

**Reason to Change:**
FileSystemWatcher API changes, new file extensions to monitor, or different event source (e.g., polling instead of
notifications).

---

### **Domain 2: Event Distribution**

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

**Reason to Change:**
Queue semantics change (drop-oldest vs. drop-newest), capacity strategy becomes adaptive, or synchronization primitive
changes (Monitor to lock-free channel).

---

### **Domain 3: File State Management**

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

**Reason to Change:**
New per-file metadata needed (e.g., line count, last modified time), tombstone strategy changes, or carry buffer
strategy changes (fixed size vs. exponential growth).

---

### **Domain 4: File Tailing**

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

**Reason to Change:**
Chunk size needs tuning, file sharing flags change (OS-specific behavior), or retry logic added for transient failures.

---

### **Domain 5: Line Scanning**

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

**Reason to Change:**
Support different line delimiters (e.g., NUL-terminated), change carryover strategy (buffer management), or add line
length validation.

---

### **Domain 6: Log Parsing**

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

**Reason to Change:**
Log format changes (timestamp format, level values, field names), new fields to extract, or parsing strictness changes.

---

### **Domain 7: File Processing**

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

**Reason to Change:**
Processing pipeline order changes, pre/post-processing hooks needed, or error handling strategy changes.

---

### **Domain 8: Processing Coordination**

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

**Reason to Change:**
Worker count policy changes, event routing rules change, dirty loop strategy changes (catch-up vs. drop), or swap ack
timing changes.

---

### **Domain 9: Statistics Collection**

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

**Reason to Change:**
New counter needed (e.g., BytesProcessed), histogram bounds change, top-K algorithm changes, or reset semantics change.

---

### **Domain 10: Worker Coordination**

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

**Reason to Change:**
Swap protocol changes (double-buffer to triple-buffer), ack mechanism changes (ManualResetEventSlim to different
primitive), or swap timing changes (per-event to periodic).

---

### **Domain 11: Reporting**

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

**Why:** Centralizes output logic so changing report format, interval, or metrics only affects this domain.

**Reason to Change:**
Report interval changes, output format changes (console to file to JSON), new GC metrics added, or rate computation
changes.

---

### **Domain 12: CLI & Host**

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

**Why:** Keeps bootstrap logic separate from domains so the core system is testable independently of how it's wired up.

**Reason to Change:**
Argument names or validation rules change, new configuration options added, component assembly order changes, or
shutdown sequence changes.

---

## **Dependency Graph (Allowed Flows)**

Data flows in one direction:

```
Ingestion → Events → Processing → Statistics → Coordination → Reporting
```

**Critical Rule:** No backward dependencies. Statistics never calls Processing. Reporting never calls Coordination.
