# Concurrency Model

This document provides detailed Mermaid diagrams for every thread, synchronization primitive, and
state machine in LogWatcher. For the authoritative component descriptions see
[technical_specification.md](technical_specification.md), and for behavioural guarantees see
[invariants.md](invariants.md).

---

## 1 — Thread Overview

Four categories of threads cooperate to process log files without blocking any producer.

```mermaid
graph TD
    subgraph "OS / Runtime"
        WT["FileSystemWatcher threads\n(OS-managed, N ≥ 1)"]
    end

    subgraph "Application threads"
        MT["Main thread\n(ApplicationHost)"]
        W0["Worker thread ws-0\n(IsBackground=true)"]
        W1["Worker thread ws-1\n(IsBackground=true)"]
        WN["Worker thread ws-N\n(IsBackground=true)"]
        RT["Reporter thread\n(IsBackground=true, name='reporter')"]
    end

    WT -- "Publish FsEvent (non-blocking)" --> BUS["BoundedEventBus&lt;FsEvent&gt;\n(Channel, bounded)"]
    BUS -- "TryDequeue (timeout)" --> W0
    BUS -- "TryDequeue (timeout)" --> W1
    BUS -- "TryDequeue (timeout)" --> WN

    W0 -- "AcknowledgeSwapIfRequested" --> WS0["WorkerStats[0]"]
    W1 -- "AcknowledgeSwapIfRequested" --> WS1["WorkerStats[1]"]
    WN -- "AcknowledgeSwapIfRequested" --> WSN["WorkerStats[N]"]

    RT -- "RequestSwap + WaitForSwapAck" --> WS0
    RT -- "RequestSwap + WaitForSwapAck" --> WS1
    RT -- "RequestSwap + WaitForSwapAck" --> WSN
    RT -- "GetInactiveBufferForMerge" --> WS0
    RT -- "GetInactiveBufferForMerge" --> WS1
    RT -- "GetInactiveBufferForMerge" --> WSN

    MT -- "Start / Stop" --> W0
    MT -- "Start / Stop" --> RT
    MT -- "EnableRaisingEvents" --> WT
```

### Startup and shutdown order

Startup and shutdown follow strict orderings (invariants **HOST-001** / **HOST-003**).

```mermaid
sequenceDiagram
    participant MT as Main thread
    participant C  as ProcessingCoordinator
    participant R  as Reporter
    participant W  as FilesystemWatcherAdapter

    Note over MT: ApplicationHost.Run()
    MT->>C: coordinator.Start()  — workers now dequeuing
    MT->>R: reporter.Start()     — reporter timer running
    MT->>W: watcher.Start()      — OS events enabled
    MT->>MT: WaitForShutdown()   — blocks on ManualResetEventSlim

    Note over MT: Ctrl+C / ProcessExit (idempotent via Interlocked.Exchange)
    MT->>W: watcher.Stop()       — disable OS events
    MT->>MT: bus.Stop()          — TryComplete channel
    MT->>C: coordinator.Stop()   — signal + Join(2 s) + Interrupt
    MT->>R: reporter.Stop()      — dispose timer + Join(2 s)
    MT->>MT: _shutdownEvent.Set() — unblock WaitForShutdown
```

---

## 2 — BoundedEventBus State Machine

The bus wraps a `Channel<T>` with drop-newest backpressure. It is the only shared data
structure touched by both watcher threads and worker threads.

```mermaid
stateDiagram-v2
    direction LR

    [*] --> Running : new BoundedEventBus(capacity)

    Running --> Running      : Publish — TryWrite succeeds\n_published++
    Running --> Running      : Publish — TryWrite fails (full)\n_dropped++
    Running --> Running      : TryDequeue — item available\nreturn true
    Running --> Running      : TryDequeue — timeout elapsed\nreturn false
    Running --> Draining     : Stop() — Writer.TryComplete()

    Draining --> Draining    : TryDequeue — item available\nreturn true
    Draining --> Stopped     : TryDequeue — channel empty + completed\nreturn false

    Stopped --> Stopped      : Publish — dropped (stopped flag)\n_dropped++
```

**Key invariants**: BP-001 (capacity never exceeded), BP-002 (drop-newest), BP-005 (consumers drain before stop), BP-006 (publishers never block).

---

## 3 — Worker Thread Lifecycle

Each worker (`ws-0 … ws-N`) runs the same loop. The only differences are which `WorkerStats`
slot it owns.

```mermaid
stateDiagram-v2
    direction TB

    [*]        --> Dequeuing   : thread.Start()

    Dequeuing  --> AckSwap1   : timeout elapsed (no event)
    AckSwap1   --> Dequeuing  : _stopping=false
    AckSwap1   --> Exiting    : _stopping=true

    Dequeuing  --> Routing    : FsEvent dequeued\nIncrementFsEvent(kind)

    Routing    --> GateAttempt : Created / Modified\n(Processable=true)
    Routing    --> DeletePath  : Deleted
    Routing    --> RenamePath  : Renamed

    GateAttempt --> BusyGate   : Monitor.TryEnter → false
    BusyGate    --> AckSwap2   : MarkDirtyIfAllowed()\nCoalescedDueToBusyGate++

    GateAttempt --> CatchUpLoop : Monitor.TryEnter → true

    CatchUpLoop --> AckSwap3   : AcknowledgeSwapIfRequested
    AckSwap3    --> CheckDelete : (continues)
    CheckDelete --> Finalize   : IsDeletePending=true
    CheckDelete --> Processing : IsDeletePending=false
    Processing  --> AckSwap4   : ProcessOnce complete
    AckSwap4    --> DirtyCheck : (continues)
    DirtyCheck  --> AckSwap3   : IsDirty=true → ClearDirty, re-loop
    DirtyCheck  --> ReleaseGate: IsDirty=false

    Finalize    --> ReleaseGate : FinalizeDelete()
    ReleaseGate --> AckSwap2   : Monitor.Exit(gate)

    DeletePath  --> AckSwap2   : HandleDelete complete
    RenamePath  --> AckSwap2   : HandleDelete(old) + HandleCreateOrModify(new)

    AckSwap2    --> Dequeuing  : _stopping=false
    AckSwap2    --> Exiting    : _stopping=true

    Exiting     --> [*]
```

---

## 4 — Per-File Gate and Dirty-Flag Protocol

Each `FileState` owns a plain `object Gate`. `Monitor.TryEnter` provides non-blocking
mutual exclusion. If the gate is busy the worker sets `IsDirty` and moves on; the gate-holder
loops until dirty is clear before releasing (**PROC-001 … PROC-003**).

```mermaid
sequenceDiagram
    participant W0  as Worker ws-0 (gate holder)
    participant W1  as Worker ws-1 (gate contender)
    participant FS  as FileState

    W0->>FS: Monitor.TryEnter(Gate) → true
    activate W0

    W1->>FS: Monitor.TryEnter(Gate) → false
    W1->>FS: MarkDirtyIfAllowed()   — Volatile.Write(_dirty=1)
    Note right of W1: W1 increments CoalescedDueToBusyGate and moves on

    loop Catch-up while IsDirty or more events
        W0->>W0: AcknowledgeSwapIfRequested()
        W0->>W0: processor.ProcessOnce(path, state, stats)
        W0->>W0: AcknowledgeSwapIfRequested()
        W0->>FS: IsDirty? → true  →  ClearDirty()
    end
    Note over W0: dirty=false, loop exits

    W0->>FS: Monitor.Exit(Gate)
    deactivate W0
```

---

## 5 — FileState Lifecycle State Machine

```mermaid
stateDiagram-v2
    direction LR

    [*]             --> Active        : registry.GetOrCreate\nOffset=0, generation=epoch+1

    Active          --> Active        : ProcessOnce\nOffset advances (monotonic ↑)
    Active          --> Dirty         : MarkDirtyIfAllowed()\n(called without gate)
    Dirty           --> Active        : ClearDirty()\n(called inside gate before re-loop)
    Active          --> DeletePending : MarkDeletePending()\n_deletePending=1, _dirty=0
    Dirty           --> DeletePending : MarkDeletePending()\n(clears dirty simultaneously)

    DeletePending   --> Finalized     : FinalizeDelete()\n(called inside gate)
    Finalized       --> [*]           : state removed from registry\nepoch bumped

    note right of DeletePending : IsDeletePending is permanent.\nCannot return to Active or Dirty.\n(FM-002, FM-003)
    note right of Finalized     : ClearCarry() releases buffer.\nNew GetOrCreate returns fresh state\nwith Offset=0. (FM-001, FM-005)
```

**Field transitions**:

| Field | Direction | Guard |
|---|---|---|
| `Offset` | 0 → ∞ (monotonic) | Must hold Gate |
| `IsDirty` | false ↔ true | Read: Volatile; Write: without gate OK |
| `IsDeletePending` | false → true (irreversible) | Write: without gate OK; see FM-002 |

---

## 6 — Double-Buffer Swap Protocol

Each `WorkerStats` owns two `WorkerStatsBuffer` instances and one `ManualResetEventSlim`.
The reporter drives swaps; workers acknowledge at safe points.

```mermaid
sequenceDiagram
    participant RT  as Reporter thread
    participant WS  as WorkerStats
    participant WT  as Worker thread ws-i

    Note over RT: PeriodicTimer fires

    RT->>WS: RequestSwap()
    Note right of WS: swapAck.Reset()         — arm the event\nVolatile.Write(_swapRequested=1) — signal worker

    RT->>WS: WaitForSwapAck(cts.Token)
    Note right of RT: Blocks on ManualResetEventSlim.Wait()

    WT->>WS: AcknowledgeSwapIfRequested()     — at next safe point
    Note right of WT: Volatile.Read(_swapRequested)==1\nswap _active ↔ _inactive\n_active.Reset()              — zero new active\nVolatile.Write(_swapRequested=0)\nswapAck.Set()               — unblock reporter

    WS-->>RT: swapAck.Set() unblocks WaitForSwapAck

    RT->>WS: GetInactiveBufferForMerge()
    Note right of RT: Reads the now-inactive buffer\n(worker is writing to the fresh active buffer)

    RT->>RT: snapshot.MergeFrom(buf)
    RT->>RT: snapshot.FinalizeSnapshot(topK)
    RT->>RT: PrintReportFrame(...)
```

**State of the two buffers over time**:

```mermaid
stateDiagram-v2
    direction LR

    [*] --> NormalA : WorkerStats constructed\nA=active, B=inactive

    NormalA --> SwapRequested : Reporter calls RequestSwap()\nswapAck.Reset(); _swapRequested=1
    SwapRequested --> NormalB  : Worker acks:\nA↔B swapped; A.Reset(); swapAck.Set()
    NormalB --> SwapRequested  : Reporter calls RequestSwap() again
    SwapRequested --> NormalA  : Worker acks:\nB↔A swapped; B.Reset(); swapAck.Set()

    note right of SwapRequested : Reporter is blocked in WaitForSwapAck.\nWorker continues processing on old active.\nSwap happens at next safe point only.
```

**Safe points for `AcknowledgeSwapIfRequested`** (every worker, per loop iteration):

1. After `TryDequeue` timeout (no event received)
2. After routing and handling a full event
3. Before each `ProcessOnce` call (inside gate, top of catch-up loop)
4. After each `ProcessOnce` call (inside gate, bottom of catch-up loop)

---

## 7 — Reporter Thread Lifecycle

```mermaid
stateDiagram-v2
    direction TB

    [*]           --> Idle          : reporter.Start()\nPeriodicTimer created

    Idle          --> Swapping      : PeriodicTimer tick\n(WaitForNextTickAsync returns true)
    Idle          --> FinalReport   : timer disposed (Stop called)\nWaitForNextTickAsync returns false

    Swapping      --> Swapping      : RequestSwap() for each worker
    Swapping      --> WaitingAcks   : all RequestSwap() calls sent

    WaitingAcks   --> WaitingAcks   : per-worker WaitForSwapAck (parallel)\nTimeout = _ackTimeout (CancellationTokenSource)
    WaitingAcks   --> PartialWarning: acked < workers.Length after timeout
    WaitingAcks   --> Building      : all workers acked

    PartialWarning --> Building      : log warning, proceed with partial data (RPT-004)

    Building      --> Printing      : GetInactiveBufferForMerge × N\nsnapshot.MergeFrom × N\nsnapshot.FinalizeSnapshot(topK)
    Printing      --> Idle          : PrintReportFrame(snapshot, elapsed)

    FinalReport   --> [*]           : BuildSnapshotAndFrame + PrintReportFrame\n(elapsedSeconds=0, final=true)
```

---

## 8 — Application Shutdown State Machine

Shutdown is triggered by `Ctrl+C` or `ProcessExit`. An `Interlocked.Exchange` guard makes it idempotent (**HOST-002**).

```mermaid
stateDiagram-v2
    direction TB

    [*]          --> Running      : ApplicationHost.Run()

    Running      --> ShutdownOnce : Ctrl+C or ProcessExit\nInterlocked.Exchange(ref _shutdownRequested, 1)==0

    Running      --> Running      : Ctrl+C or ProcessExit\nInterlocked.Exchange returns 1\n(already shutting down — ignored)

    ShutdownOnce --> StopWatcher  : (begin ordered teardown)
    StopWatcher  --> StopBus      : watcher.Stop() — DisableRaisingEvents
    StopBus      --> StopWorkers  : bus.Stop() — Writer.TryComplete()
    StopWorkers  --> StopReporter : coordinator.Stop()\n  Volatile.Write(_stopping=true)\n  foreach Join(2 s) or Interrupt
    StopReporter --> Finished     : reporter.Stop()\n  timer.Dispose(); Join(2 s)
    Finished     --> [*]          : _shutdownEvent.Set() — unblocks main thread
```

---

## 9 — Full End-to-End Event Flow

A single file-modification event travelling through all layers:

```mermaid
sequenceDiagram
    participant FS  as Filesystem
    participant WTA as WatcherThread (OS)
    participant BUS as BoundedEventBus
    participant WT  as Worker ws-0
    participant REG as FileStateRegistry
    participant FST as FileState (Gate)
    participant FP  as FileProcessor
    participant WS  as WorkerStats[0]
    participant RT  as Reporter thread

    FS->>WTA: File written
    WTA->>BUS: Publish(Modified, path)\nTryWrite → _published++
    Note right of BUS: If full: _dropped++, event silently discarded (BP-002)

    BUS-->>WT: TryDequeue(200 ms) → FsEvent
    WT->>WS: Active.IncrementFsEvent(Modified)
    WT->>REG: GetOrCreate(path) → FileState
    WT->>FST: Monitor.TryEnter(Gate)

    alt Gate free
        WT->>WS: AcknowledgeSwapIfRequested()
        WT->>FP: ProcessOnce(path, state, activeBuffer)
        FP->>FP: FileTailer.ReadAppended → bytes
        FP->>FP: Utf8LineScanner.Scan → lines
        FP->>FP: LogParser.TryParse → LogRecord
        FP->>WS: IncrementLevel / IncrementMessage / RecordLatency
        FP->>FST: state.Offset += bytesRead
        WT->>WS: AcknowledgeSwapIfRequested()
        WT->>FST: IsDirty? → false → break loop
        WT->>FST: Monitor.Exit(Gate)
    else Gate busy (another worker holds it)
        WT->>FST: MarkDirtyIfAllowed() — _dirty=1
        WT->>WS: CoalescedDueToBusyGate++
        Note right of WT: Gate holder will re-process\ndue to dirty flag
    end

    WT->>WS: AcknowledgeSwapIfRequested()

    Note over RT: PeriodicTimer fires (e.g., every 2 s)
    RT->>WS: RequestSwap()
    WS-->>WT: _swapRequested=1 visible at next safe point
    WT->>WS: Swap A↔B; A.Reset(); swapAck.Set()
    RT->>WS: WaitForSwapAck returns
    RT->>WS: GetInactiveBufferForMerge() → bufferB
    RT->>RT: snapshot.MergeFrom(bufferB)
    RT->>RT: FinalizeSnapshot → TopK + percentiles
    RT->>RT: PrintReportFrame to stdout
```

---

## 10 — Concurrency Primitives Reference

| Primitive | Where used | Purpose |
|---|---|---|
| `Channel<T>` (bounded) | `BoundedEventBus` | Lock-free producer/consumer queue with bounded capacity |
| `Volatile.Read/Write` | `_stopping`, `_dirty`, `_deletePending`, `_swapRequested`, `_stopped` | Lightweight cross-thread flag visibility without locks |
| `Interlocked.Increment` | `_published`, `_dropped`, `_errorCount` | Atomic counter updates |
| `Interlocked.Exchange` | `_shutdownRequested` | Idempotent one-shot check-and-set |
| `Monitor.TryEnter` + `finally` | `FileState.Gate` in coordinator | Non-blocking per-file mutual exclusion |
| `ManualResetEventSlim` | `WorkerStats._swapAck` | Worker → Reporter acknowledgement signal |
| `PeriodicTimer` | `Reporter` | Allocation-free periodic wake-up |
| `CancellationTokenSource` | Dequeue timeout, swap ack timeout | Bounded waits |
| `Parallel.ForEach` | Reporter swap-ack phase | Wait for all workers in parallel |
| `Thread.Join` + `Thread.Interrupt` | Shutdown | Bounded thread termination |
| `ConcurrentDictionary` | `FileStateRegistry` | Thread-safe path → state map |
