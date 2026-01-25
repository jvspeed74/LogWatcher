# US-203: Reporter Metrics with Structured Logging

## User Story

As a **DevOps engineer**, I want **structured interval metrics from the Reporter** so that I can **monitor throughput, latency percentiles, and error rates programmatically**.

## Acceptance Criteria

### AC-1: Reporter Interval Metrics Schema

**Event: reporter_interval**

```json
{
  "ts": "2024-01-15T10:30:02.000Z",
  "level": "Information",
  "category": "WatchStats.Core.Metrics.Reporter",
  "eventName": "reporter_interval",
  "msg": "Interval metrics",
  "intervalMs": 2000,
  "lines": 15000,
  "linesPerSec": 7500.0,
  "malformed": 10,
  "malformedPerSec": 5.0,
  "p50": 12.5,
  "p95": 45.0,
  "p99": 120.0,
  "drops": 0,
  "truncations": 0,
  "overflows": 0,
  "gc0": 2,
  "gc1": 0,
  "gc2": 0,
  "levelInfo": 14500,
  "levelWarn": 450,
  "levelError": 40,
  "levelOther": 10
}
```

**Field Definitions**:

| Field | Type | Unit | Description |
|-------|------|------|-------------|
| `intervalMs` | int | ms | Actual measured elapsed time for this interval |
| `lines` | long | count | Total lines processed in this interval |
| `linesPerSec` | double | lines/sec | Calculated as `lines / (intervalMs / 1000.0)` |
| `malformed` | long | count | Malformed lines in this interval |
| `malformedPerSec` | double | lines/sec | Calculated as `malformed / (intervalMs / 1000.0)` |
| `p50` | double | ms | 50th percentile latency |
| `p95` | double | ms | 95th percentile latency |
| `p99` | double | ms | 99th percentile latency |
| `drops` | long | count | Event bus drops since last report |
| `truncations` | long | count | File truncations detected |
| `overflows` | long | count | Watcher overflows |
| `gc0` | int | count | Gen 0 GC collections delta |
| `gc1` | int | count | Gen 1 GC collections delta |
| `gc2` | int | count | Gen 2 GC collections delta |
| `levelInfo` | long | count | INFO level lines |
| `levelWarn` | long | count | WARN level lines |
| `levelError` | long | count | ERROR level lines |
| `levelOther` | long | count | Other level lines |

### AC-2: Elapsed-Based Rate Calculation

**Given** reporter configured with `--interval 2000` (ms)  
**When** actual interval elapses due to GC or processing delays  
**Then** use measured `intervalMs` for rate calculations

**Example**:
```
Configured interval: 2000ms
Actual elapsed:      2345ms
Lines processed:     10000
Rate calculation:    10000 / (2345 / 1000.0) = 4264.39 lines/sec
```

**NOT** based on configured interval:
```
Wrong: 10000 / 2 = 5000 lines/sec  // Don't do this!
```

### AC-3: Zero-Line Handling

**Given** an interval with zero lines processed  
**When** metrics are emitted  
**Then** rate fields are `0.0`, NOT `NaN` or `Infinity`

```json
{
  "intervalMs": 2000,
  "lines": 0,
  "linesPerSec": 0.0,
  "malformed": 0,
  "malformedPerSec": 0.0
}
```

### AC-4: Metrics Toggle Support

**Given** CLI flag `--no-metrics-logs` or env `WATCHSTATS_METRICS_LOGS=0`  
**When** reporter runs  
**Then** `reporter_interval` events are NOT emitted  
**And** reporter still collects metrics internally (for future API exposure)

**Given** default or `WATCHSTATS_METRICS_LOGS=1`  
**Then** `reporter_interval` events ARE emitted

### AC-5: Swap Timeout Logging

**Event: reporter_swap_timeout**

```json
{
  "ts": "2024-01-15T10:30:02.500Z",
  "level": "Warning",
  "category": "WatchStats.Core.Metrics.Reporter",
  "eventName": "reporter_swap_timeout",
  "msg": "Worker buffer swap timeout",
  "workerId": 3,
  "timeoutMs": 5000
}
```

**Given** reporter requests buffer swap  
**When** worker does not acknowledge within 5 seconds  
**Then** emit `reporter_swap_timeout` warning  
**And** proceed with other workers

### AC-6: Consistent Field Sets Across Formats

**Text format** (when `--json-logs` not set):
```
2024-01-15T10:30:02.000Z [Information] WatchStats.Core.Metrics.Reporter: Interval metrics [intervalMs=2000, lines=15000, linesPerSec=7500.0, malformed=10, malformedPerSec=5.0, p50=12.5, p95=45.0, p99=120.0, drops=0, truncations=0, overflows=0, gc0=2, gc1=0, gc2=0, levelInfo=14500, levelWarn=450, levelError=40, levelOther=10]
```

**JSON format** (when `--json-logs` set):
```json
{
  "Timestamp": "2024-01-15T10:30:02.000Z",
  "Level": "Information",
  "Category": "WatchStats.Core.Metrics.Reporter",
  "Message": "Interval metrics",
  "EventId": {"Name": "reporter_interval"},
  "State": {
    "intervalMs": 2000,
    "lines": 15000,
    "linesPerSec": 7500.0,
    "malformed": 10,
    "malformedPerSec": 5.0,
    "p50": 12.5,
    "p95": 45.0,
    "p99": 120.0,
    "drops": 0,
    "truncations": 0,
    "overflows": 0,
    "gc0": 2,
    "gc1": 0,
    "gc2": 0,
    "levelInfo": 14500,
    "levelWarn": 450,
    "levelError": 40,
    "levelOther": 10,
    "{OriginalFormat}": "Interval metrics"
  }
}
```

### AC-7: Constructor Signature

```csharp
public Reporter(
    WorkerStats[] workerStats,
    BoundedEventBus<FsEvent> bus,
    int topK,
    int intervalMs,
    bool enableMetricsLogs,
    ILogger<Reporter>? logger = null)
```

### AC-8: Integration Test

**Given** a reporter with mock workers  
**When** interval elapses  
**Then** structured log contains all expected fields  
**And** `linesPerSec` = `lines / (intervalMs / 1000.0)`  
**And** zero lines produces `0.0` rates, not `NaN`

## Technical Implementation

### Measuring Elapsed Time

```csharp
private void ReportLoop()
{
    while (!_stopped)
    {
        var sw = Stopwatch.StartNew();
        Thread.Sleep(_intervalMs);
        sw.Stop();
        
        var elapsedMs = (int)sw.ElapsedMilliseconds;
        
        var snapshot = BuildSnapshotAndFrame(elapsedMs);
        EmitMetrics(snapshot, elapsedMs);
    }
}
```

### Emitting Metrics

```csharp
private void EmitMetrics(GlobalSnapshot snapshot, int intervalMs)
{
    if (!_enableMetricsLogs) return;
    
    var linesPerSec = intervalMs > 0 
        ? snapshot.Lines / (intervalMs / 1000.0) 
        : 0.0;
    var malformedPerSec = intervalMs > 0 
        ? snapshot.Malformed / (intervalMs / 1000.0) 
        : 0.0;
    
    _logger?.LogInformation(
        eventId: new EventId(10, "reporter_interval"),
        "Interval metrics. " +
        "intervalMs={IntervalMs}, " +
        "lines={Lines}, " +
        "linesPerSec={LinesPerSec:F1}, " +
        "malformed={Malformed}, " +
        "malformedPerSec={MalformedPerSec:F1}, " +
        "p50={P50:F1}, " +
        "p95={P95:F1}, " +
        "p99={P99:F1}, " +
        "drops={Drops}, " +
        "truncations={Truncations}, " +
        "overflows={Overflows}, " +
        "gc0={Gc0}, " +
        "gc1={Gc1}, " +
        "gc2={Gc2}, " +
        "levelInfo={LevelInfo}, " +
        "levelWarn={LevelWarn}, " +
        "levelError={LevelError}, " +
        "levelOther={LevelOther}",
        intervalMs,
        snapshot.Lines,
        linesPerSec,
        snapshot.Malformed,
        malformedPerSec,
        snapshot.P50,
        snapshot.P95,
        snapshot.P99,
        snapshot.BusDrops,
        snapshot.Truncations,
        snapshot.Overflows,
        snapshot.Gc0,
        snapshot.Gc1,
        snapshot.Gc2,
        snapshot.LevelInfo,
        snapshot.LevelWarn,
        snapshot.LevelError,
        snapshot.LevelOther);
}
```

### Swap Timeout Logging

```csharp
private bool RequestSwap(WorkerStats stats, int workerId)
{
    stats.RequestSwap();
    
    var timeout = TimeSpan.FromSeconds(5);
    var sw = Stopwatch.StartNew();
    
    while (!stats.SwapAcknowledged() && sw.Elapsed < timeout)
    {
        Thread.Sleep(10);
    }
    
    if (!stats.SwapAcknowledged())
    {
        _logger?.LogWarning(
            eventId: new EventId(11, "reporter_swap_timeout"),
            "Worker buffer swap timeout. workerId={WorkerId}, timeoutMs={TimeoutMs}",
            workerId,
            (int)timeout.TotalMilliseconds);
        return false;
    }
    
    return true;
}
```

## Definition of Done

- [ ] Reporter emits `reporter_interval` with all required fields
- [ ] Rates calculated using measured elapsed time
- [ ] Zero-line intervals produce `0.0` rates
- [ ] Metrics toggle works (`--no-metrics-logs`)
- [ ] Swap timeout warnings emitted correctly
- [ ] Text and JSON formats include same fields
- [ ] Unit tests validate rate calculations
- [ ] Integration tests verify metrics payloads
