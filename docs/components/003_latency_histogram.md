## Component 3: Latency Histogram (0–10,000ms + overflow) + percentiles

### Purpose (what this component must do)

Provide a **bounded, mergeable, deterministic** data structure for latency distribution that:

* supports `Add(latencyMs)` in the hot path
* supports `MergeFrom(other)` at report time (per-worker inactive buffers merged into snapshot)
* computes p50/p95/p99 at report time
* uses fixed memory (no unbounded growth)

### Spec details to implement

* Range: **0–10,000 ms** inclusive, plus **overflow** bin for `> 10,000`
* Negative values: clamp to 0
* Percentiles: computed from histogram counts using the actual interval’s merged data

### Public contract (C# types)

1. Create `sealed class LatencyHistogram` or `struct LatencyHistogram`:

    * For per-worker stats buffers, a **class** avoids copying big arrays; a **struct** is fine if it owns an array reference.
2. Fields:

    * `int[] _bins;` length = `10_002` (0..10,000 plus overflow at index 10,001)
    * `long _count;` total samples (optional; can be derived by summing bins, but keep it for speed)
3. Methods:

    * `void Add(int latencyMs)`
    * `void Reset()`
    * `void MergeFrom(LatencyHistogram other)`
    * `int? Percentile(double p)` (returns ms value; null if no samples)

### Implementation steps

1. **Initialize bins**

    * Allocate once at construction time: `new int[10_002]`.
2. **Add**

    * Compute bin index:

        * if latencyMs < 0 => idx = 0
        * else if latencyMs <= 10_000 => idx = latencyMs
        * else idx = 10_001 (overflow)
    * Increment `_bins[idx]++` and `_count++`.
    * This must be single-threaded per worker buffer; no Interlocked needed.
3. **Reset**

    * `Array.Clear(_bins)`
    * `_count = 0`
4. **MergeFrom**

    * For i in bins length: `_bins[i] += other._bins[i]`
    * `_count += other._count`
    * This occurs during reporting merge; ensure it only reads from inactive buffers.
5. **Percentile**

    * If `_count == 0`: return null
    * Determine rank:

        * `target = (long)Math.Ceiling(p * _count)`
        * Clamp target to [1, _count]
    * Scan bins cumulatively:

        * cumulative += _bins[i]
        * when cumulative >= target, return:

            * for i 0..10_000 => i
            * for overflow bin => 10_001 (or return 10_001, or return 10_000+ as “>10,000” sentinel; decide below)
6. **Overflow percentile representation**

    * To keep output readable, do not return 10,001 as a literal ms.
    * Instead:

        * Percentile returns `int?` where overflow returns 10_001.
        * The reporter formats 10_001 as `">10000"`.
    * This keeps the core math simple.

### Unit tests (xUnit)

Create `LatencyHistogramTests`:

1. `EmptyHistogram_PercentilesAreNull`
2. `SingleValue_AllPercentilesSame`
3. `LinearDistribution_PercentilesMatchExpectedBin`
4. `OverflowValues_GoToOverflowBin`
5. `Merge_SumsCountsCorrectly`

Test example:

* Add {1,2,3,4} => count=4

    * p50 target=2 => should return 2
    * p95 target=4 => should return 4

---
