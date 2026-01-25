# US-202: Structured Logging Across Components

## User Story

As a **DevOps engineer**, I want **structured logging with well-defined event schemas** across all WatchStats components so that I can **parse logs programmatically, set up alerts, and troubleshoot issues efficiently**.

## Acceptance Criteria

### AC-1: Component-by-Component Logging Requirements

All components emit structured logs with:
- `ts`: ISO 8601 timestamp
- `level`: LogLevel (Trace/Debug/Information/Warning/Error/Critical)
- `category`: Fully-qualified type name (e.g., `WatchStats.Core.IO.FilesystemWatcherAdapter`)
- `eventName`: Unique event identifier
- `msg`: Human-readable message
- Component-specific fields (see schemas below)

### AC-2: FilesystemWatcherAdapter Events

**Event: watcher_overflow**

```json
{
  "ts": "2024-01-15T10:30:15.123Z",
  "level": "Warning",
  "category": "WatchStats.Core.IO.FilesystemWatcherAdapter",
  "eventName": "watcher_overflow",
  "msg": "Filesystem watcher buffer overflowed",
  "watchPath": "/var/log/app"
}
```

**Constructor signature**:
```csharp
public FilesystemWatcherAdapter(
    string watchPath,
    BoundedEventBus<FsEvent> bus,
    ILogger<FilesystemWatcherAdapter>? logger = null)
```

### AC-3: BoundedEventBus Events

**Event: bus_drop_newest**

```json
{
  "ts": "2024-01-15T10:30:20.456Z",
  "level": "Warning",
  "category": "WatchStats.Core.Concurrency.BoundedEventBus",
  "eventName": "bus_drop_newest",
  "msg": "Event bus full, dropping newest event",
  "capacity": 10000,
  "dropped": 1
}
```

**Constructor signature**:
```csharp
public BoundedEventBus(
    int capacity,
    ILogger<BoundedEventBus<T>>? logger = null)
```

### AC-4: FileStateRegistry Events

**Event: file_truncate_detected**

```json
{
  "ts": "2024-01-15T10:30:25.789Z",
  "level": "Warning",
  "category": "WatchStats.Core.Processing.FileStateRegistry",
  "eventName": "file_truncate_detected",
  "msg": "File truncation detected",
  "path": "/var/log/app/server.log",
  "previousSize": 1048576,
  "currentSize": 512
}
```

**Constructor signature**:
```csharp
public FileStateRegistry(ILogger<FileStateRegistry>? logger = null)
```

### AC-5: FileTailer Events

**Event: tailer_io_error**

```json
{
  "ts": "2024-01-15T10:30:30.012Z",
  "level": "Error",
  "category": "WatchStats.Core.IO.FileTailer",
  "eventName": "tailer_io_error",
  "msg": "IO error reading file",
  "path": "/var/log/app/server.log",
  "error": "System.IO.IOException: The process cannot access the file..."
}
```

**Constructor signature**:
```csharp
public FileTailer(ILogger<FileTailer>? logger = null)
```

### AC-6: FileProcessor Events

**Event: worker_batch_processed**

```json
{
  "ts": "2024-01-15T10:30:35.345Z",
  "level": "Debug",
  "category": "WatchStats.Core.Processing.FileProcessor",
  "eventName": "worker_batch_processed",
  "msg": "Worker batch processed",
  "workerId": 3,
  "linesProcessed": 1500,
  "malformedLines": 2,
  "durationMs": 45
}
```

**Constructor signature**:
```csharp
public FileProcessor(
    FileTailer tailer,
    ILogger<FileProcessor>? logger = null)
```

### AC-7: ProcessingCoordinator Events

**Event: coordinator_worker_started**

```json
{
  "ts": "2024-01-15T10:30:00.100Z",
  "level": "Information",
  "category": "WatchStats.Core.Concurrency.ProcessingCoordinator",
  "eventName": "coordinator_worker_started",
  "msg": "Worker thread started",
  "workerId": 3,
  "threadId": 12345
}
```

**Event: coordinator_worker_stopped**

```json
{
  "ts": "2024-01-15T10:35:00.200Z",
  "level": "Information",
  "category": "WatchStats.Core.Concurrency.ProcessingCoordinator",
  "eventName": "coordinator_worker_stopped",
  "msg": "Worker thread stopped",
  "workerId": 3
}
```

**Constructor signature**:
```csharp
public ProcessingCoordinator(
    BoundedEventBus<FsEvent> bus,
    FileStateRegistry registry,
    FileProcessor processor,
    WorkerStats[] workerStats,
    int workerCount,
    ILogger<ProcessingCoordinator>? logger = null)
```

### AC-8: Reporter Events

See [US-203: Reporter Metrics](US-203-reporter-metrics.md) for `reporter_interval` and `reporter_swap_timeout` events.

### AC-9: Rate Limiting for Noisy Logs

**Given** high-frequency warning events (e.g., `bus_drop_newest`)  
**When** the same event fires repeatedly  
**Then** rate limit to max 1 log per path/event per 10 seconds

**Implementation**:
```csharp
private readonly Dictionary<string, DateTime> _lastLogged = new();
private readonly TimeSpan _rateLimitInterval = TimeSpan.FromSeconds(10);

bool ShouldLog(string eventKey)
{
    var now = DateTime.UtcNow;
    if (_lastLogged.TryGetValue(eventKey, out var last) 
        && now - last < _rateLimitInterval)
    {
        return false;
    }
    _lastLogged[eventKey] = now;
    return true;
}
```

**Coalesced summary** (emitted every 30 seconds):

```json
{
  "eventName": "rate_limited_summary",
  "level": "Warning",
  "msg": "Rate-limited events summary",
  "events": {
    "bus_drop_newest": 1234,
    "tailer_io_error:/var/log/app/server.log": 56
  }
}
```

### AC-10: Per-Category Log Level Overrides

**Given** environment variables like:
```bash
WATCHSTATS_LOG_LEVEL=Information
WATCHSTATS_LOG_LEVEL_TAILER=Debug
WATCHSTATS_LOG_LEVEL_WATCHER=Warning
```

**When** logger is configured  
**Then** apply category-specific levels:

```csharp
void ApplyCategoryOverrides(ILoggingBuilder builder)
{
    var overrides = new Dictionary<string, LogLevel>
    {
        ["TAILER"] = "WatchStats.Core.IO.FileTailer",
        ["WATCHER"] = "WatchStats.Core.IO.FilesystemWatcherAdapter",
        ["BUS"] = "WatchStats.Core.Concurrency.BoundedEventBus",
        ["REGISTRY"] = "WatchStats.Core.Processing.FileStateRegistry",
        ["PROCESSOR"] = "WatchStats.Core.Processing.FileProcessor",
        ["COORDINATOR"] = "WatchStats.Core.Concurrency.ProcessingCoordinator",
        ["REPORTER"] = "WatchStats.Core.Metrics.Reporter",
    };

    foreach (var (key, category) in overrides)
    {
        var envVar = $"WATCHSTATS_LOG_LEVEL_{key}";
        var value = Environment.GetEnvironmentVariable(envVar);
        if (value != null && Enum.TryParse<LogLevel>(value, true, out var level))
        {
            builder.AddFilter(category, level);
        }
    }
}
```

### AC-11: PHI/Content Exclusions

**NEVER** log:
- Actual log line content (PHI/PII risk)
- File content snippets
- Sensitive environment variables

**ALWAYS** log:
- File paths (sanitized if needed)
- Counts (lines, bytes, errors)
- Timestamps and durations
- Error types (exception type names)

### AC-12: JSON vs. Text Format Consistency

**Given** `--json-logs` flag  
**When** logs are emitted  
**Then** JSON output includes all fields from text format

**Text format example**:
```
2024-01-15T10:30:15.123Z [Warning] WatchStats.Core.IO.FilesystemWatcherAdapter: Filesystem watcher buffer overflowed [watchPath=/var/log/app]
```

**JSON format example**:
```json
{
  "Timestamp": "2024-01-15T10:30:15.123Z",
  "Level": "Warning",
  "Category": "WatchStats.Core.IO.FilesystemWatcherAdapter",
  "Message": "Filesystem watcher buffer overflowed",
  "EventId": {"Name": "watcher_overflow"},
  "State": {
    "watchPath": "/var/log/app",
    "{OriginalFormat}": "Filesystem watcher buffer overflowed"
  }
}
```

## Technical Implementation

### Logging Call Pattern

```csharp
_logger?.LogWarning(
    eventId: new EventId(1, "watcher_overflow"),
    "Filesystem watcher buffer overflowed. watchPath={WatchPath}",
    _watchPath);
```

**Key points**:
- Use named EventId for `eventName`
- Structured parameters with `{ParamName}` syntax
- Message template includes parameter placeholders
- Null-conditional `?.` for optional logger

### EventId Constants

Define per-component:

```csharp
private static class Events
{
    public static readonly EventId WatcherOverflow = new(1, "watcher_overflow");
    public static readonly EventId BusDropNewest = new(2, "bus_drop_newest");
    // ...
}
```

## Definition of Done

- [ ] All Core components accept optional `ILogger<T>` parameter
- [ ] Structured logs emitted for all defined events
- [ ] Event schemas match specifications
- [ ] Rate limiting implemented for noisy events
- [ ] Per-category overrides work via environment variables
- [ ] No PHI/content logged
- [ ] JSON and text formats include same fields
- [ ] Unit tests validate log payloads
- [ ] Integration tests verify end-to-end logging
