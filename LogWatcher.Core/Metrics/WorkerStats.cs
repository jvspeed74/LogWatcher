namespace LogWatcher.Core.Metrics
{
    /// <summary>
    /// Per-worker double-buffered statistics container.
    /// Supports a swap/ack protocol used by the reporter to safely collect per-worker metrics.
    /// </summary>
    public sealed class WorkerStats
    {
        private readonly WorkerStatsBuffer _a;
        private readonly WorkerStatsBuffer _b;

        private WorkerStatsBuffer _active;
        private WorkerStatsBuffer _inactive;

        private int _swapRequested;
        private readonly ManualResetEventSlim _swapAck;

        /// <summary>
        /// Creates a new pairing of worker stats buffers. <paramref name="messageInitialCapacity"/> sets the initial capacity for message-count dictionaries.
        /// </summary>
        /// <param name="messageInitialCapacity">Initial capacity for message counts in buffers.</param>
        public WorkerStats(int messageInitialCapacity = 256)
        {
            _a = new WorkerStatsBuffer(messageInitialCapacity);
            _b = new WorkerStatsBuffer(messageInitialCapacity);

            _active = _a;
            _inactive = _b;

            _swapRequested = 0;
            _swapAck = new ManualResetEventSlim(true); // initially acknowledged
        }

        /// <summary>Worker-visible active buffer to record metrics into.</summary>
        public WorkerStatsBuffer Active => _active;

        /// <summary>Reporter-visible inactive buffer to be merged after a swap. Call <see cref="WaitForSwapAck"/> before reading.</summary>
        public WorkerStatsBuffer Inactive => _inactive;

        /// <summary>
        /// Reporter requests a swap; the worker will perform swap at its next convenient point and acknowledge it.
        /// </summary>
        public void RequestSwap()
        {
            _swapAck.Reset();
            Volatile.Write(ref _swapRequested, 1);
        }

        /// <summary>
        /// Blocks until the worker acknowledges a requested swap. Throws if the provided cancellation token is cancelled.
        /// </summary>
        /// <param name="ct">Cancellation token to abort waiting.</param>
        public void WaitForSwapAck(CancellationToken ct)
        {
            _swapAck.Wait(ct);
        }

        /// <summary>
        /// Called by workers at safe points to perform the swap when requested and set the acknowledgment.
        /// </summary>
        public void AcknowledgeSwapIfRequested()
        {
            if (Volatile.Read(ref _swapRequested) == 0)
                return;

            // perform swap: swap references
            var prevActive = _active;
            _active = _inactive;
            _inactive = prevActive;

            // reset new active buffer so workers start fresh
            _active.Reset();

            // clear request
            Volatile.Write(ref _swapRequested, 0);

            // set ack so reporter can proceed
            _swapAck.Set();
        }

        /// <summary>
        /// Returns the inactive buffer; reporter should call this only after <see cref="WaitForSwapAck"/>.
        /// </summary>
        public WorkerStatsBuffer GetInactiveBufferForMerge()
        {
            return _inactive;
        }
    }
}