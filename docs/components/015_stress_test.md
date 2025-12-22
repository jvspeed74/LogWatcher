
## Component 15: Stress tests / harness (synthetic + IO) to validate to spec

### Purpose

Provide automated tests (or a runnable harness) that validate:

* bounded queue drop behavior under overload
* no deadlocks under concurrency
* file tailing correctness with concurrent writers
* delete/rename race handling does not crash and results in state cleanup
* R2 interval reporting remains meaningful under load (elapsed seconds printed)

### Two categories

---

### A) Synthetic publisher stress (no real FileSystemWatcher, no file IO)

This should be a test in `WatchStats.Tests` that runs quickly (a few seconds).

#### Steps

1. Create bus with small capacity to force drops (e.g., 100).
2. Create registry and coordinator with a **fake FileProcessor**:

    * Fake processor increments `LinesProcessed` and returns quickly.
    * It must be deterministic and thread-safe in its own minimal way.
3. Start coordinator with N workers (e.g., 4).
4. Spawn M publisher threads (e.g., 4–8) that publish events in a tight loop for ~2 seconds:

    * publish `Modified` events for a small set of paths (e.g., 10 paths) to force gate contention and dirty behavior.
5. Stop publishers, stop bus, stop coordinator.
6. Assertions:

    * `bus.DroppedCount > 0` (proves BP1 exercised)
    * coordinator stops without deadlock (test completes)
    * optional: “no concurrent processing per path” if fake processor can detect overlap by path (use a per-path `int inFlight` counter and assert it never exceeds 1)

---

### B) IO-based stress (temp directory, real file appends; optionally bypass FileSystemWatcher)

Prefer bypassing FileSystemWatcher for deterministic tests:

* you can publish `Modified` events yourself whenever you append.

#### Steps

1. Create temp directory.
2. Create coordinator with real `FileProcessor` and real `FileTailer`.
3. Start coordinator; do not start FileSystemWatcher.
4. Spawn writer threads:

    * each thread appends lines to its own `.log` file in the temp dir for 5–10 seconds:

        * line format matches parser (ISO-8601 + level + message + optional latency)
5. After each append batch, publish a `Modified` event for that file path into the bus.
6. Introduce races:

    * occasionally delete a file and publish `Deleted`
    * occasionally rename a file and publish `Renamed`
    * then recreate and continue writing
7. Stop writers, stop bus, stop coordinator.
8. Assertions:

    * `LinesProcessed` > 0
    * malformed lines count remains 0 if you generate valid timestamps
    * registry does not contain states for deleted files at end (optional: expose registry count or internal inspection)
    * no crash / no deadlock

---

### Optional: End-to-end smoke (FileSystemWatcher)

Keep this as a manual/dev smoke test rather than CI:

* FileSystemWatcher timing can be flaky in CI environments.

Manual steps:

1. Run tool on a directory.
2. Append to a `.log` file and observe:

    * lines/sec increases
    * top-K includes your message key
    * percentiles show values
3. Rename/delete while appending; verify tool continues and does not double-count indefinitely.

---

### Stress test data generation (valid ISO-8601)

When generating test log lines, use a stable format you know the parser accepts, e.g.:

* `DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)`

Message key (M2) should be constant-ish to make expected top-K predictable, e.g.:

* `"RequestCompleted"` or `"JobTick"`

Latency:

* generate values in 0..10,000
