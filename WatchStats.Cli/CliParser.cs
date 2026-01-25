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

        int workers = Environment.ProcessorCount;
        int queueCapacity = 10000;
        int reportIntervalSeconds = 2;
        int topK = 10;
        string? watchPath = null;

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
                    case "--queue-capacity":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--queue-capacity requires a value";
                                return false;
                            }
                        }

                        if (!int.TryParse(val, out queueCapacity))
                        {
                            error = "invalid --queue-capacity value";
                            return false;
                        }

                        break;
                    case "--report-interval-seconds":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                error = "--report-interval-seconds requires a value";
                                return false;
                            }
                        }

                        if (!int.TryParse(val, out reportIntervalSeconds))
                        {
                            error = "invalid --report-interval-seconds value";
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
            error = "missing watchPath";
            return false;
        }

        try
        {
            config = new CliConfig(watchPath, workers, queueCapacity, reportIntervalSeconds, topK);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
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