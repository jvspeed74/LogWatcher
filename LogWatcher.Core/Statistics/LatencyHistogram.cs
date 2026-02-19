namespace LogWatcher.Core.Statistics
{
    /// <summary>
    /// Bounded, mergeable histogram for latencies in milliseconds.
    /// Tracks values 0..10000 in discrete bins and uses an overflow bin for values > 10000.
    /// </summary>
    public sealed class LatencyHistogram
    {
        // TODO: Consider making histogram bounds configurable to support different latency ranges
        private const int MaxMs = 10_000;
        private const int OverflowIndex = MaxMs + 1; // 10001
        private const int BinCount = MaxMs + 2; // 0..10000 plus overflow

        private readonly int[] _bins;
        private long _count;

        /// <summary>
        /// Creates a new empty histogram.
        /// </summary>
        public LatencyHistogram()
        {
            _bins = new int[BinCount];
            _count = 0;
        }

        /// <summary>Read-only span of bin counts indexed by millisecond value; index 10001 is the overflow bin.</summary>
        public ReadOnlySpan<int> Bins => _bins;
        /// <summary>Total number of samples recorded in the histogram.</summary>
        public long Count => _count;

        /// <summary>
        /// Records a latency sample in milliseconds. Negative values are clamped to bin 0; values greater than 10000 map to the overflow bin.
        /// </summary>
        /// <param name="latencyMs">Latency in milliseconds.</param>
        public void Add(int latencyMs)
        {
            int idx;
            if (latencyMs < 0) idx = 0;
            else if (latencyMs <= MaxMs) idx = latencyMs;
            else idx = OverflowIndex;

            _bins[idx]++;
            _count++;
        }

        /// <summary>
        /// Resets the histogram to empty.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_bins, 0, _bins.Length);
            _count = 0;
        }

        /// <summary>
        /// Merges bins and count from <paramref name="other"/> into this histogram.
        /// </summary>
        /// <param name="other">Histogram to merge from; must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
        public void MergeFrom(LatencyHistogram other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            // FIXME: Potential integer overflow when merging bins with very high counts
            // Consider using checked arithmetic or long[] for bins
            for (int i = 0; i < _bins.Length; i++)
            {
                _bins[i] += other._bins[i];
            }

            _count += other._count;
        }

        /// <summary>
        /// Returns the p-th percentile bin index (for p in (0..1]). For example, p=0.5 returns the median bin.
        /// Returns <c>null</c> when no samples are present. The overflow bin index is 10001.
        /// </summary>
        /// <param name="p">Percentile to compute (0..1].</param>
        /// <returns>Bin index representing the percentile, or <c>null</c> if empty.</returns>
        public int? Percentile(double p)
        {
            if (_count == 0) return null;
            if (double.IsNaN(p) || p <= 0) p = 0; // will clamp below
            if (p > 1) p = 1;

            long target = (long)Math.Ceiling(p * _count);
            if (target < 1) target = 1;
            if (target > _count) target = _count;

            long cumulative = 0;
            for (int i = 0; i < _bins.Length; i++)
            {
                cumulative += _bins[i];
                if (cumulative >= target)
                {
                    // return bin index; overflow bin is at OverflowIndex
                    return i;
                }
            }

            // should not reach here, but as a fallback return overflow
            return OverflowIndex;
        }
    }
}