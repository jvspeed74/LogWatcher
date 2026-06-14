# LogWatcher — Invariants

> Type definitions, classification rules, and the validation gauntlet: [`invariant_definition.md`](invariant_definition.md)

This document is the invariant registry for LogWatcher. Use it to drive test coverage decisions,
validate design changes, and reason about correctness during code review.

---

## Attribute Usage

Tag tests with `[Invariant("ID")]` to declare which invariant a test protects.
Multiple attributes are allowed when a test covers more than one invariant.

```csharp
[Fact]
[Invariant("BP-001")]
[Invariant("BP-002")]
public void Publish_WhenFull_DropsNewestAndPreservesExisting() { ... }
```

---

## Invariants

### Backpressure (BP)

| ID     | Type       | Modules | Description                                                                                                                                                              |
|--------|------------|---------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| BP-001 | `strict`   | BP      | The bus never holds more items than its configured capacity.                                                                                                             |
| BP-002 | `strict`   | BP      | When the bus is full, the incoming event is dropped. Already-queued events are never evicted.                                                                            |
| BP-003 | `strict`   | BP      | Dropped event count never decreases.                                                                                                                                     |
| BP-004 | `strict`   | BP      | Published event count never decreases.                                                                                                                                   |
| BP-005 | `strict`   | BP      | After `Stop()` is called no further items are enqueued. Blocked consumers are unblocked and may still drain remaining items already in the queue before returning false. |
| BP-006 | `contract` | BP, ING | Publishers never block waiting for queue capacity.                                                                                                                       |

---

### File Management (FM)

| ID     | Type       | Modules  | Description                                                                                                           |
|--------|------------|----------|-----------------------------------------------------------------------------------------------------------------------|
| FM-001 | `strict`   | FM       | A file's offset is never shared or reused across a delete and recreate of the same path.                              |
| FM-002 | `strict`   | FM       | Once `IsDeletePending` is set it is never cleared. The state is only removed.                                         |
| FM-003 | `strict`   | FM       | `IsDirty` cannot be set when `IsDeletePending` is true.                                                               |
| FM-004 | `strict`   | FM       | The carry buffer for a finalized path is released before the state is removed.                                        |
| FM-005 | `strict`   | FM       | `GetOrCreate` after `FinalizeDelete` always returns a new state with offset zero and empty carry.                     |
| FM-006 | `strict`   | FM       | The epoch for a path never decreases.                                                                                 |
| FM-007 | `contract` | FM, PROC | Callers must hold `state.Gate` before reading or mutating `Offset` or `Carry`.                                        |
| FM-008 | `strict`   | FM       | A state created for a path after finalization is always a newer generation than the state that was finalized.         |
| FM-009 | `strict`   | FM       | Two concurrent calls to `GetOrCreate` for the same path always return the same instance until that path is finalized. |

---

### Partial Line Buffer (FM-PLB)

| ID         | Type       | Modules  | Description                                                                                                                                                                 |
|------------|------------|----------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| FM-PLB-001 | `strict`   | FM       | `Length` always reflects the number of valid bytes in the buffer, never the allocated capacity of the underlying storage.                                                   |
| FM-PLB-002 | `strict`   | FM       | Bytes written to the buffer before a growth event are always readable and correct after it.                                                                                 |
| FM-PLB-003 | `strict`   | FM       | `Append` with an empty input is a no-op. `Length` and the underlying storage are unchanged.                                                                                 |
| FM-PLB-004 | `strict`   | FM       | `Clear()` makes the buffer appear empty to callers without releasing the underlying storage. `Release()` makes the buffer appear empty and releases the underlying storage. |
| FM-PLB-005 | `contract` | FM, SCAN, PROC | The span returned by `AsSpan()` is only valid until the next mutating call on the same buffer.                                                                              |

---

### Tailing (TAIL)

| ID       | Type         | Modules    | Description                                                                                                                |
|----------|--------------|------------|----------------------------------------------------------------------------------------------------------------------------|
| TAIL-001 | `strict`     | TAIL       | The caller's offset is never advanced when an IO error occurs.                                                             |
| TAIL-002 | `strict`     | TAIL       | When truncation is detected the read restarts from the beginning of the file before any bytes are delivered to the caller. |
| TAIL-003 | `strict`     | TAIL       | Allocated read buffers are always released regardless of read outcome.                                                     |
| TAIL-004 | `behavioral` | TAIL       | File not found, access denied, and IO errors are mapped to status codes and never propagated as exceptions to the caller.  |
| TAIL-005 | `contract`   | TAIL, PROC | The span passed to `onChunk` is only valid for the duration of the callback and must not be retained by the caller.        |
| TAIL-006 | `contract`   | TAIL, PROC | After a successful read, the caller's offset is advanced by exactly the number of bytes delivered. A subsequent call with that offset reads only bytes appended since the previous call and never re-delivers already-consumed bytes. |

---

### Scanning (SCAN)

| ID       | Type       | Modules    | Description                                                                                                               |
|----------|------------|------------|---------------------------------------------------------------------------------------------------------------------------|
| SCAN-001 | `strict`   | SCAN       | Every byte in the input is either emitted as part of a complete line or stored in carry. No bytes are silently discarded. |
| SCAN-002 | `strict`   | SCAN       | Emitted lines never include the `\n` delimiter.                                                                           |
| SCAN-003 | `strict`   | SCAN       | Emitted lines never include a trailing `\r` when the line was terminated by `\r\n`.                                       |
| SCAN-004 | `strict`   | SCAN       | Carry content from a previous chunk is always prepended to the next chunk before scanning resumes.                        |
| SCAN-005 | `contract` | SCAN, PROC | The span passed to `onLine` is only valid for the duration of the callback and must not be retained by the caller.        |

---

### Parsing (PRS)

| ID      | Type       | Modules   | Description                                                                                                                                            |
|---------|------------|-----------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| PRS-001 | `strict`   | PRS       | A line is only marked malformed when timestamp parsing fails or required tokens are absent. Missing or malformed latency never marks a line malformed. |
| PRS-002 | `strict`   | PRS       | The message key is always the first token of the message field, never a later token.                                                                   |
| PRS-003 | `strict`   | PRS       | Timestamp parsing uses strict ISO-8601 only. Ambiguous or partial timestamps are rejected.                                                             |
| PRS-004 | `contract` | PRS, PROC | The `MessageKey` span inside `ParsedLogLine` is only valid for the duration of the enclosing `onLine` callback.                                        |

---

### Processing (PROC)

| ID       | Type         | Modules  | Description                                                                                                                        |
|----------|--------------|----------|------------------------------------------------------------------------------------------------------------------------------------|
| PROC-001 | `strict`     | PROC, FM | At most one worker processes a given file path at any point in time.                                                               |
| PROC-002 | `strict`     | PROC, FM | When a worker cannot acquire the gate it sets the dirty flag instead of dropping the event silently.                               |
| PROC-003 | `strict`     | PROC, FM | A worker holding the gate re-reads the file until dirty is clear and delete is not pending before releasing the gate.              |
| PROC-004 | `strict`     | PROC, FM | When delete is observed under the gate the state is finalized before the gate is released.                                         |
| PROC-005 | `behavioral` | PROC, BP | Every byte appended to a watched file is eventually processed assuming events are not permanently suppressed by the OS.            |
| PROC-006 | `contract`   | PROC, FM | `ProcessOnce` is only called while the caller holds `state.Gate`.                                                                  |
| PROC-007 | `strict`     | PROC     | Worker count is fixed at construction time. Workers are never dynamically added or removed during the lifetime of the coordinator. |
| PROC-008 | `resource`   | PROC     | No heap objects are allocated per line in the scan (`Utf8LineScanner.Scan`) and parse (`LogParser.TryParse`) steps. Statistics accumulation is excluded from this guarantee. |
| PROC-009 | `strict`     | PROC     | The worker event loop catches all exceptions at its boundary — no exception propagated from a downstream module terminates the worker thread. |

---

### Statistics (STAT)

| ID       | Type       | Modules | Description                                                                                                                                             |
|----------|------------|---------|---------------------------------------------------------------------------------------------------------------------------------------------------------|
| STAT-001 | `strict`   | STAT      | Level counts are indexed by the integer value of `StatLevel`. An unrecognized index is silently ignored and never throws.                                |
| STAT-002 | `strict`   | STAT      | Histogram bin counts never decrease within a single buffer lifetime.                                                                                    |
| STAT-003 | `strict`   | STAT      | Histogram total count always equals the sum of all bin counts.                                                                                          |
| STAT-004 | `contract` | STAT, CD  | `Reset()` returns the buffer to an observable zero state. Callers must not assume anything about the internal capacity or allocation state after reset. |
| STAT-005 | `strict`   | STAT      | Latency values outside the supported range are mapped to an overflow bucket. No exception is thrown and no value is silently discarded.                 |
| STAT-006 | `contract` | STAT, RPT | `Percentile()` returns `null` when the histogram contains no data. Callers may rely on `null` to distinguish "no measurements recorded" from a zero-valued measurement. |

---

### Worker Coordination (CD)

| ID     | Type       | Modules | Description                                                                                                    |
|--------|------------|---------|----------------------------------------------------------------------------------------------------------------|
| CD-001 | `strict`   | CD      | After a swap, the previously active buffer becomes inactive and the previously inactive buffer becomes active. |
| CD-002 | `strict`   | CD      | The new active buffer is always reset immediately after a swap before the worker resumes writing.              |
| CD-003 | `strict`   | CD      | The swap acknowledgement is only set after both the swap and the reset are complete.                           |
| CD-004 | `strict`   | CD, PROC | Workers only acknowledge a swap at a safe point — after fully handling one dequeued event.                    |
| CD-005 | `contract` | CD, RPT | The reporter only reads the inactive buffer after receiving the swap acknowledgement.                          |

---

### Reporting (RPT)

| ID      | Type          | Modules   | Description                                                                                                              |
|---------|---------------|-----------|--------------------------------------------------------------------------------------------------------------------------|
| RPT-001 | `strict`      | RPT       | Rates are always computed using actual elapsed time, never an assumed interval duration.                                 |
| RPT-002 | `strict`      | RPT, STAT | The global snapshot is reset before each merge. Stale data from a prior interval is never included.                      |
| RPT-003 | `behavioral`  | RPT       | The reporter emits at least one final report on shutdown.                                                                |
| RPT-004 | `behavioral` | RPT, CD   | If a worker fails to acknowledge a swap within the timeout the reporter proceeds with available data and logs a warning. |
| RPT-005 | `strict`      | RPT       | Calling `Stop()` causes the reporter background thread to exit within a bounded time; the stopping flag written by `Stop()` is always visible to the loop thread. |
| RPT-006 | `strict`      | RPT       | `Start()` resets the stopping flag before launching the thread; a reporter that has been stopped can be restarted without hanging or skipping reports. |
| RPT-007 | `strict`      | RPT, CD   | A worker whose swap acknowledgement was not received before the timeout is excluded from the current interval's snapshot merge. Its inactive buffer is not read and its data does not contribute to the current snapshot. |

---

### Ingestion (ING)

| ID      | Type         | Modules | Description                                                                                                    |
|---------|--------------|---------|----------------------------------------------------------------------------------------------------------------|
| ING-001 | `strict`     | ING     | Watcher callbacks never perform IO, blocking operations, or heavy computation.                                 |
| ING-002 | `strict`     | ING, BP | A publish failure due to a full bus is silent to the watcher. The watcher never retries or blocks.             |
| ING-003 | `behavioral` | ING     | Only `.log` and `.txt` files are marked processable. Other extensions are published as non-processable events. |

---

### Host (HOST)

| ID       | Type       | Modules | Description                                                                                                     |
|----------|------------|---------|-----------------------------------------------------------------------------------------------------------------|
| HOST-001 | `strict`   | HOST    | Shutdown always occurs in order: watcher stop → bus stop → coordinator stop → reporter stop.                    |
| HOST-002 | `strict`   | HOST    | Shutdown is idempotent. Calling it multiple times produces no additional side effects.                          |
| HOST-003 | `strict`   | HOST    | Components are started in order: coordinator → reporter → watcher. Consumers are always ready before producers. |
