## Component 8: BoundedEventBus (drop-newest, stop semantics, depth tracking)

### Purpose (what this component must do)

Provide a bounded in-memory queue for `FsEvent` that:

* supports multiple producers (filesystem watcher + tests)
* supports multiple consumers (N worker threads)
* drops **newest** events when full (BP1), incrementing a dropped counter
* supports clean shutdown (`Stop`) that unblocks waiting consumers
* exposes metrics for the reporter:

    * published count (accepted)
    * dropped count
    * approximate depth (queue size)

### Implementation approach (standard library only)

Use `Queue<T>` protected by `lock` + `Monitor.Wait/Pulse`. This is predictable and testable.

### Public contract

Create `sealed class BoundedEventBus<T>` with:

* Constructor:

    * `BoundedEventBus(int capacity)`
* Producer:

    * `bool Publish(T item)`
      Returns `true` if enqueued; `false` if dropped or stopped.
* Consumer:

    * `bool TryDequeue(out T item, int timeoutMs)`
      Returns `true` if an item was dequeued; `false` on timeout or stop with empty queue.
* Shutdown:

    * `void Stop()`
* Metrics (thread-safe reads):

    * `long PublishedCount { get; }`
    * `long DroppedCount { get; }`
    * `int Depth { get; }`

### Step-by-step implementation

1. **Fields**

    * `readonly int _capacity;`
    * `readonly Queue<T> _queue = new();`
    * `readonly object _lock = new();`
    * `bool _stopped;`
    * `long _published;`
    * `long _dropped;`

2. **Publish (drop-newest)**

    * `lock (_lock)`:

        * if `_stopped` => return false
        * if `_queue.Count >= _capacity`:

            * `_dropped++`
            * return false
        * `_queue.Enqueue(item)`
        * `_published++`
        * `Monitor.Pulse(_lock)` to wake one waiting consumer
        * return true

3. **TryDequeue**

    * `lock (_lock)`:

        * if queue has item: dequeue and return true
        * else if stopped: return false
        * else wait up to `timeoutMs`:

            * use a loop:

                * `Monitor.Wait(_lock, timeoutMs)`
                * if item exists: dequeue
                * if stopped: return false
                * else if timed out: return false
                  Notes:
    * Use a loop to avoid spurious wakeups.

4. **Stop**

    * `lock (_lock)`:

        * `_stopped = true`
        * `Monitor.PulseAll(_lock)` to unblock consumers

5. **Metrics**

    * For `PublishedCount` and `DroppedCount`:

        * either read under lock or store using `Interlocked` increments and read without lock.
        * Simplicity: read under lock (cheap).
    * `Depth` should read under lock.

### Tests (xUnit)

Create `BoundedEventBusTests`:

1. `Publish_DropsWhenFull`

    * capacity 2; publish 3; assert dropped=1, published=2, depth=2
2. `TryDequeue_ReturnsItemsInFifoOrder`
3. `Stop_UnblocksDequeueAndReturnsFalseWhenEmpty`
4. `MultiProducer_DoesNotCorruptQueue`

    * start several threads publishing; consume; assert counts consistent
5. `MultiConsumer_ConsumesAllPublishedItems`

    * publish N; start M consumers; ensure total dequeued == N

### Notes for spec compliance

* Dropped events are expected under overload. Your reporter must display dropped count.
* It is acceptable that Stop causes some events in queue to be unprocessed (shutdown behavior); print final summary anyway.
