using LogWatcher.Core.Backpressure;
using LogWatcher.Core.Coordination;
using LogWatcher.Core.FileManagement;
using LogWatcher.Core.Ingestion;
using LogWatcher.Core.Statistics;

using Microsoft.Extensions.Logging;

namespace LogWatcher.Core.Processing
{
    /// <summary>
    /// Coordinates worker threads that dequeue filesystem events and dispatch processing work.
    /// </summary>
    public sealed partial class ProcessingCoordinator
    {
        private readonly BoundedEventBus<FsEvent> _bus;
        private readonly FileStateRegistry _registry;
        private readonly IFileProcessor _processor;
        private readonly WorkerStats[] _workerStats;
        private readonly Thread[] _threads;
        private readonly int _dequeueTimeoutMs;
        private readonly ILogger<ProcessingCoordinator>? _logger;

        private bool _stopping;

        /// <summary>
        /// Creates a new <see cref="ProcessingCoordinator"/>.
        /// </summary>
        /// <param name="bus">Event bus that supplies filesystem events.</param>
        /// <param name="registry">Registry for per-file state.</param>
        /// <param name="processor">Processor used to read and parse file data.</param>
        /// <param name="workerStats">Preallocated per-worker stats containers (length determines worker count).</param>
        /// <param name="workerCount">Requested worker count; the actual count will be at least 1.</param>
        /// <param name="dequeueTimeoutMs">Timeout in milliseconds for bus dequeue operations; clamped to a minimum of 10ms.</param>
        /// <param name="logger">Optional logger for join timeouts and debug events.</param>
        public ProcessingCoordinator(BoundedEventBus<FsEvent> bus, FileStateRegistry registry, IFileProcessor processor,
            WorkerStats[] workerStats, int workerCount = 4, int dequeueTimeoutMs = 200,
            ILogger<ProcessingCoordinator>? logger = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _workerStats = workerStats ?? throw new ArgumentNullException(nameof(workerStats));
            _logger = logger;

            int wc = Math.Max(1, workerCount);
            _dequeueTimeoutMs = Math.Max(10, dequeueTimeoutMs);

            _threads = new Thread[wc];
            for (int i = 0; i < wc; i++)
            {
                int idx = i;
                _threads[i] = new Thread(() => WorkerLoop(idx)) { IsBackground = true, Name = $"ws-{i}" };
            }
        }

        /// <summary>
        /// Starts the worker threads. This method may be called once to begin processing.
        /// </summary>
        public void Start()
        {
            Volatile.Write(ref _stopping, false);
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i].Start();
                if (_logger != null) LogWorkerStarted(_logger, i);
            }
        }

        /// <summary>
        /// Requests an orderly shutdown of the worker threads. This method signals stop to the underlying event bus
        /// and waits briefly for threads to terminate; if a thread fails to join it is interrupted.
        /// </summary>
        public void Stop()
        {
            Volatile.Write(ref _stopping, true);
            _bus.Stop();
            try
            {
                for (int i = 0; i < _threads.Length; i++)
                {
                    var t = _threads[i];
                    if (!t.Join(2000))
                    {
                        if (_logger != null) LogWorkerJoinTimeout(_logger, i);
                        t.Interrupt();
                    }
                }
            }
            catch
            {
            }
        }

        private void WorkerLoop(int workerIndex)
        {
            var stats = _workerStats[workerIndex];
            while (!Volatile.Read(ref _stopping))
            {
                if (!_bus.TryDequeue(out var ev, _dequeueTimeoutMs))
                {
                    // nothing dequeued; allow timely swap acknowledgements and continue loop
                    stats.AcknowledgeSwapIfRequested();
                    continue;
                }

                // Increment filesystem event counter in active stats
                stats.Active.IncrementFsEvent(ToStatEventKind(ev.Kind));

                // Route event
                switch (ev.Kind)
                {
                    case FsEventKind.Created:
                    case FsEventKind.Modified:
                        if (ev.Processable)
                            HandleCreateOrModify(ev.Path, stats);
                        break;
                    case FsEventKind.Deleted:
                        HandleDelete(ev.Path, stats.Active);
                        break;
                    case FsEventKind.Renamed:
                        if (!string.IsNullOrEmpty(ev.OldPath))
                            HandleDelete(ev.OldPath, stats.Active);
                        if (ev.Processable)
                            HandleCreateOrModify(ev.Path, stats);
                        break;
                }

                // Acknowledge swap after fully handling this event
                stats.AcknowledgeSwapIfRequested();
            }

            if (_logger != null) LogWorkerStopped(_logger, workerIndex);
        }

        private void HandleCreateOrModify(string path, WorkerStats stats)
        {
            var state = _registry.GetOrCreate(path);
            var buffer = stats.Active;
            // try acquire gate
            if (!Monitor.TryEnter(state.Gate))
            {
                state.MarkDirtyIfAllowed();
                buffer.CoalescedDueToBusyGate++;
                if (_logger != null) LogGateCoalesced(_logger, path);
                return;
            }

            try
            {
                if (state.IsDeletePending)
                {
                    buffer.SkippedDueToDeletePending++;
                    _registry.FinalizeDelete(path);
                    buffer.FileStateRemovedCount++;
                    if (_logger != null) LogSkippedDeletePending(_logger, path);
                    return;
                }

                // catch-up loop
                while (true)
                {
                    // allow timely swap acknowledgements to be processed between iterations
                    stats.AcknowledgeSwapIfRequested();

                    if (state.IsDeletePending)
                    {
                        _registry.FinalizeDelete(path);
                        buffer.FileStateRemovedCount++;
                        return;
                    }

                    // process once
                    _processor.ProcessOnce(path, state, buffer);

                    // allow timely swap acknowledgements after processing
                    stats.AcknowledgeSwapIfRequested();

                    if (state.IsDeletePending)
                    {
                        _registry.FinalizeDelete(path);
                        buffer.FileStateRemovedCount++;
                        return;
                    }

                    if (state.IsDirty)
                    {
                        state.ClearDirty();
                        continue; // immediate re-read
                    }

                    break;
                }
            }
            finally
            {
                Monitor.Exit(state.Gate);
            }
        }

        private void HandleDelete(string path, WorkerStatsBuffer stats)
        {
            if (!_registry.TryGet(path, out var state))
            {
                if (_logger != null) LogDeleteUntracked(_logger, path);
                return;
            }

            // try to acquire gate
            if (!Monitor.TryEnter(state.Gate))
            {
                state.MarkDeletePending();
                stats.DeletePendingSetCount++;
                if (_logger != null) LogDeletePendingSet(_logger, path);
                return;
            }

            try
            {
                state.MarkDeletePending();
                _registry.FinalizeDelete(path);
                stats.FileStateRemovedCount++;
            }
            finally
            {
                Monitor.Exit(state.Gate);
            }
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Worker thread {WorkerIndex} failed to join within timeout")]
        private static partial void LogWorkerJoinTimeout(ILogger logger, int workerIndex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Gate busy, event coalesced path={Path}")]
        private static partial void LogGateCoalesced(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Delete pending set, gate busy path={Path}")]
        private static partial void LogDeletePendingSet(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Worker started workerIndex={WorkerIndex}")]
        private static partial void LogWorkerStarted(ILogger logger, int workerIndex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Worker stopped workerIndex={WorkerIndex}")]
        private static partial void LogWorkerStopped(ILogger logger, int workerIndex);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Delete event for untracked path={Path}")]
        private static partial void LogDeleteUntracked(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped create/modify, delete pending path={Path}")]
        private static partial void LogSkippedDeletePending(ILogger logger, string path);

        private static StatEventKind ToStatEventKind(FsEventKind kind) => kind switch
        {
            FsEventKind.Created  => StatEventKind.Created,
            FsEventKind.Modified => StatEventKind.Modified,
            FsEventKind.Deleted  => StatEventKind.Deleted,
            FsEventKind.Renamed  => StatEventKind.Renamed,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}