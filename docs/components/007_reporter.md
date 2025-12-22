## Merge snapshot structure definition (before Reporter)

### Purpose

Provide a clean, single object that represents “the merged view of all worker inactive buffers for one report.” The Reporter produces and prints from this object. Workers never touch it.

### Design goals

* Easy to merge into (summing counters, merging dictionaries, summing histograms)
* Self-contained enough that formatting is straightforward
* Supports R2 rates: snapshot stores totals; reporter computes rates using elapsed seconds

### Types to define

#### 1) `sealed class GlobalSnapshot`

Fields (mirror what you need to print and what you need to compute derived values):

1. Scalar counters (`long`):

* Filesystem event totals:

    * `FsCreated`, `FsModified`, `FsDeleted`, `FsRenamed`
* Pipeline totals:

    * `LinesProcessed`
    * `MalformedLines`
* Concurrency/state totals:

    * `CoalescedDueToBusyGate`
    * `DeletePendingSetCount`
    * `SkippedDueToDeletePending`
    * `FileStateRemovedCount`
* IO totals:

    * `FileNotFoundCount`
    * `AccessDeniedCount`
    * `IoExceptionCount`
    * `TruncationResetCount`
* Bus totals (these are not from workers, but Reporter will attach them):

    * `BusPublished`
    * `BusDropped`
    * `BusDepth` (instantaneous; not summed)

2. `long[] LevelCounts` sized to `LogLevel` enum length

3. `Dictionary<string, int> MessageCounts` (merged)

4. `LatencyHistogram Histogram` (merged)

5. Derived report outputs (computed after merge):

* `List<(string Key, int Count)> TopKMessages` (size <= K)
* `int? P50`, `int? P95`, `int? P99` (histogram-derived; overflow sentinel allowed)

Methods:

* `void ResetForNextMerge(int topK)`:

    * zero scalars
    * clear arrays
    * `MessageCounts.Clear()`
    * `Histogram.Reset()`
    * `TopKMessages.Clear()`
    * clear percentile fields

#### 2) `readonly record struct ReportFrame`

Represents what the reporter prints each interval, including timing and GC deltas.

Fields:

* `DateTimeOffset ReportedAt`
* `double ElapsedSeconds` (actual time since last successful swap)
* `GlobalSnapshot Snapshot` (or a reference to it)
* GC deltas:

    * `long AllocatedBytesDelta`
    * `int Gen0Delta`, `int Gen1Delta`, `int Gen2Delta`

Note: You can keep `ReportFrame` as a simple struct passed to a formatter.

---

## Merge rules (explicit)

Create a static merger function (keeps reporter clean):

### `static void MergeWorkerBufferInto(GlobalSnapshot snap, WorkerStatsBuffer buf)`

* Add scalar counters: `snap.X += buf.X`
* Add level counts: for i: `snap.LevelCounts[i] += buf.LevelCounts[i]`
* Merge message counts:

    * For each kvp in `buf.MessageCounts`:

        * `snap.MessageCounts[key] = snap.MessageCounts.GetValueOrDefault(key) + value`
* Merge histogram:

    * `snap.Histogram.MergeFrom(buf.Histogram)`

After all workers merged:

* `snap.TopKMessages = TopK.ComputeTopK(snap.MessageCounts, K)`
* `snap.P50 = snap.Histogram.Percentile(0.50)`
* `snap.P95 = snap.Histogram.Percentile(0.95)`
* `snap.P99 = snap.Histogram.Percentile(0.99)`

Bus metrics are attached after merging:

* `snap.BusPublished = bus.Published`
* `snap.BusDropped = bus.Dropped`
* `snap.BusDepth = bus.Depth` (if you track it)

---

# Component 7: Reporter (2s interval, swap/merge, GC deltas, R2 rates)

### Purpose (what this component must do)

Every ~2 seconds:

1. Request swap on all workers
2. Wait for all swap acknowledgements (S2a)
3. Merge all inactive buffers into a single GlobalSnapshot
4. Compute derived outputs (top-K, percentiles)
5. Compute rates using actual elapsed time (R2)
6. Capture GC deltas and print a report

### Inputs

* `WorkerStats[] workers`
* `BoundedEventBus<FsEvent> bus` (for dropped/published/depth)
* Config:

    * report interval = 2s
    * top-K value

### Step-by-step implementation

#### 1) Create `sealed class Reporter`

Fields:

* `WorkerStats[] _workers`
* `BoundedEventBus<FsEvent> _bus`
* `int _topK`
* `TimeSpan _interval = TimeSpan.FromSeconds(2)`
* `Thread _thread` or `Task` (use `Thread` to match style)
* `volatile bool _stopping`
* Timing:

    * `Stopwatch _sw = Stopwatch.StartNew()`
    * `long _lastTicks` (or store a `TimeSpan _lastElapsed`)
* GC baselines:

    * `long _lastAllocatedBytes`
    * `int _lastGen0`, `_lastGen1`, `_lastGen2`
* Snapshot instance reused:

    * `GlobalSnapshot _snapshot`

Initialization:

* `_lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false)`
* `_lastGenX = GC.CollectionCount(X)`

#### 2) Reporter loop logic

In the reporter thread:

1. Sleep/wait for `_interval`

    * simplest: `Thread.Sleep(_interval)`
    * better: `PeriodicTimer` (still standard library). If using threads only, `Thread.Sleep` is sufficient.
2. Compute elapsed seconds:

    * `nowTicks = _sw.ElapsedTicks`
    * `elapsedTicks = nowTicks - _lastTicks`
    * `elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency`
3. **Swap phase**

    * for each worker: `worker.RequestSwap()`
    * for each worker: `worker.WaitForSwapAck(...)`
    * Note: if stopping, you can break out after a final attempt.
4. **Merge phase**

    * `_snapshot.ResetForNextMerge(_topK)`
    * for each worker: merge `worker.Inactive` into snapshot using the merger function
    * attach bus counters:

        * `_snapshot.BusPublished = _bus.Published`
        * `_snapshot.BusDropped = _bus.Dropped`
        * `_snapshot.BusDepth = _bus.Depth` (if tracked)
    * compute derived outputs: top-K + percentiles
5. **GC delta phase**

    * `allocatedNow = GC.GetTotalAllocatedBytes(false)`
    * `allocatedDelta = allocatedNow - _lastAllocatedBytes`
    * `gen0Now = GC.CollectionCount(0)` etc.
    * deltas computed similarly
    * update baselines
6. **Format/print phase**
   Print a stable report with:

    * timestamp (UTC recommended)
    * elapsedSeconds (to explain drift)
    * rates:

        * fs events/sec: `(FsCreated+FsModified+FsDeleted+FsRenamed)/elapsedSeconds`
        * lines/sec: `LinesProcessed/elapsedSeconds`
    * totals (cumulative within the interval snapshot):

        * event counts by type
        * lines, malformed
    * top-K:

        * list `key: count`
    * percentiles:

        * format overflow sentinel `10001` as `>10000`
    * bus:

        * dropped, depth
    * GC:

        * allocatedDelta, gen deltas
7. Update `_lastTicks = nowTicks`

#### 3) Shutdown behavior

Expose:

* `void Start()`
* `void Stop()`
  In Stop:
* set `_stopping = true`
* join thread
  Optionally perform a final report:
* On stop request, do one final swap+merge and print final frame.

### Formatting requirements (keep it deterministic)

* Always print in the same order.
* Use invariant culture for numeric formatting.
* Show elapsedSeconds with 2 decimals so drift is visible.

### Reporter unit tests (recommended minimal)

Because timing and GC are hard to test deterministically:

* Make merge logic separately testable (it already is).
* For reporter, test:

    * “swap then merge called” via fakes/mocks is undesirable (no external libs).
      Instead:
* Factor out `BuildReportFrame(elapsedSeconds)` which takes:

    * worker inactive buffers
    * bus snapshot values
    * and returns a `ReportFrame`
      Then tests can validate:
* rates computed correctly
* percentiles/top-K computed correctly
* overflow formatting function works

---

Next response (per build order) would implement:

* Component 8: BoundedEventBus (drop-newest) with Stop semantics and depth tracking
* Component 9: FileStateRegistry (gate/dirty/delete-pending/tombstones)

(Those are tightly coupled and form the concurrency core.)
