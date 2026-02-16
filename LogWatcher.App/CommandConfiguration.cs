using System.CommandLine;
using System.CommandLine.Parsing;

namespace LogWatcher.App;

/// <summary>
/// Builds the CLI command structure using System.CommandLine.
/// Responsible for defining arguments, options, validators, and defaults.
/// </summary>
public static class CommandConfiguration
{
    /// <summary>
    /// Creates and configures the root CLI command with all arguments and options.
    /// </summary>
    /// <returns>Configured CliRootCommand ready for invocation.</returns>
    public static RootCommand CreateRootCommand()
    {
        // Define watchPath argument (required, positional)
        var watchPathArg = new Argument<string>("watchPath")
        {
            Description = "Directory path to watch for log file changes"
        };
        
        watchPathArg.Validators.Add(result =>
        {
            var path = result.GetValueOrDefault<string>();
            if (string.IsNullOrWhiteSpace(path))
            {
                result.AddError("watchPath cannot be empty");
            }
            else if (!Directory.Exists(path))
            {
                result.AddError($"watchPath does not exist: {path}");
            }
        });
        
        // Define --workers option with short alias -w
        var workersOpt = new Option<int>("--workers", new[] { "--workers", "-w" })
        {
            Description = "Number of worker threads for processing log files",
            DefaultValueFactory = _ => Environment.ProcessorCount
        };
        
        workersOpt.Validators.Add(result =>
        {
            try
            {
                var value = result.GetValueOrDefault<int>();
                if (value < 1)
                {
                    result.AddError($"--workers must be at least 1, got {value}");
                }
            }
            catch
            {
                // Parse error already recorded by System.CommandLine
            }
        });
        
        // Define --queue-capacity option with short alias -q
        var queueCapacityOpt = new Option<int>("--queue-capacity", new[] { "--queue-capacity", "-q" })
        {
            Description = "Maximum capacity of the filesystem event queue",
            DefaultValueFactory = _ => 10000
        };
        
        queueCapacityOpt.Validators.Add(result =>
        {
            try
            {
                var value = result.GetValueOrDefault<int>();
                if (value < 1)
                {
                    result.AddError($"--queue-capacity must be at least 1, got {value}");
                }
            }
            catch
            {
                // Parse error already recorded by System.CommandLine
            }
        });
        
        // Define --report-interval-seconds option with short alias -r
        var reportIntervalOpt = new Option<int>("--report-interval-seconds", new[] { "--report-interval-seconds", "-r" })
        {
            Description = "Interval in seconds between statistics reports",
            DefaultValueFactory = _ => 2
        };
        
        reportIntervalOpt.Validators.Add(result =>
        {
            try
            {
                var value = result.GetValueOrDefault<int>();
                if (value < 1)
                {
                    result.AddError($"--report-interval-seconds must be at least 1, got {value}");
                }
            }
            catch
            {
                // Parse error already recorded by System.CommandLine
            }
        });
        
        // Define --topk option with short alias -k
        var topKOpt = new Option<int>("--topk", new[] { "--topk", "-k" })
        {
            Description = "Number of top URLs to include in reports",
            DefaultValueFactory = _ => 10
        };
        
        topKOpt.Validators.Add(result =>
        {
            try
            {
                var value = result.GetValueOrDefault<int>();
                if (value < 1)
                {
                    result.AddError($"--topk must be at least 1, got {value}");
                }
            }
            catch
            {
                // Parse error already recorded by System.CommandLine
            }
        });
        
        // Build root command
        var rootCommand = new RootCommand("High-performance log file watcher with real-time statistics")
        {
            watchPathArg,
            workersOpt,
            queueCapacityOpt,
            reportIntervalOpt,
            topKOpt
        };
        
        // Register command handler
        rootCommand.SetAction(parseResult =>
        {
            var watchPath = parseResult.GetValue(watchPathArg)!;
            var workers = parseResult.GetValue(workersOpt);
            var queueCapacity = parseResult.GetValue(queueCapacityOpt);
            var reportInterval = parseResult.GetValue(reportIntervalOpt);
            var topK = parseResult.GetValue(topKOpt);
            
            // Create CliConfig (validation happens in constructor)
            CliConfig config;
            try
            {
                config = new CliConfig(watchPath, workers, queueCapacity, reportInterval, topK);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                Environment.Exit(2);
                return;
            }
            
            // Delegate to ApplicationHost
            var exitCode = ApplicationHost.Run(config);
            Environment.Exit(exitCode);
        });
        
        return rootCommand;
    }
}
