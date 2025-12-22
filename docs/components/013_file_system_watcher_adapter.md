## Component 13: FilesystemWatcherAdapter (FileSystemWatcher integration and publishing)

### Purpose

Bridge OS filesystem events into internal `FsEvent` objects and publish them to the bus without blocking.

### Dependencies

* `string watchPath`
* `BoundedEventBus<FsEvent> bus`
* `Func<string, bool> isProcessable` or reuse shared helper
* Optional: internal counter for FileSystemWatcher internal buffer overflows (if you subscribe to Error event)

### Public contract

Create:

```csharp
public sealed class FilesystemWatcherAdapter : IDisposable
{
    public FilesystemWatcherAdapter(string path, BoundedEventBus<FsEvent> bus);

    public void Start();
    public void Stop();
}
```

### Step-by-step implementation

#### 1) Instantiate FileSystemWatcher

1. `_watcher = new FileSystemWatcher(watchPath)`
2. Configure:

    * `IncludeSubdirectories = false`
    * `NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size`
    * `Filter = "*.*"`
    * `InternalBufferSize`: set higher than default (e.g., 64KB) to reduce overflow risk (still bounded by OS limits)
3. Subscribe handlers:

    * `Created += OnCreated`
    * `Changed += OnChanged`
    * `Deleted += OnDeleted`
    * `Renamed += OnRenamed`
    * `Error += OnError` (optional but useful)

#### 2) Normalize events to FsEvent

For each handler:

1. Capture `observedAt = DateTimeOffset.UtcNow`
2. Determine `processable` using extension filter `.log/.txt`
3. Construct `FsEvent`:

    * For create/modify/delete: set `Path = e.FullPath`
    * For rename: set `OldPath = e.OldFullPath`, `Path = e.FullPath`
4. Publish:

    * `bus.Publish(fsEvent)`
    * do not retry on failure; BP1 says drop-newest is acceptable and counted by bus

Important:

* `Changed` can fire for directories and for temporary files. Your `.log/.txt` filter will exclude many of these.
* Do not do IO or heavy work in handlers.

#### 3) Start/Stop lifecycle

* `Start()` sets `_watcher.EnableRaisingEvents = true`
* `Stop()` sets it false and optionally drains nothing (best-effort)
* `Dispose()` unsubscribes and disposes watcher

#### 4) Error handling

* On watcher `Error`:

    * you may increment a counter in a thread-safe way for “watcher error events”
    * typical cause is internal buffer overflow; you can surface it in report later if desired
    * do not crash the process automatically

### Minimal integration tests

FileSystemWatcher tests are flaky on CI; keep them light and treat as smoke tests.

1. In a temp directory:

    * start adapter + coordinator + reporter disabled
    * create a `.log` file
    * append to it
    * assert bus published count increases
2. Keep timeouts generous (e.g., wait up to 1–2 seconds for event delivery).

---

Next response should cover:

* CLI Host wiring (Program.cs) and lifecycle (Ctrl+C shutdown ordering)
* Stress test harness (synthetic publisher + IO writer threads)

Those complete the runnable system and validate behavior end-to-end.
