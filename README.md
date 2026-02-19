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
    subgraph Input["Input Layer"]
        FS["File System"]
        FSW["FileSystemWatcher"]
    end

    subgraph EventBus["Event Bus Layer"]
        BEB["BoundedEventBus<FsEvent>"]
        EV["FsEvent<br/>Created|Modified|Deleted|Renamed"]
    end

    subgraph Coordination["Coordination Layer"]
        PC["ProcessingCoordinator<br/>N Worker Threads"]
        FSR["FileStateRegistry<br/>Per-File State Machine"]
    end

    subgraph Processing["Processing Layer"]
        FP["FileProcessor<br/>Read & Parse"]
        FT["FileTailer<br/>Track File Position"]
        LP["LogParser<br/>Parse Log Lines"]
        USS["Utf8LineScanner<br/>Span-based Scanning"]
    end

    subgraph Metrics["Metrics & Stats Layer"]
        WS["WorkerStats<br/>Per-Worker Stats"]
        WSB["WorkerStatsBuffer<br/>Swap Buffer"]
        LH["LatencyHistogram<br/>Track Latencies"]
        TK["TopK<br/>Most Frequent Messages"]
        GS["GlobalSnapshot<br/>Aggregate View"]
    end

    subgraph Reporting["Reporting Layer"]
        REP["Reporter<br/>Format & Output"]
        STDOUT["Console Output"]
    end

    FS -->|File Changes| FSW
    FSW -->|Adapt Events| BEB
    BEB -->|Dequeue| PC
    PC -->|Lookup State| FSR
    PC -->|Process File| FP
    FP -->|Get Position| FT
    FT -->|Raw Bytes| USS
    USS -->|Lines| LP
    LP -->|Stats| WS
    LP -->|Message| TK
    LP -->|Latency| LH
    WS -->|Buffer| WSB
    TK -->|Buffer| GS
    LH -->|Buffer| GS
    GS -->|Snapshot| REP
    REP -->|Output| STDOUT
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