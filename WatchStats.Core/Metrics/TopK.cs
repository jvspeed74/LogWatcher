namespace WatchStats.Core.Metrics
{
    /// <summary>
    /// Utility to compute top-K message keys by count.
    /// </summary>
    public static class TopK
    {
        /// <summary>
        /// Computes the top <paramref name="k"/> entries from the provided counts dictionary.
        /// Results are ordered by descending count and then by ordinal key for tie-breaking.
        /// </summary>
        /// <param name="counts">Dictionary mapping keys to counts.</param>
        /// <param name="k">Number of top entries to return. When &lt;= 0 an empty list is returned.</param>
        /// <returns>A read-only list of key/count pairs of length at most <paramref name="k"/>.</returns>
        public static IReadOnlyList<(string Key, int Count)> ComputeTopK(Dictionary<string, int> counts, int k)
        {
            if (k <= 0) return Array.Empty<(string, int)>();
            if (counts == null || counts.Count == 0) return Array.Empty<(string, int)>();

            var list = new List<(string Key, int Count)>(counts.Count);
            foreach (var kv in counts)
            {
                list.Add((kv.Key, kv.Value));
            }

            list.Sort((a, b) =>
            {
                int c = b.Count.CompareTo(a.Count); // descending
                if (c != 0) return c;
                return StringComparer.Ordinal.Compare(a.Key, b.Key);
            });

            if (list.Count > k)
            {
                return list.GetRange(0, k);
            }

            return list;
        }
    }
}