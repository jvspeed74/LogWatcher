using Microsoft.Extensions.Logging;

namespace WatchStats.Cli;

/// <summary>
/// Command-line parser for the WatchStats.Core application.
/// </summary>
public static class CliParser
{
    /// <summary>
    /// Attempts to parse command-line arguments into an <see cref="CliConfig"/> instance.
    /// On success returns <c>true</c> and sets <paramref name="config"/>; on failure returns <c>false</c> and sets <paramref name="error"/>.
    /// This method never throws on parse errors.
    /// </summary>
    /// <param name="args">Array of command-line arguments.</param>
    /// <param name="config">On success receives a validated <see cref="CliConfig"/> instance; otherwise <c>null</c>.</param>
    /// <param name="error">On failure receives an error string (or "help" when help was requested).</param>
    /// <returns>True when parsing succeeded and <paramref name="config"/> is set; otherwise false.</returns>
    public static bool TryParse(string[] args, out CliConfig? config, out string? error)
    {
        config = null;
        error = null;

        int workers = GetEnvInt("WATCHSTATS_WORKERS", Environment.ProcessorCount);
        int busCapacity = GetEnvInt("WATCHSTATS_BUS_CAPACITY", 10000);
        int intervalMs = GetEnvInt("WATCHSTATS_REPORT_INTERVAL", 2000);
        int topK = GetEnvInt("WATCHSTATS_TOPK", 10);
        Microsoft.Extensions.Logging.LogLevel logLevel = GetEnvLogLevel("WATCHSTATS_LOG_LEVEL", Microsoft.Extensions.Logging.LogLevel.Information);
        bool jsonLogs = GetEnvBool("WATCHSTATS_JSON_LOGS", false);
        bool enableMetricsLogs = !GetEnvBool("WATCHSTATS_METRICS_LOGS", true, invertZero: true);
        string? watchPath = Environment.GetEnvironmentVariable("WATCHSTATS_DIRECTORY");

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.IsNullOrEmpty(a)) continue;

            if (a.StartsWith("--"))
            {
                string opt;
                string? val = null;
                int eq = a.IndexOf('=');
                if (eq >= 0)
                {
                    opt = a.Substring(0, eq);
                    val = a.Substring(eq + 1);
                }
                else
                {
                    opt = a;
                }

                switch (opt)
                {
                    case "--dir":
                    case "--directory":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--dir requires a value";
                                return false;
                            }
                        }
                        watchPath = val;
                        break;
                    case "--workers":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--workers requires a value";
                                return false;
                            }
                        }

                        if (!int.TryParse(val, out workers))
                        {
                            error = "invalid --workers value";
                            return false;
                        }

                        break;
                    case "--capacity":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--capacity requires a value";
                                return false;
                            }
                        }

                        if (!int.TryParse(val, out busCapacity))
                        {
                            error = "invalid --capacity value";
                            return false;
                        }

                        break;
                    case "--interval":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--interval requires a value";
                                return false;
                            }
                        }

                        if (!int.TryParse(val, out intervalMs))
                        {
                            error = "invalid --interval value";
                            return false;
                        }

                        break;
                    case "--topk":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--topk requires a value";
                                return false;
                            }
                        }

                        if (!int.TryParse(val, out topK))
                        {
                            error = "invalid --topk value";
                            return false;
                        }

                        break;
                    case "--logLevel":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--logLevel requires a value";
                                return false;
                            }
                        }

                        if (!Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(val, true, out logLevel))
                        {
                            error = "invalid --logLevel value (valid: Trace, Debug, Information, Warning, Error, Critical)";
                            return false;
                        }

                        break;
                    case "--json-logs":
                        jsonLogs = true;
                        break;
                    case "--no-metrics-logs":
                        enableMetricsLogs = false;
                        break;
                    case "--help":
                    case "-h":
                        error = "help";
                        return false;
                    default:
                        error = $"unknown option: {opt}";
                        return false;
                }
            }
            else
            {
                if (watchPath == null) watchPath = a;
                else
                {
                    error = "unexpected positional argument";
                    return false;
                }
            }
        }

        if (string.IsNullOrEmpty(watchPath))
        {
            error = "missing required argument: --dir <path>";
            return false;
        }

        try
        {
            config = new CliConfig(watchPath, workers, busCapacity, intervalMs, topK, logLevel, jsonLogs, enableMetricsLogs);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
    
    private static int GetEnvInt(string envVar, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        return value != null && int.TryParse(value, out var result) ? result : defaultValue;
    }
    
    private static Microsoft.Extensions.Logging.LogLevel GetEnvLogLevel(string envVar, Microsoft.Extensions.Logging.LogLevel defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        return value != null && Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(value, true, out var result) ? result : defaultValue;
    }
    
    private static bool GetEnvBool(string envVar, bool defaultValue, bool invertZero = false)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (value == null) return defaultValue;
        
        // Handle "0" and "1" explicitly
        if (value == "0") return invertZero ? true : false;
        if (value == "1") return invertZero ? false : true;
        
        // Handle true/false strings
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Attempts to consume the next argument value from <paramref name="args"/>, advancing <paramref name="i"/>.
    /// </summary>
    /// <param name="args">Argument array.</param>
    /// <param name="i">Index of the current argument; will be advanced when a value is consumed.</param>
    /// <param name="value">On success receives the consumed value.</param>
    /// <returns>True when a value was consumed; otherwise false.</returns>
    private static bool TryConsumeValue(string[] args, ref int i, out string? value)
    {
        value = null;
        if (i + 1 >= args.Length) return false;
        i++;
        value = args[i];
        return true;
    }
}