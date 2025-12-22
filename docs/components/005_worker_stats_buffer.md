## Component 5: WorkerStatsBuffer (per-interval stats container)

### Purpose (what this component must do)

Represent “all metrics accumulated during one reporting interval” for a single worker, with:

* **single-threaded writes** (only the owning worker writes)
* **read-only during merge** (only reporter reads after swap)
* ability to **reset** quickly when it becomes the active buffer again
* fields required by spec: event counters, dropped/coalesced, lines, malformed, level counts, message counts, histogram

### Data model (what to store)

Create `sealed class WorkerStatsBuffer` with:

1. **Scalar counters** (`long`):

    * `FsCreated`, `FsModified`, `FsDeleted`, `FsRenamed` (received by worker)
    * `LinesProcessed`
    * `MalformedLines`
    * `CoalescedDueToBusyGate` (dirty set because gate unavailable)
    * `DeletePendingSetCount`
    * `SkippedDueToDeletePending`
    * `FileStateRemovedCount`
    * IO counters:

        * `FileNotFoundCount`
        * `AccessDeniedCount`
        * `IoExceptionCount`
        * `TruncationResetCount`
2. **Level counts**:

    * `long[] LevelCounts` sized to `LogLevel` enum length
3. **Message counts**:

    * `Dictionary<string, int> MessageCounts`
4. **Latency**:

    * `LatencyHistogram Histogram`

### Reset contract (critical for S2)

Implement `Reset()` such that:

* all scalar counters = 0
* `Array.Clear(LevelCounts)`
* `MessageCounts.Clear()` (does not shrink capacity—desired for reuse)
* `Histogram.Reset()`

### Merge contract

You will merge many worker inactive buffers into a global snapshot. Provide either:

* `void MergeInto(GlobalSnapshot snapshot)` (buffer pushes into snapshot), or
* keep merge logic in reporter (snapshot pulls from buffer)

Recommendation: **keep merge in reporter** for clarity; buffer remains “dumb storage.”

### Step-by-step implementation

1. Define the fields and initialize in constructor:

    * `LevelCounts = new long[EnumCount]`
    * `MessageCounts = new Dictionary<string,int>(initialCapacity)`
    * `Histogram = new LatencyHistogram()`
2. Implement `Reset()` exactly per contract.
3. Add minimal “update helpers” that reduce call-site mistakes:

    * `IncrementFsEvent(FsEventKind kind)`
    * `IncrementLevel(LogLevel level)`
    * `IncrementMessage(string key)` (use `TryGetValue` then update)
    * `RecordLatency(int latencyMs)` => Histogram.Add
      These helpers are optional but reduce bugs.

### Tests (xUnit)

Create `WorkerStatsBufferTests`:

1. `Reset_ClearsScalarsArraysAndCollections`
2. `MessageCounts_AccumulatesCorrectly`
3. `Histogram_AccumulatesAndResets`
