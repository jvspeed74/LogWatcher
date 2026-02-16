namespace LogWatcher.App;

/// <summary>
/// Kinds of parse errors that can occur during argument parsing.
/// </summary>
public enum ParseErrorKind
{
    /// <summary>Unknown option was provided.</summary>
    UnknownOption,
    /// <summary>Required value for an option is missing.</summary>
    MissingValue,
    /// <summary>Invalid value format for an option.</summary>
    InvalidValue,
    /// <summary>Required positional argument is missing.</summary>
    MissingPath,
    /// <summary>Unexpected positional argument.</summary>
    UnexpectedArgument,
    /// <summary>Configuration validation failed.</summary>
    ValidationError
}

/// <summary>
/// Represents a parse error with kind and message.
/// </summary>
public sealed record ParseError(ParseErrorKind Kind, string Message);

/// <summary>
/// Result of parsing command-line arguments.
/// </summary>
public sealed record ParserResult(bool IsSuccess, bool IsHelp, CliConfig? Config, ParseError? Error);

/// <summary>
/// Command-line parser for the LogWatcher.Core application.
/// </summary>
public static class CliParser
{
    /// <summary>
    /// Usage text displayed when --help is requested or on error.
    /// </summary>
    public const string UsageText = "Usage: WatchStats <watchPath> [--workers N] [--queue-capacity N] [--report-interval-seconds N] [--topk N]";

    /// <summary>
    /// Parses command-line arguments into a structured result.
    /// This method never throws on parse errors.
    /// </summary>
    /// <param name="args">Array of command-line arguments.</param>
    /// <returns>A <see cref="ParserResult"/> indicating success, help request, or error details.</returns>
    public static ParserResult Parse(string[] args)
    {
        int workers = Environment.ProcessorCount;
        int queueCapacity = 10000;
        int reportIntervalSeconds = 2;
        int topK = 10;
        string? watchPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.IsNullOrEmpty(a)) continue;

            if (a.StartsWith("--") || a.StartsWith("-"))
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
                                return new ParserResult(false, false, null, 
                                    new ParseError(ParseErrorKind.MissingValue, "--workers requires a value"));
                            }
                        }

                        if (!int.TryParse(val, out workers))
                        {
                            return new ParserResult(false, false, null,
                                new ParseError(ParseErrorKind.InvalidValue, "invalid --workers value"));
                        }

                        break;
                    case "--queue-capacity":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                return new ParserResult(false, false, null,
                                    new ParseError(ParseErrorKind.MissingValue, "--queue-capacity requires a value"));
                            }
                        }

                        if (!int.TryParse(val, out queueCapacity))
                        {
                            return new ParserResult(false, false, null,
                                new ParseError(ParseErrorKind.InvalidValue, "invalid --queue-capacity value"));
                        }

                        break;
                    case "--report-interval-seconds":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                return new ParserResult(false, false, null,
                                    new ParseError(ParseErrorKind.MissingValue, "--report-interval-seconds requires a value"));
                            }
                        }

                        if (!int.TryParse(val, out reportIntervalSeconds))
                        {
                            return new ParserResult(false, false, null,
                                new ParseError(ParseErrorKind.InvalidValue, "invalid --report-interval-seconds value"));
                        }

                        break;
                    case "--topk":
                        if (val == null)
                        {
                            if (!TryConsumeValue(args, ref i, out val))
                            {
                                return new ParserResult(false, false, null,
                                    new ParseError(ParseErrorKind.MissingValue, "--topk requires a value"));
                            }
                        }

                        if (!int.TryParse(val, out topK))
                        {
                            return new ParserResult(false, false, null,
                                new ParseError(ParseErrorKind.InvalidValue, "invalid --topk value"));
                        }

                        break;
                    case "--help":
                    case "-h":
                        return new ParserResult(false, true, null, null);
                    default:
                        return new ParserResult(false, false, null,
                            new ParseError(ParseErrorKind.UnknownOption, $"unknown option: {opt}"));
                }
            }
            else
            {
                if (watchPath == null) watchPath = a;
                else
                {
                    return new ParserResult(false, false, null,
                        new ParseError(ParseErrorKind.UnexpectedArgument, "unexpected positional argument"));
                }
            }
        }

        if (string.IsNullOrEmpty(watchPath))
        {
            return new ParserResult(false, false, null,
                new ParseError(ParseErrorKind.MissingPath, "missing watchPath"));
        }

        try
        {
            var config = new CliConfig(watchPath, workers, queueCapacity, reportIntervalSeconds, topK);
            return new ParserResult(true, false, config, null);
        }
        catch (Exception ex)
        {
            return new ParserResult(false, false, null,
                new ParseError(ParseErrorKind.ValidationError, ex.Message));
        }
    }

    /// <summary>
    /// Attempts to parse command-line arguments into an <see cref="CliConfig"/> instance.
    /// On success returns <c>true</c> and sets <paramref name="config"/>; on failure returns <c>false</c> and sets <paramref name="error"/>.
    /// This method never throws on parse errors.
    /// </summary>
    /// <param name="args">Array of command-line arguments.</param>
    /// <param name="config">On success receives a validated <see cref="CliConfig"/> instance; otherwise <c>null</c>.</param>
    /// <param name="error">On failure receives an error string (or "help" when help was requested).</param>
    /// <returns>True when parsing succeeded and <paramref name="config"/> is set; otherwise false.</returns>
    [Obsolete("Use Parse() for structured error handling. This method is kept for backward compatibility.")]
    public static bool TryParse(string[] args, out CliConfig? config, out string? error)
    {
        var result = Parse(args);
        
        if (result.IsSuccess)
        {
            config = result.Config;
            error = null;
            return true;
        }

        config = null;
        if (result.IsHelp)
        {
            error = "help";
        }
        else
        {
            error = result.Error?.Message ?? "unknown error";
        }
        return false;
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