using Microsoft.Extensions.Logging;

namespace LogWatcher.Tests.Helpers;

/// <summary>
/// In-memory <see cref="ILogger{T}"/> that records all log entries for assertion in tests.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    public bool HasWarning(string fragment) =>
        _entries.Any(e => e.Level >= LogLevel.Warning && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    bool ILogger.IsEnabled(LogLevel logLevel) => true;

    void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _entries.Add((logLevel, formatter(state, exception)));
}