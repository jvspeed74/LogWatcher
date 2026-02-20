using LogWatcher.Core.Backpressure;

namespace LogWatcher.Core.Ingestion
{
    /// <summary>
    /// Adapter that wraps <see cref="FileSystemWatcher"/> and publishes <see cref="FsEvent"/> events to a <see cref="BoundedEventBus{T}"/>.
    /// </summary>
    public sealed class FilesystemWatcherAdapter : IDisposable
    {
        private readonly BoundedEventBus<FsEvent> _bus;
        private readonly Func<string, bool> _isProcessable;
        private FileSystemWatcher? _watcher;
        private long _errorCount;

        /// <summary>
        /// Creates a new adapter for the specified path. If <paramref name="isProcessable"/> is null a default predicate
        /// that accepts .log and .txt files is used.
        /// </summary>
        /// <param name="path">Directory path to watch.</param>
        /// <param name="bus">Event bus to publish discovered events to.</param>
        /// <param name="isProcessable">Optional predicate to filter which file paths are considered processable.</param>
        public FilesystemWatcherAdapter(string path, BoundedEventBus<FsEvent> bus,
            Func<string, bool>? isProcessable = null)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(bus);
            _bus = bus;
            _isProcessable = isProcessable ?? DefaultIsProcessable;

            // TODO: Consider validating that the path exists and is a directory before creating the watcher
            // Pre-create watcher but do not enable until Start()
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.*",
                // TODO: Make internal buffer size configurable to handle high-volume file change scenarios
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
        }

        /// <summary>
        /// Disables event raising. This method is safe to call multiple times.
        /// </summary>
        public void Stop()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
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
            // TODO: Add structured logging for FileSystemWatcher errors to diagnose buffer overflow issues
            // do not rethrow; just record
        }

        private void PublishEvent(FsEventKind kind, string path, string? oldPath)
        {
            try
            {
                bool processable = _isProcessable(path);
                var ev = new FsEvent(kind, path, oldPath, DateTimeOffset.UtcNow, processable);
                _bus.Publish(ev);
            }
            catch
            {
                // Swallow any exceptions to keep handlers lightweight
                // TODO: Add structured logging for exceptions in event publishing (path, kind, exception details)
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
    }
}