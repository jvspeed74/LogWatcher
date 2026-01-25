using Microsoft.Extensions.Logging;

namespace WatchStats.Core.Concurrency
{
    /// <summary>
    /// Thread-safe bounded FIFO event bus with drop-newest semantics when full.
    /// </summary>
    /// <typeparam name="T">Type of events carried by the bus.</typeparam>
    public sealed class BoundedEventBus<T>
    {
        private static class Events
        {
            public static readonly EventId BusDropNewest = new(2, "bus_drop_newest");
        }

        private readonly int _capacity;
        private readonly Queue<T> _queue = new Queue<T>();
        private readonly object _lock = new object();
        private readonly ILogger<BoundedEventBus<T>>? _logger;
        private bool _stopped;

        private long _published;
        private long _dropped;
        private long _lastDropLogTimeTicks = DateTime.MinValue.Ticks;
        private readonly TimeSpan _dropLogInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Creates a new bounded event bus with the provided capacity.
        /// </summary>
        /// <param name="capacity">Maximum number of items the bus will hold; must be &gt; 0.</param>
        /// <param name="logger">Optional logger for structured logging.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity"/> is not positive.</exception>
        public BoundedEventBus(int capacity, ILogger<BoundedEventBus<T>>? logger = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _logger = logger;
        }

        /// <summary>
        /// Publishes an item to the bus. Returns <c>true</c> if the item was enqueued; <c>false</c> if the bus is stopped or the item was dropped due to capacity.
        /// </summary>
        /// <param name="item">Item to publish.</param>
        /// <returns>True when enqueued; false if dropped or stopped.</returns>
        public bool Publish(T item)
        {
            lock (_lock)
            {
                if (_stopped)
                {
                    return false;
                }

                if (_queue.Count >= _capacity)
                {
                    var newDropped = Interlocked.Increment(ref _dropped);
                    
                    // Rate limit logging to once per 10 seconds using lock-free atomic compare-exchange
                    var nowTicks = DateTime.UtcNow.Ticks;
                    var lastLoggedTicks = Interlocked.Read(ref _lastDropLogTimeTicks);
                    
                    if (nowTicks - lastLoggedTicks >= _dropLogInterval.Ticks)
                    {
                        // Attempt to claim the logging slot atomically
                        if (Interlocked.CompareExchange(ref _lastDropLogTimeTicks, nowTicks, lastLoggedTicks) == lastLoggedTicks)
                        {
                            _logger?.LogWarning(Events.BusDropNewest,
                                "Event bus full, dropping newest event. Capacity={Capacity} Dropped={Dropped}",
                                _capacity, newDropped);
                        }
                    }
                    
                    return false; // drop newest
                }

                _queue.Enqueue(item);
                Interlocked.Increment(ref _published);
                Monitor.Pulse(_lock);
                return true;
            }
        }

        /// <summary>
        /// Attempts to dequeue an item, waiting up to <paramref name="timeoutMs"/> milliseconds.
        /// </summary>
        /// <param name="item">On success receives the dequeued item; when false it is set to default.</param>
        /// <param name="timeoutMs">Maximum wait time in milliseconds (clamped to minimum 0).</param>
        /// <returns>True when an item was dequeued; false on timeout or when the bus is stopped and empty.</returns>
        public bool TryDequeue(out T item, int timeoutMs)
        {
            // FIXME: Using DateTime.UtcNow for deadline calculation can be inaccurate under system clock adjustments
            // Consider using Stopwatch or Environment.TickCount64 for more reliable timeout handling
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs));

            lock (_lock)
            {
                while (true)
                {
                    if (_queue.Count > 0)
                    {
                        item = _queue.Dequeue();
                        return true;
                    }

                    if (_stopped)
                    {
                        item = default!;
                        return false;
                    }

                    var remaining = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (remaining == 0)
                    {
                        item = default!;
                        return false;
                    }

                    // Wait might return earlier due to Pulse; loop will re-check conditions
                    Monitor.Wait(_lock, remaining);
                }
            }
        }

        /// <summary>
        /// Stops the bus and unblocks waiting consumers. Subsequent publishes return false.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                _stopped = true;
                Monitor.PulseAll(_lock);
            }
        }

        /// <summary>Number of items successfully published to the bus.</summary>
        public long PublishedCount => Interlocked.Read(ref _published);
        /// <summary>Number of items dropped due to capacity limits.</summary>
        public long DroppedCount => Interlocked.Read(ref _dropped);

        /// <summary>Returns the current depth of the internal queue.
        /// This value is snapshot-based and may change immediately after being read.
        /// </summary>
        public int Depth
        {
            get
            {
                lock (_lock)
                {
                    return _queue.Count;
                }
            }
        }
    }
}