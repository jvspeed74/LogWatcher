using WatchStats.Core.Events;
using WatchStats.Core.Processing;

namespace WatchStats.Core.Metrics
{
    /// <summary>
    /// Per-worker stats buffer for a single reporting interval. Contains counters, message counts and a latency histogram.
    /// Callers should treat instances as single-threaded containers (workers write to active buffer; reporter reads inactive buffer after swap).
    /// </summary>
    public sealed class WorkerStatsBuffer
    {
        // Fs event counters
        /// <summary>Count of created events.</summary>
        public long FsCreated;
        /// <summary>Count of modified events.</summary>
        public long FsModified;
        /// <summary>Count of deleted events.</summary>
        public long FsDeleted;
        /// <summary>Count of renamed events.</summary>
        public long FsRenamed;

        // Other scalar counters
        /// <summary>Number of processed lines.</summary>
        public long LinesProcessed;
        /// <summary>Number of malformed lines encountered.</summary>
        public long MalformedLines;
        /// <summary>Number of coalesces due to busy gate.</summary>
        public long CoalescedDueToBusyGate;
        /// <summary>Number of delete-pending markers set.</summary>
        public long DeletePendingSetCount;
        /// <summary>Number of files skipped due to delete pending.</summary>
        public long SkippedDueToDeletePending;
        /// <summary>Number of file states removed.</summary>
        public long FileStateRemovedCount;

        // IO counters
        /// <summary>Count of file-not-found occurrences.</summary>
        public long FileNotFoundCount;
        /// <summary>Count of access-denied occurrences.</summary>
        public long AccessDeniedCount;
        /// <summary>Count of I/O exceptions.</summary>
        public long IoExceptionCount;
        /// <summary>Count of truncation resets.</summary>
        public long TruncationResetCount;

        // Level counts sized to LogLevel enum
        /// <summary>Per-level counters indexed by <see cref="LogLevel"/>.</summary>
        public long[] LevelCounts;

        // Message counts (string keys)
        /// <summary>Counts for individual message keys.</summary>
        public Dictionary<string, int> MessageCounts;

        // Latency histogram
        /// <summary>Latency histogram for the interval.</summary>
        public LatencyHistogram Histogram;

        private const int DefaultMessageCapacity = 256;

        /// <summary>
        /// Creates a new buffer with optional initial capacity for message keys.
        /// </summary>
        /// <param name="messageInitialCapacity">Initial capacity for message counts dictionary.</param>
        public WorkerStatsBuffer(int messageInitialCapacity = DefaultMessageCapacity)
        {
            // Initialize fields
            LevelCounts = new long[Enum.GetNames(typeof(LogLevel)).Length];
            MessageCounts = new Dictionary<string, int>(messageInitialCapacity);
            Histogram = new LatencyHistogram();
        }

        /// <summary>
        /// Resets counters, arrays and collections to prepare the buffer for reuse.
        /// </summary>
        public void Reset()
        {
            FsCreated = FsModified = FsDeleted = FsRenamed = 0;
            LinesProcessed = 0;
            MalformedLines = 0;
            CoalescedDueToBusyGate = 0;
            DeletePendingSetCount = 0;
            SkippedDueToDeletePending = 0;
            FileStateRemovedCount = 0;

            FileNotFoundCount = 0;
            AccessDeniedCount = 0;
            IoExceptionCount = 0;
            TruncationResetCount = 0;

            Array.Clear(LevelCounts);
            MessageCounts.Clear();
            Histogram.Reset();
        }

        /// <summary>
        /// Increment the appropriate filesystem event counter for <paramref name="kind"/>.
        /// </summary>
        /// <param name="kind">The filesystem event kind to increment.</param>
        public void IncrementFsEvent(FsEventKind kind)
        {
            switch (kind)
            {
                case FsEventKind.Created: FsCreated++; break;
                case FsEventKind.Modified: FsModified++; break;
                case FsEventKind.Deleted: FsDeleted++; break;
                case FsEventKind.Renamed: FsRenamed++; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// Increment the counter for the specified <see cref="LogLevel"/>.
        /// </summary>
        /// <param name="level">Level to increment.</param>
        public void IncrementLevel(LogLevel level)
        {
            var idx = (int)level;
            if (idx < 0 || idx >= LevelCounts.Length) return;
            LevelCounts[idx]++;
        }

        /// <summary>
        /// Increments the message count for the provided key.
        /// </summary>
        /// <param name="key">Non-null message key string.</param>
        /// <exception cref="ArgumentNullException">When <paramref name="key"/> is null.</exception>
        public void IncrementMessage(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (MessageCounts.TryGetValue(key, out var v)) MessageCounts[key] = v + 1;
            else MessageCounts[key] = 1;
        }

        /// <summary>
        /// Records a latency sample (milliseconds) into the histogram.
        /// </summary>
        /// <param name="latencyMs">Latency value in milliseconds.</param>
        public void RecordLatency(int latencyMs)
        {
            Histogram.Add(latencyMs);
        }
    }
}