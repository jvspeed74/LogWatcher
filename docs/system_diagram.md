# LogWatcher System Diagram

## High-Level Architecture

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
            PLL["ParsedLogLine"]
        end
    end

    subgraph Statistics["Statistics"]
        WSB["WorkerStatsBuffer<br/>Per-Interval Metrics"]
        LH["LatencyHistogram<br/>Bounded Distribution"]
        TK["TopK<br/>Frequency Computation"]
        SL["StatLevel"]
        SEK["StatEventKind"]
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
    LP -->|ParsedLogLine| PLL
    PLL -->|LogLevel → StatLevel| FP
    FP -->|Counters| WSB
    FP -->|Message| TK
    FP -->|Latency| LH
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

```

## Component Interactions - Detailed Flow

```mermaid
graph LR
    subgraph Ingestion["① Ingestion"]
        A["FileSystem<br/>Changes"]
        B["FSWatcherAdapter"]
        C["BoundedEventBus"]
    end

    subgraph Dequeue["② Dequeue"]
        D["ProcessingCoordinator<br/>Worker Threads"]
    end

    subgraph Lookup["③ Lookup State"]
        E["FileStateRegistry"]
        F["FileState<br/>Per File"]
    end

    subgraph Read["④ Read & Tail"]
        G["FileProcessor"]
        H["FileTailer<br/>Track Position"]
    end

    subgraph Parse["⑤ Parse"]
        I["Utf8LineScanner<br/>ReadOnlySpan&lt;byte&gt;"]
        J["LogParser<br/>Extract Fields"]
    end

    subgraph Aggregate["⑥ Aggregate"]
        K["WorkerStats"]
        L["TopK Queue"]
        M["LatencyHistogram"]
    end

    subgraph Report["⑦ Report"]
        N["GlobalSnapshot<br/>Swap"]
        O["Reporter"]
        P["Console Output"]
    end

    A -->|Create/Modify/Delete/Rename| B
    B -->|FsEvent| C
    C -->|Dequeue<br/>Timeout| D
    D -->|Lookup| E
    E -->|State| F
    D -->|Read File| G
    G -->|Get Position| H
    H -->|New Bytes| I
    I -->|ParsedLogLine| J
    J -->|LogLevel → StatLevel| G
    G -->|Counters| K
    G -->|Message| L
    G -->|Latency| M
    K -->|Swap Buffer| N
    L -->|Swap Buffer| N
    M -->|Swap Buffer| N
    N -->|Global Stats| O
    O -->|Formatted| P

```

## Data Structures

```mermaid
graph TB
    subgraph Events["Events"]
        FEV["FsEvent<br/>• Kind: FsEventKind<br/>• Path: string<br/>• OldPath: string?<br/>• ObservedAt: DateTimeOffset<br/>• Processable: bool"]
        FEVK["FsEventKind<br/>Created|Modified<br/>Deleted|Renamed"]
    end

    subgraph Records["Log Records"]
        LOGR["ParsedLogLine<br/>• Timestamp: DateTimeOffset<br/>• Level: LogLevel<br/>• MessageKey: ReadOnlySpan&lt;byte&gt;<br/>• LatencyMs: int?"]
        LOGLVL["LogLevel<br/>Info|Warn|Error|Debug|Other"]
    end

    subgraph FileState["File State Machine"]
        FSTATE["FileState<br/>• Offset: long<br/>• Carry: PartialLineBuffer<br/>• Generation: int<br/>• IsDirty: bool<br/>• IsDeletePending: bool"]
        FSREG["FileStateRegistry<br/>Registry of all active<br/>file states"]
    end

    subgraph Stats["Statistics"]
        WS["WorkerStats<br/>• Active: WorkerStatsBuffer<br/>• Inactive: WorkerStatsBuffer<br/>• Swap protocol"]
        WSB["WorkerStatsBuffer<br/>Current + Swap"]
        LH["LatencyHistogram<br/>• Median<br/>• P95, P99"]
        TK["TopK<br/>Most frequent<br/>message keys"]
        GS["GlobalSnapshot<br/>Aggregated metrics"]
        SL["StatLevel<br/>Info|Warn|Error|Debug|Other"]
        SEK["StatEventKind<br/>Created|Modified|Deleted|Renamed"]
    end

    FEV -.-> FEVK
    LOGR -.-> LOGLVL
    FSTATE -.-> FSREG
    WS --> WSB
    LH --> GS
    TK --> GS
    WSB --> GS

```

## Concurrency Model

```mermaid
graph TB
    subgraph Threads["Thread Model"]
        MAIN["Main Thread<br/>CLI & Setup"]
        REPORT["Reporter Thread<br/>Periodic Output"]
        W1["Worker Thread 1"]
        W2["Worker Thread 2"]
        WN["Worker Thread N<br/>..."]
    end

    subgraph Sync["Synchronization"]
        BEB["BoundedEventBus<br/>lock + Monitor.Wait/Pulse<br/>Drop-newest on full"]
        FGATE["Per-File Gate<br/>Monitor.TryEnter<br/>Single writer per file"]
        WSLOCK["WorkerStats Lock<br/>Swap with atomic check"]
    end

    subgraph Volatile["Volatile State"]
        FSTOP["_stopping: volatile bool"]
    end

    MAIN -->|Publish| BEB
    W1 -->|Dequeue| BEB
    W2 -->|Dequeue| BEB
    WN -->|Dequeue| BEB
    W1 -->|Acquire| FGATE
    W2 -->|Acquire| FGATE
    WN -->|Acquire| FGATE
    W1 -->|Update| WSLOCK
    W2 -->|Update| WSLOCK
    WN -->|Update| WSLOCK
    REPORT -->|Read GS| WSLOCK
    W1 -.->|Check| FSTOP
    W2 -.->|Check| FSTOP
    WN -.->|Check| FSTOP

```

## Shutdown Sequence

```mermaid
sequenceDiagram
    participant CLI as CLI/Main
    participant Watcher as FilesystemWatcher
    participant Bus as BoundedEventBus
    participant PC as ProcessingCoordinator
    participant Workers as Worker Threads
    participant Reporter as Reporter

    CLI->>Watcher: Stop()
    Watcher->>Watcher: Dispose (no more events)
    CLI->>Bus: Stop()
    Bus->>Bus: Mark stopped, unblock waiters
    CLI->>PC: Stop()
    PC->>PC: Set _stopping = true
    PC->>Workers: Join threads (wait for exit)
    Workers->>Bus: TryDequeue(timeout=100ms)
    Bus-->>Workers: null (stopped empty)
    Workers->>Workers: Exit loop
    Workers-->>PC: Thread exit
    PC-->>CLI: Stop() returns
    CLI->>Reporter: Final report
    Reporter-->>CLI: Output
    CLI->>CLI: Shutdown

    Note over CLI,Reporter: Graceful shutdown with timeout
```

## Data Flow - Processing a Log Line

```mermaid
graph LR
    A["Raw File<br/>Bytes"] -->|Read Chunk<br/>via FileTailer| B["Partial Line<br/>Buffer"]
    B -->|Utf8LineScanner<br/>ReadOnlySpan&lt;byte&gt;| C["Complete<br/>Log Line"]
    C -->|Parse<br/>LogParser| D["ParsedLogLine<br/>Timestamp<br/>Level<br/>Message Key<br/>Latency"]
    D -->|Extract| E["Message Key<br/>String"]
    D -->|Extract| F["Latency<br/>int?"]
    E -->|TopK.Observe| G["TopK<br/>Frequency Map"]
    F -->|Add to| H["Latency<br/>Histogram"]
    D -->|Count| I["Line Counter<br/>per Worker"]
    
    G -->|Swap| J["Global Stats"]
    H -->|Swap| J
    I -->|Swap| J
    
    J -->|Format| K["Reporter<br/>Console Output"]
    
```

## File State Machine

```mermaid
stateDiagram-v2
    [*] --> Active: File Created/Modified
    
    Active --> Active: File Modified<br/>Extend tailing
    Active --> PendingDelete: Delete event<br/>Set delete flag
    
    PendingDelete --> Deleted: Finalize delete<br/>Clear entry
    PendingDelete --> Active: File Recreated<br/>New epoch
    
    Deleted --> [*]
    
    note right of Active
        - Tracking file position
        - Tailing new content
        - Parsing log lines
    end note
    
    note right of PendingDelete
        - No new events processed
        - Await finalization
        - Can be recreated with new epoch
    end note
```

## Queue Behavior - BoundedEventBus

```mermaid
graph LR
    A["Event<br/>Incoming"] -->|Capacity<br/>Available| B["Enqueue<br/>to Queue"]
    B -->|Return true| C["Workers<br/>Dequeue & Process"]
    
    A -->|Capacity<br/>FULL| D["Drop Newest<br/>Increment Dropped"]
    D -->|Return false| E["Event Lost<br/>Counted in Metrics"]
    
    C -->|Timeout<br/>100ms| F["Check for<br/>More Events"]
    F -->|Stopped &<br/>Empty| G["Exit Worker"]
    F -->|Not Stopped| H["Repeat Dequeue"]
```

