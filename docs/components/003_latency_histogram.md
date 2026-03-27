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

## Prefix-Sum Optimization Assessment

### Current implementation

`Percentile(double p)` performs a **linear O(N) scan** over all `BinCount = 10,002` bins (indices 0–10,000 plus the
overflow bin at 10,001). Each call accumulates a running total until the target rank is reached. At ~3.1 µs per call
(measured by `Percentile_P99` benchmark on a modern CPU), this works out to roughly 0.3 ns/bin — consistent with a
sequential, cache-friendly memory scan with no branchy inner logic.

### The tradeoff

A prefix-sum array would allow O(1) or O(log N) percentile lookups after an O(N) build step. Two maintenance
strategies exist:

| Strategy | `Add` cost | `Percentile` cost |
|---|---|---|
| Incremental update | O(N) per add — update all bins ≥ inserted bin | O(1) lookup |
| Lazy rebuild (dirty flag) | O(1) — unchanged | O(N) rebuild once, then O(1) |

**Incremental update is strictly worse**: `Add` is on the hot path (called for every parsed log line across all worker
threads). Making each `Add` O(N) would replace a ~1 ns operation with a ~3 µs loop — a 3,000× regression on the
critical path. This is not viable.

**Lazy rebuild** is the only plausible form. The prefix-sum array would be rebuilt once per reporting interval
(after merge, before the first `Percentile` call). The cost breakdown at report time is:

| Approach | P50 | P95 | P99 | Total |
|---|---|---|---|---|
| Current (3 × linear scan) | 3.1 µs | 3.1 µs | 3.1 µs | ~9.3 µs |
| Prefix-sum (1 × rebuild + 3 × O(1) lookup) | — | — | — | ~3.1 µs build + ~0 µs × 3 = **~3.1 µs** |
| **Saving** | | | | **~6.2 µs** |

### Recommendation: **do not implement**

At a 2-second reporting interval (2,000,000 µs), saving 6.2 µs represents **0.0003% of the interval**. This is
completely negligible in any realistic workload.

The complexity cost is real:

- A second `int[]` of 10,002 elements (~40 KB) must be allocated and kept alive alongside `_bins`.
- A `bool _prefixDirty` flag must be maintained and checked before every `Percentile` call.
- Rebuild logic must be correct in the presence of `Reset()` and `MergeFrom()` (both must set the dirty flag).
- The cognitive overhead increases future maintenance risk for zero measurable user benefit.

**If the reporting interval were ever reduced to less than ~10 ms**, the total percentile scan time (~9.3 µs) would
represent more than 0.1% of the interval, and re-evaluation would be warranted. At that point, the lazy-rebuild
strategy could be adopted without touching the `Add` hot path.

---
