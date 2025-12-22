
## Component 6: WorkerStats (double-buffer swapping, S2a)

### Purpose (what this component must do)

Provide per-worker double-buffered stats so that:

* workers write only to **active buffer**
* reporter merges only from **inactive buffer**
* swap occurs only at safe points (after handling a single dequeued event) per S2a
* reporter waits for all workers to acknowledge swap before merging (R2 semantics)

### Public contract

Create `sealed class WorkerStats` that owns:

* `WorkerStatsBuffer Active` (worker writes)
* `WorkerStatsBuffer Inactive` (reporter reads after swap)
* a swap request mechanism
* an ack mechanism

Reporter-facing methods:

* `void RequestSwap()`
* `void WaitForSwapAck(CancellationToken ct)` (or timeout-based)
* `WorkerStatsBuffer GetInactiveBufferForMerge()` (should return the inactive reference; reporter reads only after ack)

Worker-facing method:

* `void AcknowledgeSwapIfRequested()` (called at end of each event handling)

### Synchronization requirements

* RequestSwap must be safe when called concurrently with worker updates.
* Worker must observe swap requests without heavy locking.
* Reporter must not proceed to merge until each worker has acked.

### Recommended mechanism in .NET standard library

Use:

* `int _swapRequested` as a 0/1 flag with `Volatile.Read/Write` or `Interlocked.Exchange`
* `ManualResetEventSlim _swapAck` per worker

### Step-by-step implementation

1. Initialize:

    * create two buffers: `_a`, `_b`
    * set `_active = _a`, `_inactive = _b`
    * `_swapRequested = 0`
    * `_swapAck = new ManualResetEventSlim(true)` (initially “acknowledged”)
2. Implement `RequestSwap()` (reporter thread):

    * `_swapAck.Reset()` (indicate swap pending)
    * `Volatile.Write(ref _swapRequested, 1)`
3. Implement `AcknowledgeSwapIfRequested()` (worker thread; S2a point):

    * if `Volatile.Read(_swapRequested) == 0` return
    * swap references:

        * temp = _active; _active = _inactive; _inactive = temp
    * reset new active buffer:

        * `_active.Reset()`
    * clear request:

        * `Volatile.Write(_swapRequested, 0)`
    * set ack:

        * `_swapAck.Set()`
4. Implement `WaitForSwapAck(...)`:

    * reporter waits on `_swapAck.Wait(...)`
    * for shutdown, allow cancellation or timeout; on timeout print note or proceed best-effort (spec says S2a/R2, so normally wait)
5. Expose `Active` to worker:

    * `WorkerStatsBuffer Active => _active;`
6. Expose `Inactive` to reporter *only after ack*:

    * `WorkerStatsBuffer Inactive => _inactive;` (reporter uses it after ack)

### Tests (xUnit)

Create `WorkerStatsSwapTests`:

1. `Swap_MovesWrittenDataToInactiveAndResetsActive`
2. `NoSwapRequest_DoesNothing`
3. `RequestSwap_ThenAck_SetsAckEvent`
4. `MultipleSequentialSwaps_WorkCorrectly`

Test strategy:

* simulate worker writing to `Active`
* call `RequestSwap`
* call `AcknowledgeSwapIfRequested`
* assert:

    * inactive contains pre-swap counts
    * active reset to zero
