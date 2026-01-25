using WatchStats.Core.Processing;

namespace WatchStats.Core.Metrics
{
    /// <summary>
    /// Aggregated metrics and derived outputs for a reporting interval.
    /// </summary>
    public sealed class GlobalSnapshot
    {
        // Scalars
        /// <summary>Count of created filesystem events.</summary>
        public long FsCreated;
        /// <summary>Count of modified filesystem events.</summary>
        public long FsModified;
        /// <summary>Count of deleted filesystem events.</summary>
        public long FsDeleted;
        /// <summary>Count of renamed filesystem events.</summary>
        public long FsRenamed;

        /// <summary>Total number of processed lines.</summary>
        public long LinesProcessed;
        /// <summary>Total number of malformed lines encountered during parsing.</summary>
        public long MalformedLines;

        /// <summary>Number of times processing coalesced due to busy gate.</summary>
        public long CoalescedDueToBusyGate;
        /// <summary>Count of delete-pending markers set.</summary>
        public long DeletePendingSetCount;
        /// <summary>Count of files skipped due to delete-pending.</summary>
        public long SkippedDueToDeletePending;
        /// <summary>Count of file state removals finalized.</summary>
        public long FileStateRemovedCount;

        /// <summary>Count of file-not-found occurrences reported by tailer.</summary>
        public long FileNotFoundCount;
        /// <summary>Count of access denied occurrences reported by tailer.</summary>
        public long AccessDeniedCount;
        /// <summary>Count of I/O exceptions reported by tailer.</summary>
        public long IoExceptionCount;
        /// <summary>Count of truncation resets observed by tailer.</summary>
        public long TruncationResetCount;

        // Bus metrics (attached after merge)
        /// <summary>Total events published to the bus across workers.</summary>
        public long BusPublished;
        /// <summary>Events dropped by the bus across workers.</summary>
        public long BusDropped;
        /// <summary>Observed bus depth (approximate) after merge.</summary>
        public int BusDepth;

        /// <summary>Per-level counts indexed by <see cref="LogLevel"/> value.</summary>
        public long[] LevelCounts;
        /// <summary>Accumulated message key counts.</summary>
        public Dictionary<string, int> MessageCounts;
        /// <summary>Aggregated latency histogram.</summary>
        public LatencyHistogram Histogram;

        // Derived outputs
        /// <summary>Top-K messages computed from aggregated message counts.</summary>
        public List<(string Key, int Count)> TopKMessages;
        /// <summary>Computed P50 latency in milliseconds, or null if no samples.</summary>
        public int? P50;
        /// <summary>Computed P95 latency in milliseconds, or null if no samples.</summary>
        public int? P95;
        /// <summary>Computed P99 latency in milliseconds, or null if no samples.</summary>
        public int? P99;

        /// <summary>
        /// Creates a new snapshot and preallocates containers sized for the given top-K capacity.
        /// </summary>
        /// <param name="topK">Number of top messages to reserve space for.</param>
        public GlobalSnapshot(int topK)
        {
            LevelCounts = new long[Enum.GetNames(typeof(LogLevel)).Length];
            MessageCounts = new Dictionary<string, int>(256);
            Histogram = new LatencyHistogram();
            TopKMessages = new List<(string, int)>(topK);

            ResetForNextMerge(topK);
        }

        /// <summary>
        /// Resets counters and prepared collections in preparation for the next merge. Preserves reasonable capacity where applicable.
        /// </summary>
        /// <param name="topK">Top-K capacity to prepare for.</param>
        public void ResetForNextMerge(int topK)  // TODO: Consider parameterizing message dictionary capacity to prevent excessive resizing
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

            BusPublished = 0;
            BusDropped = 0;
            BusDepth = 0;

            if (LevelCounts == null || LevelCounts.Length != Enum.GetNames(typeof(LogLevel)).Length)
                LevelCounts = new long[Enum.GetNames(typeof(LogLevel)).Length];
            else
                Array.Clear(LevelCounts, 0, LevelCounts.Length);

            MessageCounts.Clear();
            Histogram.Reset();

            TopKMessages.Clear();
            P50 = P95 = P99 = null;
        }

        /// <summary>
        /// Merges data from a worker's <see cref="WorkerStatsBuffer"/> into this global snapshot.
        /// </summary>
        /// <param name="buf">Worker buffer to merge from. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="buf"/> is null.</exception>
        public void MergeFrom(WorkerStatsBuffer buf)
        {
            if (buf == null) throw new ArgumentNullException(nameof(buf));

            FsCreated += buf.FsCreated;
            FsModified += buf.FsModified;
            FsDeleted += buf.FsDeleted;
            FsRenamed += buf.FsRenamed;

            LinesProcessed += buf.LinesProcessed;
            MalformedLines += buf.MalformedLines;

            CoalescedDueToBusyGate += buf.CoalescedDueToBusyGate;
            DeletePendingSetCount += buf.DeletePendingSetCount;
            SkippedDueToDeletePending += buf.SkippedDueToDeletePending;
            FileStateRemovedCount += buf.FileStateRemovedCount;

            FileNotFoundCount += buf.FileNotFoundCount;
            AccessDeniedCount += buf.AccessDeniedCount;
            IoExceptionCount += buf.IoExceptionCount;
            TruncationResetCount += buf.TruncationResetCount;

            // Level counts
            int min = Math.Min(LevelCounts.Length, buf.LevelCounts.Length);
            for (int i = 0; i < min; i++)
            {
                LevelCounts[i] += buf.LevelCounts[i];
            }

            // Message counts
            foreach (var kv in buf.MessageCounts)
            {
                if (MessageCounts.TryGetValue(kv.Key, out var existing)) MessageCounts[kv.Key] = existing + kv.Value;
                else MessageCounts[kv.Key] = kv.Value;
            }

            // Histogram
            Histogram.MergeFrom(buf.Histogram);
        }

        /// <summary>
        /// Finalizes derived values (Top-K and percentiles) based on the current aggregated state.
        /// </summary>
        /// <param name="topK">Number of top messages to compute.</param>
        public void FinalizeSnapshot(int topK)
        {
            TopKMessages.Clear();
            if (MessageCounts.Count > 0)
            {
                var top = TopK.ComputeTopK(MessageCounts, topK);
                TopKMessages.AddRange(top);
            }

            P50 = Histogram.Percentile(0.50);
            P95 = Histogram.Percentile(0.95);
            P99 = Histogram.Percentile(0.99);
        }
    }
}