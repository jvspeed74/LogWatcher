
## Component 9: FileStateRegistry + FileState (gate, dirty, delete-pending, tombstone epoch)

### Purpose

Maintain per-path state needed for tailing and correct concurrency semantics:

* tail offset and partial line carryover
* gate to enforce single in-flight processing per path
* flags: dirty (coalesce) and delete-pending (terminal)
* tombstone epoch per path to prevent stale state reuse after delete/rename

### Types to implement

#### 1) `sealed class FileState`

Fields:

* `public readonly object Gate = new object();` (per-path lock)
* `public long Offset;` (only mutated under Gate)
* `public PartialLineBuffer Carry;` (only mutated under Gate)
* `private int _dirty;` (0/1) — set without taking Gate, read under Gate
* `private int _deletePending;` (0/1) — set without taking Gate, read under Gate
* `public int Generation;` (optional; derived from epoch+1 at creation)

Expose helpers:

* `bool IsDirty => Volatile.Read(ref _dirty) == 1;`
* `bool IsDeletePending => Volatile.Read(ref _deletePending) == 1;`
* `void MarkDirtyIfAllowed()`

    * if deletePending already set, do nothing
    * else `Volatile.Write(ref _dirty, 1)`
* `void ClearDirty()` (under Gate in practice)
* `void MarkDeletePending()`

    * `Volatile.Write(ref _deletePending, 1)`
    * `Volatile.Write(ref _dirty, 0)` (override dirty)

Why flags are atomic:

* a worker that can’t acquire the gate needs to set dirty/deletePending safely.

#### 2) `sealed class FileStateRegistry`

Fields:

* `ConcurrentDictionary<string, FileState> _states`
* `ConcurrentDictionary<string, int> _epochs` (tombstones)
* Optional: `ConcurrentDictionary<string, long> _epochUpdatedAtTicks` for eviction (skip initially)

Methods:

* `FileState GetOrCreate(string path)`
* `bool TryGet(string path, out FileState state)`
* `bool TryRemove(string path, out FileState removed)` (internal helper)
* `void FinalizeDelete(string path)`
* `int GetCurrentEpoch(string path)` (optional for debugging)

### Step-by-step implementation

1. **GetOrCreate(path)**

    * Use `ConcurrentDictionary.GetOrAdd` with a factory:

        * compute `epoch = _epochs.TryGetValue(path, out e) ? e : 0`
        * create `new FileState { Offset = 0, Carry = new PartialLineBuffer(), Generation = epoch + 1 }`
    * Important:

        * Do not reuse carryover or offset across creations.
        * If a delete is in-flight and state still exists, GetOrCreate will return existing state; this is acceptable because deletePending will block processing and deletion will remove it.

2. **TryGet(path)**

    * Simple `_states.TryGetValue(path, out state)`

3. **FinalizeDelete(path)**

    * Remove state:

        * `_states.TryRemove(path, out var removed)`
    * Increment epoch:

        * `_epochs.AddOrUpdate(path, 1, (_, old) => old + 1)`
    * Optional memory hygiene:

        * if `removed != null`, clear its carry buffer to allow GC (not required; it will be collected)

4. **Mark flags when gate is busy**

    * You don’t necessarily need registry methods for this; workers can call:

        * `state.MarkDirtyIfAllowed()`
        * `state.MarkDeletePending()`
    * For delete events, you must locate the state first:

        * if `TryGet` fails: nothing to mark

### Correctness rules the registry must support

* After `FinalizeDelete`, the next `GetOrCreate` yields:

    * `Offset = 0`
    * empty carryover
    * deletePending cleared
* deletePending is idempotent and overrides dirty

### Tests (xUnit)

Create `FileStateRegistryTests`:

1. `FinalizeDelete_RemovesStateAndIncrementsEpoch`

    * create state, set offset/carry, finalize delete, create again; assert offset=0 and carry empty; generation incremented
2. `MarkDeletePending_ClearsDirty`
3. `MarkDirty_DoesNotSetWhenDeletePending`
4. Concurrency test (light):

    * multiple threads calling GetOrCreate for same path yields same instance until deleted

### Notes for later components

* The ProcessingCoordinator will:

    * call `Monitor.TryEnter(state.Gate)` for in-flight control
    * set `Dirty` / `DeletePending` without the gate when try-enter fails
    * perform `FinalizeDelete` while holding the gate (policy B), or set deletePending if gate unavailable

---

Next response (per build order) should cover:

* FileTailer (append-only chunk reading with truncation handling)
* FileProcessor (tail → scan → parse → update stats)

Those are tightly linked because the tailer’s API shape determines how you drive the line scanner without extra allocations.
