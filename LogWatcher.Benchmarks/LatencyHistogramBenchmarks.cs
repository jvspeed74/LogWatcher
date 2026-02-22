using BenchmarkDotNet.Attributes;

using LogWatcher.Core.Statistics;

namespace LogWatcher.Benchmarks;

[MemoryDiagnoser]
public class LatencyHistogramBenchmarks
{
    private LatencyHistogram _histogram = null!;
    private LatencyHistogram _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        _histogram = new LatencyHistogram();

        // Populate _source with a sample across the full bin range.
        _source = new LatencyHistogram();
        for (var ms = 0; ms <= 10_000; ms++)
            _source.Add(ms);
        _source.Add(10_001); // overflow bin
    }

    [IterationSetup(Target = nameof(MergeFrom_FullHistogram))]
    public void ResetHistogram() => _histogram = new LatencyHistogram();

    /// <summary>
    /// O(1) add to a single bin. Expected: 0 B allocated.
    /// </summary>
    [Benchmark]
    public void Add_SingleSample() => _histogram.Add(500);

    /// <summary>
    /// 1 000 Add calls spread across the latency range.
    /// </summary>
    [Benchmark]
    public void Add_1000Samples()
    {
        for (var i = 0; i < 1000; i++)
            _histogram.Add(i % 10_001);
    }

    /// <summary>
    /// Merge two fully populated histograms (10 002 bins each).
    /// Expected: O(bins) time, 0 B allocated.
    /// </summary>
    [Benchmark]
    public void MergeFrom_FullHistogram() => _histogram.MergeFrom(_source);

    /// <summary>
    /// Compute the 99th-percentile latency on a populated histogram.
    /// </summary>
    [Benchmark]
    public int? Percentile_P99() => _source.Percentile(0.99);

    /// <summary>
    /// Compute P50, P95, and P99 in sequence — mirrors the reporter's combined cost.
    /// Provides the true regression baseline for the three-query reporting path.
    /// </summary>
    [Benchmark]
    public (int? p50, int? p95, int? p99) Percentile_P50_P95_P99() =>
        (_source.Percentile(0.50), _source.Percentile(0.95), _source.Percentile(0.99));
}