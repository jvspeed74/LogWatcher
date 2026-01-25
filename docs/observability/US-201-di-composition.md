# US-201: DI Composition Root & Lifecycle Management

## User Story

As a **DevOps engineer**, I want **structured lifecycle logging and DI-based component composition** so that I can **monitor application startup/shutdown and troubleshoot initialization failures**.

## Acceptance Criteria

### AC-1: CLI Configuration Parsing

**Given** command-line arguments and environment variables  
**When** the application starts  
**Then** configuration is parsed with:

- `--dir` or `--directory` (required, env: `WATCHSTATS_DIRECTORY`) - absolute path validation
- `--workers <N>` (env: `WATCHSTATS_WORKERS`) - clamped to [1, 64], default: processor count
- `--capacity <N>` (env: `WATCHSTATS_BUS_CAPACITY`) - clamped to [1000, 1000000], default: 10000
- `--interval <ms>` (env: `WATCHSTATS_REPORT_INTERVAL`) - clamped to [500, 60000], default: 2000
- `--logLevel <level>` (env: `WATCHSTATS_LOG_LEVEL`) - valid levels: Trace|Debug|Information|Warning|Error|Critical, default: Information
- `--json-logs` (env: `WATCHSTATS_JSON_LOGS=1`) - boolean flag
- `--no-metrics-logs` (env: `WATCHSTATS_METRICS_LOGS=0`) - boolean flag to disable metrics
- `--topk <N>` - default: 10

**And** invalid values are rejected with clear error messages  
**And** `--help` or `-h` displays usage information

### AC-2: Help Text

```
Usage: WatchStats --dir <path> [options]

Required:
  --dir, --directory <path>    Directory to watch (env: WATCHSTATS_DIRECTORY)

Options:
  --workers <N>                Worker thread count (env: WATCHSTATS_WORKERS, default: CPU count)
  --capacity <N>               Event bus capacity (env: WATCHSTATS_BUS_CAPACITY, default: 10000)
  --interval <ms>              Report interval in milliseconds (env: WATCHSTATS_REPORT_INTERVAL, default: 2000)
  --topk <N>                   Top-K message count (default: 10)
  --logLevel <level>           Minimum log level (env: WATCHSTATS_LOG_LEVEL, default: Information)
                               Values: Trace, Debug, Information, Warning, Error, Critical
  --json-logs                  Output logs in JSON format (env: WATCHSTATS_JSON_LOGS)
  --no-metrics-logs            Disable periodic metrics logging (env: WATCHSTATS_METRICS_LOGS=0)
  -h, --help                   Show this help message

Examples:
  WatchStats --dir /var/log/app
  WatchStats --dir /var/log --workers 8 --interval 5000 --json-logs
```

### AC-3: DI Service Registration

**Given** a valid configuration  
**When** the DI container is built  
**Then** all services are registered as **singletons**:

```csharp
IServiceCollection services = new ServiceCollection();

// Logging
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(config.LogLevel);
    builder.AddConsole(options =>
    {
        options.FormatterName = config.JsonLogs 
            ? ConsoleFormatterNames.Json 
            : ConsoleFormatterNames.Simple;
    });
});

// Core components
services.AddSingleton(config); // CliConfig
services.AddSingleton<BoundedEventBus<FsEvent>>(sp => 
    new BoundedEventBus<FsEvent>(
        config.BusCapacity,
        sp.GetService<ILogger<BoundedEventBus<FsEvent>>>()));

services.AddSingleton<FileStateRegistry>(sp =>
    new FileStateRegistry(sp.GetService<ILogger<FileStateRegistry>>()));

services.AddSingleton<FileTailer>(sp => 
    new FileTailer(sp.GetService<ILogger<FileTailer>>()));

services.AddSingleton<FileProcessor>(sp =>
    new FileProcessor(
        sp.GetRequiredService<FileTailer>(),
        sp.GetService<ILogger<FileProcessor>>()));

services.AddSingleton<WorkerStats[]>(sp =>
{
    var stats = new WorkerStats[config.Workers];
    for (int i = 0; i < stats.Length; i++)
        stats[i] = new WorkerStats();
    return stats;
});

services.AddSingleton<ProcessingCoordinator>(sp =>
    new ProcessingCoordinator(
        sp.GetRequiredService<BoundedEventBus<FsEvent>>(),
        sp.GetRequiredService<FileStateRegistry>(),
        sp.GetRequiredService<FileProcessor>(),
        sp.GetRequiredService<WorkerStats[]>(),
        config.Workers,
        sp.GetService<ILogger<ProcessingCoordinator>>()));

services.AddSingleton<Reporter>(sp =>
    new Reporter(
        sp.GetRequiredService<WorkerStats[]>(),
        sp.GetRequiredService<BoundedEventBus<FsEvent>>(),
        config.TopK,
        config.IntervalMs,
        config.EnableMetricsLogs,
        sp.GetService<ILogger<Reporter>>()));

services.AddSingleton<FilesystemWatcherAdapter>(sp =>
    new FilesystemWatcherAdapter(
        config.WatchPath,
        sp.GetRequiredService<BoundedEventBus<FsEvent>>(),
        sp.GetService<ILogger<FilesystemWatcherAdapter>>()));

services.AddSingleton<AppOrchestrator>();

IServiceProvider provider = services.BuildServiceProvider();
```

### AC-4: AppOrchestrator Lifecycle

**AppOrchestrator** owns component lifecycle with the following methods:

```csharp
public sealed class AppOrchestrator
{
    private readonly ILogger<AppOrchestrator> _logger;
    private readonly FilesystemWatcherAdapter _watcher;
    private readonly ProcessingCoordinator _coordinator;
    private readonly Reporter _reporter;
    private readonly BoundedEventBus<FsEvent> _bus;

    public AppOrchestrator(
        FilesystemWatcherAdapter watcher,
        ProcessingCoordinator coordinator,
        Reporter reporter,
        BoundedEventBus<FsEvent> bus,
        ILogger<AppOrchestrator> logger)
    {
        _watcher = watcher;
        _coordinator = coordinator;
        _reporter = reporter;
        _bus = bus;
        _logger = logger;
    }

    public void Start();
    public void Stop();
    public void WaitForShutdown();
}
```

### AC-5: Startup Lifecycle Logs

**Given** AppOrchestrator.Start() is called  
**When** components start  
**Then** logs are emitted in order:

```json
{
  "ts": "2024-01-15T10:30:00.000Z",
  "level": "Information",
  "category": "WatchStats.Cli.AppOrchestrator",
  "eventName": "DI_START_SEQUENCE",
  "msg": "Starting application components"
}

{
  "ts": "2024-01-15T10:30:00.100Z",
  "level": "Information",
  "category": "WatchStats.Cli.AppOrchestrator",
  "eventName": "WATCHER_STARTED",
  "msg": "Filesystem watcher started",
  "watchPath": "/var/log/app"
}

{
  "ts": "2024-01-15T10:30:00.200Z",
  "level": "Information",
  "category": "WatchStats.Cli.AppOrchestrator",
  "eventName": "WORKERS_STARTED",
  "msg": "Processing workers started",
  "workerCount": 8
}

{
  "ts": "2024-01-15T10:30:00.300Z",
  "level": "Information",
  "category": "WatchStats.Cli.AppOrchestrator",
  "eventName": "REPORTER_STARTED",
  "msg": "Reporter started",
  "intervalMs": 2000
}

{
  "ts": "2024-01-15T10:30:00.400Z",
  "level": "Information",
  "category": "WatchStats.Cli.AppOrchestrator",
  "eventName": "DI_START_COMPLETE",
  "msg": "Application startup complete"
}
```

**If** any start step exceeds 5 seconds  
**Then** emit warning:

```json
{
  "eventName": "START_TIMEOUT_WARNING",
  "level": "Warning",
  "component": "WATCHER",
  "elapsedMs": 5200,
  "msg": "Component start exceeded timeout"
}
```

### AC-6: Shutdown Lifecycle Logs

**Given** AppOrchestrator.Stop() is called  
**When** components stop  
**Then** logs are emitted in order:

```json
{
  "eventName": "DI_STOP_SEQUENCE_BEGIN",
  "level": "Information",
  "msg": "Initiating shutdown sequence"
}

{
  "eventName": "WATCHER_STOPPED",
  "level": "Information",
  "msg": "Filesystem watcher stopped"
}

{
  "eventName": "BUS_STOPPED",
  "level": "Information",
  "msg": "Event bus stopped"
}

{
  "eventName": "WORKERS_STOPPED",
  "level": "Information",
  "msg": "Processing workers stopped"
}

{
  "eventName": "REPORTER_STOPPED",
  "level": "Information",
  "msg": "Reporter stopped"
}

{
  "eventName": "DI_STOP_COMPLETE",
  "level": "Information",
  "msg": "Shutdown sequence complete"
}
```

**If** any stop step exceeds 5 seconds  
**Then** emit warning (same schema as startup timeout)

### AC-7: CliConfig Record

```csharp
public sealed record CliConfig
{
    public string WatchPath { get; init; }
    public int Workers { get; init; }
    public int BusCapacity { get; init; }
    public int IntervalMs { get; init; }
    public int TopK { get; init; }
    public LogLevel LogLevel { get; init; }
    public bool JsonLogs { get; init; }
    public bool EnableMetricsLogs { get; init; }
}
```

### AC-8: Integration Test

**Given** a test configuration  
**When** DI provider is built  
**Then** all services resolve without exceptions  
**And** orchestrator can start/stop without errors  
**And** lifecycle logs are emitted in correct order

## Technical Notes

### NuGet Packages

Add to `WatchStats.Cli.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.0" />
```

### Core Remains DI-Free

- NO package references in `WatchStats.Core.csproj`
- Components accept `ILogger<T>?` as optional constructor parameter
- Null logger = no-op (no logging calls throw)

### Signal Handling

Preserve existing Ctrl+C and ProcessExit handlers:

```csharp
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    orchestrator.Stop();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) => orchestrator.Stop();
```

## Definition of Done

- [ ] CLI parsing supports all new arguments and environment variables
- [ ] Help text displayed correctly
- [ ] DI container builds successfully with all services
- [ ] AppOrchestrator manages lifecycle with structured logs
- [ ] Startup/shutdown logs match defined schemas
- [ ] Timeout warnings work correctly
- [ ] Integration test validates provider build
- [ ] Core project has zero DI dependencies
- [ ] Backward compatibility: existing CLI args still work
