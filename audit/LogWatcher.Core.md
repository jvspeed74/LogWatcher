# Rule Audit — LogWatcher.Core

Rules evaluated against: `.github/instructions/dotnet.instructions.md`

---

## Violations

### BCL — Bounded producer/consumer queue: `Queue<T>` + `Monitor` instead of `Channel.CreateBounded<T>`

**File:** `Backpressure/BoundedEventBus.cs`

```csharp
private readonly Queue<T> _queue = new Queue<T>();
private readonly object _lock = new object();
// ...
lock (_lock) { _queue.Enqueue(item); Monitor.Pulse(_lock); }
Monitor.Wait(_lock, remaining);
```

Rule (BCL table): *"Bounded producer/consumer queue | `Queue<T>` + `Monitor` | `Channel.CreateBounded<T>`"*  
Prohibited by Default table: *"`Queue<T>` + `Monitor` bus | Hand-rolled bounded queue | `Channel.CreateBounded<T>`"*

---

### BCL — `DateTime.UtcNow` for deadline/elapsed calculation

**File:** `Backpressure/BoundedEventBus.cs`

```csharp
var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs));
// ...
var remaining = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
```

`DateTime.UtcNow` is not monotonic and can drift under NTP clock adjustments.

Rule: *"NEVER use `DateTime.UtcNow` for elapsed time or deadline calculations — not monotonic, drifts under NTP. Use `Stopwatch.GetTimestamp()` or `Environment.TickCount64`."*  
Prohibited by Default table: *"`DateTime.UtcNow` for elapsed time | Non-monotonic time for deadlines | `Stopwatch` / `Environment.TickCount64`"*

---

### BCL — `static ReadOnlySpan<byte>` property returning `new byte[]` (allocates on every access)

**File:** `Processing/Parsing/LogParser.cs`

```csharp
private static ReadOnlySpan<byte> LatencyPrefix => new byte[]
{
    (byte)'l', (byte)'a', (byte)'t', (byte)'e', (byte)'n', (byte)'c', (byte)'y', (byte)'_', (byte)'m',
    (byte)'s', (byte)'='
};
```

This allocates a new `byte[]` on every invocation.

Rule: *"NEVER declare `static ReadOnlySpan<byte>` as a property — `=> new byte[]` allocates on every call. Use `"value"u8` directly or `static readonly byte[]`."*  
Prohibited by Default table: *"`static ReadOnlySpan<byte>` property | `=> new byte[] { ... }` | `"value"u8` or `static readonly byte[]`"*  
Should be: `private static ReadOnlySpan<byte> LatencyPrefix => "latency_ms="u8;`

---

### BCL — Hand-rolled span byte search instead of `span.IndexOf(value)`

**File:** `Processing/Parsing/LogParser.cs`

```csharp
private static int IndexOfByte(ReadOnlySpan<byte> span, byte value)
{
    for (int i = 0; i < span.Length; i++)
        if (span[i] == value)
            return i;
    return -1;
}
```

The FIXME comment in the file acknowledges this: `// FIXME: Replace with ReadOnlySpan<byte>.IndexOf()`.

Rule (BCL table): *"Span byte search | hand-rolled `for` loop | `span.IndexOf(value)`"*  
Prohibited by Default table: *"Hand-rolled span/byte search | Manual loops for BCL operations | `span.IndexOf`, `MemoryExtensions`"*

**File:** `Processing/Scanning/Utf8LineScanner.cs`

The same pattern appears twice — manual `for` loops to find `'\n'`:

```csharp
for (int i = 0; i < chunk.Length; i++)
{
    if (chunk[i] == (byte)'\n') { nlIndex = i; break; }
}
// and again:
for (int i = start; i < chunk.Length; i++)
{
    if (chunk[i] == (byte)'\n') { j = i; break; }
}
```

Should be replaced with `chunk.IndexOf((byte)'\n')`.

---

### BCL — Hand-rolled span subsequence search instead of `span.IndexOf(needle)`

**File:** `Processing/Parsing/LogParser.cs`

```csharp
private static int IndexOfSubsequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
{
    for (int i = 0; i <= haystack.Length - needle.Length; i++)
    {
        bool ok = true;
        for (int j = 0; j < needle.Length; j++) { ... }
        if (ok) return i;
    }
    return -1;
}
```

A TODO in the file acknowledges the O(n×m) complexity.

Rule (BCL table): *"Span subsequence search | hand-rolled O(n×m) loop | `span.IndexOf(needle)` / `MemoryExtensions.IndexOf`"*  
Prohibited by Default table: *"Hand-rolled span/byte search | Manual loops for BCL operations | `span.IndexOf`, `MemoryExtensions`"*

---

### BCL — Hand-rolled case-insensitive span compare instead of `MemoryExtensions.Equals`

**File:** `Processing/Parsing/LogParser.cs`

```csharp
private static bool EqualsIgnoreCaseAscii(ReadOnlySpan<byte> left, string right)
{
    if (left.Length != right.Length) return false;
    for (int i = 0; i < left.Length; i++)
    {
        byte lb = left[i];
        char rc = right[i];
        if (lb >= (byte)'a' && lb <= (byte)'z') lb = (byte)(lb - 32);
        if (lb != (byte)rc) return false;
    }
    return true;
}
```

Rule (BCL table): *"Case-insensitive span compare | byte-by-byte compare | `MemoryExtensions.Equals(span, "VALUE"u8, StringComparison.OrdinalIgnoreCase)`"*

---

### BCL — UTF-8 integer parsing via digit accumulation loop instead of `Utf8Parser.TryParse`

**File:** `Processing/Parsing/LogParser.cs`

```csharp
int i = 0;
long acc = 0;
bool any = false;
while (i < valSpan.Length)
{
    byte b = valSpan[i];
    if (b < (byte)'0' || b > (byte)'9') break;
    any = true;
    acc = acc * 10 + (b - (byte)'0');
    ...
    i++;
}
```

Rule (BCL table): *"UTF-8 integer parsing | digit accumulation loop | `Utf8Parser.TryParse`"*  
Prohibited by Default table: *"Hand-rolled UTF-8 integer parse | Digit accumulation | `Utf8Parser.TryParse`"*

---

### Language — Manual collection initialization → collection expressions

**File:** `Processing/Parsing/LogParser.cs`

```csharp
private static readonly string[] IsoFormats = new[]
{
    "yyyy-MM-ddTHH:mm:ssK",
    "yyyy-MM-ddTHH:mm:ss.fffK",
    "yyyy-MM-ddTHH:mm:ss.fffffffK"
};
```

Rule: *"Manual collection initialization → collection expressions"*  
Should be: `private static readonly string[] IsoFormats = ["yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.fffK", "yyyy-MM-ddTHH:mm:ss.fffffffK"];`

---

### Pipeline — Nested callbacks 3+ levels deep

**File:** `Processing/FileProcessor.cs`

```csharp
TailReadStatus status = _tailer.ReadAppended(path, ref localOffset, chunk =>
{
    Utf8LineScanner.Scan(chunk, ref state.Carry, line =>
    {
        stats.LinesProcessed++;
        if (!LogParser.TryParse(line, out var parsed)) { ... }
        // level counts, message key, latency — all buried here
    });
}, out var totalBytesRead, chunkSize);
```

Three levels of anonymous callbacks. The inner logic is untestable without the full outer stack. The TODO comments in the file itself acknowledge this.

Rule: *"NEVER nest processing logic 3+ levels deep — control flow becomes untraceable, inner logic untestable. Extract one named private method per level."*  
Prohibited by Default table: *"Nested lambda chains 3+ levels | Callbacks beyond two levels | Named private method per level"*

---

### Pipeline — Properties on hot-path mutable struct

**File:** `FileManagement/PartialLineBuffer.cs`

```csharp
public struct PartialLineBuffer
{
    public byte[]? Buffer { get; private set; }
    public int Length { get; private set; }
```

`PartialLineBuffer` is a mutable struct used on the hot path (invoked per-chunk in every file scan). Its own XML doc says "simple data shape (public fields) for performance", yet the implementation uses properties with private setters, adding indirection and emitting a false encapsulation signal for a type that is explicitly mutable by design.

Rule: *"DO NOT reflexively convert fields to properties on mutable structs and hot-path state objects — adds indirection, emits false encapsulation signal on types explicitly mutable by design."*  
Prohibited by Default table: *"Properties on hot-path mutable structs | Wrapping directly-mutated fields | Fields where direct mutation is the design"*

---

### BCL — Periodic background work using `Thread` + `Thread.Sleep` loop instead of `PeriodicTimer`

**File:** `Reporting/Reporter.cs`

```csharp
private void ReporterLoop()
{
    while (!_stopping)
    {
        Thread.Sleep(TimeSpan.FromSeconds(_intervalSeconds));
        // ... swap, merge, print
    }
}
```

Rule (BCL table): *"Periodic background work | `Thread` + `Thread.Sleep` loop | `PeriodicTimer`"*

---

### Concurrency — `Task.Run` to parallelize waits in a background loop

**File:** `Reporting/Reporter.cs`

```csharp
var tasks = _workers.Select((w, idx) => Task.Run(() =>
{
    try { w.WaitForSwapAck(cts.Token); return idx; }
    catch (OperationCanceledException) { return -1; }
})).ToArray();
Task.WaitAll(tasks, _ackTimeout);
```

This creates one `Task` per worker per reporter interval. The prohibition is specifically against using `Task.Run` to parallelize waits inside a background loop.

Rule: *"DO NOT use `Task.Run` to parallelize waits in a background loop — allocates a `Task` per iteration. Use sequential waits; `Parallel.ForEach` only when parallelism is justified."*  
Prohibited by Default table: *"`Task.Run` in background loops | Task allocation in tight loops | Sequential waits; `Parallel.ForEach` only when justified"*

---

### Pipeline — LINQ enumeration in per-interval path (Allocation Coherence)

**File:** `Reporting/Reporter.cs`

```csharp
var tasks = _workers.Select((w, idx) => Task.Run(...)).ToArray();          // LINQ in loop
var ackedIndices = tasks.Where(t => t.IsCompleted && t.Result >= 0)
                        .Select(t => t.Result).ToArray();                   // LINQ in loop
```

Both expressions execute once per reporting interval and allocate enumerators, closures, and arrays on each iteration.

Rule (Allocation Coherence): *"In per-chunk, per-line, or per-interval hot paths, NEVER: LINQ enumeration"*

---

### Concurrency — `volatile` keyword instead of `Volatile.Read` / `Volatile.Write`

**Files:** `Processing/ProcessingCoordinator.cs`, `Reporting/Reporter.cs`

```csharp
private volatile bool _stopping;   // ProcessingCoordinator
private volatile bool _stopping;   // Reporter
```

The concurrency primitives table prescribes `Volatile.Read` / `Volatile.Write` for non-blocking flag visibility across threads. The `volatile` keyword predates this API and is not listed as the preferred approach. `FileState.cs` in the same project already uses `Volatile.Read`/`Volatile.Write` consistently — the two classes are inconsistent with the rest of the codebase.

Rule (Concurrency table): *"Non-blocking flag visibility across threads | `Volatile.Read` / `Volatile.Write`"*
