# Observability Plan: Dependency Injection + Structured Logging

## Overview

This document outlines the observability implementation for WatchStats, adding Dependency Injection (DI) and structured logging while keeping `WatchStats.Core` dependency-free.

## Architecture Principles

1. **Core Remains DI-Free**: `WatchStats.Core` has zero dependencies on DI frameworks. All components are constructible manually for testability.
2. **CLI as Composition Root**: `WatchStats.Cli` owns DI wiring using `Microsoft.Extensions.DependencyInjection`
3. **Optional Logging**: Core components accept optional `ILogger<T>` parameters (nullable), defaulting to no-op behavior when not provided
4. **Structured Events**: All logs use structured logging with well-defined event names and field schemas

## DI Configuration

### Service Registrations (All Singleton)

```csharp
services.AddSingleton<BoundedEventBus<FsEvent>>(sp => 
    new BoundedEventBus<FsEvent>(config.BusCapacity));
services.AddSingleton<FileStateRegistry>();
services.AddSingleton<FileTailer>(sp => 
    new FileTailer(sp.GetService<ILogger<FileTailer>>()));
services.AddSingleton<FileProcessor>();
services.AddSingleton<WorkerStats[]>(...); // Array of N worker stats
services.AddSingleton<ProcessingCoordinator>();
services.AddSingleton<Reporter>();
services.AddSingleton<FilesystemWatcherAdapter>();
services.AddSingleton<AppOrchestrator>();
```

### Logging Configuration

```csharp
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(config.LogLevel);
    builder.AddConsole(options =>
    {
        options.FormatterName = config.JsonLogs 
            ? ConsoleFormatterNames.Json 
            : ConsoleFormatterNames.Simple;
    });
    
    // Per-category overrides from environment
    ApplyCategoryOverrides(builder);
});
```

## Configuration Schema

### CLI Arguments

- `--dir`, `--directory`: Watch directory (required, env: `WATCHSTATS_DIRECTORY`)
- `--workers <N>`: Worker thread count (env: `WATCHSTATS_WORKERS`, clamp [1, 64])
- `--capacity <N>`: Bus capacity (env: `WATCHSTATS_BUS_CAPACITY`, clamp [1000, 1000000])
- `--interval <ms>`: Report interval in milliseconds (env: `WATCHSTATS_REPORT_INTERVAL`, clamp [500, 60000])
- `--logLevel <level>`: Minimum log level (env: `WATCHSTATS_LOG_LEVEL`, default: Information)
- `--json-logs`: Enable JSON log output (env: `WATCHSTATS_JSON_LOGS=1`)
- `--no-metrics-logs`: Disable periodic metrics logging (env: `WATCHSTATS_METRICS_LOGS=0`)
- `--topk <N>`: Top-K message count (default: 10)

### Environment Variable Overrides

Per-category log levels:
```bash
WATCHSTATS_LOG_LEVEL_TAILER=Debug
WATCHSTATS_LOG_LEVEL_WATCHER=Warning
```

## Lifecycle Management

### Startup Sequence

```
DI_START_SEQUENCE → 
WATCHER_STARTED → 
WORKERS_STARTED → 
REPORTER_STARTED → 
DI_START_COMPLETE
```

Each step has a 5-second timeout with warning logs on exceeded timeouts.

### Shutdown Sequence

```
DI_STOP_SEQUENCE_BEGIN → 
WATCHER_STOPPED → 
BUS_STOPPED → 
WORKERS_STOPPED → 
REPORTER_STOPPED → 
DI_STOP_COMPLETE
```

Each stop operation has a 5-second timeout warning.

## Structured Logging Events

### Core Event Schema

All events include:
- `ts`: ISO 8601 timestamp
- `level`: Log level (Information, Warning, Error)
- `msg`: Human-readable message
- Component-specific fields (see US-202)

### Event Categories

1. **Lifecycle Events**: DI start/stop sequences
2. **Watcher Events**: Overflow detection
3. **Bus Events**: Drop-newest backpressure
4. **Tailer Events**: IO errors, truncation detection
5. **Worker Events**: Batch processing stats
6. **Reporter Events**: Interval metrics, swap timeouts

### Rate Limiting

Noisy warnings are rate-limited:
- Max 1 event per path per 10 seconds
- Coalesced summaries for high-frequency events

### PHI/Content Exclusions

- No log line content in structured logs
- No file paths containing sensitive data
- Only metadata (counts, timestamps, error codes)

## Implementation Guide

### Phase 1: Documentation (This Phase)
- ✓ Create observability plan documents
- ✓ Define user stories with acceptance criteria
- ✓ Establish event schemas

### Phase 2: DI Composition (US-201)
- Add Microsoft.Extensions packages
- Update CliConfig/CliParser
- Create AppOrchestrator
- Update Program.cs composition root
- Add lifecycle logging

### Phase 3: Structured Logging (US-202)
- Add ILogger parameters to Core components
- Replace Console.WriteLine with structured logs
- Implement rate limiting
- Add category overrides

### Phase 4: Reporter Metrics (US-203)
- Extend Reporter with structured interval logs
- Add metrics toggle
- Implement elapsed-based rates
- Add swap-timeout logging

### Phase 5: Testing
- Unit tests for config parsing
- Integration tests for DI composition
- Smoke tests for logging output
- Acceptance criteria validation

## Open Questions

1. **Shutdown Timing**: Should timeout warnings block shutdown or just log?
2. **Dual Providers**: Support both Console and File logging?
3. **JSON Envelope**: Custom fields in JSON formatter?
4. **Swap Timeout Threshold**: 5s hardcoded or configurable?

## References

- [US-201: DI Composition](US-201-di-composition.md)
- [US-202: Structured Logging](US-202-structured-logging.md)
- [US-203: Reporter Metrics](US-203-reporter-metrics.md)
