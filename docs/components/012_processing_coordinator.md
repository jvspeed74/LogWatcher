## Component 12: ProcessingCoordinator (workers, routing, per-file state machine, swap ack point)

### Purpose

Own the worker threads and implement the core runtime behavior:

* consume `FsEvent` from `BoundedEventBus<FsEvent>`
* maintain per-path correctness using:

    * per-file gate (`Monitor.TryEnter(state.Gate)`)
    * dirty flag (coalesce while busy)
    * delete-pending flag + finalize delete policy B
    * tombstone epochs via `FileStateRegistry.FinalizeDelete`
* process create/modify via `FileProcessor`
* keep per-worker active stats
* acknowledge stats swaps only after fully handling one dequeued event (S2a)

### Dependencies

* `BoundedEventBus<FsEvent> bus`
* `FileStateRegistry registry`
* `FileProcessor processor`
* `WorkerStats[] workerStats`
* config:

    * worker count
    * dequeue timeout (e.g., 100–250ms to allow shutdown responsiveness)

### Public contract

Create:

```csharp
public sealed class ProcessingCoordinator
{
    public ProcessingCoordinator(...);

    public void Start();
    public void Stop(); // requests stop and joins worker threads
}
```

### Step-by-step implementation

#### 1) Thread model

1. Allocate `Thread[] _threads` of size `workerCount`.
2. Each thread runs `WorkerLoop(workerIndex)`.
3. Keep `_stopping` as `volatile bool` or `CancellationTokenSource`.

#### 2) Worker loop structure (must match S2a)

Pseudo-structure (implement exactly this sequencing):

1. While not stopping:

    * Try dequeue one event:

        * `if (!bus.TryDequeue(out var ev, timeoutMs)) continue;`
    * Handle that single event completely:

        * Route based on kind (below)
    * After the event is fully handled (including any dirty catch-up loops):

        * `workerStats[workerIndex].AcknowledgeSwapIfRequested()`

Important: swap acknowledgement must not happen mid-event; do it once per dequeued event.

#### 3) Event routing rules

For each dequeued `FsEvent ev`:

1. Increment filesystem event counter in **active stats buffer** (even if unprocessable):

    * `stats.IncrementFsEvent(ev.Kind)`
2. Switch on `ev.Kind`:

    * `Created` / `Modified`:

        * if `ev.Processable` => `HandleCreateOrModify(ev.Path, stats)`
        * else: ignore for content
    * `Deleted`:

        * `HandleDelete(ev.Path, stats)`
    * `Renamed`:

        * `HandleDelete(ev.OldPath!, stats)`
        * then if new path is processable: `HandleCreateOrModify(ev.Path, stats)`

#### 4) HandleCreateOrModify(path) — gate + dirty loop

Implement:

1. `var state = registry.GetOrCreate(path);`
2. Attempt gate:

    * `if (!Monitor.TryEnter(state.Gate)) { state.MarkDirtyIfAllowed(); stats.CoalescedDueToBusyGate++; return; }`
3. Inside `try/finally` with `Monitor.Exit`:

    * If `state.IsDeletePending`:

        * `stats.SkippedDueToDeletePending++`
        * `registry.FinalizeDelete(path); stats.FileStateRemovedCount++; return;`
    * Else enter **catch-up loop**:

        * `while (true)`:

            1. If `state.IsDeletePending`:

                * finalize delete, increment counters, return
            2. `processor.ProcessOnce(path, state, stats);`
            3. If `state.IsDeletePending`:

                * finalize delete, increment counters, return
            4. If `state.IsDirty`:

                * clear dirty under gate (`state.ClearDirty()` via `Volatile.Write(ref _dirty, 0)` while holding gate)
                * continue (immediate tail re-read)
            5. break
4. Ensure you always release gate in finally.

This loop enforces “process every appended byte eventually” even if events arrived while busy.

#### 5) HandleDelete(path) — policy B

Implement:

1. If `!registry.TryGet(path, out var state)`:

    * return (still counts event)
2. Try acquire gate:

    * if cannot:

        * `state.MarkDeletePending(); stats.DeletePendingSetCount++;`
        * return
3. With gate held (try/finally):

    * `state.MarkDeletePending();`
    * `registry.FinalizeDelete(path); stats.FileStateRemovedCount++;`
    * return

This satisfies policy B: delete handler finalizes if it gets the gate; otherwise it just marks pending.

#### 6) Clearing flags

* Clearing dirty should happen only while holding gate.
* DeletePending should never be cleared; state is removed.

#### 7) Stop semantics

Implement `Stop()`:

1. Set `_stopping = true`
2. Call `bus.Stop()` (unblock dequeuers)
3. Join all worker threads
4. Optionally: attempt one final `AcknowledgeSwapIfRequested()` per worker (not required if reporter stops after coordinator)

### Coordinator unit tests (recommended)

Most coordinator logic is concurrency-sensitive; test with controlled fakes.

1. Use a fake `FileProcessor` that just increments a counter (no IO) to test state machine.
2. Test sequences:

    * Modify in-flight + delete arrives:

        * ensure deletePending set and finalize delete occurs
    * Many modify events while busy:

        * ensure dirty loop causes multiple processor calls without additional events needing to be processed
3. Concurrency stress (short):

    * two workers contending for same path; assert processor never runs concurrently for same path (use a gate-checked counter inside fake processor)

---

