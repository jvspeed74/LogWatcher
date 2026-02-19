

## **Anti-Patterns & Patterns**

### **Anti-Pattern 1: Static Mutable State**

❌ **Don't do this:**

```csharp
public static class UserCache
{
    public static Dictionary<int, User> Cache = new();  // Shared state
}
```

**Why it's bad:**

- Tests interfere with each other (state pollution)
- Race conditions in multi-threaded code
- Hidden dependencies (not obvious from method signature)
- Impossible to mock for testing

✅ **Do this instead:**

```csharp
public class UserCache
{
    private readonly Dictionary<int, User> _cache = new();
    
    public UserCache(Dictionary<int, User> cache) => _cache = cache;
}

// Inject into constructor; easy to swap with test double
```

---

### **Anti-Pattern 2: Hidden Dependencies**

❌ **Don't do this:**

```csharp
public class OrderProcessor
{
    public void ProcessOrder(Order order)
    {
        var tax = TaxCalculator.Calculate(order.Amount);  // Where does this come from?
        Logger.Log($"Processing {order.Id}");  // Where does this come from?
    }
}
```

**Why it's bad:**

- Method signature lies (looks like it only needs `order`)
- Can't mock dependencies for testing
- Hard to trace dependencies when reading code

✅ **Do this instead:**

```csharp
public class OrderProcessor
{
    private readonly ITaxCalculator _tax;
    private readonly ILogger _logger;
    
    public OrderProcessor(ITaxCalculator tax, ILogger logger)
    {
        _tax = tax;
        _logger = logger;
    }
    
    public void ProcessOrder(Order order)
    {
        var tax = _tax.Calculate(order.Amount);
        _logger.Log($"Processing {order.Id}");
    }
}
```

**Why it's good:**

- Dependencies are explicit and testable
- Constructor signature is a contract
- Easy to inject test doubles

---

### **Anti-Pattern 3: Backward Dependencies**

❌ **Don't do this:**

```csharp
// Statistics domain calling back into Processing
public class WorkerStatsBuffer
{
    public void RecordLatency(int latencyMs)
    {
        _coordinator.NotifySlowOperation();  // ❌ Backward!
    }
}
```

**Why it's bad:**

- Circular dependencies prevent independent testing
- Creates coupling: can't change one domain without affecting the other
- Makes refactoring risky

✅ **Do this instead:**

```csharp
// Statistics just accumulates; Processing coordinator decides what to do
public class WorkerStatsBuffer
{
    public void RecordLatency(int latencyMs)
    {
        _histogram.Add(latencyMs);  // Just store it
    }
}

// Reporter reads the histogram after the fact
public void PrintReport(GlobalSnapshot snapshot)
{
    if (snapshot.P95 > 5000)
        Console.WriteLine("High latency detected");
}
```

**Why it's good:**

- Unidirectional flow (easy to trace)
- Statistics doesn't know about coordination
- Reporter decides what to do with the metrics

---

### **Anti-Pattern 4: God Domains**

❌ **Don't do this:**

```csharp
// Processing domain doing too much
public class FileProcessor
{
    public void ProcessFile(string path)
    {
        // Read file
        var bytes = File.ReadAllBytes(path);
        
        // Split lines
        var lines = bytes.Split(newline);
        
        // Parse
        foreach (var line in lines)
        {
            ParseLogLine(line);
        }
        
        // Update stats
        _stats.IncrementCount();
        
        // Manage worker coordination
        _workerStats.RequestSwap();
    }
}
```

**Why it's bad:**

- Too many reasons to change
- Hard to test in isolation
- Unclear responsibility

✅ **Do this instead:**

```csharp
// FileProcessor just orchestrates
public class FileProcessor
{
    public void ProcessOnce(string path, FileState state, WorkerStatsBuffer stats)
    {
        _tailer.ReadAppended(path, ref state.Offset, chunk =>
        {
            _scanner.Scan(chunk, ref state.Carry, line =>
            {
                if (_parser.TryParse(line, out var parsed))
                {
                    stats.IncrementMessage(parsed.MessageKey);
                    stats.RecordLatency(parsed.LatencyMs ?? 0);
                }
            });
        }, out _);
    }
}
```

**Why it's good:**

- FileProcessor orchestrates, doesn't own
- Each substage (tailer, scanner, parser) has one job
- Worker coordination happens in ProcessingCoordinator, not here
- Easy to test each piece independently

---

### **Good Pattern 1: Dependency Injection**

✅ **Do this:**

```csharp
public class ProcessingCoordinator
{
    private readonly BoundedEventBus<FsEvent> _bus;
    private readonly FileStateRegistry _registry;
    private readonly FileProcessor _processor;
    
    public ProcessingCoordinator(
        BoundedEventBus<FsEvent> bus,
        FileStateRegistry registry,
        FileProcessor processor)
    {
        _bus = bus;
        _registry = registry;
        _processor = processor;
    }
}
```

**Why it's good:**

- Dependencies are explicit
- Constructor is a contract
- Easy to mock in tests
- Easy to swap implementations

---

### **Good Pattern 2: Callback-Driven Processing**

✅ **Do this:**

```csharp
public void ReadAppended(string path, ref long offset, Action<ReadOnlySpan<byte>> onChunk)
{
    // Read chunks
    while ((bytesRead = fs.Read(buffer, 0, chunkSize)) > 0)
    {
        onChunk(new ReadOnlySpan<byte>(buffer, 0, bytesRead));
    }
}
```

**Why it's good:**

- No internal buffering; caller decides what to do
- Memory efficient (64KB buffer, not whole file)
- Composes well (scanner chains to parser)

---

### **Good Pattern 3: Immutable & Readonly**

✅ **Do this:**

```csharp
public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

public readonly ref struct ParsedLogLine
{
    public DateTimeOffset Timestamp { get; }
    public LogLevel Level { get; }
}
```

**Why it's good:**

- No mutation = no race conditions
- Clear intent
- Safe to share across threads

---

### **Good Pattern 4: Unidirectional Dependencies**

✅ **Do this:**

```
Ingestion → Events → FileManagement ↘
                                      Processing ↘
                                                   Statistics ↘
                                                                Coordination ↘
                                                                              Reporting
```

**Why it's good:**

- Easy to trace data flow
- No circular dependencies
- Safe to test in isolation (mock downstream)
- Clear ownership hierarchy

---

## **When You're Confused: Decision Tree**

**"Which domain should this code go in?"**

1. Does it call FileSystemWatcher? → Ingestion
2. Does it manage a queue? → Events
3. Does it track per-file state? → FileManagement
4. Does it read files? → Processing.Tailing
5. Does it split bytes on delimiters? → Processing.Scanning
6. Does it parse bytes into records? → Processing.Parsing
7. Does it orchestrate tailer+scanner+parser? → Processing.FileProcessor
8. Does it route events to files? → Processing.ProcessingCoordinator
9. Does it accumulate counters/histograms? → Statistics
10. Does it swap buffers? → Coordination
11. Does it merge and print? → Reporting
12. Does it parse arguments? → LogWatcher.App

---

## **Key Rules**

1. **Each domain has one reason to change.** If you're thinking of two independent reasons, split it.

2. **Dependencies flow downward only.** Never call upstream from downstream.

3. **Test in isolation.** Mock dependencies; don't require the whole system.

4. **Pass dependencies, don't hide them.** Inject via constructor, not static fields.

5. **Immutable beats mutable.** Readonly fields, constants, ref structs.

6. **Make it explicit.** Hidden state and hidden dependencies are the enemy.

7. **One job per domain.** If you can't describe it in one sentence, it's too broad.

---