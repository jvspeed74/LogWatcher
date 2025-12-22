## Definition of Done (mapped to spec outputs and counters)

### Purpose

Give an engineer an explicit checklist to confirm the implementation matches the requirements and that each subsystem behaves correctly under normal load, overload, and race conditions.

---

# A) Functional correctness checklist

## 1) Directory watching and event ingestion

* [ ] Watches exactly **one directory**, **non-recursive**.
* [ ] Emits and counts these event kinds:

    * [ ] create
    * [ ] modify
    * [ ] delete
    * [ ] rename
* [ ] Filters content processing to `.log` and `.txt` only.
* [ ] Publishes events into a bounded bus; no heavy work occurs in watcher callbacks.

Verification:

* Create/modify/delete/rename files in the watched directory and observe event counters move.

## 2) Event bus BP1 behavior (drop newest when full)

* [ ] Bus capacity is configurable; default = 10,000.
* [ ] When bus is full:

    * [ ] new events are dropped (not enqueued)
    * [ ] dropped counter increments
* [ ] Bus Stop unblocks all waiting consumers.

Verification:

* Synthetic stress test must show dropped count > 0 at small capacity.
* On shutdown, workers exit without hanging.

## 3) Worker processing and per-file serialization

* [ ] Worker count is configurable.
* [ ] Multiple workers process events concurrently.
* [ ] For any given path, only one worker processes it at a time (gate enforced).
* [ ] Different files can be processed in parallel.

Verification:

* Use fake processor with per-path “in-flight > 1” detection; must never happen.

## 4) Tailing semantics (default behavior)

* [ ] For a file, only appended bytes since last processed offset are read and processed.
* [ ] Offset is not reused across delete/recreate.
* [ ] Truncation (size < offset) resets offset to 0 and increments truncation counter.

Verification:

* Write N lines, process, append M lines, process: total lines == N+M.
* Truncate file, append new lines: tool processes new lines, not stuck at old offset.

## 5) Delete/rename races (policy B)

* [ ] If delete arrives while processing is in-flight:

    * [ ] deletePending is set
    * [ ] processing does not crash if file disappears mid-read
    * [ ] state is finalized (removed) by whichever worker acquires gate first (delete handler if it can, otherwise in-flight worker on exit)
    * [ ] tombstone epoch increments
* [ ] Rename is treated as delete(old) + create(new), path-based.

Verification:

* Append to a file, delete it while busy, ensure:

    * no crash
    * state removed count increments
* Rename active file; ensure new path begins fresh (offset=0).

## 6) Coalescing (dirty flag) behavior

* [ ] If modify arrives while file gate is held:

    * [ ] dirty is set (unless deletePending)
    * [ ] after releasing the gate, the in-flight worker loops and tail-reads again until dirty cleared
* [ ] This ensures “every appended byte eventually processed” despite event storms.

Verification:

* Artificially slow processing for one file and publish many modifies; verify eventual catch-up reads occur even if many events were skipped/coalesced.

## 7) Parsing correctness (strict ISO-8601, M2, L1)

* [ ] Line format:

    * `<timestamp> <level> <message> latency_ms=<int>`
* [ ] Timestamp parsing is strict ISO-8601; invalid timestamp => malformed line.
* [ ] Level unknown => `Other`, not malformed.
* [ ] Message key (M2) = first token of `<message>`.
* [ ] Latency (L1):

    * missing/malformed latency does not mark line malformed
    * latency stats updated only when parsed successfully

Verification:

* Feed known lines and check:

    * malformed increments only for bad timestamp (or missing tokens per your strictness)
    * latency missing doesn’t affect malformed

## 8) Rolling stats outputs

* [ ] Counts:

    * [ ] total lines processed
    * [ ] malformed lines
    * [ ] counts by level
    * [ ] top-K message keys (computed at report time from exact counts)
* [ ] Latency percentiles:

    * [ ] histogram range 0–10,000ms + overflow
    * [ ] p50/p95/p99 computed from merged histogram
    * [ ] overflow displayed as `>10000`

Verification:

* Use deterministic test data: known histogram distribution => percentiles match expected.

---

# B) Non-functional checklist (performance, robustness, measurability)

## 1) Allocation discipline (“zero-ish” intent)

* [ ] Hot path uses `ReadOnlySpan<byte>` for scanning and parsing.
* [ ] No per-line allocations from line splitting.
* [ ] Timestamp parsing may allocate (acceptable baseline), and message key may allocate (acceptable baseline).
* [ ] Reporting is allowed to allocate.

Verification:

* In a sustained run, reporting shows stable allocation deltas; no unbounded growth.

## 2) Bounded memory and backpressure

* [ ] Bus queue is bounded.
* [ ] Registry does not grow unbounded for deleted paths (states removed).
* [ ] Tombstone structure does not grow unbounded (optional eviction; if not implemented, document it).

Verification:

* Stress test for several minutes should show stable state count and bus depth bounded.

## 3) Robustness to malformed input and IO races

* [ ] Malformed lines never crash the tool.
* [ ] File deleted mid-read: benign; increments appropriate counters.
* [ ] Access denied/locked: benign; increments appropriate counters.

Verification:

* Create a file and lock it exclusively in another process; tool continues running.

## 4) Concurrency safety

* [ ] No deadlocks under stress:

    * many events
    * many workers
    * frequent delete/rename
* [ ] Gate acquisition is always paired with release (try/finally).

Verification:

* Run synthetic and IO stress tests; they must complete reliably.

## 5) Measurability and reporting (2s interval, R2)

* [ ] Reporter interval nominally 2 seconds.
* [ ] Because of S2a, reporting uses actual elapsed time since last swap (R2).
* [ ] Report prints elapsed seconds explicitly.
* [ ] Report includes:

    * [ ] events/sec and lines/sec
    * [ ] bus dropped and depth
    * [ ] top-K
    * [ ] p50/p95/p99
    * [ ] allocated bytes delta and GC collection deltas

Verification:

* Under heavy load, elapsed seconds may exceed 2.0; rates remain meaningful because they use elapsedSeconds.

---

# C) Minimal acceptance test script (manual)

Run tool in Release:

1. Start tool on a temp directory.
2. Create `app.log` and append 10 valid lines:

    * verify lines processed increases; top-K shows your message key.
3. Append 10 more lines quickly:

    * verify tailing (counts increase by ~10, not 20).
4. Rename file while appending:

    * verify old file stops being tracked and new file starts fresh.
5. Delete file while appending:

    * verify no crash, deletePending/fileStateRemoved counters increment.
6. Overload:

    * run a small script that triggers hundreds/thousands of rapid writes:

        * verify dropped event counter increases, tool still runs.

---

# D) Suggested “version 1.0 deliverable”

Engineer should produce:

* Console app
* Unit tests for:

    * line scanner
    * parser
    * histogram/percentiles
    * top-K
    * bus bounded behavior
    * registry delete/recreate semantics
* Stress tests (synthetic + IO) runnable via test project or a separate harness

