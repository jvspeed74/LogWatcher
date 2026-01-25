using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using WatchStats.Cli;
using WatchStats.Core.Concurrency;
using WatchStats.Core.Events;
using WatchStats.Core.IO;
using WatchStats.Core.Metrics;
using WatchStats.Core.Processing;

// Parse CLI arguments
if (!CliParser.TryParse(args, out var config, out var parseError))
{
    if (parseError == "help")
    {
        ShowHelp();
        return;
    }

    Console.Error.WriteLine("Error: " + parseError);
    Environment.Exit(2);
}

// Build DI container
var services = new ServiceCollection();

// Logging configuration
services.AddLogging(builder =>
{
    builder.SetMinimumLevel(config.LogLevel);
    builder.AddConsole(options =>
    {
        options.FormatterName = config.JsonLogs 
            ? ConsoleFormatterNames.Json 
            : ConsoleFormatterNames.Simple;
    });
    
    // Apply per-category overrides from environment
    ApplyCategoryOverrides(builder);
});

// Configuration
services.AddSingleton(config);

// Core components (all singletons)
services.AddSingleton<BoundedEventBus<FsEvent>>(sp => 
    new BoundedEventBus<FsEvent>(config.BusCapacity));

services.AddSingleton<FileStateRegistry>(sp =>
    new FileStateRegistry());

services.AddSingleton<FileTailer>(sp => 
    new FileTailer());

services.AddSingleton<FileProcessor>(sp =>
    new FileProcessor(sp.GetRequiredService<FileTailer>()));

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
        config.Workers));

services.AddSingleton<Reporter>(sp =>
    new Reporter(
        sp.GetRequiredService<WorkerStats[]>(),
        sp.GetRequiredService<BoundedEventBus<FsEvent>>(),
        config.TopK,
        config.IntervalMs / 1000)); // Convert ms to seconds for now

services.AddSingleton<FilesystemWatcherAdapter>(sp =>
    new FilesystemWatcherAdapter(
        config.WatchPath,
        sp.GetRequiredService<BoundedEventBus<FsEvent>>()));

services.AddSingleton<AppOrchestrator>();

// Build service provider
var provider = services.BuildServiceProvider();

// Get orchestrator and start
var orchestrator = provider.GetRequiredService<AppOrchestrator>();

int exitCode;
try
{
    // Register shutdown handlers
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        orchestrator.Stop();
    };
    AppDomain.CurrentDomain.ProcessExit += (_, _) => orchestrator.Stop();

    // Start components
    orchestrator.Start();

    // Wait until shutdown is requested
    orchestrator.WaitForShutdown();
    exitCode = 0;
}
catch (Exception ex)
{
    var logger = provider.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Fatal error during application lifecycle");
    exitCode = 1;
}
finally
{
    // Ensure cleanup
    orchestrator.Stop();
    provider.Dispose();
}

Environment.Exit(exitCode);

static void ShowHelp()
{
    Console.WriteLine(@"Usage: WatchStats --dir <path> [options]

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
  
Environment Variables:
  WATCHSTATS_DIRECTORY         Watch directory
  WATCHSTATS_WORKERS           Worker count (clamped to [1, 64])
  WATCHSTATS_BUS_CAPACITY      Bus capacity (clamped to [1000, 1000000])
  WATCHSTATS_REPORT_INTERVAL   Report interval in ms (clamped to [500, 60000])
  WATCHSTATS_LOG_LEVEL         Minimum log level
  WATCHSTATS_JSON_LOGS         Enable JSON logs (1=true, 0=false)
  WATCHSTATS_METRICS_LOGS      Enable metrics logs (1=true, 0=false)
  
  Per-category log levels:
  WATCHSTATS_LOG_LEVEL_TAILER=Debug
  WATCHSTATS_LOG_LEVEL_WATCHER=Warning
  WATCHSTATS_LOG_LEVEL_BUS=Information
  WATCHSTATS_LOG_LEVEL_REGISTRY=Warning
  WATCHSTATS_LOG_LEVEL_PROCESSOR=Debug
  WATCHSTATS_LOG_LEVEL_COORDINATOR=Information
  WATCHSTATS_LOG_LEVEL_REPORTER=Information
");
}

static void ApplyCategoryOverrides(ILoggingBuilder builder)
{
    var overrides = new Dictionary<string, string>
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
        if (value != null && Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(value, true, out var level))
        {
            builder.AddFilter(category, level);
        }
    }
}