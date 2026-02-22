# Rule Audit — LogWatcher.Seeder

Rules evaluated against: `.github/instructions/dotnet.instructions.md`

---

## Violations

### BCL — `DateTime.UtcNow` for elapsed time

**File:** `Program.cs`

```csharp
var startTime = DateTime.UtcNow;
// ...
private static void PrintSummary(DateTime startTime)
{
    var elapsed = DateTime.UtcNow - startTime;
```

`DateTime.UtcNow` is not monotonic; elapsed-time calculations should use `Stopwatch`.

Rule: *"NEVER use `DateTime.UtcNow` for elapsed time or deadline calculations — not monotonic, drifts under NTP. Use `Stopwatch.GetTimestamp()` or `Environment.TickCount64`."*  
Prohibited by Default table: *"`DateTime.UtcNow` for elapsed time | Non-monotonic time for deadlines | `Stopwatch` / `Environment.TickCount64`"*

---

### Error Handling — Exceptions thrown for expected validation failures

**File:** `SeedConfig.cs`

```csharp
public void Validate()
{
    if (string.IsNullOrWhiteSpace(TempPath)) throw new ArgumentException("TempPath is required");
    if (!Directory.Exists(TempPath))         throw new DirectoryNotFoundException($"{TempPath} does not exist");
    if (!EnableTxt && !EnableLog)            throw new ArgumentException("At least one of EnableTxt or EnableLog must be true");
    if (FileNameMin < 0)                     throw new ArgumentOutOfRangeException(nameof(FileNameMin));
    // ...
}
```

Configuration validation failures are expected outcomes, not exceptional conditions. The rule requires using a `Result<T>`-style return instead of exceptions for such cases.

Rule: *"NEVER throw exceptions for expected business outcomes (not-found, validation failure, business rule violation). NEVER return `null` to signal a missing result."*

---

### BCL — `Thread.Sleep` in background worker loop instead of `PeriodicTimer`

**File:** `Program.cs`

```csharp
private static void WorkerLoop(int workerId, SeedConfig config, string tempPath, CancellationToken ct)
{
    // ...
    while (!ct.IsCancellationRequested)
    {
        // ... do work ...
        if (config.DelayMsBetweenIterations > 0)
        {
            Thread.Sleep(config.DelayMsBetweenIterations);
        }
    }
}
```

The worker loop uses `Thread.Sleep` for pacing rather than an async-friendly mechanism. Worker tasks are dispatched via `Task.Run`, so blocking thread-pool threads with `Thread.Sleep` wastes thread-pool capacity.

Rule (BCL table): *"Periodic background work | `Thread` + `Thread.Sleep` loop | `PeriodicTimer`"*

---

### Language — Manual collection initialization → collection expressions

**File:** `Program.cs`

```csharp
private static readonly string[] Levels   = new[] { "INFO", "WARN", "ERROR", "DEBUG" };
private static readonly string[] Messages = new[]
{
    "Processed request",
    "Handled connection",
    "Completed job",
    "Timeout occurred",
    "User action",
    "Background task"
};
```

Additionally, inside `WorkerLoop`:

```csharp
var extChoices = new System.Collections.Generic.List<string>();
```

(The `List<string>` is manually initialized via `.Add` when a collection expression with a conditional spread would be more idiomatic.)

Rule: *"Manual collection initialization → collection expressions"*  
Should be: `private static readonly string[] Levels = ["INFO", "WARN", "ERROR", "DEBUG"];`
