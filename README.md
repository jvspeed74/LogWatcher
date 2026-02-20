# LogWatcher

**Real-time log statistics for .NET without external dependencies.**

[![Build Status](https://img.shields.io/badge/build-active-brightgreen)](https://github.com/jvspeed74/LogWatcher/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](#license)

![](./docs/assets/terminal_demonstration.gif)

---

## Why This Exists

Built as a learning project alongside SAA-C03 preparation to develop hands-on intuition for system design tradeoffs —
specifically consistency models, backpressure, and memory management.

I wanted to see what patterns like bounded queues, decoupled producers and consumers, load shedding, and eventual
consistency actually look like in working code.

### Constraints

Before considering any design, I gave myself a set of constraints to force tradeoffs and guide decisions.

These were **intentionally restrictive** to encourage creativity and learning:

- **No external dependencies** — Only .NET built-in libraries; no third-party packages for parsing, metrics, or
  concurrency
- **Eventual consistency** — In-memory state may be temporarily stale or inconsistent across workers, but must
  converge to correctness over time without manual intervention
- **State must never be corrupted** — Handle data races and cross thread operations gracefully without crashing or
  losing consistency

---

## Technical Highlights

- **Span-based zero-allocation hot path** — UTF-8 line parsing uses `ReadOnlySpan<byte>` with pooled buffers to minimize
  GC pressure in the parsing loop
- **Double-buffer swap protocol** — Lock-free stats collection between N worker threads and the reporter thread using
  atomic buffer exchanges and interval-based snapshots
- **Drop-newest backpressure** — Bounded event queue with deterministic metrics visibility; when full, new events are
  dropped with explicit reporting, not silently deferred
- **Safe file deletion handling** — Epoch-based file tracking prevents crashes on delete/rename races;
  `FileStateRegistry` maintains consistency across concurrent workers
- **Per-worker stats aggregation** — Each worker accumulates local counters independently, reducing contention versus a
  shared global counter; buffers are merged at report time

---

## Architecture

```mermaid
graph TB
    subgraph Ingestion["Ingestion"]
        FS["File System"]
        FSW["FilesystemWatcherAdapter"]
        FEV["FsEvent<br/>Created|Modified|Deleted|Renamed"]
    end

    subgraph Backpressure["Backpressure"]
        BEB["BoundedEventBus&lt;T&gt;"]
    end

    subgraph FileManagement["FileManagement"]
        FSR["FileStateRegistry<br/>Per-File State Machine"]
        FS_State["FileState<br/>Offset + Flags + Gate"]
        PLB["PartialLineBuffer<br/>Carryover Storage"]
    end

    subgraph Processing["Processing"]
        PC["ProcessingCoordinator<br/>N Worker Threads"]
        FP["FileProcessor<br/>Orchestrator"]

        subgraph Tailing["Tailing"]
            FT["FileTailer<br/>Chunked Reads"]
            TRS["TailReadStatus"]
        end

        subgraph Scanning["Scanning"]
            USS["Utf8LineScanner<br/>Line Splitting"]
        end

        subgraph Parsing["Parsing"]
            LP["LogParser<br/>Parse Records"]
            LL["LogLevel"]
            PLL["ParsedLogLine"]
        end
    end

    subgraph Statistics["Statistics"]
        WSB["WorkerStatsBuffer<br/>Per-Interval Metrics"]
        LH["LatencyHistogram<br/>Bounded Distribution"]
        TK["TopK<br/>Frequency Computation"]
    end

    subgraph Coordination_["Coordination"]
        WS["WorkerStats<br/>Double-Buffer Swap Protocol"]
    end

    subgraph Reporting["Reporting"]
        GS["GlobalSnapshot<br/>Merged Interval View"]
        REP["Reporter<br/>Aggregation & Output"]
    end

    FS -->|File Changes| FSW
    FSW -->|FsEvent| BEB

    BEB -->|Dequeue| PC

    PC -->|Lookup/Create| FSR
    FSR -->|State| FS_State
    FS_State -->|Carryover| PLB

    PC -->|Orchestrate| FP

    FP -->|Read Chunks| FT
    FT -->|Status| TRS
    FT -->|Raw Bytes| USS

    USS -->|Lines| LP
    LP -->|Level| LL
    LP -->|ParsedLogLine| PLL

    PLL -->|Counters| WSB
    PLL -->|Message| TK
    PLL -->|Latency| LH

    WSB -->|Contains| Statistics
    TK -->|Contains| Statistics
    LH -->|Contains| Statistics

    PC -->|Coordinates| WS
    WS -->|Owns| WSB

    WS -->|Swap Request| REP
    WSB -->|Merge| GS
    TK -->|Merge| GS
    LH -->|Merge| GS

    GS -->|Snapshot| REP
    REP -->|Output| STDOUT["Console Output"]

    style Ingestion fill:#e1f5ff
    style Backpressure fill:#f3e5f5
    style FileManagement fill:#fce4ec
    style Processing fill:#fff3e0
    style Tailing fill:#fff8e1
    style Scanning fill:#fff8e1
    style Parsing fill:#fff8e1
    style Statistics fill:#f1f8e9
    style Coordination_ fill:#e0f2f1
    style Reporting fill:#ede7f6
```

---

## Usage

```bash
dotnet run --project LogWatcher.App -- <watchPath> [options]
```

| Argument/Option         | Description                                      | Default    |
|-------------------------|--------------------------------------------------|------------|
| `watchPath`             | Directory path to watch for log file changes     | (required) |
| `--workers, -w`         | Number of worker threads for parallel processing | CPU count  |
| `--queue-capacity, -q`  | Maximum capacity of the filesystem event queue   | 10,000     |
| `--report-interval, -i` | Interval between console output (seconds)        | 2          |
| `--topk, -k`            | Number of most-frequent messages to track        | 10         |

```bash
dotnet run --project LogWatcher.App -- ./logs --workers 8 --queue-capacity 50000 --report-interval 1
```

### Docker Compose

To run the application with a sample log generator using Docker Compose, use the following command:

```bash
docker compose up --build
```

---

## Key Design Decisions

For a full breakdown see [`docs/technical_specification.md`](docs/technical_specification.md) and [
`docs/thread_lifecycle.md`](docs/thread_lifecycle.md).

---

## License

MIT