using LogWatcher.Core.Backpressure;

using Microsoft.Extensions.Logging;

namespace LogWatcher.Core.Ingestion
{
    /// <summary>
    /// Adapter that wraps <see cref="FileSystemWatcher"/> and publishes <see cref="FsEvent"/> events to a <see cref="BoundedEventBus{T}"/>.
    /// </summary>
    public sealed partial class FilesystemWatcherAdapter : IDisposable
    {
        private readonly BoundedEventBus<FsEvent> _bus;
        private readonly Func<string, bool> _isProcessable;
        private readonly ILogger<FilesystemWatcherAdapter>? _logger;
        private FileSystemWatcher? _watcher;
        private long _errorCount;

        /// <summary>
        /// Creates a new adapter for the specified path. If <paramref name="isProcessable"/> is null a default predicate
        /// that accepts .log and .txt files is used.
        /// </summary>
        /// <param name="path">Directory path to watch.</param>
        /// <param name="bus">Event bus to publish discovered events to.</param>
        /// <param name="isProcessable">Optional predicate to filter which file paths are considered processable.</param>
        /// <param name="logger">Optional logger for FSW errors and publish exceptions.</param>
        public FilesystemWatcherAdapter(string path, BoundedEventBus<FsEvent> bus,
            Func<string, bool>? isProcessable = null, ILogger<FilesystemWatcherAdapter>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(bus);
            _bus = bus;
            _isProcessable = isProcessable ?? DefaultIsProcessable;
            _logger = logger;

            // Pre-create watcher but do not enable until Start()
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.*",
                InternalBufferSize = 64 * 1024
            };

            _watcher.Created += OnCreated;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
        }

        /// <summary>
        /// Number of watcher errors observed. This counter is incremented when the underlying <see cref="FileSystemWatcher"/> raises an error.
        /// </summary>
        public long ErrorCount => Interlocked.Read(ref _errorCount);

        /// <summary>
        /// Enables the underlying <see cref="FileSystemWatcher"/> to begin raising events. Throws <see cref="ObjectDisposedException"/>
        /// if the adapter has been disposed.
        /// </summary>
        public void Start()
        {
            ObjectDisposedException.ThrowIf(_watcher == null, this);
            _watcher.EnableRaisingEvents = true;
            if (_logger != null) LogWatcherStarted(_logger, _watcher.Path);
        }

        /// <summary>
        /// Disables event raising. This method is safe to call multiple times.
        /// </summary>
        public void Stop()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            if (_logger != null) LogWatcherStopped(_logger);
        }

        private bool DefaultIsProcessable(string path)
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.TrimStart('.');
            return string.Equals(ext, "log", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, "txt", StringComparison.OrdinalIgnoreCase);
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            PublishEvent(FsEventKind.Created, e.FullPath, null);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            PublishEvent(FsEventKind.Modified, e.FullPath, null);
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            PublishEvent(FsEventKind.Deleted, e.FullPath, null);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            PublishEvent(FsEventKind.Renamed, e.FullPath, e.OldFullPath);
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Interlocked.Increment(ref _errorCount);
            // Pre-extract exception so the local reference (not the method call) is passed to LogFswError.
            var ex = e.GetException();
            if (_logger != null) LogFswError(_logger, ex);
        }

        private void PublishEvent(FsEventKind kind, string path, string? oldPath)
        {
            try
            {
                bool processable = _isProcessable(path);
                var ev = new FsEvent(kind, path, oldPath, DateTimeOffset.UtcNow, processable);
                if (_bus.Publish(ev))
                {
                    if (_logger != null) LogEventPublished(_logger, kind, path, processable);
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) LogPublishException(_logger, kind, path, ex);
            }
        }

        /// <summary>
        /// Disposes the adapter and releases the underlying <see cref="FileSystemWatcher"/>. Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            if (_watcher != null)
            {
                Stop();
                _watcher.Created -= OnCreated;
                _watcher.Changed -= OnChanged;
                _watcher.Deleted -= OnDeleted;
                _watcher.Renamed -= OnRenamed;
                _watcher.Error -= OnError;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "FileSystemWatcher error")]
        private static partial void LogFswError(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Exception publishing event kind={Kind} path={Path}")]
        private static partial void LogPublishException(ILogger logger, FsEventKind kind, string path, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Watcher started path={Path}")]
        private static partial void LogWatcherStarted(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Watcher stopped")]
        private static partial void LogWatcherStopped(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Event published kind={Kind} path={Path} processable={Processable}")]
        private static partial void LogEventPublished(ILogger logger, FsEventKind kind, string path, bool processable);
    }
}