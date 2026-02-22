using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace LogWatcher.Core.Backpressure
{
    /// <summary>
    /// Thread-safe bounded FIFO event bus with drop-newest semantics when full.
    /// </summary>
    /// <typeparam name="T">Type of events carried by the bus.</typeparam>
    public sealed class BoundedEventBus<T>
    {
        // Channel provides lock-free producer/consumer coordination. FullMode=Wait is used
        // so that TryWrite returns false when the channel is full; we treat that false return
        // as a drop (BP-002). DropWrite would return true even for dropped items, making the
        // return value useless for distinguishing published from dropped.
        private readonly Channel<T> _channel;

        // Separate stopped flag lets Publish distinguish a capacity drop from a post-Stop
        // return so that _dropped is not incremented for post-Stop publish calls.
        private volatile bool _stopped;

        private long _published;
        private long _dropped;

        /// <summary>
        /// Creates a new bounded event bus with the provided capacity.
        /// </summary>
        /// <param name="capacity">Maximum number of items the bus will hold; must be &gt; 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
        public BoundedEventBus(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
            {
                // Wait mode: TryWrite returns false when full (no blocking — we never call
                // WriteAsync). This gives us a clean false return to count as a drop.
                // DropWrite would return true even for dropped items, making the return
                // value useless for distinguishing published from dropped.
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = false,
                AllowSynchronousContinuations = false,
            });
        }

        /// <summary>
        /// Publishes an item to the bus. Returns <c>true</c> if the item was enqueued; <c>false</c> if the bus is stopped or the item was dropped due to capacity.
        /// </summary>
        /// <param name="item">Item to publish.</param>
        /// <returns>True when enqueued; false if dropped or stopped.</returns>
        public bool Publish(T item)
        {
            // Return without counting as dropped — Stop is not a capacity event.
            if (_stopped)
                return false;

            if (_channel.Writer.TryWrite(item))
            {
                Interlocked.Increment(ref _published);
                return true;
            }

            // TryWrite returns false for two reasons: bus full, OR writer completed (Stop raced).
            // We already checked _stopped above and it was false, so we treat this failure as a
            // capacity drop. If Stop() raced between the initial check and TryWrite, we may count
            // at most one spurious drop at shutdown — acceptable for a metrics counter.
            Interlocked.Increment(ref _dropped);
            return false;
        }

        /// <summary>
        /// Attempts to dequeue an item, waiting up to <paramref name="timeoutMs"/> milliseconds.
        /// </summary>
        /// <param name="item">On success receives the dequeued item; when false it is set to default.</param>
        /// <param name="timeoutMs">Maximum wait time in milliseconds (clamped to minimum 0).</param>
        /// <returns>True when an item was dequeued; false on timeout or when the bus is stopped and empty.</returns>
        public bool TryDequeue([MaybeNullWhen(false)] out T item, int timeoutMs)
        {
            // Fast path: item already available — no allocation, no blocking.
            if (_channel.Reader.TryRead(out item))
                return true;

            if (timeoutMs <= 0)
            {
                item = default;
                return false;
            }

            // CancellationTokenSource uses a monotonic timer internally, replacing the
            // previous DateTime.UtcNow deadline which was susceptible to NTP clock adjustments.
            using var cts = new CancellationTokenSource(Math.Max(0, timeoutMs));
            try
            {
                // Loop because WaitToReadAsync returning true only guarantees data was present
                // for *some* reader — another concurrent consumer may win the TryRead race.
                // We keep waiting until we actually dequeue an item, the channel is completed
                // and empty (BP-005), or the timeout is cancelled.
                while (true)
                {
                    // WaitToReadAsync completes synchronously when data is already present
                    // (zero allocation); otherwise it blocks the calling thread until data
                    // arrives or the token is cancelled (timeout). Returns false only when
                    // the channel is both completed and empty (BP-005 drain behaviour).
                    var waitTask = _channel.Reader.WaitToReadAsync(cts.Token);
                    bool hasData = waitTask.IsCompletedSuccessfully
                        ? waitTask.GetAwaiter().GetResult()
                        : waitTask.AsTask().GetAwaiter().GetResult();

                    if (!hasData)
                    {
                        item = default;
                        return false;
                    }

                    if (_channel.Reader.TryRead(out item))
                        return true;

                    // Another consumer raced us and won; loop and wait again.
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout expired — normal return, not an error.
                item = default;
                return false;
            }
            catch (ThreadInterruptedException)
            {
                // Thread.Interrupt() was called (e.g. by ProcessingCoordinator.Stop() when
                // the join timeout expires). Re-set the interrupt flag so the calling thread's
                // exit logic can observe it, then return false so WorkerLoop can exit cleanly.
                Thread.CurrentThread.Interrupt();
                item = default;
                return false;
            }
        }

        /// <summary>
        /// Stops the bus and unblocks waiting consumers. Subsequent publishes return false.
        /// </summary>
        public void Stop()
        {
            _stopped = true;
            // Completing the writer causes WaitToReadAsync to return false once the
            // channel is empty, unblocking all blocked TryDequeue callers (BP-005).
            _channel.Writer.TryComplete();
        }

        /// <summary>Number of items successfully published to the bus.</summary>
        public long PublishedCount => Interlocked.Read(ref _published);
        /// <summary>Number of items dropped due to capacity limits.</summary>
        public long DroppedCount => Interlocked.Read(ref _dropped);

        /// <summary>Returns the current depth of the internal queue.
        /// This value is snapshot-based and may change immediately after being read.
        /// </summary>
        public int Depth => _channel.Reader.Count;
    }
}